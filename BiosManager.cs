using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

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

    public string StatusText => IsFound ? "✓ Установлен" : "✗ Отсутствует";
    public string StatusBadgeBg => IsFound ? "#1A10B981" : "#1AEF4444";
    public string StatusBadgeBorder => IsFound ? "#10B981" : "#EF4444";
    public string StatusBadgeText => IsFound ? "#6EE7B7" : "#FCA5A5";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class BiosManager
{
    public static readonly string SystemDir = Path.Combine(CoreManager.AppDir, "system");

    static BiosManager()
    {
        try
        {
            if (!Directory.Exists(SystemDir)) Directory.CreateDirectory(SystemDir);
        }
        catch { }
    }

    public static readonly List<BiosInfoItem> Database = new()
    {
        // === SONY PLAYSTATION ===
        new() { FileName = "scph5500.bin", Platform = "Sony PlayStation 1 [Japan]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "8dd7d5296a650fac7319bce665a6a53c", ExpectedSize = 524288,
            Description = "Оригинальный японский BIOS для PS1 (NTSC-J). Необходим для максимальной совместимости японских ромов в SwanStation/Beetle." },
        new() { FileName = "scph5501.bin", Platform = "Sony PlayStation 1 [USA]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "490f666e1afb15b7362b406ed1cea246", ExpectedSize = 524288,
            Description = "Американский эталонный BIOS для PS1 (NTSC-U). Обеспечивает 100% совместимость американских версий игр." },
        new() { FileName = "scph5502.bin", Platform = "Sony PlayStation 1 [Europe]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "32736f17079d0b2b7024407c39bd3050", ExpectedSize = 524288,
            Description = "Европейский PAL BIOS для PS1 (PAL 50Hz). Необходим для европейских версий и переводов на базе PAL." },
        new() { FileName = "scph1001.bin", Platform = "Sony PlayStation 1 (Early)", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "924e392ed05558ffdb115408c163d80e", ExpectedSize = 524288,
            Description = "Классический ранний американский BIOS PS1 (ревизия SCPH-1001/AUD)." },
        new() { FileName = "psxonpsp660.bin", Platform = "Sony PlayStation 1 [PSP Fast]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "c53ca5908936d41233386766442655b3", ExpectedSize = 524288,
            Description = "Быстрый оптимизированный BIOS PS1 от Sony PSP с быстрым пропуском анимаций загрузки." },
        new() { FileName = "scph39001.bin", Platform = "Sony PlayStation 2 [USA]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "b863d09a25b128564e29b0a73b22cf9c", ExpectedSize = 4194304,
            Description = "Основной образ BIOS консоли PlayStation 2 (v3.90 USA) для ядра LRPS2 / PCSX2." },
        new() { FileName = "scph39001.nvm", Platform = "Sony PlayStation 2 (NVM)", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "", ExpectedSize = 1024,
            Description = "Энергонезависимая память NVM настроек консоли PS2 (часы, язык, видеорежим)." },

        // === SEGA (SATURN / DREAMCAST / CD) ===
        new() { FileName = "sega_101.bin", Platform = "Sega Saturn [Japan]", PlatformCategory = "Sega (Saturn / Dreamcast / CD)", ExpectedMd5 = "85ec9ca47d8f6807718151cbcca8b964", ExpectedSize = 524288,
            Description = "Японский BIOS для Sega Saturn v1.01. Критически важен для запуска эксклюзивных японских RPG (Beetle Saturn, YabaSanshiro)." },
        new() { FileName = "mpr-17933.bin", Platform = "Sega Saturn [USA/EU]", PlatformCategory = "Sega (Saturn / Dreamcast / CD)", ExpectedMd5 = "4afcf0fa501727916d402e6025e74211", ExpectedSize = 524288,
            Description = "Американский и Европейский BIOS для Sega Saturn v1.01a." },
        new() { FileName = "dc_boot.bin", Platform = "Sega Dreamcast [Boot]", PlatformCategory = "Sega (Saturn / Dreamcast / CD)", ExpectedMd5 = "e10c53c2f8b90bab96ead2d368858623", ExpectedSize = 2097152,
            Description = "Основной системный загрузочный ROM консоли Sega Dreamcast для ядра Flycast." },
        new() { FileName = "dc_flash.bin", Platform = "Sega Dreamcast [Flash NVRAM]", PlatformCategory = "Sega (Saturn / Dreamcast / CD)", ExpectedMd5 = "74e3f69c2bb92bc1e57a240572013388", ExpectedSize = 131072,
            Description = "Флэш-память настроек консоли Dreamcast (время, имя пользователя, дата)." },
        new() { FileName = "bios_CD_U.bin", Platform = "Sega Mega-CD [USA]", PlatformCategory = "Sega (Saturn / Dreamcast / CD)", ExpectedMd5 = "6b54edb2b719488349257e8fa1cfab30", ExpectedSize = 131072,
            Description = "Системный BIOS для приставки Sega CD USA (Genesis Plus GX, PicoDrive)." },
        new() { FileName = "bios_CD_E.bin", Platform = "Sega Mega-CD [Europe]", PlatformCategory = "Sega (Saturn / Dreamcast / CD)", ExpectedMd5 = "e66fa1dc5820d254611fd9fb60731703", ExpectedSize = 131072,
            Description = "Системный BIOS для приставки Sega Mega-CD Europe." },
        new() { FileName = "bios_CD_J.bin", Platform = "Sega Mega-CD [Japan]", PlatformCategory = "Sega (Saturn / Dreamcast / CD)", ExpectedMd5 = "278a9397d192149e84e820ac621a8eff", ExpectedSize = 131072,
            Description = "Системный BIOS для приставки Sega Mega-CD Japan." },

        // === NINTENDO (GBA / DS / FDS) ===
        new() { FileName = "gba_bios.bin", Platform = "Nintendo Game Boy Advance", PlatformCategory = "Nintendo (GBA / DS / FDS)", ExpectedMd5 = "a860e8c0b67573d53b523db86e3f70a6", ExpectedSize = 16384,
            Description = "Оригинальный микрокод BIOS GBA (mGBA, VBA-Next). Устраняет проблемы со звуком и таймингами интро." },
        new() { FileName = "bios7.bin", Platform = "Nintendo DS [ARM7 Core]", PlatformCategory = "Nintendo (GBA / DS / FDS)", ExpectedMd5 = "df692a80a5b1bc90728bc3df32d43e39", ExpectedSize = 4096,
            Description = "Системный BIOS сопроцессора ARM7 консоли Nintendo DS (MelonDS, DeSmuME)." },
        new() { FileName = "bios9.bin", Platform = "Nintendo DS [ARM9 Core]", PlatformCategory = "Nintendo (GBA / DS / FDS)", ExpectedMd5 = "a392174eb3e572fed6447e083b87f469", ExpectedSize = 4096,
            Description = "Системный BIOS главного процессора ARM9 консоли Nintendo DS." },
        new() { FileName = "firmware.bin", Platform = "Nintendo DS [Firmware]", PlatformCategory = "Nintendo (GBA / DS / FDS)", ExpectedMd5 = "", ExpectedSize = 262144,
            Description = "Оригинальная прошивка меню Nintendo DS с калибровкой тачскрина и часами реального времени." },
        new() { FileName = "disksys.rom", Platform = "Famicom Disk System (FDS)", PlatformCategory = "Nintendo (GBA / DS / FDS)", ExpectedMd5 = "ca30b50f88e40f64a32f6922fa42b43c", ExpectedSize = 8192,
            Description = "Загрузочный картридж Famicom Disk System (Nestopia, Mesen, FCEUmm) для запуска образов .fds." },

        // === NEC PC ENGINE / TURBOGRAFX ===
        new() { FileName = "syscard3.pce", Platform = "PC Engine Super CD-ROM2 v3.0", PlatformCategory = "NEC PC Engine", ExpectedMd5 = "38179f2dc67789a71f05786fec76495b", ExpectedSize = 262144,
            Description = "Системная карта Super CD-ROM2 System Card v3.00 для запуска 99% игр PC Engine CD (Beetle PCE)." },
        new() { FileName = "syscard2.pce", Platform = "PC Engine CD-ROM2 v2.1", PlatformCategory = "NEC PC Engine", ExpectedMd5 = "e10f135b1d5c7f893d61b3cd66d3a824", ExpectedSize = 262144,
            Description = "Системная карта CD-ROM2 v2.10 для ранних японских игр PC Engine." },
        new() { FileName = "syscard1.pce", Platform = "PC Engine CD-ROM2 v1.0", PlatformCategory = "NEC PC Engine", ExpectedMd5 = "300b9a1a47be4d209b552319ef7d0505", ExpectedSize = 262144,
            Description = "Ранняя системная карта CD-ROM2 v1.00 для ретро-совместимости." },
        new() { FileName = "gexpress.pce", Platform = "PC Engine Game Express Card", PlatformCategory = "NEC PC Engine", ExpectedMd5 = "77b5a1b55977a4128f6d7dd59b5eeec4", ExpectedSize = 262144,
            Description = "Специальная карта Hacker International для нелицензионных CD-игр PC Engine." },

        // === ARCADE / NEO GEO ===
        new() { FileName = "neogeo.zip", Platform = "SNK Neo Geo MVS / AES", PlatformCategory = "Arcade / Neo Geo", ExpectedMd5 = "", ExpectedSize = 0,
            Description = "Архив системных BIOS микросхем Neo Geo (asia-s3, sm1, sfix, 000-lo) для ядра FBNeo / MAME." },
        new() { FileName = "awbios.zip", Platform = "Sammy Atomiswave", PlatformCategory = "Arcade / Neo Geo", ExpectedMd5 = "", ExpectedSize = 0,
            Description = "BIOS аркадной платформы Sammy Atomiswave для ядра Flycast." },
        new() { FileName = "naomi.zip", Platform = "Sega NAOMI Arcade", PlatformCategory = "Arcade / Neo Geo", ExpectedMd5 = "", ExpectedSize = 0,
            Description = "BIOS аркадной материнской платы Sega NAOMI 1 / 2 для ядра Flycast." },

        // === 3DO / AMIGA / ATARI ===
        new() { FileName = "panafz10.bin", Platform = "Panasonic 3DO FZ-10", PlatformCategory = "3DO / Amiga / Atari", ExpectedMd5 = "582283730794ceb5a59703d58b955861", ExpectedSize = 1048576,
            Description = "BIOS консоли Panasonic 3DO REAL (FZ-10) для ядра Opera (4DO)." },
        new() { FileName = "panafz1.bin", Platform = "Panasonic 3DO FZ-1", PlatformCategory = "3DO / Amiga / Atari", ExpectedMd5 = "8639f43c9cca888882041e3971610773", ExpectedSize = 1048576,
            Description = "BIOS оригинальной японской/американской консоли Panasonic 3DO FZ-1." },
        new() { FileName = "goldstar.bin", Platform = "GoldStar 3DO GDO-101", PlatformCategory = "3DO / Amiga / Atari", ExpectedMd5 = "a05581977b227c88b7754b2d1257404e", ExpectedSize = 1048576,
            Description = "BIOS корейской консоли GoldStar 3DO Interactive Multiplayer." },
        new() { FileName = "kick34005.A500", Platform = "Commodore Amiga 500 (Kickstart 1.3)", PlatformCategory = "3DO / Amiga / Atari", ExpectedMd5 = "c4f0f55f1e9408d22227b309402d7d25", ExpectedSize = 262144,
            Description = "Легендарный ROM Kickstart 1.3 для Amiga 500 (ядро PUAE) для запуска классических игр OCS." },
        new() { FileName = "kick40068.A1200", Platform = "Commodore Amiga 1200 (Kickstart 3.1)", PlatformCategory = "3DO / Amiga / Atari", ExpectedMd5 = "646773759326fbac3b2311fd8c8793ee", ExpectedSize = 524288,
            Description = "Системный Kickstart 3.1 для Amiga 1200 AGA и Amiga CD32." },
        new() { FileName = "kick40068.A4000", Platform = "Commodore Amiga 4000 (Kickstart 3.1)", PlatformCategory = "3DO / Amiga / Atari", ExpectedMd5 = "9bdedde6a4f33555b4a270c8ca53297d", ExpectedSize = 524288,
            Description = "Системный ROM Kickstart 3.1 для высокопроизводительной Amiga 4000." },
        new() { FileName = "lynxboot.img", Platform = "Atari Lynx", PlatformCategory = "3DO / Amiga / Atari", ExpectedMd5 = "fcd403db69f31090c0245f11e82f630f", ExpectedSize = 512,
            Description = "Микрокод загрузчика портативной консоли Atari Lynx (Handy, Beetle Lynx)." },
        new() { FileName = "atarivox.eeprom", Platform = "Atari 2600 / 7800 (AtariVox)", PlatformCategory = "3DO / Amiga / Atari", ExpectedMd5 = "", ExpectedSize = 2048,
            Description = "EEPROM энергонезависимой памяти синтезатора речи AtariVox для Atari 7800." }
    };

    public static List<BiosInfoItem> CheckAllBios()
    {
        foreach (var item in Database)
        {
            string fullPath = Path.Combine(SystemDir, item.FileName);
            if (File.Exists(fullPath))
            {
                var fi = new FileInfo(fullPath);
                item.IsFound = true;
                item.ActualSize = fi.Length;
                if (!string.IsNullOrEmpty(item.ExpectedMd5))
                {
                    try
                    {
                        using var md5 = MD5.Create();
                        using var stream = File.OpenRead(fullPath);
                        byte[] hash = md5.ComputeHash(stream);
                        item.ActualMd5 = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                    catch
                    {
                        item.ActualMd5 = "";
                    }
                }
            }
            else
            {
                item.IsFound = false;
                item.ActualSize = 0;
                item.ActualMd5 = "";
            }
        }
        return Database;
    }

    public static List<string> GetCategories()
    {
        return new()
        {
            "Все платформы",
            "Sony PlayStation",
            "Sega (Saturn / Dreamcast / CD)",
            "Nintendo (GBA / DS / FDS)",
            "NEC PC Engine",
            "Arcade / Neo Geo",
            "3DO / Amiga / Atari",
            "Установленные",
            "Отсутствующие"
        };
    }

    public static List<BiosInfoItem> InspectSystemDirectory() => CheckAllBios();

    public class BiosImportResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
    }

    public static BiosImportResult ImportBios(string sourceFilePath)
    {
        try
        {
            if (!File.Exists(sourceFilePath))
                return new BiosImportResult { Success = false, Message = "Файл не найден!" };

            if (!Directory.Exists(SystemDir)) Directory.CreateDirectory(SystemDir);

            string target = Path.Combine(SystemDir, Path.GetFileName(sourceFilePath));
            File.Copy(sourceFilePath, target, overwrite: true);
            return new BiosImportResult { Success = true, Message = $"Файл {Path.GetFileName(sourceFilePath)} успешно скопирован в system/!" };
        }
        catch (Exception ex)
        {
            return new BiosImportResult { Success = false, Message = $"Ошибка импорта: {ex.Message}" };
        }
    }
}
