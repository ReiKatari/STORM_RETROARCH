package com.storm.retroarch

import android.content.Context
import android.net.Uri
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.provider.OpenableColumns
import org.json.JSONArray
import org.json.JSONObject
import java.io.BufferedInputStream
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.io.InputStream
import java.net.HttpURLConnection
import java.net.URL
import java.nio.charset.StandardCharsets
import java.util.Locale
import java.util.concurrent.Executors
import java.util.zip.GZIPInputStream
import java.util.zip.Inflater
import java.util.zip.InflaterInputStream
import java.util.zip.ZipInputStream

data class MobileCoreInfo(
    val id: String,
    val displayName: String,
    val soBaseName: String,
    val platformDisplay: String,
    val platformKeys: Set<String>,
    val extensions: Set<String>,
    val descriptionRu: String,
    val descriptionEn: String,
    val isExperimental: Boolean = false,
    val platformId: String = "",
    val zipArm64: String = "${soBaseName}_android.so.zip",
    val zipArm32: String = "${soBaseName}_android.so.zip",
    val soInsideZip: String = "${soBaseName}_android.so",
    val jniLibName: String = "lib${soBaseName}_android.so",
    val biosFiles: List<String> = emptyList(),
    val recommended: Boolean = false,
    val prebundledArm64: Boolean = false,
    val prebundledArm32: Boolean = false
) {
    fun localizedDescription(lang: String = StormLocalization.currentLanguage): String {
        return if (lang.lowercase(Locale.ROOT) == "ru") descriptionRu else descriptionEn
    }
}

data class CoreOptionSpec(
    val key: String,
    val labelRu: String,
    val labelEn: String,
    val values: List<String>,
    val defaultValue: String
)

data class CorePackDefinition(
    val id: String,
    val titleRu: String,
    val titleEn: String,
    val descriptionRu: String,
    val descriptionEn: String,
    val coreIds: List<String>
)

object MobileCoreRegistry {

    const val STORM_PUBLIC_ROOT = "/storage/emulated/0/STORM RETROARCH"
    const val STORM_MIRROR_CORES_BASE = "https://stormgamesworld.ru/uploads/programs/STORM_RETROARCH/cores"
    const val BUILDBOT_ANDROID_BASE = "https://buildbot.libretro.com/nightly/android/latest"
    private const val USER_AGENT = "STORM-RETROARCH/2.7.3 (Android; LibretroCoreLoader)"
    private const val MIN_VALID_SO_BYTES = 32768L

    private val downloadExecutor = Executors.newFixedThreadPool(2)
    private val mainHandler = Handler(Looper.getMainLooper())
    private val catalogLock = Any()

    @Volatile
    private var loadedCatalogCores: List<MobileCoreInfo>? = null

    val ALL_CORES: List<MobileCoreInfo>
        get() = loadedCatalogCores ?: FALLBACK_FLAGSHIP_CORES

    private val PLATFORM_DISPLAY_NAMES: Map<String, String> = mapOf(
        "nes" to "Nintendo NES / Famicom",
        "fds" to "Famicom Disk System",
        "snes" to "Super Nintendo (SNES)",
        "n64" to "Nintendo 64",
        "gamecube" to "Nintendo GameCube",
        "wii" to "Nintendo Wii",
        "gamecube_wii" to "GameCube / Wii",
        "gc_wii" to "GameCube / Wii",
        "gb" to "Game Boy",
        "gbc" to "Game Boy Color",
        "gb_gbc" to "Game Boy / Game Boy Color",
        "gba" to "Game Boy Advance",
        "nds" to "Nintendo DS",
        "3ds" to "Nintendo 3DS",
        "vb" to "Virtual Boy",
        "virtualboy" to "Virtual Boy",
        "pokemini" to "Pokemon Mini",
        "gw" to "Game & Watch",
        "ps1" to "Sony PlayStation (PS1)",
        "psx" to "Sony PlayStation (PS1)",
        "ps2" to "Sony PlayStation 2",
        "ps3" to "Sony PlayStation 3",
        "psp" to "Sony PSP",
        "psvita" to "Sony PS Vita",
        "genesis" to "Sega Mega Drive / Genesis",
        "megadrive" to "Sega Mega Drive / Genesis",
        "segacd" to "Sega Mega-CD / Sega CD",
        "sega32x" to "Sega 32X",
        "sms_gg" to "Sega Master System / Game Gear",
        "mastersystem" to "Sega Master System / Game Gear",
        "sg1000" to "Sega SG-1000",
        "segapico" to "Sega Pico",
        "saturn" to "Sega Saturn",
        "dreamcast" to "Sega Dreamcast / NAOMI",
        "naomi" to "Sega NAOMI / Atomiswave",
        "arcade" to "Arcade (FBNeo / MAME)",
        "neogeo" to "SNK Neo Geo AES / MVS",
        "neogeocd" to "SNK Neo Geo CD",
        "neocd" to "SNK Neo Geo CD",
        "cps" to "Capcom CPS-1 / CPS-2 / CPS-3",
        "pce" to "PC Engine / TurboGrafx-16",
        "pcecd" to "PC Engine CD",
        "pcfx" to "NEC PC-FX",
        "ngp" to "Neo Geo Pocket / Color",
        "wonderswan" to "Bandai WonderSwan / Color",
        "3do" to "3DO Interactive Multiplayer",
        "cdi" to "Philips CD-i",
        "vectrex" to "GCE Vectrex",
        "coleco" to "ColecoVision",
        "colecovision" to "ColecoVision",
        "intellivision" to "Mattel Intellivision",
        "odyssey2" to "Magnavox Odyssey 2",
        "channelf" to "Fairchild Channel F",
        "atari" to "Atari Classics",
        "atari2600" to "Atari 2600",
        "atari5200" to "Atari 5200",
        "atari7800" to "Atari 7800",
        "atari800" to "Atari 8-bit / XEGS",
        "lynx" to "Atari Lynx",
        "jaguar" to "Atari Jaguar",
        "atarist" to "Atari ST / STE / Falcon",
        "dos" to "MS-DOS / Windows 9x",
        "scummvm" to "ScummVM Adventure Engine",
        "amiga" to "Commodore Amiga",
        "c64" to "Commodore 64 / 128 / VIC-20",
        "zxspectrum" to "ZX Spectrum",
        "zx81" to "Sinclair ZX81",
        "msx" to "MSX / MSX2 / TurboR",
        "amstrad" to "Amstrad CPC / GX4000",
        "amstradcpc" to "Amstrad CPC / GX4000",
        "pc88" to "NEC PC-8801",
        "pc98" to "NEC PC-9801",
        "x68000" to "Sharp X68000",
        "x1" to "Sharp X1",
        "bk0010" to "Soviet BK-0010 / BK-0011M",
        "bk" to "Soviet BK-0010 / BK-0011M",
        "rpgmaker" to "RPG Maker 2000 / 2003 (EasyRPG)",
        "fantasy" to "Fantasy Consoles (TIC-80 / PICO-8 / WASM-4)",
        "pico8" to "PICO-8",
        "tic80" to "TIC-80",
        "ports" to "Classic Game Engines & Ports",
        "doom" to "DOOM Engine"
    )

    private val PREFERRED_CORE_ORDER: Map<String, List<String>> = mapOf(
        "nes" to listOf("fceumm", "nestopia", "mesen", "quicknes"),
        "fds" to listOf("fceumm", "nestopia", "mesen"),
        "snes" to listOf("snes9x", "snes9x2010", "bsnes", "mesen-s"),
        "n64" to listOf("mupen64plus_next_gles3", "mupen64plus_next_gles2", "parallel_n64"),
        "gb" to listOf("gambatte", "sameboy", "mgba", "gearboy", "tgbdual"),
        "gbc" to listOf("gambatte", "sameboy", "mgba", "gearboy", "tgbdual"),
        "gb_gbc" to listOf("gambatte", "sameboy", "mgba", "gearboy", "tgbdual"),
        "gba" to listOf("mgba", "vbam", "vba_next", "gpsp"),
        "nds" to listOf("melonds", "melondsds", "desmume", "desmume2015"),
        "3ds" to listOf("citra", "citra2018"),
        "gamecube" to listOf("dolphin"),
        "wii" to listOf("dolphin"),
        "gamecube_wii" to listOf("dolphin"),
        "ps1" to listOf("pcsx_rearmed", "swanstation", "mednafen_psx_hw", "mednafen_psx"),
        "ps2" to listOf("play", "pcsx2"),
        "psp" to listOf("ppsspp"),
        "genesis" to listOf("genesis_plus_gx", "picodrive", "blastem"),
        "segacd" to listOf("genesis_plus_gx", "picodrive"),
        "sega32x" to listOf("picodrive"),
        "sms_gg" to listOf("genesis_plus_gx", "picodrive", "smsplus", "gearsystem"),
        "sg1000" to listOf("genesis_plus_gx", "gearsystem"),
        "saturn" to listOf("yabasanshiro", "yabause", "mednafen_saturn", "kronos"),
        "dreamcast" to listOf("flycast"),
        "naomi" to listOf("flycast"),
        "arcade" to listOf("fbneo", "mame2003_plus", "mame2010", "mamearcade"),
        "neogeo" to listOf("fbneo", "geolith", "mame2003_plus"),
        "neogeocd" to listOf("neocd", "fbneo"),
        "cps" to listOf("fbneo", "mame2003_plus"),
        "pce" to listOf("mednafen_pce_fast", "mednafen_supergrafx", "mednafen_pce"),
        "pcecd" to listOf("mednafen_pce_fast", "mednafen_pce"),
        "pcfx" to listOf("mednafen_pcfx"),
        "ngp" to listOf("mednafen_ngp", "race"),
        "wonderswan" to listOf("mednafen_wswan"),
        "3do" to listOf("opera"),
        "atari2600" to listOf("stella2014", "stella"),
        "atari5200" to listOf("a5200", "atari800"),
        "atari7800" to listOf("prosystem"),
        "lynx" to listOf("handy", "mednafen_lynx"),
        "jaguar" to listOf("virtualjaguar"),
        "dos" to listOf("dosbox_pure", "dosbox_core", "dosbox_svn"),
        "scummvm" to listOf("scummvm"),
        "amiga" to listOf("puae", "puae2021", "uae4arm"),
        "c64" to listOf("vice_x64", "vice_x64sc", "frodo"),
        "zxspectrum" to listOf("fuse", "81"),
        "msx" to listOf("bluemsx", "fmsx"),
        "pc98" to listOf("np2kai", "nekop2"),
        "x68000" to listOf("px68k"),
        "doom" to listOf("prboom", "vitaquake2"),
        "ports" to listOf("prboom", "nxengine", "tyrquake", "ecwolf")
    )

