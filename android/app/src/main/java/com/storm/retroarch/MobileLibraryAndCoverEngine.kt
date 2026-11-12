package com.storm.retroarch

import android.content.Context
import android.content.SharedPreferences
import android.os.Handler
import android.os.Looper
import java.io.BufferedInputStream
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.FileOutputStream
import java.io.RandomAccessFile
import java.net.HttpURLConnection
import java.net.URLDecoder
import java.net.URLEncoder
import java.nio.charset.StandardCharsets
import java.util.Locale
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.Executors
import java.util.zip.ZipFile
import kotlin.math.max

data class MobileRomItem(
    val name: String,
    val rawNoIntroName: String,
    val filePath: String,
    val extension: String,
    val platform: String,
    val platformKey: String,
    val regionCode: String,
    val isRussianMod: Boolean,
    var assignedCoreId: String,
    val sizeBytes: Long,
    var isFavorite: Boolean = false,
    var lastPlayedMs: Long = 0L,
    var coverPath: String? = null
) {
    val sizeFormatted: String
        get() {
            return when {
                sizeBytes >= 1024L * 1024L * 1024L ->
                    String.format(Locale.US, "%.2f GB", sizeBytes / (1024.0 * 1024.0 * 1024.0))
                sizeBytes >= 1024L * 1024L ->
                    String.format(Locale.US, "%.1f MB", sizeBytes / (1024.0 * 1024.0))
                else ->
                    String.format(Locale.US, "%d KB", max(1L, sizeBytes / 1024L))
            }
        }

    val regionDisplayBadge: String
        get() = if (isRussianMod) "$regionCode + RUS" else regionCode
}

data class LibraryFilterCounts(
    val smartCollections: Map<String, Int>,
    val platformCounts: Map<String, Int>,
    val platformKeyCounts: Map<String, Int>,
    val coreCounts: Map<String, Int>
)

object MobileLibraryAndCoverEngine {

    const val COLLECTION_ALL = "ALL"
    const val COLLECTION_FAVORITES = "FAVORITES"
    const val COLLECTION_RUS_MODS = "RUS_MODS"
    const val COLLECTION_CLASSIC_8_16 = "CLASSIC_8_16"
    const val COLLECTION_CD_3D = "CD_3D"
    const val COLLECTION_DOS_PC = "DOS_PC"
    const val COLLECTION_RECENT = "RECENT"

    private const val THUMBNAILS_SERVER = "https://thumbnails.libretro.com"
    private const val PREFS_ROM_META = "storm_mobile_rom_metadata"

    private val coverExecutor = Executors.newFixedThreadPool(3)
    private val mainHandler = Handler(Looper.getMainLooper())

    // In-memory cache of Libretro Named_Boxarts filenames per Libretro system folder
    private val boxartListingCache = ConcurrentHashMap<String, List<String>>()
    private val listingLocks = ConcurrentHashMap<String, Any>()

    private val CLASSIC_8_16_KEYS = setOf(
        "nes", "fds", "snes", "gb", "gbc", "gb_gbc", "gba", "vb", "pokemini", "gw",
        "genesis", "sega32x", "sms_gg", "sg1000", "segapico",
        "pce", "ngp", "neogeo", "cps", "arcade", "wonderswan",
        "vectrex", "coleco", "intellivision", "odyssey2",
        "atari2600", "atari5200", "atari7800", "lynx", "pico8", "tic80"
    )

    private val CD_3D_KEYS = setOf(
        "n64", "gamecube", "wii", "gamecube_wii", "nds", "3ds",
        "ps1", "ps2", "ps3", "psp", "psvita",
        "segacd", "saturn", "dreamcast", "naomi",
        "pcecd", "pcfx", "neogeocd", "3do", "cdi", "jaguar"
    )

    private val DOS_PC_KEYS = setOf(
        "dos", "scummvm", "amiga", "zxspectrum", "c64", "msx",
        "x68000", "pc98", "pc88", "amstrad", "atarist", "atari800", "bk0010", "doom"
    )

    private val IGNORED_DIR_NAMES = setOf(
        "cores", "system", "bios", "cheats", "thumbnails", "bezels",
        "shaders", "saves", "states", "autoconfig", "updates", "patches", ".git", "android"
    )

