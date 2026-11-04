using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StormRetroArch;

public static class StreamAndAiManager
{
    private static HttpListener? _aiListener;
    private static CancellationTokenSource? _aiCts;
    public static bool IsAiServerRunning => _aiListener?.IsListening == true;

    public const string DefaultAiUrl = "http://127.0.0.1:4404/";

    public static void ConfigureAiServiceInRetroArch(string targetLang = "ru", string serviceUrl = DefaultAiUrl)
    {
        RetroArchConfigManager.SetString("ai_service_enable", "true");
        RetroArchConfigManager.SetString("ai_service_mode", "1"); // 1 = Image, 0 = Speech
        RetroArchConfigManager.SetString("ai_service_url", serviceUrl);
        RetroArchConfigManager.SetString("ai_service_target_lang", targetLang);
        RetroArchConfigManager.Save();
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

            // Simple mock response or translated text
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

    // ==================== STREAMING & RECORDING ====================

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
        RetroArchConfigManager.SetString("recording_config_directory", Path.Combine(CoreManager.AppDir, "recordings"));
        RetroArchConfigManager.Save();
    }
}
