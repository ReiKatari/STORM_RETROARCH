package com.storm.retroarch

import android.content.Context
import android.content.SharedPreferences
import android.net.Uri
import android.os.Handler
import android.os.Looper
import android.provider.DocumentsContract
import android.provider.OpenableColumns
import androidx.documentfile.provider.DocumentFile
import org.json.JSONArray
import org.json.JSONObject
import java.io.BufferedInputStream
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.FileOutputStream
import java.io.RandomAccessFile
import java.net.HttpURLConnection
import java.net.URLDecoder
import java.net.URLEncoder
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone
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
    var coverPath: String? = null,
    val id: String = MobileLibraryAndCoverEngine.computeRomId(filePath),
    var completionStatus: String = "Не начато",
    var starRating: Int = 0,
    var userNotes: String = "",
    var customCollection: String = "",
    var totalPlaySeconds: Long = 0L,
    var launchCount: Int = 0,
    var lastPlayedIso: String = "",
    val year: String = "Классика",
    val translationBadge: String = "",
    val audioBadge: String = "",
    val versionFilesBadge: String = ""
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

    val coverUrl: String
        get() {
            val cp = coverPath
            return if (!cp.isNullOrBlank() && File(cp).exists()) {
                MobileLibraryAndCoverEngine.toStormLocalUrl(cp)
            } else ""
        }

    val slotYear: String
        get() = year.ifBlank { "Классика" }

    val slotRegion: String
        get() = regionCode.ifBlank { "WORLD" }

    val slotTranslation: String
        get() = translationBadge.ifBlank { if (isRussianMod) "RUS" else "ENG" }

    val slotAudio: String
        get() = audioBadge.ifBlank { if (isRussianMod) "Оригинал" else if (regionCode == "JP") "JPN" else "ENG" }

    val slotFiles: String
        get() = versionFilesBadge.ifBlank { if (isRussianMod) "1G+1M" else "1G • ${extension.uppercase(Locale.ROOT)}" }

    val slotRating: String
        get() = if (starRating > 0) "$starRating/5" else "0/5"

    val slotLaunch: String
        get() = when {
            totalPlaySeconds >= 60L -> "${totalPlaySeconds / 60L} мин • $completionStatus"
            launchCount > 0 -> "$launchCount зап."
            else -> completionStatus
        }
}

data class RomParsedMeta(
    val region: String,
    val isRussianMod: Boolean,
    val gameCode: String,
    val revision: String
)

data class RomHeaderInfo(
    val internalTitle: String,
    val gameCode: String,
    val region: String,
    val revision: String
)

