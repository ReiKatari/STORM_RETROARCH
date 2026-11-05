using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace StormRetroArch;

public class BiosInfoItem : INotifyPropertyChanged
{
    private bool _isFound;
    private long _actualSize;
    private string _actualMd5 = "";

    public string FileName { get; set; } = "";
    public string Platform { get; set; } = "";
    public string PlatformCategory { get; set; } = "Sony PlayStation";
    public string Description { get; set; } = "";
    public string ExpectedMd5 { get; set; } = "";
    public long ExpectedSize { get; set; }
    public bool IsOptional { get; set; } = true;
    public List<string> Cores { get; set; } = new();

    public bool IsInstalled => IsFound;
    public bool IsFound
    {
        get => _isFound;
        set
        {
            if (_isFound != value)
            {
                _isFound = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusBadgeBg));
                OnPropertyChanged(nameof(StatusBadgeBorder));
                OnPropertyChanged(nameof(StatusBadgeText));
            }
        }
    }

    public long ActualSize
    {
        get => _actualSize;
        set { if (_actualSize != value) { _actualSize = value; OnPropertyChanged(); } }
    }

    public string ActualMd5
    {
        get => _actualMd5;
        set { if (_actualMd5 != value) { _actualMd5 = value; OnPropertyChanged(); } }
    }

    public string StatusText => IsFound ? "Установлен" : (IsOptional ? "Опционально" : "Требуется");
    public string StatusBadgeBg => IsFound ? "#1A10B981" : (IsOptional ? "#1A64748B" : "#1AEF4444");
    public string StatusBadgeBorder => IsFound ? "#10B981" : (IsOptional ? "#64748B" : "#EF4444");
    public string StatusBadgeText => IsFound ? "#6EE7B7" : (IsOptional ? "#94A3B8" : "#FCA5A5");

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class BiosManager
{
    public static readonly string SystemDir = Path.Combine(CoreManager.AppDir, "system");
    private static readonly List<BiosInfoItem> _database = new();

    static BiosManager()
    {
        try
        {
            if (!Directory.Exists(SystemDir)) Directory.CreateDirectory(SystemDir);
        }
        catch { }

        InitializeDatabase();
    }

    public static List<BiosInfoItem> Database => _database;

    private static void InitializeDatabase()
    {
        _database.Clear();

        // 1. Curated priority BIOS entries with verified MD5 and size
        var curatedMap = new Dictionary<string, BiosInfoItem>(StringComparer.OrdinalIgnoreCase)
        {
            // === SONY PLAYSTATION ===
            ["scph5500.bin"] = new() { FileName = "scph5500.bin", Platform = "Sony PlayStation 1 [Japan]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "8dd7d5296a650fac7319bce665a6a53c", ExpectedSize = 524288, IsOptional = false,
                Description = "Оригинальный японский BIOS для PS1 (NTSC-J). Необходим для максимальной совместимости японских ромов в SwanStation/Beetle." },
            ["scph5501.bin"] = new() { FileName = "scph5501.bin", Platform = "Sony PlayStation 1 [USA]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "490f666e1afb15b7362b406ed1cea246", ExpectedSize = 524288, IsOptional = false,
                Description = "Американский эталонный BIOS для PS1 (NTSC-U). Обеспечивает 100% совместимость американских версий игр." },
            ["scph5502.bin"] = new() { FileName = "scph5502.bin", Platform = "Sony PlayStation 1 [Europe]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "32736f17079d0b2b7024407c39bd3050", ExpectedSize = 524288, IsOptional = false,
                Description = "Европейский PAL BIOS для PS1 (PAL 50Hz). Необходим для европейских версий и переводов на базе PAL." },
            ["scph1001.bin"] = new() { FileName = "scph1001.bin", Platform = "Sony PlayStation 1 (Early)", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "924e392ed05558ffdb115408c163d80e", ExpectedSize = 524288, IsOptional = true,
                Description = "Классический ранний американский BIOS PS1 (ревизия SCPH-1001/AUD)." },
            ["psxonpsp660.bin"] = new() { FileName = "psxonpsp660.bin", Platform = "Sony PlayStation 1 [PSP Fast]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "c53ca5908936d41233386766442655b3", ExpectedSize = 524288, IsOptional = true,
                Description = "Быстрый оптимизированный BIOS PS1 от Sony PSP с быстрым пропуском анимаций загрузки." },
            ["scph39001.bin"] = new() { FileName = "scph39001.bin", Platform = "Sony PlayStation 2 [USA]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "b863d09a25b128564e29b0a73b22cf9c", ExpectedSize = 4194304, IsOptional = false,
                Description = "Основной образ BIOS консоли PlayStation 2 (v3.90 USA) для ядра LRPS2 / PCSX2." },
            ["scph39001.nvm"] = new() { FileName = "scph39001.nvm", Platform = "Sony PlayStation 2 (NVM)", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "", ExpectedSize = 1024, IsOptional = true,
                Description = "Энергонезависимая память NVM настроек консоли PS2 (часы, язык, видеорежим)." },

            // === SEGA ===
            ["sega_101.bin"] = new() { FileName = "sega_101.bin", Platform = "Sega Saturn [Japan]", PlatformCategory = "Sega", ExpectedMd5 = "85ec9ca47d8f6807718151cbcca8b964", ExpectedSize = 524288, IsOptional = false,
                Description = "Японский BIOS для Sega Saturn v1.01. Критически важен для японских RPG (Beetle Saturn, YabaSanshiro)." },
            ["mpr-17933.bin"] = new() { FileName = "mpr-17933.bin", Platform = "Sega Saturn [USA/EU]", PlatformCategory = "Sega", ExpectedMd5 = "4afcf0fa501727916d402e6025e74211", ExpectedSize = 524288, IsOptional = false,
                Description = "Американский и Европейский BIOS для Sega Saturn v1.01a." },
            ["dc_boot.bin"] = new() { FileName = "dc_boot.bin", Platform = "Sega Dreamcast [Boot]", PlatformCategory = "Sega", ExpectedMd5 = "e10c53c2f8b90bab96ead2d368858623", ExpectedSize = 2097152, IsOptional = false,
                Description = "Основной системный загрузочный ROM консоли Sega Dreamcast для ядра Flycast." },
            ["dc_flash.bin"] = new() { FileName = "dc_flash.bin", Platform = "Sega Dreamcast [Flash NVRAM]", PlatformCategory = "Sega", ExpectedMd5 = "74e3f69c2bb92bc1e57a240572013388", ExpectedSize = 131072, IsOptional = true,
                Description = "Флэш-память настроек консоли Dreamcast (время, имя пользователя, дата)." },
            ["bios_CD_U.bin"] = new() { FileName = "bios_CD_U.bin", Platform = "Sega Mega-CD [USA]", PlatformCategory = "Sega", ExpectedMd5 = "6b54edb2b719488349257e8fa1cfab30", ExpectedSize = 131072, IsOptional = false,
                Description = "Системный BIOS для приставки Sega CD USA (Genesis Plus GX, PicoDrive)." },
            ["bios_CD_E.bin"] = new() { FileName = "bios_CD_E.bin", Platform = "Sega Mega-CD [Europe]", PlatformCategory = "Sega", ExpectedMd5 = "e66fa1dc5820d254611fd9fb60731703", ExpectedSize = 131072, IsOptional = false,
                Description = "Системный BIOS для приставки Sega Mega-CD Europe." },
            ["bios_CD_J.bin"] = new() { FileName = "bios_CD_J.bin", Platform = "Sega Mega-CD [Japan]", PlatformCategory = "Sega", ExpectedMd5 = "278a9397d192149e84e820ac621a8eff", ExpectedSize = 131072, IsOptional = false,
                Description = "Системный BIOS для приставки Sega Mega-CD Japan." },

            // === NINTENDO ===
            ["gba_bios.bin"] = new() { FileName = "gba_bios.bin", Platform = "Nintendo Game Boy Advance", PlatformCategory = "Nintendo", ExpectedMd5 = "a860e8c0b67573d53b523db86e3f70a6", ExpectedSize = 16384, IsOptional = true,
                Description = "Оригинальный микрокод BIOS GBA (mGBA, VBA-Next). Устраняет проблемы со звуком и интро." },
            ["bios7.bin"] = new() { FileName = "bios7.bin", Platform = "Nintendo DS [ARM7 Core]", PlatformCategory = "Nintendo", ExpectedMd5 = "df692a80a5b1bc90728bc3df32d43e39", ExpectedSize = 4096, IsOptional = false,
                Description = "Системный BIOS сопроцессора ARM7 консоли Nintendo DS (MelonDS, DeSmuME)." },
            ["bios9.bin"] = new() { FileName = "bios9.bin", Platform = "Nintendo DS [ARM9 Core]", PlatformCategory = "Nintendo", ExpectedMd5 = "a392174eb3e572fed6447e083b87f469", ExpectedSize = 4096, IsOptional = false,
                Description = "Системный BIOS главного процессора ARM9 консоли Nintendo DS." },
            ["firmware.bin"] = new() { FileName = "firmware.bin", Platform = "Nintendo DS [Firmware]", PlatformCategory = "Nintendo", ExpectedMd5 = "", ExpectedSize = 262144, IsOptional = true,
                Description = "Оригинальная прошивка меню Nintendo DS с калибровкой тачскрина и часами реального времени." },
            ["disksys.rom"] = new() { FileName = "disksys.rom", Platform = "Famicom Disk System (FDS)", PlatformCategory = "Nintendo", ExpectedMd5 = "ca30b50f88e40f64a32f6922fa42b43c", ExpectedSize = 8192, IsOptional = false,
                Description = "Загрузочный BIOS дисковой системы Famicom Disk System для эмулятора FCEUmm / Mesen." },

            // === NEC / PC ENGINE ===
            ["syscard3.pce"] = new() { FileName = "syscard3.pce", Platform = "PC Engine Super CD-ROM² v3.0", PlatformCategory = "NEC / PC Engine", ExpectedMd5 = "ff1a674273fe3540ccef576376407e1d", ExpectedSize = 262144, IsOptional = false,
                Description = "Системная карта Super CD-ROM² System Card v3.0 для запуска всех CD-игр в Beetle PCE Fast." },
            ["pcfx.rom"] = new() { FileName = "pcfx.rom", Platform = "NEC PC-FX System BIOS", PlatformCategory = "NEC / PC Engine", ExpectedMd5 = "08e36edbea28a017f79f8d4f7ff9b6d7", ExpectedSize = 1048576, IsOptional = false,
                Description = "Системный образ BIOS японской 32-битной приставки NEC PC-FX (Beetle PC-FX)." },

            // === ARCADE / NEO GEO ===
            ["neogeo.zip"] = new() { FileName = "neogeo.zip", Platform = "SNK Neo Geo AES / MVS", PlatformCategory = "Arcade / Neo Geo", ExpectedMd5 = "", ExpectedSize = 1572864, IsOptional = false,
                Description = "Комплект системных BIOS аркадной системы Neo Geo MVS и домашней консоли AES для ячеек FBNeo и MAME." },

            // === PC / DOS / AMIGA / ATARI ===
            ["kick34005.A500"] = new() { FileName = "kick34005.A500", Platform = "Commodore Amiga 500 (Kickstart 1.3)", PlatformCategory = "PC / DOS / Amiga / Atari", ExpectedMd5 = "c4f0f55f1e3bb332f505dd47d06e22e9", ExpectedSize = 262144, IsOptional = false,
                Description = "Эталонная прошивка Kickstart v1.3 r34.5 для запуска 95% классических игр Amiga в PUAE." },
            ["kick40068.A1200"] = new() { FileName = "kick40068.A1200", Platform = "Commodore Amiga 1200 (Kickstart 3.1)", PlatformCategory = "PC / DOS / Amiga / Atari", ExpectedMd5 = "1483a0911e2cf7d5c52bb3d6e556cf8a", ExpectedSize = 524288, IsOptional = false,
                Description = "Прошивка Kickstart v3.1 r40.68 для продвинутых игр архитектуры AGA." },
            ["tos.img"] = new() { FileName = "tos.img", Platform = "Atari ST TOS", PlatformCategory = "PC / DOS / Amiga / Atari", ExpectedMd5 = "", ExpectedSize = 196608, IsOptional = false,
                Description = "Операционная система TOS для компьютера Atari ST (ядро Hatari)." },
            ["lynxboot.img"] = new() { FileName = "lynxboot.img", Platform = "Atari Lynx Boot ROM", PlatformCategory = "PC / DOS / Amiga / Atari", ExpectedMd5 = "fcd403db69f071295846569ade317d85", ExpectedSize = 512, IsOptional = false,
                Description = "Загрузочный микрокод для портативной консоли Atari Lynx (Handy / Beetle Lynx)." },
            ["3do_bios.rom"] = new() { FileName = "3do_bios.rom", Platform = "Panasonic 3DO Interactive", PlatformCategory = "PC / DOS / Amiga / Atari", ExpectedMd5 = "18e695be24be90f84501484196c80c26", ExpectedSize = 1048576, IsOptional = false,
                Description = "Основной системный ROM консоли Panasonic 3DO FZ-1 / FZ-10 (ядро Opera)." },
            ["macII.rom"] = new() { FileName = "macII.rom", Platform = "Apple Macintosh II", PlatformCategory = "PC / DOS / Amiga / Atari", ExpectedMd5 = "", ExpectedSize = 262144, IsOptional = false,
                Description = "Системный образ Macintosh II для эмулятора классической Mac OS (ядро Basilisk II)." }
        };

        // 2. Scan info directory for all .info files and extract all official firmware
        string infoDir = Path.Combine(CoreManager.AppDir, "info");
        if (Directory.Exists(infoDir))
        {
            var infoFiles = Directory.GetFiles(infoDir, "*.info");
            foreach (var infoPath in infoFiles)
            {
                try
                {
                    string content = File.ReadAllText(infoPath);
                    string systemname = ExtractField(content, "systemname") ?? "Retro System";
                    string manufacturer = ExtractField(content, "manufacturer") ?? "Universal";
                    string coreDisplayName = ExtractField(content, "display_name") ?? Path.GetFileNameWithoutExtension(infoPath).Replace("_libretro", "");

                    // Find all firmware paths
                    var fpaths = Regex.Matches(content, @"firmware\d+_path\s*=\s*""([^""]*)""");
                    var fdescs = Regex.Matches(content, @"firmware\d+_desc\s*=\s*""([^""]*)""");
                    var fopts = Regex.Matches(content, @"firmware\d+_opt\s*=\s*""([^""]*)""");

                    for (int i = 0; i < fpaths.Count; i++)
                    {
                        string rawPath = fpaths[i].Groups[1].Value.Replace('\\', '/');
                        string fname = Path.GetFileName(rawPath);
                        if (string.IsNullOrEmpty(fname)) continue;

                        string fdesc = i < fdescs.Count ? fdescs[i].Groups[1].Value : fname;
                        bool fopt = i < fopts.Count && fopts[i].Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);

                        if (!curatedMap.TryGetValue(fname, out var item))
                        {
                            string cat = DeterminePlatformCategory(manufacturer, systemname, coreDisplayName);
                            item = new BiosInfoItem
                            {
                                FileName = fname,
                                Platform = systemname,
                                PlatformCategory = cat,
                                Description = $"{fdesc} ({coreDisplayName})",
                                IsOptional = fopt,
                                Cores = new List<string> { coreDisplayName }
                            };
                            curatedMap[fname] = item;
                        }
                        else
                        {
                            if (!item.Cores.Contains(coreDisplayName))
                            {
                                item.Cores.Add(coreDisplayName);
                                if (!item.Description.Contains(coreDisplayName))
                                    item.Description += $", {coreDisplayName}";
                            }
                        }
                    }
                }
                catch { }
            }
        }

        foreach (var item in curatedMap.Values.OrderBy(x => x.PlatformCategory).ThenBy(x => x.FileName))
        {
            _database.Add(item);
        }
    }

    private static string? ExtractField(string content, string key)
    {
        var match = Regex.Match(content, $@"^{key}\s*=\s*""([^""]*)""", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string DeterminePlatformCategory(string mfg, string sys, string core)
    {
        string m = mfg.ToLowerInvariant();
        string s = sys.ToLowerInvariant();
        string c = core.ToLowerInvariant();

        if (m.Contains("sony") || s.Contains("playstation") || s.Contains("ps1") || s.Contains("ps2") || s.Contains("psp"))
            return "Sony PlayStation";
        if (m.Contains("sega") || s.Contains("genesis") || s.Contains("saturn") || s.Contains("dreamcast") || s.Contains("mega-cd") || s.Contains("master system"))
            return "Sega";
        if (m.Contains("nintendo") || s.Contains("game boy") || s.Contains("gba") || s.Contains("ds") || s.Contains("nes") || s.Contains("snes") || s.Contains("famicom"))
            return "Nintendo";
        if (m.Contains("nec") || s.Contains("engine") || s.Contains("pce") || s.Contains("pc-fx"))
            return "NEC / PC Engine";
        if (m.Contains("arcade") || m.Contains("snk") || s.Contains("neo geo") || c.Contains("fbneo") || c.Contains("mame"))
            return "Arcade / Neo Geo";
        if (m.Contains("atari") || m.Contains("commodore") || s.Contains("amiga") || s.Contains("dos") || m.Contains("apple") || m.Contains("microsoft"))
            return "PC / DOS / Amiga / Atari";

        return "Другие системы";
    }

    public static void CheckAllBios()
    {
        if (!Directory.Exists(SystemDir))
        {
            try { Directory.CreateDirectory(SystemDir); } catch { }
            return;
        }

        foreach (var item in _database)
        {
            string fullPath = Path.Combine(SystemDir, item.FileName);
            if (File.Exists(fullPath))
            {
                item.IsFound = true;
                try
                {
                    var fi = new FileInfo(fullPath);
                    item.ActualSize = fi.Length;

                    using var md5 = MD5.Create();
                    using var stream = File.OpenRead(fullPath);
                    byte[] hash = md5.ComputeHash(stream);
                    item.ActualMd5 = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
                catch
                {
                    item.ActualMd5 = "ошибка чтения";
                }
            }
            else
            {
                item.IsFound = false;
                item.ActualSize = 0;
                item.ActualMd5 = "";
            }
        }
    }

    public static List<BiosInfoItem> InspectSystemDirectory() { CheckAllBios(); return Database; }

    public static (bool Success, string Message) ImportBios(string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath)) return (false, "Файл не найден");

        try
        {
            if (!Directory.Exists(SystemDir)) Directory.CreateDirectory(SystemDir);
            string dest = Path.Combine(SystemDir, Path.GetFileName(sourceFilePath));
            File.Copy(sourceFilePath, dest, overwrite: true);
            CheckAllBios();
            return (true, $"BIOS-файл {Path.GetFileName(sourceFilePath)} успешно установлен в system/");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка импорта BIOS: {ex.Message}");
        }
    }
}
