package com.storm.retroarch

import android.content.Context
import android.os.Build
import android.os.Handler
import android.os.Looper
import java.io.BufferedInputStream
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.net.HttpURLConnection
import java.net.URL
import java.util.Locale
import java.util.concurrent.Executors
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
    val isExperimental: Boolean = false
) {
    fun localizedDescription(lang: String = StormLocalization.currentLanguage): String {
        return if (lang.lowercase(Locale.ROOT) == "ru") descriptionRu else descriptionEn
    }
}

object MobileCoreRegistry {

    const val STORM_PUBLIC_ROOT = "/storage/emulated/0/STORM RETROARCH"
    const val BUILDBOT_ANDROID_BASE = "https://buildbot.libretro.com/nightly/android/latest"
    private const val USER_AGENT = "STORM-RETROARCH/2.4.0 (Android; LibretroCoreDownloader)"

    private val downloadExecutor = Executors.newFixedThreadPool(2)
    private val mainHandler = Handler(Looper.getMainLooper())

    val ALL_CORES: List<MobileCoreInfo> = listOf(
        // === Nintendo NES / Famicom / FDS ===
        MobileCoreInfo(
            id = "fceumm",
            displayName = "FCEUmm",
            soBaseName = "fceumm_libretro",
            platformDisplay = "Nintendo NES / Famicom / FDS",
            platformKeys = setOf("nes", "fds"),
            extensions = setOf("nes", "fds", "unf", "unif"),
            descriptionRu = "Эталонное быстрое ядро NES/Dendy/FDS с поддержкой всех мапперов и читов.",
            descriptionEn = "Reference fast NES/Famicom/FDS core supporting virtually all mappers and cheats."
        ),
        MobileCoreInfo(
            id = "mesen",
            displayName = "Mesen",
            soBaseName = "mesen_libretro",
            platformDisplay = "Nintendo NES / Famicom / FDS",
            platformKeys = setOf("nes", "fds"),
            extensions = setOf("nes", "fds", "unf", "unif"),
            descriptionRu = "Потактово точное ядро NES/FDS референсного уровня с HD-паками.",
            descriptionEn = "Cycle-accurate reference NES/FDS core with HD pack support."
        ),
        MobileCoreInfo(
            id = "nestopia",
            displayName = "Nestopia UE",
            soBaseName = "nestopia_libretro",
            platformDisplay = "Nintendo NES / Famicom / FDS",
            platformKeys = setOf("nes", "fds"),
            extensions = setOf("nes", "fds", "unf", "unif"),
            descriptionRu = "Классическое стабильное ядро NES/FDS с низким энергопотреблением.",
            descriptionEn = "Classic low-overhead NES/FDS emulator with accurate timing."
        ),

        // === Super Nintendo (SNES / Super Famicom) ===
        MobileCoreInfo(
            id = "snes9x",
            displayName = "Snes9x",
            soBaseName = "snes9x_libretro",
            platformDisplay = "Super Nintendo (SNES)",
            platformKeys = setOf("snes"),
            extensions = setOf("sfc", "smc", "swc", "fig", "bs"),
            descriptionRu = "Основное высокоскоростное ядро Super Nintendo с поддержкой чипов SA-1 и Super FX.",
            descriptionEn = "Primary high-speed Super Nintendo core with full SA-1, DSP, and Super FX support."
        ),
        MobileCoreInfo(
            id = "snes9x2010",
            displayName = "Snes9x 2010",
            soBaseName = "snes9x2010_libretro",
            platformDisplay = "Super Nintendo (SNES)",
            platformKeys = setOf("snes"),
            extensions = setOf("sfc", "smc", "swc", "fig"),
            descriptionRu = "Облегчённое ядро SNES для максимальной частоты кадров на бюджетных ARM чипах.",
            descriptionEn = "Lightweight SNES core tuned for maximum frame rates on mobile ARM devices."
        ),
        MobileCoreInfo(
            id = "bsnes",
            displayName = "bsnes",
            soBaseName = "bsnes_libretro",
            platformDisplay = "Super Nintendo (SNES)",
            platformKeys = setOf("snes"),
            extensions = setOf("sfc", "smc", "bs"),
            descriptionRu = "Аппаратно точная эмуляция PPU/SPC700 консоли Super Nintendo и MSU-1.",
            descriptionEn = "Hardware-accurate SNES PPU/SPC700 emulation with MSU-1 audio."
        ),
        MobileCoreInfo(
            id = "mesen_s",
            displayName = "Mesen-S",
            soBaseName = "mesen-s_libretro",
            platformDisplay = "Super Nintendo (SNES)",
            platformKeys = setOf("snes"),
            extensions = setOf("sfc", "smc", "bs"),
            descriptionRu = "Высокоточное ядро SNES с поддержкой сопроцессоров и Super Game Boy.",
            descriptionEn = "High-accuracy SNES core with enhancement chip and Super Game Boy accuracy."
        ),

        // === Nintendo 64 ===
        MobileCoreInfo(
            id = "mupen64plus_next",
            displayName = "Mupen64Plus-Next",
            soBaseName = "mupen64plus_next_gles3_libretro",
            platformDisplay = "Nintendo 64",
            platformKeys = setOf("n64"),
            extensions = setOf("z64", "n64", "v64", "ndd"),
            descriptionRu = "Современное ядро Nintendo 64 с рендерером GLideN64 GLES3 и широкоформатным режимом.",
            descriptionEn = "Modern Nintendo 64 core powered by GLideN64 GLES3 and dynarec JIT."
        ),
        MobileCoreInfo(
            id = "parallel_n64",
            displayName = "ParaLLEl N64",
            soBaseName = "parallel_n64_libretro",
            platformDisplay = "Nintendo 64",
            platformKeys = setOf("n64"),
            extensions = setOf("z64", "n64", "v64"),
            descriptionRu = "Высокопроизводительное ядро N64 с поддержкой Vulkan/GLES и ромхаков.",
            descriptionEn = "High-performance N64 core with Vulkan/GLES rendering and ROM hack compatibility."
        ),

        // === Nintendo GameCube & Wii ===
        MobileCoreInfo(
            id = "dolphin",
            displayName = "Dolphin",
            soBaseName = "dolphin_libretro",
            platformDisplay = "Nintendo GameCube / Wii",
            platformKeys = setOf("gamecube", "wii", "gamecube_wii"),
            extensions = setOf("rvz", "gcm", "gcz", "wbfs", "ciso", "iso", "dol", "elf"),
            descriptionRu = "Официальное ядро GameCube и Wii с поддержкой Vulkan и OpenGL ES 3.2.",
            descriptionEn = "Official GameCube and Wii emulator with Vulkan and OpenGL ES 3.2 backends."
        ),

        // === Nintendo Handhelds: GB / GBC / GBA / NDS / 3DS / VB / PokeMini / G&W ===
        MobileCoreInfo(
            id = "gambatte",
            displayName = "Gambatte",
            soBaseName = "gambatte_libretro",
            platformDisplay = "Nintendo Game Boy / Color",
            platformKeys = setOf("gb", "gbc", "gb_gbc"),
            extensions = setOf("gb", "gbc", "dmg"),
            descriptionRu = "Эталонное ядро Game Boy и Game Boy Color с оригинальными цветовыми палитрами.",
            descriptionEn = "Cycle-accurate Game Boy and Game Boy Color core with authentic DMG palettes."
        ),
        MobileCoreInfo(
            id = "sameboy",
            displayName = "SameBoy",
            soBaseName = "sameboy_libretro",
            platformDisplay = "Nintendo Game Boy / Color",
            platformKeys = setOf("gb", "gbc", "gb_gbc"),
            extensions = setOf("gb", "gbc"),
            descriptionRu = "Сверхточная эмуляция звука, таймингов и Super Game Boy рамок для GB/GBC.",
            descriptionEn = "Ultra-accurate Game Boy, Game Boy Color, and Super Game Boy border core."
        ),
        MobileCoreInfo(
            id = "mgba",
            displayName = "mGBA",
            soBaseName = "mgba_libretro",
            platformDisplay = "Nintendo Game Boy Advance",
            platformKeys = setOf("gba"),
            extensions = setOf("gba", "agb"),
            descriptionRu = "Лучшее ядро Game Boy Advance со 100% совместимостью, гироскопом и солнечным датчиком.",
            descriptionEn = "Premier Game Boy Advance core with full compatibility, solar sensor, and rumble."
        ),
        MobileCoreInfo(
            id = "vbam",
            displayName = "VBA-M",
            soBaseName = "vbam_libretro",
            platformDisplay = "Nintendo Game Boy Advance",
            platformKeys = setOf("gba"),
            extensions = setOf("gba", "agb"),
            descriptionRu = "Классический эмулятор VisualBoyAdvance-M с расширенной поддержкой читов.",
            descriptionEn = "Classic VisualBoyAdvance-M core with extended cheat code support."
        ),
        MobileCoreInfo(
            id = "melonds",
            displayName = "melonDS",
            soBaseName = "melonds_libretro",
            platformDisplay = "Nintendo DS / DSi",
            platformKeys = setOf("nds"),
            extensions = setOf("nds", "dsi", "ids"),
            descriptionRu = "Точное и быстрое ядро Nintendo DS / DSi с поддержкой микрофона и сенсорного экрана.",
            descriptionEn = "Fast, accurate Nintendo DS / DSi core with touch stylus and DSiWare support."
        ),
        MobileCoreInfo(
            id = "desmume",
            displayName = "DeSmuME",
            soBaseName = "desmume_libretro",
            platformDisplay = "Nintendo DS",
            platformKeys = setOf("nds"),
            extensions = setOf("nds"),
            descriptionRu = "Проверенное ядро Nintendo DS с масштабированием внутреннего 3D разрешения.",
            descriptionEn = "Battle-tested Nintendo DS core featuring internal 3D resolution upscaling."
        ),
        MobileCoreInfo(
            id = "citra",
            displayName = "Citra",
            soBaseName = "citra_libretro",
            platformDisplay = "Nintendo 3DS",
            platformKeys = setOf("3ds"),
            extensions = setOf("3ds", "3dsx", "cci", "cxi"),
            descriptionRu = "Основное 3D ядро Nintendo 3DS с аппаратным ускорением OpenGL ES / Vulkan.",
            descriptionEn = "Primary Nintendo 3DS core with hardware-accelerated rendering."
        ),
        MobileCoreInfo(
            id = "azahar",
            displayName = "Azahar",
            soBaseName = "azahar_libretro",
            platformDisplay = "Nintendo 3DS",
            platformKeys = setOf("3ds"),
            extensions = setOf("3ds", "3dsx", "cci", "cxi"),
            descriptionRu = "Современный форк эмулятора 3DS нового поколения с улучшенным Vulkan конвейером.",
            descriptionEn = "Next-gen Nintendo 3DS core fork with optimized Vulkan shader caching.",
            isExperimental = true
        ),
        MobileCoreInfo(
            id = "panda3ds",
            displayName = "Panda3DS",
            soBaseName = "panda3ds_libretro",
            platformDisplay = "Nintendo 3DS",
            platformKeys = setOf("3ds"),
            extensions = setOf("3ds", "3dsx", "cci", "cxi"),
            descriptionRu = "Экспериментальный современный эмулятор Nintendo 3DS с низкими задержками.",
            descriptionEn = "Experimental modern HLE Nintendo 3DS emulator core.",
            isExperimental = true
        ),
        MobileCoreInfo(
            id = "beetle_vb",
            displayName = "Beetle VB",
            soBaseName = "mednafen_vb_libretro",
            platformDisplay = "Nintendo Virtual Boy",
            platformKeys = setOf("vb"),
            extensions = setOf("vb", "vboy"),
            descriptionRu = "Точная эмуляция Nintendo Virtual Boy с настраиваемыми 2D/3D палитрами.",
            descriptionEn = "Accurate Nintendo Virtual Boy emulator with customizable palette modes."
        ),
        MobileCoreInfo(
            id = "pokemini",
            displayName = "PokeMini",
            soBaseName = "pokemini_libretro",
            platformDisplay = "Nintendo Pokemon Mini",
            platformKeys = setOf("pokemini"),
            extensions = setOf("min"),
            descriptionRu = "Специализированное ядро карманной консоли Nintendo Pokemon Mini.",
            descriptionEn = "Dedicated emulator for the Nintendo Pokemon Mini handheld."
        ),
        MobileCoreInfo(
            id = "gw",
            displayName = "Game & Watch",
            soBaseName = "gw_libretro",
            platformDisplay = "Nintendo Game & Watch",
            platformKeys = setOf("gw"),
            extensions = setOf("mgw"),
            descriptionRu = "Симулятор классических электронных игр Nintendo Game & Watch.",
            descriptionEn = "Authentic simulator for classic Nintendo Game & Watch handhelds."
        ),

        // === Sony PlayStation 1 / PS2 / PSP / PS Vita ===
        MobileCoreInfo(
            id = "pcsx_rearmed",
            displayName = "PCSX-ReARMed",
            soBaseName = "pcsx_rearmed_libretro",
            platformDisplay = "Sony PlayStation 1",
            platformKeys = setOf("ps1"),
            extensions = setOf("chd", "cue", "pbp", "bin", "img", "iso", "m3u"),
            descriptionRu = "Сверхбыстрое ARM NEON ядро PlayStation 1 со встроенным удвоением разрешения.",
            descriptionEn = "Blazing-fast ARM NEON optimized PlayStation 1 core with enhanced resolution."
        ),
        MobileCoreInfo(
            id = "swanstation",
            displayName = "SwanStation",
            soBaseName = "swanstation_libretro",
            platformDisplay = "Sony PlayStation 1",
            platformKeys = setOf("ps1"),
            extensions = setOf("chd", "cue", "pbp", "bin", "img", "iso", "m3u"),
            descriptionRu = "Премиальное ядро PlayStation 1 с коррекцией геометрии PGXP и 4K апскейлингом.",
            descriptionEn = "Studio-grade PlayStation 1 core with PGXP geometry correction and Vulkan/GLES upscaling."
        ),
        MobileCoreInfo(
            id = "beetle_psx_hw",
            displayName = "Beetle PSX HW",
            soBaseName = "mednafen_psx_hw_libretro",
            platformDisplay = "Sony PlayStation 1",
            platformKeys = setOf("ps1"),
            extensions = setOf("chd", "cue", "pbp", "toc", "ccd", "m3u"),
            descriptionRu = "Эталонное аппаратное ядро Mednafen PSX с субпиксельной точностью текстур.",
            descriptionEn = "Reference Mednafen PlayStation 1 hardware-accelerated core with PGXP."
        ),
        MobileCoreInfo(
            id = "play_ps2",
            displayName = "Play!",
            soBaseName = "play_libretro",
            platformDisplay = "Sony PlayStation 2",
            platformKeys = setOf("ps2"),
            extensions = setOf("chd", "iso", "cso", "isz", "elf"),
            descriptionRu = "Автономное ядро PlayStation 2 без необходимости внешнего BIOS (HLE).",
            descriptionEn = "BIOS-free HLE Sony PlayStation 2 emulator for Android ARM64."
        ),
        MobileCoreInfo(
            id = "lrps2",
            displayName = "LRPS2",
            soBaseName = "pcsx2_libretro",
            platformDisplay = "Sony PlayStation 2",
            platformKeys = setOf("ps2"),
            extensions = setOf("chd", "iso", "cso", "bin", "gz"),
            descriptionRu = "Высокосовместимое ядро PlayStation 2 на базе PCSX2 (требует BIOS в pcsx2/bios/).",
            descriptionEn = "High-compatibility PCSX2-based PlayStation 2 core (requires BIOS in pcsx2/bios/).",
            isExperimental = true
        ),
        MobileCoreInfo(
            id = "ppsspp",
            displayName = "PPSSPP",
            soBaseName = "ppsspp_libretro",
            platformDisplay = "Sony PlayStation Portable (PSP)",
            platformKeys = setOf("psp"),
            extensions = setOf("iso", "cso", "pbp", "chd", "elf"),
            descriptionRu = "Золотой стандарт эмуляции Sony PSP с поддержкой Vulkan, 60 FPS и HD текстур.",
            descriptionEn = "Gold-standard Sony PSP core with Vulkan/GLES rendering and HD upscaling."
        ),
        MobileCoreInfo(
            id = "vita3k",
            displayName = "Vita3K",
            soBaseName = "vita3k_libretro",
            platformDisplay = "Sony PlayStation Vita",
            platformKeys = setOf("psvita"),
            extensions = setOf("vpk", "zip"),
            descriptionRu = "Экспериментальное ядро Sony PS Vita с аппаратным ускорением Vulkan.",
            descriptionEn = "Experimental Sony PlayStation Vita core with Vulkan acceleration.",
            isExperimental = true
        ),

        // === Sega Consoles & Handhelds ===
        MobileCoreInfo(
            id = "genesis_plus_gx",
            displayName = "Genesis Plus GX",
            soBaseName = "genesis_plus_gx_libretro",
            platformDisplay = "Sega Genesis / Mega-CD / Master System / Game Gear",
            platformKeys = setOf("genesis", "segacd", "sms_gg", "sg1000"),
            extensions = setOf("md", "gen", "smd", "bin", "chd", "cue", "iso", "sms", "gg", "sg", "m3u"),
            descriptionRu = "Эталонная 100% точная эмуляция YM2612/VDP для Mega Drive, Mega-CD, SMS и Game Gear.",
            descriptionEn = "Cycle-accurate Sega Genesis, Mega-CD, Master System, Game Gear, and SG-1000 core."
        ),
        MobileCoreInfo(
            id = "picodrive",
            displayName = "PicoDrive",
            soBaseName = "picodrive_libretro",
            platformDisplay = "Sega Genesis / 32X / Mega-CD / Pico",
            platformKeys = setOf("genesis", "sega32x", "segacd", "segapico"),
            extensions = setOf("md", "gen", "smd", "32x", "chd", "cue", "iso", "sms", "m3u"),
            descriptionRu = "Единственное быстрое ARM JIT ядро с полной поддержкой Sega 32X, SVP (Virtua Racing) и Mega-CD.",
            descriptionEn = "Fast ARM dynarec core supporting Sega 32X, Virtua Racing SVP, Genesis, and Mega-CD."
        ),
        MobileCoreInfo(
            id = "smsplus",
            displayName = "SMS Plus GX",
            soBaseName = "smsplus_libretro",
            platformDisplay = "Sega Master System / Game Gear",
            platformKeys = setOf("sms_gg"),
            extensions = setOf("sms", "gg", "col"),
            descriptionRu = "Лёгкое специализированное ядро для Sega Master System и Game Gear с FM звуком.",
            descriptionEn = "Dedicated lightweight Sega Master System and Game Gear core with YM2413 FM audio."
        ),
        MobileCoreInfo(
            id = "yabasanshiro",
            displayName = "YabaSanshiro",
            soBaseName = "yabasanshiro_libretro",
            platformDisplay = "Sega Saturn",
            platformKeys = setOf("saturn"),
            extensions = setOf("chd", "cue", "ccd", "mds", "iso", "m3u"),
            descriptionRu = "Высокоскоростное ARM64 OpenGL ES 3.1 / Vulkan ядро Sega Saturn с HD рендерингом.",
            descriptionEn = "High-speed ARM64 Sega Saturn core with OpenGL ES 3.1 / Vulkan HD polygon rendering."
        ),
        MobileCoreInfo(
            id = "beetle_saturn",
            displayName = "Beetle Saturn",
            soBaseName = "mednafen_saturn_libretro",
            platformDisplay = "Sega Saturn",
            platformKeys = setOf("saturn"),
            extensions = setOf("chd", "cue", "ccd", "toc", "m3u"),
            descriptionRu = "Максимально точное ядро Sega Saturn для сложных двухпроцессорных игр SH-2.",
            descriptionEn = "Reference accuracy Sega Saturn core for demanding dual SH-2/VDP2 titles."
        ),
        MobileCoreInfo(
            id = "kronos",
            displayName = "Kronos",
            soBaseName = "kronos_libretro",
            platformDisplay = "Sega Saturn / ST-V",
            platformKeys = setOf("saturn"),
            extensions = setOf("chd", "cue", "ccd", "mds", "iso", "m3u"),
            descriptionRu = "Продвинутое ядро Sega Saturn и аркадной платы ST-V с тесселяцией полигонов.",
            descriptionEn = "Advanced Sega Saturn and ST-V arcade core with polygon upscaling."
        ),
        MobileCoreInfo(
            id = "flycast",
            displayName = "Flycast",
            soBaseName = "flycast_libretro",
            platformDisplay = "Sega Dreamcast / NAOMI / Atomiswave",
            platformKeys = setOf("dreamcast", "naomi"),
            extensions = setOf("chd", "gdi", "cdi", "cue", "lst", "dat", "m3u"),
            descriptionRu = "Безупречное ядро Sega Dreamcast, NAOMI и Atomiswave с попиксельной прозрачностью OIT.",
            descriptionEn = "Premier Sega Dreamcast, NAOMI, and Atomiswave core with Vulkan/GLES per-pixel sorting."
        ),

        // === Arcade, Neo Geo & CPS ===
        MobileCoreInfo(
            id = "fbneo",
            displayName = "FinalBurn Neo (FBNeo)",
            soBaseName = "fbneo_libretro",
            platformDisplay = "Arcade / Neo Geo MVS-AES / CPS-1-2-3",
            platformKeys = setOf("arcade", "neogeo", "cps"),
            extensions = setOf("zip", "7z"),
            descriptionRu = "Лучшее аркадное ядро для Neo Geo, Capcom CPS-1/2/3, Konami, Cave и сетевой игры.",
            descriptionEn = "Top-tier arcade core for Neo Geo, Capcom CPS-1/2/3, Konami, Cave, and Rollback Netplay."
        ),
        MobileCoreInfo(
            id = "mame_current",
            displayName = "MAME Current",
            soBaseName = "mame_libretro",
            platformDisplay = "Arcade (MAME)",
            platformKeys = setOf("arcade"),
            extensions = setOf("zip", "7z", "chd"),
            descriptionRu = "Полный современный каталог тысяч аркадных автоматов MAME.",
            descriptionEn = "Comprehensive modern MAME arcade preservation core."
        ),
        MobileCoreInfo(
            id = "mame2003_plus",
            displayName = "MAME 2003-Plus",
            soBaseName = "mame2003_plus_libretro",
            platformDisplay = "Arcade (MAME 2003-Plus)",
            platformKeys = setOf("arcade"),
            extensions = setOf("zip"),
            descriptionRu = "Оптимизированное мобильное аркадное ядро со стабильными 60 FPS и саундтреками CD.",
            descriptionEn = "Mobile-optimized arcade core delivering rock-solid 60 FPS and backdrop support."
        ),
        MobileCoreInfo(
            id = "neocd",
            displayName = "NeoCD",
            soBaseName = "neocd_libretro",
            platformDisplay = "SNK Neo Geo CD",
            platformKeys = setOf("neogeocd"),
            extensions = setOf("chd", "cue"),
            descriptionRu = "Точный эмулятор SNK Neo Geo CD с мгновенным пропуском экранов чтения диска.",
            descriptionEn = "Accurate SNK Neo Geo CD core featuring CD-speed skip for zero load waits."
        ),

        // === NEC, SNK Handhelds, Bandai, 3DO, Vectrex, Coleco, Intellivision ===
        MobileCoreInfo(
            id = "beetle_pce_fast",
            displayName = "Beetle PCE Fast",
            soBaseName = "mednafen_pce_fast_libretro",
            platformDisplay = "NEC PC Engine / TurboGrafx-16 / CD",
            platformKeys = setOf("pce", "pcecd"),
            extensions = setOf("pce", "sgx", "cue", "ccd", "chd", "toc", "m3u"),
            descriptionRu = "Быстрое и точное ядро PC Engine, TurboGrafx-16 и CD-ROM2 (требует syscard3.pce для CD).",
            descriptionEn = "Fast, accurate PC Engine / TurboGrafx-16 and TurboGrafx-CD core."
        ),
        MobileCoreInfo(
            id = "beetle_pcfx",
            displayName = "Beetle PC-FX",
            soBaseName = "mednafen_pcfx_libretro",
            platformDisplay = "NEC PC-FX",
            platformKeys = setOf("pcfx"),
            extensions = setOf("chd", "cue", "ccd", "toc"),
            descriptionRu = "Полная эмуляция 32-битной японской мультимедийной консоли NEC PC-FX.",
            descriptionEn = "Full emulation of NEC's 32-bit PC-FX console."
        ),
        MobileCoreInfo(
            id = "beetle_ngp",
            displayName = "Beetle NeoPop",
            soBaseName = "mednafen_ngp_libretro",
            platformDisplay = "SNK Neo Geo Pocket / Color",
            platformKeys = setOf("ngp"),
            extensions = setOf("ngp", "ngc", "ngpc"),
            descriptionRu = "Точное ядро портативных консолей SNK Neo Geo Pocket и Neo Geo Pocket Color.",
            descriptionEn = "Accurate SNK Neo Geo Pocket and Neo Geo Pocket Color core."
        ),
        MobileCoreInfo(
            id = "beetle_wswan",
            displayName = "Beetle WonderSwan",
            soBaseName = "mednafen_wswan_libretro",
            platformDisplay = "Bandai WonderSwan / Color",
            platformKeys = setOf("wonderswan"),
            extensions = setOf("ws", "wsc", "pc2"),
            descriptionRu = "Эмуляция Bandai WonderSwan и WonderSwan Color с поворотом экрана.",
            descriptionEn = "Bandai WonderSwan and WonderSwan Color core with portrait/landscape rotation."
        ),
        MobileCoreInfo(
            id = "opera_3do",
            displayName = "Opera 3DO",
            soBaseName = "opera_libretro",
            platformDisplay = "Panasonic / GoldStar 3DO",
            platformKeys = setOf("3do"),
            extensions = setOf("iso", "chd", "cue", "bin"),
            descriptionRu = "Оптимизированный эмулятор 32-битной консоли 3DO Interactive Multiplayer.",
            descriptionEn = "Optimized emulator for the 32-bit 3DO Interactive Multiplayer."
        ),
        MobileCoreInfo(
            id = "same_cdi",
            displayName = "SAME CDi",
            soBaseName = "same_cdi_libretro",
            platformDisplay = "Philips CD-i",
            platformKeys = setOf("cdi"),
            extensions = setOf("chd", "cue", "iso"),
            descriptionRu = "Специализированное ядро для мультимедийной системы Philips CD-i.",
            descriptionEn = "Dedicated core for Philips CD-i interactive discs."
        ),
        MobileCoreInfo(
            id = "vecx",
            displayName = "Vecx",
            soBaseName = "vecx_libretro",
            platformDisplay = "GCE Vectrex",
            platformKeys = setOf("vectrex"),
            extensions = setOf("vec"),
            descriptionRu = "Эмулятор векторной консоли GCE Vectrex с поддержкой цветных накладок.",
            descriptionEn = "Vector display GCE Vectrex emulator with hardware GLES line rendering."
        ),
        MobileCoreInfo(
            id = "gearcoleco",
            displayName = "Gearcoleco",
            soBaseName = "gearcoleco_libretro",
            platformDisplay = "ColecoVision",
            platformKeys = setOf("coleco"),
            extensions = setOf("col", "cv", "rom"),
            descriptionRu = "Высокоточный эмулятор классической консоли ColecoVision.",
            descriptionEn = "Cycle-accurate ColecoVision console emulator."
        ),
        MobileCoreInfo(
            id = "freeintv",
            displayName = "FreeIntv",
            soBaseName = "freeintv_libretro",
            platformDisplay = "Mattel Intellivision",
            platformKeys = setOf("intellivision"),
            extensions = setOf("int", "itv"),
            descriptionRu = "Эмулятор консоли Mattel Intellivision со встроенной виртуальной цифровой панелью.",
            descriptionEn = "Mattel Intellivision emulator with virtual keypad overlay."
        ),
        MobileCoreInfo(
            id = "o2em",
            displayName = "O2EM",
            soBaseName = "o2em_libretro",
            platformDisplay = "Magnavox Odyssey 2 / Videopac",
            platformKeys = setOf("odyssey2"),
            extensions = setOf("bin"),
            descriptionRu = "Эмулятор консоли Magnavox Odyssey 2 и Philips Videopac G7000.",
            descriptionEn = "Magnavox Odyssey 2 / Philips Videopac G7000 emulator."
        ),

        // === Atari Consoles & Computers ===
        MobileCoreInfo(
            id = "stella",
            displayName = "Stella",
            soBaseName = "stella_libretro",
            platformDisplay = "Atari 2600",
            platformKeys = setOf("atari2600"),
            extensions = setOf("a26", "bin"),
            descriptionRu = "Эталонное ядро Atari 2600 со 100% поддержкой всех банков памяти и контроллеров.",
            descriptionEn = "Reference Atari 2600 VCS emulator supporting all bank-switching schemes."
        ),
        MobileCoreInfo(
            id = "atari800",
            displayName = "Atari800",
            soBaseName = "atari800_libretro",
            platformDisplay = "Atari 5200 / Atari 8-Bit (400/800/XL/XE)",
            platformKeys = setOf("atari5200", "atari800"),
            extensions = setOf("a52", "atr", "xfd", "xex", "car"),
            descriptionRu = "Универсальный эмулятор консоли Atari 5200 и домашних компьютеров Atari 800XL/XE.",
            descriptionEn = "Authentic Atari 5200 console and Atari 400/800/XL/XE computer core."
        ),
        MobileCoreInfo(
            id = "prosystem",
            displayName = "ProSystem",
            soBaseName = "prosystem_libretro",
            platformDisplay = "Atari 7800",
            platformKeys = setOf("atari7800"),
            extensions = setOf("a78"),
            descriptionRu = "Быстрое и точное ядро консоли Atari 7800 ProSystem с чипом звука POKEY.",
            descriptionEn = "Fast, accurate Atari 7800 ProSystem core with POKEY audio."
        ),
        MobileCoreInfo(
            id = "handy",
            displayName = "Handy",
            soBaseName = "handy_libretro",
            platformDisplay = "Atari Lynx",
            platformKeys = setOf("lynx"),
            extensions = setOf("lnx", "lyx", "o"),
            descriptionRu = "Классический быстрый эмулятор цветной портативной консоли Atari Lynx.",
            descriptionEn = "Fast portable Atari Lynx handheld emulator."
        ),
        MobileCoreInfo(
            id = "virtualjaguar",
            displayName = "Virtual Jaguar",
            soBaseName = "virtualjaguar_libretro",
            platformDisplay = "Atari Jaguar",
            platformKeys = setOf("jaguar"),
            extensions = setOf("j64", "jag", "abs", "cof"),
            descriptionRu = "Эмулятор 64-битной системы Atari Jaguar.",
            descriptionEn = "64-bit Atari Jaguar console emulator."
        ),
        MobileCoreInfo(
            id = "hatari",
            displayName = "Hatari",
            soBaseName = "hatari_libretro",
            platformDisplay = "Atari ST / STE / Falcon",
            platformKeys = setOf("atarist"),
            extensions = setOf("st", "msa", "stx", "dim", "ipf"),
            descriptionRu = "Точная эмуляция 16-битных компьютеров линейки Atari ST и STE.",
            descriptionEn = "Accurate 16-bit Atari ST / STE computer emulator."
        ),

        // === MS-DOS, ScummVM & Classic Home Computers ===
        MobileCoreInfo(
            id = "dosbox_pure",
            displayName = "DOSBox-Pure",
            soBaseName = "dosbox_pure_libretro",
            platformDisplay = "MS-DOS / PC Classic",
            platformKeys = setOf("dos"),
            extensions = setOf("dosz", "exe", "com", "bat", "conf", "ima", "img", "vhd", "zip"),
            descriptionRu = "Идеальное ядро MS-DOS с прямым запуском из .ZIP, авто-маппингом геймпада, Roland MT-32 и 3dfx.",
            descriptionEn = "Zero-config MS-DOS core running directly from .ZIP archives with gamepad mapping and Sound Blaster/MT-32."
        ),
        MobileCoreInfo(
            id = "scummvm",
            displayName = "ScummVM",
            soBaseName = "scummvm_libretro",
            platformDisplay = "ScummVM Classic Adventures",
            platformKeys = setOf("scummvm"),
            extensions = setOf("scummvm"),
            descriptionRu = "Движок классических графических квестов LucasArts, Sierra, Revolution и Westwood.",
            descriptionEn = "Engine collection for classic point-and-click adventures from LucasArts, Sierra, and Westwood."
        ),
        MobileCoreInfo(
            id = "puae",
            displayName = "PUAE (Amiga)",
            soBaseName = "puae_libretro",
            platformDisplay = "Commodore Amiga / CD32",
            platformKeys = setOf("amiga"),
            extensions = setOf("adf", "adz", "dms", "ipf", "lha", "hdf", "uae"),
            descriptionRu = "Полноценная эмуляция Commodore Amiga 500/1200/4000, WHDLoad .lha и Amiga CD32.",
            descriptionEn = "Full-featured Commodore Amiga 500/1200/CD32 emulator with direct WHDLoad .lha support."
        ),
        MobileCoreInfo(
            id = "fuse",
            displayName = "Fuse (ZX Spectrum)",
            soBaseName = "fuse_libretro",
            platformDisplay = "Sinclair ZX Spectrum",
            platformKeys = setOf("zxspectrum"),
            extensions = setOf("tzx", "tap", "z80", "sna", "trd", "scl", "dsk"),
            descriptionRu = "Эталонный эмулятор ZX Spectrum 48K/128K/Pentagon с поддержкой TR-DOS .trd/.scl.",
            descriptionEn = "Reference ZX Spectrum 48K/128K/+3/Pentagon emulator supporting TAP, TZX, and TR-DOS."
        ),
        MobileCoreInfo(
            id = "vice_x64",
            displayName = "VICE x64",
            soBaseName = "vice_x64_libretro",
            platformDisplay = "Commodore 64",
            platformKeys = setOf("c64"),
            extensions = setOf("d64", "t64", "prg", "crt", "tap", "g64"),
            descriptionRu = "Классический точный эмулятор Commodore 64 с безупречным звучанием чипа SID.",
            descriptionEn = "Accurate Commodore 64 emulator with authentic MOS 6581/8580 SID sound."
        ),
        MobileCoreInfo(
            id = "bluemsx",
            displayName = "blueMSX",
            soBaseName = "bluemsx_libretro",
            platformDisplay = "MSX / MSX2 / MSX2+ / Coleco / SVi",
            platformKeys = setOf("msx"),
            extensions = setOf("mx1", "mx2", "rom", "dsk", "cas"),
            descriptionRu = "Полная эмуляция компьютеров стандарта MSX, MSX2, MSX2+ и звукового чипа SCC.",
            descriptionEn = "Full MSX, MSX2, MSX2+, and TurboR emulator with Konami SCC audio."
        ),
        MobileCoreInfo(
            id = "px68k",
            displayName = "PX68k",
            soBaseName = "px68k_libretro",
            platformDisplay = "Sharp X68000",
            platformKeys = setOf("x68000"),
            extensions = setOf("dim", "xdf", "hdm", "2hd", "d88", "m3u"),
            descriptionRu = "Эмулятор культовой японской аркадной рабочей станции Sharp X68000.",
            descriptionEn = "Emulator for Japan's powerhouse Sharp X68000 home workstation."
        ),
        MobileCoreInfo(
            id = "np2kai",
            displayName = "NP2kai (PC-98)",
            soBaseName = "np2kai_libretro",
            platformDisplay = "NEC PC-9801 / PC-9821",
            platformKeys = setOf("pc98"),
            extensions = setOf("d98", "fdi", "hdi", "nhd", "fdd", "thd"),
            descriptionRu = "Продвинутый эмулятор японских компьютеров NEC PC-9801 и PC-9821 (Neko Project II Kai).",
            descriptionEn = "Neko Project II Kai emulator for NEC PC-9801 and PC-9821 computers."
        ),
        MobileCoreInfo(
            id = "quasi88",
            displayName = "QUASI88 (PC-88)",
            soBaseName = "quasi88_libretro",
            platformDisplay = "NEC PC-8801",
            platformKeys = setOf("pc88"),
            extensions = setOf("d88", "t88", "cmt"),
            descriptionRu = "Точный эмулятор японских компьютеров серии NEC PC-8801.",
            descriptionEn = "Accurate NEC PC-8801 series computer emulator."
        ),
        MobileCoreInfo(
            id = "cap32",
            displayName = "Caprice32",
            soBaseName = "cap32_libretro",
            platformDisplay = "Amstrad CPC",
            platformKeys = setOf("amstrad"),
            extensions = setOf("cpc", "dsk", "cdt", "sna"),
            descriptionRu = "Быстрый эмулятор домашних компьютеров Amstrad CPC 464/664/6128.",
            descriptionEn = "Fast Amstrad CPC 464/664/6128 home computer emulator."
        ),
        MobileCoreInfo(
            id = "bk_elektronika",
            displayName = "BK-0010 / 0011M",
            soBaseName = "bk_libretro",
            platformDisplay = "Электроника БК-0010 / БК-0011М",
            platformKeys = setOf("bk0010"),
            extensions = setOf("bkd", "img"),
            descriptionRu = "Эмулятор отечественных 16-разрядных компьютеров Электроника БК-0010-01 и БК-0011М.",
            descriptionEn = "Emulator for Soviet 16-bit Elektronika BK-0010 and BK-0011M computers."
        ),
        MobileCoreInfo(
            id = "retro8",
            displayName = "Retro8 (PICO-8)",
            soBaseName = "retro8_libretro",
            platformDisplay = "PICO-8 Fantasy Console",
            platformKeys = setOf("pico8"),
            extensions = setOf("p8", "png"),
            descriptionRu = "Открытая реализация фэнтези-консоли PICO-8 для запуска картриджей .p8 и .p8.png.",
            descriptionEn = "Open-source PICO-8 fantasy console interpreter."
        ),
        MobileCoreInfo(
            id = "tic80",
            displayName = "TIC-80",
            soBaseName = "tic80_libretro",
            platformDisplay = "TIC-80 Fantasy Computer",
            platformKeys = setOf("tic80"),
            extensions = setOf("tic"),
            descriptionRu = "Фэнтези-компьютер TIC-80 для инди-игр и демосцены.",
            descriptionEn = "TIC-80 tiny computer for retro games and demoscene."
        ),
        MobileCoreInfo(
            id = "prboom",
            displayName = "PrBoom (DOOM)",
            soBaseName = "prboom_libretro",
            platformDisplay = "DOOM Engine",
            platformKeys = setOf("doom"),
            extensions = setOf("wad", "iwad", "pwad"),
            descriptionRu = "Классический порт движка DOOM / DOOM II / Final DOOM с плавной частотой кадров.",
            descriptionEn = "Classic DOOM / DOOM II WAD engine port."
        )
    )

