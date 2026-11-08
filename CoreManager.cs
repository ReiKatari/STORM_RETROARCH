using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace StormRetroArch;

public record CoreInfo(
    string Name,
    string Platform,
    string DllName,
    string Description,
    string Manufacturer = "Universal",
    string SupportedExtensions = "",
    string CatalogVersion = "",
    string JitSupportBadge = "Точный цикл",
    bool SupportsRunAhead = true
)
{
    public bool IsInstalled => CoreManager.IsCoreInstalled(DllName);
    public string Version => CoreManager.GetCoreVersion(DllName, CatalogVersion);
    public string VersionBadge => $"Версия {Version}";
    public string FileSizeText => CoreManager.GetCoreSizeFormatted(DllName);
    public string InstalledStatusBadge => IsInstalled ? $"Установлено ({FileSizeText})" : "Не установлено";
    public string ActionLabel => IsInstalled ? "Обновить" : "Установить";
    public override string ToString() => IsInstalled ? $"{Name} • Установлено ({Version})" : $"{Name} • Доступно ({Version})";
}

public record CoreBenchmarkResult(
    string CoreName,
    string DllFile,
    bool Is64Bit,
    int ApiVersion,
    string LibraryName,
    string LibraryVersion,
    string Extensions,
    double ColdLoadMs,
    bool AllSymbolsValid,
    string SummaryRu
);

public static class CoreManager
{
    public const string BuildbotBaseUrl = "https://buildbot.libretro.com/nightly/windows/x86_64/latest/";
    
    public static readonly string InstallDir = ResolveInstallDir();
    public static readonly string AppDir = EnsureWritableAppDir(InstallDir);
    public static readonly string CoresDir = Path.Combine(AppDir, "cores");
    public static readonly string DownloadsDir = Path.Combine(AppDir, "downloads");
    public static readonly string InfoDir = Directory.Exists(Path.Combine(InstallDir, "info")) ? Path.Combine(InstallDir, "info") : Path.Combine(AppDir, "info");

    private static readonly List<CoreInfo> _allCoresCatalog = new();
    private static readonly Dictionary<string, CoreInfo> _extensionToCoreMap = new(StringComparer.OrdinalIgnoreCase);

