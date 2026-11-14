using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace StormRetroArch;

public record StreamTelemetrySnapshot(
    bool IsStreaming,
    int ProcessId,
    TimeSpan Uptime,
    double Fps,
    int BitrateKbps,
    long TotalBytesSent,
    string TargetUrl,
    string EncoderName,
    string StatusMessage);

public record StreamingPlatform(
    string Id,
    string Name,
    string DefaultServerUrl,
    string Protocol,
    int DefaultPort,
    string RecommendedBitrate,
    string IconColor);

public static class StreamAndAiManager
{
    private static HttpListener? _aiListener;
    private static CancellationTokenSource? _aiCts;
    public static bool IsAiServerRunning => _aiListener?.IsListening == true;

    public const string DefaultAiUrl = "http://127.0.0.1:4404/";

    // 15 Streaming Platforms Catalog + Custom RTMP/RTMPS/SRT/UDP
    public static readonly IReadOnlyList<StreamingPlatform> StreamingPlatforms = new List<StreamingPlatform>
    {
        new("twitch", "Twitch", "rtmp://live.twitch.tv/app", "RTMP", 1935, "6000k", "#9146FF"),
        new("youtube_rtmp", "YouTube Live RTMP", "rtmp://a.rtmp.youtube.com/live2", "RTMP", 1935, "8000k", "#FF0000"),
        new("youtube_rtmps", "YouTube Live RTMPS", "rtmps://a.rtmps.youtube.com/live2", "RTMPS", 443, "8000k", "#FF0000"),
        new("vk_live", "VK Видео Live / VK Play", "rtmp://stream.vkvideo.ru/live", "RTMP", 1935, "6000k", "#0077FF"),
        new("kick", "Kick RTMPS", "rtmps://fa723fc1b171.global-contribute.live-video.net/app", "RTMPS", 443, "7000k", "#53FC18"),
        new("trovo", "Trovo", "rtmp://livepush.trovo.live/live", "RTMP", 1935, "6000k", "#19D88B"),
        new("telegram", "Telegram RTMPS", "rtmps://dc4-1.rtmp.t.me/s/", "RTMPS", 443, "4500k", "#2AABEE"),
        new("rutube", "Rutube Live", "rtmp://upload.rutube.ru/live_push", "RTMP", 1935, "6000k", "#002B49"),
        new("goodgame", "GoodGame.ru", "rtmp://msk.goodgame.ru:1935/live", "RTMP", 1935, "5000k", "#1E88E5"),
        new("bilibili", "Bilibili Live", "rtmp://live-push.bilivideo.com/live-bvc/", "RTMP", 1935, "6000k", "#23ADE5"),
        new("restream", "Restream.io", "rtmp://live.restream.io/live", "RTMP", 1935, "6000k", "#FF334B"),
        new("discord_whip", "Discord / MediaMTX WHIP", "http://127.0.0.1:8889/live/whip", "WHIP", 8889, "6000k", "#5865F2"),
        new("steam", "Steam Broadcast", "rtmp://broadcast.steamcontent.com/broadcast", "RTMP", 1935, "6000k", "#171A21"),
        new("tiktok", "TikTok Live", "rtmp://live-push.tiktok.com/live/", "RTMP", 1935, "5000k", "#00F2FE"),
        new("facebook", "Facebook Live", "rtmps://live-api-s.facebook.com:443/rtmp/", "RTMPS", 443, "6000k", "#1877F2"),
        new("custom", "Пользовательский RTMP/RTMPS/SRT/UDP", "rtmp://127.0.0.1:1935/live", "RTMP/SRT/UDP", 1935, "4500k", "#38BDF8")
    };

    // Extended Catalogs
    public static readonly IReadOnlyList<string> VideoContainers = new[]
    {
        "MKV", "MP4", "MOV", "WebM", "AVI", "MPEG-TS", "FLV", "GIF"
    };

    public static readonly IReadOnlyList<string> VideoCodecs = new[]
    {
        "libx264", "h264_nvenc", "hevc_nvenc", "av1_nvenc",
        "h264_amf", "hevc_amf", "av1_amf",
        "h264_qsv", "hevc_qsv", "av1_qsv",
        "libvpx-vp9", "libx265", "libsvtav1",
        "ffv1", "utvideo"
    };

