using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace StormRetroArch;

public record CoreInfo(string Name, string Platform, string DllName, string Description, string Manufacturer = "Universal")
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
        new("Snes9x", "Super Nintendo (SNES)", "snes9x_libretro.dll", "Высокоточный и быстрый эмулятор SNES / Super Famicom", "Nintendo"),
        new("Genesis Plus GX", "Sega Genesis / Mega Drive", "genesis_plus_gx_libretro.dll", "Идеальная эмуляция Mega Drive, Master System, Game Gear", "Sega"),
        new("SwanStation", "Sony PlayStation 1", "swanstation_libretro.dll", "Ультра-производительный PS1 эмулятор с поддержкой PGXP и апскейла", "Sony"),
        new("FCEUmm", "Nintendo Entertainment System", "fceumm_libretro.dll", "Современный эмулятор NES / Famicom / Dendy с поддержкой мапперов", "Nintendo"),
        new("mGBA", "Game Boy Advance", "mgba_libretro.dll", "Быстрый и точный эмулятор GBA, GBC и классического Game Boy", "Nintendo"),
        new("melonDS DS", "Nintendo DS / DSi", "melondsds_libretro.dll", "Превосходная эмуляция Nintendo DS и DSi с сенсорным экраном", "Nintendo"),
        new("Mupen64Plus-Next", "Nintendo 64", "mupen64plus_next_libretro.dll", "Мощный эмулятор N64 с плагинами GLideN64 и ParaLLEl RDP", "Nintendo"),
        new("PPSSPP", "Sony PlayStation Portable", "ppsspp_libretro.dll", "Быстрая эмуляция PSP с HD текстурами и шейдерами", "Sony"),
        new("Flycast", "Sega Dreamcast / NAOMI", "flycast_libretro.dll", "Современный мультиплатформенный эмулятор Dreamcast и NAOMI", "Sega"),
        new("Beetle Saturn", "Sega Saturn", "mednafen_saturn_libretro.dll", "Точнейшая потактовая эмуляция архитектуры Sega Saturn", "Sega"),
        new("FBNeo", "Arcade / Neo Geo", "fbneo_libretro.dll", "Флагманский эмулятор аркадных автоматов, Neo Geo и CPS-1/2/3", "Arcade"),
        new("DOSBox-Pure", "MS-DOS PC", "dosbox_pure_libretro.dll", "Запуск классических игр MS-DOS прямо из ZIP-архивов с геймпадом", "PC / DOS"),
        new("Beetle PCE Fast", "PC Engine / TurboGrafx-16", "mednafen_pce_fast_libretro.dll", "Быстрый эмулятор PC Engine HuCard и CD-ROM", "NEC"),
        new("Stella", "Atari 2600", "stella_libretro.dll", "Эталонный эмулятор классической приставки Atari 2600 VCS", "Atari"),
        new("Handy", "Atari Lynx", "handy_libretro.dll", "Эмулятор портативной консоли Atari Lynx", "Atari"),
        new("Beetle NeoPop", "Neo Geo Pocket", "mednafen_ngp_libretro.dll", "Эмулятор Neo Geo Pocket и Pocket Color", "SNK"),
        new("Beetle WonderSwan", "Bandai WonderSwan", "mednafen_wswan_libretro.dll", "Эмулятор портативной консоли WonderSwan / Color", "Bandai"),
        new("Citra", "Nintendo 3DS", "citra_libretro.dll", "Эмулятор Nintendo 3DS с апскейлом геометрии", "Nintendo"),
        new("Dolphin", "Nintendo GameCube / Wii", "dolphin_libretro.dll", "Флагманский эмулятор GameCube и Wii с высокой кадровой частотой", "Nintendo"),
        new("Gambatte", "Game Boy / Color", "gambatte_libretro.dll", "Потактово точный эмулятор оригинального Game Boy", "Nintendo"),
        new("PCSX-ReARMed", "Sony PlayStation 1 (Fast)", "pcsx_rearmed_libretro.dll", "Оптимизированный быстрый эмулятор PS1", "Sony"),
        new("Picodrive", "Sega 32X / Sega CD", "picodrive_libretro.dll", "Специализированный эмулятор Genesis, 32X и Sega CD", "Sega"),
        new("Nestopia", "Nintendo Entertainment System", "nestopia_libretro.dll", "Циклически точный эмулятор Famicom / NES", "Nintendo"),
        new("ProSystem", "Atari 7800", "prosystem_libretro.dll", "Эмулятор консоли Atari 7800 ProSystem", "Atari"),
        new("MAME 2003-Plus", "Arcade Classics", "mame2003_plus_libretro.dll", "Классический аркадный движок с улучшенным звуком и управлением", "Arcade")
    };

    public static readonly List<CoreInfo> AllCoresCatalog = new()
    {
        // === NINTENDO ===
        new("FCEUmm", "Nintendo Entertainment System", "fceumm_libretro.dll", "Современный эмулятор NES / Famicom / Dendy с поддержкой всех мапперов", "Nintendo"),
        new("Nestopia UE", "Nintendo Entertainment System", "nestopia_libretro.dll", "Потактово точный эмулятор NES / Famicom с поддержкой FDS и NSF", "Nintendo"),
        new("Mesen", "Nintendo Entertainment System", "mesen_libretro.dll", "Ультра-точный эмулятор NES с HD-паками, оверклокингом и отладчиком", "Nintendo"),
        new("QuickNES", "Nintendo Entertainment System", "quicknes_libretro.dll", "Быстрый легковесный эмулятор NES с минимальным инпут-лагом", "Nintendo"),
        new("Snes9x", "Super Nintendo (SNES)", "snes9x_libretro.dll", "Высокоточный и быстрый эмулятор SNES / Super Famicom", "Nintendo"),
        new("Snes9x 2010", "Super Nintendo (SNES)", "snes9x2010_libretro.dll", "Облегченная версия Snes9x для старых ПК и нетбуков", "Nintendo"),
        new("Snes9x 2005", "Super Nintendo (SNES)", "snes9x2005_libretro.dll", "Ультра-быстрая версия Snes9x для максимальной скорости", "Nintendo"),
        new("bsnes", "Super Nintendo (SNES)", "bsnes_libretro.dll", "100% потактово точный эмулятор SNES Хиронари Нисикавы (byuu)", "Nintendo"),
        new("bsnes HD beta", "Super Nintendo (SNES)", "bsnes_hd_beta_libretro.dll", "SNES эмулятор с поддержкой широкоформатного Mode 7 (16:9) и HD-рендера", "Nintendo"),
        new("Mupen64Plus-Next", "Nintendo 64", "mupen64plus_next_libretro.dll", "Мощный эмулятор N64 с плагинами GLideN64 и ParaLLEl RDP", "Nintendo"),
        new("ParaLLEl N64", "Nintendo 64", "parallel_n64_libretro.dll", "Эмулятор N64 с низкоуровневой Vulkan-эмуляцией графического чипа Reality Coprocessor", "Nintendo"),
        new("Gambatte", "Game Boy / Color", "gambatte_libretro.dll", "Потактово точный эмулятор Game Boy и Game Boy Color с кастомными палитрами", "Nintendo"),
        new("SameBoy", "Game Boy / Color", "sameboy_libretro.dll", "Продвинутый эмулятор Game Boy с идеальным звуком и поддержкой Super Game Boy", "Nintendo"),
        new("Gearboy", "Game Boy / Color", "gearboy_libretro.dll", "Кроссплатформенный быстрый эмулятор GB / GBC", "Nintendo"),
        new("TGB Dual", "Game Boy / Color", "tgbdual_libretro.dll", "Эмулятор GB/GBC с одновременным запуском двух экранов и эмуляцией Link Cable", "Nintendo"),
        new("mGBA", "Game Boy Advance", "mgba_libretro.dll", "Флагманский эмулятор GBA с поддержкой датчиков солнечного света и гироскопа", "Nintendo"),
        new("VBA-M", "Game Boy Advance", "vbam_libretro.dll", "Популярный эмулятор VisualBoyAdvance-M для GBA/GBC", "Nintendo"),
        new("VBA Next", "Game Boy Advance", "vba_next_libretro.dll", "Оптимизированная версия VBA для повышенного FPS на слабых процессорах", "Nintendo"),
        new("GPSP", "Game Boy Advance", "gpsp_libretro.dll", "Быстрый эмулятор GBA с динамической рекомпиляцией DynaRec", "Nintendo"),
        new("melonDS DS", "Nintendo DS / DSi", "melondsds_libretro.dll", "Флагманская эмуляция Nintendo DS и DSi с сенсорным экраном, камерой и микрофоном", "Nintendo"),
        new("DeSmuME", "Nintendo DS", "desmume_libretro.dll", "Классический эмулятор Nintendo DS с расширенными настройками разрешения", "Nintendo"),
        new("DeSmuME 2015", "Nintendo DS", "desmume2015_libretro.dll", "Легковесная версия DeSmuME для энергоэффективной игры", "Nintendo"),
        new("Citra", "Nintendo 3DS", "citra_libretro.dll", "Эмулятор Nintendo 3DS с поддержкой высокого разрешения, стерео-3D и шейдеров", "Nintendo"),
        new("Citra Canary", "Nintendo 3DS", "citra_canary_libretro.dll", "Экспериментальная сборка Citra с новейшими оптимизациями шейдеров", "Nintendo"),
        new("Dolphin", "Nintendo GameCube / Wii", "dolphin_libretro.dll", "Легендарный эмулятор GameCube и Wii с высокой точностью и поддержкой Wiimote", "Nintendo"),
        new("Beetle VB", "Nintendo Virtual Boy", "mednafen_vb_libretro.dll", "Эмулятор 3D-консоли Virtual Boy с анаглифным стереорежимом", "Nintendo"),
        new("PokeMini", "Pokemon Mini", "pokemini_libretro.dll", "Эмулятор карманной мини-консоли Pokémon Mini от Nintendo", "Nintendo"),

        // === SEGA ===
        new("Genesis Plus GX", "Sega Genesis / Mega Drive", "genesis_plus_gx_libretro.dll", "Идеальная эмуляция Mega Drive, Master System, Game Gear, SG-1000", "Sega"),
        new("Genesis Plus GX Wide", "Sega Mega Drive (16:9)", "genesis_plus_gx_wide_libretro.dll", "Эмулятор Mega Drive с расширенным широкоформатным полем зрения 16:9", "Sega"),
        new("Picodrive", "Sega 32X / Sega CD", "picodrive_libretro.dll", "Быстрый эмулятор Sega 32X, Mega CD, Mega Drive и Master System", "Sega"),
        new("Flycast", "Sega Dreamcast / NAOMI", "flycast_libretro.dll", "Современный мультиплатформенный эмулятор Dreamcast, NAOMI и Atomiswave", "Sega"),
        new("Beetle Saturn", "Sega Saturn", "mednafen_saturn_libretro.dll", "Точнейшая потактовая эмуляция архитектуры Sega Saturn с двумя SH-2 процессорами", "Sega"),
        new("YabaSanshiro", "Sega Saturn", "yabasanshiro_libretro.dll", "Высокопроизводительный эмулятор Sega Saturn с аппаратным апскейлом геометрии", "Sega"),
        new("Kronos", "Sega Saturn / ST-V", "kronos_libretro.dll", "Современный эмулятор Saturn и аркадной платформы Sega ST-V на OpenGL/Vulkan", "Sega"),
        new("Gearsystem", "Sega Master System / Game Gear", "gearsystem_libretro.dll", "Потактово точный эмулятор Master System, Game Gear и SG-1000", "Sega"),
        new("BlastEm", "Sega Genesis / Mega Drive", "blastem_libretro.dll", "Ультра-точный потактовый эмулятор Genesis с идеальной точностью таймингов YM2612", "Sega"),

        // === SONY ===
        new("SwanStation", "Sony PlayStation 1", "swanstation_libretro.dll", "Ультра-производительный PS1 эмулятор со сглаживанием PGXP, Widescreen и разгоном CPU", "Sony"),
        new("Beetle PSX", "Sony PlayStation 1", "mednafen_psx_libretro.dll", "Эталонный потактово точный эмулятор оригинальной консоли PS1", "Sony"),
        new("Beetle PSX HW", "Sony PlayStation 1 (Vulkan/GL)", "mednafen_psx_hw_libretro.dll", "Аппаратно-ускоренная версия Beetle PSX с рендером до 16x внутреннего разрешения", "Sony"),
        new("PCSX-ReARMed", "Sony PlayStation 1 (Fast)", "pcsx_rearmed_libretro.dll", "Оптимизированный быстрый эмулятор PS1 для слабых систем", "Sony"),
        new("Play!", "Sony PlayStation 2", "play_libretro.dll", "Портативный независимый эмулятор архитектуры PlayStation 2", "Sony"),
        new("PPSSPP", "Sony PlayStation Portable", "ppsspp_libretro.dll", "Быстрая эмуляция PSP с HD текстурами, анизотропной фильтрацией и шейдерами", "Sony"),

        // === ARCADE & NEO GEO ===
        new("FBNeo", "Arcade / Neo Geo / CPS", "fbneo_libretro.dll", "Флагманский эмулятор аркадных автоматов, Neo Geo MVS, CPS-1/2/3, PGM и консолей", "Arcade"),
        new("MAME 2003-Plus", "Arcade Classics", "mame2003_plus_libretro.dll", "Классический аркадный движок с сотнями исправлений управления, звука и оверлеев", "Arcade"),
        new("MAME 2010", "Arcade Classics 0.139", "mame2010_libretro.dll", "Аркадный движок на базе MAME 0.139 с поддержкой игр эпохи конца 1990-х годов", "Arcade"),
        new("MAME (Current)", "Arcade & Retro Systems", "mame_libretro.dll", "Полная актуальная версия эмулятора MAME с поддержкой десятков тысяч аркадных плат", "Arcade"),

        // === ATARI ===
        new("Stella", "Atari 2600", "stella_libretro.dll", "Эталонный эмулятор классической приставки Atari 2600 VCS", "Atari"),
        new("Stella 2014", "Atari 2600 (Legacy)", "stella2014_libretro.dll", "Легковесный эмулятор Atari 2600 для систем начального уровня", "Atari"),
        new("ProSystem", "Atari 7800", "prosystem_libretro.dll", "Эмулятор приставки Atari 7800 ProSystem", "Atari"),
        new("Handy", "Atari Lynx", "handy_libretro.dll", "Эмулятор портативной консоли Atari Lynx с поддержкой поворота экрана", "Atari"),
        new("Beetle Lynx", "Atari Lynx", "mednafen_lynx_libretro.dll", "Высокоточная потактовая эмуляция Atari Lynx", "Atari"),
        new("Virtual Jaguar", "Atari Jaguar", "virtualjaguar_libretro.dll", "Эмулятор 64-битной консоли Atari Jaguar", "Atari"),
        new("Atari800", "Atari 800 / 5200 / XE", "atari800_libretro.dll", "Эмулятор линейки 8-битных компьютеров Atari и консоли Atari 5200", "Atari"),
        new("Hatari", "Atari ST / STE / TT / Falcon", "hatari_libretro.dll", "Эмулятор 16/32-битных компьютеров семейства Atari ST", "Atari"),

        // === SNK ===
        new("Beetle NeoPop", "Neo Geo Pocket / Color", "mednafen_ngp_libretro.dll", "Эмулятор 16-битных портативных консолей Neo Geo Pocket и Pocket Color", "SNK"),
        new("RACE", "Neo Geo Pocket / Color", "race_libretro.dll", "Быстрый эмулятор Neo Geo Pocket для мгновенного запуска", "SNK"),
        new("NeoCD", "Neo Geo CD", "neocd_libretro.dll", "Специализированный эмулятор консоли Neo Geo CD со сверхбыстрой загрузкой дисков", "SNK"),

        // === COMMODORE & AMIGA ===
        new("Commodore 64 (VICE x64)", "Commodore 64", "vice_x64_libretro.dll", "Легендарный эмулятор C64 с точнейшим звуком чипа SID 6581/8580", "Commodore"),
        new("Commodore 64 Fast (VICE x64sc)", "Commodore 64 (Accurate)", "vice_x64sc_libretro.dll", "Циклически точная версия VICE C64 с эмуляцией коллизий спрайтов", "Commodore"),
        new("Commodore 128 (VICE x128)", "Commodore 128", "vice_x128_libretro.dll", "Эмулятор компьютера Commodore 128 с поддержкой VDC 80 колонок", "Commodore"),
        new("Commodore VIC-20 (VICE xvic)", "Commodore VIC-20", "vice_xvic_libretro.dll", "Эмулятор раннего домашнего компьютера Commodore VIC-20", "Commodore"),
        new("Commodore Plus/4 (VICE xplus4)", "Commodore Plus/4", "vice_xplus4_libretro.dll", "Эмулятор серии Commodore 264 (C16, C116, Plus/4)", "Commodore"),
        new("PUAE", "Commodore Amiga (OCS/ECS/AGA)", "puae_libretro.dll", "Эмулятор компьютеров Amiga 500, 1200, 4000 и консоли Amiga CD32", "Commodore"),

        // === PC & RETRO COMPUTERS ===
        new("DOSBox-Pure", "MS-DOS PC", "dosbox_pure_libretro.dll", "Запуск классических игр MS-DOS прямо из ZIP-архивов с геймпадом и авто-маппингом", "PC / DOS"),
        new("DOSBox-SVN", "MS-DOS PC", "dosbox_svn_libretro.dll", "Классический эмулятор DOSBox с чистым эмулируемым окружением x86 реального режима", "PC / DOS"),
        new("Neko Project II Kai", "NEC PC-9801 / PC-9821", "np2kai_libretro.dll", "Эмулятор японских компьютеров NEC PC-98 с поддержкой звука PC-9801-86", "PC / DOS"),
        new("Quasi88", "NEC PC-8801", "quasi88_libretro.dll", "Эмулятор классической японской платформы NEC PC-8800", "PC / DOS"),
        new("PX68K", "Sharp X68000", "px68k_libretro.dll", "Эмулятор мощного японского компьютера Sharp X68000 с поддержкой MIDI", "PC / DOS"),
        new("blueMSX", "MSX / MSX2 / ColecoVision", "bluemsx_libretro.dll", "Универсальный эмулятор стандарта MSX, MSX2, MSX2+, Turbo R и ColecoVision", "PC / DOS"),
        new("fMSX", "MSX / MSX2", "fmsx_libretro.dll", "Один из старейших и наиболее стабильных эмуляторов компьютеров MSX", "PC / DOS"),
        new("Fuse", "ZX Spectrum", "fuse_libretro.dll", "Эмулятор британского культового микрокомпьютера ZX Spectrum 48K/128K", "PC / DOS"),
        new("Caprice32", "Amstrad CPC", "cap32_libretro.dll", "Эмулятор европейских компьютеров Amstrad CPC 464, 664, 6128", "PC / DOS"),

        // === NEC & OTHER CONSOLES ===
        new("Beetle PCE Fast", "PC Engine / TurboGrafx-16", "mednafen_pce_fast_libretro.dll", "Быстрый эмулятор PC Engine HuCard и CD-ROM игр", "NEC"),
        new("Beetle SuperGrafx", "PC Engine SuperGrafx", "mednafen_supergrafx_libretro.dll", "Эмулятор расширенной консоли SuperGrafx с двумя видеоконтроллерами", "NEC"),
        new("Beetle PC-FX", "NEC PC-FX", "mednafen_pcfx_libretro.dll", "Эмулятор 32-битной CD-ROM консоли NEC PC-FX", "NEC"),
        new("Opera (4DO)", "Panasonic 3DO Interactive", "opera_libretro.dll", "Эмулятор 32-битной приставки 3DO Interactive Multiplayer", "Other"),
        new("Beetle WonderSwan", "Bandai WonderSwan", "mednafen_wswan_libretro.dll", "Эмулятор портативной консоли WonderSwan / Color / SwanCrystal", "Bandai"),
        new("Gearcoleco", "ColecoVision", "gearcoleco_libretro.dll", "Потактово точный эмулятор ретро-консоли ColecoVision", "Other"),
        new("Vectrex (VecX)", "Smith GCE Vectrex", "vecx_libretro.dll", "Эмулятор векторной игровой консоли Vectrex с рендером световых лучей", "Other"),
        new("FreeIntv", "Mattel Intellivision", "freeintv_libretro.dll", "Эмулятор классической приставки Intellivision 1979 года", "Other"),
        new("ScummVM", "Point-and-Click Quests", "scummvm_libretro.dll", "Виртуальная машина для запуска квестов LucasArts, Sierra, Revolution", "Other"),
        new("TIC-80", "Fantasy Computer TIC-80", "tic80_libretro.dll", "Виртуальная фантазийная консоль для ретро-инди игр и демосцены", "Other"),
        new("Retro8 (PICO-8)", "Fantasy Console PICO-8", "retro8_libretro.dll", "Эмулятор популярной ретро-консоли PICO-8", "Other")
    };

    public static bool IsCoreInstalled(string dllName)
    {
        string fullPath = Path.Combine(CoresDir, dllName);
        return File.Exists(fullPath);
    }

    public static List<CoreInfo> GetCoresByManufacturer(string manufacturer)
    {
        if (string.IsNullOrEmpty(manufacturer) || manufacturer == "Все" || manufacturer == "All")
            return AllCoresCatalog;

        return AllCoresCatalog.Where(c => c.Manufacturer.Equals(manufacturer, StringComparison.OrdinalIgnoreCase)).ToList();
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
        string tempZip = Path.Combine(DownloadsDir, zipName);

        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(3);

            progress?.Report((5, $"Подключение к {BuildbotBaseUrl}..."));

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
                    progress?.Report((pct, $"Загрузка... {totalRead / 1024} KB / {totalBytes / 1024} KB"));
                }
            }

            fileStream.Close();
            progress?.Report((85, "Распаковка ядра..."));

            await Task.Run(() =>
            {
                using var archive = ZipFile.OpenRead(tempZip);
                foreach (var entry in archive.Entries)
                {
                    if (entry.Name.Equals(dllName, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.ExtractToFile(targetDllPath, true);
                        break;
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
        string retroArchExe = Path.Combine(AppDir, "retroarch.exe");
        if (!File.Exists(retroArchExe))
        {
            onError?.Invoke($"Исполняемый файл retroarch.exe не найден в {AppDir}");
            return;
        }

        if (string.IsNullOrEmpty(coreDll))
        {
            var (_, identifiedCore, _) = IdentifyRom(romPath);
            coreDll = identifiedCore;
        }

        string fullCorePath = Path.Combine(CoresDir, coreDll);
        string args = File.Exists(fullCorePath)
            ? $"-L \"{fullCorePath}\" \"{romPath}\""
            : $"\"{romPath}\"";

        try
        {
            if (SettingsManager.Current.SmartProfileAutoApply)
            {
                try
                {
                    var profile = SmartProfileManager.DetectProfile(Path.GetFileName(romPath), coreDll ?? "");
                    SmartProfileManager.ApplyProfile(profile, AppDir);
                }
                catch { }
            }

            var psi = new ProcessStartInfo
            {
                FileName = retroArchExe,
                Arguments = args,
                WorkingDirectory = AppDir,
                UseShellExecute = false
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Не удалось запустить игру: {ex.Message}");
        }
    }

    public static void LaunchOzone(Action<string>? onError = null)
    {
        string retroArchExe = Path.Combine(AppDir, "retroarch.exe");
        if (!File.Exists(retroArchExe))
        {
            onError?.Invoke($"Файл retroarch.exe не найден в {AppDir}");
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = retroArchExe,
                Arguments = "--menu -v",
                WorkingDirectory = AppDir,
                UseShellExecute = false
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Ошибка запуска меню RetroArch: {ex.Message}");
        }
    }

    public static void LaunchDesktopQt(Action<string>? onError = null)
    {
        string retroArchExe = Path.Combine(AppDir, "retroarch.exe");
        if (!File.Exists(retroArchExe))
        {
            onError?.Invoke($"Файл retroarch.exe не найден в {AppDir}");
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = retroArchExe,
                Arguments = "--desktop",
                WorkingDirectory = AppDir,
                UseShellExecute = false
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Ошибка запуска рабочего стола RetroArch: {ex.Message}");
        }
    }

    public static string? FindRetroArchExecutable()
    {
        string exe = Path.Combine(AppDir, "retroarch.exe");
        if (File.Exists(exe)) return exe;
        string parent = Path.Combine(Directory.GetParent(AppDir)?.FullName ?? "", "retroarch.exe");
        if (File.Exists(parent)) return parent;
        return null;
    }

        public static async Task<bool> LaunchGameAsync(RomItem rom, Action<string>? onError = null)
    {
        return await LaunchGameAsync(rom.FilePath, rom.CoreDll, onError);
    }

public static async Task<bool> LaunchGameAsync(string romPath, string? coreDll = null, Action<string>? onError = null)
    {
        await Task.Run(() => LaunchGame(romPath, coreDll, onError));
        return true;
    }

}