    val CORE_PACKS: List<CorePackDefinition> = listOf(
        CorePackDefinition(
            id = "essential",
            titleRu = "Базовый набор 8/16-бит + PS1 + GBA/NDS",
            titleEn = "Essential 8/16-Bit + PS1 + GBA/NDS Pack",
            descriptionRu = "Главные ядра для NES, SNES, Genesis/Mega-CD, GB/GBC, GBA, NDS и PlayStation 1.",
            descriptionEn = "Flagship cores for NES, SNES, Genesis/Mega-CD, GB/GBC, GBA, NDS, and PlayStation 1.",
            coreIds = listOf("fceumm", "snes9x", "genesis_plus_gx", "gambatte", "mgba", "melonds", "pcsx_rearmed")
        ),
        CorePackDefinition(
            id = "sony_sega_3d",
            titleRu = "3D Консоли Sony, Sega, Nintendo & 3DO",
            titleEn = "Sony, Sega, Nintendo & 3DO 3D Consoles",
            descriptionRu = "SwanStation HD, PPSSPP, Mupen64Plus-Next GLES3, Flycast Dreamcast, PicoDrive 32X, Opera 3DO, Play! PS2.",
            descriptionEn = "SwanStation HD, PPSSPP, Mupen64Plus-Next GLES3, Flycast Dreamcast, PicoDrive 32X, Opera 3DO, Play! PS2.",
            coreIds = listOf("swanstation", "ppsspp", "mupen64plus_next_gles3", "flycast", "picodrive", "opera", "yabasanshiro", "play")
        ),
        CorePackDefinition(
            id = "arcade_pc",
            titleRu = "Аркады, MS-DOS и Ретро-ПК",
            titleEn = "Arcades, MS-DOS & Home Computers",
            descriptionRu = "FinalBurn Neo, MAME 2003-Plus, DOSBox-Pure, ScummVM, PUAE Amiga, VICE C64, Fuse ZX, NP2kai PC-98, PX68k.",
            descriptionEn = "FinalBurn Neo, MAME 2003-Plus, DOSBox-Pure, ScummVM, PUAE Amiga, VICE C64, Fuse ZX, NP2kai PC-98, PX68k.",
            coreIds = listOf("fbneo", "mame2003_plus", "dosbox_pure", "scummvm", "puae", "vice_x64", "fuse", "prboom", "np2kai", "px68k")
        ),
        CorePackDefinition(
            id = "all_recommended",
            titleRu = "Все рекомендованные ядра (Полный комплект)",
            titleEn = "All Recommended Flagship Cores",
            descriptionRu = "Все 20+ эталонных ядер для мгновенного запуска игр всех основных консолей, аркад и ПК.",
            descriptionEn = "All 20+ flagship cores for instant play across major consoles, handhelds, arcades, and PCs.",
            coreIds = listOf(
                "fceumm", "snes9x", "genesis_plus_gx", "picodrive", "gambatte", "mgba", "melonds",
                "mupen64plus_next_gles3", "pcsx_rearmed", "swanstation", "ppsspp", "flycast",
                "fbneo", "mame2003_plus", "dosbox_pure", "scummvm", "mednafen_pce_fast",
                "mednafen_ngp", "mednafen_wswan", "opera", "stella2014", "prboom", "puae", "vice_x64", "fuse"
            )
        )
    )

    private val CORE_LIBRARY_OPT_DIRS: Map<String, List<String>> = mapOf(
        "pcsx_rearmed" to listOf("PCSX-ReARMed", "pcsx_rearmed"),
        "swanstation" to listOf("SwanStation", "swanstation"),
        "mupen64plus_next_gles3" to listOf("Mupen64Plus-Next", "mupen64plus_next_gles3", "mupen64plus_next"),
        "mupen64plus_next_gles2" to listOf("Mupen64Plus-Next", "mupen64plus_next_gles2", "mupen64plus_next"),
        "ppsspp" to listOf("PPSSPP", "ppsspp"),
        "mgba" to listOf("mGBA", "mgba"),
        "snes9x" to listOf("Snes9x", "snes9x"),
        "genesis_plus_gx" to listOf("Genesis Plus GX", "genesis_plus_gx"),
        "dosbox_pure" to listOf("DOSBox-pure", "dosbox_pure"),
        "flycast" to listOf("Flycast", "flycast"),
        "melonds" to listOf("melonDS", "melonds"),
        "fbneo" to listOf("FinalBurn Neo", "fbneo"),
        "fceumm" to listOf("FCEUmm", "fceumm"),
        "gambatte" to listOf("Gambatte", "gambatte"),
        "picodrive" to listOf("PicoDrive", "picodrive"),
        "opera" to listOf("Opera", "opera")
    )

    private val CORE_OPTION_SCHEMAS: Map<String, List<CoreOptionSpec>> = mapOf(
        "pcsx_rearmed" to listOf(
            CoreOptionSpec("pcsx_rearmed_neon_enhancement_enable", "Удвоение разрешения (2x Hi-Res)", "Enhanced Resolution (2x Hi-Res)", listOf("disabled", "enabled"), "enabled"),
            CoreOptionSpec("pcsx_rearmed_neon_enhancement_no_main", "Быстрый буфер Hi-Res (Speed Hack)", "Enhanced Resolution Speed Hack", listOf("disabled", "enabled"), "enabled"),
            CoreOptionSpec("pcsx_rearmed_frameskip_type", "Пропуск кадров (Frameskip)", "Frameskip Mode", listOf("disabled", "auto", "fixed"), "disabled"),
            CoreOptionSpec("pcsx_rearmed_psxclock", "Частота CPU PS1 (%)", "PSX CPU Clock (%)", listOf("50", "57", "65", "75", "85", "100"), "57"),
            CoreOptionSpec("pcsx_rearmed_cd_turbo", "Быстрая загрузка CD-ROM", "CD-ROM Async Fast Load", listOf("disabled", "enabled"), "enabled"),
            CoreOptionSpec("pcsx_rearmed_dithering", "Дизеринг цвета PS1", "PSX Color Dithering", listOf("enabled", "disabled"), "enabled"),
            CoreOptionSpec("pcsx_rearmed_pad1type", "Тип геймпада Порт 1", "Pad 1 Controller Type", listOf("standard", "analog", "dualshock"), "dualshock")
        ),
        "swanstation" to listOf(
            CoreOptionSpec("swanstation_GPU_ResolutionScale", "Внутреннее разрешение (1x-8x)", "Internal Resolution Scale (1x-8x)", listOf("1", "2", "3", "4", "5", "6", "8"), "2"),
            CoreOptionSpec("swanstation_GPU_PGXPEnable", "Коррекция геометрии PGXP", "PGXP Geometry Correction", listOf("false", "true"), "true"),
            CoreOptionSpec("swanstation_GPU_PGXPTextureCorrection", "Перспективная коррекция текстур PGXP", "PGXP Perspective Texture Correction", listOf("false", "true"), "true"),
            CoreOptionSpec("swanstation_GPU_WidescreenHack", "Широкоэкранный режим 16:9", "Widescreen 16:9 Hack", listOf("false", "true"), "false"),
            CoreOptionSpec("swanstation_CDROM_LoadImagePatches", "Быстрое чтение CD (2x-10x)", "Fast CD-ROM Speedup", listOf("false", "true"), "true"),
            CoreOptionSpec("swanstation_CPU_Overclock", "Разгон процессора PS1 (%)", "PSX CPU Overclock (%)", listOf("100", "125", "150", "200"), "100"),
            CoreOptionSpec("swanstation_BIOS_PatchFastBoot", "Быстрый старт без заставки BIOS", "Fast Boot (Skip BIOS Intro)", listOf("false", "true"), "false")
        ),
        "mupen64plus_next_gles3" to listOf(
            CoreOptionSpec("mupen64plus-43screensize", "Внутреннее разрешение 4:3", "4:3 Internal Resolution", listOf("320x240", "640x480", "960x720", "1280x960", "1600x1200", "1920x1440"), "960x720"),
            CoreOptionSpec("mupen64plus-169screensize", "Внутреннее разрешение 16:9", "16:9 Internal Resolution", listOf("640x360", "960x540", "1280x720", "1920x1080", "2560x1440"), "1280x720"),
            CoreOptionSpec("mupen64plus-aspect", "Соотношение сторон", "Aspect Ratio", listOf("4:3", "16:9", "16:9 adjusted"), "4:3"),
            CoreOptionSpec("mupen64plus-FrameDuping", "Дублирование кадров (плавность)", "Frame Duping", listOf("False", "True"), "True"),
            CoreOptionSpec("mupen64plus-CountPerOp", "Циклы CPU на операцию (Overclock)", "Count Per Op (1=Overclock, 2=Default)", listOf("0", "1", "2", "3"), "0"),
            CoreOptionSpec("mupen64plus-BilinearMode", "Фильтрация текстур N64", "Texture Bilinear Mode", listOf("3point", "standard"), "3point")
        ),
        "ppsspp" to listOf(
            CoreOptionSpec("ppsspp_internal_resolution", "Внутреннее разрешение PSP (1x-8x)", "Internal Resolution (1x-8x)", listOf("480x272", "960x544", "1440x816", "1920x1088", "2400x1360", "3840x2176"), "960x544"),
            CoreOptionSpec("ppsspp_auto_frameskip", "Автоматический пропуск кадров", "Auto Frameskip", listOf("disabled", "enabled"), "disabled"),
            CoreOptionSpec("ppsspp_frameskip", "Пропуск кадров (0-4)", "Frameskip Level", listOf("0", "1", "2", "3", "4"), "0"),
            CoreOptionSpec("ppsspp_locked_cpu_speed", "Частота CPU PSP (МГц)", "Locked PSP CPU Speed", listOf("off", "222", "266", "333", "444", "666"), "off"),
            CoreOptionSpec("ppsspp_texture_anisotropic_filtering", "Анизотропная фильтрация", "Anisotropic Filtering", listOf("off", "2x", "4x", "8x", "16x"), "8x"),
            CoreOptionSpec("ppsspp_fast_memory", "Быстрая память (Fast Memory)", "Fast Memory (Unstable check off)", listOf("enabled", "disabled"), "enabled")
        ),
        "flycast" to listOf(
            CoreOptionSpec("reicast_internal_resolution", "Внутреннее разрешение Dreamcast", "Internal Resolution", listOf("640x480", "960x720", "1280x960", "1440x1080", "1920x1440", "2560x1920"), "960x720"),
            CoreOptionSpec("reicast_widescreen_hack", "Широкоэкранный хак 16:9", "Widescreen Hack", listOf("disabled", "enabled"), "disabled"),
            CoreOptionSpec("reicast_anisotropic_filtering", "Анизотропная фильтрация", "Anisotropic Filtering", listOf("off", "2", "4", "8", "16"), "4"),
            CoreOptionSpec("reicast_threaded_rendering", "Многопоточный рендеринг GPU", "Threaded Rendering", listOf("enabled", "disabled"), "enabled"),
            CoreOptionSpec("reicast_alpha_sorting", "Сортировка прозрачности", "Alpha Sorting", listOf("per-strip (fast, least accurate)", "per-triangle (normal)", "per-pixel (accurate)"), "per-triangle (normal)")
        ),
        "mgba" to listOf(
            CoreOptionSpec("mgba_color_correction", "Цветокоррекция экрана GBA", "GBA LCD Color Correction", listOf("OFF", "GBA", "GBC", "Auto"), "GBA"),
            CoreOptionSpec("mgba_interframe_blending", "Смешивание полукадров (мерцание)", "Interframe Blending", listOf("OFF", "mix", "mix_smart", "lcd_ghosting"), "mix_smart"),
            CoreOptionSpec("mgba_skip_bios", "Пропуск заставки BIOS", "Skip BIOS Intro", listOf("ON", "OFF"), "ON"),
            CoreOptionSpec("mgba_frameskip", "Пропуск кадров", "Frameskip", listOf("disabled", "auto", "auto_threshold", "fixed_interval"), "disabled"),
            CoreOptionSpec("mgba_idle_optimization", "Оптимизация холостых циклов CPU", "Idle Loop Removal", listOf("Remove Known", "Detect and Remove", "Don't Remove"), "Remove Known")
        ),
        "snes9x" to listOf(
            CoreOptionSpec("snes9x_overclock_superfx", "Разгон чипа SuperFX (%)", "SuperFX Overclock (%)", listOf("100%", "150%", "200%", "300%", "400%"), "100%"),
            CoreOptionSpec("snes9x_overclock_cycles", "Снижение замедлений CPU SNES", "Reduce Slowdown (CPU Overclock)", listOf("disabled", "light", "compatible", "max"), "disabled"),
            CoreOptionSpec("snes9x_reduce_sprite_flicker", "Убрать мерцание спрайтов", "Reduce Sprite Flicker", listOf("disabled", "enabled"), "enabled"),
            CoreOptionSpec("snes9x_aspect", "Пропорции экрана SNES", "Preferred Aspect Ratio", listOf("4:3", "uncorrected", "auto", "ntsc", "pal"), "4:3"),
            CoreOptionSpec("snes9x_blargg", "NTSC видео-фильтр Blargg", "Blargg NTSC Filter", listOf("disabled", "monochrome", "rf", "composite", "s-video", "rgb"), "disabled")
        ),
        "genesis_plus_gx" to listOf(
            CoreOptionSpec("genesis_plus_gx_joypad_1_type", "Тип геймпада Mega Drive", "Port 1 Controller Type", listOf("auto", "3 button", "6 button"), "auto"),
            CoreOptionSpec("genesis_plus_gx_overclock", "Разгон CPU 68000 (%)", "CPU Speed (%)", listOf("100%", "125%", "150%", "200%"), "100%"),
            CoreOptionSpec("genesis_plus_gx_no_sprite_limit", "Снять лимит спрайтов на строку", "Remove Per-Line Sprite Limit", listOf("disabled", "enabled"), "enabled"),
            CoreOptionSpec("genesis_plus_gx_ym2612", "Точность синтезатора YM2612 FM", "YM2612 FM Core", listOf("mame (ym2612)", "mame (asic ym3438)", "nuked (ym2612)", "nuked (ym3438)"), "mame (ym2612)"),
            CoreOptionSpec("genesis_plus_gx_blargg_ntsc_filter", "NTSC композитный фильтр", "Blargg NTSC Filter", listOf("disabled", "monochrome", "composite", "svideo", "rgb"), "disabled")
        ),
        "dosbox_pure" to listOf(
            CoreOptionSpec("dosbox_pure_cycles", "Скорость процессора DOS", "Emulated CPU Performance", listOf("auto", "max", "8086", "286", "386", "486", "pentium", "pentium_ii"), "auto"),
            CoreOptionSpec("dosbox_pure_machine", "Видеоадаптер ПК", "Graphics Machine Type", listOf("svga", "vga", "ega", "cga", "hercules", "tandy"), "svga"),
            CoreOptionSpec("dosbox_pure_memory_size", "Объем оперативной памяти RAM", "DOS/Win9x RAM Size (MB)", listOf("16", "32", "64", "128", "256"), "64"),
            CoreOptionSpec("dosbox_pure_sblaster_type", "Звуковая карта Sound Blaster", "Sound Blaster Type", listOf("sb16", "sbpro2", "sb2", "sb1", "gb", "none"), "sb16"),
            CoreOptionSpec("dosbox_pure_aspect_correction", "Коррекция пропорций 4:3 CRT", "CRT 4:3 Aspect Correction", listOf("true", "false"), "true")
        ),
        "melonds" to listOf(
            CoreOptionSpec("melonds_screen_layout", "Расположение экранов NDS", "NDS Dual Screen Layout", listOf("Top/Bottom", "Bottom/Top", "Left/Right", "Right/Left", "Top Only", "Bottom Only", "Hybrid Top"), "Top/Bottom"),
            CoreOptionSpec("melonds_threaded_renderer", "Многопоточный 3D-рендеринг", "Threaded Software Renderer", listOf("enabled", "disabled"), "enabled"),
            CoreOptionSpec("melonds_jit_enable", "JIT-компилятор ARM CPU", "Enable JIT Recompiler", listOf("enabled", "disabled"), "enabled"),
            CoreOptionSpec("melonds_boot_directly", "Прямой запуск игры", "Boot Game Directly", listOf("enabled", "disabled"), "enabled")
        )
    )

