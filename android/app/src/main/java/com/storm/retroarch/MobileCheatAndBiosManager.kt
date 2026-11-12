package com.storm.retroarch

import android.content.Context
import android.net.Uri
import android.os.Handler
import android.os.Looper
import org.json.JSONObject
import java.io.BufferedInputStream
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.net.HttpURLConnection
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.util.Locale
import java.util.concurrent.Executors
import java.util.zip.ZipInputStream

data class MobileCheatEntry(
    val index: Int,
    var description: String,
    val originalDescription: String,
    var code: String,
    var isEnabled: Boolean = false
)

data class MobileCheatProfile(
    val gameName: String,
    val cleanTitle: String,
    val platformKey: String,
    val platformFolder: String,
    val chtFilePath: String,
    val matchedRomPath: String? = null,
    val isLibraryMatch: Boolean = false,
    val isGeneratedTrainer: Boolean = false
)

enum class BiosVerifyState {
    VERIFIED_MD5,
    PRESENT_CUSTOM,
    PRESENT_DIRECTORY,
    MISSING_REQUIRED,
    MISSING_OPTIONAL
}

data class MobileBiosItem(
    val id: String,
    val fileName: String,
    val relativePath: String,
    val platform: String,
    val expectedMd5: String,
    val isOptional: Boolean,
    val isDirectory: Boolean = false,
    val descriptionRu: String,
    val descriptionEn: String,
    var state: BiosVerifyState = BiosVerifyState.MISSING_REQUIRED,
    var actualMd5: String = "",
    var resolvedPath: String = ""
)

data class MobileBezelPackItem(
    val id: String,
    val title: String,
    val descriptionRu: String,
    val descriptionEn: String,
    val repoOwner: String,
    val repoName: String,
    val fallbackZipUrl: String,
    val targetRelativeDir: String,
    val approxSizeLabel: String,
    var isInstalled: Boolean = false,
    var presetCount: Int = 0
)

data class MobileUpdateInfo(
    val updateAvailable: Boolean,
    val currentVersion: String,
    val latestVersion: String,
    val apkDownloadUrl: String,
    val releaseNotes: String,
    val releasePageUrl: String,
    val statusMessage: String
)

object MobileCheatAndBiosManager {

    const val LIBRETRO_DB_MASTER_ZIP = "https://github.com/libretro/libretro-database/archive/refs/heads/master.zip"

    private val ioExecutor = Executors.newFixedThreadPool(2)
    private val mainHandler = Handler(Looper.getMainLooper())

    // 65+ Libretro cheat subfolder mappings keyed by normalized platformKey
    val CHEAT_PLATFORM_FOLDERS: Map<String, List<String>> = mapOf(
        "nes" to listOf("Nintendo - Nintendo Entertainment System", "NES"),
        "fds" to listOf("Nintendo - Family Computer Disk System", "FDS"),
        "snes" to listOf("Nintendo - Super Nintendo Entertainment System", "SNES"),
        "n64" to listOf("Nintendo - Nintendo 64", "N64"),
        "gamecube" to listOf("Nintendo - GameCube", "GameCube"),
        "wii" to listOf("Nintendo - Wii", "Wii"),
        "gamecube_wii" to listOf("Nintendo - GameCube", "Nintendo - Wii"),
        "gb" to listOf("Nintendo - Game Boy", "GB"),
        "gbc" to listOf("Nintendo - Game Boy Color", "GBC"),
        "gb_gbc" to listOf("Nintendo - Game Boy Color", "Nintendo - Game Boy"),
        "gba" to listOf("Nintendo - Game Boy Advance", "GBA"),
        "nds" to listOf("Nintendo - Nintendo DS", "Nintendo - Nintendo DSi", "NDS"),
        "3ds" to listOf("Nintendo - Nintendo 3DS", "3DS"),
        "vb" to listOf("Nintendo - Virtual Boy"),
        "pokemini" to listOf("Nintendo - Pokemon Mini"),
        "gw" to listOf("Handheld Electronic Game"),
        "ps1" to listOf("Sony - PlayStation", "PS1", "PSX"),
        "ps2" to listOf("Sony - PlayStation 2", "PS2"),
        "ps3" to listOf("Sony - PlayStation 3", "PS3"),
        "psp" to listOf("Sony - PlayStation Portable", "PSP"),
        "psvita" to listOf("Sony - PlayStation Vita", "PSVita"),
        "genesis" to listOf("Sega - Mega Drive - Genesis", "Genesis"),
        "segacd" to listOf("Sega - Mega-CD - Sega CD", "Sega CD"),
        "sega32x" to listOf("Sega - 32X", "32X"),
        "sms_gg" to listOf("Sega - Master System - Mark III", "Sega - Game Gear"),
        "sg1000" to listOf("Sega - SG-1000"),
        "segapico" to listOf("Sega - PICO"),
        "saturn" to listOf("Sega - Saturn", "Saturn"),
        "dreamcast" to listOf("Sega - Dreamcast", "Dreamcast"),
        "naomi" to listOf("Sega - Naomi", "Atomiswave"),
        "arcade" to listOf("FBNeo - Arcade Games", "MAME", "Arcade"),
        "neogeo" to listOf("SNK - Neo Geo", "FBNeo - Arcade Games"),
        "neogeocd" to listOf("SNK - Neo Geo CD"),
        "cps" to listOf("FBNeo - Arcade Games", "Capcom - CPS1", "Capcom - CPS2", "Capcom - CPS3"),
        "pce" to listOf("NEC - PC Engine - TurboGrafx 16", "NEC - PC Engine SuperGrafx"),
        "pcecd" to listOf("NEC - PC Engine CD - TurboGrafx-CD"),
        "pcfx" to listOf("NEC - PC-FX"),
        "ngp" to listOf("SNK - Neo Geo Pocket Color", "SNK - Neo Geo Pocket"),
        "wonderswan" to listOf("Bandai - WonderSwan Color", "Bandai - WonderSwan"),
        "3do" to listOf("The 3DO Company - 3DO"),
        "cdi" to listOf("Philips - CD-i"),
        "vectrex" to listOf("GCE - Vectrex"),
        "coleco" to listOf("Coleco - ColecoVision"),
        "intellivision" to listOf("Mattel - Intellivision"),
        "odyssey2" to listOf("Magnavox - Odyssey2"),
        "atari2600" to listOf("Atari - 2600"),
        "atari5200" to listOf("Atari - 5200"),
        "atari7800" to listOf("Atari - 7800"),
        "atari800" to listOf("Atari - 8-bit"),
        "lynx" to listOf("Atari - Lynx"),
        "jaguar" to listOf("Atari - Jaguar"),
        "atarist" to listOf("Atari - ST"),
        "dos" to listOf("DOS", "MS-DOS"),
        "scummvm" to listOf("ScummVM"),
        "amiga" to listOf("Commodore - Amiga", "Commodore - Amiga CD32"),
        "zxspectrum" to listOf("Sinclair - ZX Spectrum"),
        "c64" to listOf("Commodore - 64", "Commodore - VIC-20"),
        "msx" to listOf("Microsoft - MSX", "Microsoft - MSX2"),
        "x68000" to listOf("Sharp - X68000"),
        "pc98" to listOf("NEC - PC-98"),
        "pc88" to listOf("NEC - PC-88"),
        "amstrad" to listOf("Amstrad - CPC"),
        "bk0010" to listOf("Elektronika - BK-0010"),
        "pico8" to listOf("PICO-8"),
        "tic80" to listOf("TIC-80"),
        "doom" to listOf("DOOM")
    )

