using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace StormRetroArch;

public class WatchedRomFolder
{
    public string Path { get; set; } = string.Empty;
    public bool Recursive { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public DateTime LastScannedUtc { get; set; } = DateTime.UtcNow;
}

public class UserSettings
{
    public string ThemeName { get; set; } = "MIDNIGHT";

    public int AccentIndex { get; set; } = 0;

    public int FontIndex { get; set; } = 0;

    public string LanguageCode { get; set; } = "ru";

    public bool CheckUpdatesOnStartup { get; set; } = true;

    public string RetroArchPath { get; set; } = string.Empty;

    public bool TelemetryOverlayVisible { get; set; } = false;



    public string Language { get; set; } = string.Empty;
    public string Theme { get; set; } = "MIDNIGHT";
    public string Accent { get; set; } = "Violet";
    public string FontFamily { get; set; } = "Inter Variable";
    public string ViewMode { get; set; } = "Bento";
    public bool AutoCheckUpdates { get; set; } = true;
    public bool StartMinimizedToTray { get; set; } = false;
    public List<string> RomDirectories { get; set; } = new();
    public List<WatchedRomFolder> WatchedFolders { get; set; } = new();
    public List<string> RecentRoms { get; set; } = new();
    public double CoreOptionsSidebarWidth { get; set; } = 330.0;

    // AI Walkthrough & Cloud Save Sync
    public string GeminiApiKey { get; set; } = string.Empty;
    public string AiModel { get; set; } = "gemini-2.5-flash";
    public bool AiAutoCaptureContext { get; set; } = true;
    public bool AiVoiceOutput { get; set; } = false;
    public bool CloudSyncEnabled { get; set; } = false;
    public string CloudSyncFolder { get; set; } = string.Empty;

    // UI Scalability, Layout & Customization (1.5.0)
    public double UiScale { get; set; } = 1.0;
    public double BentoCardWidth { get; set; } = 400.0;
    public double CompactCardWidth { get; set; } = 200.0;
    public double SidebarWidth { get; set; } = 276.0;
    public bool SidebarCollapsed { get; set; } = false;
    public List<int> DashboardCardOrder { get; set; } = new() { 0, 1, 2, 3 };

    // Per-Platform Default Core & Per-Game Core Overrides
    public Dictionary<string, string> PlatformDefaultCores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> GameCoreOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Persistent Window Geometry (1.5.0)
    public double WindowWidth { get; set; } = 1500.0;
    public double WindowHeight { get; set; } = 890.0;
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public bool WindowMaximized { get; set; } = false;

    // Auto-Resume & UI Animation Settings (1.6.0)
    public bool AutoSaveLoadOnExit { get; set; } = false;
    public bool EnableUiAnimations { get; set; } = true;

    // Hotkey bindings dictionary (2.0.0)
    public Dictionary<string, string> CustomHotkeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Interactive Shader Studio Parameters (2.0.0)
    public double ShaderScanlineIntensity { get; set; } = 0.0;
    public double ShaderMaskIntensity { get; set; } = 0.0;
    public double ShaderVignetteIntensity { get; set; } = 0.0;
    public double ShaderBrightnessGain { get; set; } = 1.0;
    public int ShaderColorTemperatureMode { get; set; } = 0;
    public double ShaderCurvature { get; set; } = 0.0;
    public double ShaderChromatic { get; set; } = 0.0;
    public string ActiveShaderPreset { get; set; } = "none";

    // Streaming, Recording & AI Translation Engine Settings
    public string VideoResolution { get; set; } = "Native";
    public string VideoScaling { get; set; } = "neighbor";
    public int VideoFps { get; set; } = 60;
    public string VideoRateControl { get; set; } = "CBR";
    public string VideoPreset { get; set; } = "veryfast";
    public string AudioRecordCodec { get; set; } = "aac";
    public int AudioSampleRate { get; set; } = 48000;
    public string AiSourceLang { get; set; } = "auto";
    public string AiTargetLang { get; set; } = "ru";
    public string AiEngine { get; set; } = "builtin";

    // Emulation Performance & Run-Ahead (2.0.0)
    public int RunAheadFrames { get; set; } = 0;
    public bool PrecisionFramePacer { get; set; } = true;
    public bool AutoBezelByPlatform { get; set; } = true;
    public bool AmbiluxGlowEnabled { get; set; } = false;

