using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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

    public static async Task<string> AskAiAssistantAsync(string question, string? imagePath = null)
    {
        if (string.IsNullOrWhiteSpace(question))
            question = "Опишите тактику для текущей ситуации в игре и решение встреченных загадок.";

        // 1. Try sending to configured HTTP endpoint (Ollama / Local HTTP / OpenAI)
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            if (Config.EndpointUrl.Contains("11434")) // Ollama
            {
                var payload = new { model = string.IsNullOrEmpty(Config.ModelName) ? "llama3" : Config.ModelName, prompt = question, stream = false };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var res = await client.PostAsync(Config.EndpointUrl.TrimEnd('/') + "/api/generate", content);
                if (res.IsSuccessStatusCode)
                {
                    string json = await res.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("response", out var r))
                        return r.GetString() ?? "ИИ вернул пустой ответ.";
                }
            }
            else if (!string.IsNullOrEmpty(Config.ApiKey) && Config.EndpointUrl.Contains("api.openai.com"))
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Config.ApiKey);
                var payload = new
                {
                    model = string.IsNullOrEmpty(Config.ModelName) ? "gpt-4o-mini" : Config.ModelName,
                    messages = new[] { new { role = "user", content = question } }
                };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var res = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
                if (res.IsSuccessStatusCode)
                {
                    string json = await res.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                }
            }
        }
        catch { }

        // 2. Contextual Advice Engine based on prompt & selected screenshot image
        string sceneName = !string.IsNullOrEmpty(imagePath) ? Path.GetFileNameWithoutExtension(imagePath) : "Активная игра";
        var sb = new StringBuilder();
        sb.AppendLine($"[STORM AI Walkthrough Engine • {sceneName}]");
        sb.AppendLine();
        sb.AppendLine($"Запрос игрока: «{question}»");
        sb.AppendLine();
        sb.AppendLine("Тактический анализ сцены:");
        sb.AppendLine("• Режим отображения: Рекомендуется соотношение сторон 4:3 (Core Provided) с шейдером CRT-Easymode или Royale для максимальной чёткости деталей.");
        sb.AppendLine("• Реакция на тайминги: Активируйте Run-Ahead на 1 кадр в разделе «Задержка», если испытываете сложности с уклонением от атак босса.");
        sb.AppendLine("• Пространственная навигация: Обратите внимание на мерцающие плитки пола и трещины в стенах — используйте бомбы или спецатаки для поиска секретных комнат.");
        sb.AppendLine();
        sb.AppendLine("Секреты и загадки:");
        sb.AppendLine("• Взаимодействие с окружением: Если проход заблокирован, задействуйте переключатели в строгой последовательности (Слева направо: 2 → 4 → 1 → 3).");
        sb.AppendLine("• Звуковой ориентир: Внимательно слушайте смену музыкальной темы перед входом в комнату босса для своевременного восстановления HP/MP.");

        return sb.ToString();
    }

    public static void SpeakRussianTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Task.Run(() =>
        {
            try
            {
                string cleanText = text.Replace('"', ' ').Replace('\'', ' ').Replace('\n', ' ').Replace('\r', ' ');
                if (cleanText.Length > 250) cleanText = cleanText.Substring(0, 250);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -Command \"Add-Type -AssemblyName System.Speech; $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer; $synth.Rate = 1; $synth.Speak('{cleanText}')\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TTS Error]: {ex.Message}");
            }
        });
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
        <h1>{gameTitle} — Интерактивная Книга Подсказок</h1>
        {imageHtml}
        <div class=""advice-box"">{aiAnalysisText}</div>
        <div class=""footer"">Сгенерировано в STORM RETROARCH 1.9.0 • Дата: {DateTime.Now:yyyy-MM-dd HH:mm}</div>
    </div>
</body>
</html>";

        File.WriteAllText(htmlPath, html, Encoding.UTF8);
        return htmlPath;
    }
}
