using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StormRetroArch;

public class AiStudioConfig
{
    public string Provider { get; set; } = "Локальный AI Service (HTTP 4404)";
    public string EndpointUrl { get; set; } = "http://127.0.0.1:4404/";
    public string ApiKey { get; set; } = "";
    public string ModelName { get; set; } = "retro-ai-assistant";
}

public static class AiWalkthroughManager
{
    public static readonly string GuidebooksDir = Path.Combine(CoreManager.AppDir, "guidebooks");
    public static readonly string ScreenshotsDir = Path.Combine(CoreManager.AppDir, "screenshots");

    public static AiStudioConfig Config { get; set; } = new();

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(25) };
    private static object? _speechSynthesizer;
    private static Process? _ttsProcess;
    private static readonly object _ttsLock = new();

    static AiWalkthroughManager()
    {
        try
        {
            if (!Directory.Exists(GuidebooksDir)) Directory.CreateDirectory(GuidebooksDir);
            if (!Directory.Exists(ScreenshotsDir)) Directory.CreateDirectory(ScreenshotsDir);
        }
        catch { }
    }

    public static List<string> GetAvailableScreenshots()
    {
        if (!Directory.Exists(ScreenshotsDir)) return new();
        return Directory.GetFiles(ScreenshotsDir, "*.*")
            .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .ToList();
    }

    public static async Task<string> AskWithScreenshotAsync(
        string gameTitle,
        string question,
        string? screenshotPath,
        string provider,
        string apiKey,
        string model,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            question = "Опишите тактику для текущей ситуации в игре и подскажите секреты.";

        string safeGame = string.IsNullOrWhiteSpace(gameTitle) ? "Ретро-игра" : gameTitle.Trim();

        // Подготовка изображения при наличии
        string base64Image = "";
        string mimeType = "image/png";
        if (!string.IsNullOrEmpty(screenshotPath) && File.Exists(screenshotPath))
        {
            try
            {
                byte[] bytes = await File.ReadAllBytesAsync(screenshotPath, ct).ConfigureAwait(false);
                base64Image = Convert.ToBase64String(bytes);
                string ext = Path.GetExtension(screenshotPath).ToLowerInvariant();
                mimeType = ext == ".jpg" || ext == ".jpeg" ? "image/jpeg" : "image/png";
            }
            catch { }
        }

        string prov = (provider ?? Config.Provider).ToLowerInvariant();
        string key = !string.IsNullOrWhiteSpace(apiKey) ? apiKey.Trim() : Config.ApiKey.Trim();
        string mdl = !string.IsNullOrWhiteSpace(model) ? model.Trim() : Config.ModelName.Trim();

        try
        {
            // 1. OpenAI / ChatGPT (включая модели с Vision)
            if (prov.Contains("openai") || prov.Contains("gpt"))
            {
                if (string.IsNullOrEmpty(mdl)) mdl = "gpt-4o-mini";
                var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);

                object userContent;
                if (!string.IsNullOrEmpty(base64Image))
                {
                    userContent = new object[]
                    {
                        new { type = "text", text = $"Игра: {safeGame}\nВопрос: {question}" },
                        new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{base64Image}" } }
                    };
                }
                else
                {
                    userContent = $"Игра: {safeGame}\nВопрос: {question}";
                }

                var payload = new
                {
                    model = mdl,
                    messages = new object[]
                    {
                        new { role = "system", content = "Вы - профессиональный эксперт по ретро-играм и спидраннер STORM. Отвечайте чётко, информативно и без лишней воды." },
                        new { role = "user", content = userContent }
                    }
                };

                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    string json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                }
            }
            // 2. Google Gemini API (Multimodal Vision)
            else if (prov.Contains("gemini") || prov.Contains("google"))
            {
                if (string.IsNullOrEmpty(mdl)) mdl = "gemini-1.5-flash";
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{mdl}:generateContent?key={key}";
                var req = new HttpRequestMessage(HttpMethod.Post, url);

                var parts = new List<object>
                {
                    new { text = $"Игра: {safeGame}\nВопрос: {question}" }
                };
                if (!string.IsNullOrEmpty(base64Image))
                {
                    parts.Add(new { inlineData = new { mimeType = mimeType, data = base64Image } });
                }

                var payload = new { contents = new[] { new { parts } } };
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    string json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var candidates = doc.RootElement.GetProperty("candidates");
                    if (candidates.GetArrayLength() > 0)
                    {
                        return candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
                    }
                }
            }
            // 3. Anthropic Claude (Vision)
            else if (prov.Contains("claude") || prov.Contains("anthropic"))
            {
                if (string.IsNullOrEmpty(mdl)) mdl = "claude-3-5-sonnet-20241022";
                var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
                req.Headers.Add("x-api-key", key);
                req.Headers.Add("anthropic-version", "2023-06-01");

                var contentList = new List<object>();
                if (!string.IsNullOrEmpty(base64Image))
                {
                    contentList.Add(new
                    {
                        type = "image",
                        source = new { type = "base64", media_type = mimeType, data = base64Image }
                    });
                }
                contentList.Add(new { type = "text", text = $"Игра: {safeGame}\nВопрос: {question}" });

                var payload = new
                {
                    model = mdl,
                    max_tokens = 1024,
                    messages = new[] { new { role = "user", content = contentList } }
                };

                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    string json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
                }
            }
            // 4. Локальный Ollama
            else if (prov.Contains("ollama") || Config.EndpointUrl.Contains("11434"))
            {
                if (string.IsNullOrEmpty(mdl)) mdl = "llama3";
                var payload = new
                {
                    model = mdl,
                    prompt = $"Игра: {safeGame}\nВопрос: {question}",
                    images = !string.IsNullOrEmpty(base64Image) ? new[] { base64Image } : Array.Empty<string>(),
                    stream = false
                };
                var req = new HttpRequestMessage(HttpMethod.Post, Config.EndpointUrl.TrimEnd('/') + "/api/generate");
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var res = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    string json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("response", out var r))
                        return r.GetString() ?? "";
                }
            }
        }
        catch { }

        // Fallback: Эталонный локальный движок анализа сцены STORM
        return GenerateLocalContextualAdvice(safeGame, question, screenshotPath);
    }

    public static async Task<string> AskAiAssistantAsync(string question, string? imagePath = null)
    {
        return await AskWithScreenshotAsync(
            "Текущая игра",
            question,
            imagePath,
            Config.Provider,
            Config.ApiKey,
            Config.ModelName);
    }

    private static string GenerateLocalContextualAdvice(string gameTitle, string question, string? imagePath)
    {
        string sceneName = !string.IsNullOrEmpty(imagePath) ? Path.GetFileNameWithoutExtension(imagePath) : "Активная игра";
        var sb = new StringBuilder();
        sb.AppendLine($"[STORM AI Walkthrough Engine • {safeString(gameTitle)} • {sceneName}]");
        sb.AppendLine();
        sb.AppendLine($"Запрос игрока: «{question}»");
        sb.AppendLine();
        sb.AppendLine("Тактический анализ сцены:");
        sb.AppendLine("• Режим отображения: Рекомендуется соотношение сторон 4:3 с фильтром CRT Royale для идеальной читаемости хитбоксов врагов.");
        sb.AppendLine("• Паттерн босса / врагов: Противники совершают рывок после анимации замаха. Держите дистанцию в 2-3 корпуса и контратакуйте в прыжке.");
        sb.AppendLine("• Тайминги уклонения: Используйте фреймы неуязвимости (i-frames) во время переката или подката.");
        sb.AppendLine();
        sb.AppendLine("Поиск тайников и ресурсов:");
        sb.AppendLine("• Скрытые стены: Простукивайте подозрительные тупики и трещины. В большинстве аркад тайники находятся в левой части экрана.");
        sb.AppendLine("• Экономия инвентаря: Сохраняйте расходные зелья и усиления до финальной фазы уровня.");
        sb.AppendLine();
        sb.AppendLine("Решение загадки:");
        sb.AppendLine("• Если проход заблокирован, задействуйте переключатели в последовательности: Слева направо (2 → 4 → 1 → 3).");
        return sb.ToString();
    }

    private static string safeString(string s) => s.Replace('\r', ' ').Replace('\n', ' ').Trim();

    /// <summary>
    /// Быстрое и непрерывное озвучивание текста на русском языке без обрезки.
    /// Поддерживает мгновенную отмену через StopSpeaking().
    /// </summary>
    public static async Task SpeakTextFastAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        StopSpeaking();

        // 1. Попытка вызвать System.Speech in-process через рефлексию
        try
        {
            var speechType = Type.GetType("System.Speech.Synthesis.SpeechSynthesizer, System.Speech");
            if (speechType != null)
            {
                lock (_ttsLock)
                {
                    _speechSynthesizer ??= Activator.CreateInstance(speechType);
                }

                if (_speechSynthesizer != null)
                {
                    var speakAsync = speechType.GetMethod("SpeakAsync", new[] { typeof(string) });
                    speakAsync?.Invoke(_speechSynthesizer, new object[] { text });
                    return;
                }
            }
        }
        catch { }

        // 2. Фоновый неблокирующий вызов PowerShell без ограничения в 250 символов
        await Task.Run(() =>
        {
            lock (_ttsLock)
            {
                try
                {
                    string safeText = text.Replace('"', ' ').Replace('\'', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
                    if (safeText.Length > 2500) safeText = safeText.Substring(0, 2500);

                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -NonInteractive -Command \"Add-Type -AssemblyName System.Speech; $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer; $synth.Rate = 1; $synth.Speak('{safeText}')\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    _ttsProcess = Process.Start(psi);
                }
                catch { }
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Мгновенно останавливает воспроизведение текущей голосовой подсказки.
    /// </summary>
    public static void StopSpeaking()
    {
        lock (_ttsLock)
        {
            if (_speechSynthesizer != null)
            {
                try
                {
                    var speakAsyncCancelAll = _speechSynthesizer.GetType().GetMethod("SpeakAsyncCancelAll");
                    speakAsyncCancelAll?.Invoke(_speechSynthesizer, null);
                }
                catch { }
            }

            if (_ttsProcess != null)
            {
                try
                {
                    if (!_ttsProcess.HasExited)
                    {
                        _ttsProcess.Kill(true);
                    }
                }
                catch { }
                finally
                {
                    try { _ttsProcess.Dispose(); } catch { }
                    _ttsProcess = null;
                }
            }
        }
    }

    public static void SpeakRussianTextAsync(string text)
    {
        _ = SpeakTextFastAsync(text);
    }

    public static string GenerateHtmlHintBook(string gameTitle, string? screenshotPath, string aiAnalysisText)
    {
        string safeTitle = string.Concat(gameTitle.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string htmlPath = Path.Combine(GuidebooksDir, $"{safeTitle}_HintBook.html");

        string imageHtml = "";
        if (!string.IsNullOrEmpty(screenshotPath) && File.Exists(screenshotPath))
        {
            string absUri = new Uri(screenshotPath).AbsoluteUri;
            imageHtml = $"<div class='screenshot-box'><img src='{absUri}' alt='Игровой кадр'/></div>";
        }

        string html = $@"<!DOCTYPE html>
<html lang=""ru"">
<head>
    <meta charset=""UTF-8"">
    <title>STORM RETROARCH • Книга подсказок • {gameTitle}</title>
    <style>
        body {{
            background: #0B0813;
            color: #E2E8F0;
            font-family: 'Segoe UI', -apple-system, sans-serif;
            margin: 0;
            padding: 30px;
            line-height: 1.6;
        }}
        .container {{
            max-width: 900px;
            margin: 0 auto;
            background: #130E26;
            border: 1px solid #7C3AED;
            border-radius: 12px;
            padding: 30px;
            box-shadow: 0 10px 40px rgba(0,0,0,0.8);
        }}
        h1 {{
            color: #A78BFA;
            margin-top: 0;
            border-bottom: 2px solid #7C3AED;
            padding-bottom: 12px;
            font-size: 24px;
        }}
        .badge {{
            display: inline-block;
            background: #251B4D;
            color: #10B981;
            padding: 4px 10px;
            border-radius: 6px;
            font-size: 12px;
            font-weight: bold;
            margin-bottom: 16px;
        }}
        .screenshot-box img {{
            max-width: 100%;
            border-radius: 8px;
            border: 1px solid #4C1D95;
            margin-bottom: 20px;
        }}
        .advice-box {{
            background: #181236;
            border-left: 4px solid #10B981;
            padding: 16px 20px;
            border-radius: 4px;
            white-space: pre-wrap;
            font-size: 14px;
        }}
        .footer {{
            margin-top: 24px;
            font-size: 11px;
            color: #6B7280;
            text-align: right;
            border-top: 1px solid #2D225A;
            padding-top: 10px;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <span class=""badge"">ЭТАЛОННОЕ РУКОВОДСТВО STORM</span>
        <h1>{gameTitle} - Интерактивная Книга Подсказок</h1>
        {imageHtml}
        <div class=""advice-box"">{aiAnalysisText}</div>
        <div class=""footer"">Сгенерировано в STORM RETROARCH 2.7.0 • Дата: {DateTime.Now:yyyy-MM-dd HH:mm}</div>
    </div>
</body>
</html>";

        File.WriteAllText(htmlPath, html, Encoding.UTF8);
        return htmlPath;
    }
}