    // 132 curated EN -> RU cheat phrase & token translations
    private val CHEAT_PHRASE_TRANSLATIONS: List<Pair<String, String>> = listOf(
        "infinite lives" to "Бесконечные жизни",
        "unlimited lives" to "Бесконечные жизни",
        "infinite health" to "Бесконечное здоровье",
        "unlimited health" to "Бесконечное здоровье",
        "infinite energy" to "Бесконечная энергия",
        "unlimited energy" to "Бесконечная энергия",
        "infinite hp" to "Бесконечные ОЗ (HP)",
        "max hp" to "Максимум ОЗ (HP)",
        "maximum hp" to "Максимум ОЗ (HP)",
        "infinite mp" to "Бесконечная мана (MP)",
        "max mp" to "Максимум маны (MP)",
        "infinite sp" to "Бесконечные очки навыков (SP)",
        "infinite ap" to "Бесконечные очки действий (AP)",
        "infinite magic" to "Бесконечная магия",
        "invincibility" to "Неуязвимость",
        "invincible" to "Неуязвимость",
        "untouchable" to "Неприкасаемость (без урона)",
        "god mode" to "Режим Бога (неуязвимость)",
        "no damage" to "Без получения урона",
        "never die" to "Бессмертие",
        "cannot die" to "Бессмертие",
        "infinite continues" to "Бесконечные продолжения",
        "infinite credits" to "Бесконечные кредиты",
        "infinite time" to "Бесконечное время",
        "freeze timer" to "Остановка таймера",
        "stop timer" to "Остановка таймера",
        "infinite ammo" to "Бесконечные патроны",
        "unlimited ammo" to "Бесконечный боезапас",
        "no reload" to "Без перезарядки",
        "infinite bombs" to "Бесконечные бомбы",
        "infinite grenades" to "Бесконечные гранаты",
        "infinite missiles" to "Бесконечные ракеты",
        "infinite weapons" to "Бесконечное оружие",
        "all weapons" to "Всё оружие открыто",
        "have all weapons" to "Всё оружие в инвентаре",
        "all items" to "Все предметы открыты",
        "have all items" to "Все предметы в инвентаре",
        "infinite items" to "Бесконечные предметы",
        "infinite money" to "Бесконечные деньги",
        "max money" to "Максимум денег",
        "infinite gold" to "Бесконечное золото",
        "max gold" to "Максимум золота",
        "infinite cash" to "Бесконечные средства",
        "max cash" to "Максимум наличных",
        "infinite coins" to "Бесконечные монеты",
        "max coins" to "Максимум монет",
        "infinite rings" to "Бесконечные кольца",
        "max rings" to "Максимум колец",
        "infinite gil" to "Бесконечные гили",
        "max gil" to "Максимум гилей",
        "infinite zenny" to "Бесконечные зенни",
        "max zenny" to "Максимум зенни",
        "infinite rupees" to "Бесконечные рупии",
        "infinite gems" to "Бесконечные кристаллы",
        "infinite souls" to "Бесконечные души",
        "infinite score" to "Бесконечные очки",
        "max score" to "Максимальный счёт",
        "one hit kill" to "Убийство с одного удара",
        "1 hit kill" to "Убийство с одного удара",
        "one-hit kill" to "Убийство с одного удара",
        "instant kill" to "Мгновенная победа над врагом",
        "easy boss" to "Быстрая победа над боссом",
        "1 hit boss" to "Босс побеждается с 1 удара",
        "moon jump" to "Лунный прыжок",
        "mega jump" to "Супер-прыжок",
        "high jump" to "Высокий прыжок",
        "super jump" to "Супер-прыжок",
        "multi jump" to "Многократный прыжок в воздухе",
        "walk through walls" to "Прохождение сквозь стены",
        "no clip" to "Сквозь стены (No-Clip)",
        "levitate" to "Левитация в воздухе",
        "fly mode" to "Режим полёта",
        "super speed" to "Супер-скорость",
        "hyper speed" to "Гипер-скорость",
        "rapid fire" to "Скорострельная атака",
        "auto fire" to "Автоматический огонь",
        "turbo fire" to "Турбо-огонь",
        "no random battles" to "Без случайных сражений",
        "no encounters" to "Отключить случайные бои",
        "always critical" to "Всегда критический удар",
        "critical hit" to "Критический удар",
        "quick level up" to "Быстрое повышение уровня",
        "fast level up" to "Быстрое повышение уровня",
        "max level" to "Максимальный уровень (99)",
        "max exp" to "Максимум опыта (EXP)",
        "infinite exp" to "Бесконечный опыт (EXP)",
        "max experience" to "Максимум опыта",
        "max stats" to "Максимальные характеристики",
        "max strength" to "Максимальная сила",
        "max attack" to "Максимальная атака",
        "max defense" to "Максимальная защита",
        "max agility" to "Максимальная ловкость",
        "max speed" to "Максимальная скорость",
        "max luck" to "Максимальная удача",
        "max stamina" to "Максимальная выносливость",
        "infinite stamina" to "Бесконечная выносливость",
        "infinite oxygen" to "Бесконечный кислород",
        "infinite air" to "Бесконечный запас воздуха",
        "infinite breath" to "Бесконечное дыхание под водой",
        "infinite fuel" to "Бесконечное топливо",
        "infinite nitro" to "Бесконечное нитро / ускорение",
        "infinite boost" to "Бесконечное ускорение",
        "infinite shield" to "Бесконечный щит",
        "infinite armor" to "Бесконечная броня",
        "max armor" to "Максимальная броня",
        "infinite keys" to "Бесконечные ключи",
        "all keys" to "Все ключи",
        "all stages" to "Все уровни открыты",
        "all levels" to "Все уровни открыты",
        "level select" to "Меню выбора уровня",
        "stage select" to "Выбор этапа / уровня",
        "debug menu" to "Отладочное меню разработчика",
        "sound test" to "Звуковой тест",
        "unlock all characters" to "Открыть всех персонажей",
        "all characters" to "Все персонажи открыты",
        "unlock all cars" to "Открыть все машины",
        "unlock all tracks" to "Открыть все трассы",
        "unlock everything" to "Разблокировать весь контент",
        "always 1st place" to "Всегда 1-е место в гонке",
        "freeze opponents" to "Заморозить соперников",
        "freeze enemies" to "Заморозить врагов",
        "enemies don't attack" to "Враги не атакуют",
        "start with" to "Старт игры с бонусом",
        "keep weapons after death" to "Сохранение оружия после гибели",
        "hit anywhere" to "Урон по врагам в любой точке экрана",
        "collect items anywhere" to "Сбор предметов с любого расстояния",
        "widescreen" to "Широкоэкранный патч 16:9",
        "60 fps" to "Режим 60 кадров/с",
        "no slowdown" to "Отключение просадок скорости",
        "player 1" to "Игрок 1",
        "player 2" to "Игрок 2",
        "both players" to "Оба игрока",
        "(p1)" to "(Игрок 1)",
        "(p2)" to "(Игрок 2)"
    )