    private val PLATFORM_DISPLAY_NAMES: Map<String, String> = mapOf(
        "nes" to "Nintendo NES / Famicom",
        "fds" to "Nintendo Famicom Disk System",
        "snes" to "Super Nintendo (SNES)",
        "n64" to "Nintendo 64",
        "gamecube" to "Nintendo GameCube",
        "wii" to "Nintendo Wii",
        "gamecube_wii" to "Nintendo GameCube / Wii",
        "gb" to "Nintendo Game Boy",
        "gbc" to "Nintendo Game Boy Color",
        "gb_gbc" to "Nintendo Game Boy / Color",
        "gba" to "Nintendo Game Boy Advance",
        "nds" to "Nintendo DS / DSi",
        "3ds" to "Nintendo 3DS",
        "vb" to "Nintendo Virtual Boy",
        "pokemini" to "Nintendo Pokemon Mini",
        "gw" to "Nintendo Game & Watch",
        "ps1" to "Sony PlayStation 1",
        "ps2" to "Sony PlayStation 2",
        "ps3" to "Sony PlayStation 3",
        "psp" to "Sony PlayStation Portable",
        "psvita" to "Sony PlayStation Vita",
        "genesis" to "Sega Genesis / Mega Drive",
        "segacd" to "Sega Mega-CD / Sega CD",
        "sega32x" to "Sega 32X",
        "sms_gg" to "Sega Master System / Game Gear",
        "sg1000" to "Sega SG-1000",
        "segapico" to "Sega Pico",
        "saturn" to "Sega Saturn",
        "dreamcast" to "Sega Dreamcast",
        "naomi" to "Sega NAOMI / Atomiswave",
        "arcade" to "Arcade (FBNeo / MAME)",
        "neogeo" to "SNK Neo Geo MVS / AES",
        "neogeocd" to "SNK Neo Geo CD",
        "cps" to "Capcom CPS-1 / CPS-2 / CPS-3",
        "pce" to "NEC PC Engine / TurboGrafx-16",
        "pcecd" to "NEC PC Engine CD / TurboGrafx-CD",
        "pcfx" to "NEC PC-FX",
        "ngp" to "SNK Neo Geo Pocket / Color",
        "wonderswan" to "Bandai WonderSwan / Color",
        "3do" to "Panasonic 3DO",
        "cdi" to "Philips CD-i",
        "vectrex" to "GCE Vectrex",
        "coleco" to "ColecoVision",
        "intellivision" to "Mattel Intellivision",
        "odyssey2" to "Magnavox Odyssey 2",
        "atari2600" to "Atari 2600",
        "atari5200" to "Atari 5200",
        "atari7800" to "Atari 7800",
        "atari800" to "Atari 8-Bit (800/XL/XE)",
        "lynx" to "Atari Lynx",
        "jaguar" to "Atari Jaguar",
        "atarist" to "Atari ST / STE",
        "dos" to "MS-DOS",
        "scummvm" to "ScummVM",
        "amiga" to "Commodore Amiga",
        "zxspectrum" to "Sinclair ZX Spectrum",
        "c64" to "Commodore 64",
        "msx" to "MSX / MSX2",
        "x68000" to "Sharp X68000",
        "pc98" to "NEC PC-98",
        "pc88" to "NEC PC-88",
        "amstrad" to "Amstrad CPC",
        "bk0010" to "Электроника БК-0010",
        "pico8" to "PICO-8",
        "tic80" to "TIC-80",
        "doom" to "DOOM Engine"
    )