    private val FALLBACK_FLAGSHIP_CORES: List<MobileCoreInfo> = listOf(
        buildCoreEntry("fceumm", "Nintendo NES / Famicom (FCEUmm)", "nes", listOf("nes", "fds", "unf", "unif", "zip"), listOf("disksys.rom"), true, true, true),
        buildCoreEntry("snes9x", "Super Nintendo / Super Famicom (Snes9x)", "snes", listOf("sfc", "smc", "fig", "swc", "bs", "zip"), emptyList(), true, true, false),
        buildCoreEntry("genesis_plus_gx", "Sega Genesis / Mega-CD / Master System (Genesis Plus GX)", "megadrive", listOf("md", "gen", "smd", "bin", "chd", "cue", "iso", "sms", "gg", "sg", "zip"), listOf("bios_CD_U.bin", "bios_CD_E.bin", "bios_CD_J.bin"), true, true, true),
        buildCoreEntry("picodrive", "Sega 32X / Mega Drive / Mega-CD (PicoDrive)", "sega32x", listOf("32x", "md", "gen", "smd", "bin", "cue", "iso", "chd", "sms", "zip"), listOf("bios_CD_U.bin", "bios_CD_E.bin", "bios_CD_J.bin"), true, true, true),
        buildCoreEntry("gambatte", "Nintendo Game Boy / Color (Gambatte)", "gbc", listOf("gb", "gbc", "dmg", "zip"), listOf("gb_bios.bin", "gbc_bios.bin"), true, true, true),
        buildCoreEntry("mgba", "Nintendo Game Boy Advance (mGBA)", "gba", listOf("gba", "gb", "gbc", "agb", "zip"), listOf("gba_bios.bin"), true, true, true),
        buildCoreEntry("melonds", "Nintendo DS (melonDS)", "nds", listOf("nds", "dsi", "ids", "zip"), listOf("bios7.bin", "bios9.bin", "firmware.bin"), true, true, false),
        buildCoreEntry("mupen64plus_next_gles3", "Nintendo 64 (Mupen64Plus-Next GLES3)", "n64", listOf("z64", "n64", "v64", "ndd", "zip"), emptyList(), true, true, false),
        buildCoreEntry("pcsx_rearmed", "Sony PlayStation (PCSX-ReARMed)", "psx", listOf("chd", "pbp", "cue", "bin", "img", "iso", "mdf", "m3u"), listOf("scph5501.bin", "scph5500.bin", "scph5502.bin", "scph1001.bin"), true, true, true),
        buildCoreEntry("swanstation", "Sony PlayStation HD PGXP (SwanStation)", "psx", listOf("chd", "pbp", "cue", "bin", "img", "iso", "ecm", "mds", "m3u"), listOf("scph5501.bin", "scph5500.bin", "scph5502.bin", "scph1001.bin"), true, true, false),
        buildCoreEntry("ppsspp", "Sony PlayStation Portable (PPSSPP)", "psp", listOf("iso", "cso", "pbp", "elf", "prx"), emptyList(), true, true, false),
        buildCoreEntry("flycast", "Sega Dreamcast / NAOMI / Atomiswave (Flycast)", "dreamcast", listOf("chd", "cdi", "gdi", "cue", "zip", "lst", "dat", "m3u"), listOf("dc/dc_boot.bin", "dc/dc_flash.bin"), true, true, false),
        buildCoreEntry("fbneo", "Arcade / Neo Geo / CPS-1/2/3 (FinalBurn Neo)", "arcade", listOf("zip", "7z"), listOf("neogeo.zip", "pgm.zip"), true, true, false),
        buildCoreEntry("dosbox_pure", "MS-DOS / Windows 9x (DOSBox-Pure)", "dos", listOf("zip", "dosz", "exe", "com", "bat", "iso", "cue", "img", "ima", "vhd", "conf", "m3u"), emptyList(), true, true, false),
        buildCoreEntry("mednafen_pce_fast", "NEC PC Engine / TurboGrafx-16 / CD (Beetle PCE Fast)", "pce", listOf("pce", "cue", "chd", "ccd", "toc", "sgx", "zip"), listOf("syscard3.pce"), true, true, true),
        buildCoreEntry("mednafen_ngp", "SNK Neo Geo Pocket / Color (Beetle NeoPop)", "ngp", listOf("ngp", "ngc", "ngpc", "npc", "zip"), emptyList(), true, true, true),
        buildCoreEntry("mednafen_wswan", "Bandai WonderSwan / Color (Beetle Cygne)", "wonderswan", listOf("ws", "wsc", "pc2", "zip"), emptyList(), true, true, true),
        buildCoreEntry("opera", "3DO Interactive Multiplayer (Opera)", "3do", listOf("iso", "chd", "cue", "bin", "m3u"), listOf("panafz10.bin", "panafz1.bin", "goldstar.bin"), true, true, false),
        buildCoreEntry("stella2014", "Atari 2600 (Stella 2014)", "atari2600", listOf("a26", "bin", "zip"), emptyList(), true, true, true),
        buildCoreEntry("prboom", "DOOM / DOOM II / Final DOOM (PrBoom)", "ports", listOf("wad", "iwad", "pwad", "zip"), listOf("prboom.wad"), true, true, false)
    )

    private fun buildCoreEntry(
        id: String,
        displayName: String,
        platformId: String,
        exts: List<String>,
        bios: List<String>,
        recommended: Boolean,
        pre64: Boolean,
        pre32: Boolean,
        zip64: String? = null,
        zip32: String? = null,
        soInside: String? = null,
        jniLib: String? = null
    ): MobileCoreInfo {
        val baseSo = "${id}_libretro"
        val resolvedSoInside = soInside?.takeIf { it.isNotBlank() } ?: "${baseSo}_android.so"
        val resolvedJniLib = jniLib?.takeIf { it.isNotBlank() } ?: "lib${baseSo}_android.so"
        val resolvedZip64 = zip64?.takeIf { it.isNotBlank() } ?: "${resolvedSoInside}.zip"
        val resolvedZip32 = zip32?.takeIf { it.isNotBlank() } ?: resolvedZip64
        val derivedBase = resolvedSoInside.removeSuffix("_android.so").removeSuffix(".so").removePrefix("lib")
        val pKeys = expandPlatformKeysForCore(id, platformId)
        val normPrimary = normalizePlatformKey(platformId, "")
        val pDisplay = getPlatformDisplayName(normPrimary.ifBlank { platformId })
        val biosHint = if (bios.isNotEmpty()) " BIOS: ${bios.take(2).joinToString(", ")}." else ""

        return MobileCoreInfo(
            id = id,
            displayName = displayName,
            soBaseName = derivedBase,
            platformDisplay = pDisplay,
            platformKeys = pKeys,
            extensions = exts.map { it.lowercase(Locale.ROOT).trimStart('.') }.filter { it.isNotEmpty() }.toSet(),
            descriptionRu = "Официальное ядро Libretro для $pDisplay.$biosHint",
            descriptionEn = "Official Libretro core for $pDisplay.$biosHint",
            isExperimental = id in setOf("play", "pcsx2", "citra", "citra2018", "dolphin"),
            platformId = platformId,
            zipArm64 = resolvedZip64,
            zipArm32 = resolvedZip32,
            soInsideZip = resolvedSoInside,
            jniLibName = resolvedJniLib,
            biosFiles = bios,
            recommended = recommended,
            prebundledArm64 = pre64,
            prebundledArm32 = pre32
        )
    }