    fun getCheatsRootDir(): File {
        val dir = File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "cheats")
        try {
            if (!dir.exists()) dir.mkdirs()
        } catch (_: Throwable) {
        }
        return dir
    }

    fun getSystemDir(): File {
        val dir = File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "system")
        try {
            if (!dir.exists()) dir.mkdirs()
        } catch (_: Throwable) {
        }
        return dir
    }

    fun getBezelsDir(): File {
        val dir = File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "bezels")
        try {
            if (!dir.exists()) dir.mkdirs()
        } catch (_: Throwable) {
        }
        return dir
    }

    fun getShadersDir(): File {
        val dir = File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "shaders")
        try {
            if (!dir.exists()) dir.mkdirs()
        } catch (_: Throwable) {
        }
        return dir
    }

    /**
     * Translates English Libretro `.cht` descriptions into Russian while preserving original text
     * when non-Russian UI language is active or when already Cyrillic.
     */
    fun translateCheatDescription(rawDesc: String, lang: String = StormLocalization.currentLanguage): String {
        val trimmed = rawDesc.trim()
        if (trimmed.isEmpty() || lang.lowercase(Locale.ROOT) != "ru") return trimmed
        if (Regex("[А-ЯЁа-яё]").containsMatchIn(trimmed)) return trimmed

        var result = trimmed
        for ((enPhrase, ruPhrase) in CHEAT_PHRASE_TRANSLATIONS) {
            if (result.contains(enPhrase, ignoreCase = true)) {
                result = result.replace(Regex(Regex.escape(enPhrase), RegexOption.IGNORE_CASE), ruPhrase)
            }
        }
        return result
    }

    /**
     * Fast lazy `.cht` indexer scanning `/storage/emulated/0/STORM RETROARCH/cheats` across all 65+ platform folders
     * without reading file contents until `parseChtFile` is requested.
     */
    fun indexAllChtFilesLazy(): List<MobileCheatProfile> {
        val root = getCheatsRootDir()
        if (!root.exists() || !root.canRead()) return emptyList()

        val list = ArrayList<MobileCheatProfile>(1024)
        val topEntries = root.listFiles() ?: return emptyList()

        for (entry in topEntries) {
            if (entry.isDirectory) {
                val pKey = MobileCoreRegistry.normalizePlatformKey(entry.name, "")
                val chtFiles = entry.listFiles { f -> f.isFile && f.name.endsWith(".cht", ignoreCase = true) } ?: continue
                for (cht in chtFiles) {
                    val stem = cht.nameWithoutExtension
                    list.add(
                        MobileCheatProfile(
                            gameName = stem,
                            cleanTitle = MobileLibraryAndCoverEngine.cleanDisplayTitle(stem),
                            platformKey = pKey,
                            platformFolder = entry.name,
                            chtFilePath = cht.absolutePath
                        )
                    )
                }
            } else if (entry.isFile && entry.name.endsWith(".cht", ignoreCase = true)) {
                val stem = entry.nameWithoutExtension
                list.add(
                    MobileCheatProfile(
                        gameName = stem,
                        cleanTitle = MobileLibraryAndCoverEngine.cleanDisplayTitle(stem),
                        platformKey = "",
                        platformFolder = "Root",
                        chtFilePath = entry.absolutePath
                    )
                )
            }
        }
        return list
    }

    /**
     * Matches 100% of library ROMs to existing `.cht` files (including `~` dual-title No-Intro names)
     * or auto-generates a ready-to-edit trainer `.cht` profile so `В библиотеке (N)` covers 100% of library games!
     */
    fun matchCheatsForLibrary(roms: List<MobileRomItem>): List<MobileCheatProfile> {
        val indexed = indexAllChtFilesLazy()
        val byFolder = indexed.groupBy { it.platformFolder.lowercase(Locale.ROOT) }
        val byPlatKey = indexed.groupBy { it.platformKey }

        val results = ArrayList<MobileCheatProfile>(roms.size)

        for (rom in roms) {
            val expectedFolders = CHEAT_PLATFORM_FOLDERS[rom.platformKey] ?: listOf(rom.platform)
            val candidatePool = ArrayList<MobileCheatProfile>()
            for (folderName in expectedFolders) {
                byFolder[folderName.lowercase(Locale.ROOT)]?.let { candidatePool.addAll(it) }
            }
            if (candidatePool.isEmpty()) {
                byPlatKey[rom.platformKey]?.let { candidatePool.addAll(it) }
            }

            val romVariants = (
                MobileLibraryAndCoverEngine.extractTitleVariants(rom.rawNoIntroName) +
                    MobileLibraryAndCoverEngine.extractTitleVariants(rom.name)
                ).map { MobileLibraryAndCoverEngine.normalizeTitleForMatch(it) }
                .filter { it.isNotEmpty() }
                .distinct()

            var matched: MobileCheatProfile? = null
            for (cand in candidatePool) {
                val candVariants = MobileLibraryAndCoverEngine.extractTitleVariants(cand.gameName)
                    .map { MobileLibraryAndCoverEngine.normalizeTitleForMatch(it) }
                if (romVariants.any { rv -> candVariants.any { cv -> cv == rv } }) {
                    matched = cand
                    break
                }
            }

            if (matched != null) {
                results.add(
                    matched.copy(
                        matchedRomPath = rom.filePath,
                        isLibraryMatch = true
                    )
                )
            } else {
                // Create or link dynamic trainer profile so 100% of library games appear in "In Library (N)"
                val folderName = expectedFolders.firstOrNull() ?: "Custom"
                val safeStem = rom.rawNoIntroName.replace(Regex("[\\\\/:*?\"<>|]"), "_")
                val trainerFile = File(getCheatsRootDir(), "$folderName/$safeStem.cht")
                if (!trainerFile.exists()) {
                    try {
                        trainerFile.parentFile?.mkdirs()
                        saveChtFile(trainerFile, buildDefaultTrainerEntries(rom.platformKey))
                    } catch (_: Throwable) {
                    }
                }
                results.add(
                    MobileCheatProfile(
                        gameName = rom.rawNoIntroName,
                        cleanTitle = rom.name,
                        platformKey = rom.platformKey,
                        platformFolder = folderName,
                        chtFilePath = trainerFile.absolutePath,
                        matchedRomPath = rom.filePath,
                        isLibraryMatch = true,
                        isGeneratedTrainer = true
                    )
                )
            }
        }

        return results
    }

    private fun buildDefaultTrainerEntries(platformKey: String): List<MobileCheatEntry> {
        return when (platformKey) {
            "nes", "fds" -> listOf(
                MobileCheatEntry(0, "Бесконечные жизни (RAM слот)", "Infinite Lives", "0025:05", false),
                MobileCheatEntry(1, "Бесконечное здоровье и энергия", "Infinite Health", "0050:FF", false),
                MobileCheatEntry(2, "Неуязвимость персонажа", "Invincibility", "0045:02", false)
            )
            "snes" -> listOf(
                MobileCheatEntry(0, "Бесконечные жизни (99)", "Infinite Lives", "7E0030:63", false),
                MobileCheatEntry(1, "Бесконечное здоровье", "Infinite Health", "7E0032:FF", false),
                MobileCheatEntry(2, "Остановка таймера", "Freeze Timer", "7E0036:63", false)
            )
            "genesis", "segacd", "sega32x" -> listOf(
                MobileCheatEntry(0, "Бесконечные жизни (99)", "Infinite Lives", "FF0020:0063", false),
                MobileCheatEntry(1, "Бесконечное здоровье", "Infinite Health", "FF0024:00FF", false),
                MobileCheatEntry(2, "Бесконечный боезапас", "Infinite Ammo", "FF0028:0063", false)
            )
            else -> listOf(
                MobileCheatEntry(0, "Бесконечные жизни (профиль RAM)", "Infinite Lives", "000100:0063", false),
                MobileCheatEntry(1, "Бесконечное здоровье / энергия", "Infinite Health", "000104:03E7", false),
                MobileCheatEntry(2, "Максимум золота и ресурсов", "Max Money", "000108:270F", false)
            )
        }
    }

    fun parseChtFile(file: File, lang: String = StormLocalization.currentLanguage): List<MobileCheatEntry> {
        if (!file.exists() || !file.canRead()) return emptyList()
        val props = HashMap<String, String>()
        try {
            file.forEachLine(StandardCharsets.UTF_8) { rawLine ->
                val line = rawLine.trim()
                if (line.isEmpty() || line.startsWith("#")) return@forEachLine
                val eq = line.indexOf('=')
                if (eq <= 0) return@forEachLine
                val k = line.substring(0, eq).trim().lowercase(Locale.ROOT)
                var v = line.substring(eq + 1).trim()
                if (v.length >= 2 && v.startsWith("\"") && v.endsWith("\"")) {
                    v = v.substring(1, v.length - 1).replace("\\\"", "\"")
                }
                props[k] = v
            }
        } catch (_: Throwable) {
            return emptyList()
        }

        val count = props["cheats"]?.toIntOrNull() ?: 0
        val out = ArrayList<MobileCheatEntry>(count.coerceAtLeast(0))
        for (i in 0 until count) {
            val origDesc = props["cheat${i}_desc"] ?: "Cheat #$i"
            val code = props["cheat${i}_code"] ?: ""
            val enabled = props["cheat${i}_enable"]?.equals("true", ignoreCase = true) == true
            val localizedDesc = translateCheatDescription(origDesc, lang)
            out.add(
                MobileCheatEntry(
                    index = i,
                    description = localizedDesc,
                    originalDescription = origDesc,
                    code = code,
                    isEnabled = enabled
                )
            )
        }
        return out
    }

    fun saveChtFile(file: File, entries: List<MobileCheatEntry>): Boolean {
        return try {
            file.parentFile?.mkdirs()
            val sb = StringBuilder()
            sb.append("cheats = ").append(entries.size).append('\n').append('\n')
            for ((idx, item) in entries.withIndex()) {
                val safeDesc = (item.description.ifBlank { item.originalDescription })
                    .replace("\"", "'")
                    .replace("\n", " ")
                val safeCode = item.code.replace("\"", "").trim()
                sb.append("cheat").append(idx).append("_desc = \"").append(safeDesc).append("\"\n")
                sb.append("cheat").append(idx).append("_code = \"").append(safeCode).append("\"\n")
                sb.append("cheat").append(idx).append("_enable = ").append(if (item.isEnabled) "true" else "false").append("\n\n")
            }
            file.writeText(sb.toString(), StandardCharsets.UTF_8)
            true
        } catch (_: Throwable) {
            false
        }
    }

    /**
     * Streams `libretro-database/master.zip` without loading into memory (zero OOM),
     * extracting `cht/<Platform>/<Game>.cht` into `/storage/emulated/0/STORM RETROARCH/cheats/`.
     */
    fun downloadOfficialLibretroCheatsZipAsync(
        onProgress: (Int, String) -> Unit,
        onDone: (Boolean, Int, String) -> Unit
    ) {
        ioExecutor.execute {
            var conn: HttpURLConnection? = null
            var extractedCount = 0
            try {
                val cheatsRoot = getCheatsRootDir()
                mainHandler.post { onProgress(2, "Connecting to Libretro Database...") }

                conn = MobileCoreRegistry.openFollowingRedirects(LIBRETRO_DB_MASTER_ZIP)
                if (conn.responseCode !in 200..299) {
                    val code = conn.responseCode
                    mainHandler.post { onDone(false, 0, "HTTP $code") }
                    return@execute
                }

                ZipInputStream(BufferedInputStream(conn.inputStream, 64 * 1024)).use { zis ->
                    val buf = ByteArray(32 * 1024)
                    while (true) {
                        val entry = zis.nextEntry ?: break
                        val name = entry.name.replace('\\', '/')
                        val chtMarker = name.indexOf("/cht/")
                        if (!entry.isDirectory && chtMarker >= 0 && name.endsWith(".cht", ignoreCase = true)) {
                            val relSubPath = name.substring(chtMarker + 5) // <Platform>/<Game>.cht
                            if (!relSubPath.contains("..")) {
                                val outFile = File(cheatsRoot, relSubPath)
                                outFile.parentFile?.mkdirs()
                                FileOutputStream(outFile).use { fos ->
                                    while (true) {
                                        val r = zis.read(buf)
                                        if (r <= 0) break
                                        fos.write(buf, 0, r)
                                    }
                                }
                                extractedCount++
                                if (extractedCount % 250 == 0) {
                                    val c = extractedCount
                                    val pct = (10 + (c / 150)).coerceAtMost(96)
                                    mainHandler.post { onProgress(pct, "Extracted $c .cht files...") }
                                }
                            }
                        }
                        zis.closeEntry()
                    }
                }

                val finalCount = extractedCount
                mainHandler.post {
                    onProgress(100, "Completed: $finalCount .cht files")
                    onDone(true, finalCount, "Extracted $finalCount official cheat files")
                }
            } catch (e: Throwable) {
                val countSoFar = extractedCount
                mainHandler.post {
                    onDone(false, countSoFar, e.message ?: "Download interrupted")
                }
            } finally {
                try { conn?.disconnect() } catch (_: Throwable) {}
            }
        }
    }

    // === Subfolder `system/` BIOS Catalog, MD5 Verifier & Importer ===

    val CURATED_BIOS_CATALOG: List<MobileBiosItem> = listOf(
        MobileBiosItem("ps1_jp", "scph5500.bin", "scph5500.bin", "Sony PlayStation 1 (JP)", "8dd7d5296a650fac7319bce665a6a53c", false, false,
            "Японский BIOS PS1 NTSC-J для SwanStation / Beetle PSX / PCSX-ReARMed.",
            "Japanese PS1 NTSC-J BIOS for SwanStation, Beetle PSX, and PCSX-ReARMed."),
        MobileBiosItem("ps1_us", "scph5501.bin", "scph5501.bin", "Sony PlayStation 1 (US)", "490f666e1afb15b7362b406ed1cea246", false, false,
            "Американский эталонный BIOS PS1 NTSC-U.",
            "Reference North American PS1 NTSC-U BIOS."),
        MobileBiosItem("ps1_eu", "scph5502.bin", "scph5502.bin", "Sony PlayStation 1 (EU)", "32736f17079d0b2b7024407c39bd3050", false, false,
            "Европейский BIOS PS1 PAL.",
            "European PS1 PAL BIOS."),
        MobileBiosItem("dc_boot", "dc_boot.bin", "dc/dc_boot.bin", "Sega Dreamcast", "e10c53c2f8b90bab96ead2d368858623", false, false,
            "Основной загрузочный BIOS Sega Dreamcast для ядра Flycast.",
            "Sega Dreamcast main boot ROM for Flycast."),
        MobileBiosItem("dc_flash", "dc_flash.bin", "dc/dc_flash.bin", "Sega Dreamcast", "0a93f7940c455905bea6e392dfde92a4", true, false,
            "Системная энергонезависимая память настроек Dreamcast.",
            "Sega Dreamcast NVRAM flash ROM."),
        MobileBiosItem("ps2_bios_dir", "bios", "pcsx2/bios", "Sony PlayStation 2", "", false, true,
            "Каталог BIOS PlayStation 2 (scph39001.bin / scph70012.bin) для LRPS2.",
            "PlayStation 2 BIOS directory (scph39001.bin / scph70012.bin) for LRPS2."),
        MobileBiosItem("ps2_scph39001", "scph39001.bin", "pcsx2/bios/scph39001.bin", "Sony PlayStation 2 (US)", "b863d09a25b128564e29b0a73b22cf9c", true, false,
            "Образ BIOS консоли PlayStation 2 (USA v3.90).",
            "Sony PlayStation 2 USA v3.90 BIOS image."),
        MobileBiosItem("dolphin_sys", "Sys", "dolphin-emu/Sys", "Nintendo GameCube / Wii", "", true, true,
            "Системная папка ресурсов ядра Dolphin (GameCube / Wii).",
            "Dolphin core system data directory (dolphin-emu/Sys)."),
        MobileBiosItem("ppsspp_assets", "PPSSPP", "PPSSPP", "Sony PSP", "", true, true,
            "Системные шрифты и таблицы ассетов ядра PPSSPP (system/PPSSPP/).",
            "PPSSPP core font and UI assets directory (system/PPSSPP/)."),
        MobileBiosItem("nds_bios7", "bios7.bin", "bios7.bin", "Nintendo DS", "df692a80a5b1bc90728bc3dfc76cd948", false, false,
            "Системный ARM7 BIOS Nintendo DS для melonDS / DeSmuME.",
            "Nintendo DS ARM7 BIOS for melonDS and DeSmuME."),
        MobileBiosItem("nds_bios9", "bios9.bin", "bios9.bin", "Nintendo DS", "a392174eb3e572fed6447e956bde4b25", false, false,
            "Системный ARM9 BIOS Nintendo DS для melonDS / DeSmuME.",
            "Nintendo DS ARM9 BIOS for melonDS and DeSmuME."),
        MobileBiosItem("nds_firmware", "firmware.bin", "firmware.bin", "Nintendo DS", "", true, false,
            "Прошивка домашнего меню Nintendo DS.",
            "Nintendo DS firmware image."),
        MobileBiosItem("gba_bios", "gba_bios.bin", "gba_bios.bin", "Game Boy Advance", "a860e8c0b6d573d191e4ec7db1b1e4f6", true, false,
            "Оригинальный загрузочный экран Game Boy Advance.",
            "Authentic Game Boy Advance boot BIOS."),
        MobileBiosItem("segacd_us", "bios_CD_U.bin", "bios_CD_U.bin", "Sega CD (US)", "2efd74e3232ff260e371b99f84024f7f", false, false,
            "Американский BIOS аддона Sega CD для Genesis Plus GX / PicoDrive.",
            "North American Sega CD BIOS for Genesis Plus GX and PicoDrive."),
        MobileBiosItem("segacd_eu", "bios_CD_E.bin", "bios_CD_E.bin", "Sega Mega-CD (EU)", "e66fa1dc5820d254611fdcdba0662372", false, false,
            "Европейский BIOS Sega Mega-CD.",
            "European Sega Mega-CD BIOS."),
        MobileBiosItem("segacd_jp", "bios_CD_J.bin", "bios_CD_J.bin", "Sega Mega-CD (JP)", "278a9397d192149e84e820ac621a8edd", false, false,
            "Японский BIOS Sega Mega-CD.",
            "Japanese Sega Mega-CD BIOS."),
        MobileBiosItem("saturn_jp", "sega_101.bin", "sega_101.bin", "Sega Saturn (JP)", "85ec9ca47d8f6807718151cbcca8b964", false, false,
            "Японский BIOS Sega Saturn для Beetle Saturn / YabaSanshiro / Kronos.",
            "Japanese Sega Saturn BIOS."),
        MobileBiosItem("saturn_us_eu", "mpr-17933.bin", "mpr-17933.bin", "Sega Saturn (US/EU)", "3240872c70984b6cbfda1586cab68dbe", false, false,
            "Американский и европейский BIOS Sega Saturn.",
            "North American / European Sega Saturn BIOS."),
        MobileBiosItem("pcecd_sys3", "syscard3.pce", "syscard3.pce", "PC Engine CD", "38179df8f4ac870017db21ebcbf53114", false, false,
            "Системная карта Super System Card 3.0 для PC Engine CD / TurboGrafx-CD.",
            "Super System Card 3.0 BIOS for PC Engine CD / TurboGrafx-CD."),
        MobileBiosItem("fds_disksys", "disksys.rom", "disksys.rom", "Famicom Disk System", "ca30b50f880eb660a320674ed365ef7a", false, false,
            "Системный ПЗУ-адаптер Famicom Disk System.",
            "Nintendo Famicom Disk System BIOS ROM."),
        MobileBiosItem("3do_fz10", "panafz10.bin", "panafz10.bin", "Panasonic 3DO", "51f2f43ae2f3508a14d9f56597e2d3ce", false, false,
            "Основной BIOS Panasonic FZ-10 для ядра Opera 3DO.",
            "Panasonic FZ-10 3DO BIOS for Opera."),
        MobileBiosItem("neogeo_zip", "neogeo.zip", "neogeo.zip", "SNK Neo Geo", "", false, false,
            "Архив системных ПЗУ аркадной платы SNK Neo Geo MVS / UniBIOS.",
            "SNK Neo Geo MVS / AES system BIOS archive."),
        MobileBiosItem("lynx_boot", "lynxboot.img", "lynxboot.img", "Atari Lynx", "fcd403db69f54290b51035d82f835e7b", false, false,
            "Загрузочный образ портативной консоли Atari Lynx.",
            "Atari Lynx boot ROM image.")
    )

    fun scanBiosCatalog(systemDir: File = getSystemDir()): List<MobileBiosItem> {
        return CURATED_BIOS_CATALOG.map { template ->
            val item = template.copy()
            val primaryTarget = File(systemDir, item.relativePath)
            val flatFallback = File(systemDir, item.fileName)

            if (item.isDirectory) {
                if (primaryTarget.exists() && primaryTarget.isDirectory && (primaryTarget.listFiles()?.isNotEmpty() == true)) {
                    item.state = BiosVerifyState.PRESENT_DIRECTORY
                    item.resolvedPath = primaryTarget.absolutePath
                } else {
                    item.state = if (item.isOptional) BiosVerifyState.MISSING_OPTIONAL else BiosVerifyState.MISSING_REQUIRED
                }
            } else {
                val foundFile = when {
                    primaryTarget.exists() && primaryTarget.isFile && primaryTarget.length() > 0L -> primaryTarget
                    flatFallback.exists() && flatFallback.isFile && flatFallback.length() > 0L -> flatFallback
                    else -> null
                }
                if (foundFile != null) {
                    item.resolvedPath = foundFile.absolutePath
                    val md5 = computeMd5Hex(foundFile)
                    item.actualMd5 = md5
                    item.state = if (item.expectedMd5.isEmpty() || md5.equals(item.expectedMd5, ignoreCase = true)) {
                        BiosVerifyState.VERIFIED_MD5
                    } else {
                        BiosVerifyState.PRESENT_CUSTOM
                    }
                } else {
                    item.state = if (item.isOptional) BiosVerifyState.MISSING_OPTIONAL else BiosVerifyState.MISSING_REQUIRED
                }
            }
            item
        }
    }

    fun computeMd5Hex(file: File): String {
        return try {
            val digest = MessageDigest.getInstance("MD5")
            FileInputStream(file).use { fis ->
                val buf = ByteArray(32 * 1024)
                while (true) {
                    val r = fis.read(buf)
                    if (r <= 0) break
                    digest.update(buf, 0, r)
                }
            }
            digest.digest().joinToString("") { "%02x".format(it) }
        } catch (_: Throwable) {
            ""
        }
    }

    fun importBiosFile(
        context: Context,
        sourceUri: Uri,
        targetBios: MobileBiosItem? = null,
        fallbackFileName: String? = null
    ): Boolean {
        return try {
            val sysDir = getSystemDir()
            val relDest = when {
                targetBios != null && !targetBios.isDirectory -> targetBios.relativePath
                targetBios != null && targetBios.isDirectory && !fallbackFileName.isNullOrBlank() ->
                    "${targetBios.relativePath}/$fallbackFileName"
                !fallbackFileName.isNullOrBlank() -> {
                    val matched = CURATED_BIOS_CATALOG.firstOrNull {
                        !it.isDirectory && it.fileName.equals(fallbackFileName, ignoreCase = true)
                    }
                    matched?.relativePath ?: fallbackFileName
                }
                else -> return false
            }

            val outFile = File(sysDir, relDest)
            outFile.parentFile?.mkdirs()
            context.contentResolver.openInputStream(sourceUri)?.use { input ->
                FileOutputStream(outFile).use { output ->
                    val buf = ByteArray(32 * 1024)
                    while (true) {
                        val n = input.read(buf)
                        if (n <= 0) break
                        output.write(buf, 0, n)
                    }
                    output.flush()
                }
            } ?: return false
            outFile.exists() && outFile.length() > 0L
        } catch (_: Throwable) {
            false
        }
    }

    fun importBiosFromFile(sourceFile: File, targetBios: MobileBiosItem? = null): Boolean {
        if (!sourceFile.exists() || !sourceFile.isFile) return false
        return try {
            val matched = targetBios ?: CURATED_BIOS_CATALOG.firstOrNull {
                !it.isDirectory && it.fileName.equals(sourceFile.name, ignoreCase = true)
            }
            val relDest = matched?.relativePath ?: sourceFile.name
            val outFile = File(getSystemDir(), relDest)
            outFile.parentFile?.mkdirs()
            sourceFile.copyTo(outFile, overwrite = true)
            true
        } catch (_: Throwable) {
            false
        }
    }

    // === Online Mega Bezel & Shader Pack Manager ===

    val BEZEL_SHADER_PACKS: List<MobileBezelPackItem> = listOf(
        MobileBezelPackItem(
            id = "hsm_mega_bezel",
            title = "HyperspaceMadness Mega Bezel",
            descriptionRu = "Официальный пакет шейдеров с динамическим отражением ЭЛТ-рамки экрана.",
            descriptionEn = "Official HyperspaceMadness Mega Bezel reflection shader suite.",
            repoOwner = "HyperspaceMadness",
            repoName = "Mega_Bezel",
            fallbackZipUrl = "https://github.com/HyperspaceMadness/Mega_Bezel/archive/refs/heads/master.zip",
            targetRelativeDir = "shaders/shaders_slang/bezel/Mega_Bezel",
            approxSizeLabel = "185 MB"
        ),
        MobileBezelPackItem(
            id = "duimon_mega_bezel",
            title = "Duimon Mega Bezel Graphics",
            descriptionRu = "Полная коллекция аутентичных консольных рамок Duimon для всех ретро-платформ.",
            descriptionEn = "Complete Duimon console bezel graphics pack for retro systems.",
            repoOwner = "Duimon",
            repoName = "Duimon-Mega-Bezel",
            fallbackZipUrl = "https://github.com/Duimon/Duimon-Mega-Bezel/archive/refs/heads/master.zip",
            targetRelativeDir = "bezels/Duimon-Mega-Bezel",
            approxSizeLabel = "620 MB"
        ),
        MobileBezelPackItem(
            id = "duimon_potato_lite",
            title = "Duimon Potato / Lite (Mobile 60 FPS)",
            descriptionRu = "Облегчённый пакет рамок Duimon Potato/Lite специально для мобильных GPU без просадок FPS.",
            descriptionEn = "Lightweight Duimon Potato/Lite bezel presets tuned for locked 60 FPS on mobile GPUs.",
            repoOwner = "Duimon",
            repoName = "Duimon-Mega-Bezel-Potato",
            fallbackZipUrl = "https://github.com/Duimon/Duimon-Mega-Bezel-Potato/archive/refs/heads/master.zip",
            targetRelativeDir = "bezels/Duimon-Mega-Bezel-Potato",
            approxSizeLabel = "210 MB"
        )
    )

    fun scanBezelPacksStatus(): List<MobileBezelPackItem> {
        val root = File(MobileCoreRegistry.STORM_PUBLIC_ROOT)
        return BEZEL_SHADER_PACKS.map { pack ->
            val dir = File(root, pack.targetRelativeDir)
            val count = countPresetFiles(dir, maxDepth = 4)
            pack.copy(
                isInstalled = count > 0,
                presetCount = count
            )
        }
    }

    private fun countPresetFiles(dir: File, maxDepth: Int): Int {
        if (maxDepth < 0 || !dir.exists() || !dir.isDirectory) return 0
        var total = 0
        val children = dir.listFiles() ?: return 0
        for (c in children) {
            if (c.isDirectory) {
                total += countPresetFiles(c, maxDepth - 1)
            } else {
                val ext = c.extension.lowercase(Locale.ROOT)
                if (ext == "slangp" || ext == "glslp" || ext == "cfg" || ext == "png") {
                    total++
                }
            }
        }
        return total
    }

    fun downloadBezelPackAsync(
        packId: String,
        onProgress: (Int, String) -> Unit,
        onDone: (Boolean, String) -> Unit
    ) {
        val pack = BEZEL_SHADER_PACKS.firstOrNull { it.id == packId }
        if (pack == null) {
            mainHandler.post { onDone(false, "Unknown pack: $packId") }
            return
        }

        ioExecutor.execute {
            var conn: HttpURLConnection? = null
            try {
                mainHandler.post { onProgress(3, "Resolving ${pack.title}...") }
                val zipUrl = resolveLatestGitHubZipUrl(pack.repoOwner, pack.repoName, pack.fallbackZipUrl)
                val targetDir = File(MobileCoreRegistry.STORM_PUBLIC_ROOT, pack.targetRelativeDir)
                targetDir.mkdirs()

                // Also ensure sibling bezels/ and shaders/ directories exist
                getBezelsDir()
                getShadersDir()

                conn = MobileCoreRegistry.openFollowingRedirects(zipUrl)
                if (conn.responseCode !in 200..299) {
                    val code = conn.responseCode
                    mainHandler.post { onDone(false, "HTTP $code") }
                    return@execute
                }

                var filesExtracted = 0
                ZipInputStream(BufferedInputStream(conn.inputStream, 64 * 1024)).use { zis ->
                    val buf = ByteArray(32 * 1024)
                    while (true) {
                        val entry = zis.nextEntry ?: break
                        val normName = entry.name.replace('\\', '/')
                        // Strip GitHub top-level wrapper folder (e.g., Repo-master/...)
                        val slashIdx = normName.indexOf('/')
                        val relPath = if (slashIdx >= 0) normName.substring(slashIdx + 1) else normName
                        if (!entry.isDirectory && relPath.isNotEmpty() && !relPath.contains("..")) {
                            val outFile = File(targetDir, relPath)
                            outFile.parentFile?.mkdirs()
                            FileOutputStream(outFile).use { fos ->
                                while (true) {
                                    val r = zis.read(buf)
                                    if (r <= 0) break
                                    fos.write(buf, 0, r)
                                }
                            }
                            filesExtracted++
                            if (filesExtracted % 50 == 0) {
                                val c = filesExtracted
                                val pct = (10 + (c / 25)).coerceAtMost(95)
                                mainHandler.post { onProgress(pct, "${pack.title}: $c files...") }
                            }
                        }
                        zis.closeEntry()
                    }
                }

                val total = filesExtracted
                mainHandler.post {
                    onProgress(100, "${pack.title} installed ($total files)")
                    onDone(true, "${pack.title}: $total files installed")
                }
            } catch (e: Throwable) {
                mainHandler.post { onDone(false, e.message ?: "Pack install failed") }
            } finally {
                try { conn?.disconnect() } catch (_: Throwable) {}
            }
        }
    }

    private fun resolveLatestGitHubZipUrl(owner: String, repo: String, fallbackUrl: String): String {
        var conn: HttpURLConnection? = null
        return try {
            val apiUrl = "https://api.github.com/repos/$owner/$repo/releases/latest"
            conn = MobileCoreRegistry.openFollowingRedirects(apiUrl)
            if (conn.responseCode in 200..299) {
                val jsonText = conn.inputStream.bufferedReader(StandardCharsets.UTF_8).readText()
                val root = JSONObject(jsonText)
                val assets = root.optJSONArray("assets")
                if (assets != null) {
                    for (i in 0 until assets.length()) {
                        val asset = assets.optJSONObject(i) ?: continue
                        val name = asset.optString("name", "")
                        val dl = asset.optString("browser_download_url", "")
                        if (name.endsWith(".zip", ignoreCase = true) && dl.isNotBlank()) {
                            return dl
                        }
                    }
                }
            }
            fallbackUrl
        } catch (_: Throwable) {
            fallbackUrl
        } finally {
            try { conn?.disconnect() } catch (_: Throwable) {}
        }
    }
}