    fun getPlatformDisplayName(platformKey: String): String {
        return PLATFORM_DISPLAY_NAMES[platformKey.lowercase(Locale.ROOT)] ?: platformKey.uppercase(Locale.ROOT)
    }

    fun findCoreById(idOrBaseName: String?): MobileCoreInfo? {
        if (idOrBaseName.isNullOrBlank()) return null
        val q = idOrBaseName.trim().lowercase(Locale.ROOT)
        return ALL_CORES.firstOrNull {
            it.id.lowercase(Locale.ROOT) == q ||
                it.soBaseName.lowercase(Locale.ROOT) == q ||
                "${it.soBaseName}_android.so".lowercase(Locale.ROOT) == q ||
                it.displayName.lowercase(Locale.ROOT) == q
        }
    }

    /**
     * Exact Desktop 2.3.0 / 2.4.0 platform key normalizer.
     * Enforces strict hardware isolation:
     * NES != SNES != N64 != GB/GBC != GBA != NDS != 3DS
     * PS1 != PS2 != PS3 != PSP != PSVita
     * Genesis != Mega-CD != 32X != SMS/GG != Saturn != Dreamcast
     */
    fun normalizePlatformKey(platform: String?, ext: String = ""): String {
        val p = (platform ?: "").trim().lowercase(Locale.ROOT)
        val e = ext.trim().trimStart('.').lowercase(Locale.ROOT)

        if (p.isNotEmpty()) {
            if (p == "all" || p == "все платформы" || p == "all platforms") return ""

            // Exact normalized keys fast-path
            if (PLATFORM_DISPLAY_NAMES.containsKey(p)) return p

            // Nintendo handhelds (specific before general)
            if (p.contains("game boy advance") || p == "gba" || p.contains("(gba)")) return "gba"
            if (p.contains("virtual boy") || p == "vb") return "vb"
            if (p.contains("pokemon mini") || p.contains("pokémon mini") || p == "pokemini") return "pokemini"
            if (p.contains("game & watch") || p.contains("game and watch") || p == "gw") return "gw"
            if (p.contains("game boy color") || p == "gbc") return "gbc"
            if (p.contains("game boy") || p == "gb" || p.contains("gb/gbc") || p.contains("gb_gbc")) return "gb_gbc"
            if (p.contains("3ds")) return "3ds"
            if (p.contains("nintendo ds") || p == "nds" || p == "dsi" || p.contains("ds / dsi")) return "nds"

            // Nintendo home consoles (specific before general)
            if (p.contains("super nintendo") || p.contains("snes") || p.contains("super famicom") || p == "sfc") return "snes"
            if (p.contains("famicom disk") || p == "fds") return "fds"
            if (p.contains("nintendo 64") || p == "n64" || p.contains("64dd")) return "n64"
            if (p.contains("gamecube") && p.contains("wii")) return "gamecube_wii"
            if (p.contains("gamecube") || p == "gc" || p == "ngc") return "gamecube"
            if (p == "wii" || p.contains("nintendo wii")) return "wii"
            if (p.contains("nintendo entertainment system") || p.contains("nes") || p.contains("famicom") || p.contains("dendy")) return "nes"

            // Sony (specific before general)
            if (p.contains("vita") || p == "psv" || p == "psvita") return "psvita"
            if (p.contains("portable") || p.contains("psp")) return "psp"
            if (p.contains("playstation 3") || p == "ps3") return "ps3"
            if (p.contains("playstation 2") || p == "ps2" || p.contains("pcsx2") || p.contains("lrps2")) return "ps2"
            if (p.contains("playstation") || p == "ps1" || p == "psx") return "ps1"

            // Sega (specific before general)
            if (p.contains("naomi") || p.contains("atomiswave")) return "naomi"
            if (p.contains("dreamcast") || p == "dc") return "dreamcast"
            if (p.contains("saturn")) return "saturn"
            if (p.contains("32x")) return "sega32x"
            if (p.contains("mega-cd") || p.contains("sega cd") || p == "mcd" || p == "scd") return "segacd"
            if (p.contains("master system") || p.contains("mark iii") || p == "sms" || p.contains("game gear") || p == "gg") return "sms_gg"
            if (p.contains("sg-1000") || p == "sg1000") return "sg1000"
            if (p.contains("pico") && p.contains("sega")) return "segapico"
            if (p.contains("genesis") || p.contains("mega drive") || p == "md" || p == "smd") return "genesis"

            // NEC, SNK, Bandai, 3DO, Philips, Atari, Computers
            if (p.contains("pc-fx") || p == "pcfx") return "pcfx"
            if (p.contains("pc-98") || p == "pc98") return "pc98"
            if (p.contains("pc-88") || p == "pc88") return "pc88"
            if (p.contains("pc engine cd") || p.contains("turbografx-cd")) return "pcecd"
            if (p.contains("pc engine") || p.contains("turbografx") || p.contains("supergrafx") || p == "pce" || p == "tg16") return "pce"
            if (p.contains("neo geo pocket") || p == "ngp" || p == "ngpc") return "ngp"
            if (p.contains("neo geo cd") || p == "neocd") return "neogeocd"
            if (p.contains("neo geo") || p == "neogeo" || p.contains("mvs") || p.contains("aes")) return "neogeo"
            if (p.contains("cps-1") || p.contains("cps-2") || p.contains("cps-3") || p == "cps") return "cps"
            if (p.contains("arcade") || p.contains("mame") || p.contains("fbneo")) return "arcade"
            if (p.contains("wonderswan") || p == "ws" || p == "wsc") return "wonderswan"
            if (p.contains("3do")) return "3do"
            if (p.contains("cd-i") || p == "cdi") return "cdi"
            if (p.contains("vectrex")) return "vectrex"
            if (p.contains("coleco")) return "coleco"
            if (p.contains("intellivision")) return "intellivision"
            if (p.contains("odyssey") || p.contains("videopac")) return "odyssey2"
            if (p.contains("2600")) return "atari2600"
            if (p.contains("5200")) return "atari5200"
            if (p.contains("7800")) return "atari7800"
            if (p.contains("atari 8") || p.contains("800xl") || p.contains("atari800")) return "atari800"
            if (p.contains("lynx")) return "lynx"
            if (p.contains("jaguar")) return "jaguar"
            if (p.contains("atari st") || p == "st") return "atarist"
            if (p.contains("dos") || p.contains("ms-dos")) return "dos"
            if (p.contains("scummvm")) return "scummvm"
            if (p.contains("amiga") || p.contains("cd32")) return "amiga"
            if (p.contains("spectrum") || p == "zx") return "zxspectrum"
            if (p.contains("commodore 64") || p == "c64") return "c64"
            if (p.contains("msx")) return "msx"
            if (p.contains("x68000") || p == "x68k") return "x68000"
            if (p.contains("amstrad") || p == "cpc") return "amstrad"
            if (p.contains("бк-0010") || p.contains("bk-0010") || p == "bk") return "bk0010"
            if (p.contains("pico-8") || p == "pico8") return "pico8"
            if (p.contains("tic-80") || p == "tic80") return "tic80"
            if (p.contains("doom")) return "doom"
        }

        // Extension-based fallback when platform label is absent
        return when (e) {
            "nes", "unf", "unif" -> "nes"
            "fds" -> "fds"
            "sfc", "smc", "swc", "fig", "bs" -> "snes"
            "z64", "n64", "v64", "ndd" -> "n64"
            "rvz", "gcm", "gcz", "dol" -> "gamecube"
            "wbfs" -> "wii"
            "gb", "dmg" -> "gb"
            "gbc" -> "gbc"
            "gba", "agb" -> "gba"
            "nds", "dsi", "ids" -> "nds"
            "3ds", "3dsx", "cci", "cxi" -> "3ds"
            "vb", "vboy" -> "vb"
            "min" -> "pokemini"
            "mgw" -> "gw"
            "pbp" -> "ps1"
            "cso", "isz" -> "psp"
            "vpk" -> "psvita"
            "md", "gen", "smd" -> "genesis"
            "32x" -> "sega32x"
            "sms", "gg", "sg" -> "sms_gg"
            "gdi", "cdi" -> "dreamcast"
            "pce", "sgx" -> "pce"
            "ngp", "ngc", "ngpc" -> "ngp"
            "ws", "wsc", "pc2" -> "wonderswan"
            "vec" -> "vectrex"
            "col", "cv" -> "coleco"
            "int", "itv" -> "intellivision"
            "a26" -> "atari2600"
            "a52" -> "atari5200"
            "a78" -> "atari7800"
            "atr", "xex", "xfd", "car" -> "atari800"
            "lnx", "lyx" -> "lynx"
            "j64", "jag" -> "jaguar"
            "st", "msa", "stx" -> "atarist"
            "dosz", "exe", "com", "bat", "conf" -> "dos"
            "scummvm" -> "scummvm"
            "adf", "adz", "dms", "lha", "hdf", "uae" -> "amiga"
            "tzx", "tap", "z80", "sna", "trd", "scl" -> "zxspectrum"
            "d64", "t64", "prg", "crt", "g64" -> "c64"
            "mx1", "mx2" -> "msx"
            "xdf", "hdm", "2hd" -> "x68000"
            "d98", "fdi", "hdi", "nhd" -> "pc98"
            "d88", "t88" -> "pc88"
            "cpc", "cdt" -> "amstrad"
            "bkd" -> "bk0010"
            "p8" -> "pico8"
            "tic" -> "tic80"
            "wad", "iwad", "pwad" -> "doom"
            else -> ""
        }
    }

