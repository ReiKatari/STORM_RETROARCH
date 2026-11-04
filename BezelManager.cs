using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StormRetroArch;

public class BezelPresetItem : INotifyPropertyChanged
{
    private bool _isActive;
    public string Name { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Description { get; set; } = "";
    public string CfgPath { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public string Icon { get; set; } = "🖼️";
    public string AspectRatio { get; set; } = "4:3 CRT";

    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class BezelManager
{
    public static readonly string OverlaysDir = Path.Combine(CoreManager.AppDir, "overlays");
    public static readonly string BordersDir = Path.Combine(OverlaysDir, "borders");

    public record BuiltinBezelDef(string Name, string Platform, string Icon, string Description, string BaseColorHex, string AccentColorHex);

    public static readonly List<BuiltinBezelDef> BuiltinTemplates = new()
    {
        new("Sony PlayStation 1 Bezel", "Sony PlayStation 1", "🎮", "Аутентичная тёмно-серая рамка PS1 с неоновым контуром 4:3 окна и матовыми боковинами", "#1C1C24", "#003791"),
        new("Super Nintendo (SNES) Bezel", "Super Nintendo (SNES)", "🎮", "Фирменные цвета Super Famicom / SNES с лавандовыми акцентами и CRT-затенением", "#22222E", "#7C3AED"),
        new("Nintendo NES / Famicom Bezel", "Nintendo Entertainment System", "🎮", "Классический 8-битный ретро-стиль с тёмно-бордовыми акцентами Famicom", "#1A1A22", "#E11D48"),
        new("Sega Mega Drive / Genesis Bezel", "Sega Genesis / Mega Drive", "🎮", "Чёрная текстурированная рамка 16-BIT с золотым тиснением High Definition Graphics", "#121218", "#EAB308"),
        new("Nintendo 64 Ultra Bezel", "Nintendo 64", "🎮", "Элегантная полупрозрачная рамка с объёмным логотипом и кинематографичными тенями", "#181824", "#2563EB"),
        new("Game Boy DMG-01 Matrix Bezel", "Game Boy / Color", "🕹️", "Аутентичный оливково-серый корпус оригинального Game Boy с текстурой матрицы", "#282C22", "#65A30D"),
        new("Game Boy Advance Wide Bezel", "Game Boy Advance", "🕹️", "Широкоформатная индиго-рамка GBA с плавным градиентом и индикатором питания", "#1E1B38", "#8B5CF6"),
        new("Nintendo DS Dual-Bezel", "Nintendo DS / DSi", "🕹️", "Стильный двухэкранный дизайн с серебристыми боковыми стойками", "#22252A", "#06B6D4"),
        new("Sega Saturn Titanium Bezel", "Sega Saturn", "🪐", "Тёмно-титановый корпус Sega Saturn с неоновыми синими акцентами", "#181A20", "#38BDF8"),
        new("Sega Dreamcast Spiral Bezel", "Sega Dreamcast", "🌀", "Футуристичный белый и оранжевый стиль консоли Dreamcast", "#20222A", "#F97316"),
        new("Arcade Cabinet CRT Bezel", "Arcade Classics", "🕹️", "Истинный аркадный кабинет с заклёпками, виниловым блеском и сканлайн-тенью", "#14141A", "#EF4444"),
        new("STORM Midnight Frame Bezel", "STORM Exclusive", "✨", "Эксклюзивная тёмно-фиолетовая неоновая рамка AURORA FLUID с CRT-бликами", "#0D0B18", "#A855F7")
    };

    static BezelManager()
    {
        try
        {
            if (!Directory.Exists(BordersDir)) Directory.CreateDirectory(BordersDir);
            EnsureBuiltinBezels();
        }
        catch { }
    }

    public static List<BezelPresetItem> ScanBezels()
    {
        EnsureBuiltinBezels();
        var list = new List<BezelPresetItem>();

        try
        {
            string currentOverlay = RetroArchConfigManager.GetString("input_overlay", "");
            bool overlayEnabled = RetroArchConfigManager.GetBool("input_overlay_enable", false);

            if (Directory.Exists(BordersDir))
            {
                var cfgFiles = Directory.GetFiles(BordersDir, "*.cfg", SearchOption.AllDirectories);
                foreach (var cfg in cfgFiles)
                {
                    string name = Path.GetFileNameWithoutExtension(cfg).Replace('_', ' ');
                    string png = Path.ChangeExtension(cfg, ".png");

                    var matchTmpl = BuiltinTemplates.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                                                                         cfg.Contains(t.Platform.Split(' ')[0]));

                    string platform = matchTmpl?.Platform ?? "Custom Bezel";
                    string icon = matchTmpl?.Icon ?? "🖼️";
                    string desc = matchTmpl?.Description ?? $"Пользовательская рамка Bezel для экрана 16:9 ({Path.GetFileName(cfg)})";

                    bool isActive = overlayEnabled && !string.IsNullOrEmpty(currentOverlay) &&
                                    (currentOverlay.Equals(cfg, StringComparison.OrdinalIgnoreCase) ||
                                     currentOverlay.EndsWith(Path.GetFileName(cfg), StringComparison.OrdinalIgnoreCase));

                    list.Add(new BezelPresetItem
                    {
                        Name = name,
                        Platform = platform,
                        Description = desc,
                        CfgPath = cfg,
                        ImagePath = png,
                        Icon = icon,
                        AspectRatio = "4:3 CRT",
                        IsActive = isActive
                    });
                }
            }
        }
        catch { }

        return list;
    }

    public static void ApplyBezel(BezelPresetItem item)
    {
        if (!File.Exists(item.CfgPath)) return;

        RetroArchConfigManager.Set("input_overlay_enable", "true");
        RetroArchConfigManager.Set("input_overlay", item.CfgPath);
        RetroArchConfigManager.Set("input_overlay_opacity", "1.000000");
        RetroArchConfigManager.Set("input_overlay_hide_in_menu", "true");
        RetroArchConfigManager.Set("input_overlay_scale_landscape", "1.000000");
        RetroArchConfigManager.Set("input_overlay_auto_scale", "true");
        RetroArchConfigManager.Set("aspect_ratio_index", "0"); // 4:3
        RetroArchConfigManager.Set("video_scale_integer", "false");
        RetroArchConfigManager.Save();
    }

    public static void DisableBezel()
    {
        RetroArchConfigManager.Set("input_overlay_enable", "false");
        RetroArchConfigManager.Save();
    }

    public static void SetOpacity(float opacity)
    {
        RetroArchConfigManager.Set("input_overlay_opacity", opacity.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture));
        RetroArchConfigManager.Save();
    }

    public static void SetScale(float scale)
    {
        RetroArchConfigManager.Set("input_overlay_scale_landscape", scale.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture));
        RetroArchConfigManager.Save();
    }

