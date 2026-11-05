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
    public string IconPathData { get; set; } = "M21 3H3c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h5v2h8v-2h5c1.1 0 1.99-.9 1.99-2L23 5c0-1.1-.9-2-2-2zm0 14H3V5h18v12z";
    public string IconColor { get; set; } = "#38BDF8";
    public string AspectRatio { get; set; } = "4:3 CRT";
    public bool IsCustom { get; set; }
    public ImageSource? PreviewImage { get; set; }

    public System.Windows.Visibility ActiveBadgeVisibility => IsActive ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility DeleteButtonVisibility => IsCustom ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveBadgeVisibility));
            }
        }
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
        new("Sony PlayStation 1 Bezel", "Sony PlayStation 1", "", "Аутентичная тёмно-серая рамка PS1 с неоновым контуром 4:3 окна и матовыми боковинами", "#1C1C24", "#003791"),
        new("Super Nintendo (SNES) Bezel", "Super Nintendo (SNES)", "", "Фирменные цвета Super Famicom / SNES с лавандовыми акцентами и CRT-затенением", "#22222E", "#7C3AED"),
        new("Nintendo NES / Famicom Bezel", "Nintendo Entertainment System", "", "Классический 8-битный ретро-стиль с тёмно-бордовыми акцентами Famicom", "#1A1A22", "#E11D48"),
        new("Sega Mega Drive / Genesis Bezel", "Sega Genesis / Mega Drive", "", "Чёрная текстурированная рамка 16-BIT с золотым тиснением High Definition Graphics", "#121218", "#EAB308"),
        new("Nintendo 64 Ultra Bezel", "Nintendo 64", "", "Элегантная полупрозрачная рамка с объёмным логотипом и кинематографичными тенями", "#181824", "#2563EB"),
        new("Game Boy DMG-01 Matrix Bezel", "Game Boy / Color", "", "Аутентичный оливково-серый корпус оригинального Game Boy с текстурой матрицы", "#282C22", "#65A30D"),
        new("Game Boy Advance Wide Bezel", "Game Boy Advance", "", "Широкоформатная индиго-рамка GBA с плавным градиентом и индикатором питания", "#1E1B38", "#8B5CF6"),
        new("Nintendo DS Dual-Bezel", "Nintendo DS / DSi", "", "Стильный двухэкранный дизайн с серебристыми боковыми стойками", "#22252A", "#06B6D4"),
        new("Sega Saturn Titanium Bezel", "Sega Saturn", "", "Тёмно-титановый корпус Sega Saturn с неоновыми синими акцентами", "#181A20", "#38BDF8"),
        new("Sega Dreamcast Spiral Bezel", "Sega Dreamcast", "", "Футуристичный белый и оранжевый стиль консоли Dreamcast", "#20222A", "#F97316"),
        new("Arcade Cabinet CRT Bezel", "Arcade Classics", "", "Истинный аркадный кабинет с заклёпками, виниловым блеском и сканлайн-тенью", "#14141A", "#EF4444"),
        new("STORM Midnight Frame Bezel", "STORM Exclusive", "", "Эксклюзивная тёмно-фиолетовая неоновая рамка AURORA FLUID с CRT-бликами", "#0D0B18", "#A855F7")
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

    public static BitmapImage? LoadPreviewImage(string pngPath)
    {
        if (!File.Exists(pngPath)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 320;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(pngPath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
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
                    string desc = matchTmpl?.Description ?? $"Пользовательская рамка Bezel для экрана 16:9 ({Path.GetFileName(cfg)})";

                    bool isActive = overlayEnabled && !string.IsNullOrEmpty(currentOverlay) &&
                                    (currentOverlay.Equals(cfg, StringComparison.OrdinalIgnoreCase) ||
                                     currentOverlay.EndsWith(Path.GetFileName(cfg), StringComparison.OrdinalIgnoreCase));

                    bool isCustom = matchTmpl == null && !BuiltinTemplates.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                    list.Add(new BezelPresetItem
                    {
                        Name = name,
                        Platform = platform,
                        Description = desc,
                        CfgPath = cfg,
                        ImagePath = png,
                        IconColor = matchTmpl?.AccentColorHex ?? "#38BDF8",
                        AspectRatio = "4:3 CRT",
                        IsActive = isActive,
                        IsCustom = isCustom,
                        PreviewImage = LoadPreviewImage(png)
                    });
                }
            }
        }
        catch { }

        return list;
    }

    public static BezelPresetItem? GetActiveBezel()
    {
        var list = ScanBezels();
        return list.FirstOrDefault(b => b.IsActive);
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

    public static void DeleteBezel(BezelPresetItem item)
    {
        try
        {
            if (item.IsActive || RetroArchConfigManager.GetString("input_overlay", "").Equals(item.CfgPath, StringComparison.OrdinalIgnoreCase))
            {
                DisableBezel();
            }
            if (File.Exists(item.CfgPath)) File.Delete(item.CfgPath);
            if (File.Exists(item.ImagePath)) File.Delete(item.ImagePath);
        }
        catch { }
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
        var res = CreateOrImportManualBezel("", "Custom Bezel", "Raw", "#38BDF8", sourceFile);
        return (res.Success, res.Message);
    }

    public static (bool Success, string Message, BezelPresetItem? Item) CreateOrImportManualBezel(
        string customName, string platform, string cutoutMode, string accentHex, string? sourceFile)
    {
        try
        {
            if (!Directory.Exists(BordersDir)) Directory.CreateDirectory(BordersDir);

            string displayName = !string.IsNullOrWhiteSpace(customName)
                ? customName.Trim()
                : (!string.IsNullOrWhiteSpace(sourceFile) ? Path.GetFileNameWithoutExtension(sourceFile) : "Custom Bezel");

            string safeBase = string.Join("_", displayName.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_');
            if (string.IsNullOrWhiteSpace(safeBase)) safeBase = "Custom_Bezel";

            string targetPng = Path.Combine(BordersDir, safeBase + ".png");
            string targetCfg = Path.Combine(BordersDir, safeBase + ".cfg");

            // Handle .cfg direct import
            if (!string.IsNullOrEmpty(sourceFile) && sourceFile.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) && File.Exists(sourceFile))
            {
                File.Copy(sourceFile, targetCfg, true);
                string companionPng = Path.ChangeExtension(sourceFile, ".png");
                if (File.Exists(companionPng))
                {
                    File.Copy(companionPng, targetPng, true);
                }

                var itemCfg = new BezelPresetItem
                {
                    Name = displayName,
                    Platform = platform,
                    Description = $"Импортированная конфигурация ({displayName})",
                    CfgPath = targetCfg,
                    ImagePath = targetPng,
                    IconColor = accentHex,
                    AspectRatio = "4:3 CRT",
                    IsCustom = true,
                    PreviewImage = LoadPreviewImage(targetPng)
                };
                ApplyBezel(itemCfg);
                return (true, $"Рамка «{displayName}» успешно импортирована и применена!", itemCfg);
            }

            const int width = 1920;
            const int height = 1080;
            const int stride = width * 4;
            byte[] pixels = new byte[height * stride];

            Color accentColor;
            try { accentColor = (Color)ColorConverter.ConvertFromString(accentHex); }
            catch { accentColor = Color.FromRgb(168, 85, 247); }

            bool hasSourceImage = !string.IsNullOrWhiteSpace(sourceFile) && File.Exists(sourceFile);

            if (hasSourceImage)
            {
                var uri = new Uri(Path.GetFullPath(sourceFile!), UriKind.Absolute);
                var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                var formatted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

                double scaleX = 1920.0 / Math.Max(1, formatted.PixelWidth);
                double scaleY = 1080.0 / Math.Max(1, formatted.PixelHeight);
                var scaled = new TransformedBitmap(formatted, new ScaleTransform(scaleX, scaleY));
                var finalBmp = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
                finalBmp.CopyPixels(pixels, stride, 0);
            }
            else
            {
                // Procedural background generation
                Color baseColor = Color.FromRgb(0x12, 0x12, 0x18);
                for (int y = 0; y < height; y++)
                {
                    double yNorm = (double)y / height;
                    double factor = 0.8 + 0.4 * Math.Sin(yNorm * Math.PI);
                    byte r = (byte)Math.Clamp(baseColor.R * factor, 0, 255);
                    byte g = (byte)Math.Clamp(baseColor.G * factor, 0, 255);
                    byte b = (byte)Math.Clamp(baseColor.B * factor, 0, 255);

                    for (int x = 0; x < width; x++)
                    {
                        int idx = y * stride + x * 4;
                        pixels[idx + 0] = b;
                        pixels[idx + 1] = g;
                        pixels[idx + 2] = r;
                        pixels[idx + 3] = 255;
                    }
                }
            }

            // Cutout application
            string mode = cutoutMode.Trim();
            if (mode == "Raw" && hasSourceImage)
            {
                // Check center pixel alpha; if center is opaque and format is jpg/bmp, fallback to 4:3
                int centerIdx = (540 * stride) + (960 * 4);
                byte centerAlpha = pixels[centerIdx + 3];
                string ext = Path.GetExtension(sourceFile!).ToLowerInvariant();
                if (centerAlpha == 255 && (ext == ".jpg" || ext == ".jpeg" || ext == ".bmp"))
                {
                    mode = "4:3";
                }
            }

            int cutoutLeft = -1, cutoutRight = -1, cutoutTop = 0, cutoutBottom = 1080;
            switch (mode)
            {
                case "4:3":
                    cutoutLeft = 240;
                    cutoutRight = 1680;
                    cutoutTop = 0;
                    cutoutBottom = 1080;
                    break;
                case "3:2":
                    cutoutLeft = 150;
                    cutoutRight = 1770;
                    cutoutTop = 0;
                    cutoutBottom = 1080;
                    break;
                case "1:1":
                    cutoutLeft = 420;
                    cutoutRight = 1500;
                    cutoutTop = 0;
                    cutoutBottom = 1080;
                    break;
                case "16:9":
                    cutoutLeft = 24;
                    cutoutRight = 1896;
                    cutoutTop = 24;
                    cutoutBottom = 1056;
                    break;
            }

            if (cutoutLeft >= 0)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int idx = y * stride + x * 4;
                        if (x >= cutoutLeft && x < cutoutRight && y >= cutoutTop && y < cutoutBottom)
                        {
                            // 2px neon border along inner edges
                            bool isBorder = (x < cutoutLeft + 2 || x >= cutoutRight - 2 || y < cutoutTop + 2 || y >= cutoutBottom - 2);
                            if (isBorder)
                            {
                                pixels[idx + 0] = accentColor.B;
                                pixels[idx + 1] = accentColor.G;
                                pixels[idx + 2] = accentColor.R;
                                pixels[idx + 3] = 180;
                            }
                            else
                            {
                                pixels[idx + 0] = 0;
                                pixels[idx + 1] = 0;
                                pixels[idx + 2] = 0;
                                pixels[idx + 3] = 0; // Fully transparent
                            }
                        }
                    }
                }
            }

            // Save PNG
            var bmpSource = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmpSource));
            using (var fs = File.Create(targetPng))
            {
                encoder.Save(fs);
            }

            // Save CFG
            var sbCfg = new System.Text.StringBuilder();
            sbCfg.AppendLine("overlays = 1");
            sbCfg.AppendLine($"overlay0_overlay = \"{Path.GetFileName(targetPng)}\"");
            sbCfg.AppendLine("overlay0_full_screen = true");
            sbCfg.AppendLine("overlay0_descs = 0");
            string cfgContent = sbCfg.ToString();
            File.WriteAllText(targetCfg, cfgContent);

            var item = new BezelPresetItem
            {
                Name = displayName,
                Platform = platform,
                Description = $"Пользовательская рамка Bezel ({cutoutMode})",
                CfgPath = targetCfg,
                ImagePath = targetPng,
                IconColor = accentHex,
                AspectRatio = cutoutMode switch
                {
                    "4:3" => "4:3 CRT",
                    "3:2" => "3:2 GBA",
                    "1:1" => "1:1 Квадрат",
                    "16:9" => "16:9 Тонкая",
                    _ => "Auto"
                },
                IsCustom = true,
                PreviewImage = LoadPreviewImage(targetPng)
            };

            ApplyBezel(item);
            return (true, $"Рамка «{displayName}» успешно создана и применена!", item);
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка создания рамки: {ex.Message}", null);
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
                    var sbCfg2 = new System.Text.StringBuilder();
                    sbCfg2.AppendLine("overlays = 1");
                    sbCfg2.AppendLine($"overlay0_overlay = \"{Path.GetFileName(pngPath)}\"");
                    sbCfg2.AppendLine("overlay0_full_screen = true");
                    sbCfg2.AppendLine("overlay0_descs = 0");
                    string cfgContent = sbCfg2.ToString();
                    File.WriteAllText(cfgPath, cfgContent);
                }
            }
        }
        catch { }
    }

    private static void GenerateBezelPng(string targetPngPath, string baseHex, string accentHex)
    {
        int width = 1920;
        int height = 1080;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];

        var baseColor = (Color)ColorConverter.ConvertFromString(baseHex);
        var accentColor = (Color)ColorConverter.ConvertFromString(accentHex);

        for (int y = 0; y < height; y++)
        {
            double yNorm = (double)y / height;

            for (int x = 0; x < width; x++)
            {
                int idx = y * stride + x * 4;

                if (x >= 240 && x < 1680)
                {
                    if (x <= 242 || x >= 1677)
                    {
                        pixels[idx + 0] = accentColor.B;
                        pixels[idx + 1] = accentColor.G;
                        pixels[idx + 2] = accentColor.R;
                        pixels[idx + 3] = 160;
                    }
                    else
                    {
                        pixels[idx + 0] = 0;
                        pixels[idx + 1] = 0;
                        pixels[idx + 2] = 0;
                        pixels[idx + 3] = 0;
                    }
                }
                else
                {
                    double factor = 0.8 + 0.4 * Math.Sin(yNorm * Math.PI);
                    byte r = (byte)Math.Clamp(baseColor.R * factor, 0, 255);
                    byte g = (byte)Math.Clamp(baseColor.G * factor, 0, 255);
                    byte b = (byte)Math.Clamp(baseColor.B * factor, 0, 255);

                    if (x == 20 || x == 1899)
                    {
                        r = accentColor.R;
                        g = accentColor.G;
                        b = accentColor.B;
                    }

                    pixels[idx + 0] = b;
                    pixels[idx + 1] = g;
                    pixels[idx + 2] = r;
                    pixels[idx + 3] = 255;
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