    /**
     * Strict platform-scoped core resolver.
     * NEVER returns unrelated fallback cores: e.g., NES only returns NES cores, PS1 only returns PS1 cores.
     */
    fun getCompatibleCoresForPlatform(platform: String?, ext: String = ""): List<MobileCoreInfo> {
        val key = normalizePlatformKey(platform, ext)
        if (key.isEmpty()) {
            val cleanExt = ext.trim().trimStart('.').lowercase(Locale.ROOT)
            if (cleanExt.isEmpty()) return ALL_CORES
            return ALL_CORES.filter { it.extensions.contains(cleanExt) }
        }

        val expandedKeys = when (key) {
            "gb", "gbc", "gb_gbc" -> setOf(key, "gb_gbc")
            "gamecube", "wii", "gamecube_wii" -> setOf(key, "gamecube_wii")
            else -> setOf(key)
        }

        return ALL_CORES.filter { core ->
            core.platformKeys.any { expandedKeys.contains(it) }
        }
    }

    fun getDefaultCoreForPlatform(platform: String?, ext: String = ""): MobileCoreInfo? {
        val compat = getCompatibleCoresForPlatform(platform, ext)
        return compat.firstOrNull { !it.isExperimental } ?: compat.firstOrNull()
    }

    fun getAllUniqueExtensions(): Set<String> {
        val set = LinkedHashSet<String>()
        for (core in ALL_CORES) {
            set.addAll(core.extensions)
        }
        // Standard multi-disc & archive containers
        set.addAll(listOf("m3u", "cue", "gdi", "chd", "iso", "bin", "img", "mdf", "zip", "7z"))
        return set
    }