    private val LIBRETRO_THUMBNAIL_SYSTEM_MAP: Map<String, String> = mapOf(
        "nes" to "Nintendo - Nintendo Entertainment System",
        "fds" to "Nintendo - Family Computer Disk System",
        "snes" to "Nintendo - Super Nintendo Entertainment System",
        "n64" to "Nintendo - Nintendo 64",
        "gamecube" to "Nintendo - GameCube",
        "wii" to "Nintendo - Wii",
        "gamecube_wii" to "Nintendo - GameCube",
        "gb" to "Nintendo - Game Boy",
        "gbc" to "Nintendo - Game Boy Color",
        "gb_gbc" to "Nintendo - Game Boy Color",
        "gba" to "Nintendo - Game Boy Advance",
        "nds" to "Nintendo - Nintendo DS",
        "3ds" to "Nintendo - Nintendo 3DS",
        "vb" to "Nintendo - Virtual Boy",
        "pokemini" to "Nintendo - Pokemon Mini",
        "gw" to "Handheld Electronic Game",
        "ps1" to "Sony - PlayStation",
        "ps2" to "Sony - PlayStation 2",
        "ps3" to "Sony - PlayStation 3",
        "psp" to "Sony - PlayStation Portable",
        "psvita" to "Sony - PlayStation Vita",
        "genesis" to "Sega - Mega Drive - Genesis",
        "segacd" to "Sega - Mega-CD - Sega CD",
        "sega32x" to "Sega - 32X",
        "sms_gg" to "Sega - Master System - Mark III",
        "sg1000" to "Sega - SG-1000",
        "segapico" to "Sega - PICO",
        "saturn" to "Sega - Saturn",
        "dreamcast" to "Sega - Dreamcast",
        "naomi" to "Sega - Naomi",
        "arcade" to "FBNeo - Arcade Games",
        "neogeo" to "SNK - Neo Geo",
        "neogeocd" to "SNK - Neo Geo CD",
        "cps" to "FBNeo - Arcade Games",
        "pce" to "NEC - PC Engine - TurboGrafx 16",
        "pcecd" to "NEC - PC Engine CD - TurboGrafx-CD",
        "pcfx" to "NEC - PC-FX",
        "ngp" to "SNK - Neo Geo Pocket Color",
        "wonderswan" to "Bandai - WonderSwan Color",
        "3do" to "The 3DO Company - 3DO",
        "cdi" to "Philips - CD-i",
        "vectrex" to "GCE - Vectrex",
        "coleco" to "Coleco - ColecoVision",
        "intellivision" to "Mattel - Intellivision",
        "odyssey2" to "Magnavox - Odyssey2",
        "atari2600" to "Atari - 2600",
        "atari5200" to "Atari - 5200",
        "atari7800" to "Atari - 7800",
        "atari800" to "Atari - 8-bit",
        "lynx" to "Atari - Lynx",
        "jaguar" to "Atari - Jaguar",
        "atarist" to "Atari - ST",
        "dos" to "DOS",
        "scummvm" to "ScummVM",
        "amiga" to "Commodore - Amiga",
        "zxspectrum" to "Sinclair - ZX Spectrum",
        "c64" to "Commodore - 64",
        "msx" to "Microsoft - MSX",
        "x68000" to "Sharp - X68000",
        "pc98" to "NEC - PC-98",
        "pc88" to "NEC - PC-88",
        "amstrad" to "Amstrad - CPC",
        "doom" to "DOOM"
    )

    fun getDefaultRomDirectories(): List<File> {
        val root = File(MobileCoreRegistry.STORM_PUBLIC_ROOT)
        val romsDir = File(root, "roms")
        try {
            if (!romsDir.exists()) romsDir.mkdirs()
        } catch (_: Throwable) {
        }
        return listOf(romsDir)
    }

    fun getRomMetadataPrefs(context: Context): SharedPreferences {
        return context.getSharedPreferences(PREFS_ROM_META, Context.MODE_PRIVATE)
    }

    fun saveRomItemState(prefs: SharedPreferences, item: MobileRomItem) {
        val keyHash = item.filePath.hashCode().toUInt().toString(16)
        prefs.edit()
            .putBoolean("fav_$keyHash", item.isFavorite)
            .putLong("last_$keyHash", item.lastPlayedMs)
            .putString("core_$keyHash", item.assignedCoreId)
            .putString("cover_$keyHash", item.coverPath)
            .apply()
    }

    /**
     * Walks directories, deduplicating `.cue`/`.gdi`/`.m3u` child `.bin`/`.raw`/`.iso`/`.chd` tracks,
     * inspecting binary disc/cartridge headers, preserving base region on Russian mods,
     * and restoring user favorites, play history, core bindings, and cached covers.
     */
    fun scanDirectories(dirs: List<File>, savedPrefs: SharedPreferences? = null): List<MobileRomItem> {
        val supportedExts = MobileCoreRegistry.getAllUniqueExtensions()
        val candidateFiles = ArrayList<File>(256)
        val hiddenChildPaths = HashSet<String>()

        fun collectRecursive(dir: File, depth: Int) {
            if (depth > 8 || !dir.exists() || !dir.canRead()) return
            if (depth > 0 && IGNORED_DIR_NAMES.contains(dir.name.lowercase(Locale.ROOT))) return

            val entries = dir.listFiles() ?: return
            // Pass 1 in directory: register child tracks referenced by .m3u / .cue / .gdi
            for (f in entries) {
                if (!f.isFile) continue
                val ext = f.extension.lowercase(Locale.ROOT)
                if (ext == "m3u" || ext == "cue" || ext == "gdi") {
                    for (child in parseDescriptorChildFiles(f)) {
                        hiddenChildPaths.add(normalizePathKey(child))
                    }
                }
            }

            // Pass 2 in directory: collect candidate ROMs & recurse
            for (f in entries) {
                if (f.name.startsWith(".")) continue
                if (f.isDirectory) {
                    collectRecursive(f, depth + 1)
                } else if (f.isFile && f.length() > 0L) {
                    val ext = f.extension.lowercase(Locale.ROOT)
                    if (supportedExts.contains(ext)) {
                        candidateFiles.add(f)
                    }
                }
            }
        }

        for (root in dirs) {
            collectRecursive(root, 0)
        }

        val results = ArrayList<MobileRomItem>(candidateFiles.size)
        val seenPaths = HashSet<String>()

        for (file in candidateFiles) {
            val normKey = normalizePathKey(file)
            if (hiddenChildPaths.contains(normKey) || !seenPaths.add(normKey)) continue

            val ext = file.extension.lowercase(Locale.ROOT)
            // Skip plain .png unless it is a .p8.png PICO-8 cartridge
            if (ext == "png" && !file.name.lowercase(Locale.ROOT).endsWith(".p8.png")) continue

            val rawStem = if (file.name.lowercase(Locale.ROOT).endsWith(".p8.png")) {
                file.name.substring(0, file.name.length - 7)
            } else {
                file.nameWithoutExtension
            }

            val detectedPlatformKey = detectPlatformKeyForFile(file, ext)
            if (detectedPlatformKey.isEmpty()) continue

            val platformDisplay = MobileCoreRegistry.getPlatformDisplayName(detectedPlatformKey)
            val (regionCode, isRusMod) = parseRegionAndRusMod(rawStem)
            val cleanTitle = cleanDisplayTitle(rawStem)

            val defaultCore = MobileCoreRegistry.getDefaultCoreForPlatform(detectedPlatformKey, ext)
            val keyHash = file.absolutePath.hashCode().toUInt().toString(16)

            val savedFav = savedPrefs?.getBoolean("fav_$keyHash", false) ?: false
            val savedLast = savedPrefs?.getLong("last_$keyHash", 0L) ?: 0L
            val savedCore = savedPrefs?.getString("core_$keyHash", null)
            val savedCover = savedPrefs?.getString("cover_$keyHash", null)

            // Validate saved core strictly belongs to compatible cores for this platform
            val compatIds = MobileCoreRegistry.getCompatibleCoresForPlatform(detectedPlatformKey, ext).map { it.id }.toSet()
            val resolvedCoreId = if (!savedCore.isNullOrBlank() && compatIds.contains(savedCore)) {
                savedCore
            } else {
                defaultCore?.id ?: ""
            }

            val expectedCover = getExpectedCoverCacheFile(detectedPlatformKey, cleanTitle, regionCode)
            val validCoverPath = when {
                !savedCover.isNullOrBlank() && File(savedCover).exists() -> savedCover
                expectedCover.exists() && expectedCover.length() > 64L -> expectedCover.absolutePath
                else -> null
            }

            results.add(
                MobileRomItem(
                    name = cleanTitle,
                    rawNoIntroName = rawStem,
                    filePath = file.absolutePath,
                    extension = ext,
                    platform = platformDisplay,
                    platformKey = detectedPlatformKey,
                    regionCode = regionCode,
                    isRussianMod = isRusMod,
                    assignedCoreId = resolvedCoreId,
                    sizeBytes = file.length(),
                    isFavorite = savedFav,
                    lastPlayedMs = savedLast,
                    coverPath = validCoverPath
                )
            )
        }

        results.sortWith(compareBy<MobileRomItem> { it.platform }.thenBy { it.name.lowercase(Locale.ROOT) })
        return results
    }

