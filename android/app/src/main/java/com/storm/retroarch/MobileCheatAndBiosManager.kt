package com.storm.retroarch

import android.content.Context
import android.net.Uri
import android.os.Handler
import android.os.Looper
import androidx.documentfile.provider.DocumentFile
import org.json.JSONArray
import org.json.JSONObject
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.io.InputStream
import java.net.HttpURLConnection
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.concurrent.Executors
import java.util.zip.CRC32
import java.util.zip.GZIPInputStream
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream
import java.util.zip.ZipOutputStream

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

data class CheatCatalogSearchItem(
    val systemName: String,
    val platformKey: String,
    val gameTitle: String,
    val cleanTitle: String,
    val isInLibrary: Boolean,
    val matchedRomPath: String?,
    val isCachedLocally: Boolean
)

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
    val isPresent: Boolean = false,
    val actualMd5: String = "",
    val md5Valid: Boolean = false
) {
    fun localizedDescription(lang: String = StormLocalization.currentLanguage): String {
        return if (lang.lowercase(Locale.ROOT) == "ru") descriptionRu else descriptionEn
    }
}

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
    val isInstalled: Boolean = false,
    val presetCount: Int = 0
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
    const val GITHUB_RAW_CHT_BASE = "https://raw.githubusercontent.com/libretro/libretro-database/master/cht"

    private val ioExecutor = Executors.newFixedThreadPool(2)
    private val mainHandler = Handler(Looper.getMainLooper())
    private val catalogLock = Any()

    private data class IndexedCatalogGame(
        val systemName: String,
        val platformKey: String,
        val gameTitle: String,
        val cleanTitle: String,
        val searchLower: String
    )

    @Volatile
    private var cachedCatalogSystems: Map<String, List<String>>? = null

    @Volatile
    private var cachedFlatCatalog: List<IndexedCatalogGame>? = null

    @Volatile
    private var starterUnpackedSession = false

    val CHEAT_PLATFORM_FOLDERS: Map<String, List<String>> = mapOf(
        "nes" to listOf("Nintendo - Nintendo Entertainment System", "nes"),
        "fds" to listOf("Nintendo - Family Computer Disk System", "fds"),
        "snes" to listOf("Nintendo - Super Nintendo Entertainment System", "snes"),
        "n64" to listOf("Nintendo - Nintendo 64", "n64"),
        "gb" to listOf("Nintendo - Game Boy", "gb"),
        "gbc" to listOf("Nintendo - Game Boy Color", "Nintendo - Game Boy", "gbc"),
        "gb_gbc" to listOf("Nintendo - Game Boy Color", "Nintendo - Game Boy", "gb_gbc"),
        "gba" to listOf("Nintendo - Game Boy Advance", "gba"),
        "nds" to listOf("Nintendo - Nintendo DS", "nds"),
        "3ds" to listOf("Nintendo - Nintendo 3DS", "3ds"),
        "gamecube" to listOf("Nintendo - GameCube", "gamecube"),
        "wii" to listOf("Nintendo - Wii", "wii"),
        "gamecube_wii" to listOf("Nintendo - GameCube", "Nintendo - Wii"),
        "vb" to listOf("Nintendo - Virtual Boy", "vb"),
        "ps1" to listOf("Sony - PlayStation", "ps1", "psx"),
        "ps2" to listOf("Sony - PlayStation 2", "ps2"),
        "psp" to listOf("Sony - PlayStation Portable", "psp"),
        "genesis" to listOf("Sega - Mega Drive - Genesis", "genesis", "megadrive"),
        "segacd" to listOf("Sega - Mega-CD - Sega CD", "segacd"),
        "sega32x" to listOf("Sega - 32X", "sega32x"),
        "sms_gg" to listOf("Sega - Master System - Mark III", "Sega - Game Gear", "sms_gg"),
        "saturn" to listOf("Sega - Saturn", "saturn"),
        "dreamcast" to listOf("Sega - Dreamcast", "dreamcast"),
        "arcade" to listOf("FBNeo - Arcade Games", "MAME", "SNK - Neo Geo", "arcade"),
        "neogeo" to listOf("SNK - Neo Geo", "FBNeo - Arcade Games", "neogeo"),
        "neogeocd" to listOf("SNK - Neo Geo CD", "neogeocd"),
        "pce" to listOf("NEC - PC Engine - TurboGrafx 16", "pce"),
        "pcecd" to listOf("NEC - PC Engine CD - TurboGrafx-CD", "pcecd"),
        "pcfx" to listOf("NEC - PC-FX", "pcfx"),
        "ngp" to listOf("SNK - Neo Geo Pocket Color", "SNK - Neo Geo Pocket", "ngp"),
        "wonderswan" to listOf("Bandai - WonderSwan Color", "Bandai - WonderSwan", "wonderswan"),
        "3do" to listOf("The 3DO Company - 3DO", "3do"),
        "atari2600" to listOf("Atari - 2600", "atari2600"),
        "atari5200" to listOf("Atari - 5200", "atari5200"),
        "atari7800" to listOf("Atari - 7800", "atari7800"),
        "lynx" to listOf("Atari - Lynx", "lynx"),
        "jaguar" to listOf("Atari - Jaguar", "jaguar"),
        "coleco" to listOf("Coleco - ColecoVision", "coleco"),
        "intellivision" to listOf("Mattel - Intellivision", "intellivision"),
        "dos" to listOf("DOS", "dos"),
        "amiga" to listOf("Commodore - Amiga", "amiga"),
        "c64" to listOf("Commodore - 64", "c64"),
        "zxspectrum" to listOf("Sinclair - ZX Spectrum", "zxspectrum"),
        "msx" to listOf("Microsoft - MSX", "Microsoft - MSX2", "msx"),
        "amstrad" to listOf("Amstrad - CPC", "Amstrad - GX4000", "amstrad"),
        "doom" to listOf("DOOM", "doom")
    )

    private val CHEAT_PHRASE_TRANSLATIONS: List<Pair<String, String>> = listOf(
        "Infinite Lives" to "Бесконечные жизни",
        "Infinite Health" to "Бесконечное здоровье",
        "Infinite Energy" to "Бесконечная энергия",
        "Infinite Ammo" to "Бесконечные патроны",
        "Infinite Time" to "Бесконечное время",
        "Infinite Money" to "Бесконечные деньги",
        "Infinite Gold" to "Бесконечное золото",
        "InfiniteMP" to "Бесконечные ОМ",
        "Infinite MP" to "Бесконечная мана",
        "Infinite Magic" to "Бесконечная магия",
        "Infinite Continues" to "Бесконечные продолжения",
        "Invincibility" to "Неуязвимость",
        "Invincible" to "Неуязвимость",
        "Walk Through Walls" to "Прохождение сквозь стены",
        "One Hit Kill" to "Убийство с одного удара",
        "Max Money" to "Максимум денег",
        "Max Gold" to "Максимум золота",
        "Max Level" to "Максимальный уровень",
        "Max Stats" to "Максимальные характеристики",
        "Unlock All" to "Открыть все",
        "All Weapons" to "Все оружие",
        "All Items" to "Все предметы",
        "Moon Jump" to "Лунный супер-прыжок",
        "No Random Battles" to "Без случайных боев"
    )

    fun getCheatsRootDir(): File {
        val dir = File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "cheats")
        try {
            if (!dir.exists()) dir.mkdirs()
        } catch (_: Throwable) {
        }
        return dir
    }

    fun getEffectiveCheatsRootDir(context: Context? = null): File {
        val pub = getCheatsRootDir()
        if (pub.exists() && pub.canWrite()) return pub
        if (context != null) {
            val fallback = File(context.applicationContext.filesDir, "cheats")
            if (!fallback.exists()) fallback.mkdirs()
            return fallback
        }
        return pub
    }

    fun getSystemDir(): File {
        val dir = File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "system")
        try {
            if (!dir.exists()) dir.mkdirs()
        } catch (_: Throwable) {
        }
        return dir
    }

    fun getEffectiveSystemDir(context: Context? = null): File {
        val pub = getSystemDir()
        if (pub.exists() && pub.canWrite()) return pub
        if (context != null) {
            val fallback = File(context.applicationContext.filesDir, "system")
            if (!fallback.exists()) fallback.mkdirs()
            return fallback
        }
        return pub
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

    // === Initialization: Starter Cheats Unpack & 26,797-Game GZ Catalog ===

    fun initialize(context: Context) {
        val appCtx = context.applicationContext
        ioExecutor.execute {
            unpackStarterCheatsIfNeeded(appCtx)
            ensureCatalogLoaded(appCtx)
        }
    }

    fun unpackStarterCheatsIfNeeded(context: Context): Int {
        val targetRoot = getEffectiveCheatsRootDir(context)
        val marker = File(targetRoot, ".starter_cheats_260.ok")
        if (starterUnpackedSession && marker.exists()) return 0

        var extracted = 0
        try {
            targetRoot.mkdirs()
            context.applicationContext.assets.open("starter_cheats.zip").use { rawIn ->
                ZipInputStream(BufferedInputStream(rawIn, 16 * 1024)).use { zis ->
                    var entry = zis.nextEntry
                    while (entry != null) {
                        if (!entry.isDirectory && entry.name.endsWith(".cht", ignoreCase = true)) {
                            val cleanRel = entry.name.replace('\\', '/').trimStart('/')
                            if (!cleanRel.contains("..")) {
                                val outFile = File(targetRoot, cleanRel)
                                if (!outFile.exists() || outFile.length() == 0L) {
                                    outFile.parentFile?.mkdirs()
                                    FileOutputStream(outFile).use { out ->
                                        zis.copyTo(out, 16 * 1024)
                                        out.flush()
                                    }
                                    extracted++
                                }
                            }
                        }
                        zis.closeEntry()
                        entry = zis.nextEntry
                    }
                }
            }
            marker.writeText("2.6.0", StandardCharsets.UTF_8)
            starterUnpackedSession = true
        } catch (_: Throwable) {
        }
        return extracted
    }

    private fun ensureCatalogLoaded(context: Context): List<IndexedCatalogGame> {
        cachedFlatCatalog?.let { return it }
        synchronized(catalogLock) {
            cachedFlatCatalog?.let { return it }
            val sysMap = LinkedHashMap<String, List<String>>()
            val flat = ArrayList<IndexedCatalogGame>(27500)
            try {
                val text = try {
                    // aapt2 transparently strips .gz and stores assets/cheats_catalog.json DEFLATE-compressed in the APK
                    context.applicationContext.assets.open("cheats_catalog.json").use { raw ->
                        raw.bufferedReader(StandardCharsets.UTF_8).readText()
                    }
                } catch (_: Exception) {
                    context.applicationContext.assets.open("cheats_catalog.json.gz").use { raw ->
                        GZIPInputStream(BufferedInputStream(raw, 32 * 1024)).use { gz ->
                            gz.bufferedReader(StandardCharsets.UTF_8).readText()
                        }
                    }
                }
                if (text.isNotEmpty()) {
                    run {
                        val root = JSONObject(text)
                        val systemsObj = root.optJSONObject("systems") ?: JSONObject()
                        val sysNames = ArrayList<String>()
                        val keys = systemsObj.keys()
                        while (keys.hasNext()) sysNames.add(keys.next())
                        sysNames.sort()

                        for (sys in sysNames) {
                            val arr = systemsObj.optJSONArray(sys) ?: continue
                            val pKey = MobileCoreRegistry.normalizePlatformKey(sys, "")
                            val gameList = ArrayList<String>(arr.length())
                            for (i in 0 until arr.length()) {
                                val title = arr.optString(i, "").trim()
                                if (title.isEmpty()) continue
                                gameList.add(title)
                                val clean = MobileLibraryAndCoverEngine.cleanDisplayTitle(title)
                                flat.add(
                                    IndexedCatalogGame(
                                        systemName = sys,
                                        platformKey = pKey,
                                        gameTitle = title,
                                        cleanTitle = clean,
                                        searchLower = title.lowercase(Locale.ROOT)
                                    )
                                )
                            }
                            sysMap[sys] = gameList
                        }
                    }
                }
            } catch (_: Throwable) {
            }
            cachedCatalogSystems = sysMap
            cachedFlatCatalog = flat
            return flat
        }
    }

    fun getCatalogSystemsSummary(context: Context): List<Pair<String, Int>> {
        ensureCatalogLoaded(context)
        val map = cachedCatalogSystems ?: emptyMap()
        return map.entries.map { it.key to it.value.size }
    }

    fun searchCatalog(
        context: Context,
        query: String = "",
        systemFilter: String = "",
        libraryGames: List<MobileRomItem> = emptyList(),
        libraryOnly: Boolean = false,
        limit: Int = 120
    ): List<CheatCatalogSearchItem> {
        val flat = ensureCatalogLoaded(context)
        val qTokens = query.trim().lowercase(Locale.ROOT).split(Regex("\\s+")).filter { it.isNotEmpty() }
        val sysTrim = systemFilter.trim()
        val sysLower = sysTrim.lowercase(Locale.ROOT)
        val normSysKey = if (sysTrim.isNotEmpty() && !sysLower.equals("all", true)) {
            MobileCoreRegistry.normalizePlatformKey(sysTrim, "")
        } else ""

        // Build fast lookup map of user's library games by normalized clean title + platformKey
        val libByCleanKey = HashMap<String, MobileRomItem>()
        for (rom in libraryGames) {
            val normTitle = normalizeMatchToken(rom.name)
            if (normTitle.isNotEmpty()) {
                libByCleanKey["${rom.platformKey}|$normTitle"] = rom
                libByCleanKey.putIfAbsent("*|$normTitle", rom)
            }
        }

        val cheatsRoot = getEffectiveCheatsRootDir(context)
        val results = ArrayList<CheatCatalogSearchItem>(limit.coerceIn(1, 500))

        for (item in flat) {
            if (sysTrim.isNotEmpty() && !sysLower.equals("all", true)) {
                val exactSys = item.systemName.equals(sysTrim, ignoreCase = true)
                val samePlat = normSysKey.isNotEmpty() && item.platformKey == normSysKey
                if (!exactSys && !samePlat) continue
            }

            if (qTokens.isNotEmpty()) {
                var allMatch = true
                for (tok in qTokens) {
                    if (!item.searchLower.contains(tok)) {
                        allMatch = false
                        break
                    }
                }
                if (!allMatch) continue
            }

            val normClean = normalizeMatchToken(item.cleanTitle)
            val matchedRom = if (normClean.isNotEmpty()) {
                libByCleanKey["${item.platformKey}|$normClean"] ?: libByCleanKey["*|$normClean"]
            } else null

            if (libraryOnly && matchedRom == null) continue

            val localCht = File(File(cheatsRoot, item.systemName), "${item.gameTitle}.cht")
            results.add(
                CheatCatalogSearchItem(
                    systemName = item.systemName,
                    platformKey = item.platformKey,
                    gameTitle = item.gameTitle,
                    cleanTitle = item.cleanTitle,
                    isInLibrary = matchedRom != null,
                    matchedRomPath = matchedRom?.filePath,
                    isCachedLocally = localCht.exists()
                )
            )
            if (results.size >= limit) break
        }
        return results
    }

    private fun normalizeMatchToken(s: String): String {
        return s.lowercase(Locale.ROOT).replace(Regex("[^a-z0-9а-яё]"), "")
    }

    // === On-Demand GitHub Raw Cheat Fetcher & Saver ===

    fun resolveLocalChtCandidates(
        context: Context,
        systemName: String,
        gameTitle: String
    ): List<File> {
        val pubRoot = getCheatsRootDir()
        val intRoot = File(context.applicationContext.filesDir, "cheats")
        val cleanTitle = gameTitle.removeSuffix(".cht").trim()
        val normPlat = MobileCoreRegistry.normalizePlatformKey(systemName, "")
        val list = ArrayList<File>(6)
        if (systemName.isNotBlank()) {
            list.add(File(File(pubRoot, systemName), "$cleanTitle.cht"))
            list.add(File(File(intRoot, systemName), "$cleanTitle.cht"))
        }
        if (normPlat.isNotBlank()) {
            list.add(File(File(pubRoot, normPlat), "$cleanTitle.cht"))
            list.add(File(File(intRoot, normPlat), "$cleanTitle.cht"))
        }
        return list.distinct()
    }

    fun getGameCheats(
        context: Context,
        systemName: String,
        gameTitle: String,
        lang: String = StormLocalization.currentLanguage
    ): List<MobileCheatEntry> {
        unpackStarterCheatsIfNeeded(context)
        val cleanSys = systemName.trim()
        val cleanTitle = gameTitle.removeSuffix(".cht").trim()

        for (candidate in resolveLocalChtCandidates(context, cleanSys, cleanTitle)) {
            if (candidate.exists() && candidate.length() > 8L) {
                return parseChtFile(candidate, lang)
            }
        }

        // Fetch on-demand from GitHub Raw (Fastly CDN, zero Cloudflare throttling)
        if (cleanSys.isNotEmpty() && cleanTitle.isNotEmpty()) {
            val sysFoldersToTry = ArrayList<String>()
            sysFoldersToTry.add(cleanSys)
            val normPlat = MobileCoreRegistry.normalizePlatformKey(cleanSys, "")
            CHEAT_PLATFORM_FOLDERS[normPlat]?.forEach { folder ->
                if (!sysFoldersToTry.contains(folder)) sysFoldersToTry.add(folder)
            }

            for (sysFolder in sysFoldersToTry) {
                val url = "$GITHUB_RAW_CHT_BASE/${Uri.encode(sysFolder)}/${Uri.encode("$cleanTitle.cht")}"
                var conn: HttpURLConnection? = null
                try {
                    conn = MobileCoreRegistry.openFollowingRedirects(url, connectTimeoutMs = 8000, readTimeoutMs = 15000)
                    if (conn.responseCode in 200..299) {
                        val content = conn.inputStream.bufferedReader(StandardCharsets.UTF_8).readText()
                        if (content.contains("cheat") && content.length > 10) {
                            val targetDir = File(getEffectiveCheatsRootDir(context), sysFolder)
                            targetDir.mkdirs()
                            val savedFile = File(targetDir, "$cleanTitle.cht")
                            savedFile.writeText(content, StandardCharsets.UTF_8)
                            return parseChtFile(savedFile, lang)
                        }
                    }
                } catch (_: Throwable) {
                } finally {
                    try { conn?.disconnect() } catch (_: Throwable) {}
                }
            }
        }
        return emptyList()
    }

    fun saveGameCheats(
        context: Context,
        systemName: String,
        gameTitle: String,
        entries: List<MobileCheatEntry>,
        romFilePath: String? = null
    ): Boolean {
        val cleanSys = systemName.trim().ifEmpty { "custom" }
        val cleanTitle = gameTitle.removeSuffix(".cht").trim().ifEmpty { "game" }
        val root = getEffectiveCheatsRootDir(context)
        val primaryFile = File(File(root, cleanSys), "$cleanTitle.cht")
        val ok = saveChtFile(primaryFile, entries)

        // Sync active game cheats directly to the exact path EmulationActivity loads!
        val normPlat = MobileCoreRegistry.normalizePlatformKey(cleanSys, romFilePath?.let { File(it).extension } ?: "")
        val romStem = if (!romFilePath.isNullOrBlank()) {
            File(romFilePath).nameWithoutExtension
        } else {
            cleanTitle
        }

        val syncTargets = listOfNotNull(
            if (normPlat.isNotBlank()) File(File(getCheatsRootDir(), normPlat), "$romStem.cht") else null,
            if (normPlat.isNotBlank()) File(File(root, normPlat), "$romStem.cht") else null
        ).distinct()

        for (syncFile in syncTargets) {
            if (syncFile.absolutePath != primaryFile.absolutePath) {
                try {
                    saveChtFile(syncFile, entries)
                } catch (_: Throwable) {
                }
            }
        }
        return ok
    }

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

    fun indexAllChtFilesLazy(context: Context? = null): List<MobileCheatProfile> {
        val roots = listOfNotNull(
            getCheatsRootDir(),
            context?.let { File(it.applicationContext.filesDir, "cheats") }
        ).distinctBy { it.absolutePath }

        val list = ArrayList<MobileCheatProfile>(512)
        val seenPaths = HashSet<String>()

        for (root in roots) {
            if (!root.exists() || !root.canRead()) continue
            val topEntries = root.listFiles() ?: continue
            for (entry in topEntries) {
                if (entry.isDirectory) {
                    val pKey = MobileCoreRegistry.normalizePlatformKey(entry.name, "")
                    val chtFiles = entry.listFiles { f -> f.isFile && f.name.endsWith(".cht", ignoreCase = true) } ?: continue
                    for (cht in chtFiles) {
                        if (!seenPaths.add(cht.absolutePath)) continue
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
                    if (!seenPaths.add(entry.absolutePath)) continue
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
        }
        return list
    }

    fun matchCheatsForLibrary(roms: List<MobileRomItem>, context: Context? = null): List<MobileCheatProfile> {
        if (roms.isEmpty()) return emptyList()
        val indexed = indexAllChtFilesLazy(context)
        val out = ArrayList<MobileCheatProfile>()

        for (rom in roms) {
            val romStemLower = rom.rawNoIntroName.lowercase(Locale.ROOT)
            val romCleanNorm = normalizeMatchToken(rom.name)

            val exactOrClean = indexed.firstOrNull { prof ->
                val samePlat = prof.platformKey.isEmpty() || rom.platformKey.isEmpty() || prof.platformKey == rom.platformKey
                samePlat && (
                    prof.gameName.lowercase(Locale.ROOT) == romStemLower ||
                        (romCleanNorm.isNotEmpty() && normalizeMatchToken(prof.cleanTitle) == romCleanNorm)
                    )
            }
            if (exactOrClean != null) {
                out.add(
                    exactOrClean.copy(
                        matchedRomPath = rom.filePath,
                        isLibraryMatch = true
                    )
                )
            }
        }
        return out
    }

    fun parseChtFile(file: File, lang: String = StormLocalization.currentLanguage): List<MobileCheatEntry> {
        if (!file.exists() || !file.isFile) return emptyList()
        return try {
            val props = HashMap<String, String>()
            file.forEachLine(StandardCharsets.UTF_8) { rawLine ->
                val line = rawLine.trim()
                if (line.isEmpty() || line.startsWith("#") || line.startsWith("//")) return@forEachLine
                val eq = line.indexOf('=')
                if (eq <= 0) return@forEachLine
                val key = line.substring(0, eq).trim().lowercase(Locale.ROOT)
                var value = line.substring(eq + 1).trim()
                if (value.length >= 2 && value.startsWith("\"") && value.endsWith("\"")) {
                    value = value.substring(1, value.length - 1)
                }
                props[key] = value.replace("\\\"", "\"")
            }

            val count = props["cheats"]?.toIntOrNull() ?: 0
            val maxIdx = if (count > 0) count else 256
            val entries = ArrayList<MobileCheatEntry>()
            for (i in 0 until maxIdx) {
                val rawDesc = props["cheat${i}_desc"]
                val code = props["cheat${i}_code"]
                if (rawDesc == null && code == null) {
                    if (count == 0 && i > 8) break
                    continue
                }
                val cleanOrig = rawDesc ?: "Cheat #${i + 1}"
                val enabled = props["cheat${i}_enable"]?.equals("true", ignoreCase = true) == true
                entries.add(
                    MobileCheatEntry(
                        index = entries.size,
                        description = translateCheatDescription(cleanOrig, lang),
                        originalDescription = cleanOrig,
                        code = code ?: "",
                        isEnabled = enabled
                    )
                )
            }
            entries
        } catch (_: Throwable) {
            emptyList()
        }
    }

    fun saveChtFile(file: File, entries: List<MobileCheatEntry>): Boolean {
        return try {
            file.parentFile?.mkdirs()
            val sb = StringBuilder()
            sb.append("cheats = ").append(entries.size).append('\n').append('\n')
            for ((idx, entry) in entries.withIndex()) {
                val safeDesc = (entry.originalDescription.ifBlank { entry.description }).replace("\"", "'")
                val safeCode = entry.code.replace("\"", "").trim()
                sb.append("cheat").append(idx).append("_desc = \"").append(safeDesc).append("\"\n")
                sb.append("cheat").append(idx).append("_code = \"").append(safeCode).append("\"\n")
                sb.append("cheat").append(idx).append("_enable = ").append(if (entry.isEnabled) "true" else "false").append("\n\n")
            }
            file.writeText(sb.toString(), StandardCharsets.UTF_8)
            true
        } catch (_: Throwable) {
            false
        }
    }

    fun downloadOfficialLibretroCheatsZipAsync(
        onProgress: (Int, String) -> Unit,
        onDone: (Boolean, Int, String) -> Unit
    ) {
        ioExecutor.execute {
            var conn: HttpURLConnection? = null
            try {
                mainHandler.post { onProgress(3, "Connecting to GitHub...") }
                conn = MobileCoreRegistry.openFollowingRedirects(LIBRETRO_DB_MASTER_ZIP, 15000, 45000)
                if (conn.responseCode !in 200..299) {
                    val code = conn.responseCode
                    mainHandler.post { onDone(false, 0, "HTTP $code") }
                    return@execute
                }
                val rootDir = getCheatsRootDir()
                var extracted = 0
                ZipInputStream(BufferedInputStream(conn.inputStream, 32 * 1024)).use { zis ->
                    var entry = zis.nextEntry
                    while (entry != null) {
                        val name = entry.name.replace('\\', '/')
                        val chtMarker = "/cht/"
                        val markerPos = name.indexOf(chtMarker)
                        if (!entry.isDirectory && markerPos >= 0 && name.endsWith(".cht", ignoreCase = true)) {
                            val relPath = name.substring(markerPos + chtMarker.length)
                            if (!relPath.contains("..")) {
                                val outFile = File(rootDir, relPath)
                                outFile.parentFile?.mkdirs()
                                FileOutputStream(outFile).use { fos ->
                                    zis.copyTo(fos, 16 * 1024)
                                }
                                extracted++
                                if (extracted % 250 == 0) {
                                    val countSnap = extracted
                                    val pct = (10 + (countSnap / 300)).coerceAtMost(96)
                                    mainHandler.post { onProgress(pct, "Extracted $countSnap .cht files...") }
                                }
                            }
                        }
                        zis.closeEntry()
                        entry = zis.nextEntry
                    }
                }
                mainHandler.post {
                    onProgress(100, "Done ($extracted files)")
                    onDone(true, extracted, "$extracted .cht files installed")
                }
            } catch (e: Throwable) {
                mainHandler.post { onDone(false, 0, e.message ?: "Download failed") }
            } finally {
                try { conn?.disconnect() } catch (_: Throwable) {}
            }
        }
    }

    // === Expanded BIOS Database & Recursive Auto-Scanner ===

    val CURATED_BIOS_CATALOG: List<MobileBiosItem> = listOf(
        // Sony PlayStation 1
        MobileBiosItem("ps1_5501", "scph5501.bin", "scph5501.bin", "PS1", "490f666e1afb15b7362b406ed1cea246", false, false, "Основной BIOS Sony PlayStation (USA v3.0)", "Primary Sony PlayStation BIOS (USA v3.0)"),
        MobileBiosItem("ps1_5500", "scph5500.bin", "scph5500.bin", "PS1", "8dd7d5296a650fac7319bce665a6a53c", false, false, "Японский BIOS Sony PlayStation (JP v3.0)", "Japanese Sony PlayStation BIOS (JP v3.0)"),
        MobileBiosItem("ps1_5502", "scph5502.bin", "scph5502.bin", "PS1", "32736f17079d0b2b7024407c39bd3050", false, false, "Европейский BIOS Sony PlayStation (PAL v3.0)", "European Sony PlayStation BIOS (PAL v3.0)"),
        MobileBiosItem("ps1_1001", "scph1001.bin", "scph1001.bin", "PS1", "dc2b9bf8da62ec93e868cfd29f0d067d", true, false, "Классический BIOS PS1 USA (PCSX-ReARMed)", "Classic PS1 USA BIOS (PCSX-ReARMed)"),
        // Sony PlayStation 2
        MobileBiosItem("ps2_bios", "scph39001.bin", "pcsx2/bios/scph39001.bin", "PS2", "", true, false, "BIOS Sony PlayStation 2 (USA 4MB ROM0)", "Sony PlayStation 2 BIOS (USA 4MB ROM0)"),
        // Sega CD / Mega-CD
        MobileBiosItem("scd_us", "bios_CD_U.bin", "bios_CD_U.bin", "Sega CD", "2efd74e3232ff260e371b99f84024f7f", false, false, "BIOS Sega CD (Северная Америка)", "Sega CD BIOS (North America)"),
        MobileBiosItem("scd_eu", "bios_CD_E.bin", "bios_CD_E.bin", "Sega CD", "e66fa1dc5820d254611fdcdba0662372", false, false, "BIOS Mega-CD (Европа PAL)", "Mega-CD BIOS (Europe PAL)"),
        MobileBiosItem("scd_jp", "bios_CD_J.bin", "bios_CD_J.bin", "Sega CD", "278a9397d192149e84e820ac621a8edd", false, false, "BIOS Mega-CD (Япония NTSC-J)", "Mega-CD BIOS (Japan NTSC-J)"),
        // Sega Saturn
        MobileBiosItem("sat_jp", "sega_101.bin", "sega_101.bin", "Saturn", "85ec9ca47d8f6807718151cbcca8b964", false, false, "BIOS Sega Saturn (Япония)", "Sega Saturn BIOS (Japan)"),
        MobileBiosItem("sat_us_eu", "mpr-17933.bin", "mpr-17933.bin", "Saturn", "3240872c70984b6cbfda1586cab68dbe", false, false, "BIOS Sega Saturn (США / Европа)", "Sega Saturn BIOS (USA / Europe)"),
        MobileBiosItem("sat_yaba", "saturn_bios.bin", "saturn_bios.bin", "Saturn", "af5828fdff51384f99b3c4926be27762", true, false, "Универсальный BIOS Sega Saturn (YabaSanshiro)", "Universal Sega Saturn BIOS (YabaSanshiro)"),
        // Sega Dreamcast
        MobileBiosItem("dc_boot", "dc_boot.bin", "dc/dc_boot.bin", "Dreamcast", "e10c53c2f8b90bab96ead2d368858623", false, false, "Системный загрузочный BIOS Dreamcast (Flycast)", "Dreamcast Boot BIOS (Flycast)"),
        MobileBiosItem("dc_flash", "dc_flash.bin", "dc/dc_flash.bin", "Dreamcast", "0a93f7940c455905bea6e392dfde92a4", true, false, "Flash NVRAM память Dreamcast", "Dreamcast Flash ROM"),
        // Nintendo DS
        MobileBiosItem("nds_bios7", "bios7.bin", "bios7.bin", "NDS", "df692a80a5b1bc90728bc3dfc76cd948", false, false, "ARM7 BIOS Nintendo DS (melonDS)", "Nintendo DS ARM7 BIOS (melonDS)"),
        MobileBiosItem("nds_bios9", "bios9.bin", "bios9.bin", "NDS", "a392174eb3e572fed6447e956bde4b25", false, false, "ARM9 BIOS Nintendo DS (melonDS)", "Nintendo DS ARM9 BIOS (melonDS)"),
        MobileBiosItem("nds_fw", "firmware.bin", "firmware.bin", "NDS", "e45033d9b0fa6b0de071292bba7c9d13", true, false, "Прошивка меню Nintendo DS", "Nintendo DS Firmware"),
        // GBA & FDS
        MobileBiosItem("gba_bios", "gba_bios.bin", "gba_bios.bin", "GBA", "a860e8c0b6d573d191e4ec7db1b1e4f6", true, false, "Официальный BIOS Game Boy Advance", "Official Game Boy Advance BIOS"),
        MobileBiosItem("fds_bios", "disksys.rom", "disksys.rom", "FDS", "ca30b50f880eb660a320674ed365ef7a", false, false, "BIOS Famicom Disk System", "Famicom Disk System BIOS"),
        // PC Engine CD & 3DO
        MobileBiosItem("pce_syscard3", "syscard3.pce", "syscard3.pce", "PCE-CD", "38179df8f4ac870017db21ebcbf53114", false, false, "Super CD-ROM2 System Card 3.0", "Super CD-ROM2 System Card 3.0"),
        MobileBiosItem("3do_fz10", "panafz10.bin", "panafz10.bin", "3DO", "51f2f43ae2f3508a14d9f56597e2d3ce", false, false, "BIOS Panasonic FZ-10 3DO (Opera)", "Panasonic FZ-10 3DO BIOS (Opera)"),
        MobileBiosItem("3do_fz1", "panafz1.bin", "panafz1.bin", "3DO", "f47264dd47fe30f73ab3c010015c155b", true, false, "BIOS Panasonic FZ-1 3DO", "Panasonic FZ-1 3DO BIOS"),
        // Neo Geo AES/MVS & Neo Geo CD
        MobileBiosItem("neogeo_zip", "neogeo.zip", "neogeo.zip", "Neo Geo", "", false, false, "Архив BIOS SNK Neo Geo MVS/AES/UniBIOS", "SNK Neo Geo MVS/AES/UniBIOS Archive"),
        MobileBiosItem("neocd_z", "neocd_z.rom", "neocd/neocd_z.rom", "Neo Geo CD", "", true, false, "BIOS Neo Geo CDZ (Top/Front/CDZ)", "Neo Geo CDZ BIOS"),
        // Amiga
        MobileBiosItem("amiga_a500", "kick34005.A500", "kick34005.A500", "Amiga", "82a21c1890cae844b3df741f2762d48d", false, false, "Kickstart v1.3 rev 34.5 (Amiga 500)", "Kickstart v1.3 rev 34.5 (Amiga 500)"),
        MobileBiosItem("amiga_a1200", "kick40068.A1200", "kick40068.A1200", "Amiga", "646773759326fbac3b2311fd8c8793ee", false, false, "Kickstart v3.1 rev 40.68 (Amiga 1200)", "Kickstart v3.1 rev 40.68 (Amiga 1200)"),
        // NEC PC-98 & Sharp X68000
        MobileBiosItem("pc98_bios", "bios.rom", "np2kai/bios.rom", "PC-98", "", false, false, "Системный BIOS NEC PC-9801 (np2kai/bios.rom)", "NEC PC-9801 System BIOS (np2kai/bios.rom)"),
        MobileBiosItem("pc98_font", "font.rom", "np2kai/font.rom", "PC-98", "", true, false, "Шрифтовой ПЗУ NEC PC-9801 (np2kai/font.rom)", "NEC PC-9801 Font ROM (np2kai/font.rom)"),
        MobileBiosItem("x68k_ipl", "iplrom.dat", "keropi/iplrom.dat", "X68000", "7fd4caabac1d9169e289f0f7bbf71d8e", false, false, "IPL-ROM v1.0 Sharp X68000 (keropi/iplrom.dat)", "Sharp X68000 IPL-ROM v1.0 (keropi/iplrom.dat)"),
        MobileBiosItem("x68k_cg", "cgrom.dat", "keropi/cgrom.dat", "X68000", "cb0a5cfcf7247a7eab74bb2716260269", false, false, "CG-ROM шрифтов Sharp X68000 (keropi/cgrom.dat)", "Sharp X68000 CG-ROM (keropi/cgrom.dat)"),
        // Lynx & Coleco
        MobileBiosItem("lynx_boot", "lynxboot.img", "lynxboot.img", "Lynx", "fcd403db69f54290b51035d82f835e7b", false, false, "Загрузочный ПЗУ Atari Lynx", "Atari Lynx Boot Image"),
        MobileBiosItem("coleco_rom", "coleco.rom", "coleco.rom", "ColecoVision", "2c66f5911e5b42b8ebe113403548eee7", false, false, "Системный ПЗУ ColecoVision", "ColecoVision System BIOS")
    )

    // Extra MD5 aliases so differently named BIOS dumps get automatically renamed & placed properly!
    private val EXTRA_BIOS_MD5_TO_RELPATH: Map<String, String> = mapOf(
        "924e392ed05558ffdb115408c263dccf" to "scph1001.bin",
        "1e68c231d0896b7eadcad1d7d8e76129" to "scph7001.bin",
        "b6ba06f1eb0f31be76138fbd37ef7ab9" to "bios_CD_U.bin",
        "854b9150240a198070150e4566ae1290" to "bios_CD_U.bin",
        "e40a5dfb3d017ba8779faba30cbd1c8e" to "kick40063.A600",
        "8639fd5e549bd6238cfee79e3e749114" to "goldstar.bin"
    )

    private val EXTRA_EXACT_NAME_TO_RELPATH: Map<String, String> = mapOf(
        "neogeo.zip" to "neogeo.zip",
        "pgm.zip" to "pgm.zip",
        "neocd_f.rom" to "neocd/neocd_f.rom",
        "neocd_sf.rom" to "neocd/neocd_sf.rom",
        "neocd_t.rom" to "neocd/neocd_t.rom",
        "neocd_st.rom" to "neocd/neocd_st.rom",
        "neocd_z.rom" to "neocd/neocd_z.rom",
        "neocd_sz.rom" to "neocd/neocd_sz.rom",
        "uni-bioscd.rom" to "neocd/uni-bioscd.rom",
        "000-lo.lo" to "neocd/000-lo.lo",
        "bios.rom" to "np2kai/bios.rom",
        "font.rom" to "np2kai/font.rom",
        "font.tmp" to "np2kai/FONT.TMP",
        "itf.rom" to "np2kai/itf.rom",
        "sound.rom" to "np2kai/sound.rom",
        "iplrom.dat" to "keropi/iplrom.dat",
        "cgrom.dat" to "keropi/cgrom.dat",
        "iplromxv.dat" to "keropi/iplromxv.dat",
        "iplromco.dat" to "keropi/iplromco.dat",
        "5200.rom" to "5200.rom",
        "atarixl.rom" to "ATARIXL.ROM",
        "exec.bin" to "exec.bin",
        "grom.bin" to "grom.bin",
        "o2rom.bin" to "o2rom.bin",
        "prboom.wad" to "prboom.wad"
    )

    fun scanBiosCatalog(systemDir: File = getSystemDir()): List<MobileBiosItem> {
        return CURATED_BIOS_CATALOG.map { item ->
            val target = File(systemDir, item.relativePath)
            if (item.isDirectory) {
                val present = target.exists() && target.isDirectory && (target.listFiles()?.isNotEmpty() == true)
                item.copy(isPresent = present, md5Valid = present)
            } else if (target.exists() && target.isFile && target.length() > 0L) {
                val actual = if (target.length() <= 16L * 1024L * 1024L) computeMd5Hex(target) else ""
                val valid = item.expectedMd5.isEmpty() || actual.equals(item.expectedMd5, ignoreCase = true)
                item.copy(isPresent = true, actualMd5 = actual, md5Valid = valid)
            } else {
                item.copy(isPresent = false, actualMd5 = "", md5Valid = false)
            }
        }
    }

    fun computeMd5Hex(file: File): String {
        if (!file.exists() || !file.isFile) return ""
        return try {
            val md = MessageDigest.getInstance("MD5")
            FileInputStream(file).use { fis ->
                val buf = ByteArray(32 * 1024)
                while (true) {
                    val r = fis.read(buf)
                    if (r <= 0) break
                    md.update(buf, 0, r)
                }
            }
            md.digest().joinToString("") { "%02x".format(it) }
        } catch (_: Throwable) {
            ""
        }
    }

    private fun computeMd5AndStageStream(input: InputStream, tempStage: File): String {
        val md = MessageDigest.getInstance("MD5")
        tempStage.parentFile?.mkdirs()
        FileOutputStream(tempStage).use { fos ->
            val buf = ByteArray(32 * 1024)
            while (true) {
                val r = input.read(buf)
                if (r <= 0) break
                md.update(buf, 0, r)
                fos.write(buf, 0, r)
            }
            fos.flush()
        }
        return md.digest().joinToString("") { "%02x".format(it) }
    }

    private fun resolveBiosDestinationRelPath(fileName: String, fileSize: Long, md5Lower: String): String? {
        if (md5Lower.isNotEmpty()) {
            CURATED_BIOS_CATALOG.firstOrNull {
                it.expectedMd5.isNotEmpty() && it.expectedMd5.equals(md5Lower, ignoreCase = true)
            }?.let { return it.relativePath }
            EXTRA_BIOS_MD5_TO_RELPATH[md5Lower]?.let { return it }
        }

        val lowerName = fileName.lowercase(Locale.ROOT)
        CURATED_BIOS_CATALOG.firstOrNull {
            !it.isDirectory && it.fileName.equals(fileName, ignoreCase = true)
        }?.let { return it.relativePath }

        EXTRA_EXACT_NAME_TO_RELPATH[lowerName]?.let { return it }

        // PS2 4MB BIOS auto-match (e.g. scph10000.bin, scph39001.bin, scph70012.bin, ps2-0230a-*.bin)
        if (fileSize == 4L * 1024L * 1024L && (lowerName.startsWith("scph") || lowerName.startsWith("ps2-"))) {
            return "pcsx2/bios/$fileName"
        }
        return null
    }

    fun scanAndImportBiosDirectory(context: Context, dirOrTreeUri: String): JSONObject {
        val appCtx = context.applicationContext
        val sysDir = getEffectiveSystemDir(appCtx)
        sysDir.mkdirs()

        var scannedFiles = 0
        var importedCount = 0
        val importedNames = ArrayList<String>()

        fun processFileCandidate(file: File) {
            if (!file.exists() || !file.isFile) return
            val len = file.length()
            if (len <= 0L || len > 32L * 1024L * 1024L) return
            if (file.canonicalPath.startsWith(sysDir.canonicalPath) && file.parentFile == sysDir) {
                // Still allow moving e.g. system/dc_boot.bin -> system/dc/dc_boot.bin!
                val lower = file.name.lowercase(Locale.ROOT)
                if (lower != "dc_boot.bin" && lower != "dc_flash.bin" && lower != "iplrom.dat" && lower != "cgrom.dat" && lower != "bios.rom" && lower != "font.rom") {
                    return
                }
            }
            scannedFiles++
            val md5 = if (len <= 16L * 1024L * 1024L) computeMd5Hex(file) else ""
            val relDest = resolveBiosDestinationRelPath(file.name, len, md5) ?: return
            val destFile = File(sysDir, relDest)
            if (destFile.canonicalPath == file.canonicalPath) return
            destFile.parentFile?.mkdirs()
            file.copyTo(destFile, overwrite = true)
            // Mirror to public systemDir as well if sysDir was internal fallback
            val pubSys = getSystemDir()
            if (pubSys.absolutePath != sysDir.absolutePath) {
                try {
                    val pubDest = File(pubSys, relDest)
                    pubDest.parentFile?.mkdirs()
                    file.copyTo(pubDest, overwrite = true)
                } catch (_: Throwable) {}
            }
            importedCount++
            importedNames.add(relDest)
        }

        fun walkFileDir(dir: File, depth: Int) {
            if (depth > 5 || !dir.exists() || !dir.isDirectory) return
            val children = dir.listFiles() ?: return
            for (c in children) {
                if (c.isDirectory) {
                    walkFileDir(c, depth + 1)
                } else {
                    try { processFileCandidate(c) } catch (_: Throwable) {}
                }
            }
        }

        val trimmed = dirOrTreeUri.trim()
        if (trimmed.startsWith("content://")) {
            val uri = Uri.parse(trimmed)
            val realDir = MobileLibraryAndCoverEngine.resolveTreeOrDocumentUriToRealFile(uri)
            if (realDir != null && realDir.exists() && realDir.isDirectory && realDir.canRead()) {
                walkFileDir(realDir, 0)
            } else {
                val treeDoc = DocumentFile.fromTreeUri(appCtx, uri)
                val tempStage = File(appCtx.cacheDir, "bios_scan_stage.tmp")
                fun walkDoc(doc: DocumentFile?, depth: Int) {
                    if (doc == null || depth > 5) return
                    for (child in doc.listFiles()) {
                        if (child.isDirectory) {
                            walkDoc(child, depth + 1)
                        } else if (child.isFile) {
                            val len = child.length()
                            if (len in 1L..(32L * 1024L * 1024L)) {
                                val fName = child.name ?: continue
                                scannedFiles++
                                try {
                                    appCtx.contentResolver.openInputStream(child.uri)?.use { ins ->
                                        val md5 = computeMd5AndStageStream(ins, tempStage)
                                        val relDest = resolveBiosDestinationRelPath(fName, tempStage.length(), md5)
                                        if (relDest != null) {
                                            val destFile = File(sysDir, relDest)
                                            destFile.parentFile?.mkdirs()
                                            tempStage.copyTo(destFile, overwrite = true)
                                            importedCount++
                                            importedNames.add(relDest)
                                        }
                                    }
                                } catch (_: Throwable) {
                                } finally {
                                    tempStage.delete()
                                }
                            }
                        }
                    }
                }
                walkDoc(treeDoc, 0)
            }
        } else if (trimmed.isNotEmpty()) {
            walkFileDir(File(trimmed), 0)
        } else {
            val defaultCandidates = listOf(
                File("/storage/emulated/0/Download"),
                File("/storage/emulated/0/STORM RETROARCH/bios"),
                File("/storage/emulated/0/STORM RETROARCH/system"),
                File("/storage/emulated/0/RetroArch/system")
            )
            for (d in defaultCandidates) walkFileDir(d, 0)
        }

        val updatedCatalog = scanBiosCatalog(sysDir)
        val importedArr = JSONArray()
        importedNames.distinct().forEach { importedArr.put(it) }
        return JSONObject().apply {
            put("scannedFiles", scannedFiles)
            put("importedCount", importedNames.distinct().size)
            put("importedFiles", importedArr)
            put("readyCount", updatedCatalog.count { it.isPresent })
            put("totalCatalog", updatedCatalog.size)
        }
    }

    fun importBiosFile(
        context: Context,
        sourceUri: Uri,
        targetBios: MobileBiosItem? = null
    ): Boolean {
        val appCtx = context.applicationContext
        val sysDir = getEffectiveSystemDir(appCtx)
        val tempStage = File(appCtx.cacheDir, "single_bios_import.tmp")
        return try {
            val input = appCtx.contentResolver.openInputStream(sourceUri) ?: return false
            val md5 = input.use { computeMd5AndStageStream(it, tempStage) }
            val displayName = queryUriFileName(appCtx, sourceUri) ?: targetBios?.fileName ?: "bios.bin"
            val relDest = targetBios?.relativePath
                ?: resolveBiosDestinationRelPath(displayName, tempStage.length(), md5)
                ?: displayName
            val outFile = File(sysDir, relDest)
            outFile.parentFile?.mkdirs()
            tempStage.copyTo(outFile, overwrite = true)
            tempStage.delete()
            outFile.exists() && outFile.length() > 0L
        } catch (_: Throwable) {
            tempStage.delete()
            false
        }
    }

    fun importBiosFromFile(sourceFile: File, targetBios: MobileBiosItem? = null): Boolean {
        if (!sourceFile.exists() || !sourceFile.isFile) return false
        return try {
            val md5 = if (sourceFile.length() <= 16L * 1024L * 1024L) computeMd5Hex(sourceFile) else ""
            val relDest = targetBios?.relativePath
                ?: resolveBiosDestinationRelPath(sourceFile.name, sourceFile.length(), md5)
                ?: sourceFile.name
            val outFile = File(getSystemDir(), relDest)
            outFile.parentFile?.mkdirs()
            sourceFile.copyTo(outFile, overwrite = true)
            true
        } catch (_: Throwable) {
            false
        }
    }

    // === Save & State .zip Backup Export / Import ===

    fun exportSavesAndStatesZip(context: Context, destUriOrPath: String? = null): Pair<Boolean, String> {
        val root = File(MobileCoreRegistry.STORM_PUBLIC_ROOT)
        val savesDir = File(root, "saves")
        val statesDir = File(root, "states")
        val stamp = SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US).format(Date())
        val defaultBackupDir = File(root, "backups").apply { mkdirs() }
        val outFile = if (!destUriOrPath.isNullOrBlank() && !destUriOrPath.startsWith("content://")) {
            File(destUriOrPath)
        } else {
            File(defaultBackupDir, "STORM_RETROARCH_Saves_$stamp.zip")
        }

        return try {
            outFile.parentFile?.mkdirs()
            var packedCount = 0
            val outStream = if (!destUriOrPath.isNullOrBlank() && destUriOrPath.startsWith("content://")) {
                context.contentResolver.openOutputStream(Uri.parse(destUriOrPath))
                    ?: return false to "Cannot open output URI"
            } else {
                FileOutputStream(outFile)
            }

            ZipOutputStream(BufferedOutputStream(outStream, 32 * 1024)).use { zos ->
                fun addFolder(folder: File, prefix: String) {
                    if (!folder.exists() || !folder.isDirectory) return
                    val files = folder.walkTopDown().filter { it.isFile && it.length() > 0L }
                    for (f in files) {
                        val rel = f.relativeTo(folder).invariantSeparatorsPath
                        zos.putNextEntry(ZipEntry("$prefix/$rel"))
                        FileInputStream(f).use { inp -> inp.copyTo(zos, 32 * 1024) }
                        zos.closeEntry()
                        packedCount++
                    }
                }
                addFolder(savesDir, "saves")
                addFolder(statesDir, "states")
            }

            val label = if (!destUriOrPath.isNullOrBlank() && destUriOrPath.startsWith("content://")) {
                "Exported $packedCount files"
            } else {
                outFile.absolutePath
            }
            true to label
        } catch (e: Throwable) {
            false to (e.message ?: "Backup export failed")
        }
    }

    fun importSavesAndStatesZip(context: Context, sourceUriOrPath: String): Pair<Boolean, Int> {
        val root = File(MobileCoreRegistry.STORM_PUBLIC_ROOT)
        val savesDir = File(root, "saves").apply { mkdirs() }
        val statesDir = File(root, "states").apply { mkdirs() }
        val rootCanonical = root.canonicalPath

        val rawStream: InputStream = try {
            if (sourceUriOrPath.startsWith("content://")) {
                context.contentResolver.openInputStream(Uri.parse(sourceUriOrPath))
            } else {
                FileInputStream(File(sourceUriOrPath))
            }
        } catch (_: Throwable) {
            null
        } ?: return false to 0

        var restored = 0
        return try {
            ZipInputStream(BufferedInputStream(rawStream, 32 * 1024)).use { zis ->
                var entry = zis.nextEntry
                while (entry != null) {
                    if (!entry.isDirectory) {
                        val normName = entry.name.replace('\\', '/').trimStart('/')
                        if (!normName.contains("..")) {
                            val targetFile = when {
                                normName.startsWith("saves/") || normName.startsWith("states/") -> File(root, normName)
                                normName.endsWith(".srm", true) || normName.endsWith(".sav", true) -> File(savesDir, File(normName).name)
                                normName.contains(".state") -> File(statesDir, File(normName).name)
                                else -> null
                            }
                            if (targetFile != null && targetFile.canonicalPath.startsWith(rootCanonical)) {
                                targetFile.parentFile?.mkdirs()
                                FileOutputStream(targetFile).use { fos ->
                                    zis.copyTo(fos, 32 * 1024)
                                    fos.flush()
                                }
                                restored++
                            }
                        }
                    }
                    zis.closeEntry()
                    entry = zis.nextEntry
                }
            }
            (restored > 0) to restored
        } catch (_: Throwable) {
            false to restored
        }
    }

    // === CRC32 / MD5 / SHA-1 Single-Pass ROM Hasher ===

    fun computeRomHashes(context: Context, fileOrUri: String): JSONObject {
        val crc = CRC32()
        val md5 = MessageDigest.getInstance("MD5")
        val sha1 = MessageDigest.getInstance("SHA-1")
        var totalBytes = 0L
        var fileName = "rom"

        val stream: InputStream? = try {
            if (fileOrUri.startsWith("content://")) {
                val u = Uri.parse(fileOrUri)
                fileName = queryUriFileName(context, u) ?: "rom"
                context.contentResolver.openInputStream(u)
            } else {
                val f = File(fileOrUri)
                fileName = f.name
                if (f.exists() && f.isFile) FileInputStream(f) else null
            }
        } catch (_: Throwable) {
            null
        }

        if (stream == null) {
            return JSONObject().apply {
                put("success", false)
                put("error", "ROM file could not be opened")
            }
        }

        return try {
            stream.use { ins ->
                val buf = ByteArray(64 * 1024)
                while (true) {
                    val r = ins.read(buf)
                    if (r <= 0) break
                    crc.update(buf, 0, r)
                    md5.update(buf, 0, r)
                    sha1.update(buf, 0, r)
                    totalBytes += r
                }
            }
            val crcHex = String.format(Locale.US, "%08X", crc.value)
            val md5Hex = md5.digest().joinToString("") { "%02x".format(it) }
            val sha1Hex = sha1.digest().joinToString("") { "%02x".format(it) }
            JSONObject().apply {
                put("success", true)
                put("fileName", fileName)
                put("sizeBytes", totalBytes)
                put("crc32", crcHex)
                put("md5", md5Hex)
                put("sha1", sha1Hex)
            }
        } catch (e: Throwable) {
            JSONObject().apply {
                put("success", false)
                put("error", e.message ?: "Hash calculation failed")
            }
        }
    }

    // === IPS / BPS / UPS ROM Patcher ===

    fun applyRomPatch(
        context: Context,
        romPathOrUri: String,
        patchPathOrUri: String,
        outputRomPath: String? = null
    ): Pair<File?, String> {
        return try {
            val sourceBytes = readBytesFromPathOrUri(context, romPathOrUri)
                ?: return null to "Unable to read base ROM"
            val patchBytes = readBytesFromPathOrUri(context, patchPathOrUri)
                ?: return null to "Unable to read patch file"
            if (patchBytes.size < 8) return null to "Patch file is too small or corrupted"

            val header4 = String(patchBytes, 0, minOf(4, patchBytes.size), StandardCharsets.US_ASCII)
            val header5 = String(patchBytes, 0, minOf(5, patchBytes.size), StandardCharsets.US_ASCII)

            val patchedBytes = when {
                header5 == "PATCH" -> applyIpsBytes(sourceBytes, patchBytes)
                header4 == "BPS1" -> applyBpsBytes(sourceBytes, patchBytes)
                header4 == "UPS1" -> applyUpsBytes(sourceBytes, patchBytes)
                else -> return null to "Unsupported patch header (expected IPS, BPS1, or UPS1)"
            }

            val destFile = if (!outputRomPath.isNullOrBlank()) {
                File(outputRomPath)
            } else if (!romPathOrUri.startsWith("content://")) {
                val orig = File(romPathOrUri)
                val ext = orig.extension.ifBlank { "bin" }
                File(orig.parentFile ?: File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "roms"), "${orig.nameWithoutExtension} [Patched].$ext")
            } else {
                val u = Uri.parse(romPathOrUri)
                val name = queryUriFileName(context, u) ?: "game.bin"
                val stem = name.substringBeforeLast('.', name)
                val ext = name.substringAfterLast('.', "bin")
                val outDir = File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "roms/patched").apply { mkdirs() }
                File(outDir, "$stem [Patched].$ext")
            }

            destFile.parentFile?.mkdirs()
            destFile.writeBytes(patchedBytes)
            destFile to destFile.absolutePath
        } catch (e: Throwable) {
            null to (e.message ?: "Patching failed")
        }
    }

    private fun readBytesFromPathOrUri(context: Context, pathOrUri: String): ByteArray? {
        return try {
            if (pathOrUri.startsWith("content://")) {
                context.contentResolver.openInputStream(Uri.parse(pathOrUri))?.use { it.readBytes() }
            } else {
                val f = File(pathOrUri)
                if (f.exists() && f.isFile) f.readBytes() else null
            }
        } catch (_: Throwable) {
            null
        }
    }

    internal fun applyIpsBytes(source: ByteArray, patch: ByteArray): ByteArray {
        var out = source.copyOf()
        var pos = 5
        fun ensureCap(req: Int) {
            if (req > out.size) out = out.copyOf(req)
        }
        while (pos + 3 <= patch.size) {
            val b0 = patch[pos].toInt() and 0xFF
            val b1 = patch[pos + 1].toInt() and 0xFF
            val b2 = patch[pos + 2].toInt() and 0xFF
            if (b0 == 'E'.code && b1 == 'O'.code && b2 == 'F'.code) break
            val offset = (b0 shl 16) or (b1 shl 8) or b2
            pos += 3
            if (pos + 2 > patch.size) break
            val size = ((patch[pos].toInt() and 0xFF) shl 8) or (patch[pos + 1].toInt() and 0xFF)
            pos += 2
            if (size == 0) {
                if (pos + 3 > patch.size) break
                val rleLen = ((patch[pos].toInt() and 0xFF) shl 8) or (patch[pos + 1].toInt() and 0xFF)
                val rleVal = patch[pos + 2]
                pos += 3
                ensureCap(offset + rleLen)
                out.fill(rleVal, offset, offset + rleLen)
            } else {
                if (pos + size > patch.size) break
                ensureCap(offset + size)
                System.arraycopy(patch, pos, out, offset, size)
                pos += size
            }
        }
        return out
    }

    internal fun applyBpsBytes(source: ByteArray, patch: ByteArray): ByteArray {
        var pos = 4
        fun readVarInt(): Long {
            var data = 0L
            var shift = 1L
            while (pos < patch.size) {
                val x = patch[pos++].toLong() and 0xFFL
                data += (x and 0x7FL) * shift
                if ((x and 0x80L) != 0L) break
                shift = shift shl 7
                data += shift
            }
            return data
        }
        readVarInt() // expectedSourceSize
        val targetSize = readVarInt().toInt()
        val metadataSize = readVarInt().toInt()
        pos += metadataSize

        val target = ByteArray(targetSize)
        var outputOffset = 0
        var sourceRelOffset = 0
        var targetRelOffset = 0
        val endLimit = patch.size - 12

        while (pos < endLimit && outputOffset < targetSize) {
            val actionData = readVarInt()
            val mode = (actionData and 3L).toInt()
            var length = ((actionData ushr 2) + 1L).toInt()
            when (mode) {
                0 -> {
                    while (length-- > 0 && outputOffset < targetSize) {
                        target[outputOffset] = if (outputOffset < source.size) source[outputOffset] else 0
                        outputOffset++
                    }
                }
                1 -> {
                    while (length-- > 0 && pos < endLimit && outputOffset < targetSize) {
                        target[outputOffset++] = patch[pos++]
                    }
                }
                2 -> {
                    val raw = readVarInt()
                    val signed = if ((raw and 1L) != 0L) -(raw ushr 1).toInt() else (raw ushr 1).toInt()
                    sourceRelOffset += signed
                    while (length-- > 0 && outputOffset < targetSize) {
                        target[outputOffset++] = if (sourceRelOffset in source.indices) source[sourceRelOffset] else 0
                        sourceRelOffset++
                    }
                }
                3 -> {
                    val raw = readVarInt()
                    val signed = if ((raw and 1L) != 0L) -(raw ushr 1).toInt() else (raw ushr 1).toInt()
                    targetRelOffset += signed
                    while (length-- > 0 && outputOffset < targetSize) {
                        target[outputOffset++] = target[targetRelOffset.coerceIn(0, targetSize - 1)]
                        targetRelOffset++
                    }
                }
            }
        }
        return target
    }

    internal fun applyUpsBytes(source: ByteArray, patch: ByteArray): ByteArray {
        var pos = 4
        fun readUpsVarInt(): Long {
            var data = 0L
            var shift = 1L
            while (pos < patch.size) {
                val x = patch[pos++].toLong() and 0xFFL
                data += (x and 0x7FL) * shift
                if ((x and 0x80L) != 0L) break
                shift = shift shl 7
                data += shift
            }
            return data
        }
        val inputSize = readUpsVarInt().toInt()
        val outputSize = readUpsVarInt().toInt()
        val out = ByteArray(maxOf(outputSize, inputSize))
        System.arraycopy(source, 0, out, 0, minOf(source.size, out.size))

        var relOffset = 0
        val endLimit = patch.size - 12
        while (pos < endLimit) {
            relOffset += readUpsVarInt().toInt()
            while (pos < endLimit) {
                val xorByte = patch[pos++]
                if (relOffset < out.size) {
                    val base = if (relOffset < source.size) source[relOffset].toInt() else 0
                    out[relOffset] = (base xor xorByte.toInt()).toByte()
                }
                relOffset++
                if (xorByte.toInt() == 0) break
            }
        }
        return if (out.size == outputSize) out else out.copyOf(outputSize)
    }

    // === .m3u Multi-Disc Playlist Creator ===

    fun createM3uPlaylist(
        playlistTitle: String,
        discPaths: List<String>,
        targetDir: File? = null
    ): File? {
        val files = discPaths.map { File(it) }.filter { it.exists() && it.isFile }
        if (files.isEmpty()) return null
        val outDir = targetDir ?: files.first().parentFile ?: File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "roms")
        outDir.mkdirs()
        val safeTitle = playlistTitle.trim().removeSuffix(".m3u").replace(Regex("[\\\\/:*?\"<>|]"), "_").ifBlank {
            MobileLibraryAndCoverEngine.cleanDisplayTitle(files.first().nameWithoutExtension)
        }
        val m3uFile = File(outDir, "$safeTitle.m3u")
        val lines = files.map { f ->
            if (f.parentFile?.canonicalPath == outDir.canonicalPath) f.name else f.absolutePath
        }
        m3uFile.writeText(lines.joinToString("\n") + "\n", StandardCharsets.UTF_8)
        return m3uFile
    }

    private fun queryUriFileName(context: Context, uri: Uri): String? {
        return try {
            context.contentResolver.query(uri, arrayOf(android.provider.OpenableColumns.DISPLAY_NAME), null, null, null)?.use { c ->
                if (c.moveToFirst()) c.getString(0) else null
            }
        } catch (_: Throwable) {
            null
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
            descriptionRu = "Облегченный пакет рамок Duimon Potato/Lite специально для мобильных GPU без просадок FPS.",
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
            pack.copy(isInstalled = count > 0, presetCount = count)
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
                if (ext == "slangp" || ext == "glslp" || ext == "cfg" || ext == "png") total++
            }
        }
        return total
    }

    fun downloadBezelPackAsync(
        pack: MobileBezelPackItem,
        onProgress: (Int, String) -> Unit,
        onDone: (Boolean, Int, String) -> Unit
    ) {
        ioExecutor.execute {
            var conn: HttpURLConnection? = null
            try {
                mainHandler.post { onProgress(4, "Connecting to GitHub...") }
                conn = MobileCoreRegistry.openFollowingRedirects(pack.fallbackZipUrl, 15000, 60000)
                if (conn.responseCode !in 200..299) {
                    val code = conn.responseCode
                    mainHandler.post { onDone(false, 0, "HTTP $code") }
                    return@execute
                }
                val destRoot = File(MobileCoreRegistry.STORM_PUBLIC_ROOT, pack.targetRelativeDir)
                destRoot.mkdirs()
                val destCanonical = destRoot.canonicalPath
                var filesCount = 0
                ZipInputStream(BufferedInputStream(conn.inputStream, 32 * 1024)).use { zis ->
                    var entry = zis.nextEntry
                    while (entry != null) {
                        if (!entry.isDirectory) {
                            val raw = entry.name.replace('\\', '/')
                            val slashIdx = raw.indexOf('/')
                            val rel = if (slashIdx >= 0) raw.substring(slashIdx + 1) else raw
                            if (rel.isNotEmpty() && !rel.contains("..")) {
                                val outFile = File(destRoot, rel)
                                if (outFile.canonicalPath.startsWith(destCanonical)) {
                                    outFile.parentFile?.mkdirs()
                                    FileOutputStream(outFile).use { fos ->
                                        zis.copyTo(fos, 32 * 1024)
                                    }
                                    filesCount++
                                    if (filesCount % 40 == 0) {
                                        val snap = filesCount
                                        mainHandler.post { onProgress((10 + snap / 15).coerceAtMost(95), "${pack.title}: $snap files") }
                                    }
                                }
                            }
                        }
                        zis.closeEntry()
                        entry = zis.nextEntry
                    }
                }
                mainHandler.post {
                    onProgress(100, "${pack.title}: OK")
                    onDone(true, filesCount, "$filesCount files installed")
                }
            } catch (e: Throwable) {
                mainHandler.post { onDone(false, 0, e.message ?: "Pack download failed") }
            } finally {
                try { conn?.disconnect() } catch (_: Throwable) {}
            }
        }
    }
}