    // === SELinux-Safe Core Storage & Official Libretro Buildbot Downloader ===

    fun getExecutableCoresDir(context: Context): File {
        val dir = File(context.filesDir, "cores")
        if (!dir.exists()) dir.mkdirs()
        return dir
    }

    fun getPublicCoresDir(): File {
        val dir = File(STORM_PUBLIC_ROOT, "cores")
        try {
            if (!dir.exists()) dir.mkdirs()
        } catch (_: Throwable) {
        }
        return dir
    }

    /**
     * Imports any user-placed `.so` files from `/storage/emulated/0/STORM RETROARCH/cores/`
     * into SELinux `app_data_file` `context.filesDir/cores/` and marks them readable + executable
     * so Android 10-16 W^X permits `dlopen()`.
     */
    fun syncPublicCoresToExecutableDir(context: Context): Int {
        val execDir = getExecutableCoresDir(context)
        val pubDir = getPublicCoresDir()
        var importedCount = 0
        try {
            if (pubDir.exists() && pubDir.canRead()) {
                val externalFiles = pubDir.listFiles { f -> f.isFile && f.name.endsWith(".so", ignoreCase = true) }
                if (externalFiles != null) {
                    for (src in externalFiles) {
                        val dst = File(execDir, src.name)
                        if (!dst.exists() || dst.length() != src.length()) {
                            copyFileAtomic(src, dst)
                            applyExecutablePermissions(dst)
                            importedCount++
                        } else {
                            applyExecutablePermissions(dst)
                        }
                    }
                }
            }
        } catch (_: Throwable) {
        }
        return importedCount
    }

