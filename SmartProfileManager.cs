using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StormRetroArch;

/// <summary>
/// Профиль аппаратной и визуальной оптимизации для разных эпох платформ.
/// </summary>
public class SmartProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "🎮";
    public string Era { get; set; } = "";
    public string Description { get; set; } = "";
    public string VideoDriver { get; set; } = "vulkan";
    public int RunAheadFrames { get; set; } = 1;
    public bool RunAheadSecondaryInstance { get; set; } = true;
    public string ShaderPreset { get; set; } = "crt/crt-easymode.slangp";
    public int AspectRatioIndex { get; set; } = 0; // 0 = 4:3, 1 = 16:9, 21 = Core
    public bool IntegerScale { get; set; } = false;
    public bool Vsync { get; set; } = true;
    public bool HardGpuSync { get; set; } = true;
    public int InternalResScale { get; set; } = 1;
    public bool WidescreenHack { get; set; } = false;
    public string BezelStyle { get; set; } = "CRT Curve";
}

/// <summary>
/// Менеджер умных профилей Smart Per-System Profile.
/// Автоматически определяет эпоху консоли и на лету оптимизирует видеодрайвер, Run-Ahead, шейдеры и масштабирование.
/// </summary>
public static class SmartProfileManager
{
    private static readonly List<SmartProfile> _profiles = new()
    {
        new SmartProfile
        {
            Id = "retro_2d",
            Name = "8/16-бит 2D (Zero-Latency)",
            Icon = "🕹️",
            Era = "NES, SNES, Genesis, SMS, Turbografx, GBC",
            Description = "Ультра-отзывчивый ввод: Run-Ahead 1 кадр (упреждение задержки), шейдер CRT-Easymode, точные пропорции 4:3, идеальная синхронизация звука и видео.",
            VideoDriver = "vulkan",
            RunAheadFrames = 1,
            RunAheadSecondaryInstance = true,
            ShaderPreset = "crt/crt-easymode.slangp",
            AspectRatioIndex = 0,
            IntegerScale = false,
            Vsync = true,
            HardGpuSync = true,
            InternalResScale = 1,
            WidescreenHack = false,
            BezelStyle = "CRT Curve"
        },
        new SmartProfile
        {
            Id = "retro_3d",
            Name = "32/64/128-бит 3D (HD Enhancer)",
            Icon = "🧊",
            Era = "PS1, N64, Saturn, Dreamcast, PS2, GameCube",
            Description = "Максимальное качество 3D-графики: драйвер Vulkan, апскейл геометрии до 2x-4x, широкоформатный хак 16:9, сглаживание текстур и калибровка 60 FPS.",
            VideoDriver = "vulkan",
            RunAheadFrames = 0, // 3D тяжёлое для Run-Ahead
            RunAheadSecondaryInstance = false,
            ShaderPreset = "crt/crt-royale.slangp",
            AspectRatioIndex = 1, // 16:9
            IntegerScale = false,
            Vsync = true,
            HardGpuSync = false,
            InternalResScale = 3,
            WidescreenHack = true,
            BezelStyle = "Arcade Clean"
        },
        new SmartProfile
        {
            Id = "portable_lcd",
            Name = "Портативные системы (LCD Grid)",
            Icon = "📱",
            Era = "Game Boy, GBA, Game Gear, PSP, NDS, Lynx",
            Description = "Аутентичный портативный экран: пиксельная сетка LCD3x / LCD-Grid, цветокоррекция палитры ЖК-экрана, целочисленное масштабирование и стилизованный корпус.",
            VideoDriver = "vulkan",
            RunAheadFrames = 1,
            RunAheadSecondaryInstance = false,
            ShaderPreset = "handheld/lcd-grid.slangp",
            AspectRatioIndex = 21, // Core Provided
            IntegerScale = true,
            Vsync = true,
            HardGpuSync = true,
            InternalResScale = 1,
            WidescreenHack = false,
            BezelStyle = "Handheld GBA"
        },
        new SmartProfile
        {
            Id = "arcade_dos",
            Name = "Аркады и MS-DOS (15kHz CRT)",
            Icon = "🎰",
            Era = "Arcade, MAME, FBNeo, MS-DOS, PC-98, Amiga",
            Description = "Аркадный автомат и ретро-ПК: CRT-Aperture сканлайны 15kHz, чистый звук без дрожания частоты, быстрый маппинг кнопок Монета/Старт и VFS диск.",
            VideoDriver = "vulkan",
            RunAheadFrames = 1,
            RunAheadSecondaryInstance = true,
            ShaderPreset = "crt/crt-aperture.slangp",
            AspectRatioIndex = 0, // 4:3
            IntegerScale = false,
            Vsync = true,
            HardGpuSync = true,
            InternalResScale = 1,
            WidescreenHack = false,
            BezelStyle = "Arcade Clean"
        }
    };

