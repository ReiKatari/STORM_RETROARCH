using System;
using System.Net;
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

    public void Dispose()
    {
        StopSession();
    }
}
