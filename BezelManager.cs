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
    public string AspectRatio { get; set; } = "4:3 ЭЛТ";
    public bool IsCustom { get; set; }
    public ImageSource? PreviewImage { get; set; }

    // Studio Cutout Geometry (1920x1080 default frame)
    public int CutLeft { get; set; } = 272;
    public int CutRight { get; set; } = 1648;
    public int CutTop { get; set; } = 24;
    public int CutBottom { get; set; } = 1056;
    public int CanvasWidth { get; set; } = 1920;
    public int CanvasHeight { get; set; } = 1080;

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
    // Exact Mathematical 4:3 Inner Cutout (1920x1080 canvas, 1376x1032 inner screen)
    public const double CanvasWidth = 1920.0;
    public const double CanvasHeight = 1080.0;
    public const double InnerScreenLeft = 272.0;
    public const double InnerScreenTop = 24.0;
    public const double InnerScreenWidth = 1376.0;  // 1376 / 1032 == exact 4:3!
    public const double InnerScreenHeight = 1032.0;

    public const int DefaultCanvasWidth = 1920;
    public const int DefaultCanvasHeight = 1080;
    public const int DefaultCutLeft = 272;
    public const int DefaultCutRight = 1648;
    public const int DefaultCutTop = 24;
    public const int DefaultCutBottom = 1056;

    public const double DefaultCutLeftFraction = (double)DefaultCutLeft / DefaultCanvasWidth;        // 220 / 1920 = 0.11458333... (11.46%)
    public const double DefaultCutRightFraction = (double)(DefaultCanvasWidth - DefaultCutRight) / DefaultCanvasWidth; // 220 / 1920 = 0.11458333... (11.46%)
    public const double DefaultCutTopFraction = (double)DefaultCutTop / DefaultCanvasHeight;          // 28 / 1080 = 0.02592592... (2.59%)
    public const double DefaultCutBottomFraction = (double)(DefaultCanvasHeight - DefaultCutBottom) / DefaultCanvasHeight; // 28 / 1080 = 0.02592592... (2.59%)

    public static (double LeftFraction, double TopFraction, double RightFraction, double BottomFraction) GetCutoutFractions(BezelPresetItem? bezel = null)
    {
        int cutLeft = DefaultCutLeft;
        int cutRight = DefaultCutRight;
        int cutTop = DefaultCutTop;
        int cutBottom = DefaultCutBottom;
        int width = DefaultCanvasWidth;
        int height = DefaultCanvasHeight;

        if (bezel != null)
        {
            if (bezel.CanvasWidth > 0 && bezel.CanvasHeight > 0 && bezel.CutRight > bezel.CutLeft && bezel.CutBottom > bezel.CutTop)
            {
                cutLeft = bezel.CutLeft;
                cutRight = bezel.CutRight;
                cutTop = bezel.CutTop;
                cutBottom = bezel.CutBottom;
                width = bezel.CanvasWidth;
                height = bezel.CanvasHeight;
            }
            else
            {
                switch (bezel.AspectRatio)
                {
                    case "3:2":
                    case "3:2 Широкая":
                        cutLeft = 180; cutRight = 1740; cutTop = 36; cutBottom = 1044;
                        break;
                    case "1:1":
                    case "1:1 Квадрат":
                        cutLeft = 420; cutRight = 1500; cutTop = 28; cutBottom = 1052;
                        break;
                    case "16:9":
                    case "16:9 Тонкая":
                        cutLeft = 48; cutRight = 1872; cutTop = 28; cutBottom = 1052;
                        break;
                    default:
                        cutLeft = DefaultCutLeft; cutRight = DefaultCutRight; cutTop = DefaultCutTop; cutBottom = DefaultCutBottom;
                        break;
                }
            }
        }

        double leftFrac = (double)cutLeft / width;
        double rightFrac = (double)(width - cutRight) / width;
        double topFrac = (double)cutTop / height;
        double bottomFrac = (double)(height - cutBottom) / height;

        return (leftFrac, topFrac, rightFrac, bottomFrac);
    }

    public static readonly string OverlaysDir = Path.Combine(CoreManager.AppDir, "overlays");
    public static readonly string BordersDir = Path.Combine(OverlaysDir, "borders");

    public record BuiltinBezelDef(string FileSlug, string Name, string Platform, string Icon, string Description, string BaseColorHex, string AccentColorHex);

    public static readonly List<BuiltinBezelDef> BuiltinTemplates = new()
    {
        new("STORM_Midnight_Aurora", "Фирменная неоновая рамка STORM", "Эксклюзив STORM", "", "Тёмно-фиолетовая студийная рамка с неоновым контуром и бликами ЭЛТ-экрана", "#0D0B18", "#A855F7"),
        new("Nintendo_NES_Famicom", "Рамка Nintendo NES и Famicom", "Nintendo Entertainment System", "", "Классический 8-битный ретро-стиль с бордовыми акцентами и текстурой консоли", "#1A1A22", "#E11D48"),
        new("MS_DOS_CRT_Monitor", "Рамка MS-DOS и ЭЛТ-монитор", "MS-DOS PC", "", "Классический компьютерный ЭЛТ-монитор IBM PC с янтарно-зелёным индикатором питания", "#161920", "#10B981"),
        new("Sony_PlayStation_1", "Рамка Sony PlayStation 1", "Sony PlayStation 1", "", "Аутентичная тёмно-серая рамка PS1 с фирменным синим контуром 4:3 и матовыми боковинами", "#1C1C24", "#0055D4"),
        new("Super_Nintendo_SNES", "Рамка Super Nintendo", "Super Nintendo (SNES)", "", "Фирменные цвета Super Famicom с лавандовыми акцентами и затенением экрана", "#22222E", "#7C3AED"),
        new("Sega_Mega_Drive_Genesis", "Рамка Sega Mega Drive", "Sega Genesis / Mega Drive", "", "Чёрная текстурированная рамка 16-Бит с золотистым контуром и решётками корпуса", "#121218", "#EAB308"),
        new("Nintendo_64_Ultra", "Рамка Nintendo 64", "Nintendo 64", "", "Элегантная тёмная рамка с объёмным контуром и кинематографичными тенями", "#181824", "#2563EB"),
        new("Game_Boy_DMG_01", "Рамка Game Boy DMG-01", "Game Boy / Color", "", "Аутентичный оливково-серый корпус классического Game Boy с матричной окантовкой", "#282C22", "#65A30D"),
        new("Game_Boy_Advance_Wide", "Рамка Game Boy Advance", "Game Boy Advance", "", "Индиго-рамка портативной консоли GBA с плавным градиентом и индикатором батареи", "#1E1B38", "#8B5CF6"),
        new("Nintendo_DS_Dual", "Рамка Nintendo DS", "Nintendo DS / DSi", "", "Стильный двухэкранный корпус с бирюзовыми акцентами и боковыми стойками", "#22252A", "#06B6D4"),
        new("Sega_Saturn_Titanium", "Рамка Sega Saturn", "Sega Saturn", "", "Тёмно-титановый корпус Sega Saturn с неоновыми голубыми линиями", "#181A20", "#38BDF8"),
        new("Sega_Dreamcast_Spiral", "Рамка Sega Dreamcast", "Sega Dreamcast", "", "Футуристичный стиль консоли Dreamcast с ярко-оранжевой подсветкой", "#20222A", "#F97316"),
        new("Arcade_Cabinet_CRT", "Аркадный игровой автомат", "Arcade Classics", "", "Классический аркадный кабинет с виниловыми боковинами и глубокой тенью кинескопа", "#14141A", "#EF4444")
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
        if (string.IsNullOrEmpty(pngPath) || !File.Exists(pngPath)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 360;
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

    public static BitmapImage? LoadFullBezelImage(string pngPath)
    {
        if (string.IsNullOrEmpty(pngPath) || !File.Exists(pngPath)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
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

    private static string ResolveOverlayPngPath(string cfgPath)
    {
        string directPng = Path.ChangeExtension(cfgPath, ".png");
        if (File.Exists(directPng)) return directPng;

        try
        {
            string cfgDir = Path.GetDirectoryName(cfgPath) ?? BordersDir;
            foreach (var rawLine in File.ReadAllLines(cfgPath))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("overlay0_overlay", StringComparison.OrdinalIgnoreCase))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0)
                    {
                        string rel = line.Substring(eq + 1).Trim().Trim('"', '\'');
                        string candidate = Path.GetFullPath(Path.Combine(cfgDir, rel));
                        if (File.Exists(candidate)) return candidate;
                    }
                }
            }
        }
        catch { }

        return directPng;
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
                    string png = ResolveOverlayPngPath(cfg);
                    if (!File.Exists(png))
                        continue; // Never add broken .cfg files without an existing image!

                    string fileSlug = Path.GetFileNameWithoutExtension(cfg);
                    var matchTmpl = BuiltinTemplates.FirstOrDefault(t =>
                        t.FileSlug.Equals(fileSlug, StringComparison.OrdinalIgnoreCase) ||
                        t.Name.Replace(' ', '_').Equals(fileSlug, StringComparison.OrdinalIgnoreCase));

                    string displayName = matchTmpl?.Name ?? fileSlug.Replace('_', ' ').Replace("Bezel", "Рамка").Trim();
                    string platform = matchTmpl?.Platform ?? "Пользовательская рамка";
                    string desc = matchTmpl?.Description ?? $"Пользовательская экранная рамка 16:9 ({Path.GetFileName(cfg)})";

                    bool isActive = overlayEnabled && !string.IsNullOrEmpty(currentOverlay) &&
                                    (currentOverlay.Equals(cfg, StringComparison.OrdinalIgnoreCase) ||
                                     Path.GetFileName(currentOverlay).Equals(Path.GetFileName(cfg), StringComparison.OrdinalIgnoreCase));

                    bool isCustom = matchTmpl == null;

                    list.Add(new BezelPresetItem
                    {
                        Name = displayName,
                        Platform = platform,
                        Description = desc,
                        CfgPath = cfg,
                        ImagePath = png,
                        IconColor = matchTmpl?.AccentColorHex ?? "#38BDF8",
                        CutLeft = DefaultCutLeft,
                        CutRight = DefaultCutRight,
                        CutTop = DefaultCutTop,
                        CutBottom = DefaultCutBottom,
                        CanvasWidth = DefaultCanvasWidth,
                        CanvasHeight = DefaultCanvasHeight,
                        AspectRatio = "4:3 ЭЛТ",
                        IsActive = isActive,
                        IsCustom = isCustom,
                        PreviewImage = LoadPreviewImage(png)
                    });
                }
            }
        }
        catch { }

        // Put built-in studio templates first in exact order, followed by custom bezels
        return list
            .OrderBy(b =>
            {
                int idx = BuiltinTemplates.FindIndex(t => t.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase));
                return idx >= 0 ? idx : 1000;
            })
            .ThenBy(b => b.Name)
            .ToList();
    }

    public static BezelPresetItem? GetActiveBezel()
    {
        var list = ScanBezels();
        return list.FirstOrDefault(b => b.IsActive);
    }

    public static BezelPresetItem? GetBezelForPlatform(string? platform)
    {
        var list = ScanBezels();
        if (list.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(platform))
        {
            var match = list.FirstOrDefault(b =>
                b.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase) ||
                b.Platform.Contains(platform.Split(' ')[0], StringComparison.OrdinalIgnoreCase) ||
                platform.Contains(b.Platform.Split(' ')[0], StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return list[0];
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
        RetroArchConfigManager.Set("aspect_ratio_index", "0");
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
        var res = CreateOrImportManualBezel("", "Пользовательская рамка", "Raw", "#38BDF8", sourceFile);
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
                : (!string.IsNullOrWhiteSpace(sourceFile) ? Path.GetFileNameWithoutExtension(sourceFile) : "Пользовательская рамка");

            string safeBase = string.Join("_", displayName.Split(Path.GetInvalidFileNameChars()))
                .Replace('/', '_').Replace('\\', '_').Replace(' ', '_');
            if (string.IsNullOrWhiteSpace(safeBase)) safeBase = "Custom_Frame";

            string targetPng = Path.Combine(BordersDir, safeBase + ".png");
            string targetCfg = Path.Combine(BordersDir, safeBase + ".cfg");

            if (!string.IsNullOrEmpty(sourceFile) && sourceFile.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) && File.Exists(sourceFile))
            {
                File.Copy(sourceFile, targetCfg, true);
                string companionPng = ResolveOverlayPngPath(sourceFile);
                if (File.Exists(companionPng))
                {
                    File.Copy(companionPng, targetPng, true);
                }

                var itemCfg = new BezelPresetItem
                {
                    Name = displayName,
                    Platform = platform,
                    Description = $"Импортированная рамка ({displayName})",
                    CfgPath = targetCfg,
                    ImagePath = targetPng,
                    IconColor = accentHex,
                    AspectRatio = "4:3 ЭЛТ",
                    IsCustom = true,
                    PreviewImage = LoadPreviewImage(targetPng)
                };
                ApplyBezel(itemCfg);
                return (true, $"Рамка «{displayName}» успешно импортирована и включена!", itemCfg);
            }

            int width = 1920;
            int height = 1080;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];

            var accentColor = (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(accentHex) ? "#A855F7" : accentHex);
            var baseColor = Color.FromRgb(18, 16, 28);

            if (!string.IsNullOrEmpty(sourceFile) && File.Exists(sourceFile))
            {
                var srcBmp = new BitmapImage();
                srcBmp.BeginInit();
                srcBmp.CacheOption = BitmapCacheOption.OnLoad;
                srcBmp.UriSource = new Uri(sourceFile, UriKind.Absolute);
                srcBmp.EndInit();

                var scaled = new TransformedBitmap(srcBmp, new ScaleTransform(
                    (double)width / srcBmp.PixelWidth,
                    (double)height / srcBmp.PixelHeight));
                var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
                bgra.CopyPixels(pixels, stride, 0);
            }
            else
            {
                for (int y = 0; y < height; y++)
                {
                    double yNorm = (double)y / height;
                    double factor = 0.8 + 0.4 * Math.Sin(yNorm * Math.PI);
                    byte r = (byte)Math.Clamp(baseColor.R * factor + accentColor.R * 0.14, 0, 255);
                    byte g = (byte)Math.Clamp(baseColor.G * factor + accentColor.G * 0.14, 0, 255);
                    byte b = (byte)Math.Clamp(baseColor.B * factor + accentColor.B * 0.14, 0, 255);

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

            int cutLeft = DefaultCutLeft, cutRight = DefaultCutRight, cutTop = DefaultCutTop, cutBottom = DefaultCutBottom;
            if (!string.Equals(cutoutMode, "Raw", StringComparison.OrdinalIgnoreCase))
            {
                switch (cutoutMode)
                {
                    case "3:2":
                        cutLeft = 180; cutRight = 1740; cutTop = 36; cutBottom = 1044;
                        break;
                    case "1:1":
                        cutLeft = 420; cutRight = 1500; cutTop = 28; cutBottom = 1052;
                        break;
                    case "16:9":
                        cutLeft = 48; cutRight = 1872; cutTop = 28; cutBottom = 1052;
                        break;
                }

                for (int y = cutTop; y < cutBottom; y++)
                {
                    for (int x = cutLeft; x < cutRight; x++)
                    {
                        int idx = y * stride + x * 4;
                        bool isBorder = (x - cutLeft < 4) || (cutRight - 1 - x < 4) ||
                                        (y - cutTop < 4) || (cutBottom - 1 - y < 4);
                        if (isBorder)
                        {
                            pixels[idx + 0] = accentColor.B;
                            pixels[idx + 1] = accentColor.G;
                            pixels[idx + 2] = accentColor.R;
                            pixels[idx + 3] = 235;
                        }
                        else
                        {
                            pixels[idx + 0] = 0;
                            pixels[idx + 1] = 0;
                            pixels[idx + 2] = 0;
                            pixels[idx + 3] = 0;
                        }
                    }
                }
            }

            var outBmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(outBmp));
            using (var fs = File.Create(targetPng))
            {
                encoder.Save(fs);
            }

            var sbCfg = new System.Text.StringBuilder();
            sbCfg.AppendLine("overlays = 1");
            sbCfg.AppendLine($"overlay0_overlay = \"{Path.GetFileName(targetPng)}\"");
            sbCfg.AppendLine("overlay0_full_screen = true");
            sbCfg.AppendLine("overlay0_descs = 0");
            File.WriteAllText(targetCfg, sbCfg.ToString());

            var item = new BezelPresetItem
            {
                Name = displayName,
                Platform = platform,
                Description = $"Пользовательская рамка ({cutoutMode}) • {Path.GetFileName(targetPng)}",
                CfgPath = targetCfg,
                ImagePath = targetPng,
                IconColor = accentHex,
                CutLeft = cutLeft,
                CutRight = cutRight,
                CutTop = cutTop,
                CutBottom = cutBottom,
                CanvasWidth = width,
                CanvasHeight = height,
                AspectRatio = cutoutMode switch
                {
                    "4:3" => "4:3 ЭЛТ",
                    "3:2" => "3:2 Широкая",
                    "1:1" => "1:1 Квадрат",
                    "16:9" => "16:9 Тонкая",
                    _ => "Авто"
                },
                IsCustom = true,
                PreviewImage = LoadPreviewImage(targetPng)
            };

            ApplyBezel(item);
            return (true, $"Рамка «{displayName}» успешно создана и включена!", item);
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

            string v2Marker = Path.Combine(BordersDir, ".storm_bezels_v2.ok");
            string v3Marker = Path.Combine(BordersDir, ".storm_bezels_v3.ok");
            string v4Marker = Path.Combine(BordersDir, ".storm_bezels_v4.ok");

            try
            {
                if (File.Exists(v2Marker)) File.Delete(v2Marker);
                if (File.Exists(v3Marker)) File.Delete(v3Marker);
            }
            catch { }

            bool forceRegen = !File.Exists(v4Marker);

            foreach (var tmpl in BuiltinTemplates)
            {
                try
                {
                    string pngPath = Path.Combine(BordersDir, tmpl.FileSlug + ".png");
                    string cfgPath = Path.Combine(BordersDir, tmpl.FileSlug + ".cfg");

                    if (forceRegen || !File.Exists(pngPath))
                    {
                        GenerateBezelPng(pngPath, tmpl.BaseColorHex, tmpl.AccentColorHex);
                    }

                    if (forceRegen || !File.Exists(cfgPath))
                    {
                        var sbCfg2 = new System.Text.StringBuilder();
                        sbCfg2.AppendLine("overlays = 1");
                        sbCfg2.AppendLine($"overlay0_overlay = \"{Path.GetFileName(pngPath)}\"");
                        sbCfg2.AppendLine("overlay0_full_screen = true");
                        sbCfg2.AppendLine("overlay0_descs = 0");
                        File.WriteAllText(cfgPath, sbCfg2.ToString());
                    }
                }
                catch { }
            }

            if (forceRegen)
            {
                try { File.WriteAllText(v4Marker, "v4"); } catch { }
            }
        }
        catch { }
    }

    private static void GenerateBezelPng(string targetPngPath, string baseHex, string accentHex)
    {
        int width = (int)CanvasWidth;
        int height = (int)CanvasHeight;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];

        var baseColor = (Color)ColorConverter.ConvertFromString(baseHex);
        var accentColor = (Color)ColorConverter.ConvertFromString(accentHex);

        int cutLeft = (int)InnerScreenLeft;      // 272
        int cutRight = cutLeft + (int)InnerScreenWidth; // 1648 (1376 width)
        int cutTop = (int)InnerScreenTop;        // 24
        int cutBottom = cutTop + (int)InnerScreenHeight; // 1056 (1032 height)

        for (int y = 0; y < height; y++)
        {
            double yNorm = (double)y / height;
            double vShade = 0.78 + 0.35 * Math.Sin(yNorm * Math.PI);

            for (int x = 0; x < width; x++)
            {
                int idx = y * stride + x * 4;

                bool insideCutout = (x >= cutLeft && x < cutRight && y >= cutTop && y < cutBottom);
                if (insideCutout)
                {
                    // 100% transparent game screen: zero inner border, zero inner shadow
                    pixels[idx + 0] = 0;
                    pixels[idx + 1] = 0;
                    pixels[idx + 2] = 0;
                    pixels[idx + 3] = 0;
                }
                else
                {
                    // Outer 4px neon border strictly OUTSIDE the 1376×1032 game hole
                    bool isNeonBorder = (x >= cutLeft - 4 && x < cutLeft && y >= cutTop - 4 && y < cutBottom + 4) ||
                                        (x >= cutRight && x < cutRight + 4 && y >= cutTop - 4 && y < cutBottom + 4) ||
                                        (y >= cutTop - 4 && y < cutTop && x >= cutLeft - 4 && x < cutRight + 4) ||
                                        (y >= cutBottom && y < cutBottom + 4 && x >= cutLeft - 4 && x < cutRight + 4);

                    if (isNeonBorder)
                    {
                        pixels[idx + 0] = accentColor.B;
                        pixels[idx + 1] = accentColor.G;
                        pixels[idx + 2] = accentColor.R;
                        pixels[idx + 3] = 255;
                    }
                    else
                    {
                        // Console casing frame (left/right sidebars + top/bottom bars)
                        byte r = (byte)Math.Clamp(baseColor.R * vShade, 0, 255);
                        byte g = (byte)Math.Clamp(baseColor.G * vShade, 0, 255);
                        byte b = (byte)Math.Clamp(baseColor.B * vShade, 0, 255);

                        // Horizontal metallic ribs on left and right sidebars
                        if ((x >= 32 && x <= 188) || (x >= 1732 && x <= 1888))
                        {
                            if (y >= 140 && y <= 340 && (y % 18 < 5))
                            {
                                r = (byte)(r * 0.62);
                                g = (byte)(g * 0.62);
                                b = (byte)(b * 0.62);
                            }
                        }

                        // Outer neon stripe along left & right edges
                        if ((x >= 18 && x <= 22) || (x >= 1897 && x <= 1901))
                        {
                            r = accentColor.R;
                            g = accentColor.G;
                            b = accentColor.B;
                        }

                        // Glowing console power LED indicator at bottom-right sidebar
                        if (x >= 1790 && x <= 1830 && y >= 980 && y <= 992)
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
        }

        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));

        using var fs = File.Create(targetPngPath);
        encoder.Save(fs);
    }
}