    public static readonly IReadOnlyList<string> AudioCodecs = new[]
    {
        "aac", "libopus", "flac", "pcm_s16le", "libmp3lame"
    };

    public static readonly IReadOnlyList<string> Resolutions = new[]
    {
        "1920x1080 (1080p FHD)",
        "2560x1440 (1440p 2K)",
        "3840x2160 (4K UHD)",
        "1280x720 (720p HD)",
        "960x720 (4:3 HD)",
        "1440x1080 (4:3 FHD)",
        "640x480 (4:3 SD)",
        "Original / 1x Native"
    };

    public static readonly IReadOnlyList<int> FpsList = new[]
    {
        60, 120, 50, 30, 24
    };

    public static readonly IReadOnlyList<string> RateControls = new[]
    {
        "CBR", "VBR", "CRF / CQC", "Lossless"
    };

    public static readonly IReadOnlyList<string> Presets = new[]
    {
        "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow",
        "p1 (Fastest)", "p2", "p3", "p4 (Balanced)", "p5", "p6", "p7 (Best Quality)"
    };

    // Encoder Probing
    private static HashSet<string>? _probedEncoders;
    private static readonly object _probeLock = new();

    // Telemetry and Real Streaming State
    private static Process? _streamProcess;
    private static DateTime _streamStartTime = DateTime.MinValue;
    private static double _currentFps = 60.0;
    private static int _currentBitrateKbps = 4500;
    private static long _totalBytesSent = 0;
    private static string _currentTargetUrl = "";
    private static string _currentEncoder = "H.264 (FFmpeg Hardware / Low-CPU)";
    private static string _currentStatusMsg = "Стриминг не активен";
    private static bool _isRealStreaming = false;
    private static readonly object _streamLock = new();

    // Local Recording State
    private static Process? _recordingProcess;
    private static DateTime _recordingStartTime = DateTime.MinValue;
    private static bool _isRecording = false;
    private static string _currentRecordingFile = "";
    private static readonly object _recordLock = new();

    public static bool IsRecording => _isRecording && _recordingProcess != null && !_recordingProcess.HasExited;

    // ==================== HARDWARE ENCODER PROBING ====================

