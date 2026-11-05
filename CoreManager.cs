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

public record CoreInfo(string Name, string Platform, string DllName, string Description, string Manufacturer = "Universal", string SupportedExtensions = "")
{
    public bool IsInstalled => CoreManager.IsCoreInstalled(DllName);
}

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
        string? startDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (string.IsNullOrEmpty(startDir)) startDir = AppDomain.CurrentDomain.BaseDirectory;

        if (Directory.Exists(Path.Combine(startDir, "cores")) &&
            Directory.Exists(Path.Combine(startDir, "info")))
            return startDir;

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

                    string desc = $"Официальное ядро Libretro: {displayName} ({systemName}).";
                    if (!string.IsNullOrEmpty(extensions)) desc += $" Форматы: {extensions}";

                    var core = new CoreInfo(displayName, systemName, expectedDll, desc, manufacturer, extensions);
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
        new("Snes9x", "Super Nintendo (SNES)", "snes9x_libretro.dll", "Высокоточный и быстрый эмулятор SNES / Super Famicom", "Nintendo", "smc|sfc|fig|swc"),
        new("Genesis Plus GX", "Sega Genesis / Mega Drive", "genesis_plus_gx_libretro.dll", "Идеальная эмуляция Mega Drive, Master System, Game Gear", "Sega", "md|gen|smd|sms|gg|sg"),
        new("SwanStation", "Sony PlayStation 1", "swanstation_libretro.dll", "Ультра-производительный PS1 эмулятор с поддержкой PGXP и апскейла", "Sony", "cue|chd|pbp|iso|bin|m3u"),
        new("FCEUmm", "Nintendo Entertainment System", "fceumm_libretro.dll", "Современный эмулятор NES / Famicom / Dendy с поддержкой мапперов", "Nintendo", "nes|fds|unf|unif"),
        new("mGBA", "Game Boy Advance", "mgba_libretro.dll", "Быстрый и точный эмулятор GBA, GBC и классического Game Boy", "Nintendo", "gba|gb|gbc"),
        new("melonDS DS", "Nintendo DS / DSi", "melondsds_libretro.dll", "Превосходная эмуляция Nintendo DS и DSi с сенсорным экраном", "Nintendo", "nds|dsi"),
        new("Mupen64Plus-Next", "Nintendo 64", "mupen64plus_next_libretro.dll", "Мощный эмулятор N64 с плагинами GLideN64 и ParaLLEl RDP", "Nintendo", "n64|v64|z64"),
        new("PPSSPP", "Sony PlayStation Portable", "ppsspp_libretro.dll", "Быстрая эмуляция PSP с HD текстурами и шейдерами", "Sony", "iso|cso|pbp"),
        new("Flycast", "Sega Dreamcast / NAOMI", "flycast_libretro.dll", "Современный мультиплатформенный эмулятор Dreamcast и NAOMI", "Sega", "cdi|gdi|chd|cue"),
        new("Beetle Saturn", "Sega Saturn", "mednafen_saturn_libretro.dll", "Точнейшая потактовая эмуляция архитектуры Sega Saturn", "Sega", "cue|ccd|chd|toc|m3u"),
        new("FBNeo", "Arcade / Neo Geo", "fbneo_libretro.dll", "Флагманский эмулятор аркадных автоматов, Neo Geo и CPS-1/2/3", "Arcade", "zip|7z"),
        new("DOSBox-Pure", "MS-DOS PC", "dosbox_pure_libretro.dll", "Запуск классических игр MS-DOS прямо из ZIP-архивов с геймпадом", "PC / DOS", "zip|dosz|exe|com|bat|iso"),
        new("Beetle PCE Fast", "PC Engine / TurboGrafx-16", "mednafen_pce_fast_libretro.dll", "Быстрый эмулятор PC Engine HuCard и CD-ROM", "NEC", "pce|sgx|cue|ccd|chd"),
        new("Stella", "Atari 2600", "stella_libretro.dll", "Эталонный эмулятор классической приставки Atari 2600 VCS", "Atari", "a26|bin"),
        new("Handy", "Atari Lynx", "handy_libretro.dll", "Эмулятор портативной консоли Atari Lynx", "Atari", "lnx"),
        new("Beetle NeoPop", "Neo Geo Pocket", "mednafen_ngp_libretro.dll", "Эмулятор Neo Geo Pocket и Pocket Color", "SNK", "ngp|ngc"),
        new("Beetle WonderSwan", "Bandai WonderSwan", "mednafen_wswan_libretro.dll", "Эмулятор портативной консоли WonderSwan / Color", "Bandai", "ws|wsc"),
        new("Citra", "Nintendo 3DS", "citra_libretro.dll", "Эмулятор Nintendo 3DS с апскейлом геометрии", "Nintendo", "3ds|3dsx|elf|cci|cxi"),
        new("Dolphin", "Nintendo GameCube / Wii", "dolphin_libretro.dll", "Флагманский эмулятор GameCube и Wii с высокой кадровой частотой", "Nintendo", "gcm|iso|wbfs|rvz|cso|wad"),
        new("Gambatte", "Game Boy / Color", "gambatte_libretro.dll", "Потактово точный эмулятор оригинального Game Boy", "Nintendo", "gb|gbc|dmg"),
        new("Picodrive", "Sega 32X / Sega CD", "picodrive_libretro.dll", "Оптимизированный эмулятор Sega 32X, Mega-CD и Genesis", "Sega", "bin|gen|smd|md|32x|cue|iso"),
        new("VBA-M", "Game Boy Advance", "vbam_libretro.dll", "Продвинутый эмулятор Game Boy Advance с отладкой", "Nintendo", "gba|gb|gbc"),
        new("PCSX-ReARMed", "Sony PlayStation 1", "pcsx_rearmed_libretro.dll", "Быстрый эмулятор PS1 для систем любого уровня", "Sony", "bin|cue|img|mdf|pbp|cbn|chd"),
        new("Beetle PSX HW", "Sony PlayStation 1 (Vulkan)", "mednafen_psx_hw_libretro.dll", "Аппаратно-ускоренный PS1 с Vulkan/OpenGL рендером", "Sony", "cue|ccd|chd|toc|m3u|pbp"),
        new("MAME 2003-Plus", "Arcade Classics", "mame2003_plus_libretro.dll", "Классический аркадный движок с сотнями исправлений", "Arcade", "zip")
    };

    public static List<CoreInfo> AllCoresCatalog => _allCoresCatalog;

    public static bool IsCoreInstalled(string dllName)
    {
        return File.Exists(Path.Combine(CoresDir, dllName));
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
            try
            {
                using var archive = ZipFile.OpenRead(filePath);
                
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

        return IdentifyRomByExtension(ext);
    }

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
}
