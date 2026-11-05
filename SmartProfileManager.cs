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
    public string IconPathData { get; set; } = "M12 2A10 10 0 0 0 2 12a10 10 0 0 0 10 10 10 10 0 0 0 10-10A10 10 0 0 0 12 2z";
    public string IconBrush { get; set; } = "#8B5CF6";
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
            IconPathData = "M15 7.5V2H9v5.5H3.5v6H9V19h6v-5.5h5.5v-6H15z",
            IconBrush = "#A78BFA",
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
            Name = "32/64/128-бит 3D (HD Upscale)",
            IconPathData = "M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5",
            IconBrush = "#38BDF8",
            Era = "PS1, N64, Dreamcast, Saturn, PSP, Dolphin",
            Description = "Аппаратное ускорение Vulkan/GLCore, внутреннее разрешение 2x-4x HD, шейдер сглаживания FXAA/SMAA, Widescreen 16:9, тройная буферизация 60 FPS.",
            VideoDriver = "vulkan",
            RunAheadFrames = 0,
            RunAheadSecondaryInstance = false,
            ShaderPreset = "anti-aliasing/smaa.slangp",
            AspectRatioIndex = 1, // 16:9
            IntegerScale = false,
            Vsync = true,
            HardGpuSync = false,
            InternalResScale = 3,
            WidescreenHack = true,
            BezelStyle = "None"
        },
        new SmartProfile
        {
            Id = "handheld",
            Name = "Портативные (Pixel-Grid LCD)",
            IconPathData = "M17 1.01L7 1c-1.1 0-2 .9-2 2v18c0 1.1.9 2 2 2h10c1.1 0 2-.9 2-2V3c0-1.1-.9-1.99-2-1.99zM17 19H7V5h10v14z",
            IconBrush = "#34D399",
            Era = "Game Boy, GBA, Nintendo DS, Game Gear, Neo Geo Pocket, PSP",
            Description = "Пиксельная сетка ЖК-экрана, целочисленное масштабирование Integer Scale, точная гамма-калибровка цветов и соотношение сторон оригинального экрана.",
            VideoDriver = "vulkan",
            RunAheadFrames = 1,
            RunAheadSecondaryInstance = false,
            ShaderPreset = "handheld/gameboy-dmg.slangp",
            AspectRatioIndex = 21, // Core Provided
            IntegerScale = true,
            Vsync = true,
            HardGpuSync = true,
            InternalResScale = 1,
            WidescreenHack = false,
            BezelStyle = "Handheld Frame"
        },
        new SmartProfile
        {
            Id = "arcade",
            Name = "Аркады (CRT Tube & Scanlines)",
            IconPathData = "M12 2a4 4 0 0 0-4 4c0 1.5.8 2.8 2 3.5V13H8v2h8v-2h-2V9.5c1.2-.7 2-2 2-3.5a4 4 0 0 0-4-4zm-8 15v5h16v-5H4z",
            IconBrush = "#F59E0B",
            Era = "MAME, FBNeo, CPS-1/2/3, Neo Geo MVS, Naomi",
            Description = "Аутентичный аркадный кинескоп: глубокие сканлинии, закругление углов, боковые панели аркадного кабинета Bezel и нулевая задержка кнопок.",
            VideoDriver = "vulkan",
            RunAheadFrames = 1,
            RunAheadSecondaryInstance = true,
            ShaderPreset = "crt/crt-geom.slangp",
            AspectRatioIndex = 0, // 4:3
            IntegerScale = false,
            Vsync = true,
            HardGpuSync = true,
            InternalResScale = 1,
            WidescreenHack = false,
            BezelStyle = "Arcade Cabinet"
        }
    };

    public static IReadOnlyList<SmartProfile> Profiles => _profiles;
    public static IReadOnlyList<SmartProfile> GetAllProfiles() => _profiles;

    public static SmartProfile DetectProfile(string romFileName, string coreName)
    {
        string ext = Path.GetExtension(romFileName).ToLowerInvariant();
        string core = coreName.ToLowerInvariant();

        if (core.Contains("swanstation") || core.Contains("psx") || core.Contains("mupen") ||
            core.Contains("flycast") || core.Contains("saturn") || core.Contains("dolphin") ||
            core.Contains("ppsspp") || core.Contains("citra") ||
            ext is ".iso" or ".cso" or ".chd" or ".cdi" or ".gdi" or ".rvz" or ".n64" or ".z64")
        {
            return _profiles[1]; // 3D
        }

        if (core.Contains("mgba") || core.Contains("gambatte") || core.Contains("melonds") ||
            core.Contains("gearboy") || core.Contains("gearsystem") || core.Contains("vba") ||
            ext is ".gb" or ".gbc" or ".gba" or ".nds" or ".dsi" or ".gg" or ".ws" or ".wsc" or ".ngp")
        {
            return _profiles[2]; // Handheld
        }

        if (core.Contains("fbneo") || core.Contains("mame") ||
            ext is ".zip" or ".7z")
        {
            return _profiles[3]; // Arcade
        }

        return _profiles[0]; // 2D Default
    }

    public static string ApplyProfile(SmartProfile profile, string retroArchDir)
    {
        string cfgPath = Path.Combine(retroArchDir, "retroarch.cfg");
        if (!File.Exists(cfgPath)) return "Файл retroarch.cfg не найден.";

        RetroArchConfigManager.SetString("video_driver", profile.VideoDriver);
        RetroArchConfigManager.SetInt("run_ahead_frames", profile.RunAheadFrames);
        RetroArchConfigManager.SetBool("run_ahead_enabled", profile.RunAheadFrames > 0);
        RetroArchConfigManager.SetBool("run_ahead_secondary_instance", profile.RunAheadSecondaryInstance);
        RetroArchConfigManager.SetInt("aspect_ratio_index", profile.AspectRatioIndex);
        RetroArchConfigManager.SetBool("video_scale_integer", profile.IntegerScale);
        RetroArchConfigManager.SetBool("video_vsync", profile.Vsync);
        RetroArchConfigManager.SetBool("video_hard_sync", profile.HardGpuSync);

        string shaderFull = Path.Combine(retroArchDir, "shaders", "shaders_slang", profile.ShaderPreset);
        if (File.Exists(shaderFull))
        {
            RetroArchConfigManager.SetBool("video_shader_enable", true);
            RetroArchConfigManager.SetString("video_shader_dir", shaderFull);
        }

        RetroArchConfigManager.Save();
        return $"Профиль «{profile.Name}» успешно применён в retroarch.cfg!";
    }
}
