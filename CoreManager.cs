using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace StormRetroArch;

public record CoreInfo(string Name, string Platform, string DllName, string Description)
{
    public bool IsInstalled => CoreManager.IsCoreInstalled(DllName);
}

public static class CoreManager
{
    public const string BuildbotBaseUrl = "https://buildbot.libretro.com/nightly/windows/x86_64/latest/";
    public static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory;
    public static readonly string CoresDir = Path.Combine(AppDir, "cores");
    public static readonly string DownloadsDir = Path.Combine(AppDir, "downloads");

    static CoreManager()
    {
        try
        {
            if (!Directory.Exists(CoresDir)) Directory.CreateDirectory(CoresDir);
            if (!Directory.Exists(DownloadsDir)) Directory.CreateDirectory(DownloadsDir);
        }
        catch { }
    }

    public static readonly List<CoreInfo> GoldenCores = new()
    {
        new("Snes9x", "Super Nintendo (SNES)", "snes9x_libretro.dll", "Высокоточный и быстрый эмулятор SNES / Super Famicom"),
        new("Genesis Plus GX", "Sega Genesis / Mega Drive", "genesis_plus_gx_libretro.dll", "Идеальная эмуляция Mega Drive, Master System, Game Gear"),
        new("SwanStation", "Sony PlayStation 1", "swanstation_libretro.dll", "Ультра-производительный PS1 эмулятор с поддержкой PGXP и апскейла"),
        new("FCEUmm", "Nintendo Entertainment System", "fceumm_libretro.dll", "Современный эмулятор NES / Famicom / Dendy с поддержкой мапперов"),
        new("mGBA", "Game Boy Advance", "mgba_libretro.dll", "Быстрый и точный эмулятор GBA, GBC и классического Game Boy"),
        new("melonDS DS", "Nintendo DS / DSi", "melondsds_libretro.dll", "Превосходная эмуляция Nintendo DS и DSi с сенсорным экраном"),
        new("Mupen64Plus-Next", "Nintendo 64", "mupen64plus_next_libretro.dll", "Мощный эмулятор N64 с плагинами GLideN64 и ParaLLEl RDP"),
        new("PPSSPP", "Sony PlayStation Portable", "ppsspp_libretro.dll", "Быстрая эмуляция PSP с HD текстурами и шейдерами"),
        new("Flycast", "Sega Dreamcast / NAOMI", "flycast_libretro.dll", "Современный мультиплатформенный эмулятор Dreamcast и NAOMI"),
        new("Beetle Saturn", "Sega Saturn", "mednafen_saturn_libretro.dll", "Точнейшая потактовая эмуляция архитектуры Sega Saturn"),
        new("FBNeo", "Arcade / Neo Geo", "fbneo_libretro.dll", "Флагманский эмулятор аркадных автоматов, Neo Geo и CPS-1/2/3"),
        new("DOSBox-Pure", "MS-DOS PC", "dosbox_pure_libretro.dll", "Запуск классических игр MS-DOS прямо из ZIP-архивов с геймпадом"),
        new("Beetle PCE Fast", "PC Engine / TurboGrafx-16", "mednafen_pce_fast_libretro.dll", "Быстрый эмулятор PC Engine HuCard и CD-ROM"),
        new("Stella", "Atari 2600", "stella_libretro.dll", "Эталонный эмулятор классической приставки Atari 2600 VCS"),
        new("Handy", "Atari Lynx", "handy_libretro.dll", "Эмулятор портативной консоли Atari Lynx"),
        new("Beetle NeoPop", "Neo Geo Pocket", "mednafen_ngp_libretro.dll", "Эмулятор Neo Geo Pocket и Pocket Color"),
        new("Beetle WonderSwan", "Bandai WonderSwan", "mednafen_wswan_libretro.dll", "Эмулятор портативной консоли WonderSwan / Color"),
        new("Citra", "Nintendo 3DS", "citra_libretro.dll", "Эмулятор Nintendo 3DS с апскейлом геометрии"),
        new("Dolphin", "Nintendo GameCube / Wii", "dolphin_libretro.dll", "Флагманский эмулятор GameCube и Wii с высокой кадровой частотой"),
        new("Gambatte", "Game Boy / Color", "gambatte_libretro.dll", "Потактово точный эмулятор оригинального Game Boy"),
        new("PCSX-ReARMed", "Sony PlayStation 1 (Fast)", "pcsx_rearmed_libretro.dll", "Оптимизированный быстрый эмулятор PS1"),
        new("Picodrive", "Sega 32X / Sega CD", "picodrive_libretro.dll", "Специализированный эмулятор Genesis, 32X и Sega CD"),
        new("Nestopia", "Nintendo Entertainment System", "nestopia_libretro.dll", "Циклически точный эмулятор Famicom / NES"),
        new("ProSystem", "Atari 7800", "prosystem_libretro.dll", "Эмулятор консоли Atari 7800 ProSystem"),
        new("MAME 2003-Plus", "Arcade Classics", "mame2003_plus_libretro.dll", "Классический аркадный движок с улучшенным звуком и управлением")
    };

