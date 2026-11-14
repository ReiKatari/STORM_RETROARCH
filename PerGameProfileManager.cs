using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StormRetroArch;

public class PerGameOverrideProfile
{
    public string GameName { get; set; } = string.Empty;
    public string PreferredCoreDll { get; set; } = string.Empty;
    public string ShaderPresetName { get; set; } = string.Empty;
    public double Scanlines { get; set; } = 0.0;
    public double Mask { get; set; } = 0.0;
    public double Curvature { get; set; } = 0.0;
    public string BezelName { get; set; } = string.Empty;
    public string AspectMode { get; set; } = "Core";
    public int RunAheadFrames { get; set; } = 0;
    public string AudioDspPreset { get; set; } = "Чистый студийный";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Manages per-game individual emulation, shader, bezel, aspect ratio, audio DSP,
/// and core override profiles stored under config/games/{safeName}.json.
/// </summary>
public static class PerGameProfileManager
{
    private static readonly string ConfigDir = Path.Combine(CoreManager.AppDir, "config", "games");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    static PerGameProfileManager()
    {
        try
        {
            if (!Directory.Exists(ConfigDir))
            {
                Directory.CreateDirectory(ConfigDir);
            }
        }
        catch { }
    }

    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "unknown_game";
        string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        string sanitized = Regex.Replace(name, $"[{invalidChars}]", "_").Trim();
        return string.IsNullOrEmpty(sanitized) ? "unknown_game" : sanitized;
    }

    public static bool HasProfile(string gameName)
    {
        if (string.IsNullOrWhiteSpace(gameName)) return false;
        string safeName = SanitizeFileName(gameName);
        string filePath = Path.Combine(ConfigDir, $"{safeName}.json");
        return File.Exists(filePath);
    }

    public static PerGameOverrideProfile? TryLoadProfileForGame(string gameName)
    {
        if (string.IsNullOrWhiteSpace(gameName)) return null;

        try
        {
            string safeName = SanitizeFileName(gameName);
            string filePath = Path.Combine(ConfigDir, $"{safeName}.json");

            if (!File.Exists(filePath)) return null;

            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<PerGameOverrideProfile>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static bool SaveProfileForGame(RomItem game)
    {
        if (game == null || string.IsNullOrWhiteSpace(game.Name)) return false;

        try
        {
            if (!Directory.Exists(ConfigDir))
            {
                Directory.CreateDirectory(ConfigDir);
            }

            var s = SettingsManager.Current;
            var host = NativeLibretroHost.Current;

            int hostAspect = host?.AspectMode ?? 0;
            string aspectStr = hostAspect switch
            {
                0 => "4:3",
                1 => "16:9",
                2 => "1:1",
                3 => "Растянуть",
                _ => "Core"
            };

            var profile = new PerGameOverrideProfile
            {
                GameName = game.Name,
                PreferredCoreDll = !string.IsNullOrEmpty(game.CoreDll) ? game.CoreDll : (s?.GameCoreOverrides.GetValueOrDefault(game.Name) ?? ""),
                ShaderPresetName = s?.ShaderScanlineIntensity > 0 ? "Пользовательский ЭЛТ" : "Чистый",
                Scanlines = host?.ScanlineIntensity ?? (s?.ShaderScanlineIntensity ?? 0.0),
                Mask = host?.MaskIntensity ?? (s?.ShaderMaskIntensity ?? 0.0),
                Curvature = host?.CurvatureIntensity ?? (s?.ShaderCurvature ?? 0.0),
                BezelName = BezelManager.GetActiveBezel()?.Name ?? "",
                AspectMode = aspectStr,
                RunAheadFrames = host?.RunAheadFrames ?? (s?.RunAheadFrames ?? 0),
                AudioDspPreset = AudioDspEngine.Shared.ActivePreset,
                UpdatedUtc = DateTime.UtcNow
            };

            string safeName = SanitizeFileName(game.Name);
            string filePath = Path.Combine(ConfigDir, $"{safeName}.json");

            string json = JsonSerializer.Serialize(profile, JsonOptions);
            File.WriteAllText(filePath, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool DeleteProfileForGame(string gameName)
    {
        if (string.IsNullOrWhiteSpace(gameName)) return false;

        try
        {
            string safeName = SanitizeFileName(gameName);
            string filePath = Path.Combine(ConfigDir, $"{safeName}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }
        }
        catch { }
        return false;
    }

    public static List<PerGameOverrideProfile> GetAllSavedProfiles()
    {
        var result = new List<PerGameOverrideProfile>();
        try
        {
            if (!Directory.Exists(ConfigDir)) return result;

            var files = Directory.GetFiles(ConfigDir, "*.json");
            foreach (var f in files)
            {
                try
                {
                    string json = File.ReadAllText(f);
                    var p = JsonSerializer.Deserialize<PerGameOverrideProfile>(json, JsonOptions);
                    if (p != null) result.Add(p);
                }
                catch { }
            }
        }
        catch { }
        return result;
    }
}