    public static List<SmartProfile> GetAllProfiles() => _profiles;

    /// <summary>
    /// Автоматическое определение профиля по названию платформы или ядра.
    /// </summary>
    public static SmartProfile DetectProfile(string platformName, string coreName = "")
    {
        string p = (platformName + " " + coreName).ToLowerInvariant();

        // 1. Portable LCD
        if (p.Contains("game boy") || p.Contains("gb") || p.Contains("gba") || p.Contains("game gear") ||
            p.Contains("handheld") || p.Contains("psp") || p.Contains("nds") || p.Contains("lynx") ||
            p.Contains("wonderswan") || p.Contains("ngpc") || p.Contains("pocket") || p.Contains("mgba"))
        {
            return _profiles.First(x => x.Id == "portable_lcd");
        }

        // 2. 3D Era
        if (p.Contains("ps1") || p.Contains("psx") || p.Contains("playstation") || p.Contains("n64") ||
            p.Contains("saturn") || p.Contains("dreamcast") || p.Contains("ps2") || p.Contains("gamecube") ||
            p.Contains("beetle_psx") || p.Contains("duckstation") || p.Contains("mupen64") || p.Contains("flycast") ||
            p.Contains("pcsx2") || p.Contains("dolphin") || p.Contains("yabasanshiro"))
        {
            return _profiles.First(x => x.Id == "retro_3d");
        }

        // 3. Arcade & DOS
        if (p.Contains("arcade") || p.Contains("mame") || p.Contains("fbneo") || p.Contains("dos") ||
            p.Contains("dosbox") || p.Contains("pc98") || p.Contains("amiga") || p.Contains("puae"))
        {
            return _profiles.First(x => x.Id == "arcade_dos");
        }

        // 4. Default 2D Zero-Latency (NES, SNES, Genesis, SMS, PC Engine, etc.)
        return _profiles.First(x => x.Id == "retro_2d");
    }

    /// <summary>
    /// Применение настроек профиля к конфигурации retroarch.cfg.
    /// </summary>
    public static string ApplyProfile(SmartProfile profile, string retroArchDir = "")
    {
        try
        {
            RetroArchConfigManager.SetString("video_driver", profile.VideoDriver);
            RetroArchConfigManager.SetString("run_ahead_enabled", profile.RunAheadFrames > 0 ? "true" : "false");
            RetroArchConfigManager.SetString("run_ahead_frames", profile.RunAheadFrames.ToString());
            RetroArchConfigManager.SetString("run_ahead_secondary_instance", profile.RunAheadSecondaryInstance ? "true" : "false");
            RetroArchConfigManager.SetString("video_shader_enable", "true");
            RetroArchConfigManager.SetString("video_aspect_ratio_auto", profile.AspectRatioIndex == 21 ? "true" : "false");
            RetroArchConfigManager.SetString("aspect_ratio_index", profile.AspectRatioIndex.ToString());
            RetroArchConfigManager.SetString("video_scale_integer", profile.IntegerScale ? "true" : "false");
            RetroArchConfigManager.SetString("video_vsync", profile.Vsync ? "true" : "false");
            RetroArchConfigManager.SetString("video_hard_sync", profile.HardGpuSync ? "true" : "false");

            // Применяем шейдер если найден
            ShaderManager.ApplyShaderPreset(profile.ShaderPreset);

            // Сохраняем retroarch.cfg
            RetroArchConfigManager.Save();

            return $"Профиль «{profile.Name}» успешно активирован!\n• Драйвер: {profile.VideoDriver.ToUpper()}\n• Run-Ahead: {(profile.RunAheadFrames > 0 ? profile.RunAheadFrames + " кадр." : "Выкл")}\n• Шейдер: {Path.GetFileName(profile.ShaderPreset)}\n• Пропорции: {(profile.AspectRatioIndex == 1 ? "16:9" : profile.AspectRatioIndex == 0 ? "4:3" : "Авто")}";
        }
        catch (Exception ex)
        {
            return $"Ошибка применения профиля: {ex.Message}";
        }
    }
}