    private fun normalizePathKey(file: File): String {
        return try {
            file.canonicalPath.lowercase(Locale.ROOT)
        } catch (_: Throwable) {
            file.absolutePath.lowercase(Locale.ROOT)
        }
    }

    internal fun parseDescriptorChildFiles(descriptor: File): List<File> {
        val out = ArrayList<File>()
        val parent = descriptor.parentFile ?: return out
        val ext = descriptor.extension.lowercase(Locale.ROOT)
        try {
            val text = descriptor.readText(StandardCharsets.UTF_8)
            for (rawLine in text.lineSequence()) {
                val line = rawLine.trim()
                if (line.isEmpty() || line.startsWith("#") || line.startsWith(";")) continue
                when (ext) {
                    "m3u" -> {
                        val ref = File(parent, line)
                        out.add(ref)
                        if (ref.exists() && (ref.extension.equals("cue", true) || ref.extension.equals("gdi", true))) {
                            out.addAll(parseDescriptorChildFiles(ref))
                        }
                    }
                    "cue" -> {
                        if (line.uppercase(Locale.ROOT).startsWith("FILE ")) {
                            val q1 = line.indexOf('"')
                            val q2 = line.lastIndexOf('"')
                            val name = if (q1 >= 0 && q2 > q1) {
                                line.substring(q1 + 1, q2)
                            } else {
                                line.split(Regex("\\s+")).getOrNull(1) ?: ""
                            }
                            if (name.isNotBlank()) out.add(File(parent, name))
                        }
                    }
                    "gdi" -> {
                        // Dreamcast GDI track line: <trackNum> <lba> <type> <sectorSize> <filename> <offset>
                        val q1 = line.indexOf('"')
                        val q2 = line.lastIndexOf('"')
                        if (q1 >= 0 && q2 > q1) {
                            out.add(File(parent, line.substring(q1 + 1, q2)))
                        } else {
                            val parts = line.split(Regex("\\s+"))
                            if (parts.size >= 5) {
                                out.add(File(parent, parts[4]))
                            }
                        }
                    }
                }
            }
        } catch (_: Throwable) {
        }
        return out
    }

    /**
     * Preserves original country region (`JP`, `US`, `EU`, `WORLD`, `KR`, etc.) even when
     * `[MOD - M. RUS]`, `[RUS]`, or `[T+Rus]` tags are present.
     */
    fun parseRegionAndRusMod(rawFileName: String): Pair<String, Boolean> {
        val upper = rawFileName.uppercase(Locale.ROOT)
        val isRus = upper.contains("MOD - M. RUS") ||
            upper.contains("[M. RUS]") ||
            upper.contains("[RUS]") ||
            upper.contains("(RUS)") ||
            upper.contains("(RUSSIA") ||
            upper.contains("T+RUS") ||
            upper.contains("T-RUS") ||
            Regex("[А-ЯЁа-яё]").containsMatchIn(rawFileName)

        val region = when {
            upper.contains("(JAPAN") || upper.contains("(JP)") || upper.contains("(J)") || upper.contains(", JAPAN)") -> "JP"
            upper.contains("(USA") || upper.contains("(US)") || upper.contains("(U)") -> "US"
            upper.contains("(EUROPE") || upper.contains("(EU)") || upper.contains("(E)") ||
                upper.contains("(UK)") || upper.contains("(GERMANY)") || upper.contains("(FRANCE)") -> "EU"
            upper.contains("(WORLD") || upper.contains("(W)") -> "WORLD"
            upper.contains("(KOREA") || upper.contains("(KR)") || upper.contains("(K)") -> "KR"
            upper.contains("(CHINA") || upper.contains("(TAIWAN") || upper.contains("(ASIA") -> "ASIA"
            upper.contains("(BRAZIL") || upper.contains("(BR)") -> "BR"
            else -> "WORLD"
        }
        return region to isRus
    }

