using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StormRetroArch;

public class BezelItem : BezelPresetItem { }

public class BezelPresetItem : INotifyPropertyChanged
{
    private bool _isActive;
    public string Name { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Description { get; set; } = "";
    public string CfgPath { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public string SlangpPath { get; set; } = "";
    public string PackSource { get; set; } = "STORM Studio";
    public string DeviceImagePath { get; set; } = "";
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
        new("STORM_Aurora_Glass", "STORM Aurora Glass", "Эксклюзив STORM", "", "Фирменная тёмная студийная рамка с неоновым контуром и стеклянными бликами", "#0D0B18", "#A855F7"),
        new("Sony_Trinitron_PVM", "Sony Trinitron PVM", "Sony Trinitron PVM", "", "Профессиональный студийный ЭЛТ-монитор Sony Trinitron с апертурной решёткой", "#181820", "#0055D4"),
        new("Arcade_Neon_Cabinet", "Arcade Neon Cabinet", "Arcade Classics", "", "Классический аркадный автомат с неоновой подсветкой и глубокой тенью кинескопа", "#14141A", "#EF4444"),
        new("Nintendo_NES_Famicom", "NES / Famicom", "Nintendo Entertainment System", "", "Классический 8-битный ретро-стиль с бордовыми акцентами и текстурой консоли", "#1A1A22", "#E11D48"),
        new("Super_Nintendo_SNES", "Super Nintendo", "Super Nintendo (SNES)", "", "Фирменные цвета Super Famicom с лавандовыми акцентами и затенением экрана", "#22222E", "#7C3AED"),
        new("Sega_Mega_Drive_Genesis", "Sega Mega Drive", "Sega Genesis / Mega Drive", "", "Чёрная текстурированная рамка 16-Бит с золотистым контуром и решётками корпуса", "#121218", "#EAB308"),
        new("Sony_PlayStation", "PlayStation", "Sony PlayStation 1", "", "Аутентичный тёмно-серый корпус оригинальной консоли Sony PlayStation 1", "#1C1C24", "#3B82F6"),
        new("Sega_Saturn_Titanium", "Sega Saturn", "Sega Saturn", "", "Тёмно-титановый корпус Sega Saturn с неоновыми голубыми линиями", "#181A20", "#38BDF8"),
        new("Sega_Dreamcast_Spiral", "Dreamcast", "Sega Dreamcast", "", "Футуристичный стиль консоли Dreamcast с ярко-оранжевой подсветкой", "#20222A", "#F97316"),
        new("Nintendo_64_Ultra", "Nintendo 64", "Nintendo 64", "", "Элегантная тёмная рамка N64 с объёмным контуром и кинематографичными тенями", "#181824", "#2563EB"),
        new("Game_Boy_Advance_Wide", "Game Boy Advance", "Game Boy Advance", "", "Индиго-рамка портативной консоли GBA с плавным градиентом и индикатором батареи", "#1E1B38", "#8B5CF6"),
        new("IBM_DOS_VGA_Monitor", "IBM DOS VGA Monitor", "MS-DOS PC", "", "Классический компьютерный ЭЛТ-монитор IBM PC с янтарно-зелёным индикатором питания", "#161920", "#10B981"),
        new("Commodore_1084S", "Commodore 1084S", "Commodore Amiga / C64", "", "Легендарный цветной монитор Commodore 1084S для Amiga и Commodore 64", "#202026", "#F59E0B"),
        new("PC_Engine_Core", "PC Engine", "PC Engine / TurboGrafx-16", "", "Компактный японский дизайн консоли PC Engine HuCard с неоново-розовым акцентом", "#181922", "#EC4899"),
        new("Neo_Geo_MVS_Cabinet", "Neo Geo MVS", "SNK Neo Geo", "", "Легендарный аркадный кабинет Neo Geo MVS 100 Mega Shock в тёмно-красных тонах", "#181014", "#DC2626"),
        new("Atari_Woodgrain", "Atari Woodgrain", "Atari 2600 / 7800", "", "Аутентичная отделка под ореховое дерево классической консоли Atari 2600", "#261914", "#D97706"),
        new("STORM_Universal_Bezel", "Универсальная рамка STORM 4:3 / 16:9", "Универсальная STORM", "", "Универсальная адаптивная экранная рамка STORM для всех 358 ядер", "#0D0B18", "#38BDF8"),
        new("Sony_PlayStation_2", "PlayStation 2", "Sony PlayStation 2", "", "Стильный глубокий синий монолит оригинальной консоли Sony PlayStation 2", "#0B1020", "#2563EB"),
        new("Sony_PlayStation_3", "PlayStation 3", "Sony PlayStation 3", "", "Элегантный глянцевый чёрный корпус PlayStation 3 с хромированной окантовкой", "#0F0F14", "#94A3B8"),
        new("Sony_PlayStation_Vita", "PS Vita", "Sony PlayStation Vita", "", "Глубокая тёмная рамка OLED-экрана PS Vita с бирюзовым индикатором PS", "#10141D", "#06B6D4"),
        new("Sony_PSP_Street", "PSP", "Sony PlayStation Portable", "", "Широкоэкранная рамка PSP со стальным матовым обрамлением", "#16161C", "#64748B"),
        new("Nintendo_GameCube_Wii", "GameCube / Wii", "Nintendo GameCube / Wii", "", "Кубический индиго-дизайн GameCube с бирюзово-белым акцентом Wii", "#181628", "#818CF8"),
        new("Nintendo_3DS_Dual", "Nintendo 3DS", "Nintendo 3DS", "", "Глубокая космическая чёрная рамка портативной консоли Nintendo 3DS", "#14151C", "#EC4899"),
        new("Microsoft_Xbox_Original", "Xbox", "Microsoft Xbox", "", "Мощная чёрная текстурированная рамка Xbox с ядовито-зелёным сиянием X-ядра", "#0E1810", "#22C55E"),
        new("Arcade_MAME_Retro", "Arcade / MAME", "Аркадные автоматы MAME", "", "Профессиональный аркадный кабинет с неоновыми боковыми панелями", "#141118", "#F43F5E"),
        new("MS_DOS_CRT_Amber", "MS-DOS CRT", "MS-DOS PC", "", "Аутентичный тёмный монитор MS-DOS с антибликовым стеклом и янтарной индикацией", "#121418", "#F59E0B")
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

    private static DateTime _lastBordersDirWriteTimeUtc = DateTime.MinValue;
    private static int _lastBordersCount = 0;
    private static readonly List<BezelPresetItem> _cachedBezels = new();
    private static readonly object _bezelsLock = new();

    public static List<BezelPresetItem> ScanBezels(bool forceRefresh = false)
    {
        lock (_bezelsLock)
        {
            EnsureBuiltinBezels();

            if (!forceRefresh && Directory.Exists(BordersDir))
            {
                try
                {
                    DateTime currentWrite = Directory.GetLastWriteTimeUtc(BordersDir);
                    if (currentWrite == _lastBordersDirWriteTimeUtc && _cachedBezels.Count > 0)
                    {
                        // Refresh active state in 0.0 ms without disk I/O
                        string curOverlay = RetroArchConfigManager.GetString("input_overlay", "");
                        bool ovrEnabled = RetroArchConfigManager.GetBool("input_overlay_enable", false);
                        foreach (var b in _cachedBezels)
                        {
                            b.IsActive = ovrEnabled && !string.IsNullOrEmpty(curOverlay) &&
                                         (curOverlay.Equals(b.CfgPath, StringComparison.OrdinalIgnoreCase) ||
                                          Path.GetFileName(curOverlay).Equals(Path.GetFileName(b.CfgPath), StringComparison.OrdinalIgnoreCase));
                        }
                        return new List<BezelPresetItem>(_cachedBezels);
                    }
                }
                catch { }
            }

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

        // Integrate installed Mega Bezel & Duimon packs
            try
            {
                var megaPresets = MegaBezelPackManager.ScanInstalledPresets();
                string activeSlangp = SettingsManager.Current.ActiveMegaBezelSlangp;
                foreach (var mp in megaPresets)
                {
                    if (!string.IsNullOrEmpty(activeSlangp) &&
                        string.Equals(mp.SlangpPath, activeSlangp, StringComparison.OrdinalIgnoreCase))
                    {
                        mp.IsActive = true;
                    }
                    list.Add(mp);
                }
            }
            catch { }

            // Put built-in studio templates first in exact order, followed by Mega Bezel presets and custom bezels
            var sorted = list
                .OrderBy(b =>
                {
                    int idx = BuiltinTemplates.FindIndex(t => t.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) return idx;
                    if (!string.IsNullOrEmpty(b.SlangpPath)) return 500;
                    return 1000;
                })
                .ThenBy(b => b.Platform)
                .ThenBy(b => b.Name)
                .ToList();

            try
            {
                if (Directory.Exists(BordersDir))
                {
                    _lastBordersDirWriteTimeUtc = Directory.GetLastWriteTimeUtc(BordersDir);
                    _lastBordersCount = sorted.Count;
                }
            }
            catch { }

            _cachedBezels.Clear();
            _cachedBezels.AddRange(sorted);
            return sorted;
        }
    }

    public static string EnsureRetroArchOverlayCfg(BezelPresetItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.CfgPath) && File.Exists(item.CfgPath))
            return item.CfgPath;

        if (string.IsNullOrWhiteSpace(item.ImagePath) || !File.Exists(item.ImagePath))
            return "";

        try
        {
            string dir = Path.GetDirectoryName(item.ImagePath) ?? BordersDir;
            string cfgPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(item.ImagePath) + ".cfg");
            if (!File.Exists(cfgPath))
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("overlays = 1");
                sb.AppendLine($"overlay0_overlay = \"{Path.GetFileName(item.ImagePath)}\"");
                sb.AppendLine("overlay0_full_screen = true");
                sb.AppendLine("overlay0_descs = 0");
                File.WriteAllText(cfgPath, sb.ToString());
            }
            item.CfgPath = cfgPath;
            return cfgPath;
        }
        catch
        {
            return "";
        }
    }

    public static BezelPresetItem? GetActiveBezel()
    {
        var list = ScanBezels();
        return list.FirstOrDefault(b => b.IsActive);
    }

    public static BezelPresetItem? FindBestBezelForPlatformOrCore(string? platform, string? coreDllName)
    {
        var bezels = ScanBezels();
        if (bezels.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(coreDllName))
        {
            string c = Path.GetFileName(coreDllName).ToLowerInvariant();
            string? mappedSlug = null;
            if (c.Contains("rpcs3")) mappedSlug = "Sony_PlayStation_3";
            else if (c.Contains("pcsx2") || c.Contains("play")) mappedSlug = "Sony_PlayStation_2";
            else if (c.Contains("vita3k")) mappedSlug = "Sony_PlayStation_Vita";
            else if (c.Contains("ppsspp")) mappedSlug = "Sony_PSP_Street";
            else if (c.Contains("dolphin")) mappedSlug = "Nintendo_GameCube_Wii";
            else if (c.Contains("citra") || c.Contains("panda3ds") || c.Contains("azahar")) mappedSlug = "Nintendo_3DS_Dual";
            else if (c.Contains("xemu") || c.Contains("xenia")) mappedSlug = "Microsoft_Xbox_Original";
            else if (c.Contains("mame") || c.Contains("fbneo") || c.Contains("supermodel")) mappedSlug = "Arcade_MAME_Retro";
            else if (c.Contains("dosbox")) mappedSlug = "MS_DOS_CRT_Amber";
            else if (c.Contains("mednafen_psx") || c.Contains("duckstation") || c.Contains("swanstation") || c.Contains("beetle_psx")) mappedSlug = "Sony_PlayStation";
            else if (c.Contains("snes9x") || c.Contains("bsnes") || c.Contains("mesen-s")) mappedSlug = "Super_Nintendo_SNES";
            else if (c.Contains("fceumm") || c.Contains("nestopia") || c.Contains("mesen")) mappedSlug = "Nintendo_NES_Famicom";
            else if (c.Contains("genesis_plus_gx") || c.Contains("picodrive")) mappedSlug = "Sega_Mega_Drive_Genesis";
            else if (c.Contains("flycast")) mappedSlug = "Sega_Dreamcast_Spiral";
            else if (c.Contains("beetle_saturn") || c.Contains("yabause") || c.Contains("kronos")) mappedSlug = "Sega_Saturn_Titanium";
            else if (c.Contains("mupen64plus") || c.Contains("parallel_n64")) mappedSlug = "Nintendo_64_Ultra";
            else if (c.Contains("mgba") || c.Contains("vba_next")) mappedSlug = "Game_Boy_Advance_Wide";

            if (!string.IsNullOrEmpty(mappedSlug))
            {
                var matched = bezels.FirstOrDefault(b =>
                    Path.GetFileNameWithoutExtension(b.ImagePath).Equals(mappedSlug, StringComparison.OrdinalIgnoreCase) ||
                    b.Name.Contains(mappedSlug.Replace("_", " "), StringComparison.OrdinalIgnoreCase));
                if (matched != null) return matched;
            }
        }

        if (!string.IsNullOrWhiteSpace(platform))
        {
            var match = FindBestBezelForPlatform(platform);
            if (match != null) return match;
        }

        return bezels.FirstOrDefault(b => b.Name.Contains("STORM", StringComparison.OrdinalIgnoreCase)) ?? bezels[0];
    }

    public static BezelPresetItem? FindBestBezelForPlatform(string? platformName)
    {
        var bezels = ScanBezels();
        if (bezels.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(platformName))
            return bezels.FirstOrDefault(b => b.Name.Contains("STORM", StringComparison.OrdinalIgnoreCase)) ?? bezels[0];

        string p = platformName.ToLowerInvariant();
        string targetSlug = "";

        if (p.Contains("ps3") || p.Contains("playstation 3"))
            targetSlug = "Sony_PlayStation_3";
        else if (p.Contains("ps2") || p.Contains("playstation 2"))
            targetSlug = "Sony_PlayStation_2";
        else if (p.Contains("vita") || p.Contains("psvita") || p.Contains("playstation vita"))
            targetSlug = "Sony_PlayStation_Vita";
        else if (p.Contains("psp") || p.Contains("playstation portable"))
            targetSlug = "Sony_PSP_Street";
        else if (p.Contains("gamecube") || p.Contains("wii") || p.Contains("ngc"))
            targetSlug = "Nintendo_GameCube_Wii";
        else if (p.Contains("3ds") || p.Contains("nintendo 3ds"))
            targetSlug = "Nintendo_3DS_Dual";
        else if (p.Contains("xbox"))
            targetSlug = "Microsoft_Xbox_Original";
        else if (p.Contains("nes") || p.Contains("famicom") || p.Contains("dendy"))
            targetSlug = "Nintendo_NES_Famicom";
        else if (p.Contains("snes") || p.Contains("super nintendo") || p.Contains("sfc"))
            targetSlug = "Super_Nintendo_SNES";
        else if (p.Contains("mega drive") || p.Contains("genesis") || p.Contains("32x") || p.Contains("sega cd"))
            targetSlug = "Sega_Mega_Drive_Genesis";
        else if (p.Contains("saturn"))
            targetSlug = "Sega_Saturn_Titanium";
        else if (p.Contains("dreamcast") || p.Contains("naomi"))
            targetSlug = "Sega_Dreamcast_Spiral";
        else if (p.Contains("ps1") || p.Contains("psx") || p.Contains("playstation 1") || p.Contains("playstation"))
            targetSlug = "Sony_PlayStation";
        else if (p.Contains("n64") || p.Contains("nintendo 64"))
            targetSlug = "Nintendo_64_Ultra";
        else if (p.Contains("gba") || p.Contains("game boy advance"))
            targetSlug = "Game_Boy_Advance_Wide";
        else if (p.Contains("dos") || p.Contains("ms-dos") || p.Contains("pc / dos") || p.Contains("ibm"))
            targetSlug = "IBM_DOS_VGA_Monitor";
        else if (p.Contains("amiga") || p.Contains("c64") || p.Contains("commodore"))
            targetSlug = "Commodore_1084S";
        else if (p.Contains("pce") || p.Contains("pc engine") || p.Contains("turbografx") || p.Contains("supergrafx"))
            targetSlug = "PC_Engine_Core";
        else if (p.Contains("neo geo") || p.Contains("neogeo") || p.Contains("mvs") || p.Contains("aes"))
            targetSlug = "Neo_Geo_MVS_Cabinet";
        else if (p.Contains("atari"))
            targetSlug = "Atari_Woodgrain";
        else if (p.Contains("arcade") || p.Contains("mame") || p.Contains("fbneo"))
            targetSlug = "Arcade_Neon_Cabinet";
        else if (p.Contains("trinitron") || p.Contains("pvm"))
            targetSlug = "Sony_Trinitron_PVM";

        if (!string.IsNullOrEmpty(targetSlug))
        {
            var matched = bezels.FirstOrDefault(b => Path.GetFileNameWithoutExtension(b.ImagePath).Equals(targetSlug, StringComparison.OrdinalIgnoreCase) ||
                                                    b.Name.Contains(targetSlug.Replace("_", " "), StringComparison.OrdinalIgnoreCase));
            if (matched != null) return matched;
        }

        return GetBezelForPlatform(platformName) ?? bezels[0];
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

            string v6Marker = Path.Combine(BordersDir, ".storm_bezels_v6_220.ok");
            bool forceRegen = !File.Exists(v6Marker);

            foreach (var tmpl in BuiltinTemplates)
            {
                try
                {
                    string pngPath = Path.Combine(BordersDir, tmpl.FileSlug + ".png");
                    string cfgPath = Path.Combine(BordersDir, tmpl.FileSlug + ".cfg");

                    if (forceRegen || !File.Exists(pngPath))
                    {
                        GenerateBezelPng(pngPath, tmpl.BaseColorHex, tmpl.AccentColorHex, tmpl.FileSlug);
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
                try { File.WriteAllText(v6Marker, "v6_220"); } catch { }
            }
        }
        catch { }
    }

    private static void GenerateBezelPng(string targetPngPath, string baseHex, string accentHex, string slug)
    {
        int width = (int)CanvasWidth;   // 1920
        int height = (int)CanvasHeight; // 1080
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];

        var baseColor = (Color)ColorConverter.ConvertFromString(baseHex);
        var accentColor = (Color)ColorConverter.ConvertFromString(accentHex);

        // 1440x1080 exact mathematical 4:3 cutout centered in 1920x1080 canvas
        int cutLeft = (width - 1440) / 2;       // 240
        int cutRight = cutLeft + 1440;          // 1680
        int cutTop = 0;
        int cutBottom = height;                 // 1080

        bool isAtariWood = slug.Equals("Atari_Woodgrain", StringComparison.OrdinalIgnoreCase);

        for (int y = 0; y < height; y++)
        {
            double yNorm = (double)y / height;
            double vShade = 0.80 + 0.30 * Math.Sin(yNorm * Math.PI);
            int rowOffset = y * stride;

            for (int x = 0; x < width; x++)
            {
                int idx = rowOffset + x * 4;

                bool insideCutout = (x >= cutLeft && x < cutRight && y >= cutTop && y < cutBottom);
                if (insideCutout)
                {
                    // Inner CRT tube bevel shadow along the edges of the 1440x1080 screen
                    int distLeft = x - cutLeft;
                    int distRight = cutRight - 1 - x;
                    int distTop = y - cutTop;
                    int distBottom = cutBottom - 1 - y;
                    int edgeDist = Math.Min(distLeft, distRight);

                    if (edgeDist < 12)
                    {
                        byte shadowAlpha = (byte)((12 - edgeDist) * 11);
                        pixels[idx + 0] = 0;
                        pixels[idx + 1] = 0;
                        pixels[idx + 2] = 0;
                        pixels[idx + 3] = shadowAlpha;
                    }
                    else
                    {
                        // Subtle diagonal glass reflection highlight across the screen cutout
                        double diagPos = x * 0.75 + y * 0.55;
                        if (diagPos >= 700 && diagPos <= 840)
                        {
                            double glareIntensity = Math.Sin((diagPos - 700) / 140.0 * Math.PI);
                            byte glareAlpha = (byte)(glareIntensity * 12); // ~4.7% maximum opacity glass sheen
                            pixels[idx + 0] = 255;
                            pixels[idx + 1] = 255;
                            pixels[idx + 2] = 255;
                            pixels[idx + 3] = glareAlpha;
                        }
                        else
                        {
                            // 100% transparent center viewport
                            pixels[idx + 0] = 0;
                            pixels[idx + 1] = 0;
                            pixels[idx + 2] = 0;
                            pixels[idx + 3] = 0;
                        }
                    }
                }
                else
                {
                    // Outer 3px accent border right against the 1440x1080 screen cutout
                    bool isBevelEdge = (x >= cutLeft - 3 && x < cutLeft) || (x >= cutRight && x < cutRight + 3);

                    if (isBevelEdge)
                    {
                        pixels[idx + 0] = accentColor.B;
                        pixels[idx + 1] = accentColor.G;
                        pixels[idx + 2] = accentColor.R;
                        pixels[idx + 3] = 255;
                    }
                    else
                    {
                        byte r = (byte)Math.Clamp(baseColor.R * vShade, 0, 255);
                        byte g = (byte)Math.Clamp(baseColor.G * vShade, 0, 255);
                        byte b = (byte)Math.Clamp(baseColor.B * vShade, 0, 255);

                        // Authentic woodgrain tone variations for Atari Woodgrain
                        if (isAtariWood)
                        {
                            double woodWave = Math.Sin(y * 0.14) * 14.0 + Math.Sin(x * 0.05) * 8.0;
                            r = (byte)Math.Clamp(r + woodWave * 1.5, 20, 255);
                            g = (byte)Math.Clamp(g + woodWave * 0.8, 12, 220);
                            b = (byte)Math.Clamp(b + woodWave * 0.3, 8, 180);
                        }

                        // Speaker grilles on left and right cabinet panels
                        if ((x >= 40 && x <= 200) || (x >= 1720 && x <= 1880))
                        {
                            if (y >= 260 && y <= 720 && (y % 16 < 5))
                            {
                                r = (byte)(r * 0.45);
                                g = (byte)(g * 0.45);
                                b = (byte)(b * 0.45);
                            }
                        }

                        // Outer metallic/neon stripe along cabinet edges
                        if ((x >= 12 && x <= 16) || (x >= 1903 && x <= 1907))
                        {
                            r = accentColor.R;
                            g = accentColor.G;
                            b = accentColor.B;
                        }

                        // Glowing power LED on lower right sidebar panel
                        if (x >= 1780 && x <= 1824 && y >= 960 && y <= 974)
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
