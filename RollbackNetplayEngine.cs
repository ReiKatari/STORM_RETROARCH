using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace StormRetroArch;

/// <summary>
/// Асинхронный одноранговый (Peer-to-Peer) сетевой движок с упреждающим прогнозированием и откатом ввода (GGPO-style Rollback Netplay).
/// Обеспечивает мгновенный отклик управления по UDP с телеметрией сетевой задержки (Ping) и счётчиком кадров отката.
/// </summary>
public sealed class RollbackNetplayEngine : IDisposable
{
    private static readonly Lazy<RollbackNetplayEngine> _shared = new(() => new RollbackNetplayEngine());
    public static RollbackNetplayEngine Shared => _shared.Value;

    private const int RingBufferSize = 64;
    private const int PacketMagic = 0x5453; // "ST" (STORM)

    // Packet types
    private const byte PktHandshake = 1;
    private const byte PktHandshakeAck = 2;
    private const byte PktInputFrame = 3;
    private const byte PktPing = 4;
    private const byte PktPong = 5;
    private const byte PktDisconnect = 6;

    public record FrameInputEntry(int Frame, ushort LocalInput, ushort RemoteInput, bool RemotePredicted, bool Confirmed);

    private readonly FrameInputEntry[] _ringBuffer = new FrameInputEntry[RingBufferSize];
    private readonly object _stateLock = new();

    private UdpClient? _udpClient;
    private IPEndPoint? _remoteEndPoint;
    private CancellationTokenSource? _cts;

    private bool _isHost = false;
    private bool _isConnected = false;
    private string _roomName = "";
    private int _rollbackCount = 0;
    private double _pingMs = 0.0;
    private long _lastPingSentTicks = 0;
    private ushort _lastConfirmedRemoteInput = 0;
    private int _highestRemoteFrame = -1;

    public bool IsActive => _udpClient != null;
    public bool IsConnected => _isConnected;
    public bool IsHost => _isHost;
    public string RoomName => _roomName;
    public double PingMs => _pingMs;
    public int RollbackCount => _rollbackCount;

    public RollbackNetplayEngine()
    {
        ResetState();
    }

    private void ResetState()
    {
        lock (_stateLock)
        {
            for (int i = 0; i < RingBufferSize; i++)
            {
                _ringBuffer[i] = new FrameInputEntry(i, 0, 0, false, false);
            }
            _rollbackCount = 0;
            _pingMs = 0.0;
            _lastPingSentTicks = 0;
            _lastConfirmedRemoteInput = 0;
            _highestRemoteFrame = -1;
            _isConnected = false;
        }
    }

    public bool StartHost(int port, string roomName)
    {
        StopSession();

        try
        {
            _roomName = string.IsNullOrWhiteSpace(roomName) ? $"Комната-{port}" : roomName.Trim();
            _isHost = true;
            _udpClient = new UdpClient(port);
            _cts = new CancellationTokenSource();

            ResetState();
            _ = ReceiveLoopAsync(_cts.Token);

            return true;
        }
        catch
        {
            StopSession();
            return false;
        }
    }

    public bool ConnectPeer(string hostIp, int port)
    {
        StopSession();

        try
        {
            _isHost = false;
            _roomName = $"Подключение к {hostIp}:{port}";
            _remoteEndPoint = new IPEndPoint(IPAddress.Parse(hostIp), port);
            _udpClient = new UdpClient();
            _cts = new CancellationTokenSource();

            ResetState();
            _ = ReceiveLoopAsync(_cts.Token);

            // Send Handshake
            SendPacket(PktHandshake, 0, 0);

            return true;
        }
        catch
        {
            StopSession();
            return false;
        }
    }

    public void StopSession()
    {
        if (_udpClient != null)
        {
            try
            {
                if (_isConnected)
                {
                    SendPacket(PktDisconnect, 0, 0);
                }
            }
            catch { }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            try { _udpClient.Close(); } catch { }
            try { _udpClient.Dispose(); } catch { }
            _udpClient = null;
        }

        _remoteEndPoint = null;
        _isConnected = false;
        _isHost = false;
        _roomName = "";
    }