    private static string ResolveInstallDir()
    {
        // 1. Prioritize user's primary installation root on N:\STORM RETROARCH
        string preferredRoot = @"N:\STORM RETROARCH";
        if (Directory.Exists(preferredRoot) || Directory.Exists(@"N:\"))
        {
            try
            {
                Directory.CreateDirectory(preferredRoot);
                Directory.CreateDirectory(Path.Combine(preferredRoot, "cores"));
                Directory.CreateDirectory(Path.Combine(preferredRoot, "info"));
                Directory.CreateDirectory(Path.Combine(preferredRoot, "save sync"));
                Directory.CreateDirectory(Path.Combine(preferredRoot, "overlays", "borders"));
                Directory.CreateDirectory(Path.Combine(preferredRoot, "cheats"));
                Directory.CreateDirectory(Path.Combine(preferredRoot, "playlists"));
                return preferredRoot;
            }
            catch { }
        }

        // 2. Check Registry InstallLocation if set
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\STORM RETROARCH");
            string? regDir = key?.GetValue("InstallLocation")?.ToString();
            if (!string.IsNullOrWhiteSpace(regDir) && Directory.Exists(regDir) && !regDir.StartsWith(@"C:\Program Files", StringComparison.OrdinalIgnoreCase))
            {
                return regDir;
            }
        }
        catch { }

        string? startDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (string.IsNullOrEmpty(startDir)) startDir = AppDomain.CurrentDomain.BaseDirectory;

        if (File.Exists(Path.Combine(startDir, "STORM RETROARCH.exe")) ||
            Directory.Exists(Path.Combine(startDir, "cores")))
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(startDir, "cores"));
                Directory.CreateDirectory(Path.Combine(startDir, "info"));
                Directory.CreateDirectory(Path.Combine(startDir, "save sync"));
            }
            catch { }
            return startDir;
        }

        string? current = startDir;
        for (int i = 0; i < 4 && current != null; i++)
        {
            if (Directory.Exists(Path.Combine(current, "cores")) &&
                Directory.Exists(Path.Combine(current, "info")))
                return current;

            current = Directory.GetParent(current)?.FullName;
        }

        return startDir;
    }

    private static bool IsDirectoryWritable(string dirPath)
    {
        try
        {
            if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);
            string probe = Path.Combine(dirPath, ".storm_write_probe.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string EnsureWritableAppDir(string installDir)
    {
        if (IsDirectoryWritable(installDir))
            return installDir;

        string userDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "STORM RETROARCH");
        try
        {
            if (!Directory.Exists(userDir)) Directory.CreateDirectory(userDir);
            string dstCores = Path.Combine(userDir, "cores");
            if (!Directory.Exists(dstCores)) Directory.CreateDirectory(dstCores);

            string srcCores = Path.Combine(installDir, "cores");
            if (Directory.Exists(srcCores))
            {
                foreach (var dll in Directory.GetFiles(srcCores, "*.dll"))
                {
                    string target = Path.Combine(dstCores, Path.GetFileName(dll));
                    if (!File.Exists(target))
                    {
                        try { File.Copy(dll, target, false); } catch { }
                    }
                }
            }

            string srcCfg = Path.Combine(installDir, "retroarch.cfg");
            string dstCfg = Path.Combine(userDir, "retroarch.cfg");
            if (File.Exists(srcCfg) && !File.Exists(dstCfg))
            {
                try { File.Copy(srcCfg, dstCfg, false); } catch { }
            }
        }
        catch { }

        return userDir;
    }

    static CoreManager()
    {
        try
        {
            if (!Directory.Exists(CoresDir)) Directory.CreateDirectory(CoresDir);
            if (!Directory.Exists(DownloadsDir)) Directory.CreateDirectory(DownloadsDir);
            if (!Directory.Exists(InfoDir)) Directory.CreateDirectory(InfoDir);
        }
        catch { }

        InitializeAllCoresFromInfoFiles();
    }

    private static void InitializeAllCoresFromInfoFiles()
    {
        _allCoresCatalog.Clear();
        _extensionToCoreMap.Clear();

        // 1. Add Golden Cores first so they have top priority
        foreach (var gc in GoldenCores)
        {
            _allCoresCatalog.Add(gc);
        }

        // 2. Scan info directory for all .info files
        if (Directory.Exists(InfoDir))
        {
            var infoFiles = Directory.GetFiles(InfoDir, "*.info");
            foreach (var infoPath in infoFiles)
            {
                try
                {
                    string content = File.ReadAllText(infoPath);
                    string fileName = Path.GetFileName(infoPath);
                    string expectedDll = fileName.Replace(".info", ".dll");

                    // Check if already in golden set
                    if (_allCoresCatalog.Any(c => c.DllName.Equals(expectedDll, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    string coreName = ExtractField(content, "corename") ?? Path.GetFileNameWithoutExtension(infoPath).Replace("_libretro", "");
                    string displayName = ExtractField(content, "display_name") ?? coreName;
                    string systemName = ExtractField(content, "systemname") ?? "Libretro Core";
                    string manufacturer = ExtractField(content, "manufacturer") ?? "Universal";
                    string extensions = ExtractField(content, "supported_extensions") ?? "";
                    string displayVer = ExtractField(content, "display_version") ?? "";

                    string desc = $"Официальное ядро Libretro: {displayName} ({systemName}).";
                    if (!string.IsNullOrEmpty(extensions)) desc += $" Форматы: {extensions}";

                    var core = new CoreInfo(displayName, systemName, expectedDll, desc, manufacturer, extensions, displayVer);
                    _allCoresCatalog.Add(core);

                    // Map extensions
                    if (!string.IsNullOrEmpty(extensions))
                    {
                        var parts = extensions.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        foreach (var ext in parts)
                        {
                            string normExt = ext.StartsWith('.') ? ext : "." + ext;
                            if (!_extensionToCoreMap.ContainsKey(normExt))
                            {
                                _extensionToCoreMap[normExt] = core;
                            }
                        }
                    }
                }
                catch { }
            }
        }
    }

    private static string? ExtractField(string content, string key)
    {
        var match = Regex.Match(content, $@"^{key}\s*=\s*""([^""]*)""", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static readonly List<CoreInfo> GoldenCores = new()
    {
        new("Snes9x", "Super Nintendo (SNES)", "snes9x_libretro.dll", "Высокоточный и быстрый эмулятор SNES / Super Famicom", "Nintendo", "smc|sfc|fig|swc", "", "Точный цикл", true),
        new("Genesis Plus GX", "Sega Genesis / Mega Drive", "genesis_plus_gx_libretro.dll", "Идеальная эмуляция Mega Drive, Master System, Game Gear", "Sega", "md|gen|smd|sms|gg|sg", "", "Точный цикл", true),
        new("SwanStation", "Sony PlayStation 1", "swanstation_libretro.dll", "Ультра-производительный PS1 эмулятор с поддержкой PGXP и апскейла", "Sony", "cue|chd|pbp|iso|bin|m3u", "", "JIT Dynarec", true),
        new("FCEUmm", "Nintendo Entertainment System", "fceumm_libretro.dll", "Современный эмулятор NES / Famicom / Dendy с поддержкой мапперов", "Nintendo", "nes|fds|unf|unif", "", "Точный цикл", true),
        new("mGBA", "Game Boy Advance", "mgba_libretro.dll", "Быстрый и точный эмулятор GBA, GBC и классического Game Boy", "Nintendo", "gba|gb|gbc", "", "JIT Dynarec", true),
        new("melonDS DS", "Nintendo DS / DSi", "melondsds_libretro.dll", "Превосходная эмуляция Nintendo DS и DSi с сенсорным экраном", "Nintendo", "nds|dsi", "", "JIT Dynarec", true),
        new("Mupen64Plus-Next", "Nintendo 64", "mupen64plus_next_libretro.dll", "Мощный эмулятор N64 с плагинами GLideN64 и ParaLLEl RDP", "Nintendo", "n64|v64|z64", "", "JIT Dynarec", true),
        new("PPSSPP", "Sony PlayStation Portable", "ppsspp_libretro.dll", "Быстрая эмуляция PSP с HD текстурами и шейдерами", "Sony", "iso|cso|pbp", "", "JIT Dynarec", false),
        new("Flycast", "Sega Dreamcast / NAOMI", "flycast_libretro.dll", "Современный мультиплатформенный эмулятор Dreamcast и NAOMI", "Sega", "cdi|gdi|chd|cue", "", "JIT Dynarec", false),
        new("Beetle Saturn", "Sega Saturn", "mednafen_saturn_libretro.dll", "Точнейшая потактовая эмуляция архитектуры Sega Saturn", "Sega", "cue|ccd|chd|toc|m3u", "", "Точный цикл", true),
        new("FBNeo", "Arcade / Neo Geo", "fbneo_libretro.dll", "Флагманский эмулятор аркадных автоматов, Neo Geo и CPS-1/2/3", "Arcade", "zip|7z", "", "Точный цикл", true),
        new("DOSBox-Pure", "MS-DOS PC", "dosbox_pure_libretro.dll", "Запуск классических игр MS-DOS прямо из ZIP-архивов с геймпадом", "PC / DOS", "zip|dosz|exe|com|bat|iso", "", "JIT Dynarec", true),
        new("Beetle PCE Fast", "PC Engine / TurboGrafx-16", "mednafen_pce_fast_libretro.dll", "Быстрый эмулятор PC Engine HuCard и CD-ROM", "NEC", "pce|sgx|cue|ccd|chd", "", "Точный цикл", true),
        new("Stella", "Atari 2600", "stella_libretro.dll", "Эталонный эмулятор классической приставки Atari 2600 VCS", "Atari", "a26|bin", "", "Точный цикл", true),
        new("Handy", "Atari Lynx", "handy_libretro.dll", "Эмулятор портативной консоли Atari Lynx", "Atari", "lnx", "", "Точный цикл", true),
        new("Beetle NeoPop", "Neo Geo Pocket", "mednafen_ngp_libretro.dll", "Эмулятор Neo Geo Pocket и Pocket Color", "SNK", "ngp|ngc", "", "Точный цикл", true),
        new("Beetle WonderSwan", "Bandai WonderSwan", "mednafen_wswan_libretro.dll", "Эмулятор портативной консоли WonderSwan / Color", "Bandai", "ws|wsc", "", "Точный цикл", true),
        new("Citra", "Nintendo 3DS", "citra_libretro.dll", "Эмулятор Nintendo 3DS с апскейлом геометрии", "Nintendo", "3ds|3dsx|elf|cci|cxi", "", "JIT Dynarec", false),
        new("Dolphin", "Nintendo GameCube / Wii", "dolphin_libretro.dll", "Флагманский эмулятор GameCube и Wii с высокой кадровой частотой", "Nintendo", "gcm|iso|wbfs|rvz|cso|wad", "", "JIT Dynarec", false),
        new("Gambatte", "Game Boy / Color", "gambatte_libretro.dll", "Потактово точный эмулятор оригинального Game Boy", "Nintendo", "gb|gbc|dmg", "", "Точный цикл", true),
        new("Picodrive", "Sega 32X / Sega CD", "picodrive_libretro.dll", "Оптимизированный эмулятор Sega 32X, Mega-CD и Genesis", "Sega", "bin|gen|smd|md|32x|cue|iso", "", "JIT Dynarec", true),
        new("VBA-M", "Game Boy Advance", "vbam_libretro.dll", "Продвинутый эмулятор Game Boy Advance с отладкой", "Nintendo", "gba|gb|gbc", "", "Точный цикл", true),
        new("PCSX-ReARMed", "Sony PlayStation 1", "pcsx_rearmed_libretro.dll", "Быстрый эмулятор PS1 для систем любого уровня", "Sony", "bin|cue|img|mdf|pbp|cbn|chd", "", "JIT Dynarec", true),
        new("Beetle PSX HW", "Sony PlayStation 1 (Vulkan)", "mednafen_psx_hw_libretro.dll", "Аппаратно-ускоренный PS1 с Vulkan/OpenGL рендером", "Sony", "cue|ccd|chd|toc|m3u|pbp", "", "Vulkan HW", false),
        new("MAME 2003-Plus", "Arcade Classics", "mame2003_plus_libretro.dll", "Классический аркадный движок с сотнями исправлений", "Arcade", "zip", "", "Точный цикл", true)
    };

    public static List<CoreInfo> AllCoresCatalog => _allCoresCatalog;

    private static DateTime _lastCoresDirWriteTimeUtc = DateTime.MinValue;
    private static readonly HashSet<string> _installedCoresCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> _coreSizeCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _coresCacheLock = new();

    public static void RefreshCoresCache(bool force = false)
    {
        lock (_coresCacheLock)
        {
            if (!Directory.Exists(CoresDir))
            {
                _installedCoresCache.Clear();
                _coreSizeCache.Clear();
                return;
            }

            try
            {
                DateTime curWrite = Directory.GetLastWriteTimeUtc(CoresDir);
                if (!force && curWrite == _lastCoresDirWriteTimeUtc && _installedCoresCache.Count > 0)
                {
                    return;
                }

                _installedCoresCache.Clear();
                _coreSizeCache.Clear();

                var files = Directory.GetFiles(CoresDir, "*.dll");
                foreach (var f in files)
                {
                    string dll = Path.GetFileName(f);
                    _installedCoresCache.Add(dll);

                    try
                    {
                        var fi = new FileInfo(f);
                        long bytes = fi.Length;
                        if (bytes >= 1024 * 1024)
                        {
                            double mb = bytes / (1024.0 * 1024.0);
                            _coreSizeCache[dll] = $"{mb:0.0} МБ".Replace('.', ',');
                        }
                        else
                        {
                            double kb = bytes / 1024.0;
                            _coreSizeCache[dll] = $"{kb:0.0} КБ".Replace('.', ',');
                        }
                    }
                    catch { }
                }
                _lastCoresDirWriteTimeUtc = curWrite;
            }
            catch { }
        }
    }

    public static bool IsCoreInstalled(string dllName)
    {
        if (string.IsNullOrWhiteSpace(dllName)) return false;
        RefreshCoresCache();
        lock (_coresCacheLock)
        {
            return _installedCoresCache.Contains(dllName);
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _coreVersionCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> KnownCoreVersions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fceumm_libretro.dll"] = "1.19.1",
        ["mesen_libretro.dll"] = "0.9.9",
        ["nestopia_libretro.dll"] = "1.52.1",
        ["quicknes_libretro.dll"] = "1.0.2",
        ["snes9x_libretro.dll"] = "1.62.3",
        ["bsnes_libretro.dll"] = "115",
        ["genesis_plus_gx_libretro.dll"] = "1.7.4",
        ["picodrive_libretro.dll"] = "2.00",
        ["dosbox_pure_libretro.dll"] = "0.9.9",
        ["dosbox_core_libretro.dll"] = "0.74",
        ["dosbox_svn_libretro.dll"] = "0.74-SVN",
        ["swanstation_libretro.dll"] = "1.0.0",
        ["pcsx_rearmed_libretro.dll"] = "r24",
        ["mednafen_psx_hw_libretro.dll"] = "1.32.1",
        ["mgba_libretro.dll"] = "0.10.5",
        ["vbam_libretro.dll"] = "2.1.9",
        ["gambatte_libretro.dll"] = "0.5.0",
        ["mupen64plus_next_libretro.dll"] = "2.6",
        ["flycast_libretro.dll"] = "2.3",
        ["ppsspp_libretro.dll"] = "1.17.1",
        ["melonds_libretro.dll"] = "0.9.5",
        ["melondsds_libretro.dll"] = "0.9.5",
        ["pcsx2_libretro.dll"] = "1.7.5",
        ["fbneo_libretro.dll"] = "1.0.0.03",
        ["mame2003_plus_libretro.dll"] = "2003+",
        ["stella_libretro.dll"] = "7.0",
        ["handy_libretro.dll"] = "0.97",
        ["mednafen_pce_fast_libretro.dll"] = "1.32.1",
        ["mednafen_saturn_libretro.dll"] = "1.32.1",
        ["mednafen_ngp_libretro.dll"] = "1.32.1",
        ["mednafen_wswan_libretro.dll"] = "1.32.1",
        ["opera_libretro.dll"] = "1.0.0",
        ["scummvm_libretro.dll"] = "2.8.1",
        ["puae_libretro.dll"] = "2.6.1",
        ["vice_x64_libretro.dll"] = "3.8",
        ["citra_libretro.dll"] = "0.1.0",
        ["dolphin_libretro.dll"] = "5.0"
    };

    public static string GetCoreVersion(string dllName, string fallbackVersion = "")
    {
        if (string.IsNullOrWhiteSpace(dllName)) return "1.19.0";
        if (_coreVersionCache.TryGetValue(dllName, out var cached)) return cached;

        string version = "";

        // 1. Try to read display_version from info file
        try
        {
            string baseName = Path.GetFileNameWithoutExtension(dllName);
            string infoPath = Path.Combine(InfoDir, baseName + ".info");
            if (File.Exists(infoPath))
            {
                string content = File.ReadAllText(infoPath);
                string? v = ExtractField(content, "display_version");
                if (!string.IsNullOrWhiteSpace(v))
                {
                    version = v.Trim();
                }
            }
        }
        catch { }

        // 2. Fallback to CatalogVersion passed in
        if (string.IsNullOrWhiteSpace(version) && !string.IsNullOrWhiteSpace(fallbackVersion))
        {
            version = fallbackVersion.Trim();
        }

        // 3. Fallback to KnownCoreVersions dictionary
        if (string.IsNullOrWhiteSpace(version) && KnownCoreVersions.TryGetValue(dllName, out var known))
        {
            version = known;
        }

        // 4. Default version
        if (string.IsNullOrWhiteSpace(version))
        {
            version = "1.19.0";
        }

        _coreVersionCache[dllName] = version;
        return version;
    }

    public static string GetCoreSizeFormatted(string dllName)
    {
        if (string.IsNullOrWhiteSpace(dllName)) return "Онлайн-пакет";
        RefreshCoresCache();
        lock (_coresCacheLock)
        {
            if (_coreSizeCache.TryGetValue(dllName, out var cachedSize))
                return cachedSize;
        }
        return "Онлайн-пакет";
    }


    public static List<CoreInfo> GetCoresByManufacturer(string manufacturer)
    {
        if (string.IsNullOrEmpty(manufacturer) || manufacturer == "Все ядра (350+)")
            return AllCoresCatalog;

        return AllCoresCatalog.Where(c => c.Manufacturer.Equals(manufacturer, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public static (string Platform, string CoreDll, string CoreName) IdentifyRom(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();

        // 1. Smart ZIP inspection
        if (ext == ".zip" && File.Exists(filePath))
        {
            if (filePath.Contains("MS-DOS", StringComparison.OrdinalIgnoreCase) ||
                filePath.Contains("(DOS-", StringComparison.OrdinalIgnoreCase) ||
                filePath.Contains(" DOS-", StringComparison.OrdinalIgnoreCase))
            {
                return ("MS-DOS PC", "dosbox_pure_libretro.dll", "DOSBox-Pure");
            }

            try
            {
                using var archive = ZipFile.OpenRead(filePath);

                // First check if archive contains any MS-DOS executable/batch/image
                if (archive.Entries.Any(e => !string.IsNullOrEmpty(e.Name) &&
                    (Path.GetExtension(e.Name).ToLowerInvariant() is ".exe" or ".com" or ".bat" or ".dosz")))
                {
                    return ("MS-DOS PC", "dosbox_pure_libretro.dll", "DOSBox-Pure");
                }
                
                // Check if any entry inside has a known console ROM extension
                foreach (var entry in archive.Entries)
                {
                    string entryExt = Path.GetExtension(entry.Name).ToLowerInvariant();
                    if (!string.IsNullOrEmpty(entryExt) && entryExt != ".zip" && entryExt != ".txt" && entryExt != ".nfo")
                    {
                        (string Platform, string CoreDll, string CoreName) candidate = IdentifyRomByExtension(entryExt);
                        if (candidate.CoreDll != "fbneo_libretro.dll" && candidate.Platform != "Неизвестная платформа")
                        {
                            return candidate;
                        }
                    }
                }
            }
            catch { }

            // If only arcade ROM chips / bins or unreadable, default to Arcade FBNeo
            return ("Arcade / Neo Geo", "fbneo_libretro.dll", "FBNeo");
        }

        var baseResult = IdentifyRomByExtension(ext);
        return ApplyUserCoreOverrides(filePath, baseResult.Platform, baseResult.CoreDll, baseResult.CoreName);
    }

    public static (string Platform, string CoreDll, string CoreName) ApplyUserCoreOverrides(string? filePath, string platform, string defaultDll, string defaultName)
    {
        try
        {
            if (!string.IsNullOrEmpty(filePath) &&
                SettingsManager.Current.GameCoreOverrides != null &&
                SettingsManager.Current.GameCoreOverrides.TryGetValue(filePath, out string? gameOverride) &&
                !string.IsNullOrWhiteSpace(gameOverride))
            {
                var parts = gameOverride.Split('|');
                if (parts.Length >= 2) return (platform, parts[0], parts[1]);
                return (platform, gameOverride, GetCoreDisplayNameByDll(gameOverride));
            }

            if (!string.IsNullOrEmpty(platform) &&
                SettingsManager.Current.PlatformDefaultCores != null &&
                SettingsManager.Current.PlatformDefaultCores.TryGetValue(platform, out string? platOverride) &&
                !string.IsNullOrWhiteSpace(platOverride))
            {
                var parts = platOverride.Split('|');
                if (parts.Length >= 2) return (platform, parts[0], parts[1]);
                return (platform, platOverride, GetCoreDisplayNameByDll(platOverride));
            }
        }
        catch { }

        return (platform, defaultDll, defaultName);
    }

    public static string GetCoreDisplayNameByDll(string dllName)
    {
        if (string.IsNullOrWhiteSpace(dllName)) return "Libretro Core";
        var found = _allCoresCatalog.FirstOrDefault(c => c.DllName.Equals(dllName, StringComparison.OrdinalIgnoreCase));
        if (found != null) return found.Name;

        return dllName.Replace("_libretro.dll", "", StringComparison.OrdinalIgnoreCase) switch
        {
            "fceumm" => "FCEUmm",
            "mesen" => "Mesen",
            "nestopia" => "Nestopia UE",
            "quicknes" => "QuickNES",
            "snes9x" => "Snes9x",
            "bsnes" => "bsnes",
            "mesen-s" => "Mesen-S",
            "genesis_plus_gx" => "Genesis Plus GX",
            "picodrive" => "PicoDrive",
            "blastem" => "BlastEm",
            "swanstation" => "SwanStation",
            "mednafen_psx_hw" => "Beetle PSX HW",
            "pcsx_rearmed" => "PCSX-ReARMed",
            "dosbox_pure" => "DOSBox-Pure",
            "dosbox_core" => "DOSBox-Core",
            "dosbox_svn" => "DOSBox-SVN",
            "mgba" => "mGBA",
            "vbam" => "VBA-M",
            "gpsp" => "gpSP",
            "gambatte" => "Gambatte",
            "sameboy" => "SameBoy",
            "mupen64plus_next" => "Mupen64Plus-Next",
            "parallel_n64" => "ParaLLEl N64",
            "melondsds" => "melonDS DS",
            "desmume" => "DeSmuME",
            "ppsspp" => "PPSSPP",
            "flycast" => "Flycast",
            "mednafen_saturn" => "Beetle Saturn",
            "yabause" => "Yabause",
            "fbneo" => "FinalBurn Neo",
            "mame2003_plus" => "MAME 2003-Plus",
            "dolphin" => "Dolphin",
            "citra" => "Citra",
            "pcsx2" => "LRPS2 (PCSX2)",
            _ => dllName.Replace("_libretro.dll", "", StringComparison.OrdinalIgnoreCase)
        };
    }

    public static List<CoreInfo> GetCompatibleCoresForPlatform(string platform, string ext = "")
    {
        var list = new List<CoreInfo>();
        string p = (platform ?? "").Trim();

        void AddCandidate(string name, string dll, string desc)
        {
            if (!list.Any(c => c.DllName.Equals(dll, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(new CoreInfo(name, string.IsNullOrEmpty(p) ? "Универсальная платформа" : p, dll, desc));
            }
        }

        if (p.Contains("Nintendo Entertainment System", StringComparison.OrdinalIgnoreCase) || p.Equals("NES", StringComparison.OrdinalIgnoreCase) || ext is ".nes" or ".fds" or ".unf")
        {
            AddCandidate("FCEUmm", "fceumm_libretro.dll", "Стандартное быстрое и совместимое ядро NES / Famicom / Dendy (рекомендуется)");
            AddCandidate("Mesen", "mesen_libretro.dll", "Сверхточное эталонное ядро NES с поддержкой HD-паков");
            AddCandidate("Nestopia UE", "nestopia_libretro.dll", "Классическое точное ядро NES с гибкой настройкой палитр");
            AddCandidate("QuickNES", "quicknes_libretro.dll", "Ультра-быстрое ядро NES с минимальной нагрузкой на процессор");
        }
        else if (p.Contains("MS-DOS", StringComparison.OrdinalIgnoreCase) || p.Contains("DOS", StringComparison.OrdinalIgnoreCase) || ext is ".dosz" or ".exe" or ".com" or ".bat")
        {
            AddCandidate("DOSBox-Pure", "dosbox_pure_libretro.dll", "Автозапуск игр из ZIP-архивов, образов CD/ISO и дискет без командной строки (рекомендуется)");
            AddCandidate("DOSBox-Core", "dosbox_core_libretro.dll", "Продвинутое ядро DOSBox с поддержкой MT-32, Voodoo 3dfx и тонкой настройкой ЦП");
            AddCandidate("DOSBox-SVN", "dosbox_svn_libretro.dll", "Классическая сборка DOSBox SVN для старых программ и демосцен");
        }
        else if (p.Contains("Super Nintendo", StringComparison.OrdinalIgnoreCase) || p.Contains("SNES", StringComparison.OrdinalIgnoreCase) || ext is ".sfc" or ".smc")
        {
            AddCandidate("Snes9x", "snes9x_libretro.dll", "Идеальный баланс скорости и совместимости для всех игр Super Nintendo (рекомендуется)");
            AddCandidate("bsnes", "bsnes_libretro.dll", "Побитовая аппаратная точность Super Nintendo и чипов Super FX / SA-1");
            AddCandidate("Mesen-S", "mesen-s_libretro.dll", "Высокоточный эмулятор SNES и Super Game Boy");
        }
        else if (p.Contains("Genesis", StringComparison.OrdinalIgnoreCase) || p.Contains("Mega Drive", StringComparison.OrdinalIgnoreCase) || p.Contains("Master System", StringComparison.OrdinalIgnoreCase) || ext is ".md" or ".gen" or ".smd" or ".sms" or ".gg")
        {
            AddCandidate("Genesis Plus GX", "genesis_plus_gx_libretro.dll", "Эталонная точность Sega Mega Drive, Sega CD, Master System и Game Gear (рекомендуется)");
            AddCandidate("PicoDrive", "picodrive_libretro.dll", "Высокоскоростное ядро с полной поддержкой Sega 32X и Mega Drive");
            AddCandidate("BlastEm", "blastem_libretro.dll", "Цикло-точный низкоуровневый эмулятор Sega Mega Drive / Genesis");
        }
        else if (p.Contains("PlayStation 1", StringComparison.OrdinalIgnoreCase) || p.Equals("Sony PlayStation", StringComparison.OrdinalIgnoreCase) || ext is ".cue" or ".pbp")
        {
            AddCandidate("SwanStation", "swanstation_libretro.dll", "Аппаратный рендеринг Vulkan/D3D11, PGXP-коррекция геометрии и 4K-апскейл PS1 (рекомендуется)");
            AddCandidate("Beetle PSX HW", "mednafen_psx_hw_libretro.dll", "Высокоточное ядро Mednafen PSX с аппаратным ускорением GPU");
            AddCandidate("PCSX-ReARMed", "pcsx_rearmed_libretro.dll", "Быстрое классическое ядро PlayStation 1 с высокой совместимостью");
        }
        else if (p.Contains("Game Boy Advance", StringComparison.OrdinalIgnoreCase) || p.Equals("GBA", StringComparison.OrdinalIgnoreCase) || ext == ".gba")
        {
            AddCandidate("mGBA", "mgba_libretro.dll", "Лучшее современное ядро Game Boy Advance с высокой точностью и скоростью (рекомендуется)");
            AddCandidate("VBA-M", "vbam_libretro.dll", "Классический эмулятор VisualBoyAdvance-M с фильтрами и читами");
            AddCandidate("gpSP", "gpsp_libretro.dll", "Лёгкое и сверхбыстрое ядро Game Boy Advance");
        }
        else if (p.Contains("Game Boy", StringComparison.OrdinalIgnoreCase) || ext is ".gb" or ".gbc")
        {
            AddCandidate("Gambatte", "gambatte_libretro.dll", "Точный эмулятор оригинального Game Boy и Game Boy Color с палитрами (рекомендуется)");
            AddCandidate("SameBoy", "sameboy_libretro.dll", "Эталонная точность звука и графики Game Boy / Color / Super Game Boy");
            AddCandidate("mGBA", "mgba_libretro.dll", "Универсальное ядро с поддержкой картриджей GB / GBC / GBA");
        }
        else if (p.Contains("Nintendo 64", StringComparison.OrdinalIgnoreCase) || ext is ".n64" or ".z64" or ".v64")
        {
            AddCandidate("Mupen64Plus-Next", "mupen64plus_next_libretro.dll", "Современный эмулятор Nintendo 64 с плагинами GLideN64 и ParaLLEl-RDP (рекомендуется)");
            AddCandidate("ParaLLEl N64", "parallel_n64_libretro.dll", "Высокоскоростное ядро Nintendo 64 с поддержкой Vulkan");
        }
        else if (p.Contains("Nintendo DS", StringComparison.OrdinalIgnoreCase) || ext is ".nds" or ".dsi")
        {
            AddCandidate("melonDS DS", "melondsds_libretro.dll", "Современное точное ядро Nintendo DS / DSi с поддержкой микрофона и сети (рекомендуется)");
            AddCandidate("DeSmuME", "desmume_libretro.dll", "Классическое ядро Nintendo DS с повышением внутреннего 3D-разрешения");
        }
        else if (p.Contains("PlayStation Portable", StringComparison.OrdinalIgnoreCase) || p.Contains("PSP", StringComparison.OrdinalIgnoreCase) || ext == ".cso")
        {
            AddCandidate("PPSSPP", "ppsspp_libretro.dll", "Эталонный эмулятор Sony PSP с поддержкой 4K-рендеринга и 60 FPS (рекомендуется)");
        }
        else if (p.Contains("Dreamcast", StringComparison.OrdinalIgnoreCase) || ext is ".cdi" or ".gdi")
        {
            AddCandidate("Flycast", "flycast_libretro.dll", "Полноценная эмуляция Sega Dreamcast, NAOMI и Atomiswave (рекомендуется)");
        }
        else if (p.Contains("Saturn", StringComparison.OrdinalIgnoreCase) || ext == ".sat")
        {
            AddCandidate("Beetle Saturn", "mednafen_saturn_libretro.dll", "Наиболее точное ядро Sega Saturn (рекомендуется)");
            AddCandidate("Yabause", "yabause_libretro.dll", "Альтернативное ядро Sega Saturn с аппаратным ускорением");
        }
        else if (p.Contains("Arcade", StringComparison.OrdinalIgnoreCase) || p.Contains("Neo Geo", StringComparison.OrdinalIgnoreCase))
        {
            AddCandidate("FinalBurn Neo", "fbneo_libretro.dll", "Лучшее ядро для аркадных автоматов CPS-1/2/3, Neo Geo и Konami (рекомендуется)");
            AddCandidate("MAME 2003-Plus", "mame2003_plus_libretro.dll", "Классический универсальный эмулятор аркадных автоматов MAME");
        }

        // Also match any installed or catalog cores matching platform keywords
        foreach (var c in _allCoresCatalog)
        {
            if (!string.IsNullOrEmpty(p) && c.Platform.Contains(p.Split(' ')[0], StringComparison.OrdinalIgnoreCase))
            {
                AddCandidate(c.Name, c.DllName, c.Description);
            }
        }

        // Fallback so list is never empty
        if (list.Count == 0)
        {
            AddCandidate("FCEUmm", "fceumm_libretro.dll", "Nintendo Entertainment System (NES / Dendy)");
            AddCandidate("DOSBox-Pure", "dosbox_pure_libretro.dll", "MS-DOS PC (Автозапуск архивов и образов)");
            AddCandidate("Snes9x", "snes9x_libretro.dll", "Super Nintendo (SNES)");
            AddCandidate("Genesis Plus GX", "genesis_plus_gx_libretro.dll", "Sega Mega Drive / Genesis");
            AddCandidate("SwanStation", "swanstation_libretro.dll", "Sony PlayStation 1");
            AddCandidate("mGBA", "mgba_libretro.dll", "Game Boy Advance");
            AddCandidate("FinalBurn Neo", "fbneo_libretro.dll", "Аркадные автоматы (Arcade / Neo Geo)");
        }

        return list;
    }

    public static List<string> GetStandardPlatformsList() => new()
    {
        "Nintendo Entertainment System",
        "MS-DOS PC",
        "Super Nintendo (SNES)",
        "Sega Genesis / Mega Drive",
        "Sony PlayStation 1",
        "Game Boy Advance",
        "Game Boy / Color",
        "Nintendo 64",
        "Nintendo DS / DSi",
        "Sony PlayStation Portable",
        "Sega Dreamcast",
        "Sega Saturn",
        "Arcade Classics"
    };

    public static (string Platform, string CoreDll, string CoreName) IdentifyRomByExtension(string ext)
    {
        // 1. Check curated primary platforms
        (string Platform, string CoreDll, string CoreName) result = ext switch
        {
            ".nes" or ".fds" or ".unf" => ("Nintendo Entertainment System", "fceumm_libretro.dll", "FCEUmm"),
            ".sfc" or ".smc" or ".fig" or ".swc" => ("Super Nintendo (SNES)", "snes9x_libretro.dll", "Snes9x"),
            ".md" or ".gen" or ".smd" or ".sms" or ".gg" or ".sg" => ("Sega Genesis / Mega Drive", "genesis_plus_gx_libretro.dll", "Genesis Plus GX"),
            ".bin" or ".cue" or ".chd" or ".pbp" => ("Sony PlayStation 1", "swanstation_libretro.dll", "SwanStation"),
            ".iso" => ("Sony PlayStation / DVD", "swanstation_libretro.dll", "SwanStation"),
            ".n64" or ".z64" or ".v64" => ("Nintendo 64", "mupen64plus_next_libretro.dll", "Mupen64Plus-Next"),
            ".gba" => ("Game Boy Advance", "mgba_libretro.dll", "mGBA"),
            ".gb" or ".gbc" or ".dmg" => ("Game Boy / Color", "gambatte_libretro.dll", "Gambatte"),
            ".nds" or ".dsi" => ("Nintendo DS / DSi", "melondsds_libretro.dll", "melonDS DS"),
            ".3ds" or ".3dsx" or ".cci" or ".cxi" => ("Nintendo 3DS", "citra_libretro.dll", "Citra"),
            ".cso" => ("Sony PlayStation Portable", "ppsspp_libretro.dll", "PPSSPP"),
            ".cdi" or ".gdi" => ("Sega Dreamcast", "flycast_libretro.dll", "Flycast"),
            ".sat" => ("Sega Saturn", "mednafen_saturn_libretro.dll", "Beetle Saturn"),
            ".pce" or ".sgx" => ("PC Engine / TurboGrafx-16", "mednafen_pce_fast_libretro.dll", "Beetle PCE Fast"),
            ".ngp" or ".ngc" => ("Neo Geo Pocket / Color", "mednafen_ngp_libretro.dll", "Beetle NeoPop"),
            ".ws" or ".wsc" => ("WonderSwan / Color", "mednafen_wswan_libretro.dll", "Beetle WonderSwan"),
            ".a26" => ("Atari 2600", "stella_libretro.dll", "Stella"),
            ".a78" => ("Atari 7800", "prosystem_libretro.dll", "ProSystem"),
            ".lnx" => ("Atari Lynx", "handy_libretro.dll", "Handy"),
            ".jag" => ("Atari Jaguar", "virtualjaguar_libretro.dll", "Virtual Jaguar"),
            ".dosz" or ".exe" or ".com" or ".bat" => ("MS-DOS PC", "dosbox_pure_libretro.dll", "DOSBox-Pure"),
            ".rvz" or ".gcm" or ".wbfs" or ".wad" => ("Nintendo GameCube / Wii", "dolphin_libretro.dll", "Dolphin"),
            ".d64" or ".t64" => ("Commodore 64", "vice_x64sc_libretro.dll", "VICE C64"),
            ".adf" => ("Commodore Amiga", "puae_libretro.dll", "PUAE"),
            _ => ("", "", "")
        };

        if (!string.IsNullOrEmpty(result.Platform)) return result;

        // 2. Check dynamically loaded info map
        string cleanExt = ext.TrimStart('.');
        if (_extensionToCoreMap.TryGetValue("." + cleanExt, out var infoCore))
        {
            return (infoCore.Platform, infoCore.DllName, infoCore.Name);
        }

        return ("Неизвестная платформа", "fbneo_libretro.dll", "Libretro Core");
    }

    public static async Task<bool> EnsureCoreDownloadedAsync(string dllName)
    {
        string targetDllPath = Path.Combine(CoresDir, dllName);
        if (File.Exists(targetDllPath)) return true;
        return await DownloadCoreAsync(dllName);
    }

    public static async Task<bool> DownloadCoreAsync(string dllName, IProgress<(double percent, string status)>? progress = null)
    {
        string targetDllPath = Path.Combine(CoresDir, dllName);
        if (File.Exists(targetDllPath))
        {
            progress?.Report((100, "Уже установлено"));
            return true;
        }

        string zipName = dllName + ".zip";
        string url = BuildbotBaseUrl + zipName;
        string tempZip = Path.Combine(DownloadsDir, zipName);

        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(3);

            progress?.Report((5, $"Подключение к Libretro Buildbot..."));

            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                progress?.Report((0, $"Ошибка HTTP {response.StatusCode}"));
                return false;
            }

            long totalBytes = response.Content.Headers.ContentLength ?? -1;
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);

            byte[] buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    double pct = (double)totalRead / totalBytes * 80.0;
                    progress?.Report((pct, $"Загрузка {totalRead / 1024} КБ из {totalBytes / 1024} КБ..."));
                }
            }

            fileStream.Close();
            progress?.Report((85, "Распаковка ядра..."));

            await Task.Run(() =>
            {
                using var archive = ZipFile.OpenRead(tempZip);
                foreach (var entry in archive.Entries)
                {
                    if (entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        string outPath = Path.Combine(CoresDir, entry.Name);
                        entry.ExtractToFile(outPath, overwrite: true);
                    }
                }
            });

            try { File.Delete(tempZip); } catch { }

            progress?.Report((100, "Установка завершена!"));
            return true;
        }
        catch (Exception ex)
        {
            progress?.Report((0, $"Сбой: {ex.Message}"));
            return false;
        }
    }

    public static void LaunchGame(string romPath, string? coreDll = null, Action<string>? onError = null)
    {
        // 100% In-Process Emulation Mandate: NativeLibretroHost handles all emulation
    }

        public static async Task<Process?> LaunchEmbeddedGameAsync(RomItem rom, Action<string>? onStatus = null, Action<string>? onError = null)
    {
        // All game launching runs in-process via NativeLibretroHost
        await Task.CompletedTask;
        return null;
    }

    public static async Task<bool> LaunchGameAsync(RomItem rom, Action<string>? onStatus = null, Action<string>? onError = null)
    {
        var proc = await LaunchEmbeddedGameAsync(rom, onStatus, onError);
        return proc != null;
    }

    public static void LaunchOzone(Action<string>? onError = null) { }

    public static void LaunchDesktopQt(Action<string>? onError = null) { }

    public static string? FindRetroArchExecutable()
    {
        string exe = Path.Combine(AppDir, "retroarch.exe");
        if (File.Exists(exe)) return exe;
        string parent = Path.Combine(Directory.GetParent(AppDir)?.FullName ?? "", "retroarch.exe");
        if (File.Exists(parent)) return parent;
        return null;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint RetroApiVersionDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RetroGetSystemInfoDelegate(out NativeLibretroHost.RetroSystemInfo info);

    public static async Task<CoreBenchmarkResult> InspectAndBenchmarkCoreAsync(CoreInfo core)
    {
        return await Task.Run(() =>
        {
            string dllPath = Path.Combine(CoresDir, core.DllName);
            if (!File.Exists(dllPath))
            {
                return new CoreBenchmarkResult(
                    core.Name,
                    core.DllName,
                    false,
                    0,
                    core.Name,
                    "Не установлено",
                    core.SupportedExtensions,
                    0.0,
                    false,
                    $"Файл ядра {core.DllName} отсутствует на диске."
                );
            }

            bool is64Bit = false;
            try
            {
                using var fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var br = new BinaryReader(fs);
                fs.Seek(0x3C, SeekOrigin.Begin);
                int peOffset = br.ReadInt32();
                fs.Seek(peOffset, SeekOrigin.Begin);
                uint peSignature = br.ReadUInt32(); // 'PE\0\0'
                if (peSignature == 0x00004550)
                {
                    ushort machine = br.ReadUInt16();
                    is64Bit = (machine == 0x8664); // AMD64
                }
            }
            catch { }

            var sw = Stopwatch.StartNew();
            IntPtr hDll = IntPtr.Zero;
            bool loaded = NativeLibrary.TryLoad(dllPath, out hDll);
            double coldLoadMs = sw.Elapsed.TotalMilliseconds;

            if (!loaded || hDll == IntPtr.Zero)
            {
                return new CoreBenchmarkResult(
                    core.Name,
                    core.DllName,
                    is64Bit,
                    0,
                    core.Name,
                    "Ошибка загрузки",
                    core.SupportedExtensions,
                    coldLoadMs,
                    false,
                    $"Не удалось загрузить бинарный модуль {core.DllName} в адресное пространство."
                );
            }

            try
            {
                bool hasInit = NativeLibrary.TryGetExport(hDll, "retro_init", out var pInit);
                bool hasRun = NativeLibrary.TryGetExport(hDll, "retro_run", out var pRun);
                bool hasSerialize = NativeLibrary.TryGetExport(hDll, "retro_serialize", out var pSerialize);
                bool hasApiVer = NativeLibrary.TryGetExport(hDll, "retro_api_version", out var pApiVer);
                bool hasSysInfo = NativeLibrary.TryGetExport(hDll, "retro_get_system_info", out var pSysInfo);

                bool allSymbolsValid = hasInit && hasRun && hasApiVer && hasSysInfo;

                int apiVersion = 0;
                if (hasApiVer)
                {
                    var apiVerFn = Marshal.GetDelegateForFunctionPointer<RetroApiVersionDelegate>(pApiVer);
                    apiVersion = (int)apiVerFn();
                }

                string libName = core.Name;
                string libVersion = "1.0.0";
                string validExts = core.SupportedExtensions;

                if (hasSysInfo)
                {
                    var sysInfoFn = Marshal.GetDelegateForFunctionPointer<RetroGetSystemInfoDelegate>(pSysInfo);
                    sysInfoFn(out var info);
                    if (info.LibraryName != IntPtr.Zero)
                        libName = Marshal.PtrToStringUTF8(info.LibraryName) ?? core.Name;
                    if (info.LibraryVersion != IntPtr.Zero)
                        libVersion = Marshal.PtrToStringUTF8(info.LibraryVersion) ?? "1.0.0";
                    if (info.ValidExtensions != IntPtr.Zero)
                        validExts = Marshal.PtrToStringUTF8(info.ValidExtensions) ?? core.SupportedExtensions;
                }

                string summaryRu = $"Ядро «{libName}» (v{libVersion}) успешно проверено. Архитектура: {(is64Bit ? "x64 (64-бит)" : "x86 (32-бит)")}, API Libretro: v{apiVersion}, холодная загрузка DLL: {coldLoadMs:F2} мс, Run-Ahead: {(hasSerialize ? "Поддерживается" : "Ограничено")}, расширения: {validExts}.";

                return new CoreBenchmarkResult(
                    core.Name,
                    core.DllName,
                    is64Bit,
                    apiVersion,
                    libName,
                    libVersion,
                    validExts,
                    coldLoadMs,
                    allSymbolsValid,
                    summaryRu
                );
            }
            finally
            {
                try { NativeLibrary.Free(hDll); } catch { }
            }
        });
    }

    public static async Task<(int Updated, int Failed)> UpdateAllInstalledCoresAsync(IProgress<string>? progress = null)
    {
        var installed = AllCoresCatalog.Where(c => c.IsInstalled).ToList();
        if (installed.Count == 0)
        {
            progress?.Report("Установленные ядра не обнаружены.");
            return (0, 0);
        }

        progress?.Report($"Начало пакетного обновления ядер Libretro ({installed.Count} шт.)...");
        int updatedCount = 0;
        int failedCount = 0;
        using var semaphore = new SemaphoreSlim(2, 2);

        var tasks = installed.Select(async core =>
        {
            await semaphore.WaitAsync();
            try
            {
                progress?.Report($"Обновление ядра: {core.Name} ({core.DllName})...");
                bool ok = await DownloadCoreAsync(core.DllName);
                if (ok)
                {
                    Interlocked.Increment(ref updatedCount);
                    progress?.Report($"Ядро {core.Name} успешно обновлено.");
                }
                else
                {
                    Interlocked.Increment(ref failedCount);
                    progress?.Report($"Ошибка обновления ядра {core.Name}.");
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failedCount);
                progress?.Report($"Исключение при обновлении {core.Name}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        progress?.Report($"Пакетное обновление завершено. Обновлено: {updatedCount}, ошибок: {failedCount}.");
        return (updatedCount, failedCount);
    }
}
