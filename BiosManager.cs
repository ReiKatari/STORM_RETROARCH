using System.IO.Compression;
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
    public string RelativePath { get; set; } = "";
    public string SubfolderDisplay => RelativePath.Contains('/') ? "system/" + (Path.GetDirectoryName(RelativePath)?.Replace('\\', '/') ?? "") : "system/";
    public string ExpectedFullPath => Path.Combine(BiosManager.SystemDir, (string.IsNullOrEmpty(RelativePath) ? FileName : RelativePath).Replace('/', Path.DirectorySeparatorChar));
    public bool IsDirectoryRequirement { get; set; }
    public string Platform { get; set; } = "";
    public string PlatformCategory { get; set; } = "Sony PlayStation";
    public string SystemFamily => BiosManager.GetSystemFamilyForConsole(Platform);
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
    public static string GetSystemFamilyForConsole(string console)
    {
        if (string.IsNullOrWhiteSpace(console)) return "Arcade & Home Computers";
        string c = console.ToLowerInvariant();
        if (c.Contains("playstation") || c.Contains("ps1") || c.Contains("ps2") || c.Contains("ps3") || c.Contains("psp") || c.Contains("vita") || c.Contains("psx"))
            return "PlayStation";
        if (c.Contains("sega") || c.Contains("genesis") || c.Contains("saturn") || c.Contains("dreamcast") || c.Contains("naomi") || c.Contains("mega drive") || c.Contains("master system") || c.Contains("game gear") || c.Contains("atomiswave"))
            return "Sega";
        if (c.Contains("nintendo") || c.Contains("nes") || c.Contains("famicom") || c.Contains("snes") || c.Contains("game boy") || c.Contains("gba") || c.Contains("ds") || c.Contains("3ds") || c.Contains("gamecube") || c.Contains("wii"))
            return "Nintendo";
        return "Arcade & Home Computers";
    }

    public static readonly string SystemDir = Directory.Exists(CoreManager.InstallDir)
        ? Path.Combine(CoreManager.InstallDir, "system")
        : Path.Combine(CoreManager.AppDir, "system");
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
        var curatedMap = new Dictionary<string, BiosInfoItem>(StringComparer.OrdinalIgnoreCase);

        void AddCurated(BiosInfoItem item)
        {
            if (string.IsNullOrEmpty(item.RelativePath))
                item.RelativePath = item.FileName;
            if (string.IsNullOrEmpty(item.FileName))
                item.FileName = Path.GetFileName(item.RelativePath);

            curatedMap[item.RelativePath] = item;
            if (!curatedMap.ContainsKey(item.FileName))
                curatedMap[item.FileName] = item;
        }

        // === SONY PLAYSTATION 1 / 2 / 3 / VITA / PSP ===
        AddCurated(new() { FileName = "scph5500.bin", RelativePath = "scph5500.bin", Platform = "Sony PlayStation 1 [Japan]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "8dd7d5296a650fac7319bce665a6a53c", ExpectedSize = 524288, IsOptional = false,
            Description = "Оригинальный японский BIOS для PS1 (NTSC-J). Необходим для максимальной совместимости японских ромов в SwanStation/Beetle." });
        AddCurated(new() { FileName = "scph5501.bin", RelativePath = "scph5501.bin", Platform = "Sony PlayStation 1 [USA]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "490f666e1afb15b7362b406ed1cea246", ExpectedSize = 524288, IsOptional = false,
            Description = "Американский эталонный BIOS для PS1 (NTSC-U). Обеспечивает 100% совместимость американских версий игр." });
        AddCurated(new() { FileName = "scph5502.bin", RelativePath = "scph5502.bin", Platform = "Sony PlayStation 1 [Europe]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "32736f17079d0b2b7024407c39bd3050", ExpectedSize = 524288, IsOptional = false,
            Description = "Европейский PAL BIOS для PS1 (PAL 50Hz). Необходим для европейских версий и переводов на базе PAL." });
        AddCurated(new() { FileName = "scph1001.bin", RelativePath = "scph1001.bin", Platform = "Sony PlayStation 1 (Early)", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "924e392ed05558ffdb115408c163d80e", ExpectedSize = 524288, IsOptional = true,
            Description = "Классический ранний американский BIOS PS1 (ревизия SCPH-1001/AUD)." });
        AddCurated(new() { FileName = "psxonpsp660.bin", RelativePath = "psxonpsp660.bin", Platform = "Sony PlayStation 1 [PSP Fast]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "c53ca5908936d41233386766442655b3", ExpectedSize = 524288, IsOptional = true,
            Description = "Быстрый оптимизированный BIOS PS1 от Sony PSP с быстрым пропуском анимаций загрузки." });
        
        // PS2 Curated
        AddCurated(new() { FileName = "scph39001.bin", RelativePath = "pcsx2/bios/scph39001.bin", Platform = "Sony PlayStation 2 [USA]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "b863d09a25b128564e29b0a73b22cf9c", ExpectedSize = 4194304, IsOptional = false,
            Description = "Основной образ BIOS консоли PlayStation 2 (v3.90 USA) для ядра LRPS2 / PCSX2." });
        AddCurated(new() { FileName = "scph39001.nvm", RelativePath = "pcsx2/bios/scph39001.nvm", Platform = "Sony PlayStation 2 (NVM)", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "", ExpectedSize = 1024, IsOptional = true,
            Description = "Энергонезависимая память NVM настроек консоли PS2 (часы, язык, видеорежим)." });
        AddCurated(new() { FileName = "bios", RelativePath = "pcsx2/bios", IsDirectoryRequirement = true, Platform = "Sony PlayStation 2 [BIOS Dir]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "Каталог BIOS PS2", ExpectedSize = 0, IsOptional = false,
            Description = "Папка BIOS PlayStation 2 (поместите любой образ scph39001.bin / scph70012.bin для LRPS2, PCEE2, ARMSX2)." });
        AddCurated(new() { FileName = "resources", RelativePath = "pcsx2/resources", IsDirectoryRequirement = true, Platform = "Sony PlayStation 2 [Resources]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "Каталог ресурсов PS2", ExpectedSize = 0, IsOptional = false,
            Description = "Папка системных ресурсов PS2 (GameIndex.yaml, шейдеры и шрифты для ядер LRPS2 и PCEE2)." });
        AddCurated(new() { FileName = "PSP2UPDAT_PREINST.PUP", RelativePath = "vita3k/PSP2UPDAT_PREINST.PUP", Platform = "Sony PlayStation Vita [Preinst]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "", ExpectedSize = 0, IsOptional = false,
            Description = "Системный предзагрузочный пакет прошивки PS Vita (требуется ядром Vita3K вместе с PSP2UPDAT.PUP)." });
        AddCurated(new() { FileName = "GameIndex.yaml", RelativePath = "pcsx2/resources/GameIndex.yaml", Platform = "Sony PlayStation 2 [Resources]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "", ExpectedSize = 0, IsOptional = true,
            Description = "База метаданных, патчей и исправлений игр PS2 для ядра LRPS2 / PCSX2." });

        // PS3 Curated
        AddCurated(new() { FileName = "PS3UPDAT.PUP", RelativePath = "rpcs3/PS3UPDAT.PUP", Platform = "Sony PlayStation 3 [Firmware]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "", ExpectedSize = 200000000, IsOptional = false,
            Description = "Официальная системная прошивка PlayStation 3 (OFW/CFW) для ядра RPCS3." });

        // PS Vita Curated
        AddCurated(new() { FileName = "PSP2UPDAT.PUP", RelativePath = "vita3k/PSP2UPDAT.PUP", Platform = "Sony PlayStation Vita [Firmware]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "", ExpectedSize = 130000000, IsOptional = false,
            Description = "Официальная системная прошивка PS Vita для эмулятора Vita3K." });
        AddCurated(new() { FileName = "PSP2UPDAT_FONT.PUP", RelativePath = "vita3k/PSP2UPDAT_FONT.PUP", Platform = "Sony PlayStation Vita [Font Package]", PlatformCategory = "Sony PlayStation", ExpectedMd5 = "", ExpectedSize = 40000000, IsOptional = true,
            Description = "Пакет системных шрифтов PS Vita для корректного отображения диалогов в Vita3K." });

        // === MICROSOFT XBOX ===
        AddCurated(new() { FileName = "mcpx_1.0.bin", RelativePath = "xemu/mcpx_1.0.bin", Platform = "Microsoft Xbox [MCPX Boot ROM]", PlatformCategory = "PC / DOS / Amiga / Atari", ExpectedMd5 = "580666014e7a27a41285093557e4e84b", ExpectedSize = 512, IsOptional = false,
            Description = "Загрузочный микрокод MCPX v1.0 оригинальной консоли Xbox для ядра xemu." });
        AddCurated(new() { FileName = "Complex_4627v1.03.bin", RelativePath = "xemu/Complex_4627v1.03.bin", Platform = "Microsoft Xbox [BIOS Flash]", PlatformCategory = "PC / DOS / Amiga / Atari", ExpectedMd5 = "", ExpectedSize = 1048576, IsOptional = false,
            Description = "Образ BIOS дампа чипа Xbox для низкоуровневой эмуляции в ядре xemu." });

        // === SEGA SATURN / DREAMCAST / NAOMI / ATOMISWAVE ===
        AddCurated(new() { FileName = "sega_101.bin", RelativePath = "sega_101.bin", Platform = "Sega Saturn [Japan]", PlatformCategory = "Sega", ExpectedMd5 = "85ec9ca47d8f6807718151cbcca8b964", ExpectedSize = 524288, IsOptional = false,
            Description = "Японский BIOS для Sega Saturn v1.01. Критически важен для японских RPG (Beetle Saturn, YabaSanshiro, Kronos)." });
        AddCurated(new() { FileName = "mpr-17933.bin", RelativePath = "mpr-17933.bin", Platform = "Sega Saturn [USA/EU]", PlatformCategory = "Sega", ExpectedMd5 = "4afcf0fa501727916d402e6025e74211", ExpectedSize = 524288, IsOptional = false,
            Description = "Американский и Европейский BIOS для Sega Saturn v1.01a." });
        AddCurated(new() { FileName = "dc_boot.bin", RelativePath = "dc/dc_boot.bin", Platform = "Sega Dreamcast [Boot]", PlatformCategory = "Sega", ExpectedMd5 = "e10c53c2f8b90bab96ead2d368858623", ExpectedSize = 2097152, IsOptional = false,
            Description = "Основной системный загрузочный ROM консоли Sega Dreamcast для ядра Flycast." });
        AddCurated(new() { FileName = "dc_flash.bin", RelativePath = "dc/dc_flash.bin", Platform = "Sega Dreamcast [Flash NVRAM]", PlatformCategory = "Sega", ExpectedMd5 = "74e3f69c2bb92bc1e57a240572013388", ExpectedSize = 131072, IsOptional = true,
            Description = "Энергонезависимая память NVRAM настроек Sega Dreamcast." });
        AddCurated(new() { FileName = "naomi.zip", RelativePath = "dc/naomi.zip", Platform = "Sega NAOMI [Arcade BIOS]", PlatformCategory = "Sega", ExpectedMd5 = "", ExpectedSize = 2097152, IsOptional = false,
            Description = "Системный BIOS аркадной платформы Sega NAOMI для ядра Flycast." });
        AddCurated(new() { FileName = "awbios.zip", RelativePath = "dc/awbios.zip", Platform = "Sammy Atomiswave [Arcade BIOS]", PlatformCategory = "Sega", ExpectedMd5 = "", ExpectedSize = 131072, IsOptional = false,
            Description = "Системный BIOS аркадной платформы Sammy Atomiswave для ядра Flycast." });

        // === NINTENDO NEXT-GEN & PORTABLES ===
        AddCurated(new() { FileName = "aes_keys.txt", RelativePath = "citra/sysdata/aes_keys.txt", Platform = "Nintendo 3DS [AES Keys]", PlatformCategory = "Nintendo", ExpectedMd5 = "", ExpectedSize = 0, IsOptional = true,
            Description = "Таблица криптографических ключей AES для расшифровки зашифрованных образов 3DS в Citra/Azahar." });
        AddCurated(new() { FileName = "keys.txt", RelativePath = "cemu/keys.txt", Platform = "Nintendo Wii U [Title Keys]", PlatformCategory = "Nintendo", ExpectedMd5 = "", ExpectedSize = 0, IsOptional = true,
            Description = "Ключи шифрования дисков и обновлений Nintendo Wii U для ядра Cemu." });
        AddCurated(new() { FileName = "IPL.bin", RelativePath = "dolphin-emu/Sys/GC/IPL.bin", Platform = "Nintendo GameCube [IPL]", PlatformCategory = "Nintendo", ExpectedMd5 = "", ExpectedSize = 2097152, IsOptional = true,
            Description = "Загрузочный образ Initial Program Loader (IPL) консоли Nintendo GameCube для ядра Dolphin." });
        AddCurated(new() { FileName = "gba_bios.bin", RelativePath = "gba_bios.bin", Platform = "Game Boy Advance", PlatformCategory = "Nintendo", ExpectedMd5 = "aabe92a89c99253a86350f23b6f9362f", ExpectedSize = 16384, IsOptional = true,
            Description = "Официальный загрузочный BIOS GBA. Устраняет графические артефакты в интро и улучшает тайминги звука." });
        AddCurated(new() { FileName = "gb_bios.bin", RelativePath = "gb_bios.bin", Platform = "Game Boy (DMG)", PlatformCategory = "Nintendo", ExpectedMd5 = "32fbbd84168d3482956eb3c5e361018a", ExpectedSize = 256, IsOptional = true,
            Description = "Загрузочный бутром оригинального монохромного Game Boy для Gambatte / SameBoy." });
        AddCurated(new() { FileName = "gbc_bios.bin", RelativePath = "gbc_bios.bin", Platform = "Game Boy Color", PlatformCategory = "Nintendo", ExpectedMd5 = "dbfce9148562811f0863216d60363a2d", ExpectedSize = 2048, IsOptional = true,
            Description = "Загрузочный бутром Game Boy Color для инициализации палитры и ускорения." });
        AddCurated(new() { FileName = "bios7.bin", RelativePath = "bios7.bin", Platform = "Nintendo DS (ARM7 BIOS)", PlatformCategory = "Nintendo", ExpectedMd5 = "df692a80a5b1bc90728bc3df32d41436", ExpectedSize = 16384, IsOptional = false,
            Description = "Микрокод сопроцессора ARM7 для эмулятора melonDS / DeSmuME." });
        AddCurated(new() { FileName = "bios9.bin", RelativePath = "bios9.bin", Platform = "Nintendo DS (ARM9 BIOS)", PlatformCategory = "Nintendo", ExpectedMd5 = "a392174eb3e572fed6447e01cf54f155", ExpectedSize = 4096, IsOptional = false,
            Description = "Микрокод основного процессора ARM9 консоли Nintendo DS." });
        AddCurated(new() { FileName = "firmware.bin", RelativePath = "firmware.bin", Platform = "Nintendo DS Firmware", PlatformCategory = "Nintendo", ExpectedMd5 = "", ExpectedSize = 262144, IsOptional = false,
            Description = "Полная системная прошивка Nintendo DS (калибровка тачскрина, часы, никнейм)." });

        // === ARCADE / NEO GEO / CPS ===
        AddCurated(new() { FileName = "neogeo.zip", RelativePath = "neogeo.zip", Platform = "SNK Neo Geo AES / MVS", PlatformCategory = "Arcade / Neo Geo", ExpectedMd5 = "", ExpectedSize = 1500000, IsOptional = false,
            Description = "Комплект системных BIOS аркадной системы Neo Geo MVS и AES для FBNeo и MAME." });
        AddCurated(new() { FileName = "pgm.zip", RelativePath = "pgm.zip", Platform = "IGS PolyGame Master [PGM BIOS]", PlatformCategory = "Arcade / Neo Geo", ExpectedMd5 = "", ExpectedSize = 131072, IsOptional = false,
            Description = "Системный BIOS аркадной платы IGS PGM для FBNeo/MAME." });
        AddCurated(new() { FileName = "qsound_hle.zip", RelativePath = "qsound_hle.zip", Platform = "Capcom CPS-2 QSound", PlatformCategory = "Arcade / Neo Geo", ExpectedMd5 = "", ExpectedSize = 2048, IsOptional = true,
            Description = "Звуковой микрокод процессора Capcom QSound для точной эмуляции аудио CPS-2." });

        // === PC / DOS / AMIGA / ATARI ===
        AddCurated(new() { FileName = "kick34005.A500", RelativePath = "kick34005.A500", Platform = "Commodore Amiga 500 (Kickstart 1.3)", PlatformCategory = "PC / DOS / Amiga / Atari", ExpectedMd5 = "c4f0f55f1e3bb332f505dd47d06e22e9", ExpectedSize = 262144, IsOptional = false,
            Description = "Эталонная прошивка Kickstart v1.3 r34.5 для запуска 95% классических игр Amiga в PUAE." });
        AddCurated(new() { FileName = "kick40068.A1200", RelativePath = "kick40068.A1200", Platform = "Commodore Amiga 1200 (Kickstart 3.1)", PlatformCategory = "PC / DOS / Amiga / Atari", ExpectedMd5 = "1483a0911e2cf7d5c52bb3d6e556cf8a", ExpectedSize = 524288, IsOptional = false,
            Description = "Прошивка Kickstart v3.1 r40.68 для продвинутых игр архитектуры AGA." });
        AddCurated(new() { FileName = "tos.img", RelativePath = "tos.img", Platform = "Atari ST TOS", PlatformCategory = "PC / DOS / Amiga / Atari", ExpectedMd5 = "", ExpectedSize = 196608, IsOptional = false,
            Description = "Операционная система TOS для компьютера Atari ST (ядро Hatari)." });
        AddCurated(new() { FileName = "lynxboot.img", RelativePath = "lynxboot.img", Platform = "Atari Lynx Boot ROM", PlatformCategory = "PC / DOS / Amiga / Atari", ExpectedMd5 = "fcd403db69f071295846569ade317d85", ExpectedSize = 512, IsOptional = false,
            Description = "Загрузочный микрокод для портативной консоли Atari Lynx (Handy / Beetle Lynx)." });
        AddCurated(new() { FileName = "3do_bios.rom", RelativePath = "3do_bios.rom", Platform = "Panasonic 3DO Interactive", PlatformCategory = "PC / DOS / Amiga / Atari", ExpectedMd5 = "18e695be24be90f84501484196c80c26", ExpectedSize = 1048576, IsOptional = false,
            Description = "Основной системный ROM консоли Panasonic 3DO FZ-1 / FZ-10 (ядро Opera)." });
        AddCurated(new() { FileName = "macII.rom", RelativePath = "macII.rom", Platform = "Apple Macintosh II", PlatformCategory = "PC / DOS / Amiga / Atari", ExpectedMd5 = "", ExpectedSize = 262144, IsOptional = false,
            Description = "Системный образ Macintosh II для эмулятора классической Mac OS (ядро Basilisk II)." });

        // 2. Scan info directory for all .info files and extract all official firmware with full subfolder paths preserved
        string infoDir = CoreManager.InfoDir;
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
                        if (string.IsNullOrEmpty(fname))
                        {
                            string trimmed = rawPath.TrimEnd('/');
                            int slash = trimmed.LastIndexOf('/');
                            fname = slash >= 0 ? trimmed.Substring(slash + 1) : trimmed;
                        }
                        if (string.IsNullOrEmpty(fname)) continue;

                        string fdesc = i < fdescs.Count ? fdescs[i].Groups[1].Value : fname;
                        bool fopt = i < fopts.Count && fopts[i].Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);

                        string lookupKey = string.IsNullOrEmpty(rawPath) ? fname : rawPath;

                        if (!curatedMap.TryGetValue(lookupKey, out var item) && !curatedMap.TryGetValue(fname, out item))
                        {
                            string cat = DeterminePlatformCategory(manufacturer, systemname, coreDisplayName);
                            item = new BiosInfoItem
                            {
                                FileName = fname,
                                RelativePath = rawPath,
                                Platform = systemname,
                                PlatformCategory = cat,
                                Description = $"{fdesc} ({coreDisplayName})",
                                IsOptional = fopt,
                                Cores = new List<string> { coreDisplayName },
                                IsDirectoryRequirement = rawPath.EndsWith('/') || rawPath.EndsWith(".sfc") || rawPath.EndsWith(".sfc/") || string.IsNullOrEmpty(Path.GetExtension(rawPath.TrimEnd('/')))
                            };
                            curatedMap[lookupKey] = item;
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(item.RelativePath) && !string.IsNullOrEmpty(rawPath))
                            {
                                item.RelativePath = rawPath;
                            }
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

        var uniqueItems = curatedMap.Values.Distinct().OrderBy(x => x.PlatformCategory).ThenBy(x => x.RelativePath).ToList();
        foreach (var item in uniqueItems)
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
            if (Directory.Exists(CoreManager.InstallDir))
            {
                try { Directory.CreateDirectory(SystemDir); } catch { }
            }
            return;
        }

        foreach (var item in _database)
        {
            string fullPath = item.ExpectedFullPath;

            if (item.IsDirectoryRequirement)
            {
                if (Directory.Exists(fullPath) && Directory.EnumerateFileSystemEntries(fullPath).Any())
                {
                    item.IsFound = true;
                    item.ActualSize = 0;
                    item.ActualMd5 = "Каталог заполнен";
                    continue;
                }
            }

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
                // Check if placed in root system/ as misplaced fallback
                string rootPath = Path.Combine(SystemDir, item.FileName);
                if (File.Exists(rootPath))
                {
                    item.IsFound = true;
                    try
                    {
                        var fi = new FileInfo(rootPath);
                        item.ActualSize = fi.Length;
                        using var md5 = MD5.Create();
                        using var stream = File.OpenRead(rootPath);
                        byte[] hash = md5.ComputeHash(stream);
                        item.ActualMd5 = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                    catch { item.ActualMd5 = "ошибка чтения"; }
                }
                else
                {
                    item.IsFound = false;
                    item.ActualSize = 0;
                    item.ActualMd5 = "";
                }
            }
        }
    }

    public static List<BiosInfoItem> InspectSystemDirectory() { CheckAllBios(); return Database; }

    public static void EnsureStandardBiosSubfolders()
    {
        try
        {
            if (!Directory.Exists(SystemDir))
            {
                if (Directory.Exists(CoreManager.InstallDir))
                {
                    Directory.CreateDirectory(SystemDir);
                }
                else return;
            }

            var subdirs = new[]
            {
                "pcsx2/bios",
                "pcsx2/resources",
                "rpcs3",
                "vita3k",
                "xemu",
                "dc",
                "dolphin-emu/Sys/GC",
                "citra/sysdata",
                "cemu",
                "mame/bios",
                "PPSSPP",
                "Mupen64plus",
                "bk",
                "Machines/Shared Roms"
            };

            foreach (var sub in subdirs)
            {
                string p = Path.Combine(SystemDir, sub.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(p)) Directory.CreateDirectory(p);
            }
        }
        catch { }
    }

    public static int AutoHealMisplacedBiosFiles()
    {
        if (!Directory.Exists(SystemDir)) return 0;
        int healed = 0;

        try
        {
            foreach (var item in _database)
            {
                if (!item.RelativePath.Contains('/')) continue;

                string rootFile = Path.Combine(SystemDir, item.FileName);
                string targetFile = item.ExpectedFullPath;

                if (File.Exists(rootFile) && !File.Exists(targetFile))
                {
                    string? targetDir = Path.GetDirectoryName(targetFile);
                    if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    File.Copy(rootFile, targetFile, overwrite: false);
                    healed++;
                }
            }

            if (healed > 0)
            {
                CheckAllBios();
            }
        }
        catch { }

        return healed;
    }

    public static async Task<(int ImportedCount, string Summary)> ImportBiosPackAsync(string folderOrZipPath)
    {
        return await Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(folderOrZipPath))
                return (0, "Путь к архиву или папке не указан");

            if (!File.Exists(folderOrZipPath) && !Directory.Exists(folderOrZipPath))
                return (0, "Указанный файл или папка не существует");

            EnsureStandardBiosSubfolders();
            int imported = 0;

            try
            {
                var byFileName = new Dictionary<string, BiosInfoItem>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in _database)
                {
                    if (!string.IsNullOrEmpty(item.FileName) && !byFileName.ContainsKey(item.FileName))
                        byFileName[item.FileName] = item;
                }

                if (File.Exists(folderOrZipPath) && folderOrZipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var archive = System.IO.Compression.ZipFile.OpenRead(folderOrZipPath);
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        string entryName = entry.Name;
                        long length = entry.Length;

                        string destRelPath = "";

                        if (entryName.EndsWith(".PUP", StringComparison.OrdinalIgnoreCase))
                        {
                            if (entryName.Contains("PS3", StringComparison.OrdinalIgnoreCase) || length > 150 * 1024 * 1024)
                                destRelPath = "rpcs3/PS3UPDAT.PUP";
                            else if (entryName.Contains("FONT", StringComparison.OrdinalIgnoreCase))
                                destRelPath = "vita3k/PSP2UPDAT_FONT.PUP";
                            else
                                destRelPath = "vita3k/PSP2UPDAT.PUP";
                        }
                        else if (length == 4194304 && (entryName.Contains("scph", StringComparison.OrdinalIgnoreCase) || entryName.Contains("ps2", StringComparison.OrdinalIgnoreCase) || entryName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) || entryName.EndsWith(".rom", StringComparison.OrdinalIgnoreCase)))
                        {
                            destRelPath = $"pcsx2/bios/{entryName}";
                        }
                        else if (byFileName.TryGetValue(entryName, out var bi))
                        {
                            destRelPath = string.IsNullOrEmpty(bi.RelativePath) ? bi.FileName : bi.RelativePath;
                        }
                        else if (entry.FullName.Contains('/') || entry.FullName.Contains('\\'))
                        {
                            destRelPath = entry.FullName.Replace('\\', '/').TrimStart('/');
                            if (destRelPath.StartsWith("system/", StringComparison.OrdinalIgnoreCase))
                                destRelPath = destRelPath.Substring(7);
                        }
                        else
                        {
                            destRelPath = entryName;
                        }

                        string targetFull = Path.Combine(SystemDir, destRelPath.Replace('/', Path.DirectorySeparatorChar));
                        string? targetDir = Path.GetDirectoryName(targetFull);
                        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                            Directory.CreateDirectory(targetDir);

                        entry.ExtractToFile(targetFull, overwrite: true);
                        imported++;
                    }
                }
                else if (Directory.Exists(folderOrZipPath))
                {
                    var files = Directory.GetFiles(folderOrZipPath, "*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        string fn = Path.GetFileName(file);
                        var fi = new FileInfo(file);
                        long length = fi.Length;
                        string destRelPath = "";

                        if (fn.EndsWith(".PUP", StringComparison.OrdinalIgnoreCase))
                        {
                            if (fn.Contains("PS3", StringComparison.OrdinalIgnoreCase) || length > 150 * 1024 * 1024)
                                destRelPath = "rpcs3/PS3UPDAT.PUP";
                            else if (fn.Contains("FONT", StringComparison.OrdinalIgnoreCase))
                                destRelPath = "vita3k/PSP2UPDAT_FONT.PUP";
                            else
                                destRelPath = "vita3k/PSP2UPDAT.PUP";
                        }
                        else if (length == 4194304 && (fn.Contains("scph", StringComparison.OrdinalIgnoreCase) || fn.Contains("ps2", StringComparison.OrdinalIgnoreCase) || fn.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) || fn.EndsWith(".rom", StringComparison.OrdinalIgnoreCase)))
                        {
                            destRelPath = $"pcsx2/bios/{fn}";
                        }
                        else if (byFileName.TryGetValue(fn, out var bi))
                        {
                            destRelPath = string.IsNullOrEmpty(bi.RelativePath) ? bi.FileName : bi.RelativePath;
                        }
                        else
                        {
                            string rel = Path.GetRelativePath(folderOrZipPath, file).Replace('\\', '/');
                            if (rel.StartsWith("system/", StringComparison.OrdinalIgnoreCase))
                                rel = rel.Substring(7);
                            destRelPath = rel;
                        }

                        string targetFull = Path.Combine(SystemDir, destRelPath.Replace('/', Path.DirectorySeparatorChar));
                        string? targetDir = Path.GetDirectoryName(targetFull);
                        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                            Directory.CreateDirectory(targetDir);

                        File.Copy(file, targetFull, overwrite: true);
                        imported++;
                    }
                }

                CheckAllBios();
                return (imported, $"Успешно импортировано файлов BIOS: {imported}");
            }
            catch (Exception ex)
            {
                return (imported, $"Ошибка при импорте пака BIOS: {ex.Message}");
            }
        });
    }

    public static async Task<bool> DownloadOpenFirmwareResourcesAsync(Action<string>? onStatus = null)
    {
        try
        {
            EnsureStandardBiosSubfolders();

            string targetFile = Path.Combine(SystemDir, "pcsx2", "resources", "GameIndex.yaml");
            string? dir = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            onStatus?.Invoke("Загрузка базы совместимости PCSX2 GameIndex.yaml...");

            string[] urls = new[]
            {
                "https://raw.githubusercontent.com/libretro/LRPS2/main/bin/resources/GameIndex.yaml",
                "https://raw.githubusercontent.com/PCSX2/pcsx2/master/bin/resources/GameIndex.yaml"
            };

            using var http = new System.Net.Http.HttpClient();
            http.Timeout = TimeSpan.FromMinutes(2);

            foreach (var url in urls)
            {
                try
                {
                    using var resp = await http.GetAsync(url).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        byte[] bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        if (bytes.Length > 1000)
                        {
                            await File.WriteAllBytesAsync(targetFile, bytes).ConfigureAwait(false);
                            onStatus?.Invoke($"База GameIndex.yaml успешно загружена ({bytes.Length / 1024} КБ)");
                            CheckAllBios();
                            return true;
                        }
                    }
                }
                catch { }
            }

            onStatus?.Invoke("Не удалось загрузить GameIndex.yaml с официальных репозиториев");
            return false;
        }
        catch (Exception ex)
        {
            onStatus?.Invoke($"Ошибка загрузки ресурсов: {ex.Message}");
            return false;
        }
    }

    public static (bool Success, string Message) ImportBios(string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath)) return (false, "Файл не найден");

        try
        {
            EnsureStandardBiosSubfolders();
            string fileName = Path.GetFileName(sourceFilePath);
            var match = _database.FirstOrDefault(b => b.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
            string dest = match != null ? match.ExpectedFullPath : Path.Combine(SystemDir, fileName);

            string? parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                Directory.CreateDirectory(parent);

            File.Copy(sourceFilePath, dest, overwrite: true);
            CheckAllBios();
            return (true, $"BIOS-файл {fileName} успешно установлен в {match?.SubfolderDisplay ?? "system/"}");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка импорта BIOS: {ex.Message}");
        }
    }
}