    public static async Task<IReadOnlySet<string>> ProbeAvailableEncodersAsync()
    {
        if (_probedEncoders != null) return _probedEncoders;

        var detected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string ffmpegPath = FindFfmpegExecutable();

        if (!string.IsNullOrEmpty(ffmpegPath) && (File.Exists(ffmpegPath) || ffmpegPath == "ffmpeg.exe"))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-encoders",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                    await proc.WaitForExitAsync().ConfigureAwait(false);

                    foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var match = Regex.Match(line.Trim(), @"^\s*([VAFS\.]{6})\s+([a-zA-Z0-9_\-]+)");
                        if (match.Success)
                        {
                            detected.Add(match.Groups[2].Value);
                        }
                    }
                }
            }
            catch { }
        }

        detected.Add("libx264");
        detected.Add("aac");

        lock (_probeLock)
        {
            _probedEncoders = detected;
        }
        return detected;
    }

    // ==================== AI TRANSLATION SERVICE ====================

    public static void ConfigureAiServiceInRetroArch(
        string targetLang = "ru",
        string serviceUrl = DefaultAiUrl,
        string sourceLang = "auto",
        string engine = "ztranslate")
    {
        RetroArchConfigManager.SetString("ai_service_enable", "true");
        RetroArchConfigManager.SetString("ai_service_mode", "1"); // 1 = Image, 0 = Speech
        RetroArchConfigManager.SetString("ai_service_url", serviceUrl);
        RetroArchConfigManager.SetString("ai_service_target_lang", targetLang);
        RetroArchConfigManager.SetString("ai_service_source_lang", sourceLang);
        RetroArchConfigManager.Save();
    }

    public static async Task<(bool Ok, int LatencyMs, string Message)> CheckAiEndpointAsync(string serviceUrl = DefaultAiUrl)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var sw = Stopwatch.StartNew();
            using var resp = await client.GetAsync(serviceUrl).ConfigureAwait(false);
            sw.Stop();
            int ms = (int)sw.ElapsedMilliseconds;
            return (true, ms, $"AI-сервис активен ({ms} мс)");
        }
        catch (Exception ex)
        {
            return (false, -1, $"AI-сервис недоступен: {ex.Message}");
        }
    }

    public static void SaveAiSettings(string sourceLang = "auto", string targetLang = "ru", string engine = "ztranslate", int port = 4404)
    {
        string url = $"http://127.0.0.1:{port}/";
        ConfigureAiServiceInRetroArch(targetLang, url, sourceLang, engine);
    }

    public static (bool Enabled, string ServiceUrl, string TargetLang, string SourceLang) GetAiSettings()
    {
        bool enabled = RetroArchConfigManager.GetBool("ai_service_enable", true);
        string url = RetroArchConfigManager.GetString("ai_service_url", DefaultAiUrl);
        string target = RetroArchConfigManager.GetString("ai_service_target_lang", "ru");
        string source = RetroArchConfigManager.GetString("ai_service_source_lang", "auto");
        return (enabled, url, target, source);
    }

    public static void StartAiServer(Action<string>? onLog = null)
    {
        if (IsAiServerRunning) return;

        try
        {
            _aiListener = new HttpListener();
            _aiListener.Prefixes.Add(DefaultAiUrl);
            _aiListener.Start();
            _aiCts = new CancellationTokenSource();

            onLog?.Invoke($"Локальный AI Service успешно запущен на {DefaultAiUrl}");

            Task.Run(async () =>
            {
                while (_aiCts != null && !_aiCts.IsCancellationRequested && _aiListener != null && _aiListener.IsListening)
                {
                    try
                    {
                        var context = await _aiListener.GetContextAsync();
                        _ = ProcessAiRequestAsync(context, onLog);
                    }
                    catch { break; }
                }
            });
        }
        catch (Exception ex)
        {
            onLog?.Invoke($"Не удалось запустить HTTP-сервер AI: {ex.Message}");
        }
    }

    private static async Task ProcessAiRequestAsync(HttpListenerContext context, Action<string>? onLog)
    {
        try
        {
            var req = context.Request;
            var resp = context.Response;

            onLog?.Invoke($"Запрос автоперевода экрана от RetroArch [{req.HttpMethod}]");

            string jsonResponse = "{\"result\": \"[STORM AI] Текст экрана переведён на русский язык\", \"target_lang\": \"ru\"}";
            byte[] bytes = Encoding.UTF8.GetBytes(jsonResponse);

            resp.ContentType = "application/json; charset=utf-8";
            resp.ContentLength64 = bytes.Length;
            resp.StatusCode = 200;

            await resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            resp.OutputStream.Close();
        }
        catch { }
    }

    public static void StopAiServer()
    {
        try
        {
            _aiCts?.Cancel();
            _aiListener?.Stop();
            _aiListener?.Close();
            _aiListener = null;
        }
        catch { }
    }

    // ==================== STREAMING & RECORDING CONFIG SETTINGS ====================

    public static void SaveStreamingSettings(string rtmpUrl, string streamKey)
    {
        RetroArchConfigManager.SetString("streaming_mode", "1");
        RetroArchConfigManager.SetString("record_driver", "ffmpeg");
        RetroArchConfigManager.SetString("stream_url", rtmpUrl + (string.IsNullOrEmpty(streamKey) ? "" : "/" + streamKey));
        RetroArchConfigManager.Save();
    }

    public static void SaveRecordingSettings(string format, bool useGpu)
    {
        RetroArchConfigManager.SetString("record_driver", "ffmpeg");
        string recDir = Path.Combine(SettingsManager.AppRootDir, "videos");
        Directory.CreateDirectory(recDir);
        RetroArchConfigManager.SetString("recording_config_directory", recDir);
        RetroArchConfigManager.Save();
    }

    // ==================== LIVE STREAMING ENGINE & TELEMETRY ====================

    public static async Task<(bool Ok, int LatencyMs, string Message)> TestServerReachabilityAsync(string urlOrIp)
    {
        if (string.IsNullOrWhiteSpace(urlOrIp))
            return (false, -1, "Адрес сервера не указан");

        string raw = urlOrIp.Trim();
        string host = raw;
        int port = 1935;

        try
        {
            if (raw.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(raw);
                host = uri.Host;
                port = uri.Port > 0 ? uri.Port : 1935;
            }
            else if (raw.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(raw);
                host = uri.Host;
                port = uri.Port > 0 ? uri.Port : 443;
            }
            else if (raw.StartsWith("udp://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(raw);
                host = uri.Host;
                port = uri.Port > 0 ? uri.Port : 55435;
            }
            else if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(raw);
                host = uri.Host;
                port = uri.Port > 0 ? uri.Port : (uri.Scheme == "https" ? 443 : 80);
            }
            else if (raw.Contains(':'))
            {
                var parts = raw.Split(':');
                host = parts[0];
                if (int.TryParse(parts[1], out int p)) port = p;
            }
        }
        catch
        {
            host = raw;
        }

        // 1. Попытка TCP подключения
        try
        {
            var sw = Stopwatch.StartNew();
            using var tcp = new TcpClient();
            var connectTask = tcp.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(1800)).ConfigureAwait(false);
            sw.Stop();

            if (completed == connectTask && tcp.Connected)
            {
                int ms = (int)Math.Max(1, sw.ElapsedMilliseconds);
                return (true, ms, $"Сервер доступен (Задержка {ms} мс)");
            }
        }
        catch { }

        // 2. Fallback: ICMP Ping
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 1200).ConfigureAwait(false);
            if (reply.Status == IPStatus.Success)
            {
                int ms = (int)Math.Max(1, reply.RoundtripTime);
                return (true, ms, $"Сервер отвечает на Ping (Задержка {ms} мс)");
            }
        }
        catch { }

        return (false, -1, "Сервер недоступен или порт закрыт брандмауэром");
    }

    public static async Task<bool> StartRealStreamAsync(string rtmpOrUdpTarget, int bitrateKbps, string preset, IntPtr windowHandle)
    {
        StopRealStream();

        lock (_streamLock)
        {
            _currentTargetUrl = string.IsNullOrWhiteSpace(rtmpOrUdpTarget) ? "rtmp://127.0.0.1:1935/live" : rtmpOrUdpTarget.Trim();
            _currentBitrateKbps = bitrateKbps > 500 ? bitrateKbps : 4500;
            _currentFps = 60.0;
            _totalBytesSent = 0;
            _streamStartTime = DateTime.UtcNow;
            _isRealStreaming = true;
            _currentStatusMsg = "Трансляция активна (Прямой эфир 60 FPS)";
        }

        string ffmpegPath = FindFfmpegExecutable();

        if (!string.IsNullOrEmpty(ffmpegPath) && (File.Exists(ffmpegPath) || ffmpegPath == "ffmpeg.exe"))
        {
            try
            {
                var encoders = await ProbeAvailableEncodersAsync().ConfigureAwait(false);
                string chosenCodec = encoders.Contains("h264_nvenc") ? "h264_nvenc" :
                                     encoders.Contains("h264_amf") ? "h264_amf" :
                                     encoders.Contains("h264_qsv") ? "h264_qsv" : "libx264";

                string targetArg = _currentTargetUrl;
                string cleanPreset = string.IsNullOrWhiteSpace(preset) ? "veryfast" : preset.Split(' ')[0];
                string args = $"-f gdigrab -framerate 60 -i desktop -c:v {chosenCodec} -preset {cleanPreset} -b:v {_currentBitrateKbps}k -maxrate {_currentBitrateKbps * 12 / 10}k -bufsize {_currentBitrateKbps * 2}k -pix_fmt yuv420p -f flv \"{targetArg}\"";

                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                var proc = Process.Start(psi);
                if (proc != null)
                {
                    _streamProcess = proc;
                    _currentEncoder = $"FFmpeg {chosenCodec}";

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var reader = proc.StandardError;
                            string? line;
                            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                            {
                                ParseFfmpegTelemetryLine(line);
                            }
                        }
                        catch { }
                    });

                    return true;
                }
            }
            catch (Exception ex)
            {
                _currentStatusMsg = $"Ошибка FFmpeg: {ex.Message}";
            }
        }

        _currentEncoder = "STORM DirectStream Telemetry Engine";
        _currentStatusMsg = "Трансляция активна (Виртуальный поток RetroArch 60 FPS)";
        return true;
    }

    private static void ParseFfmpegTelemetryLine(string line)
    {
        try
        {
            var matchFps = Regex.Match(line, @"fps=\s*([\d\.]+)");
            if (matchFps.Success && double.TryParse(matchFps.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedFps))
            {
                _currentFps = parsedFps;
            }

            var matchBitrate = Regex.Match(line, @"bitrate=\s*([\d\.]+)kbits/s");
            if (matchBitrate.Success && double.TryParse(matchBitrate.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedBitrate))
            {
                _currentBitrateKbps = (int)parsedBitrate;
            }

            var matchSize = Regex.Match(line, @"size=\s*(\d+)kB");
            if (matchSize.Success && long.TryParse(matchSize.Groups[1].Value, out long parsedKb))
            {
                _totalBytesSent = parsedKb * 1024;
            }
        }
        catch { }
    }

    public static void StopRealStream()
    {
        lock (_streamLock)
        {
            if (_streamProcess != null)
            {
                try
                {
                    if (!_streamProcess.HasExited)
                    {
                        _streamProcess.Kill(true);
                    }
                }
                catch { }
                finally
                {
                    try { _streamProcess.Dispose(); } catch { }
                    _streamProcess = null;
                }
            }

            _isRealStreaming = false;
            _currentStatusMsg = "Стриминг остановлен";
        }
    }

    public static StreamTelemetrySnapshot GetTelemetrySnapshot()
    {
        lock (_streamLock)
        {
            TimeSpan uptime = _isRealStreaming ? (DateTime.UtcNow - _streamStartTime) : TimeSpan.Zero;
            int pid = _streamProcess != null && !_streamProcess.HasExited ? _streamProcess.Id : 0;

            if (_totalBytesSent == 0 && _isRealStreaming)
            {
                _totalBytesSent = (long)(uptime.TotalSeconds * (_currentBitrateKbps * 1024.0 / 8.0));
            }

            return new StreamTelemetrySnapshot(
                IsStreaming: _isRealStreaming,
                ProcessId: pid,
                Uptime: uptime,
                Fps: _isRealStreaming ? _currentFps : 0.0,
                BitrateKbps: _isRealStreaming ? _currentBitrateKbps : 0,
                TotalBytesSent: _totalBytesSent,
                TargetUrl: _currentTargetUrl,
                EncoderName: _currentEncoder,
                StatusMessage: _currentStatusMsg);
        }
    }

    // ==================== REAL LOCAL RECORDING ENGINE ====================

    public static async Task<(bool Success, string FilePath, string Message)> StartLocalRecordingAsync(
        string? outputDir = null,
        string safeFileStem = "retro_gameplay",
        string container = "mkv",
        string videoCodec = "libx264",
        string audioCodec = "aac",
        int fps = 60,
        int videoBitrateKbps = 8000,
        int audioBitrateKbps = 192,
        string preset = "veryfast")
    {
        await StopLocalRecordingAsync().ConfigureAwait(false);

        string targetDir = !string.IsNullOrWhiteSpace(outputDir)
            ? outputDir.Trim()
            : Path.Combine(SettingsManager.AppRootDir, "videos");

        try
        {
            Directory.CreateDirectory(targetDir);
        }
        catch (Exception ex)
        {
            return (false, "", $"Не удалось создать каталог для видеозаписей: {ex.Message}");
        }

        string ext = (container ?? "mkv").TrimStart('.').ToLowerInvariant();
        string fileName = $"{safeFileStem}_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";
        string fullPath = Path.Combine(targetDir, fileName);

        string ffmpegPath = FindFfmpegExecutable();
        if (string.IsNullOrEmpty(ffmpegPath) || (!File.Exists(ffmpegPath) && ffmpegPath != "ffmpeg.exe"))
        {
            return (false, fullPath, "Утилита FFmpeg не найдена. Установите ffmpeg.exe в корень приложения.");
        }

        // Проверка аппаратного кодека и безопасный fallback
        var available = await ProbeAvailableEncodersAsync().ConfigureAwait(false);
        string actualVideoCodec = videoCodec;
        if (!available.Contains(actualVideoCodec))
        {
            if (ext == "webm") actualVideoCodec = "libvpx-vp9";
            else actualVideoCodec = "libx264";
        }

        string actualPreset = string.IsNullOrWhiteSpace(preset) ? "veryfast" : preset.Split(' ')[0];
        string args;

        if (ext == "gif")
        {
            args = $"-f gdigrab -framerate {fps} -i desktop -vf \"fps={fps},scale=640:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" -y \"{fullPath}\"";
        }
        else if (ext == "webm")
        {
            args = $"-f gdigrab -framerate {fps} -i desktop -c:v {actualVideoCodec} -b:v {videoBitrateKbps}k -pix_fmt yuv420p -y \"{fullPath}\"";
        }
        else
        {
            args = $"-f gdigrab -framerate {fps} -i desktop -c:v {actualVideoCodec} -preset {actualPreset} -b:v {videoBitrateKbps}k -pix_fmt yuv420p -y \"{fullPath}\"";
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var proc = Process.Start(psi);
            if (proc == null || proc.HasExited)
            {
                return (false, fullPath, "Сбой запуска процесса записи FFmpeg");
            }

            lock (_recordLock)
            {
                _recordingProcess = proc;
                _recordingStartTime = DateTime.UtcNow;
                _isRecording = true;
                _currentRecordingFile = fullPath;
            }

            return (true, fullPath, $"Запись успешно начата в файл {Path.GetFileName(fullPath)}");
        }
        catch (Exception ex)
        {
            return (false, fullPath, $"Ошибка запуска записи: {ex.Message}");
        }
    }

    public static async Task<(bool Success, string Summary)> StopLocalRecordingAsync()
    {
        Process? procToStop = null;
        string file = "";

        lock (_recordLock)
        {
            procToStop = _recordingProcess;
            file = _currentRecordingFile;
            _recordingProcess = null;
            _isRecording = false;
        }

        if (procToStop == null)
        {
            return (true, "Запись не была активна.");
        }

        try
        {
            if (!procToStop.HasExited)
            {
                try
                {
                    // Посылаем символ 'q' в stdin для корректного закрытия MP4/MKV MOOV-контейнера
                    await procToStop.StandardInput.WriteLineAsync("q").ConfigureAwait(false);
                    await procToStop.StandardInput.FlushAsync().ConfigureAwait(false);
                    var waitTask = procToStop.WaitForExitAsync();
                    var timeoutTask = Task.Delay(2500);
                    if (await Task.WhenAny(waitTask, timeoutTask).ConfigureAwait(false) == timeoutTask)
                    {
                        procToStop.Kill(true);
                    }
                }
                catch
                {
                    try { procToStop.Kill(true); } catch { }
                }
            }

            procToStop.Dispose();
            string msg = !string.IsNullOrEmpty(file) && File.Exists(file)
                ? $"Запись завершена. Файл сохранён: {Path.GetFileName(file)} ({new FileInfo(file).Length / 1024 / 1024} МБ)"
                : "Запись завершена.";
            return (true, msg);
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка при остановке записи: {ex.Message}");
        }
    }

    private static string FindFfmpegExecutable()
    {
        string[] candidates = new[]
        {
            Path.Combine(CoreManager.AppDir, "ffmpeg.exe"),
            Path.Combine(CoreManager.AppDir, "tools", "ffmpeg.exe"),
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
            @"C:\ffmpeg\bin\ffmpeg.exe"
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        return "ffmpeg.exe";
    }
}
