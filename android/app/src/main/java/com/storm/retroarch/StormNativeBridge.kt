package com.storm.retroarch

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.os.Handler
import android.os.Looper
import android.os.VibrationEffect
import android.os.Vibrator
import android.webkit.JavascriptInterface
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.nio.charset.StandardCharsets
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.Executors

class StormNativeBridge(
    private val activity: MainActivity
) {

    companion object {
        const val PREFS_STUDIO_UI = "storm_retroarch_studio_ui_prefs"
        private const val PREFS_UNIFIED = "storm_retroarch_prefs"
        private const val PREFS_EMU = "storm_retroarch_emu_prefs"
    }

    private val appCtx: Context = activity.applicationContext
    private val mainHandler = Handler(Looper.getMainLooper())
    private val bgExecutor = Executors.newFixedThreadPool(3)

    private val cachedLibrary = CopyOnWriteArrayList<MobileRomItem>()

    @Volatile
    private var startupWarmDone = false

    @Volatile
    private var cachedBiosItems: List<MobileBiosItem>? = null

    @Volatile
    private var lastUpdateInfo: MobileUpdateInfo = MobileUpdateInfo(
        updateAvailable = false,
        currentVersion = "2.6.0",
        latestVersion = "2.6.0",
        apkDownloadUrl = "",
        releaseNotes = "",
        releasePageUrl = "https://github.com/ReiKatari/STORM_RETROARCH/releases",
        statusMessage = "STORM RETROARCH 2.6.0"
    )

    @Volatile
    var pendingPatchGameId: String = ""

    init {
        MobileCoreRegistry.init(appCtx)
        MobileCheatAndBiosManager.initialize(appCtx)
        MobileControllerProfileManager.ensureProfilesLoaded(appCtx)
        bgExecutor.execute { warmStartupCachesAsync() }
    }

    private fun warmStartupCachesAsync() {
        try {
            ensurePublicDirectoryStructure()
            MobileCoreRegistry.init(appCtx)
            MobileCoreRegistry.syncPublicCoresToExecutableDir(appCtx)

            val games = MobileLibraryAndCoverEngine.scanLibraryDirectories(appCtx)
            cachedLibrary.clear()
            cachedLibrary.addAll(games)

            val sysDir = MobileCheatAndBiosManager.getEffectiveSystemDir(appCtx)
            cachedBiosItems = MobileCheatAndBiosManager.scanBiosCatalog(sysDir)

            startupWarmDone = true
            emitStormEvent("bootstrapUpdated", buildFullBootstrapJson(games))
            fetchMissingCoversInBackground(games)
        } catch (_: Throwable) {
            startupWarmDone = true
        }
    }

    fun emitStormEvent(eventType: String, payload: Any) {
        val serializedPayload = when (payload) {
            is JSONObject -> payload.toString()
            is JSONArray -> payload.toString()
            is String -> JSONObject.quote(payload)
            is Boolean, is Number -> payload.toString()
            else -> JSONObject.quote(payload.toString())
        }
        mainHandler.post {
            activity.evaluateJsOnMain(
                "if (typeof window.onStormEvent === 'function') { window.onStormEvent(${JSONObject.quote(eventType)}, $serializedPayload); }"
            )
        }
    }

    fun emitToast(message: String) {
        emitStormEvent("toast", JSONObject().put("message", message))
    }

    fun hasAllFilesStorageAccess(): Boolean {
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            Environment.isExternalStorageManager()
        } else {
            val root = File(MobileCoreRegistry.STORM_PUBLIC_ROOT)
            try {
                if (!root.exists()) root.mkdirs()
                root.exists() && root.canRead()
            } catch (_: Throwable) {
                false
            }
        }
    }

    fun ensurePublicDirectoryStructure() {
        if (!hasAllFilesStorageAccess()) return
        val root = File(MobileCoreRegistry.STORM_PUBLIC_ROOT)
        val subDirs = listOf(
            "roms", "cores", "system", "saves", "states", "cheats",
            "thumbnails", "screenshots", "bezels", "shaders", "config",
            "autoconfig", "patches", "backups", "updates", "playlists"
        )
        for (sub in subDirs) {
            try {
                File(root, sub).mkdirs()
            } catch (_: Throwable) {
            }
        }
    }

    // === 1. Bootstrap, Haptics & Settings Sync ===

    @JavascriptInterface
    fun getBootstrapJson(): String {
        // Non-blocking immediate response so WebView first paint never stalls;
        // background warmStartupCachesAsync() pushes bootstrapUpdated as soon as scans finish.
        return buildFullBootstrapJson(cachedLibrary.toList(), includeSlowScans = startupWarmDone).toString()
    }

    @JavascriptInterface
    fun getAppInfo(): String = getBootstrapJson()

    fun buildFullBootstrapJson(
        games: List<MobileRomItem> = cachedLibrary,
        includeSlowScans: Boolean = true
    ): JSONObject {
        val root = JSONObject()
        root.put("version", "2.6.0")
        root.put("versionCode", 260)
        root.put("warming", !startupWarmDone)
        root.put("abi", MobileCoreRegistry.detectDeviceAbi())
        root.put("sdkInt", Build.VERSION.SDK_INT)
        root.put("storageGranted", hasAllFilesStorageAccess())
        root.put("storagePath", MobileCoreRegistry.STORM_PUBLIC_ROOT)
        root.put("settings", loadStudioSettingsJson())
        root.put("games", serializeGamesArray(games))
        root.put("cores", serializeCoresCatalogArray())
        root.put("bios", if (includeSlowScans || cachedBiosItems != null) serializeBiosArray() else JSONArray())
        root.put("saves", if (includeSlowScans) serializeSaveSlotsOverview(games) else JSONArray())
        root.put("controllers", serializeControllersArray())
        root.put("updateInfo", serializeUpdateInfo(lastUpdateInfo))
        return root
    }

    private fun loadStudioSettingsJson(): JSONObject {
        StormThemeEngine.loadFromPrefs(appCtx)
        val lang = StormLocalization.loadFromPrefs(appCtx)
        val uiPrefs = appCtx.getSharedPreferences(PREFS_STUDIO_UI, Context.MODE_PRIVATE)
        val savedJson = uiPrefs.getString("settings_blob", null)
        val obj = if (!savedJson.isNullOrBlank()) {
            try { JSONObject(savedJson) } catch (_: Throwable) { JSONObject() }
        } else {
            JSONObject()
        }
        if (!obj.has("theme")) obj.put("theme", StormThemeEngine.currentTheme.id)
        if (!obj.has("accent")) obj.put("accent", StormThemeEngine.currentAccent.id)
        if (!obj.has("language")) obj.put("language", lang)
        if (!obj.has("highRefresh120")) obj.put("highRefresh120", true)
        return obj
    }

    @JavascriptInterface
    fun saveSettingsJson(patchJson: String): Boolean {
        return try {
            val patch = JSONObject(patchJson)
            val current = loadStudioSettingsJson()
            val keys = patch.keys()
            while (keys.hasNext()) {
                val k = keys.next()
                current.put(k, patch.get(k))
            }

            appCtx.getSharedPreferences(PREFS_STUDIO_UI, Context.MODE_PRIVATE)
                .edit()
                .putString("settings_blob", current.toString())
                .apply()

            // Synchronize with StormThemeEngine, StormLocalization & EmulationActivity prefs
            if (patch.has("theme")) {
                StormThemeEngine.setTheme(patch.optString("theme", "MIDNIGHT"), appCtx)
            }
            if (patch.has("accent")) {
                StormThemeEngine.setAccent(patch.optString("accent", "VIOLET"), appCtx)
            }
            if (patch.has("language")) {
                StormLocalization.setLanguage(patch.optString("language", "ru"), appCtx)
            }

            val unifiedEdit = appCtx.getSharedPreferences(PREFS_UNIFIED, Context.MODE_PRIVATE).edit()
            val emuEdit = appCtx.getSharedPreferences(PREFS_EMU, Context.MODE_PRIVATE).edit()
            if (patch.has("shaderPreset")) {
                val rawPreset = patch.optString("shaderPreset", "crt_royale")
                unifiedEdit.putString("shaderPreset", rawPreset)
                val mode = when {
                    rawPreset.contains("crt", true) || rawPreset.contains("scanline", true) -> NativeLibretroBridge.SHADER_CRT_SCANLINES
                    rawPreset.contains("lcd", true) || rawPreset.contains("grid", true) -> NativeLibretroBridge.SHADER_LCD_GRID
                    rawPreset.contains("bilinear", true) || rawPreset.contains("smooth", true) -> NativeLibretroBridge.SHADER_BILINEAR_SMOOTH
                    else -> NativeLibretroBridge.SHADER_PIXEL_PERFECT
                }
                emuEdit.putInt("shader_mode", mode)
            }
            if (patch.has("aspectRatio")) {
                val rawAspect = patch.optString("aspectRatio", "auto")
                unifiedEdit.putString("aspectRatio", rawAspect)
                val aspect = when (rawAspect.lowercase(Locale.ROOT)) {
                    "16:9", "widescreen" -> NativeLibretroBridge.ASPECT_16_9
                    "4:3" -> NativeLibretroBridge.ASPECT_4_3
                    "stretch", "full" -> NativeLibretroBridge.ASPECT_STRETCH
                    "integer" -> NativeLibretroBridge.ASPECT_INTEGER
                    else -> NativeLibretroBridge.ASPECT_CORE_FIT
                }
                emuEdit.putInt("aspect_mode", aspect)
                emuEdit.putBoolean("integer_scale", aspect == NativeLibretroBridge.ASPECT_INTEGER)
            }
            if (patch.has("rewindEnabled")) {
                val rew = patch.optBoolean("rewindEnabled", true)
                unifiedEdit.putBoolean("rewindEnabled", rew)
                emuEdit.putBoolean("rewind_enabled", rew)
            }
            if (patch.has("preAmpDb")) {
                unifiedEdit.putFloat("audioGainDb", patch.optDouble("preAmpDb", 0.0).toFloat())
            }
            if (patch.has("gyroStick")) {
                val gyroOn = patch.optBoolean("gyroStick", false)
                unifiedEdit.putString("gyroTiltStick", if (gyroOn) "left" else "off")
            }
            if (patch.has("touchOpacity")) {
                val op = patch.optInt("touchOpacity", 85)
                unifiedEdit.putInt("touchOpacity", op)
                emuEdit.putInt("touch_opacity", op)
            }
            if (patch.has("touchScale")) {
                val sc = patch.optInt("touchScale", 100)
                unifiedEdit.putInt("touchScale", sc)
                emuEdit.putInt("touch_scale", sc)
            }
            if (patch.has("highRefresh120")) {
                val wantHigh = patch.optBoolean("highRefresh120", true)
                unifiedEdit.putBoolean("highRefresh120Hz", wantHigh)
                emuEdit.putBoolean("high_refresh_120hz", wantHigh)
                mainHandler.post { activity.applyPreferredRefreshRate(wantHigh) }
            }
            unifiedEdit.apply()
            emuEdit.apply()
            true
        } catch (_: Throwable) {
            false
        }
    }

    @JavascriptInterface
    fun haptic(kind: String) {
        try {
            @Suppress("DEPRECATION")
            val vib = appCtx.getSystemService(Context.VIBRATOR_SERVICE) as? Vibrator ?: return
            if (!vib.hasVibrator()) return
            val ms = when (kind.lowercase(Locale.ROOT)) {
                "heavy", "launch" -> 22L
                "success" -> 16L
                else -> 9L
            }
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                val amp = if (ms > 15L) 150 else 75
                vib.vibrate(VibrationEffect.createOneShot(ms, amp))
            } else {
                @Suppress("DEPRECATION")
                vib.vibrate(ms)
            }
        } catch (_: Throwable) {
        }
    }

    // === 2. Library, SAF Folder/File Pickers, Cover Engine & Game Launcher ===

    @JavascriptInterface
    fun getLibrary(): String {
        if (cachedLibrary.isEmpty()) {
            cachedLibrary.addAll(MobileLibraryAndCoverEngine.scanLibraryDirectories(appCtx))
        }
        return serializeGamesArray(cachedLibrary).toString()
    }

    @JavascriptInterface
    fun scanPath(path: String) {
        bgExecutor.execute {
            emitStormEvent("scanProgress", JSONObject().put("message", path.ifBlank { MobileCoreRegistry.STORM_PUBLIC_ROOT }))
            val customRoots = if (path.isNotBlank() && File(path).exists()) {
                val f = File(path)
                if (f.absolutePath == MobileCoreRegistry.STORM_PUBLIC_ROOT) null else listOf(f)
            } else null

            val scanned = MobileLibraryAndCoverEngine.scanLibraryDirectories(appCtx, customRoots)
            cachedLibrary.clear()
            cachedLibrary.addAll(scanned)
            emitStormEvent("libraryUpdated", JSONObject().put("games", serializeGamesArray(scanned)))
            fetchMissingCoversInBackground(scanned)
        }
    }

    @JavascriptInterface
    fun pickRomFolder() {
        mainHandler.post { activity.openRomFolderPicker() }
    }

    @JavascriptInterface
    fun pickSingleRom() {
        mainHandler.post { activity.openSingleRomPicker() }
    }

    @JavascriptInterface
    fun pickRomFile() = pickSingleRom()

    @JavascriptInterface
    fun refreshCovers() {
        bgExecutor.execute {
            if (cachedLibrary.isEmpty()) {
                cachedLibrary.addAll(MobileLibraryAndCoverEngine.scanLibraryDirectories(appCtx))
            }
            fetchMissingCoversInBackground(cachedLibrary.toList(), forceNotifyToast = true)
        }
    }

    private fun fetchMissingCoversInBackground(games: List<MobileRomItem>, forceNotifyToast: Boolean = false) {
        val missing = games.filter { it.coverPath.isNullOrBlank() || !File(it.coverPath!!).exists() }
        if (missing.isEmpty()) {
            if (forceNotifyToast) {
                emitToast(if (StormLocalization.currentLanguage == "ru") "Все 3D обложки актуальны" else "All boxart covers are up to date")
            }
            return
        }
        var resolvedCount = 0
        for (rom in missing) {
            MobileLibraryAndCoverEngine.fetchCoverArtAsync(appCtx, rom) { _, file ->
                if (file != null && file.exists()) {
                    resolvedCount++
                    emitStormEvent("libraryUpdated", JSONObject().put("games", serializeGamesArray(cachedLibrary)))
                }
            }
        }
    }

    @JavascriptInterface
    fun toggleFavorite(gameId: String): Boolean {
        val rom = findRomByIdOrPath(gameId) ?: return false
        rom.isFavorite = !rom.isFavorite
        MobileLibraryAndCoverEngine.saveRomPreferenceState(appCtx, rom)
        return rom.isFavorite
    }

    @JavascriptInterface
    fun setPreferredCore(gameId: String, coreId: String): Boolean {
        val rom = findRomByIdOrPath(gameId) ?: return false
        rom.assignedCoreId = coreId.trim()
        MobileLibraryAndCoverEngine.saveRomPreferenceState(appCtx, rom)
        return true
    }

    @JavascriptInterface
    fun saveGameMetadataJson(gameId: String, jsonPatch: String): String {
        return try {
            val patch = JSONObject(jsonPatch)
            val rom = findRomByIdOrPath(gameId)
            val saved = MobileLibraryAndCoverEngine.saveGameMetadataPatch(appCtx, rom, gameId, patch)
            saved.toString()
        } catch (t: Throwable) {
            JSONObject()
                .put("ok", false)
                .put("gameId", gameId)
                .put("error", t.message ?: "Invalid metadata patch")
                .toString()
        }
    }

    @JavascriptInterface
    fun launchGame(gameId: String) {
        launchGame(gameId, "", -1)
    }

    @JavascriptInterface
    fun launchGame(gameId: String, coreId: String) {
        launchGame(gameId, coreId, -1)
    }

    @JavascriptInterface
    fun launchGame(gameId: String, coreId: String, stateSlot: Int) {
        val rom = findRomByIdOrPath(gameId) ?: run {
            val directFile = File(gameId)
            if (directFile.exists() && directFile.isFile) {
                MobileLibraryAndCoverEngine.buildRomItemForFile(appCtx, directFile)
            } else null
        }

        if (rom == null || !File(rom.filePath).exists()) {
            val msg = if (StormLocalization.currentLanguage == "ru") {
                "Выберите папку с ROM или образ игры для запуска"
            } else {
                "Please add a ROM folder or game file first"
            }
            emitToast(msg)
            return
        }

        val effectiveCoreId = coreId.trim().ifBlank { rom.assignedCoreId }
        val core = MobileCoreRegistry.findCoreById(effectiveCoreId, appCtx)
            ?: MobileCoreRegistry.getDefaultCoreForPlatform(rom.platformKey, rom.extension, appCtx)
            ?: MobileCoreRegistry.findCoreById("snes9x", appCtx)

        if (core == null) {
            emitStormEvent("coreError", JSONObject().put("coreId", effectiveCoreId).put("message", "Core not found"))
            return
        }

        // Materialize per-core & per-game Libretro .opt configuration before launch
        MobileCoreRegistry.syncCoreOptionsToLibretroOptFiles(appCtx, core.id, rom.id, rom.rawNoIntroName)

        val installedSo = MobileCoreRegistry.resolveInstalledCoreFile(appCtx, core)
        if (installedSo != null && installedSo.exists()) {
            startEmulationActivityOnMain(rom, core, installedSo, stateSlot)
            return
        }

        // Unbundled core not yet downloaded: auto-download from Mirror 1 -> Mirror 2 then launch immediately!
        emitToast(
            if (StormLocalization.currentLanguage == "ru") {
                "Загрузка ядра ${core.displayName}..."
            } else {
                "Downloading core ${core.displayName}..."
            }
        )
        MobileCoreRegistry.downloadCoreAsync(
            context = appCtx,
            core = core,
            onProgress = { pct, mirrorName ->
                emitStormEvent(
                    "coreProgress",
                    JSONObject()
                        .put("coreId", core.id)
                        .put("percent", pct)
                        .put("progress", pct)
                        .put("mirror", mirrorName)
                )
            },
            onDone = { ok, soFile, msg ->
                if (ok && soFile != null && soFile.exists()) {
                    emitStormEvent(
                        "coreInstalled",
                        JSONObject()
                            .put("coreId", core.id)
                            .put("id", core.id)
                            .put("installed", true)
                            .put("mirror", msg)
                    )
                    startEmulationActivityOnMain(rom, core, soFile, stateSlot)
                } else {
                    emitStormEvent(
                        "coreError",
                        JSONObject()
                            .put("coreId", core.id)
                            .put("message", msg)
                    )
                }
            }
        )
    }

    private fun startEmulationActivityOnMain(
        rom: MobileRomItem,
        core: MobileCoreInfo,
        soFile: File,
        stateSlot: Int
    ) {
        rom.lastPlayedMs = System.currentTimeMillis()
        rom.assignedCoreId = core.id
        MobileLibraryAndCoverEngine.saveRomPreferenceState(appCtx, rom)
        MobileLibraryAndCoverEngine.recordPlaySession(appCtx, rom, rom.id, 0L, countLaunch = true)

        val accentIdx = StormThemeEngine.ACCENTS.indexOfFirst { it.id == StormThemeEngine.currentAccent.id }
        mainHandler.post {
            val intent = Intent(activity, EmulationActivity::class.java).apply {
                putExtra(EmulationActivity.EXTRA_ROM_PATH, rom.filePath)
                putExtra(EmulationActivity.EXTRA_ROM_NAME, rom.name)
                putExtra(EmulationActivity.EXTRA_PLATFORM, rom.platform)
                putExtra(EmulationActivity.EXTRA_CORE_SO_PATH, soFile.absolutePath)
                putExtra(EmulationActivity.EXTRA_CORE_NAME, core.displayName)
                putExtra(EmulationActivity.EXTRA_THEME_ID, StormThemeEngine.currentTheme.id)
                putExtra(EmulationActivity.EXTRA_ACCENT_IDX, accentIdx)
                putExtra(EmulationActivity.EXTRA_LANG_CODE, StormLocalization.currentLanguage)
                putExtra("core_id", core.id)
                putExtra("game_id", rom.id)
                putExtra("state_slot", stateSlot)
                putExtra("auto_load_slot", stateSlot)
            }
            activity.startActivity(intent)
        }
    }

    private fun findRomByIdOrPath(gameIdOrPath: String?): MobileRomItem? {
        if (gameIdOrPath.isNullOrBlank()) return null
        val q = gameIdOrPath.trim()
        cachedLibrary.firstOrNull { it.id == q || it.filePath == q || it.rawNoIntroName == q }?.let { return it }
        val scanned = MobileLibraryAndCoverEngine.scanLibraryDirectories(appCtx)
        cachedLibrary.clear()
        cachedLibrary.addAll(scanned)
        return cachedLibrary.firstOrNull { it.id == q || it.filePath == q || it.rawNoIntroName == q }
    }

    // === 3. 252-Core Loader, Multi-Mirror Downloader, Batch Packs & Core Options ===

    @JavascriptInterface
    fun getCoresCatalog(): String {
        return serializeCoresCatalogArray().toString()
    }

    @JavascriptInterface
    fun downloadCore(coreId: String) {
        val core = MobileCoreRegistry.findCoreById(coreId, appCtx)
        if (core == null) {
            emitStormEvent("coreError", JSONObject().put("coreId", coreId).put("message", "Unknown core: $coreId"))
            return
        }

        // If user clicks download on an already bundled core, still allow updating from mirror OR report ready if offline
        MobileCoreRegistry.downloadCoreAsync(
            context = appCtx,
            core = core,
            onProgress = { pct, mirrorName ->
                emitStormEvent(
                    "coreProgress",
                    JSONObject()
                        .put("coreId", core.id)
                        .put("percent", pct)
                        .put("progress", pct)
                        .put("mirror", mirrorName)
                )
            },
            onDone = { ok, soFile, msg ->
                if (ok && soFile != null) {
                    emitStormEvent(
                        "coreInstalled",
                        JSONObject()
                            .put("coreId", core.id)
                            .put("id", core.id)
                            .put("installed", true)
                            .put("mirror", msg)
                    )
                } else if (MobileCoreRegistry.isCoreBundled(appCtx, core)) {
                    emitStormEvent(
                        "coreInstalled",
                        JSONObject()
                            .put("coreId", core.id)
                            .put("id", core.id)
                            .put("installed", true)
                            .put("bundled", true)
                    )
                } else {
                    emitStormEvent(
                        "coreError",
                        JSONObject()
                            .put("coreId", core.id)
                            .put("message", msg)
                    )
                }
            }
        )
    }

    @JavascriptInterface
    fun downloadCorePack(packId: String) {
        MobileCoreRegistry.downloadCorePack(
            context = appCtx,
            packId = packId,
            onProgress = { pct, activeCoreId, mirrorName, completed, total ->
                if (activeCoreId.isNotBlank()) {
                    emitStormEvent(
                        "coreProgress",
                        JSONObject()
                            .put("coreId", activeCoreId)
                            .put("percent", pct)
                            .put("progress", pct)
                            .put("mirror", mirrorName)
                            .put("packId", packId)
                    )
                }
            },
            onDone = { _, completed, total, _ ->
                emitStormEvent("bootstrapUpdated", JSONObject().put("cores", serializeCoresCatalogArray()))
                emitToast(
                    if (StormLocalization.currentLanguage == "ru") {
                        "Пакет $packId готов ($completed / $total ядер)"
                    } else {
                        "Core pack $packId ready ($completed / $total cores)"
                    }
                )
            }
        )
    }

    @JavascriptInterface
    fun importCoreFile() {
        mainHandler.post { activity.openCoreFilePicker() }
    }

    @JavascriptInterface
    fun deleteCore(coreId: String): Boolean {
        val core = MobileCoreRegistry.findCoreById(coreId, appCtx) ?: return false
        val downloaded = MobileCoreRegistry.resolveDownloadedCoreFile(appCtx, core) ?: return false
        return try {
            downloaded.setWritable(true, true)
            downloaded.delete()
        } catch (_: Throwable) {
            false
        }
    }

    @JavascriptInterface
    fun getCoreOptionsJson(coreId: String, gameId: String): String {
        val rawObj = MobileCoreRegistry.getCoreOptions(appCtx, coreId, gameId.ifBlank { null })
        val values = rawObj.optJSONObject("values") ?: JSONObject()
        val uiDraft = JSONObject()
        uiDraft.put("coreId", coreId)
        uiDraft.put("gameId", gameId)
        uiDraft.put("resolution", values.optString("ui_resolution", "2x (720p HD)"))
        uiDraft.put("cpuOverclock", values.optString("ui_cpuOverclock", "100% (Штатная)"))
        uiDraft.put("pgxp", values.optString("ui_pgxp", "true").equals("true", true))
        uiDraft.put("frameSkip", values.optString("ui_frameSkip", "0 (60 FPS)"))
        uiDraft.put("biosMode", values.optString("ui_biosMode", "Оригинальный BIOS / Авто"))
        uiDraft.put("widescreenHack", values.optString("ui_widescreenHack", "false").equals("true", true))
        uiDraft.put("fastCdLoad", values.optString("ui_fastCdLoad", "true").equals("true", true))
        uiDraft.put("schema", rawObj.optJSONArray("schema") ?: JSONArray())
        uiDraft.put("values", values)
        return uiDraft.toString()
    }

    @JavascriptInterface
    fun saveCoreOptionsJson(coreId: String, gameId: String, draftJson: String): Boolean {
        return try {
            val draft = JSONObject(draftJson)
            val map = LinkedHashMap<String, String>()

            // Preserve UI draft fields
            val resStr = draft.optString("resolution", "2x (720p HD)")
            val cpuStr = draft.optString("cpuOverclock", "100% (Штатная)")
            val pgxp = draft.optBoolean("pgxp", true)
            val skipStr = draft.optString("frameSkip", "0 (60 FPS)")
            val biosStr = draft.optString("biosMode", "Оригинальный BIOS / Авто")
            val wide = draft.optBoolean("widescreenHack", false)
            val fastCd = draft.optBoolean("fastCdLoad", true)

            map["ui_resolution"] = resStr
            map["ui_cpuOverclock"] = cpuStr
            map["ui_pgxp"] = pgxp.toString()
            map["ui_frameSkip"] = skipStr
            map["ui_biosMode"] = biosStr
            map["ui_widescreenHack"] = wide.toString()
            map["ui_fastCdLoad"] = fastCd.toString()

            val scaleNum = Regex("(\\d+)x").find(resStr)?.groupValues?.getOrNull(1)?.toIntOrNull()?.coerceIn(1, 8) ?: 2
            val cpuPct = Regex("(\\d+)%").find(cpuStr)?.groupValues?.getOrNull(1)?.toIntOrNull()?.coerceIn(50, 300) ?: 100
            val skipNum = Regex("(\\d+)").find(skipStr)?.groupValues?.getOrNull(1)?.toIntOrNull()?.coerceIn(0, 4) ?: 0
            val isAutoSkip = skipStr.contains("Auto", true) || skipStr.contains("Авто", true)
            val hleBios = biosStr.contains("HLE", true) || biosStr.contains("Fast", true) || biosStr.contains("Быстр", true)

            // Translate into exact Libretro core option keys for all flagship cores
            when (coreId.trim().lowercase(Locale.ROOT)) {
                "swanstation" -> {
                    map["swanstation_GPU_ResolutionScale"] = scaleNum.toString()
                    map["swanstation_GPU_PGXPEnable"] = pgxp.toString()
                    map["swanstation_GPU_PGXPTextureCorrection"] = pgxp.toString()
                    map["swanstation_GPU_WidescreenHack"] = wide.toString()
                    map["swanstation_CDROM_LoadImagePatches"] = fastCd.toString()
                    map["swanstation_CPU_Overclock"] = cpuPct.toString()
                    map["swanstation_BIOS_PatchFastBoot"] = hleBios.toString()
                }
                "pcsx_rearmed" -> {
                    map["pcsx_rearmed_neon_enhancement_enable"] = if (scaleNum >= 2) "enabled" else "disabled"
                    map["pcsx_rearmed_neon_enhancement_no_main"] = if (scaleNum >= 2) "enabled" else "disabled"
                    map["pcsx_rearmed_frameskip_type"] = when {
                        isAutoSkip -> "auto"
                        skipNum > 0 -> "fixed"
                        else -> "disabled"
                    }
                    map["pcsx_rearmed_frameskip_interval"] = skipNum.toString()
                    map["pcsx_rearmed_psxclock"] = ((cpuPct * 57) / 100).coerceIn(45, 100).toString()
                    map["pcsx_rearmed_cd_turbo"] = if (fastCd) "enabled" else "disabled"
                    map["pcsx_rearmed_bios"] = if (hleBios) "hle" else "auto"
                }
                "mupen64plus_next_gles3", "mupen64plus_next_gles2", "mupen64plus_next" -> {
                    map["mupen64plus-43screensize"] = when {
                        scaleNum <= 1 -> "320x240"
                        scaleNum == 2 -> "640x480"
                        scaleNum == 3 -> "960x720"
                        scaleNum == 4 -> "1280x960"
                        else -> "1600x1200"
                    }
                    map["mupen64plus-169screensize"] = when {
                        scaleNum <= 1 -> "640x360"
                        scaleNum == 2 -> "960x540"
                        scaleNum == 3 -> "1280x720"
                        else -> "1920x1080"
                    }
                    map["mupen64plus-aspect"] = if (wide) "16:9 adjusted" else "4:3"
                    map["mupen64plus-CountPerOp"] = if (cpuPct > 100) "1" else "0"
                }
                "ppsspp" -> {
                    val w = 480 * scaleNum
                    val h = 272 * scaleNum
                    map["ppsspp_internal_resolution"] = "${w}x${h}"
                    map["ppsspp_auto_frameskip"] = if (isAutoSkip) "enabled" else "disabled"
                    map["ppsspp_frameskip"] = skipNum.toString()
                    map["ppsspp_locked_cpu_speed"] = if (cpuPct > 100) "333" else "off"
                }
                "flycast" -> {
                    map["reicast_internal_resolution"] = when {
                        scaleNum <= 1 -> "640x480"
                        scaleNum == 2 -> "960x720"
                        scaleNum == 3 -> "1280x960"
                        else -> "1920x1440"
                    }
                    map["reicast_widescreen_hack"] = if (wide) "enabled" else "disabled"
                    map["reicast_boot_to_bios"] = if (hleBios) "disabled" else "disabled"
                }
                "mgba" -> {
                    map["mgba_frameskip"] = if (isAutoSkip) "auto" else if (skipNum > 0) "fixed_interval" else "disabled"
                    map["mgba_skip_bios"] = if (hleBios) "ON" else "OFF"
                }
                "snes9x" -> {
                    map["snes9x_overclock_cycles"] = if (cpuPct >= 150) "compatible" else if (cpuPct > 100) "light" else "disabled"
                    map["snes9x_aspect"] = if (wide) "16:9" else "4:3"
                }
                "genesis_plus_gx" -> {
                    map["genesis_plus_gx_overclock"] = "${cpuPct.coerceIn(100, 200)}%"
                }
                "dosbox_pure" -> {
                    map["dosbox_pure_cycles"] = if (cpuPct > 100) "max" else "auto"
                    map["dosbox_pure_aspect_correction"] = (!wide).toString()
                }
            }

            // Include any explicit raw key-values passed in draft.values
            draft.optJSONObject("values")?.let { explicitVals ->
                val kIter = explicitVals.keys()
                while (kIter.hasNext()) {
                    val k = kIter.next()
                    map[k] = explicitVals.optString(k, "")
                }
            }

            val matchedRom = findRomByIdOrPath(gameId)
            MobileCoreRegistry.saveCoreOptions(
                context = appCtx,
                coreId = coreId,
                options = map,
                gameId = gameId.ifBlank { null },
                romStem = matchedRom?.rawNoIntroName
            )
        } catch (_: Throwable) {
            false
        }
    }

    // === 4. 26,797+ Cheats Catalog, GitHub Raw .cht Fetcher & Saver ===

    @JavascriptInterface
    fun searchCheatsCatalogJson(
        query: String,
        systemFilter: String,
        libraryOnly: Boolean,
        limit: Int
    ): String {
        val matches = MobileCheatAndBiosManager.searchCatalog(
            context = appCtx,
            query = query,
            systemFilter = systemFilter,
            libraryGames = cachedLibrary,
            libraryOnly = libraryOnly,
            limit = limit.coerceIn(1, 250)
        )
        val arr = JSONArray()
        for (item in matches) {
            arr.put(
                JSONObject()
                    .put("id", "${item.systemName}::${item.gameTitle}")
                    .put("gameName", item.gameTitle)
                    .put("cleanTitle", item.cleanTitle)
                    .put("title", item.gameTitle)
                    .put("platformFolder", item.systemName)
                    .put("system", item.systemName)
                    .put("platformKey", item.platformKey)
                    .put("isLibraryMatch", item.isInLibrary)
                    .put("matchedRomPath", item.matchedRomPath ?: "")
                    .put("isCachedLocally", item.isCachedLocally)
            )
        }
        return arr.toString()
    }

    @JavascriptInterface
    fun getGameCheatsJson(systemName: String, gameTitle: String, gameId: String): String {
        var resolvedSys = systemName
        var resolvedTitle = gameTitle
        if (gameId.contains("::")) {
            val split = gameId.split("::", limit = 2)
            if (resolvedSys.isBlank()) resolvedSys = split[0]
            if (resolvedTitle.isBlank()) resolvedTitle = split[1]
        } else {
            findRomByIdOrPath(gameId)?.let { rom ->
                if (resolvedSys.isBlank()) resolvedSys = rom.platformKey
                if (resolvedTitle.isBlank()) resolvedTitle = rom.rawNoIntroName
            }
        }

        val entries = MobileCheatAndBiosManager.getGameCheats(appCtx, resolvedSys, resolvedTitle)
        val arr = JSONArray()
        for (e in entries) {
            arr.put(
                JSONObject()
                    .put("index", e.index)
                    .put("description", e.description)
                    .put("originalDescription", e.originalDescription)
                    .put("code", e.code)
                    .put("enabled", e.isEnabled)
                    .put("isEnabled", e.isEnabled)
            )
        }
        return arr.toString()
    }

    @JavascriptInterface
    fun saveGameCheatsJson(
        systemName: String,
        gameTitle: String,
        gameId: String,
        entriesJson: String
    ): Boolean {
        return try {
            val arr = JSONArray(entriesJson)
            val list = ArrayList<MobileCheatEntry>(arr.length())
            for (i in 0 until arr.length()) {
                val obj = arr.optJSONObject(i) ?: continue
                val desc = obj.optString("description", "Cheat #${i + 1}")
                val orig = obj.optString("originalDescription", desc)
                val code = obj.optString("code", "").trim()
                if (code.isEmpty()) continue
                val en = obj.optBoolean("enabled", obj.optBoolean("isEnabled", false))
                list.add(MobileCheatEntry(list.size, desc, orig, code, en))
            }

            val matchedRom = findRomByIdOrPath(gameId)
                ?: cachedLibrary.firstOrNull {
                    it.name.equals(MobileLibraryAndCoverEngine.cleanDisplayTitle(gameTitle), ignoreCase = true)
                }

            MobileCheatAndBiosManager.saveGameCheats(
                context = appCtx,
                systemName = systemName,
                gameTitle = gameTitle,
                entries = list,
                romFilePath = matchedRom?.filePath
            )
        } catch (_: Throwable) {
            false
        }
    }

    @JavascriptInterface
    fun downloadLibraryCheatsBatch() {
        bgExecutor.execute {
            val games = if (cachedLibrary.isNotEmpty()) cachedLibrary.toList() else MobileLibraryAndCoverEngine.scanLibraryDirectories(appCtx)
            var synced = 0
            for (rom in games) {
                val found = MobileCheatAndBiosManager.searchCatalog(
                    context = appCtx,
                    query = rom.name,
                    systemFilter = rom.platformKey,
                    limit = 3
                ).firstOrNull()
                if (found != null) {
                    val entries = MobileCheatAndBiosManager.getGameCheats(appCtx, found.systemName, found.gameTitle)
                    if (entries.isNotEmpty()) {
                        MobileCheatAndBiosManager.saveGameCheats(appCtx, found.systemName, found.gameTitle, entries, rom.filePath)
                        synced++
                    }
                }
            }
            emitToast(
                if (StormLocalization.currentLanguage == "ru") {
                    "Читы синхронизированы для игр библиотеки: $synced"
                } else {
                    "Synchronized cheats for $synced library games"
                }
            )
        }
    }

    // === 5. BIOS Auto-Scanner, Bezels, Controllers, Saves ZIP, Patcher & Hasher ===

    @JavascriptInterface
    fun pickBiosFolder() {
        mainHandler.post { activity.openBiosFolderPicker() }
    }

    @JavascriptInterface
    fun importBiosFile() {
        mainHandler.post { activity.openBiosFilePicker() }
    }

    @JavascriptInterface
    fun scanBiosFolder() {
        bgExecutor.execute {
            val res = MobileCheatAndBiosManager.scanAndImportBiosDirectory(appCtx, "")
            val imported = res.optInt("importedCount", 0)
            val ready = res.optInt("readyCount", 0)
            cachedBiosItems = null
            emitStormEvent("biosUpdated", JSONObject().put("bios", serializeBiosArray()))
            emitToast(
                if (StormLocalization.currentLanguage == "ru") {
                    "Проверка MD5 BIOS завершена: готово $ready (импортировано $imported)"
                } else {
                    "BIOS MD5 scan complete: $ready ready ($imported imported)"
                }
            )
        }
    }

    @JavascriptInterface
    fun downloadBezelPack(packId: String) {
        val mappedPack = when (packId.lowercase(Locale.ROOT)) {
            "hsm_mega_bezel" -> MobileCheatAndBiosManager.BEZEL_SHADER_PACKS[0]
            "duimon_mega", "duimon_mega_bezel" -> MobileCheatAndBiosManager.BEZEL_SHADER_PACKS[1]
            else -> MobileCheatAndBiosManager.BEZEL_SHADER_PACKS[2]
        }
        MobileCheatAndBiosManager.downloadBezelPackAsync(
            pack = mappedPack,
            onProgress = { pct, _ ->
                emitStormEvent("bezelProgress", JSONObject().put("packId", packId).put("percent", pct))
            },
            onDone = { ok, count, msg ->
                emitStormEvent("bezelProgress", JSONObject().put("packId", packId).put("percent", if (ok) 100 else 0))
                emitToast(if (ok) "${mappedPack.title}: $count OK" else msg)
            }
        )
    }

    @JavascriptInterface
    fun refreshControllersJson(): String {
        return serializeControllersArray().toString()
    }

    @JavascriptInterface
    fun saveControllerMappingJson(descriptor: String, mapJson: String): Boolean {
        return try {
            appCtx.getSharedPreferences(PREFS_STUDIO_UI, Context.MODE_PRIVATE)
                .edit()
                .putString("pad_map_$descriptor", mapJson)
                .apply()
            true
        } catch (_: Throwable) {
            false
        }
    }

    @JavascriptInterface
    fun exportSavesBackupZip() {
        bgExecutor.execute {
            val (ok, pathOrErr) = MobileCheatAndBiosManager.exportSavesAndStatesZip(appCtx)
            if (ok) {
                emitToast(
                    if (StormLocalization.currentLanguage == "ru") {
                        "Архив сохранений создан: $pathOrErr"
                    } else {
                        "Saves backup created: $pathOrErr"
                    }
                )
            } else {
                emitToast(pathOrErr)
            }
        }
    }

    @JavascriptInterface
    fun importSavesBackupZip() {
        mainHandler.post { activity.openSavesZipPicker() }
    }

    @JavascriptInterface
    fun deleteSaveSlot(gameId: String, slot: Int): Boolean {
        val rom = findRomByIdOrPath(gameId)
        val stem = rom?.rawNoIntroName ?: File(gameId).nameWithoutExtension.ifBlank { gameId }
        val statesDir = File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "states")
        val savesDir = File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "saves")
        var deleted = false
        if (slot >= 0) {
            val sf = File(statesDir, "$stem.state$slot")
            val pf = File(statesDir, "$stem.state$slot.png")
            if (sf.exists() && sf.delete()) deleted = true
            if (pf.exists()) pf.delete()
        } else {
            val srm = File(savesDir, "$stem.srm")
            if (srm.exists() && srm.delete()) deleted = true
        }
        return deleted
    }

    @JavascriptInterface
    fun applyRomPatchPicker(gameId: String) {
        pendingPatchGameId = gameId
        mainHandler.post { activity.openPatchFilePicker() }
    }

    @JavascriptInterface
    fun createM3uPlaylist(title: String, pathsJson: String): String {
        return try {
            val arr = JSONArray(pathsJson)
            val list = ArrayList<String>(arr.length())
            for (i in 0 until arr.length()) {
                val p = arr.optString(i, "").trim()
                if (p.isNotEmpty()) list.add(p)
            }
            val created = MobileCheatAndBiosManager.createM3uPlaylist(title, list)
            if (created != null && created.exists()) {
                val scanned = MobileLibraryAndCoverEngine.scanLibraryDirectories(appCtx)
                cachedLibrary.clear()
                cachedLibrary.addAll(scanned)
                emitStormEvent("libraryUpdated", JSONObject().put("games", serializeGamesArray(scanned)))
                created.absolutePath
            } else ""
        } catch (_: Throwable) {
            ""
        }
    }

    @JavascriptInterface
    fun computeRomHashJson(gameId: String): String {
        val rom = findRomByIdOrPath(gameId)
        val targetPath = rom?.filePath ?: gameId
        val res = MobileCheatAndBiosManager.computeRomHashes(appCtx, targetPath)
        if (!res.optBoolean("success", false)) {
            return JSONObject()
                .put("crc32", "--------")
                .put("md5", "File not found")
                .put("sha1", "-")
                .put("verified", "Select a local ROM file")
                .toString()
        }
        val sizeMb = String.format(Locale.US, "%.2f MB", res.optLong("sizeBytes", 0L) / 1048576.0)
        res.put("verified", "${res.optString("fileName", "ROM")} ($sizeMb)")
        return res.toString()
    }

    // === 6. Permissions & Auto-Update ===

    @JavascriptInterface
    fun requestStorageAccess() {
        mainHandler.post { activity.requestAllFilesStoragePermission() }
    }

    @JavascriptInterface
    fun checkForUpdates() {
        AutoUpdateHelper.checkForUpdateAsync("2.6.0") { info ->
            lastUpdateInfo = info
            emitStormEvent("updateStatus", serializeUpdateInfo(info))
            emitToast(info.statusMessage)
        }
    }

    @JavascriptInterface
    fun installUpdateApk() {
        val info = lastUpdateInfo
        if (!info.updateAvailable || info.apkDownloadUrl.isBlank()) {
            emitToast(
                if (StormLocalization.currentLanguage == "ru") {
                    "Установлена актуальная версия 2.6.0"
                } else {
                    "STORM RETROARCH 2.6.0 is up to date"
                }
            )
            return
        }
        emitToast("Downloading STORM RETROARCH ${info.latestVersion}...")
        AutoUpdateHelper.downloadUpdateApkAsync(
            context = appCtx,
            apkUrl = info.apkDownloadUrl,
            versionLabel = info.latestVersion,
            onProgress = { _, status -> emitToast(status) },
            onDone = { ok, apkFile, err ->
                if (ok && apkFile != null && apkFile.exists()) {
                    mainHandler.post { activity.launchApkPackageInstaller(apkFile) }
                } else {
                    emitToast(err)
                }
            }
        )
    }

    // === ActivityResult Handlers Invoked by MainActivity ===

    fun onRomFolderPicked(uri: Uri) {
        bgExecutor.execute {
            val realDir = MobileLibraryAndCoverEngine.resolveTreeOrDocumentUriToRealFile(uri)
            val storedToken = if (realDir != null && realDir.exists() && realDir.canRead()) {
                realDir.absolutePath
            } else {
                uri.toString()
            }
            MobileLibraryAndCoverEngine.addPersistedScanFolder(appCtx, storedToken)
            val scanned = MobileLibraryAndCoverEngine.scanLibraryDirectories(appCtx)
            cachedLibrary.clear()
            cachedLibrary.addAll(scanned)
            emitStormEvent("libraryUpdated", JSONObject().put("games", serializeGamesArray(scanned)))
            emitToast(
                if (StormLocalization.currentLanguage == "ru") {
                    "Найдено игр в библиотеке: ${scanned.size}"
                } else {
                    "Library updated: ${scanned.size} games"
                }
            )
            fetchMissingCoversInBackground(scanned)
        }
    }

    fun onSingleRomPicked(uri: Uri) {
        bgExecutor.execute {
            val imported = MobileLibraryAndCoverEngine.importSingleRomUri(appCtx, uri)
            if (imported != null) {
                val scanned = MobileLibraryAndCoverEngine.scanLibraryDirectories(appCtx)
                cachedLibrary.clear()
                cachedLibrary.addAll(scanned)
                emitStormEvent("libraryUpdated", JSONObject().put("games", serializeGamesArray(scanned)))
                emitToast(imported.name)
                fetchMissingCoversInBackground(listOf(imported))
            } else {
                emitToast("Failed to import selected ROM file")
            }
        }
    }

    fun onCoreFilePicked(uri: Uri) {
        bgExecutor.execute {
            val (core, pathOrErr) = MobileCoreRegistry.importCoreFromUri(appCtx, uri)
            if (core != null) {
                emitStormEvent(
                    "coreInstalled",
                    JSONObject()
                        .put("coreId", core.id)
                        .put("id", core.id)
                        .put("installed", true)
                        .put("path", pathOrErr)
                )
                emitStormEvent("bootstrapUpdated", JSONObject().put("cores", serializeCoresCatalogArray()))
            } else {
                emitStormEvent("coreError", JSONObject().put("message", pathOrErr))
            }
        }
    }

    fun onBiosFolderPicked(uri: Uri) {
        bgExecutor.execute {
            val summary = MobileCheatAndBiosManager.scanAndImportBiosDirectory(appCtx, uri.toString())
            val count = summary.optInt("importedCount", 0)
            cachedBiosItems = null
            emitStormEvent("biosUpdated", JSONObject().put("bios", serializeBiosArray()))
            emitToast(
                if (StormLocalization.currentLanguage == "ru") {
                    "Импортировано файлов BIOS: $count"
                } else {
                    "Imported $count BIOS firmware files"
                }
            )
        }
    }

    fun onBiosFilePicked(uri: Uri) {
        bgExecutor.execute {
            val ok = MobileCheatAndBiosManager.importBiosFile(appCtx, uri, null)
            cachedBiosItems = null
            emitStormEvent("biosUpdated", JSONObject().put("bios", serializeBiosArray()))
            emitToast(if (ok) "BIOS OK" else "BIOS import failed")
        }
    }

    fun onSavesZipPicked(uri: Uri) {
        bgExecutor.execute {
            val (ok, count) = MobileCheatAndBiosManager.importSavesAndStatesZip(appCtx, uri.toString())
            emitStormEvent("bootstrapUpdated", JSONObject().put("saves", serializeSaveSlotsOverview(cachedLibrary)))
            emitToast(
                if (ok) {
                    if (StormLocalization.currentLanguage == "ru") "Восстановлено сохранений: $count" else "Restored $count save files"
                } else {
                    "Saves ZIP import failed"
                }
            )
        }
    }

    fun onPatchFilePicked(uri: Uri) {
        bgExecutor.execute {
            val rom = findRomByIdOrPath(pendingPatchGameId) ?: cachedLibrary.firstOrNull()
            if (rom == null || !File(rom.filePath).exists()) {
                emitToast("Base ROM file not found")
                return@execute
            }
            val (outFile, msg) = MobileCheatAndBiosManager.applyRomPatch(appCtx, rom.filePath, uri.toString())
            if (outFile != null && outFile.exists()) {
                val scanned = MobileLibraryAndCoverEngine.scanLibraryDirectories(appCtx)
                cachedLibrary.clear()
                cachedLibrary.addAll(scanned)
                emitStormEvent("libraryUpdated", JSONObject().put("games", serializeGamesArray(scanned)))
                emitToast(outFile.name)
            } else {
                emitToast(msg)
            }
        }
    }

    fun onStoragePermissionReturned() {
        ensurePublicDirectoryStructure()
        val granted = hasAllFilesStorageAccess()
        emitStormEvent("storagePermission", JSONObject().put("granted", granted))
        if (granted) {
            bgExecutor.execute {
                MobileCheatAndBiosManager.unpackStarterCheatsIfNeeded(appCtx)
                val scanned = MobileLibraryAndCoverEngine.scanLibraryDirectories(appCtx)
                cachedLibrary.clear()
                cachedLibrary.addAll(scanned)
                cachedBiosItems = null
                startupWarmDone = true
                emitStormEvent("bootstrapUpdated", buildFullBootstrapJson(scanned))
                fetchMissingCoversInBackground(scanned)
            }
        }
    }

    // === JSON Serializers ===

    private fun serializeGamesArray(games: List<MobileRomItem>): JSONArray {
        val arr = JSONArray()
        for (g in games) {
            val coverUrl = g.coverUrl
            arr.put(
                JSONObject()
                    .put("id", g.id)
                    .put("title", g.name)
                    .put("name", g.name)
                    .put("subtitle", g.rawNoIntroName)
                    .put("rawNoIntroName", g.rawNoIntroName)
                    .put("platform", g.platform)
                    .put("platformKey", g.platformKey)
                    .put("region", g.regionDisplayBadge)
                    .put("regionCode", g.regionCode)
                    .put("isRussianMod", g.isRussianMod)
                    .put("coreId", g.assignedCoreId)
                    .put("assignedCoreId", g.assignedCoreId)
                    .put("favorite", g.isFavorite)
                    .put("isFavorite", g.isFavorite)
                    .put("lastPlayedMs", g.lastPlayedMs)
                    .put("sizeBytes", g.sizeBytes)
                    .put("sizeMb", g.sizeFormatted)
                    .put("filePath", g.filePath)
                    .put("extension", g.extension)
                    .put("cover", coverUrl)
                    .put("coverUrl", coverUrl)
                    .put("coverPath", g.coverPath ?: "")
                    .put("completionStatus", g.completionStatus)
                    .put("starRating", g.starRating)
                    .put("userNotes", g.userNotes)
                    .put("customCollection", g.customCollection)
                    .put("totalPlaySeconds", g.totalPlaySeconds)
                    .put("playtimeMin", (g.totalPlaySeconds / 60L).toInt())
                    .put("launchCount", g.launchCount)
                    .put("lastPlayedIso", g.lastPlayedIso)
            )
        }
        return arr
    }

    private fun serializeCoresCatalogArray(): JSONArray {
        val arr = JSONArray()
        for (c in MobileCoreRegistry.getAllCores(appCtx)) {
            val bundled = MobileCoreRegistry.isCoreBundled(appCtx, c)
            val installed = bundled || MobileCoreRegistry.isCoreInstalled(appCtx, c)
            val keysArr = JSONArray()
            c.platformKeys.forEach { keysArr.put(it) }
            val extArr = JSONArray()
            c.extensions.forEach { extArr.put(it) }
            arr.put(
                JSONObject()
                    .put("id", c.id)
                    .put("displayName", c.displayName)
                    .put("platformId", c.platformId)
                    .put("platformKey", MobileCoreRegistry.normalizePlatformKey(c.platformId, "").ifBlank { c.platformId })
                    .put("platform", c.platformDisplay)
                    .put("platformDisplay", c.platformDisplay)
                    .put("platformKeys", keysArr)
                    .put("extensions", extArr)
                    .put("bundled", bundled)
                    .put("installed", installed)
                    .put("recommended", c.recommended)
                    .put("jniLibName", c.jniLibName)
                    .put("soInsideZip", c.soInsideZip)
                    .put("sizeFormatted", if (installed) MobileCoreRegistry.getInstalledCoreSizeFormatted(appCtx, c) else "")
                    .put("progress", 0)
            )
        }
        return arr
    }

    private fun serializeBiosArray(): JSONArray {
        val scanned = cachedBiosItems ?: run {
            val sysDir = MobileCheatAndBiosManager.getEffectiveSystemDir(appCtx)
            MobileCheatAndBiosManager.scanBiosCatalog(sysDir).also { cachedBiosItems = it }
        }
        val arr = JSONArray()
        for (b in scanned) {
            val st = when {
                b.isPresent && b.md5Valid -> "verified"
                b.isPresent -> "present"
                else -> "missing"
            }
            arr.put(
                JSONObject()
                    .put("id", b.id)
                    .put("fileName", b.fileName)
                    .put("platform", b.platform)
                    .put("relativePath", "system/${b.relativePath}")
                    .put("expectedMd5", b.expectedMd5)
                    .put("actualMd5", b.actualMd5)
                    .put("status", st)
            )
        }
        return arr
    }

    fun serializeSaveSlotsOverview(games: List<MobileRomItem>): JSONArray {
        val arr = JSONArray()
        val statesDir = File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "states")
        val savesDir = File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "saves")
        val dateFmt = SimpleDateFormat("dd.MM HH:mm", Locale.US)

        val romByStem = games.associateBy { it.rawNoIntroName }

        if (statesDir.exists() && statesDir.isDirectory) {
            val stateFiles = statesDir.listFiles { f ->
                f.isFile && Regex(".*\\.state\\d+$").matches(f.name)
            }?.sortedByDescending { it.lastModified() } ?: emptyList()

            for (sf in stateFiles) {
                val slotNum = sf.name.substringAfterLast(".state").toIntOrNull() ?: 0
                val stem = sf.name.substringBeforeLast(".state")
                val matchedRom = romByStem[stem]
                val previewPng = File(statesDir, "${sf.name}.png")
                val screenshotUrl = if (previewPng.exists()) {
                    MobileLibraryAndCoverEngine.toStormLocalUrl(previewPng.absolutePath)
                } else ""
                val hasSrm = File(savesDir, "$stem.srm").exists()

                arr.put(
                    JSONObject()
                        .put("gameId", matchedRom?.id ?: stem)
                        .put("gameTitle", matchedRom?.name ?: MobileLibraryAndCoverEngine.cleanDisplayTitle(stem))
                        .put("platform", matchedRom?.platform ?: "Libretro")
                        .put("coreId", matchedRom?.assignedCoreId ?: "")
                        .put("slot", slotNum)
                        .put("type", if (hasSrm) "State #$slotNum + .SRM" else "State #$slotNum")
                        .put("date", dateFmt.format(Date(sf.lastModified())))
                        .put("size", String.format(Locale.US, "%d KB", maxOf(1L, sf.length() / 1024L)))
                        .put("screenshotUrl", screenshotUrl)
                )
            }
        }

        if (savesDir.exists() && savesDir.isDirectory) {
            val srmFiles = savesDir.listFiles { f ->
                f.isFile && f.extension.equals("srm", ignoreCase = true) && f.length() > 0L
            }?.sortedByDescending { it.lastModified() } ?: emptyList()

            for (srm in srmFiles) {
                val stem = srm.nameWithoutExtension
                val alreadyListed = (0 until arr.length()).any { idx ->
                    val o = arr.optJSONObject(idx)
                    o != null && (o.optString("gameId") == stem || o.optString("gameId") == romByStem[stem]?.id)
                }
                if (alreadyListed) continue
                val matchedRom = romByStem[stem]
                arr.put(
                    JSONObject()
                        .put("gameId", matchedRom?.id ?: stem)
                        .put("gameTitle", matchedRom?.name ?: MobileLibraryAndCoverEngine.cleanDisplayTitle(stem))
                        .put("platform", matchedRom?.platform ?: "SRAM")
                        .put("coreId", matchedRom?.assignedCoreId ?: "")
                        .put("slot", -1)
                        .put("type", "Battery .SRM")
                        .put("date", dateFmt.format(Date(srm.lastModified())))
                        .put("size", String.format(Locale.US, "%d KB", maxOf(1L, srm.length() / 1024L)))
                        .put("screenshotUrl", "")
                )
            }
        }
        return arr
    }

    fun serializeControllersArray(): JSONArray {
        val arr = JSONArray()
        val list = MobileControllerProfileManager.enumerateConnectedControllers(appCtx)
        for ((idx, pad) in list.withIndex()) {
            arr.put(
                JSONObject()
                    .put("descriptor", pad.profileKey)
                    .put("name", pad.name)
                    .put("port", idx + 1)
                    .put("type", pad.controllerType)
            )
        }
        return arr
    }

    private fun serializeUpdateInfo(info: MobileUpdateInfo): JSONObject {
        return JSONObject()
            .put("updateAvailable", info.updateAvailable)
            .put("currentVersion", info.currentVersion)
            .put("latestVersion", info.latestVersion)
            .put("apkDownloadUrl", info.apkDownloadUrl)
            .put("releaseNotes", info.releaseNotes)
            .put("releasePageUrl", info.releasePageUrl)
            .put("statusMessage", info.statusMessage)
    }
}