    public static void SetAspectRatio(string aspectIndex)
    {
        RetroArchConfigManager.Set("aspect_ratio_index", aspectIndex);
        RetroArchConfigManager.Save();
    }

    public static void SetIntegerScale(bool enable)
    {
        RetroArchConfigManager.Set("video_scale_integer", enable ? "true" : "false");
        RetroArchConfigManager.Save();
    }

    public static (bool Success, string Message) ImportBezel(string sourceFile)
    {
        try
        {
            if (!File.Exists(sourceFile)) return (false, "Файл не найден");

            string ext = Path.GetExtension(sourceFile).ToLowerInvariant();
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourceFile);

            if (ext == ".png")
            {
                string targetPng = Path.Combine(BordersDir, Path.GetFileName(sourceFile));
                File.Copy(sourceFile, targetPng, true);

                string targetCfg = Path.Combine(BordersDir, fileNameWithoutExt + ".cfg");
                string cfgContent = "overlays = 1\n" +
                                   $"overlay0_overlay = \"{Path.GetFileName(targetPng)}\"\n" +
                                    "overlay0_full_screen = true\n" +
                                    "overlay0_descs = 0\n";
                File.WriteAllText(targetCfg, cfgContent.Replace("\\n", "\n"));

                return (true, $"Рамка «{fileNameWithoutExt}» успешно импортирована и настроена!");
            }
            else if (ext == ".cfg")
            {
                string targetCfg = Path.Combine(BordersDir, Path.GetFileName(sourceFile));
                File.Copy(sourceFile, targetCfg, true);

                // Check if associated png exists in source directory
                string sourcePng = Path.ChangeExtension(sourceFile, ".png");
                if (File.Exists(sourcePng))
                {
                    string targetPng = Path.Combine(BordersDir, Path.GetFileName(sourcePng));
                    File.Copy(sourcePng, targetPng, true);
                }

                return (true, $"Конфигурация рамки «{fileNameWithoutExt}» импортирована!");
            }

            return (false, "Поддерживаются только файлы .png или .cfg");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка импорта: {ex.Message}");
        }
    }

    public static void EnsureBuiltinBezels()
    {
        try
        {
            if (!Directory.Exists(BordersDir)) Directory.CreateDirectory(BordersDir);

            foreach (var tmpl in BuiltinTemplates)
            {
                string safeName = tmpl.Name.Replace(" ", "_");
                string pngPath = Path.Combine(BordersDir, safeName + ".png");
                string cfgPath = Path.Combine(BordersDir, safeName + ".cfg");

                if (!File.Exists(pngPath))
                {
                    GenerateBezelPng(pngPath, tmpl.BaseColorHex, tmpl.AccentColorHex);
                }

                if (!File.Exists(cfgPath))
                {
                    string cfgContent = "overlays = 1\n" +
                                       $"overlay0_overlay = \"{Path.GetFileName(pngPath)}\"\n" +
                                        "overlay0_full_screen = true\n" +
                                        "overlay0_descs = 0\n";
                    File.WriteAllText(cfgPath, cfgContent.Replace("\\n", "\n"));
                }
            }
        }
        catch { }
    }

    private static void GenerateBezelPng(string targetPngPath, string baseHex, string accentHex)
    {
        // 1920x1080 32-bit BGRA:
        // Screen viewport for 4:3 on 16:9 is x in [240..1680], y in [0..1080] (1440x1080)
        // Left pillar: x in [0..239]
        // Right pillar: x in [1680..1919]
        int width = 1920;
        int height = 1080;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];

        // Parse colors
        var baseColor = (Color)ColorConverter.ConvertFromString(baseHex);
        var accentColor = (Color)ColorConverter.ConvertFromString(accentHex);

        for (int y = 0; y < height; y++)
        {
            double yNorm = (double)y / height;

            for (int x = 0; x < width; x++)
            {
                int idx = y * stride + x * 4;

                // Center 4:3 area is 100% transparent:
                if (x >= 240 && x < 1680)
                {
                    // Draw a subtle 2px glowing CRT inner border at x=240, 241 and x=1678, 1679
                    if (x <= 242 || x >= 1677)
                    {
                        pixels[idx + 0] = accentColor.B;
                        pixels[idx + 1] = accentColor.G;
                        pixels[idx + 2] = accentColor.R;
                        pixels[idx + 3] = 160; // Semi-transparent glow
                    }
                    else
                    {
                        pixels[idx + 0] = 0;
                        pixels[idx + 1] = 0;
                        pixels[idx + 2] = 0;
                        pixels[idx + 3] = 0; // Transparent
                    }
                }
                else
                {
                    // Pillars: dark gradient with glowing accent strip
                    double factor = 0.8 + 0.4 * Math.Sin(yNorm * Math.PI);
                    byte r = (byte)Math.Clamp(baseColor.R * factor, 0, 255);
                    byte g = (byte)Math.Clamp(baseColor.G * factor, 0, 255);
                    byte b = (byte)Math.Clamp(baseColor.B * factor, 0, 255);

                    // Decorative accent vertical line at x = 20 and x = 1900
                    if (x == 20 || x == 1899)
                    {
                        r = accentColor.R;
                        g = accentColor.G;
                        b = accentColor.B;
                    }

                    pixels[idx + 0] = b; // Blue
                    pixels[idx + 1] = g; // Green
                    pixels[idx + 2] = r; // Red
                    pixels[idx + 3] = 255; // Fully opaque
                }
            }
        }

        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));

        using var fs = File.Create(targetPngPath);
        encoder.Save(fs);
    }
}