    private fun expandPlatformKeysForCore(coreId: String, rawPlatformId: String): Set<String> {
        val set = LinkedHashSet<String>()
        val pid = rawPlatformId.trim().lowercase(Locale.ROOT)
        if (pid.isNotEmpty()) {
            set.add(pid)
            val norm = normalizePlatformKey(pid, "")
            if (norm.isNotEmpty()) set.add(norm)
        }
        val cid = coreId.lowercase(Locale.ROOT)
        when {
            cid == "pcsx2" || cid == "play" -> {
                set.clear()
                set.add("ps2")
            }
            cid.startsWith("genesis_plus_gx") -> {
                set.addAll(listOf("genesis", "megadrive", "segacd", "sms_gg", "mastersystem", "sg1000"))
            }
            cid == "picodrive" -> {
                set.addAll(listOf("genesis", "megadrive", "segacd", "sega32x", "sms_gg", "mastersystem"))
            }
            cid == "fceumm" || cid == "nestopia" || cid == "mesen" -> {
                set.addAll(listOf("nes", "fds"))
            }
            cid in setOf("gambatte", "sameboy", "tgbdual", "gearboy") -> {
                set.addAll(listOf("gb", "gbc", "gb_gbc"))
            }
            cid in setOf("mgba", "vbam", "vba_next") -> {
                set.addAll(listOf("gba", "gb", "gbc", "gb_gbc"))
            }
            cid in setOf("pcsx_rearmed", "swanstation", "mednafen_psx", "mednafen_psx_hw") -> {
                set.addAll(listOf("ps1", "psx"))
            }
            cid == "dolphin" -> {
                set.addAll(listOf("gc_wii", "gamecube_wii", "gamecube", "wii"))
            }
            cid == "flycast" -> {
                set.addAll(listOf("dreamcast", "naomi", "arcade"))
            }
            cid == "fbneo" || cid.startsWith("mame") || cid == "hbmame" -> {
                set.addAll(listOf("arcade", "neogeo", "cps"))
            }
            cid.startsWith("mednafen_pce") -> {
                set.addAll(listOf("pce", "pcecd"))
            }
            cid == "neocd" -> {
                set.addAll(listOf("neocd", "neogeocd"))
            }
            cid == "prboom" -> {
                set.addAll(listOf("doom", "ports"))
            }
            pid == "gbc" -> {
                set.addAll(listOf("gb", "gbc", "gb_gbc"))
            }
            pid == "psx" -> {
                set.add("ps1")
            }
            pid == "megadrive" -> {
                set.add("genesis")
            }
            pid == "mastersystem" -> {
                set.add("sms_gg")
            }
            pid == "colecovision" -> {
                set.add("coleco")
            }
            pid == "virtualboy" -> {
                set.add("vb")
            }
            pid == "amstradcpc" -> {
                set.add("amstrad")
            }
            pid == "bk" -> {
                set.add("bk0010")
            }
            pid == "fantasy" -> {
                set.addAll(listOf("pico8", "tic80"))
            }
        }
        return set
    }

    fun init(context: Context): List<MobileCoreInfo> {
        loadedCatalogCores?.let { return it }
        synchronized(catalogLock) {
            loadedCatalogCores?.let { return it }
            val parsed = ArrayList<MobileCoreInfo>(260)
            try {
                context.applicationContext.assets.open("cores_catalog_android.json").use { ins ->
                    val jsonStr = ins.bufferedReader(StandardCharsets.UTF_8).readText()
                    val root = JSONObject(jsonStr)
                    val arr = root.optJSONArray("cores") ?: JSONArray()
                    for (i in 0 until arr.length()) {
                        val obj = arr.optJSONObject(i) ?: continue
                        val id = obj.optString("id", "").trim()
                        if (id.isEmpty()) continue
                        val displayName = obj.optString("displayName", id).trim()
                        val platformId = obj.optString("platformId", "ports").trim()
                        val zip64 = obj.optString("zipArm64", "${id}_libretro_android.so.zip").trim()
                        val zip32 = obj.optString("zipArm32", zip64).trim()
                        val soInside = obj.optString("soInsideZip", "${id}_libretro_android.so").trim()
                        val jniLib = obj.optString("jniLibName", "lib${id}_libretro_android.so").trim()

                        val extList = ArrayList<String>()
                        val extArr = obj.optJSONArray("extensions")
                        if (extArr != null) {
                            for (j in 0 until extArr.length()) {
                                val e = extArr.optString(j, "").trim().trimStart('.').lowercase(Locale.ROOT)
                                if (e.isNotEmpty()) extList.add(e)
                            }
                        }

                        val biosList = ArrayList<String>()
                        val biosArr = obj.optJSONArray("biosFiles")
                        if (biosArr != null) {
                            for (j in 0 until biosArr.length()) {
                                val b = biosArr.optString(j, "").trim()
                                if (b.isNotEmpty()) biosList.add(b)
                            }
                        }

                        parsed.add(
                            buildCoreEntry(
                                id = id,
                                displayName = displayName,
                                platformId = platformId,
                                exts = extList,
                                bios = biosList,
                                recommended = obj.optBoolean("recommended", false),
                                pre64 = obj.optBoolean("prebundledArm64", false),
                                pre32 = obj.optBoolean("prebundledArm32", false),
                                zip64 = zip64,
                                zip32 = zip32,
                                soInside = soInside,
                                jniLib = jniLib
                            )
                        )
                    }
                }
            } catch (_: Throwable) {
            }
            val finalCatalog = if (parsed.isNotEmpty()) parsed else FALLBACK_FLAGSHIP_CORES
            loadedCatalogCores = finalCatalog
            return finalCatalog
        }
    }

    fun getAllCores(context: Context? = null): List<MobileCoreInfo> {
        if (context != null) return init(context)
        return ALL_CORES
    }

    fun getPlatformDisplayName(platformKey: String): String {
        val k = platformKey.lowercase(Locale.ROOT).trim()
        return PLATFORM_DISPLAY_NAMES[k]
            ?: PLATFORM_DISPLAY_NAMES[normalizePlatformKey(k, "")]
            ?: platformKey.uppercase(Locale.ROOT)
    }

    fun findCoreById(idOrBaseName: String?, context: Context? = null): MobileCoreInfo? {
        if (idOrBaseName.isNullOrBlank()) return null
        val catalog = getAllCores(context)
        val q = idOrBaseName.trim().lowercase(Locale.ROOT)
        if (q == "mupen64plus_next") {
            catalog.firstOrNull { it.id == "mupen64plus_next_gles3" }?.let { return it }
        }
        return catalog.firstOrNull {
            it.id.lowercase(Locale.ROOT) == q ||
                it.soBaseName.lowercase(Locale.ROOT) == q ||
                it.soInsideZip.lowercase(Locale.ROOT) == q ||
                it.jniLibName.lowercase(Locale.ROOT) == q ||
                it.displayName.lowercase(Locale.ROOT) == q
        }
    }