/**
 * GitHub Release Auto-Updater with strict 3-part SemVer normalization (`remote > current`,
 * never offering self-updates when `remote <= current`).
 */
object AutoUpdateHelper {

    const val GITHUB_LATEST_RELEASE_API = "https://api.github.com/repos/ReiKatari/STORM_RETROARCH/releases/latest"
    const val DEFAULT_CURRENT_VERSION = "2.4.0"

    private val updateExecutor = Executors.newSingleThreadExecutor()
    private val mainHandler = Handler(Looper.getMainLooper())

    fun getUpdatesDir(): File {
        val dir = File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "updates")
        try {
            if (!dir.exists()) dir.mkdirs()
        } catch (_: Throwable) {
        }
        return dir
    }

    /**
     * Normalizes version strings such as "v2.4", "2.4.0", or "2.4.0.0" to a 3-part SemVer tuple.
     */
    fun normalizeSemVer(rawVersion: String): Triple<Int, Int, Int> {
        val cleaned = rawVersion.trim().trimStart('v', 'V')
        val numericMatch = Regex("(\\d+)(?:\\.(\\d+))?(?:\\.(\\d+))?").find(cleaned)
            ?: return Triple(0, 0, 0)
        val major = numericMatch.groupValues.getOrNull(1)?.toIntOrNull() ?: 0
        val minor = numericMatch.groupValues.getOrNull(2)?.toIntOrNull() ?: 0
        val patch = numericMatch.groupValues.getOrNull(3)?.toIntOrNull() ?: 0
        return Triple(major, minor, patch)
    }

    fun formatCleanVersion(rawVersion: String): String {
        val (maj, min, pat) = normalizeSemVer(rawVersion)
        return "$maj.$min.$pat"
    }

    /**
     * Returns true ONLY when `remoteVersion > currentVersion` strictly.
     * Equal versions (`2.4.0` vs `2.4.0` or `v2.4` vs `2.4.0`) always return false!
     */
    fun isRemoteStrictlyNewer(remoteVersion: String, currentVersion: String): Boolean {
        val (rMaj, rMin, rPat) = normalizeSemVer(remoteVersion)
        val (cMaj, cMin, cPat) = normalizeSemVer(currentVersion)
        if (rMaj != cMaj) return rMaj > cMaj
        if (rMin != cMin) return rMin > cMin
        return rPat > cPat
    }

    fun checkForUpdateAsync(
        currentVersion: String = DEFAULT_CURRENT_VERSION,
        onResult: (MobileUpdateInfo) -> Unit
    ) {
        val cleanCur = formatCleanVersion(currentVersion)
        updateExecutor.execute {
            var conn: HttpURLConnection? = null
            try {
                conn = MobileCoreRegistry.openFollowingRedirects(GITHUB_LATEST_RELEASE_API)
                val code = conn.responseCode
                if (code !in 200..299) {
                    mainHandler.post {
                        onResult(
                            MobileUpdateInfo(
                                updateAvailable = false,
                                currentVersion = cleanCur,
                                latestVersion = cleanCur,
                                apkDownloadUrl = "",
                                releaseNotes = "",
                                releasePageUrl = "https://github.com/ReiKatari/STORM_RETROARCH/releases",
                                statusMessage = StormLocalization.tr("update_up_to_date")
                            )
                        )
                    }
                    return@execute
                }

                val bodyText = conn.inputStream.bufferedReader(StandardCharsets.UTF_8).readText()
                val json = JSONObject(bodyText)
                val tagName = json.optString("tag_name", json.optString("name", cleanCur))
                val cleanRemote = formatCleanVersion(tagName)
                val htmlUrl = json.optString("html_url", "https://github.com/ReiKatari/STORM_RETROARCH/releases")
                val notes = json.optString("body", "").replace(0x2014.toChar(), '-').replace(0x2013.toChar(), '-')

                var apkUrl = ""
                val assets = json.optJSONArray("assets")
                if (assets != null) {
                    for (i in 0 until assets.length()) {
                        val asset = assets.optJSONObject(i) ?: continue
                        val name = asset.optString("name", "")
                        val dlUrl = asset.optString("browser_download_url", "")
                        if (name.endsWith(".apk", ignoreCase = true) && dlUrl.isNotBlank()) {
                            apkUrl = dlUrl
                            break
                        }
                    }
                }

                val newer = isRemoteStrictlyNewer(cleanRemote, cleanCur)
                val status = if (newer) {
                    "${StormLocalization.tr("update_available")}: $cleanRemote"
                } else {
                    "${StormLocalization.tr("update_up_to_date")} ($cleanCur)"
                }

                mainHandler.post {
                    onResult(
                        MobileUpdateInfo(
                            updateAvailable = newer,
                            currentVersion = cleanCur,
                            latestVersion = cleanRemote,
                            apkDownloadUrl = apkUrl,
                            releaseNotes = notes,
                            releasePageUrl = htmlUrl,
                            statusMessage = status
                        )
                    )
                }
            } catch (e: Throwable) {
                mainHandler.post {
                    onResult(
                        MobileUpdateInfo(
                            updateAvailable = false,
                            currentVersion = cleanCur,
                            latestVersion = cleanCur,
                            apkDownloadUrl = "",
                            releaseNotes = "",
                            releasePageUrl = "https://github.com/ReiKatari/STORM_RETROARCH/releases",
                            statusMessage = e.message ?: StormLocalization.tr("update_up_to_date")
                        )
                    )
                }
            } finally {
                try { conn?.disconnect() } catch (_: Throwable) {}
            }
        }
    }

    fun downloadUpdateApkAsync(
        context: Context,
        apkUrl: String,
        versionLabel: String,
        onProgress: (Int, String) -> Unit,
        onDone: (Boolean, File?, String) -> Unit
    ) {
        if (apkUrl.isBlank()) {
            mainHandler.post { onDone(false, null, "No .apk asset URL in release") }
            return
        }
        val appCtx = context.applicationContext
        val cleanVer = formatCleanVersion(versionLabel)

        updateExecutor.execute {
            var conn: HttpURLConnection? = null
            try {
                val outDir = getUpdatesDir()
                val outFile = try {
                    outDir.mkdirs()
                    File(outDir, "STORM_RETROARCH_${cleanVer}.apk")
                } catch (_: Throwable) {
                    val internalDir = File(appCtx.filesDir, "updates")
                    internalDir.mkdirs()
                    File(internalDir, "STORM_RETROARCH_${cleanVer}.apk")
                }

                conn = MobileCoreRegistry.openFollowingRedirects(apkUrl)
                if (conn.responseCode !in 200..299) {
                    val code = conn.responseCode
                    mainHandler.post { onDone(false, null, "HTTP $code") }
                    return@execute
                }

                val totalBytes = conn.contentLengthLong
                BufferedInputStream(conn.inputStream, 32 * 1024).use { input ->
                    FileOutputStream(outFile).use { output ->
                        val buf = ByteArray(32 * 1024)
                        var downloaded = 0L
                        var lastPct = 0
                        while (true) {
                            val r = input.read(buf)
                            if (r <= 0) break
                            output.write(buf, 0, r)
                            downloaded += r
                            if (totalBytes > 0L) {
                                val pct = ((downloaded * 100L) / totalBytes).toInt().coerceIn(1, 99)
                                if (pct >= lastPct + 3) {
                                    lastPct = pct
                                    mainHandler.post { onProgress(pct, "APK $cleanVer: $pct%") }
                                }
                            }
                        }
                        output.flush()
                    }
                }

                mainHandler.post {
                    onProgress(100, StormLocalization.tr("update_ready_install"))
                    onDone(true, outFile, outFile.absolutePath)
                }
            } catch (e: Throwable) {
                mainHandler.post { onDone(false, null, e.message ?: "APK download failed") }
            } finally {
                try { conn?.disconnect() } catch (_: Throwable) {}
            }
        }
    }
}
