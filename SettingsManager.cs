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

    // UI Scalability, Layout & Customization (v1.2.0)
    public double UiScale { get; set; } = 1.0;
    public double BentoCardWidth { get; set; } = 380.0;
    public double CompactCardWidth { get; set; } = 200.0;
    public double SidebarWidth { get; set; } = 276.0;
    public bool SidebarCollapsed { get; set; } = false;
    public List<int> DashboardCardOrder { get; set; } = new() { 0, 1, 2, 3 };
}

public static class SettingsManager
{
    private static readonly string AppDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string LegacySettingsDir = Path.Combine(AppDataRoot, "STORM RETROARCH");
    private static readonly string LegacySettingsFile = Path.Combine(LegacySettingsDir, "settings.json");

    public static string LocalSettingsFile => Path.Combine(CoreManager.AppDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static UserSettings Current { get; private set; } = new();

    public static void NormalizeRootPaths()
    {
        string rootSaveSync = Path.Combine(CoreManager.AppDir, "save sync");
        try
        {
            if (!Directory.Exists(rootSaveSync))
                Directory.CreateDirectory(rootSaveSync);
        }
        catch { }

        string cur = (Current.CloudSyncFolder ?? string.Empty).Trim();
        bool needsReset = string.IsNullOrEmpty(cur)
            || cur.EndsWith("cloud_saves", StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(cur);

        if (!needsReset && cur.EndsWith("\\save sync", StringComparison.OrdinalIgnoreCase))
        {
            // If CloudSyncFolder points to default 'save sync' inside a DIFFERENT install folder (e.g. C:\Program Files\STORM RETROARCH\save sync when running from N:\STORM RETROARCH), bind it to the active root folder!
            if (!cur.Equals(rootSaveSync, StringComparison.OrdinalIgnoreCase))
            {
                needsReset = true;
            }
        }

        if (needsReset)
        {
            Current.CloudSyncFolder = rootSaveSync;
        }

        if (Current.UiScale < 0.75 || Current.UiScale > 1.5) Current.UiScale = 1.0;
        if (Current.BentoCardWidth < 250 || Current.BentoCardWidth > 600) Current.BentoCardWidth = 380.0;
        if (Current.CompactCardWidth < 130 || Current.CompactCardWidth > 360) Current.CompactCardWidth = 200.0;
        if (Current.SidebarWidth < 220 || Current.SidebarWidth > 460) Current.SidebarWidth = 276.0;
        if (Current.DashboardCardOrder == null || Current.DashboardCardOrder.Count != 4)
            Current.DashboardCardOrder = new List<int> { 0, 1, 2, 3 };
    }

    public static void Load()
    {
        try
        {
            string targetFile = File.Exists(LocalSettingsFile) ? LocalSettingsFile : LegacySettingsFile;
            if (File.Exists(targetFile))
            {
                string json = File.ReadAllText(targetFile);
                var loaded = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    Current = loaded;
                    NormalizeRootPaths();
                    Save();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsManager.Load Error]: {ex.Message}");
        }
        Current = new UserSettings();
        NormalizeRootPaths();
        Save();
    }

    public static void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(Current, JsonOptions);
            try
            {
                File.WriteAllText(LocalSettingsFile, json);
            }
            catch { }

            if (!Directory.Exists(LegacySettingsDir))
            {
                Directory.CreateDirectory(LegacySettingsDir);
            }
            File.WriteAllText(LegacySettingsFile, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsManager.Save Error]: {ex.Message}");
        }
    }
}