    public static bool IsCoreInstalled(string dllName)
    {
        string fullPath = Path.Combine(CoresDir, dllName);
        return File.Exists(fullPath);
    }

    public static (string Platform, string CoreDll, string CoreName) IdentifyRom(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();

        return ext switch
        {
            ".nes" or ".fds" => ("Nintendo Entertainment System", "fceumm_libretro.dll", "FCEUmm"),
            ".sfc" or ".smc" => ("Super Nintendo (SNES)", "snes9x_libretro.dll", "Snes9x"),
            ".md" or ".gen" or ".smd" => ("Sega Genesis / Mega Drive", "genesis_plus_gx_libretro.dll", "Genesis Plus GX"),
            ".bin" or ".cue" or ".chd" or ".pbp" => ("Sony PlayStation 1", "swanstation_libretro.dll", "SwanStation"),
            ".iso" => ("Sony PlayStation / DVD", "swanstation_libretro.dll", "SwanStation"),
            ".n64" or ".z64" or ".v64" => ("Nintendo 64", "mupen64plus_next_libretro.dll", "Mupen64Plus-Next"),
            ".gba" => ("Game Boy Advance", "mgba_libretro.dll", "mGBA"),
            ".gb" or ".gbc" => ("Game Boy / Color", "gambatte_libretro.dll", "Gambatte"),
            ".nds" or ".dsi" => ("Nintendo DS / DSi", "melondsds_libretro.dll", "melonDS DS"),
            ".3ds" or ".cci" => ("Nintendo 3DS", "citra_libretro.dll", "Citra"),
            ".cso" => ("Sony PlayStation Portable", "ppsspp_libretro.dll", "PPSSPP"),
            ".cdi" or ".gdi" => ("Sega Dreamcast", "flycast_libretro.dll", "Flycast"),
            ".sat" => ("Sega Saturn", "mednafen_saturn_libretro.dll", "Beetle Saturn"),
            ".pce" => ("PC Engine / TurboGrafx-16", "mednafen_pce_fast_libretro.dll", "Beetle PCE Fast"),
            ".ngp" or ".ngc" => ("Neo Geo Pocket / Color", "mednafen_ngp_libretro.dll", "Beetle NeoPop"),
            ".ws" or ".wsc" => ("WonderSwan / Color", "mednafen_wswan_libretro.dll", "Beetle WonderSwan"),
            ".a26" => ("Atari 2600", "stella_libretro.dll", "Stella"),
            ".a78" => ("Atari 7800", "prosystem_libretro.dll", "ProSystem"),
            ".lnx" => ("Atari Lynx", "handy_libretro.dll", "Handy"),
            ".jag" => ("Atari Jaguar", "virtualjaguar_libretro.dll", "Virtual Jaguar"),
            ".dosz" => ("MS-DOS", "dosbox_pure_libretro.dll", "DOSBox-Pure"),
            ".rvz" => ("Nintendo GameCube / Wii", "dolphin_libretro.dll", "Dolphin"),
            ".zip" or ".7z" or ".rar" => ("Arcade / Multi-System", "fbneo_libretro.dll", "FBNeo"),
            _ => ("Неизвестная платформа", "fbneo_libretro.dll", "Libretro Core")
        };
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
        string tempZipPath = Path.Combine(DownloadsDir, zipName);

        try
        {
            progress?.Report((5, $"Подключение к Buildbot: {zipName}..."));
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            client.DefaultRequestHeaders.Add("User-Agent", "STORM-RETROARCH");

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                progress?.Report((0, $"Ошибка сервера: {response.StatusCode}"));
                return false;
            }

            long totalBytes = response.Content.Headers.ContentLength ?? -1;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            byte[] buffer = new byte[8192];
            long totalRead = 0;
            int read;

            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read);
                totalRead += read;
                if (totalBytes > 0)
                {
                    double pct = (double)totalRead * 100.0 / totalBytes;
                    progress?.Report((pct, $"Загрузка: {pct:F1}% ({(totalRead / 1024):N0} KB / {(totalBytes / 1024):N0} KB)"));
                }
            }

            fileStream.Close();
            progress?.Report((90, "Распаковка ядра в cores/..."));

            // Extract DLL from ZIP
            using (var zipArchive = ZipFile.OpenRead(tempZipPath))
            {
                foreach (var entry in zipArchive.Entries)
                {
                    if (entry.Name.Equals(dllName, StringComparison.OrdinalIgnoreCase) ||
                        entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.ExtractToFile(targetDllPath, overwrite: true);
                        break;
                    }
                }
            }

            try { File.Delete(tempZipPath); } catch { }

            progress?.Report((100, "Ядро успешно установлено!"));
            return File.Exists(targetDllPath);
        }
        catch (Exception ex)
        {
            progress?.Report((0, $"Ошибка: {ex.Message}"));
            Debug.WriteLine($"[Core Download Error]: {ex.Message}");
            return false;
        }
    }

    public static string FindRetroArchExecutable()
    {
        // 1. Check user configured path
        if (!string.IsNullOrWhiteSpace(SettingsManager.Current.RetroArchPath) &&
            File.Exists(SettingsManager.Current.RetroArchPath))
        {
            return SettingsManager.Current.RetroArchPath;
        }

        // 2. Candidates in AppDir
        string[] localCandidates =
        {
            Path.Combine(AppDir, "retroarch_engine.exe"),
            Path.Combine(AppDir, "retroarch.exe"),
            Path.Combine(AppDir, "STORM RETROARCH.exe")
        };

        foreach (var c in localCandidates)
        {
            if (File.Exists(c)) return c;
        }

        // 3. Parent directory search (up to 3 levels)
        try
        {
            var cur = new DirectoryInfo(AppDir);
            for (int i = 0; i < 3 && cur.Parent != null; i++)
            {
                cur = cur.Parent;
                string p1 = Path.Combine(cur.FullName, "retroarch_engine.exe");
                if (File.Exists(p1)) return p1;
                string p2 = Path.Combine(cur.FullName, "retroarch.exe");
                if (File.Exists(p2)) return p2;
            }
        }
        catch { }

        // 4. Standard system install locations
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string[] systemCandidates =
        {
            Path.Combine(appData, "RetroArch", "retroarch.exe"),
            @"C:\RetroArch-Win64\retroarch.exe",
            @"C:\RetroArch\retroarch.exe",
            @"C:\Program Files\RetroArch\retroarch.exe",
            @"D:\RetroArch\retroarch.exe"
        };

        foreach (var c in systemCandidates)
        {
            if (File.Exists(c)) return c;
        }

        return Path.Combine(AppDir, "retroarch.exe");
    }

    public static async Task LaunchGameAsync(RomItem item)
    {
        string corePath = Path.Combine(CoresDir, item.CoreDll);

        if (!File.Exists(corePath))
        {
            item.IsDownloading = true;
            item.StatusText = LocalizationManager.GetString("StatusDownloadingCore");
            var prog = new Progress<(double percent, string status)>(p =>
            {
                item.ProgressValue = p.percent;
                item.StatusText = p.status;
            });

            bool downloaded = await DownloadCoreAsync(item.CoreDll, prog);
            item.IsDownloading = false;
            item.IsCoreInstalled = downloaded;

            if (!downloaded)
            {
                MainWindow.Instance?.ShowNotification(
                    "STORM RETROARCH",
                    $"Не удалось автоматически загрузить ядро {item.CoreDll}.\nПроверьте подключение к интернету или поместите ядро вручную в папку cores/.");
                return;
            }
        }

        string exe = FindRetroArchExecutable();
        if (!File.Exists(exe))
        {
            MainWindow.Instance?.ShowNotification(
                "STORM RETROARCH",
                $"Исполняемый файл RetroArch не найден:\n{exe}\nПожалуйста, укажите путь к retroarch.exe в Настройках или поместите его в папку приложения.");
            return;
        }

        string workDir = Path.GetDirectoryName(exe) ?? AppDir;
        string args = $"-L \"{corePath}\" \"{item.FilePath}\"";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                WorkingDirectory = workDir,
                UseShellExecute = false
            };

            // Inject low-latency settings from RetroArchConfigManager or SettingsManager
            psi.Environment["LIBRETRO_VIDEO_DRIVER"] = RetroArchConfigManager.GetString("video_driver", SettingsManager.Current.VideoDriver);
            if (RetroArchConfigManager.GetBool("preemptive_frames_enable", SettingsManager.Current.PreemptiveFrames))
                psi.Environment["LIBRETRO_RUNAHEAD"] = "1";

            Process.Start(psi);
            item.StatusText = LocalizationManager.GetString("StatusRunning");
            item.LastPlayed = DateTime.Now;
            MainWindow.Instance?.RecordGameHistory(item);
        }
        catch (Exception ex)
        {
            MainWindow.Instance?.ShowNotification(
                "STORM RETROARCH",
                $"Ошибка запуска эмулятора:\n{ex.Message}\nКоманда: {exe} {args}");
        }
    }

    public static void LaunchOzone()
    {
        string exe = FindRetroArchExecutable();
        if (!File.Exists(exe))
        {
            MainWindow.Instance?.ShowNotification(
                "STORM RETROARCH",
                $"Исполняемый файл RetroArch не найден:\n{exe}\nПожалуйста, укажите путь к retroarch.exe в Настройках.");
            return;
        }

        string workDir = Path.GetDirectoryName(exe) ?? AppDir;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--menu -f",
                WorkingDirectory = workDir,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            MainWindow.Instance?.ShowNotification(
                "STORM RETROARCH",
                $"Не удалось запустить RetroArch Ozone:\n{ex.Message}");
        }
    }

    public static void LaunchDesktopQt()
    {
        string exe = FindRetroArchExecutable();
        if (!File.Exists(exe))
        {
            MainWindow.Instance?.ShowNotification(
                "STORM RETROARCH",
                $"Исполняемый файл RetroArch не найден:\n{exe}\nПожалуйста, укажите путь к retroarch.exe в Настройках.");
            return;
        }

        string workDir = Path.GetDirectoryName(exe) ?? AppDir;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--menu --ui-companion-start-on-boot",
                WorkingDirectory = workDir,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            MainWindow.Instance?.ShowNotification(
                "STORM RETROARCH",
                $"Не удалось запустить десктопное меню Qt:\n{ex.Message}");
        }
    }
}