    fun cleanDisplayTitle(rawStem: String): String {
        var s = rawStem
            .replace(Regex("\\[MOD\\s*-\\s*M\\.\\s*RUS\\]", RegexOption.IGNORE_CASE), "")
            .replace(Regex("\\[[^\\]]*\\]"), "")
            .replace(Regex("\\((USA|Japan|Europe|World|Korea|Russia|En,Fr,De[^)]*|Rev\\s*[A-Z0-9]+|v\\d[^)]*|Proto|Beta|Sample|Unl)\\)", RegexOption.IGNORE_CASE), "")
            .replace(Regex("\\s{2,}"), " ")
            .trim()
        if (s.isEmpty()) s = rawStem.trim()
        return s
    }

    /**
     * Resolves platform key via parent folder hints, archive peek, and binary disc/ROM header inspection.
     */
    fun detectPlatformKeyForFile(file: File, ext: String): String {
        // 1. Check parent folder names first (up to 2 levels, e.g., roms/ps1/game.chd)
        val parent1 = file.parentFile?.name ?: ""
        val parent2 = file.parentFile?.parentFile?.name ?: ""
        for (folderHint in listOf(parent1, parent2)) {
            val lower = folderHint.lowercase(Locale.ROOT)
            if (lower.isNotEmpty() && lower != "roms" && lower != "games" && lower != "storm retroarch") {
                val mapped = MobileCoreRegistry.normalizePlatformKey(lower, "")
                if (mapped.isNotEmpty()) return mapped
            }
        }

        // 2. Inspect .zip contents to distinguish zipped console ROMs vs MS-DOS vs Arcade MAME/FBNeo
        if (ext == "zip") {
            try {
                ZipFile(file).use { zf ->
                    val entries = zf.entries()
                    var hasDosExe = false
                    while (entries.hasMoreElements()) {
                        val entry = entries.nextElement()
                        if (entry.isDirectory) continue
                        val innerExt = entry.name.substringAfterLast('.', "").lowercase(Locale.ROOT)
                        if (innerExt in setOf("exe", "com", "bat", "dosz")) {
                            hasDosExe = true
                        } else if (innerExt != "zip" && innerExt != "txt" && innerExt != "nfo" && innerExt != "rom" && innerExt != "bin") {
                            val innerKey = MobileCoreRegistry.normalizePlatformKey("", innerExt)
                            if (innerKey.isNotEmpty()) return innerKey
                        }
                    }
                    if (hasDosExe) return "dos"
                }
            } catch (_: Throwable) {
            }
            return "arcade"
        }

        // 3. Disc & ambiguous binary formats (.cue, .gdi, .m3u, .iso, .bin, .img, .mdf, .chd)
        if (ext in setOf("cue", "gdi", "m3u", "iso", "bin", "img", "mdf", "chd")) {
            if (ext == "gdi") return "dreamcast"
            val targetBinary = if (ext == "cue" || ext == "m3u") {
                parseDescriptorChildFiles(file).firstOrNull { it.exists() && it.length() > 65536L } ?: file
            } else {
                file
            }
            val discKey = inspectBinaryDiscHeader(targetBinary)
            if (discKey.isNotEmpty()) return discKey
            if (ext == "cue" || ext == "chd" || ext == "m3u" || ext == "iso") return "ps1"
            if (ext == "bin") {
                return if (file.length() <= 16L * 1024L * 1024L) "genesis" else "ps1"
            }
        }

        return MobileCoreRegistry.normalizePlatformKey("", ext)
    }

    /**
     * Binary header signature detector distinguishing:
     * Sony PlayStation 1 vs PlayStation 2 vs PSP vs Sega Saturn vs Sega Dreamcast vs Sega Mega-CD vs GameCube/Wii vs 3DO vs Genesis.
     */
    fun inspectBinaryDiscHeader(file: File): String {
        if (!file.exists() || file.length() < 512L) return ""
        try {
            RandomAccessFile(file, "r").use { raf ->
                val head = ByteArray(minOf(65536L, raf.length()).toInt())
                raf.readFully(head)

                // GameCube (0xC2339F3D at 0x1C) & Wii (0x5D1C9EA3 at 0x18)
                if (head.size >= 0x20) {
                    if (head[0x1C] == 0xC2.toByte() && head[0x1D] == 0x33.toByte() &&
                        head[0x1E] == 0x9F.toByte() && head[0x1F] == 0x3D.toByte()
                    ) return "gamecube"
                    if (head[0x18] == 0x5D.toByte() && head[0x19] == 0x1C.toByte() &&
                        head[0x1A] == 0x9E.toByte() && head[0x1B] == 0xA3.toByte()
                    ) return "wii"
                }

                // 3DO Opera filesystem signature: 0x01 'Z' 'Z' 'Z' 'Z' 'Z' 0x01
                for (off in listOf(0, 0x10)) {
                    if (head.size > off + 7 &&
                        head[off] == 0x01.toByte() &&
                        head[off + 1] == 0x5A.toByte() &&
                        head[off + 2] == 0x5A.toByte() &&
                        head[off + 3] == 0x5A.toByte() &&
                        head[off + 4] == 0x5A.toByte() &&
                        head[off + 5] == 0x5A.toByte()
                    ) return "3do"
                }

                val headAscii = String(head, StandardCharsets.ISO_8859_1)
                when {
                    headAscii.contains("SEGA SEGASATURN") -> return "saturn"
                    headAscii.contains("SEGA SEGAKATANA") -> return "dreamcast"
                    headAscii.contains("SEGADISCSYSTEM") || headAscii.contains("SEGA DISCSYSTEM") -> return "segacd"
                    headAscii.contains("SEGA MEGA DRIVE") || headAscii.contains("SEGA GENESIS") -> return "genesis"
                    headAscii.contains("SEGA 32X") -> return "sega32x"
                    headAscii.contains("PSP GAME") -> return "psp"
                    headAscii.contains("BOOT2 =") || headAscii.contains("BOOT2=") -> return "ps2"
                }

                // Check ISO9660 Primary Volume Descriptor at 0x8000 (2048-byte sector) or 0x9310 (2352-byte raw sector)
                if (headAscii.contains("PLAYSTATION")) {
                    // Scan further for SYSTEM.CNF BOOT2 (PS2) vs BOOT (PS1), or size > 850 MB DVD
                    if (raf.length() > 850L * 1024L * 1024L) return "ps2"
                    if (raf.length() > 0x12000L) {
                        val scanBuf = ByteArray(minOf(131072L, raf.length() - 0x8000L).toInt())
                        raf.seek(0x8000L)
                        raf.readFully(scanBuf)
                        val dirAscii = String(scanBuf, StandardCharsets.ISO_8859_1)
                        if (dirAscii.contains("BOOT2")) return "ps2"
                        if (dirAscii.contains("PSP_GAME")) return "psp"
                    }
                    return "ps1"
                }
            }
        } catch (_: Throwable) {
        }
        return ""
    }

