using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace StormRetroArch;

public class CoreOptionItem : INotifyPropertyChanged
{
    private string _value = "";

    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string CoreName { get; set; } = "";
    public List<string> Options { get; set; } = new();

    public string Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                OnPropertyChanged();
            }
        }
    }

    public void EnsureValueInOptions()
    {
        if (Options == null) Options = new();
        if (Options.Count == 0)
        {
            if (!string.IsNullOrEmpty(Value)) Options.Add(Value);
            else Options.Add("enabled");
        }

        if (string.IsNullOrEmpty(Value))
        {
            Value = Options[0];
            return;
        }

        if (!Options.Contains(Value))
        {
            var match = Options.FirstOrDefault(o => o.StartsWith(Value, StringComparison.OrdinalIgnoreCase) || Value.StartsWith(o, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                Value = match;
            }
            else
            {
                Options.Insert(0, Value);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class CoreProfileItem
{
    public string CoreName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Icon { get; set; } = "🎛️";
    public List<CoreOptionItem> Options { get; set; } = new();
}

public static class CoreOptionsAndOverridesManager
{
    public static readonly string ConfigDir = Path.Combine(CoreManager.AppDir, "config");
    public static readonly string ShadersDir = Path.Combine(CoreManager.AppDir, "shaders");

    public static List<CoreProfileItem> GetCoreProfiles()
    {
        var profiles = new List<CoreProfileItem>();

        // 1. SwanStation (PS1)
        profiles.Add(new CoreProfileItem
        {
            CoreName = "SwanStation", DisplayName = "SwanStation (Sony PlayStation 1)", Platform = "Sony PS1", Icon = "🎮",
            Options = new()
            {
                new() { Key = "swanstation_ResolutionScale", Title = "Внутреннее разрешение 3D", Description = "Апскейл геометрии и полигонов (1x..8x 4K)", CoreName = "SwanStation", Value = "4x (1440p)", Options = new() { "1x (Native 240p)", "2x (720p)", "3x (1080p)", "4x (1440p)", "8x (4K UHD)" } },
                new() { Key = "swanstation_PGXP_Enable", Title = "PGXP коррекция геометрии", Description = "Устранение дрожания текстур и полигонов оригинальной PS1", CoreName = "SwanStation", Value = "enabled", Options = new() { "disabled", "enabled", "memory_only" } },
                new() { Key = "swanstation_PGXP_TextureCorrection", Title = "PGXP коррекция перспективы текстур", Description = "Сглаживание растяжения текстур в 3D пространстве", CoreName = "SwanStation", Value = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "swanstation_WidescreenHack", Title = "Широкоэкранный хак (16:9 Widescreen)", Description = "Рендеринг 3D-мира в формате 16:9 без растяжения 2D элементов", CoreName = "SwanStation", Value = "disabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "swanstation_TextureFilter", Title = "Фильтрация текстур", Description = "Интерполяция пикселей поверхностей", CoreName = "SwanStation", Value = "xBR", Options = new() { "Nearest", "Bilinear", "xBR", "Jinc2" } },
                new() { Key = "swanstation_CPUOverclock", Title = "Разгон процессора R3000A", Description = "Устранение просадок кадров в тяжелых сценах", CoreName = "SwanStation", Value = "100%", Options = new() { "100%", "150%", "200%", "300%" } }
            }
        });

        // 2. Mupen64Plus-Next (N64)
        profiles.Add(new CoreProfileItem
        {
            CoreName = "Mupen64Plus-Next", DisplayName = "Mupen64Plus-Next (Nintendo 64)", Platform = "Nintendo 64", Icon = "🕹️",
            Options = new()
            {
                new() { Key = "mupen64plus-next-rdp-plugin", Title = "Графический плагин RDP", Description = "GLideN64 с аппаратным ускорением или пиксельно точный ParaLLEl", CoreName = "Mupen64Plus-Next", Value = "GLideN64", Options = new() { "GLideN64", "Parallel", "Angrylion" } },
                new() { Key = "mupen64plus-next-43screensize", Title = "Внутреннее разрешение", Description = "Разрешение рендеринга 3D мира N64", CoreName = "Mupen64Plus-Next", Value = "1920x1440", Options = new() { "640x480", "1280x960", "1920x1440", "2560x1920" } },
                new() { Key = "mupen64plus-next-aspect", Title = "Соотношение сторон", Description = "Формат кадра N64", CoreName = "Mupen64Plus-Next", Value = "4:3", Options = new() { "4:3", "16:9 adjusted", "16:9 widescreen hack" } },
                new() { Key = "mupen64plus-next-cpucore", Title = "Ядро CPU", Description = "Динамическая рекомпиляция", CoreName = "Mupen64Plus-Next", Value = "dynamic_recompiler", Options = new() { "dynamic_recompiler", "pure_interpreter" } }
            }
        });

        // 3. Flycast (Dreamcast / NAOMI)
        profiles.Add(new CoreProfileItem
        {
            CoreName = "Flycast", DisplayName = "Flycast (Sega Dreamcast / NAOMI)", Platform = "Sega Dreamcast", Icon = "🌀",
            Options = new()
            {
                new() { Key = "flycast_internal_resolution", Title = "Внутреннее разрешение", Description = "Апскейл графики PowerVR2", CoreName = "Flycast", Value = "1920x1440", Options = new() { "640x480", "1280x960", "1920x1440", "2560x1920", "3840x2880" } },
                new() { Key = "flycast_widescreen_hack", Title = "Широкоэкранный хак 16:9", Description = "Расширение поля зрения FOV для широкоформатных дисплеев", CoreName = "Flycast", Value = "disabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "flycast_anisotropic_filtering", Title = "Анизотропная фильтрация", Description = "Чёткость текстур под острыми углами", CoreName = "Flycast", Value = "16x", Options = new() { "off", "2x", "4x", "8x", "16x" } },
                new() { Key = "flycast_texupscale", Title = "Апскейл текстур xBRZ", Description = "Увеличение чёткости 2D спрайтов и шрифтов", CoreName = "Flycast", Value = "off", Options = new() { "off", "2x", "4x" } }
            }
        });

        // 4. PPSSPP (PSP)
        profiles.Add(new CoreProfileItem
        {
            CoreName = "PPSSPP", DisplayName = "PPSSPP (Sony PlayStation Portable)", Platform = "Sony PSP", Icon = "📱",
            Options = new()
            {
                new() { Key = "ppsspp_internal_resolution", Title = "Разрешение рендеринга", Description = "Кратность относительно родного экрана 480x272", CoreName = "PPSSPP", Value = "4x", Options = new() { "1x (480x272)", "2x (720p)", "3x (1080p)", "4x (1440p)", "5x (4K)" } },
                new() { Key = "ppsspp_texture_scaling_type", Title = "Алгоритм масштабирования текстур", Description = "Векторное сглаживание текстур", CoreName = "PPSSPP", Value = "xBRZ", Options = new() { "off", "xBRZ", "Hybrid", "Bicubic" } },
                new() { Key = "ppsspp_anisotropic_filtering", Title = "Анизотропная фильтрация", Description = "Максимальная резкость дорожного полотна и полов", CoreName = "PPSSPP", Value = "16x", Options = new() { "off", "2x", "4x", "8x", "16x" } }
            }
        });

        // 5. melonDS DS (Nintendo DS)
        profiles.Add(new CoreProfileItem
        {
            CoreName = "melonDS DS", DisplayName = "melonDS DS (Nintendo DS / DSi)", Platform = "Nintendo DS", Icon = "🕹️",
            Options = new()
            {
                new() { Key = "melonds_screen_layout", Title = "Расположение двух экранов", Description = "Ориентация дисплеев NDS", CoreName = "melonDS DS", Value = "Top/Bottom", Options = new() { "Top/Bottom", "Left/Right", "Top Only", "Bottom Only", "Hybrid 3:1" } },
                new() { Key = "melonds_render_3d", Title = "3D Рендерер", Description = "Аппаратный OpenGL или пиксельно точный программный", CoreName = "melonDS DS", Value = "OpenGL", Options = new() { "Software", "OpenGL" } },
                new() { Key = "melonds_jit_enable", Title = "JIT Компилятор", Description = "Сверхбыстрое выполнение кода ARM9/ARM7", CoreName = "melonDS DS", Value = "enabled", Options = new() { "disabled", "enabled" } }
            }
        });

        // 6. mGBA (Game Boy Advance)
        profiles.Add(new CoreProfileItem
        {
            CoreName = "mGBA", DisplayName = "mGBA (Game Boy Advance)", Platform = "Game Boy Advance", Icon = "🕹️",
            Options = new()
            {
                new() { Key = "mgba_color_correction", Title = "Цветокоррекция экрана", Description = "Воссоздание гаммы дисплея GBA SP AGS-101", CoreName = "mGBA", Value = "GBA", Options = new() { "OFF", "GBA", "GBC", "NDS" } },
                new() { Key = "mgba_interframe_blending", Title = "Межкадровое смешивание", Description = "Эмуляция послесвечения ЖК-матрицы", CoreName = "mGBA", Value = "smart", Options = new() { "OFF", "smart", "ghosting" } },
                new() { Key = "mgba_sound_interpolation", Title = "Интерполяция звука", Description = "Сглаживание ступенчатого аудиопотока", CoreName = "mGBA", Value = "enabled", Options = new() { "disabled", "enabled" } }
            }
        });

        // 7. Snes9x (Super Nintendo)
        profiles.Add(new CoreProfileItem
        {
            CoreName = "Snes9x", DisplayName = "Snes9x (Super Nintendo / Super Famicom)", Platform = "SNES", Icon = "🎮",
            Options = new()
            {
                new() { Key = "snes9x_hires_blend", Title = "Hi-Res Blending", Description = "Сглаживание полупрозрачности в высоком разрешении 512x448", CoreName = "Snes9x", Value = "merge", Options = new() { "disabled", "merge", "blur" } },
                new() { Key = "snes9x_overclock_cycles", Title = "Разгон CPU Super FX", Description = "Устранение тормозов в Star Fox, Doom, Yoshi's Island", CoreName = "Snes9x", Value = "100%", Options = new() { "100%", "150%", "200%", "300%" } },
                new() { Key = "snes9x_region", Title = "Регион эмуляции", Description = "Автоматический выбор NTSC 60Hz / PAL 50Hz", CoreName = "Snes9x", Value = "Auto", Options = new() { "Auto", "NTSC", "PAL" } }
            }
        });

        // 8. FCEUmm (NES / Dendy)
        profiles.Add(new CoreProfileItem
        {
            CoreName = "FCEUmm", DisplayName = "FCEUmm (Nintendo Entertainment System / Dendy)", Platform = "NES", Icon = "🕹️",
            Options = new()
            {
                new() { Key = "fceumm_palette", Title = "Цветовая палитра", Description = "Палитра Dendy, NES Classic или аутентичная NTSC", CoreName = "FCEUmm", Value = "dendy", Options = new() { "default", "dendy", "composite-direct-fbx", "sony-cxa2025as-us", "nes-classic" } },
                new() { Key = "fceumm_overclocking", Title = "Оверклокинг процессора 2A03", Description = "Устранение мигания спрайтов и лагов", CoreName = "FCEUmm", Value = "2x-postrender", Options = new() { "disabled", "2x-postrender", "2x-vblank" } }
            }
        });

        // 9. Genesis Plus GX (Sega Mega Drive)
        profiles.Add(new CoreProfileItem
        {
            CoreName = "Genesis Plus GX", DisplayName = "Genesis Plus GX (Sega Genesis / Mega Drive)", Platform = "Sega Genesis", Icon = "🎮",
            Options = new()
            {
                new() { Key = "genesis_plus_gx_ym2612", Title = "Эмуляция синтезатора YM2612", Description = "Звуковой чип FM-синтеза (MAME или улучшенный Nuked OPN2)", CoreName = "Genesis Plus GX", Value = "mame (enhanced)", Options = new() { "mame (enhanced)", "nuked (discrete)" } },
                new() { Key = "genesis_plus_gx_audio_filter", Title = "Низкочастотный аудиофильтр", Description = "Аутентичный тёплый аналоговый звук ревизии Model 1", CoreName = "Genesis Plus GX", Value = "low-pass", Options = new() { "disabled", "low-pass" } },
                new() { Key = "genesis_plus_gx_overclock", Title = "Разгон Motorola 68000", Description = "Плавная работа игр без просадок кадровой частоты", CoreName = "Genesis Plus GX", Value = "100%", Options = new() { "100%", "125%", "150%", "200%" } }
            }
        });

        // 10. DOSBox-Pure (MS-DOS)
        profiles.Add(new CoreProfileItem
        {
            CoreName = "DOSBox-Pure", DisplayName = "DOSBox-Pure (MS-DOS PC)", Platform = "PC / DOS", Icon = "💾",
            Options = new()
            {
                new() { Key = "dosbox_pure_cycles", Title = "Циклы процессора x86", Description = "Тактовая частота виртуального ПК (286, 386, 486, Pentium)", CoreName = "DOSBox-Pure", Value = "auto", Options = new() { "auto", "3000 (286)", "15000 (386)", "45000 (486)", "max" } },
                new() { Key = "dosbox_pure_soundblaster", Title = "Звуковая карта Sound Blaster", Description = "Эмуляция стандарта аудио DOS", CoreName = "DOSBox-Pure", Value = "sb16", Options = new() { "none", "sb2", "sbpro2", "sb16", "gravis" } },
                new() { Key = "dosbox_pure_midi", Title = "MIDI Синтезатор", Description = "Аудиофильская музыка Roland Sound Canvas / MT-32", CoreName = "DOSBox-Pure", Value = "sc-55", Options = new() { "disabled", "mt-32", "sc-55", "general_midi" } }
            }
        });

        foreach (var p in profiles)
        {
            foreach (var opt in p.Options)
            {
                opt.EnsureValueInOptions();
            }
        }
        return profiles;
    }

    public static void LoadCoreOptionsFromDisk(string coreName, IEnumerable<CoreOptionItem> options)
    {
        string optPath = Path.Combine(ConfigDir, coreName, coreName + ".opt");
        if (!File.Exists(optPath)) return;

        try
        {
            var lines = File.ReadAllLines(optPath, Encoding.UTF8);
            var map = new Dictionary<string, string>();
            foreach (var l in lines)
            {
                int eq = l.IndexOf('=');
                if (eq > 0)
                {
                    string k = l.Substring(0, eq).Trim();
                    string v = l.Substring(eq + 1).Trim().Trim('"');
                    map[k] = v;
                }
            }

            foreach (var opt in options)
            {
                if (map.TryGetValue(opt.Key, out var diskVal))
                {
                    opt.Value = diskVal;
                }
            }
        }
        catch { }
    }

    public static void SaveCoreOptionsToDisk(string coreName, IEnumerable<CoreOptionItem> options)
    {
        string coreDir = Path.Combine(ConfigDir, coreName);
        if (!Directory.Exists(coreDir)) Directory.CreateDirectory(coreDir);

        string optPath = Path.Combine(coreDir, coreName + ".opt");
        var sb = new StringBuilder();

        foreach (var opt in options)
        {
            sb.AppendLine($"{opt.Key} = \"{opt.Value}\"");
        }

        File.WriteAllText(optPath, sb.ToString(), Encoding.UTF8);
    }

    public static void SaveCoreOverrideCfg(string coreName, Dictionary<string, string> overrideSettings)
    {
        string coreDir = Path.Combine(ConfigDir, coreName);
        if (!Directory.Exists(coreDir)) Directory.CreateDirectory(coreDir);

        string cfgPath = Path.Combine(coreDir, coreName + ".cfg");
        var sb = new StringBuilder();

        foreach (var kvp in overrideSettings)
        {
            sb.AppendLine($"{kvp.Key} = \"{kvp.Value}\"");
        }

        File.WriteAllText(cfgPath, sb.ToString(), Encoding.UTF8);
    }

    public static void ResetCoreOptions(string coreName)
    {
        string optPath = Path.Combine(ConfigDir, coreName, coreName + ".opt");
        if (File.Exists(optPath))
        {
            try { File.Delete(optPath); } catch { }
        }
    }

    // Custom Shader Chain Builder
    public static string SaveCustomShaderChain(string pass1Rel, string pass2Rel, float curvature, float bloom, float sharpness, float scanlines)
    {
        string customChainPath = Path.Combine(ShadersDir, "storm_custom_chain.slangp");

        var sb = new StringBuilder();
        sb.AppendLine("shaders = 2");
        sb.AppendLine($"shader0 = \"{pass1Rel}\"");
        sb.AppendLine("filter_linear0 = false");
        sb.AppendLine("scale_type0 = source");
        sb.AppendLine("scale0 = 2.0");
        sb.AppendLine($"shader1 = \"{pass2Rel}\"");
        sb.AppendLine("filter_linear1 = true");
        sb.AppendLine($"parameters = \"CURVATURE;BLOOM;SHARPNESS;SCANLINE_WEIGHT\"");
        sb.AppendLine($"CURVATURE = \"{curvature:0.000000}\"");
        sb.AppendLine($"BLOOM = \"{bloom:0.000000}\"");
        sb.AppendLine($"SHARPNESS = \"{sharpness:0.000000}\"");
        sb.AppendLine($"SCANLINE_WEIGHT = \"{scanlines:0.000000}\"");

        File.WriteAllText(customChainPath, sb.ToString(), Encoding.UTF8);

        // Apply to retroarch.cfg
        RetroArchConfigManager.SetString("video_shader_enable", "true");
        RetroArchConfigManager.SetString("video_shader", customChainPath);
        RetroArchConfigManager.Save();

        return customChainPath;
    }
}