data class WatchedFolderEntry(
    val pathOrUri: String,
    var recursive: Boolean = true
)

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
    private const val KEY_CUSTOM_DIRS = "custom_scan_dirs_json"
    private const val KEY_CUSTOM_DIR_MODES = "custom_scan_dir_modes_json"
    private const val KEY_SINGLE_ROMS = "single_picked_roms_json"

    private val coverExecutor = Executors.newFixedThreadPool(3)
    private val mainHandler = Handler(Looper.getMainLooper())

    private val boxartListingCache = ConcurrentHashMap<String, List<String>>()
    private val listingLocks = ConcurrentHashMap<String, Any>()

    private val CLASSIC_8_16_KEYS = setOf(
        "nes", "fds", "snes", "gb", "gbc", "gb_gbc", "gba", "vb", "pokemini", "gw",
        "genesis", "sega32x", "sms_gg", "sg1000", "segapico",
        "pce", "ngp", "neogeo", "cps", "arcade", "wonderswan",
        "vectrex", "coleco", "intellivision", "odyssey2",
        "atari2600", "atari5200", "atari7800", "lynx", "pico8", "tic80", "fantasy"
    )

    private val CD_3D_KEYS = setOf(
        "n64", "gamecube", "wii", "gamecube_wii", "nds", "3ds",
        "ps1", "ps2", "ps3", "psp", "psvita",
        "segacd", "saturn", "dreamcast", "naomi",
        "pcecd", "pcfx", "neogeocd", "3do", "cdi", "jaguar"
    )

    private val DOS_PC_KEYS = setOf(
        "dos", "scummvm", "amiga", "zxspectrum", "zx81", "c64", "msx",
        "x68000", "x1", "pc98", "pc88", "amstrad", "atarist", "atari800", "bk0010", "doom", "ports"
    )

    private val IGNORED_DIR_NAMES = setOf(
        "cores", "system", "bios", "cheats", "thumbnails", "bezels", "config",
        "shaders", "saves", "states", "screenshots", "autoconfig", "updates",
        "patches", "backups", ".git", "android", "android_data"
    )

    // Unambiguous console/handheld/disc ROM extensions safe to pick up directly in /storage/emulated/0/Download
    private val UNAMBIGUOUS_ROM_EXTENSIONS = setOf(
        "nes", "fds", "unf", "unif", "sfc", "smc", "fig", "swc", "z64", "n64", "v64",
        "gb", "gbc", "dmg", "gba", "agb", "nds", "dsi", "3ds", "cci", "cxi", "vb", "min",
        "md", "gen", "smd", "32x", "sms", "gg", "sg", "cdi", "gdi",
        "chd", "pbp", "cso", "cue", "m3u", "pce", "sgx", "pcfx", "ngp", "ngc", "ngpc",
        "ws", "wsc", "vec", "col", "a26", "a52", "a78", "lnx", "j64", "jag",
        "adf", "adz", "dms", "d64", "t64", "tap", "tzx", "z80", "scl", "trd",
        "dim", "hdi", "nhd", "dosz", "wad"
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
        "channelf" to "Fairchild - Channel F",
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
        "c64" to "Commodore - 64",
        "zxspectrum" to "Sinclair - ZX Spectrum",
        "msx" to "Microsoft - MSX",
        "amstrad" to "Amstrad - CPC",
        "pc98" to "NEC - PC-98",
        "x68000" to "Sharp - X68000",
        "doom" to "DOOM"
    )

    fun computeRomId(filePath: String): String {
        return try {
            val md = MessageDigest.getInstance("MD5")
            val digest = md.digest(filePath.toByteArray(StandardCharsets.UTF_8))
            digest.take(8).joinToString("") { "%02x".format(it) }
        } catch (_: Throwable) {
            filePath.hashCode().toUInt().toString(16)
        }
    }

    fun toStormLocalUrl(absFilePath: String): String {
        if (absFilePath.isBlank()) return ""
        val encoded = URLEncoder.encode(absFilePath, "UTF-8")
        return "https://stormlocal.app/file?path=$encoded"
    }

    fun getMetaPrefs(context: Context): SharedPreferences {
        return context.applicationContext.getSharedPreferences(PREFS_ROM_META, Context.MODE_PRIVATE)
    }

    fun getThumbnailsRootDir(context: Context? = null): File {
        val pub = File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "thumbnails")
        try {
            if (!pub.exists()) pub.mkdirs()
        } catch (_: Throwable) {
        }
        if (pub.exists() && pub.canWrite()) return pub
        if (context != null) {
            val internalDir = File(context.applicationContext.filesDir, "thumbnails")
            if (!internalDir.exists()) internalDir.mkdirs()
            return internalDir
        }
        return pub
    }

    fun getRomsStagingDir(context: Context): File {
        val pub = File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "roms/imported")
        try {
            if (!pub.exists()) pub.mkdirs()
            if (pub.exists() && pub.canWrite()) return pub
        } catch (_: Throwable) {
        }
        val fallback = File(context.applicationContext.filesDir, "roms/imported")
        fallback.mkdirs()
        return fallback
    }

    // === Persisted Custom Directories & Single Picked ROMs ===

    private val foldersLock = Any()

    private fun getWatchedFoldersMirrorFiles(context: Context): List<File> {
        return listOf(
            File(context.applicationContext.filesDir, "playlists/storm_watched_folders.json"),
            File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "playlists/storm_watched_folders.json")
        )
    }

    private fun writeWatchedFoldersMirrorLocked(context: Context, entries: List<WatchedFolderEntry>) {
        val arr = JSONArray()
        for (e in entries) {
            arr.put(JSONObject().put("path", e.pathOrUri).put("recursive", e.recursive))
        }
        val jsonText = arr.toString()
        for (file in getWatchedFoldersMirrorFiles(context)) {
            try {
                file.parentFile?.mkdirs()
                val tmp = File(file.parentFile, "${file.name}.tmp")
                tmp.writeText(jsonText, StandardCharsets.UTF_8)
                if (file.exists()) file.delete()
                if (!tmp.renameTo(file)) {
                    file.writeText(jsonText, StandardCharsets.UTF_8)
                    tmp.delete()
                }
            } catch (_: Throwable) {
            }
        }
    }

    fun getPersistedScanFolderEntries(context: Context): List<WatchedFolderEntry> {
        synchronized(foldersLock) {
            val prefs = getMetaPrefs(context)
            val map = LinkedHashMap<String, WatchedFolderEntry>()

            fun addOrMerge(path: String, rec: Boolean) {
                val clean = path.trim()
                if (clean.isEmpty()) return
                val existing = map[clean]
                if (existing == null) {
                    map[clean] = WatchedFolderEntry(clean, rec)
                }
            }

            // 1. Primary structured mode array in SharedPreferences
            try {
                val rawModes = prefs.getString(KEY_CUSTOM_DIR_MODES, "[]") ?: "[]"
                val arr = JSONArray(rawModes)
                for (i in 0 until arr.length()) {
                    val obj = arr.optJSONObject(i) ?: continue
                    val p = obj.optString("path", "").trim()
                    val rec = obj.optBoolean("recursive", true)
                    if (p.isNotEmpty()) addOrMerge(p, rec)
                }
            } catch (_: Throwable) {
            }

            // 2. Legacy plain folder string array
            try {
                val raw = prefs.getString(KEY_CUSTOM_DIRS, "[]") ?: "[]"
                val arr = JSONArray(raw)
                for (i in 0 until arr.length()) {
                    val s = arr.optString(i, "").trim()
                    if (s.isNotEmpty()) addOrMerge(s, true)
                }
            } catch (_: Throwable) {
            }

            // 3. Mirror files (survives app kill / reinstall backup restore)
            for (mirror in getWatchedFoldersMirrorFiles(context)) {
                try {
                    if (!mirror.exists() || !mirror.canRead()) continue
                    val arr = JSONArray(mirror.readText(StandardCharsets.UTF_8))
                    for (i in 0 until arr.length()) {
                        val obj = arr.optJSONObject(i) ?: continue
                        val p = obj.optString("path", "").trim()
                        val rec = obj.optBoolean("recursive", true)
                        if (p.isNotEmpty()) addOrMerge(p, rec)
                    }
                } catch (_: Throwable) {
                }
            }

            return map.values.toList()
        }
    }

    private fun saveWatchedFolderEntriesLocked(context: Context, entries: List<WatchedFolderEntry>) {
        val modesArr = JSONArray()
        val legacyArr = JSONArray()
        for (e in entries) {
            modesArr.put(JSONObject().put("path", e.pathOrUri).put("recursive", e.recursive))
            legacyArr.put(e.pathOrUri)
        }
        getMetaPrefs(context).edit()
            .putString(KEY_CUSTOM_DIR_MODES, modesArr.toString())
            .putString(KEY_CUSTOM_DIRS, legacyArr.toString())
            .commit()
        writeWatchedFoldersMirrorLocked(context, entries)
    }

    fun getPersistedScanFolders(context: Context): List<String> {
        return getPersistedScanFolderEntries(context).map { it.pathOrUri }
    }

    fun addPersistedScanFolder(context: Context, pathOrUri: String, recursive: Boolean = true) {
        val clean = pathOrUri.trim()
        if (clean.isEmpty()) return
        synchronized(foldersLock) {
            val current = getPersistedScanFolderEntries(context).toMutableList()
            val idx = current.indexOfFirst { it.pathOrUri.equals(clean, ignoreCase = false) }
            if (idx >= 0) {
                current[idx].recursive = recursive
            } else {
                current.add(WatchedFolderEntry(clean, recursive))
            }
            saveWatchedFolderEntriesLocked(context, current)
        }
    }

    fun setPersistedScanFolderRecursive(context: Context, pathOrUri: String, recursive: Boolean): Boolean {
        val clean = pathOrUri.trim()
        if (clean.isEmpty()) return false
        synchronized(foldersLock) {
            val current = getPersistedScanFolderEntries(context).toMutableList()
            val target = current.firstOrNull { it.pathOrUri == clean } ?: return false
            target.recursive = recursive
            saveWatchedFolderEntriesLocked(context, current)
            return true
        }
    }

    fun removePersistedScanFolder(context: Context, pathOrUri: String): Boolean {
        val clean = pathOrUri.trim()
        synchronized(foldersLock) {
            val current = getPersistedScanFolderEntries(context).toMutableList()
            val removed = current.removeAll { it.pathOrUri == clean }
            if (removed) {
                saveWatchedFolderEntriesLocked(context, current)
            }
            return removed
        }
    }

    fun getPersistedSingleRoms(context: Context): List<String> {
        val raw = getMetaPrefs(context).getString(KEY_SINGLE_ROMS, "[]") ?: "[]"
        val out = ArrayList<String>()
        try {
            val arr = JSONArray(raw)
            for (i in 0 until arr.length()) {
                val p = arr.optString(i, "").trim()
                if (p.isNotEmpty() && File(p).exists() && !out.contains(p)) out.add(p)
            }
        } catch (_: Throwable) {
        }
        return out
    }

    private fun addPersistedSingleRom(context: Context, filePath: String) {
        val current = getPersistedSingleRoms(context).toMutableList()
        if (!current.contains(filePath)) {
            current.add(filePath)
            val arr = JSONArray()
            current.forEach { arr.put(it) }
            getMetaPrefs(context).edit().putString(KEY_SINGLE_ROMS, arr.toString()).commit()
        }
    }

    // === SAF Tree & Document URI Real Path Resolver + Importer ===

    fun resolveTreeOrDocumentUriToRealFile(uri: Uri): File? {
        return try {
            val docId = when {
                DocumentsContract.isDocumentUri(null, uri) -> DocumentsContract.getDocumentId(uri)
                uri.path?.contains("/tree/") == true -> DocumentsContract.getTreeDocumentId(uri)
                else -> null
            } ?: return null

            if (docId.startsWith("raw:")) {
                val f = File(docId.removePrefix("raw:"))
                if (f.exists()) return f
            }

            val parts = docId.split(":", limit = 2)
            if (parts.isNotEmpty()) {
                val vol = parts[0]
                val rel = parts.getOrNull(1)?.trimStart('/') ?: ""
                val rootPath = if (vol.equals("primary", ignoreCase = true) || vol.equals("home", ignoreCase = true)) {
                    "/storage/emulated/0"
                } else {
                    "/storage/$vol"
                }
                val candidate = if (rel.isEmpty()) File(rootPath) else File(rootPath, rel)
                if (candidate.exists()) return candidate
            }
            null
        } catch (_: Throwable) {
            null
        }
    }

    fun importSingleRomUri(context: Context, uri: Uri): MobileRomItem? {
        val appCtx = context.applicationContext
        MobileCoreRegistry.init(appCtx)

        val directFile = resolveDirectDocumentUri(uri)
        val finalFile = if (directFile != null && directFile.exists() && directFile.canRead()) {
            directFile
        } else {
            val name = queryUriDisplayName(appCtx, uri) ?: uri.lastPathSegment?.substringAfterLast('/') ?: "game.rom"
            val safeName = name.replace(Regex("[\\\\/:*?\"<>|]"), "_")
            val dest = File(getRomsStagingDir(appCtx), safeName)
            try {
                appCtx.contentResolver.openInputStream(uri)?.use { inp ->
                    FileOutputStream(dest).use { out ->
                        inp.copyTo(out, 32 * 1024)
                        out.flush()
                    }
                } ?: return null
                dest
            } catch (_: Throwable) {
                return null
            }
        }

        if (!finalFile.exists() || !finalFile.isFile || finalFile.length() <= 0L) return null
        addPersistedSingleRom(appCtx, finalFile.absolutePath)
        return buildRomItemForFile(appCtx, finalFile, getMetaPrefs(appCtx))
    }

    private fun resolveDirectDocumentUri(uri: Uri): File? {
        return try {
            val docId = DocumentsContract.getDocumentId(uri)
            if (docId.startsWith("raw:")) {
                val f = File(docId.removePrefix("raw:"))
                if (f.exists()) return f
            }
            val split = docId.split(":", limit = 2)
            if (split.size == 2) {
                val vol = split[0]
                val rel = split[1].trimStart('/')
                val base = if (vol.equals("primary", ignoreCase = true)) "/storage/emulated/0" else "/storage/$vol"
                val f = File(base, rel)
                if (f.exists()) return f
            }
            null
        } catch (_: Throwable) {
            null
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

    // === Fast Multi-Folder + Download + SAF Scanner ===

    fun saveRomPreferenceState(context: Context, rom: MobileRomItem) {
        val p = getMetaPrefs(context)
        val keyBase = rom.filePath.hashCode().toString()
        p.edit()
            .putBoolean("fav_$keyBase", rom.isFavorite)
            .putLong("last_$keyBase", rom.lastPlayedMs)
            .putString("core_$keyBase", rom.assignedCoreId)
            .apply()
    }

    fun scanLibraryDirectories(
        context: Context,
        customRootDirs: List<File>? = null
    ): List<MobileRomItem> {
        val appCtx = context.applicationContext
        MobileCoreRegistry.init(appCtx)
        val prefs = getMetaPrefs(appCtx)
        val supportedExts = MobileCoreRegistry.getAllUniqueExtensions(appCtx)

        val scanRoots = LinkedHashMap<String, Pair<File, Boolean>>()
        fun putScanRoot(dir: File, recursive: Boolean, overrideExisting: Boolean) {
            val norm = normalizePathKey(dir)
            if (overrideExisting || !scanRoots.containsKey(norm)) {
                scanRoots[norm] = dir to recursive
            }
        }

        if (customRootDirs != null && customRootDirs.isNotEmpty()) {
            customRootDirs.forEach { putScanRoot(it, true, overrideExisting = false) }
        } else {
            val pubRoot = File(MobileCoreRegistry.STORM_PUBLIC_ROOT)
            putScanRoot(File(pubRoot, "roms"), true, overrideExisting = false)
            putScanRoot(File(pubRoot, "games"), true, overrideExisting = false)
            putScanRoot(File(appCtx.filesDir, "roms"), true, overrideExisting = false)
            putScanRoot(File("/storage/emulated/0/Download"), true, overrideExisting = false)
        }

        val safTreesToStage = ArrayList<Pair<Uri, Boolean>>()
        for (entry in getPersistedScanFolderEntries(appCtx)) {
            val savedPath = entry.pathOrUri
            if (savedPath.startsWith("content://")) {
                val uri = Uri.parse(savedPath)
                val resolved = resolveTreeOrDocumentUriToRealFile(uri)
                if (resolved != null && resolved.exists() && resolved.canRead()) {
                    putScanRoot(resolved, entry.recursive, overrideExisting = true)
                } else {
                    safTreesToStage.add(uri to entry.recursive)
                }
            } else {
                val f = File(savedPath)
                if (f.exists()) putScanRoot(f, entry.recursive, overrideExisting = true)
            }
        }

        // Stage ROMs from non-primary SAF trees (e.g. external SD card / restricted DocumentFile trees)
        for ((treeUri, recFlag) in safTreesToStage) {
            stageRomsFromSafTree(appCtx, treeUri, supportedExts, recFlag)
        }

        val candidateFiles = ArrayList<File>()
        val hiddenChildPaths = HashSet<String>()

        fun collectRecursive(dir: File, depth: Int, isDownloadFolder: Boolean, recursive: Boolean) {
            if (!recursive && depth > 0) return
            if (depth > 5 || !dir.exists() || !dir.isDirectory || !dir.canRead()) return
            if (depth > 0 && IGNORED_DIR_NAMES.contains(dir.name.lowercase(Locale.ROOT))) return

            val entries = dir.listFiles() ?: return
            for (entry in entries) {
                if (entry.name.startsWith(".")) continue
                if (entry.isDirectory) {
                    if (!recursive) continue
                    val maxSubDepth = if (isDownloadFolder) 2 else 5
                    if (depth + 1 <= maxSubDepth) {
                        collectRecursive(entry, depth + 1, isDownloadFolder, recursive = true)
                    }
                } else if (entry.isFile && entry.length() > 128L) {
                    val ext = entry.extension.lowercase(Locale.ROOT)
                    if (!supportedExts.contains(ext)) continue

                    if (isDownloadFolder && depth == 0) {
                        if (!isLikelyRomInDownloadFolder(entry, ext)) continue
                    }

                    candidateFiles.add(entry)
                    if (ext == "m3u" || ext == "cue" || ext == "gdi") {
                        for (child in parseDescriptorChildFiles(entry)) {
                            hiddenChildPaths.add(normalizePathKey(child))
                        }
                    }
                }
            }
        }

        for ((root, recFlag) in scanRoots.values) {
            val isDl = root.name.equals("Download", ignoreCase = true)
            collectRecursive(root, 0, isDl, recFlag)
        }

        for (singlePath in getPersistedSingleRoms(appCtx)) {
            val sf = File(singlePath)
            if (sf.exists() && sf.isFile) candidateFiles.add(sf)
        }

        val results = ArrayList<MobileRomItem>(candidateFiles.size)
        val seenPaths = HashSet<String>()

        for (file in candidateFiles) {
            val normPath = normalizePathKey(file)
            if (hiddenChildPaths.contains(normPath)) continue
            if (!seenPaths.add(normPath)) continue

            buildRomItemForFile(appCtx, file, prefs)?.let { results.add(it) }
        }

        results.sortWith(
            compareByDescending<MobileRomItem> { it.isFavorite }
                .thenByDescending { it.lastPlayedMs }
                .thenBy { it.name.lowercase(Locale.ROOT) }
        )
        return results
    }

    private fun stageRomsFromSafTree(context: Context, treeUri: Uri, supportedExts: Set<String>, recursive: Boolean = true) {
        val docRoot = DocumentFile.fromTreeUri(context, treeUri) ?: return
        val stagingRoot = getRomsStagingDir(context)

        fun walkDoc(doc: DocumentFile, depth: Int, parentHint: String) {
            if (!recursive && depth > 0) return
            if (depth > 4) return
            for (child in doc.listFiles()) {
                val cName = child.name ?: continue
                if (cName.startsWith(".")) continue
                if (child.isDirectory) {
                    if (recursive && !IGNORED_DIR_NAMES.contains(cName.lowercase(Locale.ROOT))) {
                        walkDoc(child, depth + 1, cName)
                    }
                } else if (child.isFile) {
                    val ext = cName.substringAfterLast('.', "").lowercase(Locale.ROOT)
                    if (ext.isEmpty() || !supportedExts.contains(ext)) continue
                    val len = child.length()
                    if (len <= 128L || len > 1024L * 1024L * 1024L) continue
                    val subDir = if (parentHint.isNotBlank()) File(stagingRoot, parentHint).apply { mkdirs() } else stagingRoot
                    val target = File(subDir, cName.replace(Regex("[\\\\/:*?\"<>|]"), "_"))
                    if (!target.exists() || target.length() != len) {
                        try {
                            context.contentResolver.openInputStream(child.uri)?.use { inp ->
                                FileOutputStream(target).use { out -> inp.copyTo(out, 32 * 1024) }
                            }
                        } catch (_: Throwable) {
                        }
                    }
                }
            }
        }
        try {
            walkDoc(docRoot, 0, docRoot.name ?: "")
        } catch (_: Throwable) {
        }
    }

    private fun isLikelyRomInDownloadFolder(file: File, ext: String): Boolean {
        val lowerName = file.name.lowercase(Locale.ROOT)
        if (lowerName.startsWith("storm_retroarch")) return false
        if (UNAMBIGUOUS_ROM_EXTENSIONS.contains(ext)) return true
        if (ext == "iso") return file.length() in (2L * 1024L * 1024L)..(8500L * 1024L * 1024L)
        if (ext == "zip") {
            return try {
                ZipFile(file).use { zf ->
                    val entries = zf.entries()
                    var count = 0
                    var hasRomInner = false
                    while (entries.hasMoreElements() && count < 24) {
                        val e = entries.nextElement()
                        if (!e.isDirectory) {
                            count++
                            val innerExt = e.name.substringAfterLast('.', "").lowercase(Locale.ROOT)
                            if (UNAMBIGUOUS_ROM_EXTENSIONS.contains(innerExt) || innerExt == "bin" || innerExt == "rom") {
                                hasRomInner = true
                                break
                            }
                        }
                    }
                    hasRomInner
                }
            } catch (_: Throwable) {
                false
            }
        }
        return false
    }

    fun buildRomItemForFile(
        context: Context,
        file: File,
        prefs: SharedPreferences = getMetaPrefs(context)
    ): MobileRomItem? {
        val ext = file.extension.lowercase(Locale.ROOT)
        val rawStem = file.nameWithoutExtension
        val cleanName = cleanDisplayTitle(rawStem)
        val (region, isRus) = parseRegionAndRusMod(rawStem)
        val platformKey = detectPlatformKeyForFile(file, ext)
        val platformDisplay = MobileCoreRegistry.getPlatformDisplayName(platformKey)

        val keyBase = file.absolutePath.hashCode().toString()
        val savedFav = prefs.getBoolean("fav_$keyBase", false)
        val savedLast = prefs.getLong("last_$keyBase", 0L)
        val savedCore = prefs.getString("core_$keyBase", null)

        val defaultCore = MobileCoreRegistry.getDefaultCoreForPlatform(platformKey, ext, context)
        val resolvedCoreId = if (!savedCore.isNullOrBlank() && MobileCoreRegistry.findCoreById(savedCore, context) != null) {
            savedCore
        } else {
            defaultCore?.id ?: "snes9x"
        }

        val existingCover = findCachedCoverFile(context, platformKey, cleanName, region)
        val yearMatch = Regex("\\b(19\\d\\d|20\\d\\d)\\b").find(rawStem)
        val parsedYear = yearMatch?.groupValues?.getOrNull(1) ?: "Классика"
        val upperStem = rawStem.uppercase(Locale.ROOT)
        val transBadge = when {
            upperStem.contains("MOD - M. RUS") || upperStem.contains("M. RUS") -> "MOD - M. RUS"
            isRus -> "RUS"
            region == "JP" -> "JPN"
            else -> "ENG"
        }
        val audioBadgeParsed = when {
            Regex("\\[(VOX|SND|DUB|VOICE)\\]").containsMatchIn(upperStem) -> "RUS Озв."
            upperStem.contains("[S. RUS]") -> "RUS Звук"
            isRus -> "Оригинал"
            region == "JP" -> "JPN"
            else -> "ENG"
        }
        val filesBadgeParsed = when {
            upperStem.contains("(1G+1U+1M)") -> "1G+1U+1M"
            upperStem.contains("(1G+1M)") -> "1G+1M"
            upperStem.contains("(1G+1U)") -> "1G+1U"
            upperStem.contains("(1G)") -> "1G"
            isRus -> "1G+1M"
            else -> "1G • ${ext.uppercase(Locale.ROOT)}"
        }

        val item = MobileRomItem(
            name = cleanName,
            rawNoIntroName = rawStem,
            filePath = file.absolutePath,
            extension = ext,
            platform = platformDisplay,
            platformKey = platformKey,
            regionCode = region,
            isRussianMod = isRus,
            assignedCoreId = resolvedCoreId,
            sizeBytes = file.length(),
            isFavorite = savedFav,
            lastPlayedMs = savedLast,
            coverPath = existingCover?.absolutePath,
            id = computeRomId(file.absolutePath),
            year = parsedYear,
            translationBadge = transBadge,
            audioBadge = audioBadgeParsed,
            versionFilesBadge = filesBadgeParsed
        )
        applyMetadataToRom(context, item)
        return item
    }

    private fun normalizePathKey(file: File): String {
        return try {
            file.canonicalPath.lowercase(Locale.ROOT)
        } catch (_: Throwable) {
            file.absolutePath.lowercase(Locale.ROOT)
        }
    }

    private fun parseDescriptorChildFiles(descriptor: File): List<File> {
        val out = ArrayList<File>()
        val parent = descriptor.parentFile ?: return out
        val ext = descriptor.extension.lowercase(Locale.ROOT)
        try {
            descriptor.forEachLine { raw ->
                val line = raw.trim()
                if (line.isEmpty() || line.startsWith("#")) return@forEachLine
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
                        val q1 = line.indexOf('"')
                        val q2 = line.lastIndexOf('"')
                        if (q1 >= 0 && q2 > q1) {
                            out.add(File(parent, line.substring(q1 + 1, q2)))
                        } else {
                            val parts = line.split(Regex("\\s+"))
                            if (parts.size >= 5) out.add(File(parent, parts[4]))
                        }
                    }
                }
            }
        } catch (_: Throwable) {
        }
        return out
    }

    fun parseRomMeta(rawFileName: String): RomParsedMeta {
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
            Regex("(\\((USA|US|U|NORTH AMERICA)\\)|(\\[(USA|US|U)\\]))", RegexOption.IGNORE_CASE).containsMatchIn(rawFileName) -> "US"
            Regex("(\\((EUROPE|EU|EUR|E|UK|GERMANY|FRANCE)\\)|(\\[(EUROPE|EU|EUR|E)\\]))", RegexOption.IGNORE_CASE).containsMatchIn(rawFileName) -> "EU"
            Regex("(\\((JAPAN|JP|JPN|J)\\)|(\\[(JAPAN|JP|JPN|J)\\])|,\\s*JAPAN\\))", RegexOption.IGNORE_CASE).containsMatchIn(rawFileName) -> "JP"
            Regex("(\\((WORLD|W)\\)|(\\[(WORLD|W)\\]))", RegexOption.IGNORE_CASE).containsMatchIn(rawFileName) -> "WORLD"
            Regex("(\\((KOREA|KR|KOR|K)\\)|(\\[(KOREA|KR|KOR|K)\\]))", RegexOption.IGNORE_CASE).containsMatchIn(rawFileName) -> "KR"
            Regex("(\\((CHINA|TAIWAN|ASIA|TW)\\)|(\\[(CHINA|ASIA|TW)\\]))", RegexOption.IGNORE_CASE).containsMatchIn(rawFileName) -> "ASIA"
            Regex("(\\((BRAZIL|BR|BRA)\\)|(\\[(BRAZIL|BR)\\]))", RegexOption.IGNORE_CASE).containsMatchIn(rawFileName) -> "BR"
            else -> "WORLD"
        }

        val codeMatch = Regex("\\b([A-Z0-9]{2,5}-[A-Z0-9]{2,6}(?:-[A-Z0-9]+)?)\\b").find(rawFileName)
        val gameCode = codeMatch?.groupValues?.getOrNull(1) ?: ""

        val revMatch = Regex("\\b(?:Rev\\s*([A-Z0-9.]+)|v(\\d+(?:\\.\\d+)*))\\b", RegexOption.IGNORE_CASE).find(rawFileName)
        val revision = if (revMatch != null) {
            val v = revMatch.groupValues[1].ifEmpty { revMatch.groupValues[2] }
            when {
                v.startsWith("1.1") -> "Rev 1"
                v.startsWith("1.2") -> "Rev 2"
                v.startsWith("1.3") -> "Rev 3"
                v.all { it.isDigit() } -> "Rev $v"
                else -> "Rev $v"
            }
        } else {
            val numMatch = Regex("[\\(\\[](\\d+\\.\\d+)(?:\\s*-\\s*[A-Z0-9-]+)?[\\)\\]").find(rawFileName)
            if (numMatch != null) {
                when (val num = numMatch.groupValues[1]) {
                    "1.1" -> "Rev 1"
                    "1.2" -> "Rev 2"
                    "1.3" -> "Rev 3"
                    else -> "v$num"
                }
            } else ""
        }

        return RomParsedMeta(region, isRus, gameCode, revision)
    }

    fun parseRegionAndRusMod(rawFileName: String): Pair<String, Boolean> {
        val meta = parseRomMeta(rawFileName)
        return meta.region to meta.isRussianMod
    }

    fun cleanDisplayTitle(rawStem: String): String {
        var s = rawStem
            .replace(Regex("\\[MOD\\s*-\\s*M\\.\\s*RUS\\]", RegexOption.IGNORE_CASE), "")
            .replace(Regex("\\[RUS\\]", RegexOption.IGNORE_CASE), "")
            .replace(Regex("\\[!\\]"), "")
            .replace(Regex("[\\(\\[]1G(?:\\+1[UM])*(?:\\+[0-9A-Z]+)*[\\)\\]", RegexOption.IGNORE_CASE), "")
            .replace(Regex("[\\(\\[](?:19\\d\\d|20\\d\\d)[\\)\\]"), "")
            .replace(Regex("[\\(\\[]\\d+(?:\\.\\d+)*\\s*-\\s*[A-Z0-9-]+[\\)\\]", RegexOption.IGNORE_CASE), "")
            .replace(Regex("\\[[^\\]]*\\]"), "")
            .replace(
                Regex(
                    "\\((USA|Japan|Europe|World|Korea|Russia|Brazil|Asia|Australia|En,Fr[^)]*|En,Ja[^)]*|Rev\\s*[A-Z0-9.]+|v\\d[^)]*|Track\\s*\\d+|Disc\\s*\\d+|Disk\\s*\\d+|Proto|Beta|Sample|Unl|SGB\\s*Enhanced|NP|\\d+\\.\\d+[^)]*)\\)",
                    RegexOption.IGNORE_CASE
                ),
                ""
            )
            .replace(Regex("\\s{2,}"), " ")
            .trim()
        if (s.isEmpty()) s = rawStem.trim()
        return s
    }

    fun readRomHeaderInfo(file: File): RomHeaderInfo? {
        if (!file.exists() || file.length() < 512L) return null
        return try {
            val head = ByteArray(65536)
            var headLen = 0
            val ext = file.extension.lowercase(Locale.ROOT)

            if (ext == "zip") {
                ZipFile(file).use { zf ->
                    val entries = zf.entries()
                    while (entries.hasMoreElements()) {
                        val e = entries.nextElement()
                        if (e.isDirectory) continue
                        val iext = File(e.name).extension.lowercase(Locale.ROOT)
                        if (iext in setOf("sfc", "smc", "gba", "nds", "n64", "z64", "v64", "gen", "md", "bin", "iso")) {
                            zf.getInputStream(e).use { stream ->
                                var readTotal = 0
                                while (readTotal < head.size) {
                                    val r = stream.read(head, readTotal, head.size - readTotal)
                                    if (r <= 0) break
                                    readTotal += r
                                }
                                headLen = readTotal
                            }
                            break
                        }
                    }
                }
            } else {
                RandomAccessFile(file, "r").use { raf ->
                    headLen = raf.read(head, 0, minOf(head.size.toLong(), file.length()).toInt())
                }
            }

            if (headLen < 512) return null

            // 1. SNES (LoROM at 0x7FC0 or HiROM at 0xFFC0, with 512 copier offset)
            val copierOffset = if (file.length() % 1024L == 512L) 512 else 0
            val loOffset = copierOffset + 0x7FC0
            val hiOffset = copierOffset + 0xFFC0
            for (off in listOf(loOffset, hiOffset)) {
                if (off + 32 <= headLen) {
                    val csumComp = (head[off + 0x1C].toInt() and 0xFF) or ((head[off + 0x1D].toInt() and 0xFF) shl 8)
                    val csum = (head[off + 0x1E].toInt() and 0xFF) or ((head[off + 0x1F].toInt() and 0xFF) shl 8)
                    if ((csum xor csumComp) == 0xFFFF) {
                        val rawTitle = String(head, off, 21, StandardCharsets.ISO_8859_1).trim()
                        val countryByte = head[off + 0x19].toInt() and 0xFF
                        val reg = when (countryByte) {
                            0x00 -> "JP"
                            0x01 -> "US"
                            0x02 -> "EU"
                            0x0D -> "KR"
                            else -> "WORLD"
                        }
                        val verByte = head[off + 0x1B].toInt() and 0xFF
                        val rev = when (verByte) {
                            0x01 -> "Rev 1"
                            0x02 -> "Rev 2"
                            0x03 -> "Rev 3"
                            else -> if (verByte > 0) "Rev $verByte" else ""
                        }
                        val code = if (off >= 0x0E) String(head, off - 0x0E, 4, StandardCharsets.ISO_8859_1).trim() else ""
                        return RomHeaderInfo(rawTitle, code, reg, rev)
                    }
                }
            }

            // 2. GBA (0xA0..0xBC)
            if (headLen >= 0xC0) {
                val gbaCheck = (head[0xB2].toInt() and 0xFF) == 0x96
                if (gbaCheck) {
                    val rawTitle = String(head, 0xA0, 12, StandardCharsets.ISO_8859_1).trim()
                    val code = String(head, 0xAC, 4, StandardCharsets.ISO_8859_1).trim()
                    val reg = when (code.takeLast(1)) {
                        "J" -> "JP"
                        "E" -> "US"
                        "P" -> "EU"
                        else -> "WORLD"
                    }
                    val verByte = head[0xBC].toInt() and 0xFF
                    val rev = if (verByte > 0) "Rev $verByte" else ""
                    return RomHeaderInfo(rawTitle, code, reg, rev)
                }
            }

            // 3. NDS (0x00..0x1E)
            if (headLen >= 0x40 && head[0x12].toInt() == 0) {
                val rawTitle = String(head, 0x00, 12, StandardCharsets.ISO_8859_1).trim()
                val code = String(head, 0x0C, 4, StandardCharsets.ISO_8859_1).trim()
                if (code.length >= 4 && code.all { it.isLetterOrDigit() }) {
                    val reg = when (code.takeLast(1)) {
                        "J" -> "JP"
                        "E" -> "US"
                        "P" -> "EU"
                        "K" -> "KR"
                        else -> "WORLD"
                    }
                    val verByte = head[0x1E].toInt() and 0xFF
                    val rev = if (verByte > 0) "Rev $verByte" else ""
                    return RomHeaderInfo(rawTitle, code, reg, rev)
                }
            }

            // 4. N64 (0x20..0x3F)
            if (headLen >= 0x40) {
                val magic = ((head[0].toInt() and 0xFF) shl 24) or ((head[1].toInt() and 0xFF) shl 16) or ((head[2].toInt() and 0xFF) shl 8) or (head[3].toInt() and 0xFF)
                if (magic == 0x80371240.toInt() || magic == 0x37804012 || magic == 0x40123780) {
                    val rawTitle = String(head, 0x20, 20, StandardCharsets.ISO_8859_1).trim()
                    val code = String(head, 0x3B, 4, StandardCharsets.ISO_8859_1).trim()
                    val regByte = head[0x3E].toInt().toChar()
                    val reg = when (regByte) {
                        'E' -> "US"
                        'J' -> "JP"
                        'P' -> "EU"
                        else -> "WORLD"
                    }
                    val verByte = head[0x3F].toInt() and 0xFF
                    val rev = if (verByte > 0) "Rev $verByte" else ""
                    return RomHeaderInfo(rawTitle, code, reg, rev)
                }
            }

            // 5. Mega Drive / Genesis (0x100..0x200)
            if (headLen >= 0x200) {
                val segaMagic = String(head, 0x100, 16, StandardCharsets.ISO_8859_1)
                if (segaMagic.contains("SEGA")) {
                    val domTitle = String(head, 0x120, 48, StandardCharsets.ISO_8859_1).trim()
                    val ovsTitle = String(head, 0x150, 48, StandardCharsets.ISO_8859_1).trim()
                    val code = String(head, 0x180, 14, StandardCharsets.ISO_8859_1).trim()
                    val country = String(head, 0x1F0, 16, StandardCharsets.ISO_8859_1).trim()
                    val reg = when {
                        country.contains("J") -> "JP"
                        country.contains("U") -> "US"
                        country.contains("E") -> "EU"
                        else -> "WORLD"
                    }
                    val title = if (ovsTitle.isNotBlank() && !ovsTitle.all { it == ' ' }) ovsTitle else domTitle
                    return RomHeaderInfo(title, code, reg, "")
                }
            }

            // 6. PS1 (Scan for SLUS/SCUS/SLES/SCES/SLPS/SLPM/SCPS)
            val ascii = String(head, StandardCharsets.ISO_8859_1)
            val ps1Regex = Regex("\\b(SLUS|SCUS|SLES|SCES|SLPS|SLPM|SCPS|SLED)[_-](\\d{3})[.]?(\\d{2})\\b")
            val ps1Match = ps1Regex.find(ascii)
            if (ps1Match != null) {
                val prefix = ps1Match.groupValues[1]
                val code = "$prefix-${ps1Match.groupValues[2]}.${ps1Match.groupValues[3]}"
                val reg = when (prefix) {
                    "SLUS", "SCUS" -> "US"
                    "SLES", "SCES", "SLED" -> "EU"
                    "SLPS", "SLPM", "SCPS" -> "JP"
                    else -> "WORLD"
                }
                return RomHeaderInfo("", code, reg, "")
            }

            null
        } catch (_: Throwable) {
            null
        }
    }

    fun detectPlatformKeyForFile(file: File, ext: String): String {
        val parent1 = file.parentFile?.name ?: ""
        val parent2 = file.parentFile?.parentFile?.name ?: ""
        for (folderHint in listOf(parent1, parent2)) {
            val lower = folderHint.lowercase(Locale.ROOT)
            if (lower.isNotEmpty() && lower !in setOf("roms", "games", "imported", "download", "storm retroarch")) {
                val mapped = MobileCoreRegistry.normalizePlatformKey(folderHint, "")
                if (mapped.isNotEmpty()) return mapped
            }
        }

        if (ext == "zip") {
            try {
                ZipFile(file).use { zf ->
                    val entries = zf.entries()
                    var hasDosExe = false
                    while (entries.hasMoreElements()) {
                        val entry = entries.nextElement()
                        if (entry.isDirectory) continue
                        val innerExt = File(entry.name).extension.lowercase(Locale.ROOT)
                        if (innerExt in setOf("exe", "com", "bat", "conf")) {
                            hasDosExe = true
                        } else if (innerExt.isNotEmpty() && innerExt != "txt" && innerExt != "nfo" && innerExt != "rom" && innerExt != "bin") {
                            val innerKey = MobileCoreRegistry.normalizePlatformKey("", innerExt)
                            if (innerKey.isNotEmpty() && innerKey != "arcade" && innerKey != "ps1") {
                                return innerKey
                            }
                        }
                    }
                    if (hasDosExe) return "dos"
                }
            } catch (_: Throwable) {
            }
            return "arcade"
        }

        if (ext in setOf("iso", "bin", "img", "cue", "chd", "m3u")) {
            val targetBin = resolvePrimaryTrackForInspection(file, ext)
            val detectedDisc = inspectBinaryDiscHeader(targetBin)
            if (detectedDisc.isNotEmpty()) return detectedDisc
        }

        val fallback = MobileCoreRegistry.normalizePlatformKey("", ext)
        return fallback.ifEmpty { "arcade" }
    }

    private fun resolvePrimaryTrackForInspection(file: File, ext: String): File {
        if (ext == "cue" || ext == "m3u") {
            val children = parseDescriptorChildFiles(file)
            val firstExisting = children.firstOrNull { it.exists() && it.length() > 65536L }
            if (firstExisting != null) return firstExisting
        }
        return file
    }

    fun inspectBinaryDiscHeader(file: File): String {
        if (!file.exists() || file.length() < 1024L) return ""
        return try {
            RandomAccessFile(file, "r").use { raf ->
                val head = ByteArray(minOf(65536L, file.length()).toInt())
                raf.readFully(head)
                val ascii = String(head, StandardCharsets.ISO_8859_1)
                when {
                    ascii.contains("SEGA SEGASATURN") -> "saturn"
                    ascii.contains("SEGA SEGAKATANA") -> "dreamcast"
                    ascii.contains("SEGA DISCSYSTEM") || ascii.contains("SEGADISCSYSTEM") -> "segacd"
                    ascii.contains("SEGA 32X") -> "sega32x"
                    ascii.contains("SEGA MEGA DRIVE") || ascii.contains("SEGA GENESIS") -> "genesis"
                    ascii.contains("PlayStation") || ascii.contains("SONY COMPUTER ENTERTAINMENT") -> {
                        if (file.length() > 850L * 1024L * 1024L) "ps2" else "ps1"
                    }
                    ascii.contains("PSP GAME") -> "psp"
                    ascii.contains("PC Engine") || ascii.contains("HUDSON") -> "pcecd"
                    head.size >= 0x20 &&
                        head[0x1C] == 0xC2.toByte() && head[0x1D] == 0x33.toByte() &&
                        head[0x1E] == 0x9F.toByte() && head[0x1F] == 0x3D.toByte() -> "gamecube"
                    head.size >= 0x1C &&
                        head[0x18] == 0x5D.toByte() && head[0x19] == 0x1C.toByte() &&
                        head[0x1A] == 0x9E.toByte() && head[0x1B] == 0xA3.toByte() -> "wii"
                    else -> ""
                }
            }
        } catch (_: Throwable) {
            ""
        }
    }

    // === Filtering & Smart Collections ===

    fun matchesCollection(item: MobileRomItem, collection: String): Boolean {
        return when (collection) {
            COLLECTION_ALL -> true
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
        activeCollection: String,
        activePlatform: String
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
        for (rom in platformFiltered) {
            coreCounts[rom.assignedCoreId] = (coreCounts[rom.assignedCoreId] ?: 0) + 1
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
        collection: String,
        platform: String,
        coreIdFilter: String,
        searchQuery: String
    ): List<MobileRomItem> {
        val normPlat = MobileCoreRegistry.normalizePlatformKey(platform, "")
        val q = searchQuery.trim().lowercase(Locale.ROOT)

        val filtered = allRoms.filter { rom ->
            if (!matchesCollection(rom, collection)) return@filter false
            if (normPlat.isNotEmpty() && rom.platformKey != normPlat) return@filter false
            if (coreIdFilter.isNotEmpty() && !coreIdFilter.equals("ALL", ignoreCase = true) && rom.assignedCoreId != coreIdFilter) {
                return@filter false
            }
            if (q.isNotEmpty()) {
                val matchTitle = rom.name.lowercase(Locale.ROOT).contains(q)
                val matchRaw = rom.rawNoIntroName.lowercase(Locale.ROOT).contains(q)
                val matchPlat = rom.platform.lowercase(Locale.ROOT).contains(q)
                val matchReg = rom.regionDisplayBadge.lowercase(Locale.ROOT).contains(q)
                if (!matchTitle && !matchRaw && !matchPlat && !matchReg) return@filter false
            }
            true
        }

        return if (collection == COLLECTION_RECENT) {
            filtered.sortedByDescending { it.lastPlayedMs }
        } else {
            filtered
        }
    }

    // === Regional Boxart Matcher & Downloader ===

    fun getExpectedCoverCacheFile(
        platformKey: String,
        cleanName: String,
        regionCode: String,
        context: Context? = null
    ): File {
        val safePlat = sanitizeFileComponent(platformKey.ifEmpty { "misc" })
        val safeName = sanitizeFileComponent(cleanName.ifEmpty { "rom" })
        val safeReg = sanitizeFileComponent(regionCode.ifEmpty { "WORLD" })
        val dir = File(getThumbnailsRootDir(context), safePlat)
        return File(dir, "${safeName}_${safeReg}.png")
    }

    private fun findCachedCoverFile(
        context: Context,
        platformKey: String,
        cleanName: String,
        regionCode: String
    ): File? {
        val primary = getExpectedCoverCacheFile(platformKey, cleanName, regionCode, context)
        if (primary.exists() && primary.length() > 256L) return primary
        val pubAlt = File(File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "thumbnails/${sanitizeFileComponent(platformKey)}"), primary.name)
        if (pubAlt.exists() && pubAlt.length() > 256L) return pubAlt
        return null
    }

    fun resolvePlatformType(romPath: String, platformKey: String, platformName: String): String {
        val p = romPath.replace('\\', '/').uppercase(Locale.ROOT)
        for (cat in listOf("CONSOLES", "HANDHELDS", "PERSONAL COMPUTERS", "ARCADES", "GAME ENGINES", "MOBILES", "VR AR HEADSETS")) {
            if (p.contains("/$cat/") || p.startsWith("$cat/") || p.contains("\\$cat\\")) {
                return cat
            }
        }
        val k = platformKey.lowercase(Locale.ROOT).trim()
        val n = platformName.lowercase(Locale.ROOT).trim()
        return when {
            k in setOf("nes", "fds", "snes", "sfc", "n64", "gamecube", "gc", "wii", "wiiu", "switch",
                "genesis", "megadrive", "md", "segacd", "sega32x", "32x", "saturn", "dreamcast",
                "ps1", "psx", "ps2", "ps3", "ps4", "ps5", "xbox", "xbox360", "tg16", "pce", "pcecd",
                "pcfx", "3do", "jaguar", "cdi", "coleco", "intellivision", "atari2600", "atari5200",
                "atari7800", "vectrex", "sg1000", "sms", "mastersystem") -> "CONSOLES"
            k in setOf("gb", "gbc", "gba", "nds", "3ds", "psp", "vita", "gamegear", "gg", "sms_gg",
                "wonderswan", "wsc", "ngpc", "neogeopocket", "lynx", "pokemonmini", "playdate", "gamewatch") -> "HANDHELDS"
            k in setOf("dos", "pc", "win3x", "win9x", "windows", "msx", "msx2", "c64", "c128", "amiga",
                "atarist", "zxspectrum", "pc88", "pc98", "x68000", "fmtowns", "amstrad", "amstradcpc",
                "apple2", "mac", "bk0010", "korvet", "vector06c", "agat") -> "PERSONAL COMPUTERS"
            k in setOf("arcade", "mame", "fba", "fbneo", "cps1", "cps2", "cps3", "neogeo", "neocd", "model2", "model3", "naomi") -> "ARCADES"
            k in setOf("scummvm", "rpgmaker", "wolf3d", "doom", "quake", "residualvm", "solarus", "easyrpg", "flash") -> "GAME ENGINES"
            k in setOf("android", "ios", "j2me", "symbian", "ngage") -> "MOBILES"
            k in setOf("vb", "virtualboy", "oculus", "quest", "psvr") -> "VR AR HEADSETS"
            n.contains("handheld") || n.contains("pocket") || n.contains("game boy") || n.contains("advance") || n.contains("ds") || n.contains("psp") -> "HANDHELDS"
            n.contains("computer") || n.contains("dos") || n.contains("pc") || n.contains("amiga") || n.contains("spectrum") || n.contains("msx") -> "PERSONAL COMPUTERS"
            n.contains("arcade") || n.contains("mame") || n.contains("neo geo") -> "ARCADES"
            n.contains("engine") || n.contains("scumm") || n.contains("doom") || n.contains("quake") -> "GAME ENGINES"
            else -> "CONSOLES"
        }
    }

    fun getStructuredMediaJson(context: Context): String {
        val rootObj = JSONObject()
        val screenshotsArr = JSONArray()
        val videosArr = JSONArray()

        val imageExts = setOf("png", "jpg", "jpeg", "webp")
        val videoExts = setOf("mp4", "mkv", "webm", "ts", "mov", "flv", "gif")

        val searchDirs = listOf(
            File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "screenshots") to "screenshots",
            File(context.applicationContext.filesDir, "screenshots") to "screenshots",
            File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "videos") to "videos",
            File(context.applicationContext.filesDir, "videos") to "videos"
        )

        val seenPaths = HashSet<String>()

        fun scanDir(dir: File, mediaType: String) {
            if (!dir.exists() || !dir.isDirectory) return
            val files = dir.listFiles() ?: return
            for (f in files) {
                if (f.isDirectory) {
                    scanDir(f, mediaType)
                } else if (f.isFile && f.length() > 256L) {
                    val ext = f.extension.lowercase(Locale.ROOT)
                    val normPath = normalizePathKey(f)
                    if (!seenPaths.add(normPath)) continue

                    val isImg = ext in imageExts
                    val isVid = ext in videoExts
                    if (!isImg && !isVid) continue

                    val parts = f.parentFile?.absolutePath?.replace('\\', '/')?.split('/') ?: emptyList()
                    val pTypeIdx = parts.indexOfFirst { it.equals("screenshots", true) || it.equals("videos", true) }
                    var pType = "CONSOLES"
                    var platform = "Super Nintendo (SNES)"
                    var gameTitle = cleanDisplayTitle(f.nameWithoutExtension)

                    if (pTypeIdx >= 0 && parts.size > pTypeIdx + 3) {
                        pType = parts[pTypeIdx + 1]
                        platform = parts[pTypeIdx + 2]
                        gameTitle = parts[pTypeIdx + 3]
                    } else if (pTypeIdx >= 0 && parts.size > pTypeIdx + 2) {
                        pType = parts[pTypeIdx + 1]
                        platform = parts[pTypeIdx + 2]
                    }

                    val itemObj = JSONObject()
                        .put("platformType", pType)
                        .put("platform", platform)
                        .put("gameTitle", gameTitle)
                        .put("path", f.absolutePath)
                        .put("url", toStormLocalUrl(f.absolutePath))
                        .put("size", f.length())
                        .put("name", f.name)

                    if (isImg) {
                        screenshotsArr.put(itemObj)
                    } else {
                        videosArr.put(itemObj)
                    }
                }
            }
        }

        for ((dir, type) in searchDirs) {
            scanDir(dir, type)
        }

        rootObj.put("screenshots", screenshotsArr)
        rootObj.put("videos", videosArr)
        return rootObj.toString()
    }

    fun getGameMediaGallery(context: Context, rom: MobileRomItem): List<Pair<String, String>> {
        val gallery = ArrayList<Pair<String, String>>()
        val seenPaths = HashSet<String>()

        fun addFile(f: File?, caption: String) {
            if (f == null || !f.exists() || !f.isFile || f.length() <= 256L) return
            val ext = f.extension.lowercase(Locale.ROOT)
            if (ext !in setOf("png", "jpg", "jpeg", "webp")) return
            val norm = normalizePathKey(f)
            if (!seenPaths.add(norm)) return
            gallery.add(toStormLocalUrl(f.absolutePath) to caption)
        }

        if (!rom.coverPath.isNullOrBlank()) {
            addFile(File(rom.coverPath!!), "${rom.name} - Обложка")
        }

        val platformType = resolvePlatformType(rom.filePath, rom.platformKey, rom.platform)
        val safePlatDisplay = sanitizeFileComponent(rom.platform)
        val safeGame = sanitizeFileComponent(rom.name)
        val queryNorm = normalizeTitleForMatch(rom.name)
        val stemNorm = normalizeTitleForMatch(rom.rawNoIntroName)
        val safePlat = sanitizeFileComponent(rom.platformKey.ifEmpty { "misc" })

        val snapDirs = listOf(
            File(File(File(File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "screenshots"), platformType), safePlatDisplay), safeGame),
            File(File(File(File(context.applicationContext.filesDir, "screenshots"), platformType), safePlatDisplay), safeGame),
            File(File(File(File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "videos"), platformType), safePlatDisplay), safeGame),
            File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "screenshots"),
            File(context.applicationContext.filesDir, "screenshots"),
            File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "states"),
            File(getThumbnailsRootDir(context), "$safePlat/Named_Snaps"),
            File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "thumbnails/$safePlat/Named_Snaps"),
            File(getThumbnailsRootDir(context), "$safePlat/Named_Titles")
        )

        for (dir in snapDirs) {
            if (gallery.size >= 16) break
            if (!dir.exists() || !dir.isDirectory) continue
            val files = dir.listFiles() ?: continue
            for (f in files) {
                if (gallery.size >= 16) break
                val normFile = normalizeTitleForMatch(f.nameWithoutExtension)
                val matches = dir.name == safeGame ||
                    (queryNorm.length >= 3 && normFile.contains(queryNorm)) ||
                    (stemNorm.length >= 4 && normFile.contains(stemNorm))
                if (matches) {
                    addFile(f, f.nameWithoutExtension)
                }
            }
        }
        return gallery
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
        val primaryCache = getExpectedCoverCacheFile(rom.platformKey, rom.name, rom.regionCode, context)
        if (primaryCache.exists() && primaryCache.length() > 256L) {
            rom.coverPath = primaryCache.absolutePath
            mainHandler.post { onResolved(rom, primaryCache) }
            return
        }

        val appCtx = context.applicationContext
        coverExecutor.execute {
            val libretroSys = LIBRETRO_THUMBNAIL_SYSTEM_MAP[rom.platformKey]
            if (libretroSys == null) {
                mainHandler.post { onResolved(rom, null) }
                return@execute
            }

            val encodedSys = encodeUrlPathSegment(libretroSys)
            val candidates = buildOrderedRegionalCoverCandidates(rom)

            var downloadedBytes: ByteArray? = null
            for (cand in candidates) {
                val encodedFile = encodeUrlPathSegment(cand)
                val boxUrl = "$THUMBNAILS_SERVER/$encodedSys/Named_Boxarts/$encodedFile.png"
                downloadedBytes = downloadBytes(boxUrl, 8 * 1024 * 1024)
                if (downloadedBytes != null && isValidImageHeader(downloadedBytes)) break
                downloadedBytes = null
            }

            // Fallback: fuzzy match against Libretro Named_Boxarts directory index
            if (downloadedBytes == null) {
                val indexList = getOrFetchBoxartDirectoryListing(libretroSys)
                val bestMatch = findBestRegionalBoxartMatch(indexList, rom)
                if (bestMatch != null) {
                    val encodedFile = encodeUrlPathSegment(bestMatch)
                    val boxUrl = "$THUMBNAILS_SERVER/$encodedSys/Named_Boxarts/$encodedFile.png"
                    val b = downloadBytes(boxUrl, 8 * 1024 * 1024)
                    if (b != null && isValidImageHeader(b)) {
                        downloadedBytes = b
                    }
                }
            }

            if (downloadedBytes != null) {
                try {
                    val targetFile = getExpectedCoverCacheFile(rom.platformKey, rom.name, rom.regionCode, appCtx)
                    targetFile.parentFile?.mkdirs()
                    FileOutputStream(targetFile).use { fos ->
                        fos.write(downloadedBytes)
                        fos.flush()
                    }
                    rom.coverPath = targetFile.absolutePath
                    mainHandler.post { onResolved(rom, targetFile) }
                    return@execute
                } catch (_: Throwable) {
                }
            }

            mainHandler.post { onResolved(rom, null) }
        }
    }

    private fun buildOrderedRegionalCoverCandidates(rom: MobileRomItem): List<String> {
        val list = LinkedHashSet<String>()
        val clean = cleanDisplayTitle(rom.rawNoIntroName).replace(":", " -")
        val altClean = rom.name.replace(":", " -")

        val meta = parseRomMeta(rom.rawNoIntroName)
        val headerInfo = try {
            readRomHeaderInfo(File(rom.filePath))
        } catch (_: Throwable) { null }

        val effectiveRev = meta.revision.ifBlank { headerInfo?.revision ?: "" }
        val revTag = if (effectiveRev.isNotBlank()) " ($effectiveRev)" else ""

        val effectiveRegion = meta.region.ifBlank { headerInfo?.region?.ifBlank { rom.regionCode } ?: rom.regionCode }
        val primaryRegionName = when (effectiveRegion) {
            "JP" -> "(Japan)"
            "US" -> "(USA)"
            "EU" -> "(Europe)"
            "KR" -> "(Korea)"
            "ASIA" -> "(Asia)"
            "BR" -> "(Brazil)"
            else -> "(World)"
        }

        // 1. Title + Region + (Rev X)
        if (revTag.isNotBlank() && clean.isNotBlank()) {
            list.add("$clean $primaryRegionName$revTag")
        }
        // 2. Title + Region
        if (clean.isNotBlank()) {
            list.add("$clean $primaryRegionName")
        }
        // 3. Title before " - " + Region + (Rev X)
        if (clean.contains(" - ")) {
            val shortTitle = clean.substringBefore(" - ").trim()
            if (shortTitle.isNotBlank() && shortTitle != clean) {
                if (revTag.isNotBlank()) {
                    list.add("$shortTitle $primaryRegionName$revTag")
                }
                list.add("$shortTitle $primaryRegionName")
            }
        }
        // 4. Internal Header Title + Region + (Rev X)
        val internalTitle = headerInfo?.internalTitle?.let { cleanDisplayTitle(it) } ?: ""
        if (internalTitle.isNotBlank() && internalTitle != clean) {
            if (revTag.isNotBlank()) {
                list.add("$internalTitle $primaryRegionName$revTag")
            }
            list.add("$internalTitle $primaryRegionName")
        }

        // 5. Alternate titles and No-Intro original stem if it had parens
        val strippedNoIntro = rom.rawNoIntroName
            .replace(Regex("\\[MOD\\s*-\\s*M\\.\\s*RUS\\]", RegexOption.IGNORE_CASE), "")
            .replace(Regex("\\[RUS\\]", RegexOption.IGNORE_CASE), "")
            .replace(Regex("\\[!\\]"), "")
            .replace(Regex("\\[[^\\]]*\\]"), "")
            .replace(Regex("\\((Rev\\s*[A-Z0-9.]+|v\\d[^)]*|Track\\s*\\d+)\\)", RegexOption.IGNORE_CASE), "")
            .replace(Regex("\\s{2,}"), " ")
            .trim()

        if (strippedNoIntro.contains("(") && strippedNoIntro.contains(")")) {
            list.add(strippedNoIntro)
        }

        val orderedRegionSuffixes = when (effectiveRegion) {
            "US" -> listOf("(USA)", "(USA, Europe)", "(World)", "(Europe)", "(Japan)")
            "EU" -> listOf("(Europe)", "(USA, Europe)", "(World)", "(USA)", "(Japan)")
            "JP" -> listOf("(Japan)", "(World)", "(USA)", "(Europe)")
            "KR" -> listOf("(Korea)", "(USA)", "(World)", "(Japan)")
            else -> listOf("(USA)", "(Europe)", "(World)", "(Japan)", "(USA, Europe)")
        }

        for (base in listOf(clean, altClean).distinct()) {
            if (base.isBlank()) continue
            for (reg in orderedRegionSuffixes) {
                if (revTag.isNotBlank()) {
                    list.add("$base $reg$revTag")
                }
                list.add("$base $reg")
            }
            if (CD_3D_KEYS.contains(rom.platformKey)) {
                val primaryReg = orderedRegionSuffixes.first()
                list.add("$base $primaryReg (Disc 1)")
            }
            list.add(base)
        }
        return list.toList()
    }

    private fun getOrFetchBoxartDirectoryListing(libretroSys: String): List<String> {
        boxartListingCache[libretroSys]?.let { return it }
        val lock = listingLocks.getOrPut(libretroSys) { Any() }
        synchronized(lock) {
            boxartListingCache[libretroSys]?.let { return it }
            val encodedSys = encodeUrlPathSegment(libretroSys)
            val url = "$THUMBNAILS_SERVER/$encodedSys/Named_Boxarts/"
            val htmlBytes = downloadBytes(url, 5 * 1024 * 1024) ?: return emptyList()
            val html = String(htmlBytes, StandardCharsets.UTF_8)
            val parsed = ArrayList<String>(2048)
            val hrefRegex = Regex("href=\"([^\"]+\\.png)\"", RegexOption.IGNORE_CASE)
            for (m in hrefRegex.findAll(html)) {
                val rawHref = m.groupValues[1]
                if (rawHref.contains("/")) continue
                try {
                    val decoded = URLDecoder.decode(rawHref, "UTF-8").removeSuffix(".png")
                    if (decoded.isNotBlank()) parsed.add(decoded)
                } catch (_: Throwable) {
                }
            }
            boxartListingCache[libretroSys] = parsed
            return parsed
        }
    }

    private fun findBestRegionalBoxartMatch(entries: List<String>, rom: MobileRomItem): String? {
        if (entries.isEmpty()) return null
        val normQuery = normalizeTitleForMatch(rom.name)
        if (normQuery.isEmpty()) return null

        var bestName: String? = null
        var bestScore = -1
        for (entry in entries) {
            val baseEntry = cleanDisplayTitle(entry)
            val normEntry = normalizeTitleForMatch(baseEntry)
            if (normEntry.isEmpty()) continue

            var score = when {
                normEntry == normQuery -> 100
                normEntry.startsWith(normQuery) || normQuery.startsWith(normEntry) -> 78
                normQuery.length >= 5 && normEntry.contains(normQuery) -> 62
                else -> -1
            }
            if (score < 0) continue

            val upper = entry.uppercase(Locale.ROOT)
            when (rom.regionCode) {
                "JP" -> if (upper.contains("(JAPAN")) score += 25
                "US" -> if (upper.contains("(USA")) score += 25
                "EU" -> if (upper.contains("(EUROPE")) score += 25
                "WORLD" -> if (upper.contains("(WORLD") || upper.contains("(USA")) score += 18
            }
            if (upper.contains("BETA") || upper.contains("PROTO") || upper.contains("DEMO")) score -= 20

            if (score > bestScore) {
                bestScore = score
                bestName = entry
            }
        }
        return if (bestScore >= 60) bestName else null
    }

    private fun normalizeTitleForMatch(s: String): String {
        return s.lowercase(Locale.ROOT)
            .replace("&", "and")
            .replace(Regex("[^a-z0-9а-яё]"), "")
    }

    private fun encodeUrlPathSegment(segment: String): String {
        val libretroSafe = segment.replace(Regex("[&*/:`<>?\\\\|]"), "_")
        return URLEncoder.encode(libretroSafe, "UTF-8").replace("+", "%20")
    }

    private fun downloadBytes(url: String, maxBytes: Int): ByteArray? {
        var conn: HttpURLConnection? = null
        return try {
            conn = MobileCoreRegistry.openFollowingRedirects(url, connectTimeoutMs = 8000, readTimeoutMs = 15000)
            if (conn.responseCode !in 200..299) return null
            BufferedInputStream(conn.inputStream, 16 * 1024).use { input ->
                val baos = ByteArrayOutputStream()
                val buf = ByteArray(16 * 1024)
                var total = 0
                while (true) {
                    val n = input.read(buf)
                    if (n <= 0) break
                    total += n
                    if (total > maxBytes) return null
                    baos.write(buf, 0, n)
                }
                baos.toByteArray()
            }
        } catch (_: Throwable) {
            null
        } finally {
            try { conn?.disconnect() } catch (_: Throwable) {}
        }
    }

    fun isValidImageHeader(bytes: ByteArray): Boolean {
        if (bytes.size < 16) return false
        val isPng = bytes[0] == 0x89.toByte() && bytes[1] == 0x50.toByte() && bytes[2] == 0x4E.toByte() && bytes[3] == 0x47.toByte()
        val isJpeg = bytes[0] == 0xFF.toByte() && bytes[1] == 0xD8.toByte()
        val isWebp = bytes[0] == 'R'.code.toByte() && bytes[1] == 'I'.code.toByte() && bytes[2] == 'F'.code.toByte() && bytes[3] == 'F'.code.toByte()
        return isPng || isJpeg || isWebp
    }

    // === Automatic On-the-Fly Soft-Patching (IPS / BPS / UPS) for EmulationActivity ===

    fun applyIpsOrBpsIfPresent(context: Context, romFile: File): File {
        if (!romFile.exists() || !romFile.isFile || romFile.length() > 128L * 1024L * 1024L) return romFile
        val stem = romFile.nameWithoutExtension
        val parent = romFile.parentFile
        val globalPatchesDir = File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "patches")

        val patchCandidates = listOfNotNull(
            parent?.let { File(it, "$stem.ips") },
            parent?.let { File(it, "$stem.bps") },
            parent?.let { File(it, "$stem.ups") },
            File(globalPatchesDir, "$stem.ips"),
            File(globalPatchesDir, "$stem.bps"),
            File(globalPatchesDir, "$stem.ups")
        )
        val foundPatch = patchCandidates.firstOrNull { it.exists() && it.length() > 8L } ?: return romFile

        return try {
            val cacheDir = File(context.applicationContext.cacheDir, "patched_roms").apply { mkdirs() }
            val outFile = File(cacheDir, "${stem}_patched.${romFile.extension}")
            if (outFile.exists() && outFile.lastModified() >= maxOf(romFile.lastModified(), foundPatch.lastModified())) {
                return outFile
            }
            val (patched, _) = MobileCheatAndBiosManager.applyRomPatch(
                context,
                romFile.absolutePath,
                foundPatch.absolutePath,
                outFile.absolutePath
            )
            patched ?: romFile
        } catch (_: Throwable) {
            romFile
        }
    }

    fun createM3uPlaylist(discFiles: List<File>, customPlaylistName: String? = null): File? {
        if (discFiles.isEmpty()) return null
        val sorted = discFiles.sortedBy { it.name.lowercase(Locale.ROOT) }
        val cleanTitle = customPlaylistName?.takeIf { it.isNotBlank() }
            ?: cleanDisplayTitle(sorted.first().nameWithoutExtension)
        return MobileCheatAndBiosManager.createM3uPlaylist(
            cleanTitle,
            sorted.map { it.absolutePath },
            sorted.first().parentFile
        )
    }

    // === Playnite-Grade Game Metadata Persistence (playlists/storm_game_metadata.json) ===

    private val VALID_COMPLETION_STATUSES = setOf(
        "Не начато", "Играю сейчас", "Пройдено", "Пройдено на 100%", "В планах", "Отложено"
    )
    private val metadataLock = Any()
    @Volatile
    private var metadataRootCache: JSONObject? = null

    private fun formatIsoUtcNow(ms: Long = System.currentTimeMillis()): String {
        val fmt = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.US)
        fmt.timeZone = TimeZone.getTimeZone("UTC")
        return fmt.format(Date(ms))
    }

    private fun resolveMetadataFileForRead(context: Context): File {
        val pubFile = File(File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "playlists"), "storm_game_metadata.json")
        if (pubFile.exists() && pubFile.isFile && pubFile.canRead()) return pubFile
        return File(File(context.applicationContext.filesDir, "playlists"), "storm_game_metadata.json")
    }

    private fun resolveMetadataFileForWrite(context: Context): File {
        val pubRoot = File(MobileCoreRegistry.STORM_PUBLIC_ROOT)
        if (pubRoot.exists() && pubRoot.canWrite()) {
            val dir = File(pubRoot, "playlists")
            if (!dir.exists()) dir.mkdirs()
            if (dir.exists() && dir.canWrite()) {
                return File(dir, "storm_game_metadata.json")
            }
        }
        val privDir = File(context.applicationContext.filesDir, "playlists")
        if (!privDir.exists()) privDir.mkdirs()
        return File(privDir, "storm_game_metadata.json")
    }

    private fun loadMetadataRootLocked(context: Context): JSONObject {
        metadataRootCache?.let { return it }
        val file = resolveMetadataFileForRead(context)
        val parsed = if (file.exists() && file.isFile && file.length() > 0L) {
            try {
                val raw = file.readText(StandardCharsets.UTF_8)
                val obj = JSONObject(raw)
                // Support both direct { "<id>": {...} } map and { "games": { "<id>": {...} } } wrapper
                obj.optJSONObject("games") ?: obj
            } catch (_: Throwable) {
                JSONObject()
            }
        } else {
            JSONObject()
        }
        metadataRootCache = parsed
        return parsed
    }

    private fun writeMetadataRootLocked(context: Context, root: JSONObject) {
        metadataRootCache = root
        val target = resolveMetadataFileForWrite(context)
        try {
            val tmp = File(target.parentFile, "storm_game_metadata.json.tmp")
            tmp.writeText(root.toString(2), StandardCharsets.UTF_8)
            if (!tmp.renameTo(target)) {
                target.writeText(root.toString(2), StandardCharsets.UTF_8)
                tmp.delete()
            }
        } catch (_: Throwable) {
            try {
                val fallback = File(File(context.applicationContext.filesDir, "playlists").apply { mkdirs() }, "storm_game_metadata.json")
                fallback.writeText(root.toString(2), StandardCharsets.UTF_8)
            } catch (_: Throwable) {
            }
        }
    }

    private fun findMetadataKeyLocked(root: JSONObject, rom: MobileRomItem?, fallbackId: String): String? {
        if (rom != null) {
            val candidates = listOf(
                rom.id,
                rom.filePath,
                File(rom.filePath).name,
                rom.rawNoIntroName
            )
            for (c in candidates) {
                if (c.isNotBlank() && root.has(c)) return c
            }
        }
        if (fallbackId.isNotBlank() && root.has(fallbackId)) return fallbackId
        return null
    }

    fun applyMetadataToRom(context: Context, rom: MobileRomItem) {
        synchronized(metadataLock) {
            val root = loadMetadataRootLocked(context)
            val key = findMetadataKeyLocked(root, rom, rom.id) ?: return
            val entry = root.optJSONObject(key) ?: return
            val status = entry.optString("completionStatus", "Не начато")
            rom.completionStatus = if (VALID_COMPLETION_STATUSES.contains(status)) status else "Не начато"
            rom.starRating = entry.optInt("starRating", 0).coerceIn(0, 5)
            rom.userNotes = entry.optString("userNotes", "")
            rom.customCollection = entry.optString("customCollection", "")
            rom.totalPlaySeconds = max(0L, entry.optLong("totalPlaySeconds", 0L))
            rom.launchCount = max(0, entry.optInt("launchCount", 0))
            rom.lastPlayedIso = entry.optString("lastPlayedIso", "")
        }
    }

    fun saveGameMetadataPatch(
        context: Context,
        rom: MobileRomItem?,
        gameId: String,
        patch: JSONObject
    ): JSONObject {
        synchronized(metadataLock) {
            val root = loadMetadataRootLocked(context)
            val key = findMetadataKeyLocked(root, rom, gameId) ?: rom?.id ?: gameId
            val existing = root.optJSONObject(key) ?: JSONObject()

            val curStatus = existing.optString("completionStatus", rom?.completionStatus ?: "Не начато")
            val nextStatus = if (patch.has("completionStatus")) {
                val cand = patch.optString("completionStatus", "Не начато").trim()
                if (VALID_COMPLETION_STATUSES.contains(cand)) cand else "Не начато"
            } else {
                if (VALID_COMPLETION_STATUSES.contains(curStatus)) curStatus else "Не начато"
            }

            val nextRating = if (patch.has("starRating")) {
                patch.optInt("starRating", 0).coerceIn(0, 5)
            } else {
                existing.optInt("starRating", rom?.starRating ?: 0).coerceIn(0, 5)
            }

            val nextNotes = if (patch.has("userNotes")) {
                patch.optString("userNotes", "")
            } else {
                existing.optString("userNotes", rom?.userNotes ?: "")
            }

            val nextCollection = if (patch.has("customCollection")) {
                patch.optString("customCollection", "").trim()
            } else {
                existing.optString("customCollection", rom?.customCollection ?: "")
            }

            val nextPlaySec = if (patch.has("totalPlaySeconds")) {
                max(0L, patch.optLong("totalPlaySeconds", 0L))
            } else {
                max(0L, existing.optLong("totalPlaySeconds", rom?.totalPlaySeconds ?: 0L))
            }

            val nextLaunchCount = if (patch.has("launchCount")) {
                max(0, patch.optInt("launchCount", 0))
            } else {
                max(0, existing.optInt("launchCount", rom?.launchCount ?: 0))
            }

            val nextLastPlayedIso = if (patch.has("lastPlayedIso")) {
                patch.optString("lastPlayedIso", "")
            } else {
                existing.optString("lastPlayedIso", rom?.lastPlayedIso ?: "")
            }

            val updated = JSONObject()
                .put("completionStatus", nextStatus)
                .put("starRating", nextRating)
                .put("userNotes", nextNotes)
                .put("customCollection", nextCollection)
                .put("totalPlaySeconds", nextPlaySec)
                .put("launchCount", nextLaunchCount)
                .put("lastPlayedIso", nextLastPlayedIso)

            root.put(key, updated)
            writeMetadataRootLocked(context, root)

            if (rom != null) {
                rom.completionStatus = nextStatus
                rom.starRating = nextRating
                rom.userNotes = nextNotes
                rom.customCollection = nextCollection
                rom.totalPlaySeconds = nextPlaySec
                rom.launchCount = nextLaunchCount
                rom.lastPlayedIso = nextLastPlayedIso
            }

            return JSONObject(updated.toString())
                .put("ok", true)
                .put("gameId", rom?.id ?: gameId)
        }
    }

    fun recordPlaySession(
        context: Context,
        rom: MobileRomItem?,
        gameIdOrPath: String,
        addedSeconds: Long,
        countLaunch: Boolean
    ) {
        val targetId = rom?.id ?: if (gameIdOrPath.isNotBlank()) computeRomId(gameIdOrPath) else ""
        if (targetId.isBlank()) return
        synchronized(metadataLock) {
            val root = loadMetadataRootLocked(context)
            val key = findMetadataKeyLocked(root, rom, targetId) ?: targetId
            val existing = root.optJSONObject(key) ?: JSONObject()

            val prevSec = max(0L, existing.optLong("totalPlaySeconds", rom?.totalPlaySeconds ?: 0L))
            val prevCount = max(0, existing.optInt("launchCount", rom?.launchCount ?: 0))
            val nextSec = prevSec + max(0L, addedSeconds)
            val nextCount = if (countLaunch) prevCount + 1 else prevCount
            val iso = formatIsoUtcNow()

            val patch = JSONObject()
                .put("totalPlaySeconds", nextSec)
                .put("launchCount", nextCount)
                .put("lastPlayedIso", iso)
            saveGameMetadataPatch(context, rom, key, patch)
        }
    }
}