    // Gamepad, Deadzone, Rumble & Turbo (2.0.0)
    public int AnalogDeadzonePercent { get; set; } = 15;
    public int TriggerThresholdPercent { get; set; } = 12;
    public bool RumbleEnabled { get; set; } = true;
    public int TurboFireHz { get; set; } = 15;
    public bool TurboButtonsEnabled { get; set; } = false;

    // Studio Audio DSP & Soft-Limiter Pipeline (2.0.0)
    public string AudioDspPreset { get; set; } = "Чистый студийный";
    public int AudioBassGainDb { get; set; } = 0;
    public int AudioTrebleGainDb { get; set; } = 0;
    public int AudioStereoWidthPercent { get; set; } = 100;
    public int AudioReverbPercent { get; set; } = 0;
    public bool AudioSoftLimiter { get; set; } = true;

    // 2.0.0 Hardware Render, WASAPI Low-Latency Audio, DRC, Instant Replay & Gamepad UI
    public bool HwRenderEnabled { get; set; } = true;
    public bool WasapiLowLatencyAudio { get; set; } = true;
    public bool DynamicRateControl { get; set; } = true;
    public double DrcMaxDelta { get; set; } = 0.0025;
    public bool InstantReplayEnabled { get; set; } = true;
    public bool GamepadUiNavigation { get; set; } = true;

    // 2.6.1 Hardware Engine, Universal Bezel and Mega Bezel Pipeline
    public string GraphicsBackend { get; set; } = "Auto";
    public bool AutoDownloadMissingCoreOnLaunch { get; set; } = true;
    public bool UniversalBezelForAllCores { get; set; } = true;
    public string ActiveMegaBezelSlangp { get; set; } = "";

    // 2.7.5 Universal Hardware Autoconfig, Multi-Player Port Routing and Hotkey Chords
    public Dictionary<int, string> PortDeviceAssignments { get; set; } = new() { [0] = "auto" };
    public Dictionary<int, Dictionary<int, string>> PlayerKeyboardBindings { get; set; } = new();
    public string GamepadChordModifier { get; set; } = "SelectOrBack";
}

public static class SettingsManager
{
    private static readonly string AppDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string LegacySettingsDir = Path.Combine(AppDataRoot, "STORM RETROARCH");
    private static readonly string LegacySettingsFile = Path.Combine(LegacySettingsDir, "settings.json");