    fun normalizePlatformKey(platform: String?, ext: String = ""): String {
        val p = (platform ?: "").trim().lowercase(Locale.ROOT)
        val e = ext.trim().trimStart('.').lowercase(Locale.ROOT)

        if (p.isNotEmpty()) {
            if (p == "all" || p == "все платформы" || p == "all platforms") return ""

            when (p) {
                "psx", "ps1", "playstation" -> return "ps1"
                "megadrive", "genesis", "mega drive" -> return "genesis"
                "mastersystem", "sms", "game gear", "gg", "sms_gg" -> return "sms_gg"
                "gc_wii", "gamecube_wii" -> return "gamecube_wii"
                "neocd", "neogeocd" -> return "neogeocd"
                "colecovision", "coleco" -> return "coleco"
                "virtualboy", "vb" -> return "vb"
                "amstradcpc", "amstrad" -> return "amstrad"
                "bk", "bk0010" -> return "bk0010"
            }

            if (PLATFORM_DISPLAY_NAMES.containsKey(p)) return p

            if (p.contains("game boy advance") || p == "gba" || p.contains("(gba)")) return "gba"
            if (p.contains("virtual boy")) return "vb"
            if (p.contains("pokemon mini")) return "pokemini"
            if (p.contains("game & watch") || p.contains("game and watch") || p == "gw") return "gw"
            if (p.contains("game boy color") || p == "gbc") return "gbc"
            if (p.contains("game boy") || p == "gb" || p.contains("gb/gbc") || p.contains("gb_gbc")) return "gb_gbc"
            if (p.contains("3ds")) return "3ds"
            if (p.contains("nintendo ds") || p == "nds" || p == "dsi" || p.contains("ds / dsi")) return "nds"

            if (p.contains("super nintendo") || p.contains("super famicom") || p == "snes" || p == "sfc") return "snes"
            if (p.contains("famicom disk") || p == "fds") return "fds"
            if (p.contains("nintendo 64") || p == "n64") return "n64"
            if (p.contains("gamecube") && p.contains("wii")) return "gamecube_wii"
            if (p.contains("gamecube") || p == "gc" || p == "ngc") return "gamecube"
            if (p.contains("wii")) return "wii"
            if (p.contains("nintendo entertainment") || p.contains("nes") || p.contains("famicom") || p.contains("dendy")) return "nes"

            if (p.contains("playstation portable") || p == "psp") return "psp"
            if (p.contains("vita") || p == "psvita") return "psvita"
            if (p.contains("playstation 3") || p == "ps3") return "ps3"
            if (p.contains("playstation 2") || p == "ps2") return "ps2"
            if (p.contains("playstation") || p == "psone") return "ps1"

            if (p.contains("32x")) return "sega32x"
            if (p.contains("mega-cd") || p.contains("mega cd") || p.contains("sega cd") || p == "segacd" || p == "mcd") return "segacd"
            if (p.contains("saturn")) return "saturn"
            if (p.contains("dreamcast") || p == "dc") return "dreamcast"
            if (p.contains("naomi") || p.contains("atomiswave")) return "naomi"
            if (p.contains("master system") || p.contains("mark iii")) return "sms_gg"
            if (p.contains("sg-1000") || p == "sg1000") return "sg1000"
            if (p.contains("mega drive") || p.contains("genesis") || p == "md") return "genesis"

            if (p.contains("neo geo pocket") || p == "ngp" || p == "ngpc") return "ngp"
            if (p.contains("neo geo cd")) return "neogeocd"
            if (p.contains("neo geo") || p == "neogeo" || p == "mvs" || p == "aes") return "neogeo"
            if (p.contains("cps-1") || p.contains("cps-2") || p.contains("cps-3") || p.contains("cps1") || p.contains("cps2")) return "cps"
            if (p.contains("arcade") || p.contains("fbneo") || p.contains("mame")) return "arcade"

            if (p.contains("pc-fx") || p == "pcfx") return "pcfx"
            if (p.contains("pc engine cd") || p.contains("turbografx-cd") || p == "pcecd") return "pcecd"
            if (p.contains("pc engine") || p.contains("turbografx") || p == "pce" || p == "tg16") return "pce"
            if (p.contains("wonderswan") || p == "ws" || p == "wsc") return "wonderswan"
            if (p.contains("3do")) return "3do"
            if (p.contains("cd-i") || p == "cdi") return "cdi"
            if (p.contains("vectrex")) return "vectrex"
            if (p.contains("coleco")) return "coleco"
            if (p.contains("intellivision")) return "intellivision"
            if (p.contains("odyssey")) return "odyssey2"
            if (p.contains("channel f")) return "channelf"

            if (p.contains("2600")) return "atari2600"
            if (p.contains("5200")) return "atari5200"
            if (p.contains("7800")) return "atari7800"
            if (p.contains("lynx")) return "lynx"
            if (p.contains("jaguar")) return "jaguar"
            if (p.contains("atari st") || p == "atarist") return "atarist"
            if (p.contains("atari 8") || p.contains("atari800")) return "atari800"

            if (p.contains("dos") || p.contains("ms-dos")) return "dos"
            if (p.contains("scummvm")) return "scummvm"
            if (p.contains("amiga")) return "amiga"
            if (p.contains("commodore 64") || p == "c64") return "c64"
            if (p.contains("spectrum") || p == "zx") return "zxspectrum"
            if (p.contains("msx")) return "msx"
            if (p.contains("x68000")) return "x68000"
            if (p.contains("pc-98") || p == "pc98") return "pc98"
            if (p.contains("pc-88") || p == "pc88") return "pc88"
            if (p.contains("amstrad") || p == "cpc") return "amstrad"
            if (p.contains("бк-0010") || p.contains("bk-0010")) return "bk0010"
            if (p.contains("pico-8") || p == "pico8") return "pico8"
            if (p.contains("tic-80") || p == "tic80") return "tic80"
            if (p.contains("doom")) return "doom"
        }

        return when (e) {
            "nes", "unf", "unif" -> "nes"
            "fds" -> "fds"
            "sfc", "smc", "swc", "fig", "bs" -> "snes"
            "z64", "n64", "v64" -> "n64"
            "gcm", "gcz", "rvz", "wbfs" -> "gamecube_wii"
            "gb", "dmg" -> "gb"
            "gbc" -> "gbc"
            "gba", "agb" -> "gba"
            "nds", "dsi", "ids" -> "nds"
            "3ds", "3dsx", "cci", "cia", "cxi" -> "3ds"
            "vb", "vboy" -> "vb"
            "min" -> "pokemini"
            "mgw" -> "gw"
            "pbp", "cso" -> "psp"
            "ps2" -> "ps2"
            "md", "gen", "smd" -> "genesis"
            "32x" -> "sega32x"
            "sms", "gg" -> "sms_gg"
            "sg" -> "sg1000"
            "cdi", "gdi" -> "dreamcast"
            "pce", "sgx" -> "pce"
            "pcfx" -> "pcfx"
            "ngp", "ngc", "ngpc", "npc" -> "ngp"
            "ws", "wsc", "pc2" -> "wonderswan"
            "vec" -> "vectrex"
            "col", "cv" -> "coleco"
            "int" -> "intellivision"
            "a26" -> "atari2600"
            "a52" -> "atari5200"
            "a78" -> "atari7800"
            "lnx", "lyx" -> "lynx"
            "j64", "jag" -> "jaguar"
            "st", "stx", "msa" -> "atarist"
            "atr", "xex" -> "atari800"
            "dosz", "exe", "com", "bat", "conf" -> "dos"
            "scummvm" -> "scummvm"
            "adf", "adz", "dms", "ipf", "lha", "hdf" -> "amiga"
            "d64", "d71", "d81", "t64", "prg", "crt" -> "c64"
            "tzx", "tap", "z80", "sna", "scl", "trd" -> "zxspectrum"
            "mx1", "mx2" -> "msx"
            "dim", "xdf" -> "x68000"
            "d88", "fdi", "hdi", "nhd", "t98" -> "pc98"
            "bkd" -> "bk0010"
            "p8" -> "pico8"
            "tic" -> "tic80"
            "wad", "iwad", "pwad" -> "doom"
            "zip", "7z" -> "arcade"
            "chd", "cue", "iso", "m3u", "bin", "img" -> "ps1"
            else -> ""
        }
    }

    fun getCompatibleCoresForPlatform(
        platform: String?,
        ext: String = "",
        context: Context? = null
    ): List<MobileCoreInfo> {
        val catalog = getAllCores(context)
        val pKey = normalizePlatformKey(platform, ext)
        val rawLower = (platform ?: "").trim().lowercase(Locale.ROOT)
        val cleanExt = ext.trim().trimStart('.').lowercase(Locale.ROOT)

        val candidates = if (pKey.isNotEmpty() || rawLower.isNotEmpty()) {
            val byPlat = catalog.filter { core ->
                (pKey.isNotEmpty() && core.platformKeys.contains(pKey)) ||
                    (rawLower.isNotEmpty() && (core.platformId.lowercase(Locale.ROOT) == rawLower || core.platformKeys.contains(rawLower)))
            }
            if (byPlat.isNotEmpty()) byPlat
            else if (cleanExt.isNotEmpty()) catalog.filter { it.extensions.contains(cleanExt) }
            else catalog
        } else if (cleanExt.isNotEmpty()) {
            catalog.filter { it.extensions.contains(cleanExt) }.ifEmpty { catalog }
        } else {
            catalog
        }

        val prefOrder = PREFERRED_CORE_ORDER[pKey] ?: emptyList()
        return candidates.sortedWith(
            compareBy<MobileCoreInfo> { core ->
                val idx = prefOrder.indexOf(core.id)
                if (idx >= 0) idx else 500
            }
                .thenByDescending { core -> if (context != null) isCoreInstalled(context, core) else core.prebundledArm64 }
                .thenByDescending { it.recommended }
                .thenBy { it.displayName }
        )
    }

    fun getDefaultCoreForPlatform(
        platform: String?,
        ext: String = "",
        context: Context? = null
    ): MobileCoreInfo? {
        val compat = getCompatibleCoresForPlatform(platform, ext, context)
        if (compat.isEmpty()) return findCoreById("snes9x", context)
        if (context != null) {
            compat.firstOrNull { isCoreInstalled(context, it) && it.recommended }?.let { return it }
            compat.firstOrNull { isCoreInstalled(context, it) }?.let { return it }
        }
        return compat.firstOrNull { it.recommended } ?: compat.first()
    }

    fun getAllUniqueExtensions(context: Context? = null): Set<String> {
        val out = LinkedHashSet<String>()
        for (c in getAllCores(context)) {
            out.addAll(c.extensions)
        }
        out.addAll(listOf("m3u", "cue", "gdi", "chd", "iso", "zip", "7z"))
        return out
    }

    // === 3-Tier Core .so Resolution (Downloaded -> Pre-Bundled nativeLibraryDir -> Public Mirror) ===

    fun getExecutableCoresDir(context: Context): File {
        val dir = File(context.applicationContext.filesDir, "cores")
        if (!dir.exists()) dir.mkdirs()
        return dir
    }

    private val CORE_STEM_REGEX = Regex(
        "^(?:lib)?([a-z0-9_+.-]+?)_libretro(?:_android)?(?:\\.so)?(?:\\..+)?$",
        RegexOption.IGNORE_CASE
    )

    @Volatile
    private var cachedPublicDropMap: Map<String, File> = emptyMap()

    @Volatile
    private var lastPublicScanMs: Long = 0L

    fun getPublicCoresDir(): File {
        val dir = File(STORM_PUBLIC_ROOT, "cores")
        try {
            if (!dir.exists()) dir.mkdirs()
        } catch (_: Throwable) {
        }
        return dir
    }

    fun getPublicCoreDropDirs(): List<File> {
        val rawDirs = listOf(
            File(STORM_PUBLIC_ROOT, "cores"),
            File(STORM_PUBLIC_ROOT, "lib"),
            File(STORM_PUBLIC_ROOT, "LIB")
        )
        val out = ArrayList<File>(rawDirs.size)
        val seen = HashSet<String>(rawDirs.size)
        for (dir in rawDirs) {
            try {
                if (dir.name != "LIB" && !dir.exists()) {
                    dir.mkdirs()
                }
            } catch (_: Throwable) {
            }
            val key = try { dir.canonicalPath } catch (_: Throwable) { dir.absolutePath }
            if (seen.add(key)) {
                out.add(dir)
            }
        }
        return out
    }

    fun extractCoreIdStem(fileName: String): String {
        val base = File(fileName).name.trim().lowercase(Locale.ROOT)
        CORE_STEM_REGEX.matchEntire(base)?.let { m ->
            return m.groupValues[1]
        }
        return base
            .removePrefix("lib")
            .replace(Regex("\\.lcbk$", RegexOption.IGNORE_CASE), "")
            .replace(Regex("\\.\\d{8}T\\d{6}.*$", RegexOption.IGNORE_CASE), "")
            .removeSuffix(".zip")
            .removeSuffix(".gz")
            .removeSuffix("_android.so")
            .removeSuffix(".so")
            .removeSuffix("_libretro")
    }

    private fun isSupportedDropCoreFileName(name: String): Boolean {
        val low = name.lowercase(Locale.ROOT)
        return low.endsWith(".so") || low.endsWith(".so.zip") || low.endsWith(".lcbk") ||
            (low.endsWith(".zip") && low.contains("libretro"))
    }

    private fun dropFilePriority(name: String): Int {
        val low = name.lowercase(Locale.ROOT)
        return when {
            low.startsWith("lib") && low.endsWith(".so") -> 4
            low.endsWith(".so") -> 3
            low.endsWith(".zip") -> 2
            low.endsWith(".lcbk") -> 1
            else -> 0
        }
    }