    private fun applyExecutablePermissions(soFile: File) {
        try {
            soFile.setReadable(true, false)
            soFile.setExecutable(true, false)
        } catch (_: Throwable) {
            try {
                soFile.setReadable(true)
                soFile.setExecutable(true)
            } catch (_: Throwable) {
            }
        }
    }

    private fun copyFileAtomic(src: File, dst: File) {
        dst.parentFile?.mkdirs()
        val tmp = File(dst.parentFile, "${dst.name}.tmp")
        FileInputStream(src).use { input ->
            FileOutputStream(tmp).use { output ->
                val buf = ByteArray(32 * 1024)
                while (true) {
                    val read = input.read(buf)
                    if (read <= 0) break
                    output.write(buf, 0, read)
                }
                output.flush()
            }
        }
        if (dst.exists()) dst.delete()
        tmp.renameTo(dst)
    }

    fun resolveInstalledCoreFile(context: Context, core: MobileCoreInfo): File? {
        val execDir = getExecutableCoresDir(context)
        val candidates = listOf(
            "${core.soBaseName}_android.so",
            "${core.soBaseName}.so",
            "lib${core.soBaseName}_android.so",
            "lib${core.soBaseName}.so"
        )
        for (name in candidates) {
            val f = File(execDir, name)
            if (f.exists() && f.length() > 4096L) {
                applyExecutablePermissions(f)
                return f
            }
        }

        // Check public mirror and import on demand if found
        val pubDir = getPublicCoresDir()
        for (name in candidates) {
            val extFile = File(pubDir, name)
            if (extFile.exists() && extFile.length() > 4096L) {
                return try {
                    val dst = File(execDir, "${core.soBaseName}_android.so")
                    copyFileAtomic(extFile, dst)
                    applyExecutablePermissions(dst)
                    dst
                } catch (_: Throwable) {
                    null
                }
            }
        }
        return null
    }