    public static string AppRootDir => CoreManager.AppDir;
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
            || cur.StartsWith(@"C:\Program Files", StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(cur);

        if (!needsReset && cur.EndsWith(@"\save sync", StringComparison.OrdinalIgnoreCase))
        {
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
        if (Current.BentoCardWidth < 360 || Current.BentoCardWidth > 620) Current.BentoCardWidth = 400.0;
        if (Current.CompactCardWidth < 130 || Current.CompactCardWidth > 360) Current.CompactCardWidth = 200.0;
        if (Current.SidebarWidth < 220 || Current.SidebarWidth > 460) Current.SidebarWidth = 276.0;
        if (Current.CoreOptionsSidebarWidth < 250 || Current.CoreOptionsSidebarWidth > 560 || double.IsNaN(Current.CoreOptionsSidebarWidth)) Current.CoreOptionsSidebarWidth = 330.0;
        NormalizeWatchedFolders();

        if (Current.WindowWidth < 960 || Current.WindowWidth > 7680 || double.IsNaN(Current.WindowWidth)) Current.WindowWidth = 1500.0;
        if (Current.WindowHeight < 620 || Current.WindowHeight > 4320 || double.IsNaN(Current.WindowHeight)) Current.WindowHeight = 890.0;
        if (Current.DashboardCardOrder == null || Current.DashboardCardOrder.Count != 4)
            Current.DashboardCardOrder = new List<int> { 0, 1, 2, 3 };
        Current.PlatformDefaultCores ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Current.GameCoreOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Current.CustomHotkeys ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Current.ShaderBrightnessGain < 0.5 || Current.ShaderBrightnessGain > 2.0) Current.ShaderBrightnessGain = 1.0;
        if (Current.ShaderCurvature < 0.0 || Current.ShaderCurvature > 0.5) Current.ShaderCurvature = 0.0;
        if (Current.ShaderChromatic < 0.0 || Current.ShaderChromatic > 1.0) Current.ShaderChromatic = 0.0;

        if (string.IsNullOrWhiteSpace(Current.ActiveShaderPreset) ||
            Current.ActiveShaderPreset.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            Current.ActiveShaderPreset.Equals("Без фильтра", StringComparison.OrdinalIgnoreCase))
        {
            Current.ActiveShaderPreset = "none";
            Current.ShaderScanlineIntensity = 0.0;
            Current.ShaderMaskIntensity = 0.0;
            Current.ShaderVignetteIntensity = 0.0;
            Current.ShaderBrightnessGain = 1.0;
        }
        if (Current.RunAheadFrames < 0 || Current.RunAheadFrames > 2) Current.RunAheadFrames = 0;
        if (Current.AnalogDeadzonePercent < 5 || Current.AnalogDeadzonePercent > 40) Current.AnalogDeadzonePercent = 15;
        if (Current.TriggerThresholdPercent < 5 || Current.TriggerThresholdPercent > 50) Current.TriggerThresholdPercent = 12;
        if (Current.TurboFireHz < 5 || Current.TurboFireHz > 30) Current.TurboFireHz = 15;
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

    public static string NormalizeFolderPath(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return string.Empty;
        string trimmed = folderPath.Trim();
        try
        {
            return Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return trimmed.TrimEnd('\\', '/');
        }
    }

    public static void NormalizeWatchedFolders()
    {
        Current.WatchedFolders ??= new List<WatchedRomFolder>();
        Current.RomDirectories ??= new List<string>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clean = new List<WatchedRomFolder>();

        foreach (var wf in Current.WatchedFolders)
        {
            if (wf == null) continue;
            string norm = NormalizeFolderPath(wf.Path);
            if (string.IsNullOrEmpty(norm) || !seen.Add(norm)) continue;
            wf.Path = norm;
            clean.Add(wf);
        }

        foreach (var rawDir in Current.RomDirectories)
        {
            string norm = NormalizeFolderPath(rawDir);
            if (string.IsNullOrEmpty(norm) || !seen.Add(norm)) continue;
            clean.Add(new WatchedRomFolder
            {
                Path = norm,
                Recursive = true,
                Enabled = true,
                LastScannedUtc = DateTime.UtcNow
            });
        }

        Current.WatchedFolders = clean;
        Current.RomDirectories = clean.Where(w => w.Enabled).Select(w => w.Path).ToList();
    }

    public static WatchedRomFolder AddOrUpdateWatchedFolder(string folderPath, bool recursive)
    {
        NormalizeWatchedFolders();
        string norm = NormalizeFolderPath(folderPath);
        var existing = Current.WatchedFolders.FirstOrDefault(w => w.Path.Equals(norm, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Recursive = recursive;
            existing.Enabled = true;
            existing.LastScannedUtc = DateTime.UtcNow;
        }
        else
        {
            existing = new WatchedRomFolder
            {
                Path = norm,
                Recursive = recursive,
                Enabled = true,
                LastScannedUtc = DateTime.UtcNow
            };
            Current.WatchedFolders.Add(existing);
        }
        NormalizeWatchedFolders();
        Save();
        return existing;
    }

    public static bool SetWatchedFolderRecursive(string folderPath, bool recursive)
    {
        NormalizeWatchedFolders();
        string norm = NormalizeFolderPath(folderPath);
        var existing = Current.WatchedFolders.FirstOrDefault(w => w.Path.Equals(norm, StringComparison.OrdinalIgnoreCase));
        if (existing == null) return false;
        existing.Recursive = recursive;
        existing.LastScannedUtc = DateTime.UtcNow;
        Save();
        return true;
    }

    public static bool RemoveWatchedFolder(string folderPath)
    {
        NormalizeWatchedFolders();
        string norm = NormalizeFolderPath(folderPath);
        int removed = Current.WatchedFolders.RemoveAll(w => w.Path.Equals(norm, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            NormalizeWatchedFolders();
            Save();
            return true;
        }
        return false;
    }

    private static void WriteAtomicText(string targetFile, string contents)
    {
        string? dir = Path.GetDirectoryName(targetFile);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        string tmp = targetFile + ".tmp";
        File.WriteAllText(tmp, contents);
        File.Move(tmp, targetFile, overwrite: true);
    }

    public static void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(Current, JsonOptions);
            try { WriteAtomicText(LocalSettingsFile, json); } catch { }
            try { WriteAtomicText(LegacySettingsFile, json); } catch { }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsManager.Save Error]: {ex.Message}");
        }
    }
}