    private fun scanPublicDropCoreFiles(forceRefresh: Boolean = false): Map<String, File> {
        val now = android.os.SystemClock.elapsedRealtime()
        if (!forceRefresh && now - lastPublicScanMs < 5000L && cachedPublicDropMap.isNotEmpty()) {
            return cachedPublicDropMap
        }
        val map = LinkedHashMap<String, File>()
        for (dir in getPublicCoreDropDirs()) {
            if (!dir.exists() || !dir.canRead()) continue
            val files = dir.listFiles { f -> f.isFile && isSupportedDropCoreFileName(f.name) } ?: continue
            for (f in files) {
                if (f.length() < 16384L) continue
                val stem = extractCoreIdStem(f.name)
                if (stem.isBlank()) continue
                val prev = map[stem]
                if (prev == null || dropFilePriority(f.name) > dropFilePriority(prev.name)) {
                    map[stem] = f
                }
            }
        }
        cachedPublicDropMap = map
        lastPublicScanMs = now
        return map
    }

    fun extractCorePayloadToElf(srcFile: File, dstTempFile: File): Pair<Boolean, String?> {
        if (!srcFile.exists() || !srcFile.isFile || srcFile.length() < 64L) return false to null
        dstTempFile.parentFile?.mkdirs()

        val head = ByteArray(16)
        val headLen = try {
            FileInputStream(srcFile).use { it.read(head) }
        } catch (_: Throwable) {
            return false to null
        }
        if (headLen < 4) return false to null

        if (head[0] == 0x7F.toByte() && head[1] == 'E'.code.toByte() &&
            head[2] == 'L'.code.toByte() && head[3] == 'F'.code.toByte()
        ) {
            return try {
                FileInputStream(srcFile).use { inp ->
                    FileOutputStream(dstTempFile).use { out ->
                        inp.copyTo(out, 16 * 1024)
                        out.flush()
                    }
                }
                val ok = hasValidElfMagic(dstTempFile)
                ok to if (ok) srcFile.name else null
            } catch (_: Throwable) {
                false to null
            }
        }

        if (head[0] == 'P'.code.toByte() && head[1] == 'K'.code.toByte()) {
            try {
                ZipInputStream(BufferedInputStream(FileInputStream(srcFile), 32 * 1024)).use { zis ->
                    var entry = zis.nextEntry
                    while (entry != null) {
                        if (!entry.isDirectory && (entry.name.endsWith(".so", ignoreCase = true) || entry.size > MIN_VALID_SO_BYTES)) {
                            val innerName = File(entry.name).name
                            FileOutputStream(dstTempFile).use { out ->
                                zis.copyTo(out, 32 * 1024)
                                out.flush()
                            }
                            if (hasValidElfMagic(dstTempFile)) {
                                zis.closeEntry()
                                return true to innerName
                            }
                        }
                        zis.closeEntry()
                        entry = zis.nextEntry
                    }
                }
            } catch (_: Throwable) {
            }
        }

        if (head[0] == 0x1F.toByte() && head[1] == 0x8B.toByte()) {
            try {
                GZIPInputStream(BufferedInputStream(FileInputStream(srcFile), 32 * 1024)).use { gzis ->
                    FileOutputStream(dstTempFile).use { out ->
                        gzis.copyTo(out, 32 * 1024)
                        out.flush()
                    }
                }
                if (hasValidElfMagic(dstTempFile)) {
                    return true to null
                }
            } catch (_: Throwable) {
            }
        }

        val skipOffsets = intArrayOf(0, 4, 8, 12)
        for (off in skipOffsets) {
            if (off + 2 <= headLen && head[off] == 0x78.toByte()) {
                for (nowrap in booleanArrayOf(false, true)) {
                    try {
                        val inflater = Inflater(nowrap)
                        FileInputStream(srcFile).use { fis ->
                            if (off > 0) {
                                val skipped = ByteArray(off)
                                fis.read(skipped)
                            }
                            InflaterInputStream(BufferedInputStream(fis, 32 * 1024), inflater).use { iis ->
                                FileOutputStream(dstTempFile).use { out ->
                                    iis.copyTo(out, 32 * 1024)
                                    out.flush()
                                }
                            }
                        }
                        if (hasValidElfMagic(dstTempFile)) {
                            return true to null
                        }
                    } catch (_: Throwable) {
                    }
                }
            }
        }

        try { dstTempFile.delete() } catch (_: Throwable) {}
        return false to null
    }

    /**
     * Mandatory for Android 14/15/16 Dynamic Code Loading (DCL) security policy:
     * any dynamically loaded .so inside context.filesDir/cores/ MUST be read-only (non-writable)
     * and executable!
     */
    fun enforceReadOnlyExecutableDclPermissions(soFile: File) {
        try {
            soFile.setReadable(true, false)
            soFile.setExecutable(true, false)
            soFile.setWritable(false, false)
        } catch (_: Throwable) {
            try {
                soFile.setReadable(true)
                soFile.setExecutable(true)
                soFile.setWritable(false)
            } catch (_: Throwable) {
            }
        }
    }

    fun hasValidElfMagic(file: File): Boolean {
        if (!file.exists() || !file.isFile || file.length() <= MIN_VALID_SO_BYTES) return false
        return try {
            FileInputStream(file).use { fis ->
                val magic = ByteArray(4)
                val read = fis.read(magic)
                read == 4 &&
                    magic[0] == 0x7F.toByte() &&
                    magic[1] == 'E'.code.toByte() &&
                    magic[2] == 'L'.code.toByte() &&
                    magic[3] == 'F'.code.toByte()
            }
        } catch (_: Throwable) {
            false
        }
    }

    fun resolveBundledCoreFile(context: Context, core: MobileCoreInfo): File? {
        val nativeDirPath = context.applicationInfo?.nativeLibraryDir ?: return null
        val nativeDir = File(nativeDirPath)
        if (!nativeDir.exists()) return null

        val candidates = listOf(
            core.jniLibName,
            "lib${core.soInsideZip.removePrefix("lib")}",
            "lib${core.soBaseName}_android.so",
            "lib${core.id}_libretro_android.so"
        ).distinct()

        for (name in candidates) {
            val f = File(nativeDir, name)
            if (f.exists() && f.length() > MIN_VALID_SO_BYTES) {
                return f
            }
        }
        return null
    }

    fun resolveDownloadedCoreFile(context: Context, core: MobileCoreInfo): File? {
        val execDir = getExecutableCoresDir(context)
        val candidates = listOf(
            core.jniLibName,
            core.soInsideZip,
            "${core.soBaseName}_android.so",
            "lib${core.soBaseName}_android.so",
            "${core.soBaseName}.so"
        ).distinct()

        for (name in candidates) {
            val f = File(execDir, name)
            if (f.exists() && f.length() > MIN_VALID_SO_BYTES && hasValidElfMagic(f)) {
                enforceReadOnlyExecutableDclPermissions(f)
                return f
            }
        }
        return null
    }

    fun resolveInstalledCoreFile(context: Context, core: MobileCoreInfo): File? {
        // Tier 1: User-updated or newly downloaded .so in internal filesDir/cores/
        resolveDownloadedCoreFile(context, core)?.let { return it }

        // Tier 2: Pre-bundled APK jniLibs extracted to nativeLibraryDir (0.0s instant offline load!)
        resolveBundledCoreFile(context, core)?.let { return it }

        // Tier 3: Public /storage/emulated/0/STORM RETROARCH/{cores,lib}/ drop-in folders (.so, lib*.so, .zip, .lcbk)
        val execDir = getExecutableCoresDir(context)
        val candidates = listOf(core.jniLibName, core.soInsideZip, "${core.soBaseName}_android.so").distinct()
        for (pubDir in getPublicCoreDropDirs()) {
            if (!pubDir.exists() || !pubDir.canRead()) continue
            for (name in candidates) {
                val extFile = File(pubDir, name)
                if (extFile.exists() && extFile.length() > MIN_VALID_SO_BYTES && hasValidElfMagic(extFile)) {
                    return try {
                        val dst = File(execDir, core.jniLibName)
                        copyFileAtomicDcl(extFile, dst)
                        dst
                    } catch (_: Throwable) {
                        null
                    }
                }
            }
        }
        val dropMap = scanPublicDropCoreFiles(forceRefresh = false)
        val stemMatch = dropMap[core.id.lowercase(Locale.ROOT)]
            ?: dropMap[extractCoreIdStem(core.jniLibName)]
        if (stemMatch != null) {
            val tmp = File(execDir, "drop_extract_${core.id}_${System.currentTimeMillis()}.so.tmp")
            val (ok, _) = extractCorePayloadToElf(stemMatch, tmp)
            if (ok) {
                return try {
                    val dst = File(execDir, core.jniLibName)
                    replaceTargetWithReadOnlySo(tmp, dst)
                    dst
                } catch (_: Throwable) {
                    try { tmp.delete() } catch (_: Throwable) {}
                    null
                }
            } else {
                try { tmp.delete() } catch (_: Throwable) {}
            }
        }
        return null
    }

    fun resolveCoreSoPath(context: Context, coreId: String): String? {
        val core = findCoreById(coreId, context) ?: return null
        return resolveInstalledCoreFile(context, core)?.absolutePath
    }

    fun isCoreBundled(context: Context, core: MobileCoreInfo): Boolean {
        return resolveBundledCoreFile(context, core) != null
    }

    fun isCoreDownloaded(context: Context, core: MobileCoreInfo): Boolean {
        return resolveDownloadedCoreFile(context, core) != null
    }

    fun isCoreInstalled(context: Context, core: MobileCoreInfo): Boolean {
        return resolveInstalledCoreFile(context, core) != null
    }

    fun getInstalledCoreSizeFormatted(context: Context, core: MobileCoreInfo): String {
        val f = resolveInstalledCoreFile(context, core) ?: return ""
        val mb = f.length().toDouble() / (1024.0 * 1024.0)
        return String.format(Locale.US, "%.2f MB", mb)
    }

    fun syncPublicCoresToExecutableDir(context: Context): Int {
        val dropMap = scanPublicDropCoreFiles(forceRefresh = true)
        if (dropMap.isEmpty()) return 0
        val execDir = getExecutableCoresDir(context)
        val catalog = init(context)
        var imported = 0
        for ((stem, src) in dropMap) {
            val matched = catalog.firstOrNull {
                it.id.equals(stem, ignoreCase = true) ||
                    extractCoreIdStem(it.jniLibName) == stem
            }
            val targetName = matched?.jniLibName ?: run {
                val base = File(src.name).name
                if (base.lowercase(Locale.ROOT).endsWith(".so")) {
                    if (base.startsWith("lib", ignoreCase = true)) "lib" + base.substring(3) else "lib$base"
                } else {
                    "lib${stem}_libretro_android.so"
                }
            }
            val dst = File(execDir, targetName)
            val isDirectSo = src.name.endsWith(".so", ignoreCase = true) && hasValidElfMagic(src)
            if (isDirectSo) {
                if (!dst.exists() || dst.length() != src.length()) {
                    try {
                        copyFileAtomicDcl(src, dst)
                        imported++
                    } catch (_: Throwable) {
                    }
                } else {
                    enforceReadOnlyExecutableDclPermissions(dst)
                }
            } else if (!dst.exists() || !hasValidElfMagic(dst)) {
                val tmp = File(execDir, "sync_${stem}_${System.currentTimeMillis()}.so.tmp")
                val (ok, _) = extractCorePayloadToElf(src, tmp)
                if (ok) {
                    try {
                        replaceTargetWithReadOnlySo(tmp, dst)
                        imported++
                    } catch (_: Throwable) {
                        try { tmp.delete() } catch (_: Throwable) {}
                    }
                } else {
                    try { tmp.delete() } catch (_: Throwable) {}
                }
            } else {
                enforceReadOnlyExecutableDclPermissions(dst)
            }
        }
        return imported
    }

