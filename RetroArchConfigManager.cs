using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace StormRetroArch;

/// <summary>
/// Direct parser and serializer for the real RetroArch configuration file (retroarch.cfg).
/// Supports all 16 configuration categories, hotkeys, drivers, and directories.
/// </summary>
public static class RetroArchConfigManager
{
    private static readonly Dictionary<string, string> _config = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();

    public static string ConfigFilePath { get; private set; } = string.Empty;
    public static bool IsLoaded { get; private set; }

    public static event Action? ConfigChanged;

    public static string ResolveConfigPath()
    {
        // 1. Always prioritize the active root installation directory (e.g. N:\STORM RETROARCH\retroarch.cfg)
        var appDir = CoreManager.AppDir;
        var appDirCfg = Path.Combine(appDir, "retroarch.cfg");
        if (File.Exists(appDirCfg)) return appDirCfg;

        // 2. User specified path
        if (!string.IsNullOrWhiteSpace(SettingsManager.Current.RetroArchPath))
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsManager.Current.RetroArchPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    var cand = Path.Combine(dir, "retroarch.cfg");
                    if (File.Exists(cand))
                    {
                        try { File.Copy(cand, appDirCfg, false); return appDirCfg; } catch { return cand; }
                    }
                }
            }
            catch { }
        }

        // 3. Look next to retroarch executable and copy to AppDir if needed
        try
        {
            var exe = CoreManager.FindRetroArchExecutable();
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                var exeDir = Path.GetDirectoryName(exe);
                if (!string.IsNullOrEmpty(exeDir))
                {
                    var cand = Path.Combine(exeDir, "retroarch.cfg");
                    if (File.Exists(cand))
                    {
                        try { File.Copy(cand, appDirCfg, false); return appDirCfg; } catch { return cand; }
                    }
                }
            }
        }
        catch { }

        // 4. Check AppData
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appDataCfg = Path.Combine(appData, "RetroArch", "retroarch.cfg");
            if (File.Exists(appDataCfg)) return appDataCfg;
        }
        catch { }

        // 5. Parent folders (e.g. running from bin/Release/net10.0-windows/)
        try
        {
            var cur = new DirectoryInfo(appDir);
            for (int i = 0; i < 3 && cur.Parent != null; i++)
            {
                cur = cur.Parent;
                var cand = Path.Combine(cur.FullName, "retroarch.cfg");
                if (File.Exists(cand)) return cand;
            }
        }
        catch { }

        // Default path
        return appDirCfg;
    }

    public static void Load(string? explicitPath = null)
    {
        lock (_lock)
        {
            ConfigFilePath = explicitPath ?? ResolveConfigPath();
            _config.Clear();

            if (File.Exists(ConfigFilePath))
            {
                try
                {
                    var lines = File.ReadAllLines(ConfigFilePath, Encoding.UTF8);
                    foreach (var rawLine in lines)
                    {
                        var line = rawLine.Trim();
                        if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                        int eqIdx = line.IndexOf('=');
                        if (eqIdx > 0)
                        {
                            string key = line.Substring(0, eqIdx).Trim();
                            string val = line.Substring(eqIdx + 1).Trim();

                            // Strip quotes
                            if (val.StartsWith("\"") && val.EndsWith("\"") && val.Length >= 2)
                            {
                                val = val.Substring(1, val.Length - 2);
                            }
                            _config[key] = val;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RetroArchConfigManager] Load error: {ex.Message}");
                }
            }

            // Populate mandatory defaults across all 16 categories
            EnsureDefaults();
            IsLoaded = true;
            ConfigChanged?.Invoke();
        }
    }

    public static void Save(string? targetPath = null)
    {
        lock (_lock)
        {
            string outPath = targetPath ?? ConfigFilePath;
            if (string.IsNullOrWhiteSpace(outPath))
                outPath = Path.Combine(CoreManager.AppDir, "retroarch.cfg");

            try
            {
                var dir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var sb = new StringBuilder();
                sb.AppendLine("# STORM RETROARCH 1.5.0 - Unified RetroArch Configuration File");
                sb.AppendLine($"# Last saved: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();

                // Sort keys alphabetically for clean diffs
                var keys = new List<string>(_config.Keys);
                keys.Sort(StringComparer.OrdinalIgnoreCase);

                foreach (var k in keys)
                {
                    string val = _config[k];
                    sb.AppendLine($"{k} = \"{val}\"");
                }

                File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
                ConfigFilePath = outPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RetroArchConfigManager] Save error: {ex.Message}");
            }
        }
    }

    public static void ResetToDefaults()
    {
        lock (_lock)
        {
            _config.Clear();
            EnsureDefaults();
            Save();
            ConfigChanged?.Invoke();
        }
    }

    private static void EnsureDefaults()
    {
        // 1. DRIVERS
        SetDefault("video_driver", "vulkan");
        SetDefault("menu_driver", "ozone");
        SetDefault("audio_driver", "wasapi");
        SetDefault("audio_resampler", "sinc");
        SetDefault("input_driver", "raw");
        SetDefault("input_joypad_driver", "xinput");
        SetDefault("record_driver", "null");
        SetDefault("midi_driver", "winmm");

        // 2. VIDEO
        SetDefault("video_fullscreen", "true");
        SetDefault("video_windowed_fullscreen", "true");
        SetDefault("video_fullscreen_x", "0");
        SetDefault("video_fullscreen_y", "0");
        SetDefault("video_scale", "3.000000");
        SetDefault("aspect_ratio_index", "0");
        SetDefault("video_scale_integer", "false");
        SetDefault("video_smooth", "false");
        SetDefault("video_shader_enable", "false");
        SetDefault("video_vsync", "true");
        SetDefault("video_adaptive_vsync", "false");
        SetDefault("vrr_runloop_enable", "true");
        SetDefault("video_hard_sync", "true");
        SetDefault("video_max_swapchain_images", "3");
        SetDefault("video_black_frame_insertion", "0");
        SetDefault("video_threaded", "false");
        SetDefault("fps_show", "false");
        SetDefault("framecount_show", "false");
        SetDefault("memory_show", "false");

        // 3. AUDIO
        SetDefault("audio_enable", "true");
        SetDefault("audio_sync", "true");
        SetDefault("audio_latency", "64");
        SetDefault("audio_wasapi_exclusive_mode", "false");
        SetDefault("audio_out_rate", "48000");
        SetDefault("audio_volume", "0.000000");
        SetDefault("audio_rate_control", "true");
        SetDefault("audio_enable_menu", "true");
        SetDefault("audio_mute_enable", "false");

        // 4. INPUT & HOTKEYS
        SetDefault("input_max_users", "5");
        SetDefault("input_autodetect_enable", "true");
        SetDefault("enable_device_vibration", "true");
        SetDefault("input_analog_deadzone", "0.050000");
        SetDefault("input_analog_sensitivity", "1.000000");
        SetDefault("input_turbo_period", "6");
        SetDefault("input_menu_toggle", "f1");
        SetDefault("input_desktop_menu_toggle", "f5");
        SetDefault("input_toggle_fullscreen", "f11");
        SetDefault("input_toggle_fast_forward", "space");
        SetDefault("input_rewind", "r");
        SetDefault("input_pause_toggle", "p");
        SetDefault("input_save_state", "f2");
        SetDefault("input_load_state", "f4");
        SetDefault("input_screenshot", "f8");
        SetDefault("input_exit_emulator", "escape");
        SetDefault("quit_press_twice", "true");

        // 5. LATENCY
        SetDefault("preemptive_frames_enable", "true");
        SetDefault("run_ahead_enabled", "false");
        SetDefault("run_ahead_frames", "1");
        SetDefault("run_ahead_secondary_instance", "true");
        SetDefault("video_frame_delay_auto", "true");
        SetDefault("video_frame_delay", "0");

        // 6. SAVING
        SetDefault("savestate_auto_save", "false");
        SetDefault("savestate_auto_load", "false");
        SetDefault("savestate_auto_index", "true");
        SetDefault("savestate_compression", "true");
        SetDefault("savefiles_in_content_dir", "false");
        SetDefault("sort_savefiles_enable", "true");
        SetDefault("sort_savestates_enable", "true");
        SetDefault("autosave_interval", "0");

        // 7. FRAME THROTTLE & REWIND
        SetDefault("rewind_enable", "false");
        SetDefault("rewind_buffer_size", "100");
        SetDefault("rewind_granularity", "1");
        SetDefault("fastforward_ratio", "0.000000");
        SetDefault("slowmotion_ratio", "3.000000");
        SetDefault("pause_nonactive", "true");

        // 8. ON-SCREEN DISPLAY
        SetDefault("input_overlay_enable", "false");
        SetDefault("input_overlay_opacity", "0.700000");
        SetDefault("input_overlay_scale_landscape", "1.000000");
        SetDefault("video_font_enable", "true");
        SetDefault("video_font_size", "32.000000");
        SetDefault("menu_widget_scale_auto", "true");

        // 9. USER INTERFACE
        SetDefault("ozone_color_theme", "0");
        SetDefault("menu_horizontal_animation", "true");
        SetDefault("menu_throttle_framerate", "true");

        // 10. RETROACHIEVEMENTS
        SetDefault("cheevos_enable", "false");
        SetDefault("cheevos_username", "");
        SetDefault("cheevos_password", "");
        SetDefault("cheevos_hardcore_mode_enable", "false");
        SetDefault("cheevos_leaderboards_enable", "true");
        SetDefault("cheevos_richpresence_enable", "true");
        SetDefault("cheevos_test_unofficial", "false");
        SetDefault("cheevos_auto_screenshot", "false");

        // 11. NETWORK & NETPLAY
        SetDefault("netplay_public_announce", "false");
        SetDefault("netplay_use_mitm_server", "true");
        SetDefault("netplay_mitm_server", "nyc");
        SetDefault("netplay_ip_port", "55435");
        SetDefault("netplay_input_latency_frames_min", "0");
        SetDefault("netplay_spectate_mode_enable", "false");

        // 12. RECORDING
        SetDefault("video_gpu_record", "false");
        SetDefault("streaming_mode", "0");

        // 13. AI SERVICE
        SetDefault("ai_service_enable", "false");
        SetDefault("ai_service_mode", "0");
        SetDefault("ai_service_url", "http://localhost:4404/");
        SetDefault("ai_service_source_lang", "0");
        SetDefault("ai_service_target_lang", "0");
        SetDefault("ai_service_pause", "false");

        // 14. CONFIGURATION & LOGGING
        SetDefault("config_save_on_exit", "true");
        SetDefault("auto_overrides_enable", "true");
        SetDefault("auto_remaps_enable", "true");
        SetDefault("auto_shaders_enable", "false");
        SetDefault("log_to_file", "false");
        SetDefault("frontend_log_level", "1");

        // 15. DIRECTORY
        string baseDir = CoreManager.AppDir;
        SetDefault("system_directory", Path.Combine(baseDir, "system"));
        SetDefault("libretro_directory", Path.Combine(baseDir, "cores"));
        SetDefault("savefile_directory", Path.Combine(baseDir, "saves"));
        SetDefault("savestate_directory", Path.Combine(baseDir, "states"));
        SetDefault("screenshot_directory", Path.Combine(baseDir, "screenshots"));
        SetDefault("video_shader_dir", Path.Combine(baseDir, "shaders"));
        SetDefault("assets_directory", Path.Combine(baseDir, "assets"));
        SetDefault("overlay_directory", Path.Combine(baseDir, "overlays"));
        SetDefault("cheat_database_path", Path.Combine(baseDir, "cheats"));
        SetDefault("playlist_directory", Path.Combine(baseDir, "playlists"));
        SetDefault("thumbnails_directory", Path.Combine(baseDir, "thumbnails"));
        SetDefault("core_assets_directory", Path.Combine(baseDir, "downloads"));
        SetDefault("rgui_browser_directory", baseDir);

        // 16. USER
        SetDefault("netplay_nickname", "StormPlayer");
        SetDefault("user_privacy_mode", "false");
        SetDefault("user_language", "9"); // Russian
    }

    private static void SetDefault(string key, string val)
    {
        if (!_config.ContainsKey(key))
        {
            _config[key] = val;
        }
    }

    public static string GetString(string key, string defaultValue = "")
    {
        lock (_lock)
        {
            if (_config.TryGetValue(key, out var val))
            {
                return val;
            }
            return defaultValue;
        }
    }

    public static void Set(string key, string value) => SetString(key, value);

    public static void SetString(string key, string value)
    {
        lock (_lock)
        {
            _config[key] = value ?? string.Empty;
        }
    }

    public static bool GetBool(string key, bool defaultValue = false)
    {
        lock (_lock)
        {
            if (_config.TryGetValue(key, out var val))
            {
                if (bool.TryParse(val, out bool b)) return b;
                if (val == "1") return true;
                if (val == "0") return false;
            }
            return defaultValue;
        }
    }

    public static void SetBool(string key, bool value)
    {
        lock (_lock)
        {
            _config[key] = value ? "true" : "false";
        }
    }

    public static int GetInt(string key, int defaultValue = 0)
    {
        lock (_lock)
        {
            if (_config.TryGetValue(key, out var val))
            {
                if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                    return i;
            }
            return defaultValue;
        }
    }

    public static void SetInt(string key, int value)
    {
        lock (_lock)
        {
            _config[key] = value.ToString(CultureInfo.InvariantCulture);
        }
    }

    public static float GetFloat(string key, float defaultValue = 0f)
    {
        lock (_lock)
        {
            if (_config.TryGetValue(key, out var val))
            {
                if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                    return f;
            }
            return defaultValue;
        }
    }

    public static void SetFloat(string key, float value)
    {
        lock (_lock)
        {
            _config[key] = value.ToString("F6", CultureInfo.InvariantCulture);
        }
    }

    public static Dictionary<string, string> GetAllSettings()
    {
        lock (_lock)
        {
            return new Dictionary<string, string>(_config, StringComparer.OrdinalIgnoreCase);
        }
    }
}