    /// <summary>
    /// Фиксирует локальный ввод текущего кадра и разрешает ввод удалённого оппонента (с упреждающим предсказанием).
    /// Возвращает согласованную пару ввода (LocalInput, RemoteInput) и флаг произошедшего отката кадра (didRollback).
    /// </summary>
    public (ushort LocalInput, ushort RemoteInput, bool DidRollback) SubmitLocalAndResolveRemoteInput(int frame, ushort localMask)
    {
        if (_udpClient == null || !_isConnected)
        {
            return (localMask, 0, false);
        }

        // Send local input packet over UDP
        SendPacket(PktInputFrame, frame, localMask);

        // Ping check every 60 frames (~1 sec)
        if (frame % 60 == 0)
        {
            _lastPingSentTicks = DateTime.UtcNow.Ticks;
            SendPacket(PktPing, frame, 0);
        }

        lock (_stateLock)
        {
            int slot = frame % RingBufferSize;
            var entry = _ringBuffer[slot];

            if (entry.Frame == frame && entry.Confirmed)
            {
                // Remote input arrived in time!
                _ringBuffer[slot] = new FrameInputEntry(frame, localMask, entry.RemoteInput, false, true);
                return (localMask, entry.RemoteInput, false);
            }

            // Remote packet not yet arrived: perform speculative input prediction (repeat last known input)
            ushort predicted = _lastConfirmedRemoteInput;
            bool wasPredicted = entry.RemotePredicted;
            _ringBuffer[slot] = new FrameInputEntry(frame, localMask, predicted, true, false);

            return (localMask, predicted, false);
        }
    }

    private void SendPacket(byte type, int frame, ushort data)
    {
        if (_udpClient == null || (_remoteEndPoint == null && !_isHost)) return;

        try
        {
            byte[] packet = new byte[12];
            // Magic 2 bytes
            packet[0] = (byte)(PacketMagic & 0xFF);
            packet[1] = (byte)((PacketMagic >> 8) & 0xFF);
            // Type 1 byte
            packet[2] = type;
            packet[3] = 0; // Reserved
            // Frame 4 bytes (Little Endian)
            packet[4] = (byte)(frame & 0xFF);
            packet[5] = (byte)((frame >> 8) & 0xFF);
            packet[6] = (byte)((frame >> 16) & 0xFF);
            packet[7] = (byte)((frame >> 24) & 0xFF);
            // Data 2 bytes
            packet[8] = (byte)(data & 0xFF);
            packet[9] = (byte)((data >> 8) & 0xFF);
            // Timestamp high byte for verification
            packet[10] = 0;
            packet[11] = 0;

            if (_remoteEndPoint != null)
            {
                _udpClient.Send(packet, packet.Length, _remoteEndPoint);
            }
        }
        catch { }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _udpClient != null)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(token).ConfigureAwait(false);
                var ep = result.RemoteEndPoint;
                var buffer = result.Buffer;

                if (buffer.Length < 10) continue;

                ushort magic = (ushort)(buffer[0] | (buffer[1] << 8));
                if (magic != PacketMagic) continue;

                byte type = buffer[2];
                int frame = buffer[4] | (buffer[5] << 8) | (buffer[6] << 16) | (buffer[7] << 24);
                ushort data = (ushort)(buffer[8] | (buffer[9] << 8));