    private fun copyFileAtomicDcl(src: File, dst: File) {
        dst.parentFile?.mkdirs()
        val tmp = File(dst.parentFile, "${dst.name}.tmp")
        if (tmp.exists()) {
            try { tmp.setWritable(true, true); tmp.delete() } catch (_: Throwable) {}
        }
        FileInputStream(src).use { input ->
            FileOutputStream(tmp).use { output ->
                val buf = ByteArray(16 * 1024)
                while (true) {
                    val r = input.read(buf)
                    if (r <= 0) break
                    output.write(buf, 0, r)
                }
                output.flush()
            }
        }
        replaceTargetWithReadOnlySo(tmp, dst)
    }

    private fun replaceTargetWithReadOnlySo(tempSo: File, targetSo: File) {
        if (targetSo.exists()) {
            try {
                targetSo.setWritable(true, true)
                targetSo.delete()
            } catch (_: Throwable) {
            }
        }
        if (!tempSo.renameTo(targetSo)) {
            FileInputStream(tempSo).use { inp ->
                FileOutputStream(targetSo).use { out ->
                    inp.copyTo(out, 16 * 1024)
                    out.flush()
                }
            }
            tempSo.delete()
        }
        enforceReadOnlyExecutableDclPermissions(targetSo)
    }

    // === Multi-Mirror Resilient Downloader (Defeats Cloudflare TSPU 16KB Throttling) ===

    fun detectDeviceAbi(): String {
        val bit64 = Build.SUPPORTED_64_BIT_ABIS ?: emptyArray()
        if (bit64.contains("arm64-v8a")) return "arm64-v8a"
        val bit32 = Build.SUPPORTED_32_BIT_ABIS ?: emptyArray()
        if (bit32.contains("armeabi-v7a")) return "armeabi-v7a"
        val all = Build.SUPPORTED_ABIS ?: emptyArray()
        for (abi in all) {
            if (abi == "arm64-v8a" || abi == "armeabi-v7a" || abi == "x86_64" || abi == "x86") {
                return abi
            }
        }
        return "arm64-v8a"
    }

    fun openFollowingRedirects(
        urlStr: String,
        connectTimeoutMs: Int = 15000,
        readTimeoutMs: Int = 30000
    ): HttpURLConnection {
        var currentUrl = urlStr
        var redirects = 0
        while (redirects < 6) {
            val conn = (URL(currentUrl).openConnection() as HttpURLConnection).apply {
                instanceFollowRedirects = false
                connectTimeout = connectTimeoutMs
                readTimeout = readTimeoutMs
                useCaches = false
                setRequestProperty("User-Agent", USER_AGENT)
                setRequestProperty("Accept", "*/*")
                setRequestProperty("Accept-Encoding", "identity")
                setRequestProperty("Connection", "close")
            }
            conn.connect()
            val code = conn.responseCode
            if (code in listOf(301, 302, 303, 307, 308)) {
                val loc = conn.getHeaderField("Location")
                conn.disconnect()
                if (loc.isNullOrBlank()) throw IllegalStateException("Redirect without Location")
                currentUrl = URL(URL(currentUrl), loc).toString()
                redirects++
                continue
            }
            return conn
        }
        throw IllegalStateException("Too many redirects for $urlStr")
    }

    fun downloadCoreBlocking(
        context: Context,
        core: MobileCoreInfo,
        onProgress: (Int, String) -> Unit
    ): Pair<File?, String> {
        val appCtx = context.applicationContext
        val abi = detectDeviceAbi()
        val zipName = if (abi == "arm64-v8a") {
            core.zipArm64.ifBlank { "${core.soBaseName}_android.so.zip" }
        } else {
            core.zipArm32.ifBlank { core.zipArm64.ifBlank { "${core.soBaseName}_android.so.zip" } }
        }

        val encodedZip = Uri.encode(zipName)
        data class MirrorTarget(
            val label: String,
            val url: String,
            val connectMs: Int,
            val readMs: Int,
            val retries: Int
        )

        val mirrors = listOf(
            MirrorTarget(
                label = "STORM Direct Mirror",
                url = "$STORM_MIRROR_CORES_BASE/$abi/$encodedZip",
                connectMs = 6000,
                readMs = 25000,
                retries = 1
            ),
            MirrorTarget(
                label = "Libretro Buildbot",
                url = "$BUILDBOT_ANDROID_BASE/$abi/$encodedZip",
                connectMs = 15000,
                readMs = 30000,
                retries = 2
            )
        )

        val coresDir = getExecutableCoresDir(appCtx)
        val tempDownload = File(coresDir, "${core.id}_${abi}.download.tmp")
        val tempExtractedSo = File(coresDir, "${core.jniLibName}.extract.tmp")
        val targetSo = File(coresDir, core.jniLibName)
        var lastError = "Core archive unavailable"

        for (mirror in mirrors) {
            for (attempt in 1..mirror.retries) {
                var conn: HttpURLConnection? = null
                try {
                    if (tempDownload.exists()) tempDownload.delete()
                    if (tempExtractedSo.exists()) tempExtractedSo.delete()

                    postProgress(onProgress, 3, mirror.label)
                    conn = openFollowingRedirects(mirror.url, mirror.connectMs, mirror.readMs)
                    val code = conn.responseCode
                    if (code !in 200..299) {
                        lastError = "${mirror.label}: HTTP $code"
                        conn.disconnect()
                        break
                    }

                    val totalBytes = conn.contentLengthLong
                    BufferedInputStream(conn.inputStream, 16 * 1024).use { input ->
                        FileOutputStream(tempDownload).use { output ->
                            val buf = ByteArray(16 * 1024)
                            var downloaded = 0L
                            var lastPct = 3
                            while (true) {
                                val r = input.read(buf)
                                if (r <= 0) break
                                output.write(buf, 0, r)
                                downloaded += r
                                if (totalBytes > 0L) {
                                    val pct = (5 + ((downloaded * 83L) / totalBytes).toInt()).coerceIn(5, 88)
                                    if (pct >= lastPct + 3) {
                                        lastPct = pct
                                        postProgress(onProgress, pct, mirror.label)
                                    }
                                }
                            }
                            output.flush()
                        }
                    }

                    if (!tempDownload.exists() || tempDownload.length() < 16384L) {
                        lastError = "${mirror.label}: truncated stream (${tempDownload.length()} B)"
                        continue
                    }

                    postProgress(onProgress, 92, mirror.label)
                    val extractedOk = extractOrCopySoPayload(tempDownload, tempExtractedSo)
                    tempDownload.delete()

                    if (!extractedOk || !hasValidElfMagic(tempExtractedSo)) {
                        tempExtractedSo.delete()
                        lastError = "${mirror.label}: invalid ELF binary"
                        continue
                    }

                    replaceTargetWithReadOnlySo(tempExtractedSo, targetSo)

                    // Mirror optional backup copy into public folder if accessible
                    try {
                        val pubCopy = File(getPublicCoresDir(), core.jniLibName)
                        FileInputStream(targetSo).use { inp ->
                            FileOutputStream(pubCopy).use { out -> inp.copyTo(out, 16 * 1024) }
                        }
                    } catch (_: Throwable) {
                    }

                    postProgress(onProgress, 100, mirror.label)
                    return targetSo to mirror.label
                } catch (e: Throwable) {
                    lastError = "${mirror.label}: ${e.message ?: e.javaClass.simpleName}"
                } finally {
                    try { conn?.disconnect() } catch (_: Throwable) {}
                }
            }
        }

        tempDownload.delete()
        tempExtractedSo.delete()
        return null to lastError
    }

    fun downloadCoreAsync(
        context: Context,
        core: MobileCoreInfo,
        onProgress: (Int, String) -> Unit,
        onDone: (Boolean, File?, String) -> Unit
    ) {
        val appCtx = context.applicationContext
        downloadExecutor.execute {
            val (soFile, msg) = downloadCoreBlocking(appCtx, core, onProgress)
            if (soFile != null && soFile.exists()) {
                postDone(onDone, true, soFile, msg)
            } else {
                postDone(onDone, false, null, msg)
            }
        }
    }

    private fun extractOrCopySoPayload(archiveOrSo: File, outSoFile: File): Boolean {
        if (hasValidElfMagic(archiveOrSo)) {
            FileInputStream(archiveOrSo).use { inp ->
                FileOutputStream(outSoFile).use { out ->
                    inp.copyTo(out, 16 * 1024)
                    out.flush()
                }
            }
            return outSoFile.exists() && outSoFile.length() > MIN_VALID_SO_BYTES
        }

        ZipInputStream(BufferedInputStream(FileInputStream(archiveOrSo), 16 * 1024)).use { zis ->
            var entry = zis.nextEntry
            while (entry != null) {
                if (!entry.isDirectory && entry.name.endsWith(".so", ignoreCase = true)) {
                    outSoFile.parentFile?.mkdirs()
                    FileOutputStream(outSoFile).use { out ->
                        val buf = ByteArray(16 * 1024)
                        while (true) {
                            val n = zis.read(buf)
                            if (n <= 0) break
                            out.write(buf, 0, n)
                        }
                        out.flush()
                    }
                    zis.closeEntry()
                    return outSoFile.exists() && outSoFile.length() > MIN_VALID_SO_BYTES
                }
                zis.closeEntry()
                entry = zis.nextEntry
            }
        }
        return false
    }

    // === Local Storage .so / .so.zip Core Importer ===

    fun importCoreFromUri(context: Context, uri: Uri): Pair<MobileCoreInfo?, String> {
        val appCtx = context.applicationContext
        val catalog = init(appCtx)
        val coresDir = getExecutableCoresDir(appCtx)
        val rawDisplayName = queryUriDisplayName(appCtx, uri) ?: uri.lastPathSegment ?: "imported_core.so"
        val tempInput = File(coresDir, "uri_import_${System.currentTimeMillis()}.tmp")
        val tempExtracted = File(coresDir, "uri_extracted_${System.currentTimeMillis()}.so.tmp")

        return try {
            val stream: InputStream = appCtx.contentResolver.openInputStream(uri)
                ?: return null to "Unable to open selected file URI"
            stream.use { inp ->
                FileOutputStream(tempInput).use { out ->
                    inp.copyTo(out, 16 * 1024)
                    out.flush()
                }
            }

            val (extractedOk, discoveredInnerName) = extractCorePayloadToElf(tempInput, tempExtracted)
            tempInput.delete()
            if (!extractedOk || !hasValidElfMagic(tempExtracted)) {
                tempExtracted.delete()
                return null to "No valid Android ELF .so Libretro core found in selected file (.so / .zip / .lcbk)"
            }

            val insideEntryName = discoveredInnerName?.takeIf { it.isNotBlank() } ?: rawDisplayName
            val cleanStem = extractCoreIdStem(insideEntryName).ifBlank { extractCoreIdStem(rawDisplayName) }

            val matchedCore = catalog.firstOrNull {
                it.id.lowercase(Locale.ROOT) == cleanStem ||
                    it.jniLibName.equals(insideEntryName, ignoreCase = true) ||
                    it.soInsideZip.equals(insideEntryName, ignoreCase = true)
            }

            val targetFileName = matchedCore?.jniLibName ?: run {
                if (insideEntryName.endsWith(".so", ignoreCase = true)) {
                    if (insideEntryName.startsWith("lib")) insideEntryName else "lib$insideEntryName"
                } else {
                    "lib${cleanStem.ifBlank { "custom" }}_libretro_android.so"
                }
            }
            val targetSo = File(coresDir, targetFileName)
            replaceTargetWithReadOnlySo(tempExtracted, targetSo)

            val resolvedCore = matchedCore ?: buildCoreEntry(
                id = cleanStem.ifBlank { "custom_core" },
                displayName = "Custom Core ($insideEntryName)",
                platformId = "ports",
                exts = emptyList(),
                bios = emptyList(),
                recommended = false,
                pre64 = false,
                pre32 = false,
                jniLib = targetFileName
            )
            resolvedCore to targetSo.absolutePath
        } catch (e: Throwable) {
            tempInput.delete()
            tempExtracted.delete()
            null to (e.message ?: "Core import failed")
        }
    }

