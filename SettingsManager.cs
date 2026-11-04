using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace StormRetroArch;

public class UserSettings
{
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public string ThemeName { get; set; } = "MIDNIGHT";
    public int AccentIndex { get; set; } = 0;
    public int FontIndex { get; set; } = 0;
    public string LanguageCode { get; set; } = string.Empty;
    public string VideoDriver { get; set; } = "vulkan";
    public bool PreemptiveFrames { get; set; } = true;
    public bool VrrEnabled { get; set; } = true;
    public bool WasapiLowLatency { get; set; } = true;
    public bool IntegerScaling { get; set; } = false;
    public string ShaderPreset { get; set; } = "None";
    public string RetroArchPath { get; set; } = string.Empty;
    public List<string> RecentRoms { get; set; } = new();
    public bool TelemetryOverlayVisible { get; set; } = false;
    public bool SmartProfileAutoApply { get; set; } = true;
    public bool AutoSaveSyncEnabled { get; set; } = false;
    public string CloudSyncFolder { get; set; } = string.Empty;
}

public static class SettingsManager
{
    private static readonly string AppDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string SettingsDir = Path.Combine(AppDataRoot, "STORM RETROARCH");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static UserSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                string json = File.ReadAllText(SettingsFile);
                var loaded = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    Current = loaded;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsManager.Load Error]: {ex.Message}");
        }
        Current = new UserSettings();
    }

    public static void Save()
    {
        try
        {
            if (!Directory.Exists(SettingsDir))
            {
                Directory.CreateDirectory(SettingsDir);
            }
            string json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(SettingsFile, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsManager.Save Error]: {ex.Message}");
        }
    }
}