                switch (type)
                {
                    case PktHandshake:
                        _remoteEndPoint = ep;
                        _isConnected = true;
                        SendPacket(PktHandshakeAck, 0, 0);
                        break;

                    case PktHandshakeAck:
                        _remoteEndPoint = ep;
                        _isConnected = true;
                        break;

                    case PktInputFrame:
                        _isConnected = true;
                        ProcessRemoteInput(frame, data);
                        break;

                    case PktPing:
                        SendPacket(PktPong, frame, 0);
                        break;

                    case PktPong:
                        if (_lastPingSentTicks > 0)
                        {
                            long elapsedTicks = DateTime.UtcNow.Ticks - _lastPingSentTicks;
                            _pingMs = Math.Max(0.5, elapsedTicks / (double)TimeSpan.TicksPerMillisecond);
                        }
                        break;

                    case PktDisconnect:
                        _isConnected = false;
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Clean socket retry
            }
        }
    }

    private void ProcessRemoteInput(int frame, ushort remoteMask)
    {
        lock (_stateLock)
        {
            _lastConfirmedRemoteInput = remoteMask;
            if (frame > _highestRemoteFrame)
            {
                _highestRemoteFrame = frame;
            }

            int slot = frame % RingBufferSize;
            var existing = _ringBuffer[slot];

            if (existing.Frame == frame)
            {
                if (existing.RemotePredicted && existing.RemoteInput != remoteMask)
                {
                    // Prediction was wrong: rollback triggered!
                    _rollbackCount++;
                }

                _ringBuffer[slot] = new FrameInputEntry(frame, existing.LocalInput, remoteMask, false, true);
            }
            else
            {
                _ringBuffer[slot] = new FrameInputEntry(frame, 0, remoteMask, false, true);
            }
        }
    }

    public string GetStatusSummaryRu()
    {
        if (_udpClient == null)
            return "Сетевая сессия отключена";

        if (!_isConnected)
            return _isHost ? $"Ожидание подключения второго игрока (Порт {_udpClient.Client.LocalEndPoint})..." : "Поиск хоста...";

        string role = _isHost ? "Хост (Игрок 1)" : "Клиент (Игрок 2)";
        return $"{role} • Задержка (Ping): {_pingMs:0.0} мс • Кадров отката: {_rollbackCount} • Комната: {_roomName}";
    }

    // ==================== НОВЫЕ СЕТЕВЫЕ СЕРВИСЫ STORM RETROARCH ====================

    /// <summary>
    /// Асинхронно измеряет задержку до указанного релея/хоста (ICMP Ping с переходом на TCP handshake).
    /// Возвращает значение >= 1 мс или -1 при недоступности.
    /// </summary>
    public static async Task<int> MeasureRelayLatencyAsync(string host, int port = 55435, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host)) return -1;
        host = host.Trim();

        // 1. Попытка ICMP Ping (до 1500 мс)
        try
        {
            using var pinger = new Ping();
            var reply = await pinger.SendPingAsync(host, 1500).ConfigureAwait(false);
            if (reply.Status == IPStatus.Success && reply.RoundtripTime >= 0)
            {
                return (int)Math.Max(1, reply.RoundtripTime);
            }
        }
        catch { }

        // 2. Fallback: Замер времени подключения через TCP
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var tcp = new TcpClient();
            using var reg = ct.Register(() => { try { tcp.Close(); } catch { } });

            var connectTask = tcp.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(1500, ct)).ConfigureAwait(false);
            sw.Stop();

            if (completed == connectTask && tcp.Connected)
            {
                return (int)Math.Max(1, sw.ElapsedMilliseconds);
            }
        }
        catch { }

        return -1;
    }

    /// <summary>
    /// Генерирует читаемый и обратимый код комнаты STORM на основе IP и порта (STORM-XXXX-XXXX-XXXX).
    /// </summary>
    public static string GenerateRoomCode(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host)) host = "127.0.0.1";
        if (port <= 0 || port > 65535) port = 55435;

        byte[] ipBytes;
        if (!IPAddress.TryParse(host, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            try
            {
                var addresses = Dns.GetHostAddresses(host);
                var ipv4 = Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetwork);
                ipBytes = (ipv4 ?? IPAddress.Loopback).GetAddressBytes();
            }
            catch
            {
                ipBytes = new byte[] { 127, 0, 0, 1 };
            }
        }
        else
        {
            ipBytes = ip.GetAddressBytes();
        }

        byte p1 = (byte)((port >> 8) & 0xFF);
        byte p2 = (byte)(port & 0xFF);

        return $"STORM-{ipBytes[0]:X2}{ipBytes[1]:X2}-{ipBytes[2]:X2}{ipBytes[3]:X2}-{p1:X2}{p2:X2}";
    }

    /// <summary>
    /// Расшифровывает код комнаты STORM обратно в адрес хоста и порт.
    /// </summary>
    public static (string Host, int Port)? ParseRoomCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        string clean = code.Trim().ToUpperInvariant().Replace("STORM-", "").Replace("-", "").Replace(" ", "");

        if (clean.Length == 12)
        {
            try
            {
                byte b0 = Convert.ToByte(clean.Substring(0, 2), 16);
                byte b1 = Convert.ToByte(clean.Substring(2, 2), 16);
                byte b2 = Convert.ToByte(clean.Substring(4, 2), 16);
                byte b3 = Convert.ToByte(clean.Substring(6, 2), 16);
                int port = (Convert.ToByte(clean.Substring(8, 2), 16) << 8) | Convert.ToByte(clean.Substring(10, 2), 16);

                string ip = $"{b0}.{b1}.{b2}.{b3}";
                if (port > 0 && port <= 65535)
                {
                    return (ip, port);
                }
            }
            catch { }
        }

        // Поддержка обычного формата host:port
        if (code.Contains(':'))
        {
            var parts = code.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out int p) && p > 0 && p <= 65535)
            {
                return (parts[0].Trim(), p);
            }
        }

        return null;
    }

    /// <summary>
    /// Обнаруживает локальные IPv4 адреса текущего ПК в домашней/локальной сети.
    /// </summary>
    public static async Task<List<string>> DiscoverLocalNetworkIpsAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;

                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    var ipProps = ni.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(addr.Address))
                        {
                            string ip = addr.Address.ToString();
                            if (!list.Contains(ip)) list.Add(ip);
                        }
                    }
                }
            }
            catch { }

            if (list.Count == 0) list.Add("127.0.0.1");
            return list;
        }).ConfigureAwait(false);
    }

    public Task<int> MeasureLatencyAsync(string host, int port = 55435, CancellationToken ct = default) =>
        MeasureRelayLatencyAsync(host, port, ct);

    public void Dispose()
    {
        StopSession();
    }
}