    private fun queryUriDisplayName(context: Context, uri: Uri): String? {
        return try {
            context.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { c ->
                if (c.moveToFirst()) c.getString(0) else null
            }
        } catch (_: Throwable) {
            null
        }
    }

    // === Batch Core Pack Downloader ===

    fun downloadCorePack(
        context: Context,
        packId: String,
        onProgress: (Int, String, String, Int, Int) -> Unit,
        onDone: (Boolean, Int, Int, String) -> Unit
    ) {
        val appCtx = context.applicationContext
        downloadExecutor.execute {
            val catalog = init(appCtx)
            val pack = CORE_PACKS.firstOrNull { it.id.equals(packId, ignoreCase = true) }
            val targetCores = if (pack != null) {
                pack.coreIds.mapNotNull { id -> findCoreById(id, appCtx) }
            } else if (packId.equals("all_recommended", ignoreCase = true)) {
                catalog.filter { it.recommended }
            } else {
                emptyList()
            }

            if (targetCores.isEmpty()) {
                mainHandler.post { onDone(false, 0, 0, "Unknown core pack: $packId") }
                return@execute
            }

            val total = targetCores.size
            var completed = 0
            var failed = 0
            var lastMirror = "Bundled"

            for ((idx, core) in targetCores.withIndex()) {
                if (isCoreInstalled(appCtx, core)) {
                    completed++
                    val pct = ((idx + 1) * 100) / total
                    mainHandler.post {
                        onProgress(pct, core.id, "Installed", completed, total)
                    }
                    continue
                }

                val (file, mirrorOrErr) = downloadCoreBlocking(appCtx, core) { subPct, mirrorName ->
                    lastMirror = mirrorName
                    val overall = (((idx * 100) + subPct) / total).coerceIn(1, 99)
                    mainHandler.post {
                        onProgress(overall, core.id, mirrorName, completed, total)
                    }
                }
                if (file != null && file.exists()) {
                    completed++
                } else {
                    failed++
                    lastMirror = mirrorOrErr
                }
            }

            mainHandler.post {
                onProgress(100, "", lastMirror, completed, total)
                onDone(failed == 0 && completed > 0, completed, total, lastMirror)
            }
        }
    }

    // === Core Options & Per-Game Overrides Store (JSON + Standard Libretro .opt) ===

    fun getCoreOptionDefinitions(coreId: String): List<CoreOptionSpec> {
        val norm = coreId.trim().lowercase(Locale.ROOT)
        CORE_OPTION_SCHEMAS[norm]?.let { return it }
        if (norm == "mupen64plus_next") {
            CORE_OPTION_SCHEMAS["mupen64plus_next_gles3"]?.let { return it }
        }
        return listOf(
            CoreOptionSpec("${norm}_frameskip", "Пропуск кадров (Frameskip)", "Frameskip", listOf("disabled", "auto", "1", "2"), "disabled"),
            CoreOptionSpec("${norm}_aspect", "Соотношение сторон", "Aspect Ratio", listOf("auto", "4:3", "16:9"), "auto")
        )
    }

    private fun getCoreOptionsStoreDir(context: Context): File {
        val dir = File(context.applicationContext.filesDir, "core_options")
        if (!dir.exists()) dir.mkdirs()
        return dir
    }

    private fun sanitizeFileToken(raw: String): String {
        return raw.replace(Regex("[^a-zA-Z0-9._-]"), "_").take(96).ifBlank { "default" }
    }

    fun getCoreOptions(context: Context, coreId: String, gameId: String? = null): JSONObject {
        val cleanCore = coreId.trim().lowercase(Locale.ROOT)
        val defs = getCoreOptionDefinitions(cleanCore)
        val valuesObj = JSONObject()
        for (d in defs) {
            valuesObj.put(d.key, d.defaultValue)
        }

        val storeDir = getCoreOptionsStoreDir(context)
        val globalFile = File(storeDir, "${sanitizeFileToken(cleanCore)}.json")
        if (globalFile.exists()) {
            try {
                val saved = JSONObject(globalFile.readText(StandardCharsets.UTF_8))
                val keys = saved.keys()
                while (keys.hasNext()) {
                    val k = keys.next()
                    valuesObj.put(k, saved.optString(k, ""))
                }
            } catch (_: Throwable) {
            }
        }

        var hasGameOverride = false
        if (!gameId.isNullOrBlank()) {
            val gameFile = File(File(storeDir, "games"), "${sanitizeFileToken(cleanCore)}__${sanitizeFileToken(gameId)}.json")
            if (gameFile.exists()) {
                try {
                    val savedGame = JSONObject(gameFile.readText(StandardCharsets.UTF_8))
                    val keys = savedGame.keys()
                    while (keys.hasNext()) {
                        val k = keys.next()
                        valuesObj.put(k, savedGame.optString(k, ""))
                        hasGameOverride = true
                    }
                } catch (_: Throwable) {
                }
            }
        }

        val schemaArr = JSONArray()
        val lang = StormLocalization.currentLanguage
        for (d in defs) {
            val specObj = JSONObject()
            specObj.put("key", d.key)
            specObj.put("label", if (lang == "ru") d.labelRu else d.labelEn)
            specObj.put("labelRu", d.labelRu)
            specObj.put("labelEn", d.labelEn)
            specObj.put("defaultValue", d.defaultValue)
            specObj.put("currentValue", valuesObj.optString(d.key, d.defaultValue))
            val valsArr = JSONArray()
            d.values.forEach { valsArr.put(it) }
            specObj.put("values", valsArr)
            schemaArr.put(specObj)
        }

        return JSONObject().apply {
            put("coreId", cleanCore)
            put("gameId", gameId ?: "")
            put("hasGameOverride", hasGameOverride)
            put("schema", schemaArr)
            put("values", valuesObj)
        }
    }

    fun saveCoreOptions(
        context: Context,
        coreId: String,
        options: Map<String, String>,
        gameId: String? = null,
        romStem: String? = null
    ): Boolean {
        val cleanCore = coreId.trim().lowercase(Locale.ROOT)
        if (cleanCore.isEmpty()) return false

        val storeDir = getCoreOptionsStoreDir(context)
        val jsonToSave = JSONObject()
        for ((k, v) in options) {
            if (k.isNotBlank()) jsonToSave.put(k.trim(), v)
        }

        return try {
            val targetJsonFile = if (!gameId.isNullOrBlank()) {
                val gamesDir = File(storeDir, "games").apply { mkdirs() }
                File(gamesDir, "${sanitizeFileToken(cleanCore)}__${sanitizeFileToken(gameId)}.json")
            } else {
                File(storeDir, "${sanitizeFileToken(cleanCore)}.json")
            }
            targetJsonFile.writeText(jsonToSave.toString(2), StandardCharsets.UTF_8)

            // Materialize effective merged options to standard Libretro .opt files
            syncCoreOptionsToLibretroOptFiles(context, cleanCore, gameId, romStem)
            true
        } catch (_: Throwable) {
            false
        }
    }

    fun resetCoreOptions(context: Context, coreId: String, gameId: String? = null): Boolean {
        val cleanCore = coreId.trim().lowercase(Locale.ROOT)
        val storeDir = getCoreOptionsStoreDir(context)
        return try {
            if (!gameId.isNullOrBlank()) {
                val gf = File(File(storeDir, "games"), "${sanitizeFileToken(cleanCore)}__${sanitizeFileToken(gameId)}.json")
                if (gf.exists()) gf.delete()
            } else {
                val cf = File(storeDir, "${sanitizeFileToken(cleanCore)}.json")
                if (cf.exists()) cf.delete()
            }
            syncCoreOptionsToLibretroOptFiles(context, cleanCore, null, null)
            true
        } catch (_: Throwable) {
            false
        }
    }

    fun resolveEffectiveCoreVariables(context: Context, coreId: String, gameId: String? = null): Map<String, String> {
        val obj = getCoreOptions(context, coreId, gameId)
        val values = obj.optJSONObject("values") ?: return emptyMap()
        val out = LinkedHashMap<String, String>()
        val keys = values.keys()
        while (keys.hasNext()) {
            val k = keys.next()
            out[k] = values.optString(k, "")
        }
        return out
    }

    fun syncCoreOptionsToLibretroOptFiles(
        context: Context,
        coreId: String,
        gameId: String? = null,
        romStem: String? = null
    ) {
        val cleanCore = coreId.trim().lowercase(Locale.ROOT)
        val merged = resolveEffectiveCoreVariables(context, cleanCore, gameId)
        if (merged.isEmpty()) return

        val optContent = buildString {
            for ((k, v) in merged) {
                val safeV = v.replace("\"", "")
                append(k).append(" = \"").append(safeV).append("\"\n")
            }
        }

        val folderNames = (CORE_LIBRARY_OPT_DIRS[cleanCore] ?: listOf(cleanCore)).distinct()
        val roots = listOf(
            File(STORM_PUBLIC_ROOT, "config"),
            File(context.applicationContext.filesDir, "config")
        )

        for (cfgRoot in roots) {
            for (folder in folderNames) {
                try {
                    val dir = File(cfgRoot, folder)
                    dir.mkdirs()
                    File(dir, "$folder.opt").writeText(optContent, StandardCharsets.UTF_8)
                    if (folder != cleanCore) {
                        File(dir, "$cleanCore.opt").writeText(optContent, StandardCharsets.UTF_8)
                    }
                    if (!romStem.isNullOrBlank()) {
                        File(dir, "$romStem.opt").writeText(optContent, StandardCharsets.UTF_8)
                    }
                } catch (_: Throwable) {
                }
            }
        }
    }

    private fun postProgress(cb: (Int, String) -> Unit, pct: Int, msg: String) {
        mainHandler.post { cb(pct, msg) }
    }

    private fun postDone(cb: (Boolean, File?, String) -> Unit, ok: Boolean, file: File?, msg: String) {
        mainHandler.post { cb(ok, file, msg) }
    }
}