    // === Dynamic Filter Counters & Collection Filtering ===

    fun matchesCollection(item: MobileRomItem, collection: String): Boolean {
        return when (collection.uppercase(Locale.ROOT)) {
            COLLECTION_ALL, "" -> true
            COLLECTION_FAVORITES -> item.isFavorite
            COLLECTION_RUS_MODS -> item.isRussianMod
            COLLECTION_CLASSIC_8_16 -> CLASSIC_8_16_KEYS.contains(item.platformKey)
            COLLECTION_CD_3D -> CD_3D_KEYS.contains(item.platformKey)
            COLLECTION_DOS_PC -> DOS_PC_KEYS.contains(item.platformKey)
            COLLECTION_RECENT -> item.lastPlayedMs > 0L
            else -> true
        }
    }

    fun computeFilterCounts(
        allRoms: List<MobileRomItem>,
        activeCollection: String = COLLECTION_ALL,
        activePlatform: String? = null
    ): LibraryFilterCounts {
        val smartMap = LinkedHashMap<String, Int>()
        smartMap[COLLECTION_ALL] = allRoms.size
        smartMap[COLLECTION_FAVORITES] = allRoms.count { it.isFavorite }
        smartMap[COLLECTION_RUS_MODS] = allRoms.count { it.isRussianMod }
        smartMap[COLLECTION_CLASSIC_8_16] = allRoms.count { CLASSIC_8_16_KEYS.contains(it.platformKey) }
        smartMap[COLLECTION_CD_3D] = allRoms.count { CD_3D_KEYS.contains(it.platformKey) }
        smartMap[COLLECTION_DOS_PC] = allRoms.count { DOS_PC_KEYS.contains(it.platformKey) }
        smartMap[COLLECTION_RECENT] = allRoms.count { it.lastPlayedMs > 0L }

        val collectionFiltered = allRoms.filter { matchesCollection(it, activeCollection) }

        val platformCounts = LinkedHashMap<String, Int>()
        val platformKeyCounts = LinkedHashMap<String, Int>()
        for (rom in collectionFiltered) {
            platformCounts[rom.platform] = (platformCounts[rom.platform] ?: 0) + 1
            platformKeyCounts[rom.platformKey] = (platformKeyCounts[rom.platformKey] ?: 0) + 1
        }

        val normActivePlat = MobileCoreRegistry.normalizePlatformKey(activePlatform, "")
        val platformFiltered = if (normActivePlat.isEmpty()) {
            collectionFiltered
        } else {
            collectionFiltered.filter { it.platformKey == normActivePlat }
        }

        val coreCounts = LinkedHashMap<String, Int>()
        if (normActivePlat.isNotEmpty()) {
            val strictCores = MobileCoreRegistry.getCompatibleCoresForPlatform(normActivePlat, "")
            for (core in strictCores) {
                coreCounts[core.id] = platformFiltered.count {
                    it.assignedCoreId == core.id || core.platformKeys.contains(it.platformKey)
                }
            }
        } else {
            for (rom in platformFiltered) {
                if (rom.assignedCoreId.isNotEmpty()) {
                    coreCounts[rom.assignedCoreId] = (coreCounts[rom.assignedCoreId] ?: 0) + 1
                }
            }
        }

        return LibraryFilterCounts(
            smartCollections = smartMap,
            platformCounts = platformCounts,
            platformKeyCounts = platformKeyCounts,
            coreCounts = coreCounts
        )
    }

    fun filterRoms(
        allRoms: List<MobileRomItem>,
        collection: String = COLLECTION_ALL,
        platform: String? = null,
        coreId: String? = null,
        searchQuery: String = ""
    ): List<MobileRomItem> {
        val normPlat = MobileCoreRegistry.normalizePlatformKey(platform, "")
        val q = searchQuery.trim().lowercase(Locale.ROOT)

        val filtered = allRoms.filter { rom ->
            if (!matchesCollection(rom, collection)) return@filter false
            if (normPlat.isNotEmpty() && rom.platformKey != normPlat) return@filter false
            if (!coreId.isNullOrBlank() && !coreId.equals("ALL", true)) {
                val core = MobileCoreRegistry.findCoreById(coreId)
                val matchAssigned = rom.assignedCoreId.equals(coreId, true)
                val matchCompat = core?.platformKeys?.contains(rom.platformKey) == true
                if (!matchAssigned && !matchCompat) return@filter false
            }
            if (q.isNotEmpty()) {
                val hay = "${rom.name} ${rom.rawNoIntroName} ${rom.platform} ${rom.regionCode}".lowercase(Locale.ROOT)
                if (!hay.contains(q)) return@filter false
            }
            true
        }

        return if (collection.equals(COLLECTION_RECENT, ignoreCase = true)) {
            filtered.sortedByDescending { it.lastPlayedMs }
        } else {
            filtered
        }
    }

    // === Exact Regional Cover Art Resolver (Zero 404 Storms) ===