    fun isCoreInstalled(context: Context, core: MobileCoreInfo): Boolean {
        return resolveInstalledCoreFile(context, core) != null
    }

    fun getInstalledCoreSizeFormatted(context: Context, core: MobileCoreInfo): String {
        val f = resolveInstalledCoreFile(context, core) ?: return ""
        val mb = f.length().toDouble() / (1024.0 * 1024.0)
        return String.format(Locale.US, "%.2f MB", mb)
    }

    fun detectDeviceAbi(): String {
        val supported = Build.SUPPORTED_ABIS ?: emptyArray()
        for (abi in supported) {
            when (abi) {
                "arm64-v8a", "armeabi-v7a", "x86_64", "x86" -> return abi
            }
        }
        return "arm64-v8a"
    }

    /**
     * Downloads the official Libretro Android `.so.zip` core from buildbot.libretro.com
     * matching the device ABI, extracts it into `context.filesDir/cores` with `r-x` permissions,
     * and mirrors a backup copy into `/storage/emulated/0/STORM RETROARCH/cores/`.
     */
    fun downloadCoreAsync(
        context: Context,
        core: MobileCoreInfo,
        onProgress: (Int, String) -> Unit,
        onDone: (Boolean, File?, String) -> Unit
    ) {
        val appCtx = context.applicationContext
        downloadExecutor.execute {
            val abi = detectDeviceAbi()
            val urlsToTry = listOf(
                "$BUILDBOT_ANDROID_BASE/$abi/${core.soBaseName}_android.so.zip",
                "$BUILDBOT_ANDROID_BASE/$abi/${core.soBaseName}.so.zip"
            )

            val execDir = getExecutableCoresDir(appCtx)
            val tempZip = File(appCtx.cacheDir, "${core.soBaseName}_${abi}.zip.tmp")
            var downloadedOk = false
            var lastError = "HTTP 404"

            postProgress(onProgress, 2, "${StormLocalization.tr("core_downloading")} ($abi)")

            for (candidateUrl in urlsToTry) {
                var conn: HttpURLConnection? = null
                try {
                    conn = openFollowingRedirects(candidateUrl)
                    val code = conn.responseCode
                    if (code in 200..299) {
                        val totalBytes = conn.contentLengthLong
                        BufferedInputStream(conn.inputStream, 32 * 1024).use { input ->
                            FileOutputStream(tempZip).use { output ->
                                val buf = ByteArray(32 * 1024)
                                var received = 0L
                                var lastPct = 2
                                while (true) {
                                    val n = input.read(buf)
                                    if (n <= 0) break
                                    output.write(buf, 0, n)
                                    received += n
                                    if (totalBytes > 0L) {
                                        val pct = (5 + ((received * 75L) / totalBytes).toInt()).coerceIn(5, 80)
                                        if (pct >= lastPct + 4) {
                                            lastPct = pct
                                            postProgress(onProgress, pct, "${core.displayName}: $pct%")
                                        }
                                    }
                                }
                                output.flush()
                            }
                        }
                        if (tempZip.exists() && tempZip.length() > 1024L) {
                            downloadedOk = true
                            break
                        }
                    } else {
                        lastError = "HTTP $code ($abi)"
                    }
                } catch (e: Throwable) {
                    lastError = e.message ?: "Network error"
                } finally {
                    try {
                        conn?.disconnect()
                    } catch (_: Throwable) {
                    }
                }
            }

            if (!downloadedOk) {
                try { tempZip.delete() } catch (_: Throwable) {}
                postDone(onDone, false, null, "${StormLocalization.tr("core_failed")}: $lastError")
                return@execute
            }

            postProgress(onProgress, 85, StormLocalization.tr("core_extracting"))

            try {
                val targetExecSo = File(execDir, "${core.soBaseName}_android.so")
                val tempSo = File(execDir, "${core.soBaseName}_android.so.extracting")
                var extractedEntryFound = false

                ZipInputStream(BufferedInputStream(FileInputStream(tempZip), 32 * 1024)).use { zis ->
                    while (true) {
                        val entry = zis.nextEntry ?: break
                        if (!entry.isDirectory && entry.name.endsWith(".so", ignoreCase = true)) {
                            FileOutputStream(tempSo).use { out ->
                                val buf = ByteArray(32 * 1024)
                                while (true) {
                                    val r = zis.read(buf)
                                    if (r <= 0) break
                                    out.write(buf, 0, r)
                                }
                                out.flush()
                            }
                            extractedEntryFound = true
                            zis.closeEntry()
                            break
                        }
                        zis.closeEntry()
                    }
                }

                try { tempZip.delete() } catch (_: Throwable) {}

                if (!extractedEntryFound || !tempSo.exists() || tempSo.length() < 4096L) {
                    try { tempSo.delete() } catch (_: Throwable) {}
                    postDone(onDone, false, null, "${StormLocalization.tr("core_failed")}: Invalid .so archive")
                    return@execute
                }

                if (targetExecSo.exists()) targetExecSo.delete()
                tempSo.renameTo(targetExecSo)
                applyExecutablePermissions(targetExecSo)

                // Mirror copy to /storage/emulated/0/STORM RETROARCH/cores/
                try {
                    val pubDir = getPublicCoresDir()
                    val mirrorSo = File(pubDir, targetExecSo.name)
                    copyFileAtomic(targetExecSo, mirrorSo)
                } catch (_: Throwable) {
                }

                postProgress(onProgress, 100, StormLocalization.tr("core_ready"))
                postDone(onDone, true, targetExecSo, "${core.displayName} (${getInstalledCoreSizeFormatted(appCtx, core)})")
            } catch (e: Throwable) {
                try { tempZip.delete() } catch (_: Throwable) {}
                postDone(onDone, false, null, "${StormLocalization.tr("core_failed")}: ${e.message ?: "Extract error"}")
            }
        }
    }

    internal fun openFollowingRedirects(initialUrl: String, maxRedirects: Int = 5): HttpURLConnection {
        var currentUrl = initialUrl
        var redirects = 0
        while (true) {
            val conn = (URL(currentUrl).openConnection() as HttpURLConnection).apply {
                instanceFollowRedirects = true
                connectTimeout = 15_000
                readTimeout = 45_000
                requestMethod = "GET"
                setRequestProperty("User-Agent", USER_AGENT)
                setRequestProperty("Accept", "*/*")
            }
            val status = conn.responseCode
            if (status in 301..308 && redirects < maxRedirects) {
                val loc = conn.getHeaderField("Location")
                conn.disconnect()
                if (!loc.isNullOrBlank()) {
                    currentUrl = URL(URL(currentUrl), loc).toString()
                    redirects++
                    continue
                }
            }
            return conn
        }
    }

    private fun postProgress(cb: (Int, String) -> Unit, pct: Int, msg: String) {
        mainHandler.post { cb(pct, msg) }
    }

    private fun postDone(cb: (Boolean, File?, String) -> Unit, ok: Boolean, file: File?, msg: String) {
        mainHandler.post { cb(ok, file, msg) }
    }
}