object AutoUpdateHelper {

    const val GITHUB_LATEST_RELEASE_API = "https://api.github.com/repos/ReiKatari/STORM_RETROARCH/releases/latest"
    const val DEFAULT_CURRENT_VERSION = "2.6.0"

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

    fun normalizeSemVer(rawVersion: String): Triple<Int, Int, Int> {
        val cleaned = rawVersion.trim().removePrefix("v").removePrefix("V").split('-', '+').firstOrNull() ?: "0.0.0"
        val parts = cleaned.split('.')
        val major = parts.getOrNull(0)?.toIntOrNull() ?: 0
        val minor = parts.getOrNull(1)?.toIntOrNull() ?: 0
        val patch = parts.getOrNull(2)?.toIntOrNull() ?: 0
        return Triple(major, minor, patch)
    }

    fun formatCleanVersion(rawVersion: String): String {
        val (maj, min, pat) = normalizeSemVer(rawVersion)
        return "$maj.$min.$pat"
    }

    fun isRemoteStrictlyNewer(remoteVersion: String, currentVersion: String): Boolean {
        val r = normalizeSemVer(remoteVersion)
        val c = normalizeSemVer(currentVersion)
        if (r.first != c.first) return r.first > c.first
        if (r.second != c.second) return r.second > c.second
        return r.third > c.third
    }