    fun getExpectedCoverCacheFile(platformKey: String, cleanName: String, regionCode: String): File {
        val safePlat = sanitizeFileComponent(platformKey.ifEmpty { "misc" })
        val safeName = sanitizeFileComponent(cleanName.ifEmpty { "rom" })
        val safeReg = sanitizeFileComponent(regionCode.ifEmpty { "WORLD" })
        return File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "thumbnails/$safePlat/${safeName}__${safeReg}.png")
    }

    private fun sanitizeFileComponent(raw: String): String {
        return raw.replace(Regex("[\\\\/:*?\"<>|]"), "_").replace(Regex("\\s+"), " ").trim()
    }

    fun fetchCoverArtAsync(
        context: Context,
        rom: MobileRomItem,
        prefs: SharedPreferences? = null,
        onResolved: (MobileRomItem, File?) -> Unit
    ) {
        val primaryCache = getExpectedCoverCacheFile(rom.platformKey, rom.name, rom.regionCode)
        if (primaryCache.exists() && primaryCache.length() > 64L) {
            rom.coverPath = primaryCache.absolutePath
            mainHandler.post { onResolved(rom, primaryCache) }
            return
        }

        val appCtx = context.applicationContext
        coverExecutor.execute {
            try {
                // Check sibling local artwork next to ROM first
                val romFile = File(rom.filePath)
                val parent = romFile.parentFile
                if (parent != null && parent.exists()) {
                    for (suffix in listOf(".png", ".jpg", "-thumb.png", "_cover.png")) {
                        val sibling = File(parent, "${rom.rawNoIntroName}$suffix")
                        if (sibling.exists() && sibling.length() > 128L) {
                            rom.coverPath = sibling.absolutePath
                            if (prefs != null) saveRomItemState(prefs, rom)
                            mainHandler.post { onResolved(rom, sibling) }
                            return@execute
                        }
                    }
                }

                val libretroSys = LIBRETRO_THUMBNAIL_SYSTEM_MAP[rom.platformKey]
                if (libretroSys == null) {
                    mainHandler.post { onResolved(rom, null) }
                    return@execute
                }

                val entries = getOrFetchBoxartDirectoryListing(appCtx, libretroSys)
                if (entries.isEmpty()) {
                    mainHandler.post { onResolved(rom, null) }
                    return@execute
                }

                val bestEntryName = selectBestRegionalBoxartEntry(rom, entries)
                if (bestEntryName == null) {
                    mainHandler.post { onResolved(rom, null) }
                    return@execute
                }

                val encSys = encodeUrlPathSegment(libretroSys)
                val encFile = encodeUrlPathSegment(bestEntryName)
                val imageUrl = "$THUMBNAILS_SERVER/$encSys/Named_Boxarts/$encFile"

                val bytes = downloadBytes(imageUrl, maxBytes = 8 * 1024 * 1024)
                if (bytes != null && isValidImageHeader(bytes)) {
                    val outFile = try {
                        primaryCache.parentFile?.mkdirs()
                        FileOutputStream(primaryCache).use { it.write(bytes) }
                        primaryCache
                    } catch (_: Throwable) {
                        val internalFallback = File(
                            appCtx.filesDir,
                            "thumbnails/${sanitizeFileComponent(rom.platformKey)}/${sanitizeFileComponent(rom.name)}__${rom.regionCode}.png"
                        )
                        internalFallback.parentFile?.mkdirs()
                        FileOutputStream(internalFallback).use { it.write(bytes) }
                        internalFallback
                    }
                    rom.coverPath = outFile.absolutePath
                    if (prefs != null) saveRomItemState(prefs, rom)
                    mainHandler.post { onResolved(rom, outFile) }
                    return@execute
                }
            } catch (_: Throwable) {
            }
            mainHandler.post { onResolved(rom, null) }
        }
    }

    private fun getOrFetchBoxartDirectoryListing(context: Context, libretroSys: String): List<String> {
        boxartListingCache[libretroSys]?.let { return it }

        val lock = listingLocks.getOrPut(libretroSys) { Any() }
        synchronized(lock) {
            boxartListingCache[libretroSys]?.let { return it }

            val safeSys = sanitizeFileComponent(libretroSys)
            val diskCache = File(context.cacheDir, "libretro_boxart_index/$safeSys.txt")
            if (diskCache.exists() && diskCache.length() > 32L) {
                try {
                    val cachedLines = diskCache.readLines(StandardCharsets.UTF_8).filter { it.isNotBlank() }
                    if (cachedLines.isNotEmpty()) {
                        boxartListingCache[libretroSys] = cachedLines
                        return cachedLines
                    }
                } catch (_: Throwable) {
                }
            }

            val encSys = encodeUrlPathSegment(libretroSys)
            val listingUrl = "$THUMBNAILS_SERVER/$encSys/Named_Boxarts/"
            val htmlBytes = downloadBytes(listingUrl, maxBytes = 12 * 1024 * 1024) ?: return emptyList()
            val html = String(htmlBytes, StandardCharsets.UTF_8)

            val parsed = ArrayList<String>(1024)
            val hrefRegex = Regex("href=\"([^\"]+\\.png)\"", RegexOption.IGNORE_CASE)
            for (match in hrefRegex.findAll(html)) {
                val rawHref = match.groupValues[1]
                if (rawHref.contains("/") || rawHref.startsWith("?")) continue
                val decoded = try {
                    URLDecoder.decode(rawHref, "UTF-8")
                } catch (_: Throwable) {
                    rawHref
                }
                parsed.add(decoded)
            }

            if (parsed.isNotEmpty()) {
                boxartListingCache[libretroSys] = parsed
                try {
                    diskCache.parentFile?.mkdirs()
                    diskCache.writeText(parsed.joinToString("\n"), StandardCharsets.UTF_8)
                } catch (_: Throwable) {
                }
            }
            return parsed
        }
    }

    /**
     * Scores Libretro Named_Boxarts filenames against both `~` dual titles and strict region tags
     * (`JP` vs `US` vs `EU` vs `KR`) so Japanese Famicom games never receive US/EU covers.
     */
    internal fun selectBestRegionalBoxartEntry(rom: MobileRomItem, candidates: List<String>): String? {
        val queryTitles = extractTitleVariants(rom.rawNoIntroName) + extractTitleVariants(rom.name)
        val normQueries = queryTitles.map { normalizeTitleForMatch(it) }.filter { it.isNotEmpty() }.distinct()
        if (normQueries.isEmpty()) return null

        var bestName: String? = null
        var bestScore = -1

        for (entry in candidates) {
            val stem = entry.removeSuffix(".png").removeSuffix(".PNG")
            val entryBasePart = stem.substringBefore('(').trim()
            val entryVariants = extractTitleVariants(entryBasePart).map { normalizeTitleForMatch(it) }

            var titleScore = -1
            for (q in normQueries) {
                for (ev in entryVariants) {
                    if (ev.isEmpty()) continue
                    if (ev == q) {
                        titleScore = max(titleScore, 1000)
                    } else if (ev.startsWith(q) || q.startsWith(ev)) {
                        val lenDiff = kotlin.math.abs(ev.length - q.length)
                        if (lenDiff <= 6) {
                            titleScore = max(titleScore, 820 - lenDiff * 15)
                        }
                    }
                }
            }
            if (titleScore < 0) continue

            val upperStem = stem.uppercase(Locale.ROOT)
            var regionScore = 0
            when (rom.regionCode) {
                "JP" -> {
                    if (upperStem.contains("(JAPAN")) regionScore += 500
                    else if (upperStem.contains("JAPAN")) regionScore += 380
                    else if (upperStem.contains("(WORLD")) regionScore += 180
                    else if (upperStem.contains("(USA") || upperStem.contains("(EUROPE")) regionScore -= 120
                }
                "US" -> {
                    if (upperStem.contains("(USA")) regionScore += 500
                    else if (upperStem.contains("USA")) regionScore += 380
                    else if (upperStem.contains("(WORLD")) regionScore += 320
                    else if (upperStem.contains("(EUROPE")) regionScore += 140
                }
                "EU" -> {
                    if (upperStem.contains("(EUROPE")) regionScore += 500
                    else if (upperStem.contains("EUROPE")) regionScore += 380
                    else if (upperStem.contains("(WORLD")) regionScore += 300
                    else if (upperStem.contains("(USA")) regionScore += 160
                }
                "KR" -> {
                    if (upperStem.contains("(KOREA")) regionScore += 500
                    else if (upperStem.contains("(JAPAN") || upperStem.contains("(USA")) regionScore += 120
                }
                else -> {
                    if (upperStem.contains("(WORLD") || upperStem.contains("(USA")) regionScore += 280
                    else if (upperStem.contains("(EUROPE") || upperStem.contains("(JAPAN")) regionScore += 200
                }
            }

            if (upperStem.contains("BETA") || upperStem.contains("PROTO") || upperStem.contains("DEMO")) {
                regionScore -= 150
            }

            val total = titleScore + regionScore
            if (total > bestScore) {
                bestScore = total
                bestName = entry
            }
        }

        return bestName
    }

    internal fun extractTitleVariants(rawTitle: String): List<String> {
        val withoutBrackets = rawTitle
            .replace(Regex("\\[[^\\]]*\\]"), " ")
            .replace(Regex("\\([^)]*\\)"), " ")
            .trim()
        val parts = withoutBrackets.split('~').map { it.trim() }.filter { it.isNotEmpty() }
        return if (parts.size > 1) listOf(withoutBrackets) + parts else listOf(withoutBrackets)
    }

    internal fun normalizeTitleForMatch(title: String): String {
        return title.lowercase(Locale.ROOT)
            .replace("&", "and")
            .replace(Regex("[^a-z0-9а-яё]+"), "")
    }

    private fun encodeUrlPathSegment(segment: String): String {
        // Libretro thumbnail server sanitizes & * / : ` < > ? \ | as _
        val libretroSafe = segment.replace(Regex("[&*/:`<>?\\\\|]"), "_")
        return URLEncoder.encode(libretroSafe, "UTF-8").replace("+", "%20")
    }

    private fun downloadBytes(url: String, maxBytes: Int): ByteArray? {
        var conn: HttpURLConnection? = null
        return try {
            conn = MobileCoreRegistry.openFollowingRedirects(url)
            if (conn.responseCode !in 200..299) return null
            BufferedInputStream(conn.inputStream, 16 * 1024).use { input ->
                val out = ByteArrayOutputStream()
                val buf = ByteArray(16 * 1024)
                var total = 0
                while (true) {
                    val n = input.read(buf)
                    if (n <= 0) break
                    total += n
                    if (total > maxBytes) return null
                    out.write(buf, 0, n)
                }
                out.toByteArray()
            }
        } catch (_: Throwable) {
            null
        } finally {
            try { conn?.disconnect() } catch (_: Throwable) {}
        }
    }

    fun isValidImageHeader(bytes: ByteArray): Boolean {
        if (bytes.size < 16) return false
        // PNG
        if (bytes[0] == 0x89.toByte() && bytes[1] == 0x50.toByte() && bytes[2] == 0x4E.toByte() && bytes[3] == 0x47.toByte()) return true
        // JPEG
        if (bytes[0] == 0xFF.toByte() && bytes[1] == 0xD8.toByte()) return true
        // WebP
        if (bytes[0] == 0x52.toByte() && bytes[1] == 0x49.toByte() && bytes[2] == 0x46.toByte() && bytes[3] == 0x46.toByte() &&
            bytes[8] == 0x57.toByte() && bytes[9] == 0x45.toByte() && bytes[10] == 0x42.toByte() && bytes[11] == 0x50.toByte()
        ) return true
        return false
    }

    // === Non-Destructive IPS / BPS Soft-Patcher & Multi-Disc .m3u Generator ===

    /**
     * Checks for a matching `.ips` or `.bps` patch alongside `romFile` or in
     * `/storage/emulated/0/STORM RETROARCH/patches/` and returns a soft-patched temporary ROM
     * in `context.cacheDir/soft_patched/` without altering the user's original ROM.
     */
    fun applyIpsOrBpsIfPresent(context: Context, romFile: File): File {
        if (!romFile.exists() || romFile.length() > 64L * 1024L * 1024L) return romFile

        val stem = romFile.nameWithoutExtension
        val parent = romFile.parentFile
        val globalPatchesDir = File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "patches")

        val patchCandidates = listOfNotNull(
            parent?.let { File(it, "$stem.bps") },
            parent?.let { File(it, "$stem.ips") },
            File(globalPatchesDir, "$stem.bps"),
            File(globalPatchesDir, "$stem.ips")
        )

        val foundPatch = patchCandidates.firstOrNull { it.exists() && it.length() > 8L } ?: return romFile

        return try {
            val sourceBytes = romFile.readBytes()
            val patchBytes = foundPatch.readBytes()
            val patchedBytes = if (foundPatch.extension.equals("bps", ignoreCase = true)) {
                applyBpsPatch(sourceBytes, patchBytes)
            } else {
                applyIpsPatch(sourceBytes, patchBytes)
            } ?: return romFile

            val outDir = File(context.cacheDir, "soft_patched")
            outDir.mkdirs()
            val outFile = File(outDir, romFile.name)
            FileOutputStream(outFile).use { it.write(patchedBytes) }
            outFile
        } catch (_: Throwable) {
            romFile
        }
    }

    internal fun applyIpsPatch(source: ByteArray, patch: ByteArray): ByteArray? {
        if (patch.size < 8) return null
        if (String(patch, 0, 5, StandardCharsets.US_ASCII) != "PATCH") return null

        var out = source.copyOf()
        var pos = 5

        fun ensureCapacity(required: Int) {
            if (required > out.size) {
                out = out.copyOf(required)
            }
        }

        while (pos + 3 <= patch.size) {
            val offset = ((patch[pos].toInt() and 0xFF) shl 16) or
                ((patch[pos + 1].toInt() and 0xFF) shl 8) or
                (patch[pos + 2].toInt() and 0xFF)
            pos += 3

            if (offset == 0x454F46) { // "EOF"
                return out
            }
            if (pos + 2 > patch.size) break
            val size = ((patch[pos].toInt() and 0xFF) shl 8) or (patch[pos + 1].toInt() and 0xFF)
            pos += 2

            if (size == 0) {
                if (pos + 3 > patch.size) break
                val rleLen = ((patch[pos].toInt() and 0xFF) shl 8) or (patch[pos + 1].toInt() and 0xFF)
                val value = patch[pos + 2]
                pos += 3
                ensureCapacity(offset + rleLen)
                out.fill(value, offset, offset + rleLen)
            } else {
                if (pos + size > patch.size) break
                ensureCapacity(offset + size)
                System.arraycopy(patch, pos, out, offset, size)
                pos += size
            }
        }
        return out
    }

    internal fun applyBpsPatch(source: ByteArray, patch: ByteArray): ByteArray? {
        if (patch.size < 16) return null
        if (String(patch, 0, 4, StandardCharsets.US_ASCII) != "BPS1") return null

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

        val expectedSourceSize = readVarInt().toInt()
        val targetSize = readVarInt().toInt()
        val metadataSize = readVarInt().toInt()
        if (expectedSourceSize < 0 || targetSize <= 0 || targetSize > 128 * 1024 * 1024) return null
        pos += metadataSize
        if (pos > patch.size - 12) return null

        val target = ByteArray(targetSize)
        var outputOffset = 0
        var sourceRelOffset = 0
        var targetRelOffset = 0
        val endLimit = patch.size - 12

        while (pos < endLimit && outputOffset < targetSize) {
            val cmd = readVarInt()
            val length = ((cmd ushr 2) + 1L).toInt()
            when ((cmd and 3L).toInt()) {
                0 -> { // SourceRead
                    for (i in 0 until length) {
                        target[outputOffset] = if (outputOffset < source.size) source[outputOffset] else 0
                        outputOffset++
                    }
                }
                1 -> { // TargetRead
                    for (i in 0 until length) {
                        target[outputOffset++] = patch[pos++]
                    }
                }
                2 -> { // SourceCopy
                    val d = readVarInt()
                    val delta = (if ((d and 1L) != 0L) -1 else 1) * (d ushr 1).toInt()
                    sourceRelOffset += delta
                    for (i in 0 until length) {
                        target[outputOffset++] = source[sourceRelOffset++]
                    }
                }
                3 -> { // TargetCopy
                    val d = readVarInt()
                    val delta = (if ((d and 1L) != 0L) -1 else 1) * (d ushr 1).toInt()
                    targetRelOffset += delta
                    for (i in 0 until length) {
                        target[outputOffset++] = target[targetRelOffset++]
                    }
                }
            }
        }
        return target
    }

    /**
     * Generates a multi-disc `.m3u` playlist referencing relative disc filenames in sorted order.
     */
    fun createM3uPlaylist(discFiles: List<File>, customPlaylistName: String? = null): File? {
        if (discFiles.size < 2) return null
        val sorted = discFiles.sortedBy { it.name.lowercase(Locale.ROOT) }
        val baseDir = sorted.first().parentFile ?: return null
        val cleanTitle = customPlaylistName?.takeIf { it.isNotBlank() }
            ?: sorted.first().nameWithoutExtension
                .replace(Regex("\\s*\\((Disc|Disk|CD)\\s*\\d+[^)]*\\)", RegexOption.IGNORE_CASE), "")
                .trim()

        val m3uFile = File(baseDir, "$cleanTitle.m3u")
        return try {
            val content = sorted.joinToString("\n") { it.name } + "\n"
            m3uFile.writeText(content, StandardCharsets.UTF_8)
            m3uFile
        } catch (_: Throwable) {
            null
        }
    }
}