    fun checkForUpdateAsync(
        currentVersion: String = DEFAULT_CURRENT_VERSION,
        onResult: (MobileUpdateInfo) -> Unit
    ) {
        val cleanCur = formatCleanVersion(currentVersion)
        updateExecutor.execute {
            var conn: HttpURLConnection? = null
            try {
                conn = MobileCoreRegistry.openFollowingRedirects(GITHUB_LATEST_RELEASE_API, 10000, 15000)
                if (conn.responseCode !in 200..299) {
                    mainHandler.post {
                        onResult(
                            MobileUpdateInfo(
                                updateAvailable = false,
                                currentVersion = cleanCur,
                                latestVersion = cleanCur,
                                apkDownloadUrl = "",
                                releaseNotes = "",
                                releasePageUrl = "https://github.com/ReiKatari/STORM_RETROARCH/releases",
                                statusMessage = "${StormLocalization.tr("update_up_to_date")} ($cleanCur)"
                            )
                        )
                    }
                    return@execute
                }

                val jsonText = conn.inputStream.bufferedReader(StandardCharsets.UTF_8).readText()
                val root = JSONObject(jsonText)
                val tagName = root.optString("tag_name", cleanCur)
                val cleanRemote = formatCleanVersion(tagName)
                val htmlUrl = root.optString("html_url", "https://github.com/ReiKatari/STORM_RETROARCH/releases")
                val notes = root.optString("body", "").replace("\u2014", "-").replace("\u2013", "-")

                var apkUrl = ""
                val assets = root.optJSONArray("assets")
                if (assets != null) {
                    for (i in 0 until assets.length()) {
                        val asset = assets.optJSONObject(i) ?: continue
                        val name = asset.optString("name", "")
                        val dl = asset.optString("browser_download_url", "")
                        if (name.endsWith(".apk", ignoreCase = true) && dl.isNotBlank()) {
                            apkUrl = dl
                            if (name.contains("modern", true) || name.contains("arm64", true)) break
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
                val outFile = try {
                    val outDir = getUpdatesDir()
                    outDir.mkdirs()
                    File(outDir, "STORM_RETROARCH_${cleanVer}.apk")
                } catch (_: Throwable) {
                    val internalDir = File(appCtx.filesDir, "updates").apply { mkdirs() }
                    File(internalDir, "STORM_RETROARCH_${cleanVer}.apk")
                }

                conn = MobileCoreRegistry.openFollowingRedirects(apkUrl, 15000, 45000)
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
