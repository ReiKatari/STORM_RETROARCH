package com.storm.retroarch

import android.Manifest
import android.app.Activity
import android.app.Dialog
import android.content.Context
import android.content.Intent
import android.content.SharedPreferences
import android.content.pm.PackageManager
import android.content.res.Configuration
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Color
import android.graphics.drawable.ColorDrawable
import android.graphics.drawable.GradientDrawable
import android.hardware.input.InputManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Environment
import android.os.Handler
import android.os.Looper
import android.provider.DocumentsContract
import android.provider.OpenableColumns
import android.provider.Settings
import android.text.Editable
import android.text.TextUtils
import android.text.TextWatcher
import android.util.LruCache
import android.util.TypedValue
import android.view.Gravity
import android.view.KeyEvent
import android.view.View
import android.view.ViewGroup
import android.view.Window
import android.widget.CheckBox
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.GridLayout
import android.widget.HorizontalScrollView
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ProgressBar
import android.widget.ScrollView
import android.widget.TextView
import android.widget.Toast
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream
import java.util.Locale
import java.util.concurrent.Executors
import kotlin.math.max
import kotlin.math.roundToInt

class MainActivity : Activity() {

    companion object {
        const val APP_VERSION = "2.4.0"

        private const val REQ_MANAGE_STORAGE = 4101
        private const val REQ_RUNTIME_PERMS = 4102
        private const val REQ_PICK_ROM_FOLDER = 4103
        private const val REQ_PICK_ROM_FILE = 4104
        private const val REQ_PICK_BIOS_FILE = 4105

        private const val PREFS_MAIN = "storm_retroarch_main_prefs"
        private const val PREFS_EMU = "storm_retroarch_emu_prefs"

        private const val TAB_LIBRARY = 0
        private const val TAB_CORES = 1
        private const val TAB_CHEATS = 2
        private const val TAB_BIOS_BEZELS = 3
        private const val TAB_SETTINGS = 4
    }

    private val stormRoot = File(MobileCoreRegistry.STORM_PUBLIC_ROOT)
    private val settingsJsonFile = File(stormRoot, "settings/mobile_ui_settings.json")
    private val mainHandler = Handler(Looper.getMainLooper())
    private val bgExecutor = Executors.newFixedThreadPool(2)

    private lateinit var prefs: SharedPreferences
    private lateinit var romMetaPrefs: SharedPreferences

    // Adaptive shell views
    private lateinit var outerContainer: FrameLayout
    private var contentFrame: FrameLayout? = null
    private var headerSearchInput: EditText? = null

    // Persistent UI state
    private var activeTab: Int = TAB_LIBRARY
    private var uiScale: Float = 1.0f
    private var searchQuery: String = ""

    // Library state
    private val allRoms = ArrayList<MobileRomItem>()
    private val customRomDirs = LinkedHashSet<String>()
    private val platformDefaultCores = HashMap<String, String>()
    private var activeCollection: String = MobileLibraryAndCoverEngine.COLLECTION_ALL
    private var activePlatformKey: String = ""
    private var activeCoreFilterId: String = "ALL"
    private var libraryVisibleLimit: Int = 60
    @Volatile private var isScanningLibrary = false

    // Cores tab state
    private var coresPlatformFilterKey: String = ""
    private var coresOnlyInstalled: Boolean = false

    // Cheats tab state
    private var cheatsLibraryOnly: Boolean = true
    private var cheatsPlatformFilterKey: String = ""
    private var cheatsPage: Int = 0
    private var cachedAllCheats: List<MobileCheatProfile> = emptyList()
    private var cachedLibCheats: List<MobileCheatProfile> = emptyList()

    // BIOS import pending target
    private var pendingBiosTarget: MobileBiosItem? = null

    // Connected gamepads
    private var controllerListener: InputManager.InputDeviceListener? = null
    private var connectedControllers: List<MobileControllerDevice> = emptyList()

    // Thumbnail memory cache (max 24 MB)
    private val coverBitmapCache = object : LruCache<String, Bitmap>(24 * 1024) {
        override fun sizeOf(key: String, value: Bitmap): Int = (value.byteCount / 1024).coerceAtLeast(1)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        requestWindowFeature(Window.FEATURE_NO_TITLE)
        super.onCreate(savedInstanceState)

        prefs = getSharedPreferences(PREFS_MAIN, Context.MODE_PRIVATE)
        romMetaPrefs = MobileLibraryAndCoverEngine.getRomMetadataPrefs(this)

        StormThemeEngine.loadFromPrefs(this)
        StormLocalization.loadFromPrefs(this)
        loadPersistedAppState()

        initializeStormDirectoryTree()
        requestFirstRunPermissionsIfNeeded()

        bgExecutor.execute {
            MobileCoreRegistry.syncPublicCoresToExecutableDir(this)
        }

        outerContainer = FrameLayout(this)
        setContentView(outerContainer)

        connectedControllers = MobileControllerProfileManager.enumerateConnectedControllers(this)
        controllerListener = MobileControllerProfileManager.registerControllerListener(this) { list ->
            connectedControllers = list
            if (activeTab == TAB_SETTINGS) {
                renderActiveTabContent()
            }
        }

        rebuildAdaptiveShell()
        triggerLibraryScan(autoFetchMissingCovers = false)
    }

    override fun onConfigurationChanged(newConfig: Configuration) {
        super.onConfigurationChanged(newConfig)
        rebuildAdaptiveShell()
    }

    override fun onResume() {
        super.onResume()
        initializeStormDirectoryTree()
        if (allRoms.isEmpty() && !isScanningLibrary) {
            triggerLibraryScan(autoFetchMissingCovers = false)
        }
    }

    override fun onDestroy() {
        controllerListener?.let { MobileControllerProfileManager.unregisterControllerListener(this, it) }
        super.onDestroy()
    }

    // === Directory Tree & Permissions ===

    private fun initializeStormDirectoryTree() {
        val dirs = listOf(
            "roms",
            "cores",
            "saves",
            "states",
            "system",
            "system/dc",
            "system/pcsx2/bios",
            "system/dolphin-emu/Sys",
            "system/PPSSPP",
            "system/citra",
            "cheats",
            "shaders",
            "bezels",
            "thumbnails",
            "screenshots",
            "autoconfig",
            "settings",
            "updates",
            "patches"
        )
        for (rel in dirs) {
            try {
                val f = File(stormRoot, rel)
                if (!f.exists()) f.mkdirs()
            } catch (_: Throwable) {
            }
        }
    }

    private fun hasAllFilesAccess(): Boolean {
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            Environment.isExternalStorageManager()
        } else if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            checkSelfPermission(Manifest.permission.WRITE_EXTERNAL_STORAGE) == PackageManager.PERMISSION_GRANTED
        } else {
            true
        }
    }

    private fun requestFirstRunPermissionsIfNeeded() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            val askedOnce = prefs.getBoolean("asked_manage_storage", false)
            if (!Environment.isExternalStorageManager() && !askedOnce) {
                prefs.edit().putBoolean("asked_manage_storage", true).apply()
                openManageAllFilesSettings()
            }
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            val needed = ArrayList<String>()
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                for (p in listOf(
                    Manifest.permission.POST_NOTIFICATIONS,
                    Manifest.permission.READ_MEDIA_IMAGES,
                    Manifest.permission.READ_MEDIA_AUDIO,
                    Manifest.permission.READ_MEDIA_VIDEO
                )) {
                    if (checkSelfPermission(p) != PackageManager.PERMISSION_GRANTED) needed.add(p)
                }
            } else if (Build.VERSION.SDK_INT <= 32) {
                for (p in listOf(
                    Manifest.permission.READ_EXTERNAL_STORAGE,
                    Manifest.permission.WRITE_EXTERNAL_STORAGE
                )) {
                    if (checkSelfPermission(p) != PackageManager.PERMISSION_GRANTED) needed.add(p)
                }
            }
            if (needed.isNotEmpty()) {
                requestPermissions(needed.toTypedArray(), REQ_RUNTIME_PERMS)
            }
        }
    }

    private fun openManageAllFilesSettings() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            try {
                val intent = Intent(
                    Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION,
                    Uri.parse("package:$packageName")
                )
                startActivityForResult(intent, REQ_MANAGE_STORAGE)
            } catch (_: Throwable) {
                try {
                    startActivityForResult(Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION), REQ_MANAGE_STORAGE)
                } catch (_: Throwable) {
                }
            }
        }
    }

    @Deprecated("Handled via Activity level lifecycle")
    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        initializeStormDirectoryTree()
        triggerLibraryScan(autoFetchMissingCovers = false)
    }

    // === Persistent UI Settings ===

    private fun loadPersistedAppState() {
        uiScale = prefs.getFloat("ui_scale", 1.0f).coerceIn(0.85f, 1.30f)
        customRomDirs.clear()
        prefs.getStringSet("custom_rom_dirs", emptySet())?.let { customRomDirs.addAll(it) }

        platformDefaultCores.clear()
        val defCoreJson = prefs.getString("platform_default_cores", null)
        if (!defCoreJson.isNullOrBlank()) {
            try {
                val obj = JSONObject(defCoreJson)
                val keys = obj.keys()
                while (keys.hasNext()) {
                    val k = keys.next()
                    platformDefaultCores[k] = obj.optString(k, "")
                }
            } catch (_: Throwable) {
            }
        }

        if (settingsJsonFile.exists() && settingsJsonFile.length() > 4L) {
            try {
                val root = JSONObject(settingsJsonFile.readText(Charsets.UTF_8))
                uiScale = root.optDouble("uiScale", uiScale.toDouble()).toFloat().coerceIn(0.85f, 1.30f)
                root.optJSONArray("customRomDirs")?.let { arr ->
                    for (i in 0 until arr.length()) {
                        val p = arr.optString(i, "")
                        if (p.isNotBlank()) customRomDirs.add(p)
                    }
                }
            } catch (_: Throwable) {
            }
        }
    }

    private fun savePersistedAppState() {
        val defCoresObj = JSONObject()
        for ((k, v) in platformDefaultCores) {
            defCoresObj.put(k, v)
        }
        prefs.edit()
            .putFloat("ui_scale", uiScale)
            .putStringSet("custom_rom_dirs", HashSet(customRomDirs))
            .putString("platform_default_cores", defCoresObj.toString())
            .apply()

        try {
            settingsJsonFile.parentFile?.mkdirs()
            val root = JSONObject()
            root.put("version", APP_VERSION)
            root.put("themeId", StormThemeEngine.currentTheme.id)
            root.put("accentId", StormThemeEngine.currentAccent.id)
            root.put("fontId", StormThemeEngine.currentFont.id)
            root.put("language", StormLocalization.currentLanguage)
            root.put("uiScale", uiScale.toDouble())
            val arr = JSONArray()
            customRomDirs.forEach { arr.put(it) }
            root.put("customRomDirs", arr)
            root.put("platformDefaultCores", defCoresObj)
            settingsJsonFile.writeText(root.toString(2), Charsets.UTF_8)
        } catch (_: Throwable) {
        }
    }

    // === Adaptive Shell: Portrait (Bottom Nav) vs Landscape/Tablet (Left Rail) ===

    private fun isWideOrLandscapeMode(): Boolean {
        val dm = resources.displayMetrics
        val widthDp = dm.widthPixels / dm.density
        return dm.widthPixels > dm.heightPixels || widthDp >= 640f
    }

    private fun rebuildAdaptiveShell() {
        val pal = StormThemeEngine.currentTheme
        val accent = StormThemeEngine.activeAccentColor

        window.statusBarColor = pal.bgSurface
        window.navigationBarColor = pal.bgSurface

        outerContainer.removeAllViews()
        outerContainer.setBackgroundColor(pal.bgMain)

        val wide = isWideOrLandscapeMode()

        if (wide) {
            // Landscape / Tablet / Foldable / Handheld: Left Compact Navigation Rail + Right Main Column
            val row = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                layoutParams = FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
            }
            row.addView(buildLeftNavigationRail(pal, accent))

            val rightCol = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f)
            }
            rightCol.addView(buildTopHeaderBar(pal, accent, compact = true))
            val cf = FrameLayout(this).apply {
                layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f)
            }
            contentFrame = cf
            rightCol.addView(cf)
            row.addView(rightCol)
            outerContainer.addView(row)
        } else {
            // Portrait Smartphone: Top Header + Content + Thumb-Friendly Bottom Navigation Bar
            val col = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                layoutParams = FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
            }
            col.addView(buildTopHeaderBar(pal, accent, compact = false))
            val cf = FrameLayout(this).apply {
                layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f)
            }
            contentFrame = cf
            col.addView(cf)
            col.addView(buildBottomNavigationBar(pal, accent))
            outerContainer.addView(col)
        }

        renderActiveTabContent()
    }

    private data class NavTabSpec(val index: Int, val icon: String, val labelKey: String, val fallbackRu: String)

    private fun navTabs(): List<NavTabSpec> = listOf(
        NavTabSpec(TAB_LIBRARY, StormVectorIcons.LIBRARY, "tab_library", "Библиотека"),
        NavTabSpec(TAB_CORES, StormVectorIcons.CORES, "tab_cores", "Ядра"),
        NavTabSpec(TAB_CHEATS, StormVectorIcons.CHEATS, "tab_cheats", "Читы"),
        NavTabSpec(TAB_BIOS_BEZELS, StormVectorIcons.SHIELD, "tab_bios", "BIOS и Рамки"),
        NavTabSpec(TAB_SETTINGS, StormVectorIcons.SETTINGS, "tab_settings", "Настройки")
    )

    private fun buildTopHeaderBar(pal: StormThemePalette, accent: Int, compact: Boolean): View {
        val container = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = GradientDrawable(
                GradientDrawable.Orientation.TOP_BOTTOM,
                intArrayOf(StormThemeEngine.lighten(pal.bgSurface, 0.04f), pal.bgSurface)
            ).apply {
                setStroke(dp(1f), pal.border)
            }
            setPadding(dp(12f), dp(if (compact) 8f else 10f), dp(12f), dp(8f))
        }

        val topRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }

        // Brand title: STORM RETROARCH 2.4.0 (ALL CAPS, version without 'v')
        val brandRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        }
        val logoIcon = ImageView(this).apply {
            setImageDrawable(StormVectorIcons.create(this@MainActivity, StormVectorIcons.GAMEPAD, accent, 20f))
        }
        brandRow.addView(logoIcon, LinearLayout.LayoutParams(dp(20f), dp(20f)).apply { rightMargin = dp(8f) })

        val titleTv = TextView(this).apply {
            text = "STORM RETROARCH $APP_VERSION"
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(14.5f))
            maxLines = 1
            ellipsize = TextUtils.TruncateAt.END
            StormThemeEngine.applyFont(this, bold = true)
        }
        brandRow.addView(titleTv)
        topRow.addView(brandRow)

        // Quick Theme Switcher Pill
        val themePill = createHeaderQuickPill(StormThemeEngine.currentTheme.name) {
            val idx = StormThemeEngine.THEMES.indexOfFirst { it.id == StormThemeEngine.currentTheme.id }
            val next = StormThemeEngine.THEMES[(idx + 1) % StormThemeEngine.THEMES.size]
            StormThemeEngine.setTheme(next.id, this, syncDefaultAccent = false)
            savePersistedAppState()
            rebuildAdaptiveShell()
        }
        topRow.addView(themePill, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(30f)).apply { rightMargin = dp(6f) })

        // Quick Language Switcher Pill
        val langPill = createHeaderQuickPill(StormLocalization.currentLanguage.uppercase(Locale.ROOT)) {
            showQuickLanguageDialog()
        }
        topRow.addView(langPill, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(30f)))
        container.addView(topRow)

        // Search bar shown on searchable tabs (Library, Cores, Cheats)
        if (activeTab in listOf(TAB_LIBRARY, TAB_CORES, TAB_CHEATS)) {
            val searchBox = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                background = StormThemeEngine.createRecessedInputDrawable(this@MainActivity, cornerDp = 12f)
                setPadding(dp(10f), dp(2f), dp(8f), dp(2f))
                layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(38f)).apply {
                    topMargin = dp(7f)
                }
            }
            val sIcon = ImageView(this).apply {
                setImageDrawable(StormVectorIcons.create(this@MainActivity, StormVectorIcons.SEARCH, pal.textSecondary, 16f))
            }
            searchBox.addView(sIcon, LinearLayout.LayoutParams(dp(16f), dp(16f)).apply { rightMargin = dp(8f) })

            val edit = EditText(this).apply {
                setText(searchQuery)
                hint = if (activeTab == TAB_CHEATS) StormLocalization.tr("cheats_search_hint") else StormLocalization.tr("search_hint")
                setHintTextColor(pal.textSecondary)
                setTextColor(pal.textPrimary)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(12.5f))
                background = null
                isSingleLine = true
                StormThemeEngine.applyFont(this, bold = false)
                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f)
                addTextChangedListener(object : TextWatcher {
                    override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
                    override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
                    override fun afterTextChanged(s: Editable?) {
                        val nextQ = s?.toString() ?: ""
                        if (searchQuery != nextQ) {
                            searchQuery = nextQ
                            cheatsPage = 0
                            libraryVisibleLimit = 60
                            renderActiveTabContent()
                        }
                    }
                })
            }
            headerSearchInput = edit
            searchBox.addView(edit)

            if (searchQuery.isNotEmpty()) {
                val clearIv = ImageView(this).apply {
                    setImageDrawable(StormVectorIcons.create(this@MainActivity, StormVectorIcons.CLOSE, pal.textSecondary, 16f))
                    setOnClickListener {
                        searchQuery = ""
                        edit.setText("")
                        renderActiveTabContent()
                    }
                }
                searchBox.addView(clearIv, LinearLayout.LayoutParams(dp(22f), dp(22f)))
            }
            container.addView(searchBox)
        }

        return container
    }

    private fun createHeaderQuickPill(label: String, onClick: () -> Unit): TextView {
        val pal = StormThemeEngine.currentTheme
        return TextView(this).apply {
            text = label
            gravity = Gravity.CENTER
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(11f))
            setPadding(dp(10f), 0, dp(10f), 0)
            StormThemeEngine.applyFont(this, bold = true)
            background = StormThemeEngine.createSurfaceButtonDrawable(this@MainActivity, 10f)
            StormThemeEngine.attachSpringTouchFeedback(this)
            setOnClickListener { onClick() }
        }
    }

    private fun buildBottomNavigationBar(pal: StormThemePalette, accent: Int): View {
        val bar = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            background = GradientDrawable(
                GradientDrawable.Orientation.TOP_BOTTOM,
                intArrayOf(pal.bgSurface, StormThemeEngine.darken(pal.bgSurface, 0.08f))
            ).apply {
                setStroke(dp(1f), pal.border)
            }
            setPadding(dp(4f), dp(6f), dp(4f), dp(6f))
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(62f))
        }

        for (tab in navTabs()) {
            val selected = activeTab == tab.index
            val itemColor = if (selected) accent else pal.textSecondary
            val cell = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                gravity = Gravity.CENTER
                isFocusable = true
                isClickable = true
                background = if (selected) {
                    GradientDrawable().apply {
                        shape = GradientDrawable.RECTANGLE
                        cornerRadius = dpF(12f)
                        setColor(StormThemeEngine.withAlpha(accent, 34))
                    }
                } else null
                setOnClickListener { switchTab(tab.index) }
                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f)
            }
            val iv = ImageView(this).apply {
                setImageDrawable(StormVectorIcons.create(this@MainActivity, tab.icon, itemColor, 20f))
            }
            val tv = TextView(this).apply {
                text = StormLocalization.tr(tab.labelKey).ifBlank { tab.fallbackRu }
                setTextColor(if (selected) pal.textPrimary else pal.textSecondary)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(10f))
                maxLines = 1
                ellipsize = TextUtils.TruncateAt.END
                StormThemeEngine.applyFont(this, bold = selected)
            }
            cell.addView(iv, LinearLayout.LayoutParams(dp(20f), dp(20f)))
            cell.addView(tv, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(2f) })
            bar.addView(cell)
        }
        return bar
    }

    private fun buildLeftNavigationRail(pal: StormThemePalette, accent: Int): View {
        val rail = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.TOP or Gravity.CENTER_HORIZONTAL
            background = GradientDrawable(
                GradientDrawable.Orientation.LEFT_RIGHT,
                intArrayOf(pal.bgSurface, StormThemeEngine.darken(pal.bgSurface, 0.05f))
            ).apply {
                setStroke(dp(1f), pal.border)
            }
            setPadding(dp(6f), dp(10f), dp(6f), dp(10f))
            layoutParams = LinearLayout.LayoutParams(dp(92f), ViewGroup.LayoutParams.MATCH_PARENT)
        }

        for (tab in navTabs()) {
            val selected = activeTab == tab.index
            val itemColor = if (selected) accent else pal.textSecondary
            val cell = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                gravity = Gravity.CENTER
                isFocusable = true
                isClickable = true
                setPadding(dp(4f), dp(8f), dp(4f), dp(8f))
                background = if (selected) {
                    StormThemeEngine.createFilterChipDrawable(this@MainActivity, true, accent, 14f)
                } else null
                setOnClickListener { switchTab(tab.index) }
                layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                    bottomMargin = dp(6f)
                }
            }
            val fg = if (selected) StormThemeEngine.contrastTextForFill(accent) else itemColor
            val iv = ImageView(this).apply {
                setImageDrawable(StormVectorIcons.create(this@MainActivity, tab.icon, fg, 20f))
            }
            val tv = TextView(this).apply {
                text = StormLocalization.tr(tab.labelKey).ifBlank { tab.fallbackRu }
                setTextColor(if (selected) fg else pal.textSecondary)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(10f))
                gravity = Gravity.CENTER
                maxLines = 2
                StormThemeEngine.applyFont(this, bold = selected)
            }
            cell.addView(iv, LinearLayout.LayoutParams(dp(20f), dp(20f)))
            cell.addView(tv, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(3f) })
            rail.addView(cell)
        }
        return rail
    }

    private fun switchTab(newTab: Int) {
        if (activeTab == newTab) return
        activeTab = newTab.coerceIn(0, 4)
        rebuildAdaptiveShell()
    }

    override fun onKeyDown(keyCode: Int, event: KeyEvent): Boolean {
        // Physical Gamepad L1 / R1 Tab Switching in Launcher
        if (event.repeatCount == 0) {
            if (keyCode == KeyEvent.KEYCODE_BUTTON_L1) {
                switchTab((activeTab + 4) % 5)
                return true
            }
            if (keyCode == KeyEvent.KEYCODE_BUTTON_R1) {
                switchTab((activeTab + 1) % 5)
                return true
            }
        }
        return super.onKeyDown(keyCode, event)
    }

    private fun renderActiveTabContent() {
        val host = contentFrame ?: return
        host.removeAllViews()
        val view = when (activeTab) {
            TAB_LIBRARY -> buildLibraryTabView()
            TAB_CORES -> buildCoresTabView()
            TAB_CHEATS -> buildCheatsTabView()
            TAB_BIOS_BEZELS -> buildBiosAndBezelsTabView()
            else -> buildSettingsTabView()
        }
        host.addView(view, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
    }

    // =========================================================================
    // TAB 1: БИБЛИОТЕКА (Smart Collections, Strict Platform Cores, Regional Boxarts)
    // =========================================================================

    private fun triggerLibraryScan(autoFetchMissingCovers: Boolean) {
        if (isScanningLibrary) return
        isScanningLibrary = true
        bgExecutor.execute {
            val scanDirs = LinkedHashSet<File>()
            scanDirs.addAll(MobileLibraryAndCoverEngine.getDefaultRomDirectories())
            for (path in customRomDirs) {
                val f = File(path)
                if (f.exists() && f.isDirectory) scanDirs.add(f)
            }

            val scanned = MobileLibraryAndCoverEngine.scanDirectories(scanDirs.toList(), romMetaPrefs)
            // Apply per-platform default core overrides when user hasn't explicitly pinned a custom ROM core
            for (rom in scanned) {
                val keyHash = rom.filePath.hashCode().toUInt().toString(16)
                val explicitCore = romMetaPrefs.getString("core_$keyHash", null)
                if (explicitCore.isNullOrBlank()) {
                    platformDefaultCores[rom.platformKey]?.takeIf { it.isNotBlank() }?.let { defId ->
                        rom.assignedCoreId = defId
                    }
                }
            }

            mainHandler.post {
                allRoms.clear()
                allRoms.addAll(scanned)
                cachedLibCheats = emptyList()
                isScanningLibrary = false
                if (activeTab == TAB_LIBRARY || activeTab == TAB_CHEATS) {
                    renderActiveTabContent()
                }
                if (autoFetchMissingCovers) {
                    batchFetchMissingCovers()
                }
            }
        }
    }

    private fun batchFetchMissingCovers() {
        val missing = allRoms.filter { it.coverPath.isNullOrBlank() || !File(it.coverPath!!).exists() }
        if (missing.isEmpty()) {
            Toast.makeText(this, "Все обложки уже загружены (${allRoms.size})", Toast.LENGTH_SHORT).show()
            return
        }
        Toast.makeText(this, "${StormLocalization.tr("refresh_covers")}: ${missing.size}...", Toast.LENGTH_SHORT).show()
        var resolvedCount = 0
        for (rom in missing) {
            MobileLibraryAndCoverEngine.fetchCoverArtAsync(this, rom, romMetaPrefs) { _, file ->
                if (file != null) {
                    resolvedCount++
                    if (activeTab == TAB_LIBRARY && (resolvedCount % 4 == 0 || resolvedCount == missing.size)) {
                        renderActiveTabContent()
                    }
                }
            }
        }
    }

    private fun buildLibraryTabView(): View {
        val pal = StormThemeEngine.currentTheme
        val accent = StormThemeEngine.activeAccentColor

        val scroll = ScrollView(this).apply {
            isVerticalScrollBarEnabled = true
        }
        val col = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(12f), dp(10f), dp(12f), dp(20f))
        }

        // Storage permission banner if needed on Android 11+
        if (!hasAllFilesAccess()) {
            val permCard = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                background = StormThemeEngine.createVolumetricCardDrawable(this@MainActivity, borderColor = accent, cornerDp = 14f)
                setPadding(dp(12f), dp(10f), dp(12f), dp(10f))
            }
            val permText = TextView(this).apply {
                text = "Разрешите полный доступ к памяти для чтения /storage/emulated/0/STORM RETROARCH"
                setTextColor(pal.textPrimary)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(11.5f))
                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply { rightMargin = dp(8f) }
            }
            val permBtn = createActionBtn(StormVectorIcons.SHIELD, "Разрешить", accentFilled = true) {
                openManageAllFilesSettings()
            }
            permCard.addView(permText)
            permCard.addView(permBtn)
            col.addView(permCard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(10f) })
        }

        // 1. Action Row: + Папка с играми | + Файл ROM | Обложки | Очистить
        val actionScroll = HorizontalScrollView(this).apply { isHorizontalScrollBarEnabled = false }
        val actionRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }

        actionRow.addView(
            createActionBtn(StormVectorIcons.FOLDER, "+ Папка с играми", accentFilled = true) {
                showAddFolderChooserDialog()
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(38f)).apply { rightMargin = dp(7f) }
        )
        actionRow.addView(
            createActionBtn(StormVectorIcons.PLAY, "+ Файл ROM", accentFilled = false) {
                launchSafFilePicker(REQ_PICK_ROM_FILE)
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(38f)).apply { rightMargin = dp(7f) }
        )
        actionRow.addView(
            createActionBtn(StormVectorIcons.CAMERA, "Обложки", accentFilled = false) {
                batchFetchMissingCovers()
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(38f)).apply { rightMargin = dp(7f) }
        )
        actionRow.addView(
            createActionBtn(StormVectorIcons.REFRESH, StormLocalization.tr("scan_folders"), accentFilled = false) {
                triggerLibraryScan(autoFetchMissingCovers = false)
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(38f)).apply { rightMargin = dp(7f) }
        )
        actionRow.addView(
            createActionBtn(StormVectorIcons.CLOSE, "Очистить", accentFilled = false) {
                activeCollection = MobileLibraryAndCoverEngine.COLLECTION_ALL
                activePlatformKey = ""
                activeCoreFilterId = "ALL"
                searchQuery = ""
                headerSearchInput?.setText("")
                if (customRomDirs.isNotEmpty()) {
                    customRomDirs.clear()
                    savePersistedAppState()
                    triggerLibraryScan(autoFetchMissingCovers = false)
                } else {
                    renderActiveTabContent()
                }
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(38f))
        )
        actionScroll.addView(actionRow)
        col.addView(actionScroll, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(10f) })

        val counts = MobileLibraryAndCoverEngine.computeFilterCounts(allRoms, activeCollection, activePlatformKey)

        // 2. Smart Collection Pills with live (N) counts
        val smartSpecs = listOf(
            MobileLibraryAndCoverEngine.COLLECTION_ALL to "Все игры",
            MobileLibraryAndCoverEngine.COLLECTION_FAVORITES to "Избранное",
            MobileLibraryAndCoverEngine.COLLECTION_RUS_MODS to "Русские переводы",
            MobileLibraryAndCoverEngine.COLLECTION_CLASSIC_8_16 to "Классика 8/16-бит",
            MobileLibraryAndCoverEngine.COLLECTION_CD_3D to "3D и CD",
            MobileLibraryAndCoverEngine.COLLECTION_DOS_PC to "MS-DOS",
            MobileLibraryAndCoverEngine.COLLECTION_RECENT to "Недавние"
        )
        val smartRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        for ((cKey, cTitle) in smartSpecs) {
            val n = counts.smartCollections[cKey] ?: 0
            val sel = activeCollection == cKey
            val pill = createFilterPill("$cTitle ($n)", sel) {
                activeCollection = cKey
                activePlatformKey = ""
                activeCoreFilterId = "ALL"
                libraryVisibleLimit = 60
                renderActiveTabContent()
            }
            smartRow.addView(pill, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(34f)).apply { rightMargin = dp(6f) })
        }
        col.addView(HorizontalScrollView(this).apply {
            isHorizontalScrollBarEnabled = false
            addView(smartRow)
        }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(8f) })

        // 3. Platform Chips with live (N) counts
        val platRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        val totalInCol = counts.platformKeyCounts.values.sum()
        platRow.addView(
            createFilterPill("${StormLocalization.tr("filter_all_platforms")} ($totalInCol)", activePlatformKey.isEmpty()) {
                activePlatformKey = ""
                activeCoreFilterId = "ALL"
                libraryVisibleLimit = 60
                renderActiveTabContent()
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(32f)).apply { rightMargin = dp(6f) }
        )
        for ((pKey, cnt) in counts.platformKeyCounts.entries.sortedByDescending { it.value }) {
            val disp = MobileCoreRegistry.getPlatformDisplayName(pKey)
            platRow.addView(
                createFilterPill("$disp ($cnt)", activePlatformKey == pKey) {
                    activePlatformKey = pKey
                    activeCoreFilterId = "ALL"
                    libraryVisibleLimit = 60
                    renderActiveTabContent()
                },
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(32f)).apply { rightMargin = dp(6f) }
            )
        }
        col.addView(HorizontalScrollView(this).apply {
            isHorizontalScrollBarEnabled = false
            addView(platRow)
        }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(8f) })

        // 4. Strict Platform-Scoped Core Filter (shows strictly ONLY cores of the selected platform!)
        if (activePlatformKey.isNotEmpty()) {
            val coreRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
            val strictCores = MobileCoreRegistry.getCompatibleCoresForPlatform(activePlatformKey, "")
            coreRow.addView(
                createFilterPill("${StormLocalization.tr("filter_all_cores")} (${strictCores.size})", activeCoreFilterId == "ALL") {
                    activeCoreFilterId = "ALL"
                    renderActiveTabContent()
                },
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(30f)).apply { rightMargin = dp(6f) }
            )
            for (core in strictCores) {
                val cCount = counts.coreCounts[core.id] ?: 0
                val installedMark = if (MobileCoreRegistry.isCoreInstalled(this, core)) " • OK" else ""
                coreRow.addView(
                    createFilterPill("${core.displayName}$installedMark ($cCount)", activeCoreFilterId == core.id) {
                        activeCoreFilterId = core.id
                        renderActiveTabContent()
                    },
                    LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(30f)).apply { rightMargin = dp(6f) }
                )
            }
            col.addView(HorizontalScrollView(this).apply {
                isHorizontalScrollBarEnabled = false
                addView(coreRow)
            }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(10f) })
        }

        // 5. Filtered ROM Grid / Cards
        val filtered = MobileLibraryAndCoverEngine.filterRoms(
            allRoms = allRoms,
            collection = activeCollection,
            platform = activePlatformKey,
            coreId = activeCoreFilterId,
            searchQuery = searchQuery
        )

        if (filtered.isEmpty()) {
            val emptyCard = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                gravity = Gravity.CENTER
                background = StormThemeEngine.createVolumetricCardDrawable(this@MainActivity, cornerDp = 16f)
                setPadding(dp(20f), dp(28f), dp(20f), dp(28f))
            }
            val emptyIcon = ImageView(this).apply {
                setImageDrawable(StormVectorIcons.create(this@MainActivity, StormVectorIcons.LIBRARY, accent, 38f))
            }
            val emptyTv = TextView(this).apply {
                text = if (isScanningLibrary) "Сканирование игровых каталогов..." else StormLocalization.tr("empty_library")
                setTextColor(pal.textSecondary)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(13f))
                gravity = Gravity.CENTER
                StormThemeEngine.applyFont(this, bold = false)
            }
            emptyCard.addView(emptyIcon, LinearLayout.LayoutParams(dp(38f), dp(38f)).apply { bottomMargin = dp(10f) })
            emptyCard.addView(emptyTv)
            col.addView(emptyCard)
        } else {
            val columns = computeResponsiveGridColumns()
            val slice = filtered.take(libraryVisibleLimit)
            val rows = slice.chunked(columns)
            for (rowItems in rows) {
                val rowLayout = LinearLayout(this).apply {
                    orientation = LinearLayout.HORIZONTAL
                    weightSum = columns.toFloat()
                }
                for (i in 0 until columns) {
                    val item = rowItems.getOrNull(i)
                    if (item != null) {
                        rowLayout.addView(
                            buildGameCardView(item, pal, accent),
                            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                                if (i < columns - 1) rightMargin = dp(8f)
                                bottomMargin = dp(8f)
                            }
                        )
                    } else {
                        rowLayout.addView(View(this), LinearLayout.LayoutParams(0, 1, 1f).apply {
                            if (i < columns - 1) rightMargin = dp(8f)
                        })
                    }
                }
                col.addView(rowLayout)
            }

            if (filtered.size > libraryVisibleLimit) {
                col.addView(
                    createActionBtn(StormVectorIcons.DOWNLOAD, "Показать ещё (${filtered.size - libraryVisibleLimit})", accentFilled = false) {
                        libraryVisibleLimit += 60
                        renderActiveTabContent()
                    },
                    LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(42f)).apply { topMargin = dp(6f) }
                )
            }
        }

        scroll.addView(col)
        return scroll
    }

    private fun computeResponsiveGridColumns(): Int {
        val dm = resources.displayMetrics
        val availDp = (dm.widthPixels / dm.density) - if (isWideOrLandscapeMode()) 104f else 24f
        val targetCardDp = 285f * uiScale
        return max(1, (availDp / targetCardDp).toInt()).coerceAtMost(4)
    }

    private fun buildGameCardView(rom: MobileRomItem, pal: StormThemePalette, accent: Int): View {
        val card = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            background = StormThemeEngine.createVolumetricCardDrawable(this@MainActivity, cornerDp = 15f)
            setPadding(dp(10f), dp(10f), dp(10f), dp(10f))
            isFocusable = true
        }

        // Left Regional Boxart Container
        val coverFrame = FrameLayout(this).apply {
            background = GradientDrawable(
                GradientDrawable.Orientation.TL_BR,
                intArrayOf(StormThemeEngine.lighten(pal.bgElevated, 0.08f), StormThemeEngine.darken(pal.bgMain, 0.1f))
            ).apply {
                cornerRadius = dpF(10f)
                setStroke(dp(1f), pal.border)
            }
        }
        val coverIv = ImageView(this).apply {
            scaleType = ImageView.ScaleType.CENTER_CROP
        }
        val placeholderLabel = TextView(this).apply {
            text = rom.platformKey.uppercase(Locale.ROOT).take(5)
            gravity = Gravity.CENTER
            setTextColor(pal.textSecondary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(11f))
            StormThemeEngine.applyFont(this, bold = true)
        }
        coverFrame.addView(placeholderLabel, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        coverFrame.addView(coverIv, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))

        bindCoverThumbnailAsync(rom, coverIv, placeholderLabel)
        card.addView(coverFrame, LinearLayout.LayoutParams(dp(74f), dp(96f)).apply { rightMargin = dp(10f) })

        // Right Info + Badges + Launch Row
        val infoCol = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        }

        val titleTv = TextView(this).apply {
            text = rom.name
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(13.5f))
            maxLines = 2
            ellipsize = TextUtils.TruncateAt.END
            StormThemeEngine.applyFont(this, bold = true)
        }
        infoCol.addView(titleTv)

        val platTv = TextView(this).apply {
            text = "${rom.platform} • ${rom.sizeFormatted}"
            setTextColor(pal.textSecondary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(11f))
            maxLines = 1
            ellipsize = TextUtils.TruncateAt.END
            StormThemeEngine.applyFont(this, bold = false)
        }
        infoCol.addView(platTv, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(2f) })

        // Badges row: Region (JP/US/EU/WORLD) + MOD - M. RUS + Assigned Core
        val badgeRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        badgeRow.addView(createMiniTag(rom.regionCode, StormThemeEngine.withAlpha(accent, 48), pal.textPrimary))
        if (rom.isRussianMod) {
            badgeRow.addView(
                createMiniTag("MOD - M. RUS", Color.argb(65, 16, 185, 129), Color.rgb(110, 231, 183)),
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { leftMargin = dp(4f) }
            )
        }
        val assignedCore = MobileCoreRegistry.findCoreById(rom.assignedCoreId)
            ?: MobileCoreRegistry.getDefaultCoreForPlatform(rom.platformKey, rom.extension)
        if (assignedCore != null) {
            badgeRow.addView(
                createMiniTag(assignedCore.displayName, StormThemeEngine.withAlpha(pal.bgElevated, 220), pal.textSecondary),
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { leftMargin = dp(4f) }
            )
        }
        infoCol.addView(badgeRow, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(5f) })

        // Buttons row: Играть + Bottom-Sheet Menu + Favorite Star
        val btnRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        btnRow.addView(
            createActionBtn(StormVectorIcons.PLAY, "Играть", accentFilled = true) {
                launchGameWithAutoCoreDownload(rom)
            },
            LinearLayout.LayoutParams(0, dp(34f), 1f).apply { rightMargin = dp(6f) }
        )
        btnRow.addView(
            createIconOnlyBtn(
                StormVectorIcons.STAR,
                if (rom.isFavorite) Color.rgb(250, 204, 21) else pal.textSecondary
            ) {
                rom.isFavorite = !rom.isFavorite
                MobileLibraryAndCoverEngine.saveRomItemState(romMetaPrefs, rom)
                renderActiveTabContent()
            },
            LinearLayout.LayoutParams(dp(34f), dp(34f)).apply { rightMargin = dp(5f) }
        )
        btnRow.addView(
            createIconOnlyBtn(StormVectorIcons.SLIDERS, pal.textPrimary) {
                showGameBottomSheetMenu(rom)
            },
            LinearLayout.LayoutParams(dp(34f), dp(34f))
        )
        infoCol.addView(btnRow, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(7f) })

        card.addView(infoCol)
        return card
    }

    private fun bindCoverThumbnailAsync(rom: MobileRomItem, imageView: ImageView, placeholder: View) {
        val existingPath = rom.coverPath
        imageView.tag = rom.filePath
        if (!existingPath.isNullOrBlank()) {
            val cachedBmp = coverBitmapCache.get(existingPath)
            if (cachedBmp != null) {
                imageView.setImageBitmap(cachedBmp)
                placeholder.visibility = View.GONE
                return
            }
            bgExecutor.execute {
                val f = File(existingPath)
                if (f.exists() && f.length() > 64L) {
                    val opts = BitmapFactory.Options().apply { inSampleSize = 2 }
                    val bmp = BitmapFactory.decodeFile(f.absolutePath, opts)
                    if (bmp != null) {
                        coverBitmapCache.put(existingPath, bmp)
                        mainHandler.post {
                            if (imageView.tag == rom.filePath) {
                                imageView.setImageBitmap(bmp)
                                placeholder.visibility = View.GONE
                            }
                        }
                        return@execute
                    }
                }
            }
        } else {
            // Opportunistic background fetch
            MobileLibraryAndCoverEngine.fetchCoverArtAsync(this, rom, romMetaPrefs) { _, file ->
                if (file != null && imageView.tag == rom.filePath) {
                    bgExecutor.execute {
                        val opts = BitmapFactory.Options().apply { inSampleSize = 2 }
                        val bmp = BitmapFactory.decodeFile(file.absolutePath, opts)
                        if (bmp != null) {
                            coverBitmapCache.put(file.absolutePath, bmp)
                            mainHandler.post {
                                if (imageView.tag == rom.filePath) {
                                    imageView.setImageBitmap(bmp)
                                    placeholder.visibility = View.GONE
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private fun launchGameWithAutoCoreDownload(rom: MobileRomItem) {
        val core = MobileCoreRegistry.findCoreById(rom.assignedCoreId)
            ?: MobileCoreRegistry.getDefaultCoreForPlatform(rom.platformKey, rom.extension)

        if (core == null) {
            Toast.makeText(this, "Не найдено совместимое ядро для ${rom.platform}", Toast.LENGTH_LONG).show()
            return
        }

        rom.lastPlayedMs = System.currentTimeMillis()
        MobileLibraryAndCoverEngine.saveRomItemState(romMetaPrefs, rom)

        val installedSo = MobileCoreRegistry.resolveInstalledCoreFile(this, core)
        if (installedSo != null && installedSo.exists()) {
            startEmulationActivity(rom, core, installedSo)
            return
        }

        // Core not installed yet -> auto-download from Libretro Android Buildbot with smooth progress dialog!
        showProgressOperationDialog(
            titleText = "${StormLocalization.tr("core_downloading")} - ${core.displayName}",
            initialStatus = "ABI: ${MobileCoreRegistry.detectDeviceAbi()}"
        ) { updateProgress, closeDialog ->
            MobileCoreRegistry.downloadCoreAsync(
                context = this,
                core = core,
                onProgress = { pct, msg -> updateProgress(pct, msg) },
                onDone = { ok, soFile, message ->
                    closeDialog()
                    if (ok && soFile != null && soFile.exists()) {
                        startEmulationActivity(rom, core, soFile)
                    } else {
                        Toast.makeText(this, "${StormLocalization.tr("core_failed")}: $message", Toast.LENGTH_LONG).show()
                    }
                }
            )
        }
    }

    private fun startEmulationActivity(rom: MobileRomItem, core: MobileCoreInfo, soFile: File) {
        val accentIdx = StormThemeEngine.ACCENTS.indexOfFirst { it.id == StormThemeEngine.currentAccent.id }.coerceAtLeast(0)
        val intent = Intent(this, EmulationActivity::class.java).apply {
            putExtra(EmulationActivity.EXTRA_ROM_PATH, rom.filePath)
            putExtra(EmulationActivity.EXTRA_ROM_NAME, rom.name)
            putExtra(EmulationActivity.EXTRA_PLATFORM, rom.platformKey.ifBlank { rom.platform })
            putExtra(EmulationActivity.EXTRA_CORE_SO_PATH, soFile.absolutePath)
            putExtra(EmulationActivity.EXTRA_CORE_NAME, core.displayName)
            putExtra(EmulationActivity.EXTRA_THEME_ID, StormThemeEngine.currentTheme.id)
            putExtra(EmulationActivity.EXTRA_ACCENT_IDX, accentIdx)
            putExtra(EmulationActivity.EXTRA_LANG_CODE, StormLocalization.currentLanguage)
        }
        startActivity(intent)
    }

    private fun showGameBottomSheetMenu(rom: MobileRomItem) {
        val pal = StormThemeEngine.currentTheme
        val accent = StormThemeEngine.activeAccentColor

        val dlg = Dialog(this, android.R.style.Theme_Translucent_NoTitleBar)
        dlg.setCanceledOnTouchOutside(true)
        attachEscDismiss(dlg)

        val root = FrameLayout(this).apply {
            setBackgroundColor(Color.argb(145, 4, 3, 12))
            setOnClickListener { dlg.dismiss() }
        }

        val sheet = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            isClickable = true
            background = StormThemeEngine.createVolumetricCardDrawable(this@MainActivity, cornerDp = 20f)
            setPadding(dp(18f), dp(16f), dp(18f), dp(20f))
        }

        val title = TextView(this).apply {
            text = rom.name
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(15.5f))
            StormThemeEngine.applyFont(this, bold = true)
        }
        val sub = TextView(this).apply {
            text = "${rom.platform} • ${File(rom.filePath).name}"
            setTextColor(pal.textSecondary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(11.5f))
        }
        sheet.addView(title)
        sheet.addView(sub, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(12f) })

        // Strictly platform-scoped Core Selection inside Bottom Sheet
        val compatCores = MobileCoreRegistry.getCompatibleCoresForPlatform(rom.platformKey, rom.extension)
        val coreHdr = TextView(this).apply {
            text = "${StormLocalization.tr("assign_core")} (${compatCores.size}):"
            setTextColor(accent)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(12f))
            StormThemeEngine.applyFont(this, bold = true)
        }
        sheet.addView(coreHdr, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(6f) })

        val coreChips = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        for (c in compatCores) {
            val isSel = rom.assignedCoreId == c.id
            val inst = if (MobileCoreRegistry.isCoreInstalled(this, c)) " [OK]" else ""
            val chip = createFilterPill("${c.displayName}$inst", isSel) {
                rom.assignedCoreId = c.id
                MobileLibraryAndCoverEngine.saveRomItemState(romMetaPrefs, rom)
                dlg.dismiss()
                renderActiveTabContent()
            }
            coreChips.addView(chip, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(34f)).apply { rightMargin = dp(6f) })
        }
        sheet.addView(HorizontalScrollView(this).apply {
            isHorizontalScrollBarEnabled = false
            addView(coreChips)
        }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(12f) })

        // Bottom-sheet actions: Play, Cheats, Favorite, .m3u Disc Builder
        sheet.addView(
            createActionBtn(StormVectorIcons.PLAY, "Играть сейчас", accentFilled = true) {
                dlg.dismiss()
                launchGameWithAutoCoreDownload(rom)
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(42f)).apply { bottomMargin = dp(8f) }
        )
        sheet.addView(
            createActionBtn(StormVectorIcons.CHEATS, StormLocalization.tr("cheats_title"), accentFilled = false) {
                dlg.dismiss()
                val matched = MobileCheatAndBiosManager.matchCheatsForLibrary(listOf(rom)).firstOrNull()
                if (matched != null) {
                    showCheatProfileEditorDialog(matched)
                }
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(40f)).apply { bottomMargin = dp(8f) }
        )
        sheet.addView(
            createActionBtn(StormVectorIcons.STAR, StormLocalization.tr("favorite_toggle"), accentFilled = false) {
                rom.isFavorite = !rom.isFavorite
                MobileLibraryAndCoverEngine.saveRomItemState(romMetaPrefs, rom)
                dlg.dismiss()
                renderActiveTabContent()
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(40f)).apply { bottomMargin = dp(8f) }
        )
        sheet.addView(
            createActionBtn(StormVectorIcons.FOLDER, StormLocalization.tr("create_m3u"), accentFilled = false) {
                dlg.dismiss()
                buildM3uForRomSiblings(rom)
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(40f))
        )

        root.addView(
            sheet,
            FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.BOTTOM).apply {
                leftMargin = dp(8f)
                rightMargin = dp(8f)
                bottomMargin = dp(8f)
            }
        )
        dlg.setContentView(root)
        dlg.show()
    }

    private fun buildM3uForRomSiblings(rom: MobileRomItem) {
        val f = File(rom.filePath)
        val parent = f.parentFile ?: return
        val discExts = setOf("cue", "chd", "iso", "pbp", "gdi", "cdi", "img")
        val basePrefix = rom.name.take(8).lowercase(Locale.ROOT)
        val candidates = parent.listFiles { cand ->
            cand.isFile && discExts.contains(cand.extension.lowercase(Locale.ROOT)) &&
                cand.name.lowercase(Locale.ROOT).contains(basePrefix)
        }?.sortedBy { it.name } ?: emptyList()

        if (candidates.size < 2) {
            Toast.makeText(this, "В папке игры найдено менее 2 дисков для сборки .m3u", Toast.LENGTH_SHORT).show()
            return
        }
        val m3u = MobileLibraryAndCoverEngine.createM3uPlaylist(candidates, rom.name)
        if (m3u != null) {
            Toast.makeText(this, "Создан плейлист: ${m3u.name}", Toast.LENGTH_LONG).show()
            triggerLibraryScan(autoFetchMissingCovers = false)
        }
    }

    // =========================================================================
    // TAB 2: ЯДРА (360 Cores / 60+ Platforms, Strict Filter, 1-Tap Download)
    // =========================================================================

    private fun buildCoresTabView(): View {
        val pal = StormThemeEngine.currentTheme
        val accent = StormThemeEngine.activeAccentColor

        val scroll = ScrollView(this)
        val col = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(12f), dp(10f), dp(12f), dp(20f))
        }

        val allCores = MobileCoreRegistry.ALL_CORES
        val installedCount = allCores.count { MobileCoreRegistry.isCoreInstalled(this, it) }
        val abi = MobileCoreRegistry.detectDeviceAbi()

        // Summary & Import bar
        val topCard = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            background = StormThemeEngine.createVolumetricCardDrawable(this@MainActivity, cornerDp = 14f)
            setPadding(dp(12f), dp(10f), dp(12f), dp(10f))
        }
        val topInfo = TextView(this).apply {
            text = "${StormLocalization.tr("cores_title")}: ${allCores.size} • ${StormLocalization.tr("cores_installed")}: $installedCount • ABI: $abi"
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(12f))
            StormThemeEngine.applyFont(this, bold = true)
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply { rightMargin = dp(8f) }
        }
        val syncBtn = createActionBtn(StormVectorIcons.FOLDER, "Импорт .so", accentFilled = false) {
            val count = MobileCoreRegistry.syncPublicCoresToExecutableDir(this)
            Toast.makeText(this, "Импортировано ядер .so: $count", Toast.LENGTH_SHORT).show()
            renderActiveTabContent()
        }
        topCard.addView(topInfo)
        topCard.addView(syncBtn)
        col.addView(topCard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(10f) })

        // Installed toggle + Strict Platform Filter Chips
        val filterRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        filterRow.addView(
            createFilterPill("${StormLocalization.tr("cores_available")} (${allCores.size})", !coresOnlyInstalled) {
                coresOnlyInstalled = false
                renderActiveTabContent()
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(32f)).apply { rightMargin = dp(6f) }
        )
        filterRow.addView(
            createFilterPill("${StormLocalization.tr("cores_installed")} ($installedCount)", coresOnlyInstalled) {
                coresOnlyInstalled = true
                renderActiveTabContent()
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(32f)).apply { rightMargin = dp(10f) }
        )
        filterRow.addView(
            createFilterPill(StormLocalization.tr("filter_all_platforms"), coresPlatformFilterKey.isEmpty()) {
                coresPlatformFilterKey = ""
                renderActiveTabContent()
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(32f)).apply { rightMargin = dp(6f) }
        )

        val uniquePlatforms = LinkedHashSet<String>()
        allCores.forEach { uniquePlatforms.addAll(it.platformKeys) }
        for (pKey in uniquePlatforms) {
            val label = MobileCoreRegistry.getPlatformDisplayName(pKey)
            filterRow.addView(
                createFilterPill(label, coresPlatformFilterKey == pKey) {
                    coresPlatformFilterKey = pKey
                    renderActiveTabContent()
                },
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(32f)).apply { rightMargin = dp(6f) }
            )
        }
        col.addView(HorizontalScrollView(this).apply {
            isHorizontalScrollBarEnabled = false
            addView(filterRow)
        }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(10f) })

        val q = searchQuery.trim().lowercase(Locale.ROOT)
        val visibleCores = allCores.filter { core ->
            if (coresOnlyInstalled && !MobileCoreRegistry.isCoreInstalled(this, core)) return@filter false
            if (coresPlatformFilterKey.isNotEmpty() && !core.platformKeys.contains(coresPlatformFilterKey)) return@filter false
            if (q.isNotEmpty()) {
                val hay = "${core.displayName} ${core.id} ${core.platformDisplay} ${core.descriptionRu} ${core.descriptionEn}".lowercase(Locale.ROOT)
                if (!hay.contains(q)) return@filter false
            }
            true
        }

        for (core in visibleCores.take(120)) {
            val installed = MobileCoreRegistry.isCoreInstalled(this, core)
            val sizeStr = MobileCoreRegistry.getInstalledCoreSizeFormatted(this, core)
            val primaryPlatKey = if (coresPlatformFilterKey.isNotEmpty()) coresPlatformFilterKey else core.platformKeys.firstOrNull().orEmpty()
            val isDefaultForPlat = primaryPlatKey.isNotEmpty() && platformDefaultCores[primaryPlatKey] == core.id

            val card = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                background = StormThemeEngine.createVolumetricCardDrawable(this@MainActivity, cornerDp = 14f)
                setPadding(dp(12f), dp(10f), dp(12f), dp(10f))
            }

            val topLine = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
            }
            val nameTv = TextView(this).apply {
                text = "${core.displayName} (${core.platformDisplay})"
                setTextColor(pal.textPrimary)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(13f))
                StormThemeEngine.applyFont(this, bold = true)
                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            }
            topLine.addView(nameTv)

            if (installed) {
                topLine.addView(
                    createMiniTag(
                        "${StormLocalization.tr("core_installed_badge")}${if (sizeStr.isNotEmpty()) " • $sizeStr" else ""}",
                        Color.argb(60, 16, 185, 129),
                        Color.rgb(110, 231, 183)
                    )
                )
            }
            card.addView(topLine)

            val descTv = TextView(this).apply {
                text = core.localizedDescription()
                setTextColor(pal.textSecondary)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(11.5f))
            }
            card.addView(descTv, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(3f) })

            val actions = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
            }
            actions.addView(
                createActionBtn(
                    StormVectorIcons.DOWNLOAD,
                    if (installed) StormLocalization.tr("core_update") else StormLocalization.tr("core_install"),
                    accentFilled = !installed
                ) {
                    showProgressOperationDialog(
                        titleText = "${StormLocalization.tr("core_downloading")} - ${core.displayName}",
                        initialStatus = "ABI: $abi"
                    ) { upd, close ->
                        MobileCoreRegistry.downloadCoreAsync(
                            context = this,
                            core = core,
                            onProgress = { pct, msg -> upd(pct, msg) },
                            onDone = { ok, _, msg ->
                                close()
                                Toast.makeText(this, if (ok) StormLocalization.tr("core_ready") else msg, Toast.LENGTH_SHORT).show()
                                renderActiveTabContent()
                            }
                        )
                    }
                },
                LinearLayout.LayoutParams(0, dp(36f), 1f).apply { rightMargin = dp(6f) }
            )
            if (primaryPlatKey.isNotEmpty()) {
                actions.addView(
                    createActionBtn(
                        StormVectorIcons.CHECK,
                        if (isDefaultForPlat) "Ядро по умолчанию" else "Сделать по умолч.",
                        accentFilled = isDefaultForPlat
                    ) {
                        platformDefaultCores[primaryPlatKey] = core.id
                        savePersistedAppState()
                        for (r in allRoms) {
                            if (r.platformKey == primaryPlatKey) r.assignedCoreId = core.id
                        }
                        renderActiveTabContent()
                    },
                    LinearLayout.LayoutParams(0, dp(36f), 1f)
                )
            }
            card.addView(actions, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(8f) })
            col.addView(card, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(8f) })
        }

        scroll.addView(col)
        return scroll
    }

    // =========================================================================
    // TAB 3: ЧИТЫ И ТРЕНЕРЫ (26 800+ Games, 1-Tap Libretro DB, Paginated List)
    // =========================================================================

    private fun buildCheatsTabView(): View {
        val pal = StormThemeEngine.currentTheme
        val scroll = ScrollView(this)
        val col = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(12f), dp(10f), dp(12f), dp(20f))
        }

        if (cachedLibCheats.isEmpty() && allRoms.isNotEmpty()) {
            cachedLibCheats = MobileCheatAndBiosManager.matchCheatsForLibrary(allRoms)
        }
        if (cachedAllCheats.isEmpty()) {
            cachedAllCheats = MobileCheatAndBiosManager.indexAllChtFilesLazy()
        }

        // 1-Tap Download Official Libretro Cheats Database button
        col.addView(
            createActionBtn(StormVectorIcons.DOWNLOAD, StormLocalization.tr("cheats_download_db"), accentFilled = true) {
                showProgressOperationDialog(
                    titleText = StormLocalization.tr("cheats_download_db"),
                    initialStatus = "GitHub: libretro-database..."
                ) { upd, close ->
                    MobileCheatAndBiosManager.downloadOfficialLibretroCheatsZipAsync(
                        onProgress = { pct, msg -> upd(pct, msg) },
                        onDone = { ok, count, msg ->
                            close()
                            cachedAllCheats = MobileCheatAndBiosManager.indexAllChtFilesLazy()
                            cachedLibCheats = MobileCheatAndBiosManager.matchCheatsForLibrary(allRoms)
                            Toast.makeText(this, if (ok) "Извлечено .cht файлов: $count" else msg, Toast.LENGTH_LONG).show()
                            renderActiveTabContent()
                        }
                    )
                }
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(42f)).apply { bottomMargin = dp(10f) }
        )

        // 2-Column Mode Toggle: В библиотеке (N) | Все читы (N)
        val modeRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        modeRow.addView(
            createFilterPill("${StormLocalization.tr("cheats_in_library")} (${cachedLibCheats.size})", cheatsLibraryOnly) {
                cheatsLibraryOnly = true
                cheatsPage = 0
                renderActiveTabContent()
            },
            LinearLayout.LayoutParams(0, dp(36f), 1f).apply { rightMargin = dp(6f) }
        )
        modeRow.addView(
            createFilterPill("${StormLocalization.tr("cheats_all_db")} (${cachedAllCheats.size})", !cheatsLibraryOnly) {
                cheatsLibraryOnly = false
                cheatsPage = 0
                renderActiveTabContent()
            },
            LinearLayout.LayoutParams(0, dp(36f), 1f)
        )
        col.addView(modeRow, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(8f) })

        val baseSource = if (cheatsLibraryOnly) cachedLibCheats else cachedAllCheats

        // Platform filter chips
        val platCounts = LinkedHashMap<String, Int>()
        for (p in baseSource) {
            if (p.platformKey.isNotEmpty()) {
                platCounts[p.platformKey] = (platCounts[p.platformKey] ?: 0) + 1
            }
        }
        if (platCounts.isNotEmpty()) {
            val pRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
            pRow.addView(
                createFilterPill("${StormLocalization.tr("filter_all_platforms")} (${baseSource.size})", cheatsPlatformFilterKey.isEmpty()) {
                    cheatsPlatformFilterKey = ""
                    cheatsPage = 0
                    renderActiveTabContent()
                },
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(32f)).apply { rightMargin = dp(6f) }
            )
            for ((pk, cnt) in platCounts.entries.sortedByDescending { it.value }) {
                pRow.addView(
                    createFilterPill("${MobileCoreRegistry.getPlatformDisplayName(pk)} ($cnt)", cheatsPlatformFilterKey == pk) {
                        cheatsPlatformFilterKey = pk
                        cheatsPage = 0
                        renderActiveTabContent()
                    },
                    LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(32f)).apply { rightMargin = dp(6f) }
                )
            }
            col.addView(HorizontalScrollView(this).apply {
                isHorizontalScrollBarEnabled = false
                addView(pRow)
            }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(10f) })
        }

        val q = searchQuery.trim().lowercase(Locale.ROOT)
        val filtered = baseSource.filter { item ->
            if (cheatsPlatformFilterKey.isNotEmpty() && item.platformKey != cheatsPlatformFilterKey) return@filter false
            if (q.isNotEmpty()) {
                val hay = "${item.cleanTitle} ${item.gameName} ${item.platformFolder}".lowercase(Locale.ROOT)
                if (!hay.contains(q)) return@filter false
            }
            true
        }

        val pageSize = 50
        val totalPages = max(1, (filtered.size + pageSize - 1) / pageSize)
        cheatsPage = cheatsPage.coerceIn(0, totalPages - 1)
        val pageSlice = filtered.drop(cheatsPage * pageSize).take(pageSize)

        if (pageSlice.isEmpty()) {
            val emptyTv = TextView(this).apply {
                text = if (baseSource.isEmpty()) {
                    "Нажмите «Скачать базу читов Libretro», чтобы загрузить 26 800+ профилей .cht, или добавьте игры в библиотеку."
                } else {
                    "По вашему запросу читы не найдены."
                }
                setTextColor(pal.textSecondary)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(12.5f))
                setPadding(dp(12f), dp(18f), dp(12f), dp(18f))
            }
            col.addView(emptyTv)
        } else {
            for (prof in pageSlice) {
                val card = LinearLayout(this).apply {
                    orientation = LinearLayout.HORIZONTAL
                    gravity = Gravity.CENTER_VERTICAL
                    background = StormThemeEngine.createVolumetricCardDrawable(this@MainActivity, cornerDp = 13f)
                    setPadding(dp(12f), dp(10f), dp(12f), dp(10f))
                    setOnClickListener { showCheatProfileEditorDialog(prof) }
                }
                val txtCol = LinearLayout(this).apply {
                    orientation = LinearLayout.VERTICAL
                    layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                }
                val gTitle = TextView(this).apply {
                    text = prof.cleanTitle
                    setTextColor(pal.textPrimary)
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(13f))
                    StormThemeEngine.applyFont(this, bold = true)
                }
                val gSub = TextView(this).apply {
                    val badge = if (prof.isGeneratedTrainer) "Trainer Ready" else ".CHT"
                    text = "${prof.platformFolder} • $badge"
                    setTextColor(pal.textSecondary)
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(11f))
                }
                txtCol.addView(gTitle)
                txtCol.addView(gSub)
                card.addView(txtCol)

                card.addView(
                    createActionBtn(StormVectorIcons.CHEATS, "Открыть", accentFilled = false) {
                        showCheatProfileEditorDialog(prof)
                    },
                    LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(34f))
                )
                col.addView(card, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(7f) })
            }

            if (totalPages > 1) {
                val pagerRow = LinearLayout(this).apply {
                    orientation = LinearLayout.HORIZONTAL
                    gravity = Gravity.CENTER
                }
                pagerRow.addView(
                    createFilterPill("< Пред.", false) {
                        if (cheatsPage > 0) {
                            cheatsPage--
                            renderActiveTabContent()
                        }
                    },
                    LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(34f)).apply { rightMargin = dp(10f) }
                )
                val pgLabel = TextView(this).apply {
                    text = "${cheatsPage + 1} / $totalPages"
                    setTextColor(pal.textPrimary)
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(12f))
                    StormThemeEngine.applyFont(this, bold = true)
                }
                pagerRow.addView(pgLabel)
                pagerRow.addView(
                    createFilterPill("След. >", false) {
                        if (cheatsPage < totalPages - 1) {
                            cheatsPage++
                            renderActiveTabContent()
                        }
                    },
                    LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(34f)).apply { leftMargin = dp(10f) }
                )
                col.addView(pagerRow, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(8f) })
            }
        }

        scroll.addView(col)
        return scroll
    }

    private fun showCheatProfileEditorDialog(profile: MobileCheatProfile) {
        val pal = StormThemeEngine.currentTheme
        val chtFile = File(profile.chtFilePath)
        val entries = ArrayList(if (chtFile.exists()) MobileCheatAndBiosManager.parseChtFile(chtFile) else emptyList())

        val dlg = Dialog(this)
        dlg.requestWindowFeature(Window.FEATURE_NO_TITLE)
        dlg.window?.setBackgroundDrawable(ColorDrawable(Color.TRANSPARENT))
        attachEscDismiss(dlg)

        val card = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = StormThemeEngine.createVolumetricCardDrawable(this@MainActivity, cornerDp = 18f)
            setPadding(dp(16f), dp(16f), dp(16f), dp(16f))
        }

        val hdr = TextView(this).apply {
            text = profile.cleanTitle
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(15f))
            StormThemeEngine.applyFont(this, bold = true)
        }
        card.addView(hdr)

        val listCol = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }

        fun rebuildEntries() {
            listCol.removeAllViews()
            if (entries.isEmpty()) {
                val empty = TextView(this).apply {
                    text = "Список кодов пуст. Добавьте собственный чит-код ниже и нажмите «Сохранить .cht»."
                    setTextColor(pal.textSecondary)
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(12f))
                    setPadding(0, dp(10f), 0, dp(10f))
                }
                listCol.addView(empty)
            } else {
                for (e in entries) {
                    val r = LinearLayout(this).apply {
                        orientation = LinearLayout.HORIZONTAL
                        gravity = Gravity.CENTER_VERTICAL
                        setPadding(0, dp(5f), 0, dp(5f))
                    }
                    val cb = CheckBox(this).apply {
                        isChecked = e.isEnabled
                        setOnCheckedChangeListener { _, checked -> e.isEnabled = checked }
                    }
                    val tc = LinearLayout(this).apply {
                        orientation = LinearLayout.VERTICAL
                        layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                    }
                    val dTv = TextView(this).apply {
                        text = e.description
                        setTextColor(pal.textPrimary)
                        setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(12.5f))
                        StormThemeEngine.applyFont(this, bold = true)
                    }
                    val cTv = TextView(this).apply {
                        text = e.code
                        setTextColor(pal.textSecondary)
                        setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(10.5f))
                    }
                    tc.addView(dTv)
                    tc.addView(cTv)
                    r.addView(cb)
                    r.addView(tc)
                    listCol.addView(r)
                }
            }
        }
        rebuildEntries()

        card.addView(ScrollView(this).apply {
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(240f)).apply {
                topMargin = dp(8f)
                bottomMargin = dp(8f)
            }
            addView(listCol)
        })

        val descBox = createStyledEdit("Описание чита (напр. Бесконечные жизни)")
        val codeBox = createStyledEdit("Код чита (напр. 8009D000 0063)")
        card.addView(descBox, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(38f)).apply { bottomMargin = dp(6f) })
        card.addView(codeBox, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(38f)).apply { bottomMargin = dp(8f) })

        val btns = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        btns.addView(
            createActionBtn(StormVectorIcons.CHEATS, "+ Новый чит", accentFilled = false) {
                val c = codeBox.text.toString().trim()
                if (c.isNotEmpty()) {
                    val d = descBox.text.toString().trim().ifBlank { "Чит #${entries.size + 1}" }
                    entries.add(MobileCheatEntry(entries.size, d, d, c, true))
                    descBox.setText("")
                    codeBox.setText("")
                    rebuildEntries()
                }
            },
            LinearLayout.LayoutParams(0, dp(40f), 1f).apply { rightMargin = dp(6f) }
        )
        btns.addView(
            createActionBtn(StormVectorIcons.SAVE, StormLocalization.tr("cheats_save"), accentFilled = true) {
                chtFile.parentFile?.mkdirs()
                MobileCheatAndBiosManager.saveChtFile(chtFile, entries)
                Toast.makeText(this, StormLocalization.tr("cheats_save"), Toast.LENGTH_SHORT).show()
                dlg.dismiss()
                renderActiveTabContent()
            },
            LinearLayout.LayoutParams(0, dp(40f), 1f)
        )
        card.addView(btns)

        dlg.setContentView(
            card,
            ViewGroup.LayoutParams((resources.displayMetrics.widthPixels * 0.92f).roundToInt(), ViewGroup.LayoutParams.WRAP_CONTENT)
        )
        dlg.show()
    }

    // =========================================================================
    // TAB 4: BIOS И РАМКИ (MD5 Verification + Mega Bezel / Duimon Online Hub)
    // =========================================================================

    private fun buildBiosAndBezelsTabView(): View {
        val pal = StormThemeEngine.currentTheme
        val scroll = ScrollView(this)
        val col = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(12f), dp(10f), dp(12f), dp(20f))
        }

        // Section A: Online Bezel & Shader Packs (HSM Mega Bezel, Duimon Mega Bezel, Duimon Mobile Lite)
        col.addView(createGroupHeader(StormLocalization.tr("bezel_title")))

        val emuPrefs = getSharedPreferences(PREFS_EMU, Context.MODE_PRIVATE)
        val bezelEnabled = emuPrefs.getBoolean("enable_bezel_overlay", false)
        val toggleRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            background = StormThemeEngine.createVolumetricCardDrawable(this@MainActivity, cornerDp = 12f)
            setPadding(dp(12f), dp(8f), dp(12f), dp(8f))
        }
        val cbBezel = CheckBox(this).apply {
            isChecked = bezelEnabled
            setOnCheckedChangeListener { _, checked ->
                emuPrefs.edit().putBoolean("enable_bezel_overlay", checked).apply()
            }
        }
        val lblBezel = TextView(this).apply {
            text = "Включить отображение декоративных рамок (Bezel Overlay) в эмуляции"
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(12f))
        }
        toggleRow.addView(cbBezel)
        toggleRow.addView(lblBezel)
        col.addView(toggleRow, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(8f) })

        val bezelPacks = MobileCheatAndBiosManager.scanBezelPacksStatus()
        for (pack in bezelPacks) {
            val card = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                background = StormThemeEngine.createVolumetricCardDrawable(this@MainActivity, cornerDp = 14f)
                setPadding(dp(12f), dp(10f), dp(12f), dp(10f))
            }
            val titleTv = TextView(this).apply {
                text = "${pack.title} (${pack.approxSizeLabel})"
                setTextColor(pal.textPrimary)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(13.5f))
                StormThemeEngine.applyFont(this, bold = true)
            }
            val descTv = TextView(this).apply {
                val statusStr = if (pack.isInstalled) "${StormLocalization.tr("bezel_installed")} (${pack.presetCount})" else pack.descriptionRu
                text = statusStr
                setTextColor(if (pack.isInstalled) Color.rgb(110, 231, 183) else pal.textSecondary)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(11.5f))
            }
            card.addView(titleTv)
            card.addView(descTv, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(3f); bottomMargin = dp(8f) })

            card.addView(
                createActionBtn(
                    StormVectorIcons.DOWNLOAD,
                    if (pack.isInstalled) StormLocalization.tr("core_update") else StormLocalization.tr("bezel_install"),
                    accentFilled = !pack.isInstalled
                ) {
                    showProgressOperationDialog(pack.title, "Загрузка архива рамок...") { upd, close ->
                        MobileCheatAndBiosManager.downloadBezelPackAsync(
                            packId = pack.id,
                            onProgress = { pct, msg -> upd(pct, msg) },
                            onDone = { ok, msg ->
                                close()
                                Toast.makeText(this, msg, Toast.LENGTH_LONG).show()
                                if (ok) emuPrefs.edit().putBoolean("enable_bezel_overlay", true).apply()
                                renderActiveTabContent()
                            }
                        )
                    }
                },
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(38f))
            )
            col.addView(card, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(8f) })
        }

        // Section B: System BIOS Catalog & MD5 Verification
        val biosHeaderRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        biosHeaderRow.addView(
            createGroupHeader(StormLocalization.tr("bios_title")),
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        )
        biosHeaderRow.addView(
            createActionBtn(StormVectorIcons.REFRESH, StormLocalization.tr("bios_rescan"), accentFilled = false) {
                renderActiveTabContent()
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(34f))
        )
        col.addView(biosHeaderRow, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(6f); bottomMargin = dp(6f) })

        val biosList = MobileCheatAndBiosManager.scanBiosCatalog()
        for (bios in biosList) {
            val (statusLabel, statusColor) = when (bios.state) {
                BiosVerifyState.VERIFIED_MD5 -> StormLocalization.tr("bios_verified") to Color.rgb(52, 211, 153)
                BiosVerifyState.PRESENT_CUSTOM, BiosVerifyState.PRESENT_DIRECTORY -> StormLocalization.tr("bios_present") to Color.rgb(56, 189, 248)
                BiosVerifyState.MISSING_REQUIRED -> "${StormLocalization.tr("bios_missing")} (${StormLocalization.tr("bios_required")})" to Color.rgb(251, 113, 133)
                BiosVerifyState.MISSING_OPTIONAL -> "${StormLocalization.tr("bios_missing")} (${StormLocalization.tr("bios_optional")})" to pal.textSecondary
            }

            val card = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                background = StormThemeEngine.createVolumetricCardDrawable(this@MainActivity, cornerDp = 13f)
                setPadding(dp(12f), dp(10f), dp(12f), dp(10f))
            }
            val info = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply { rightMargin = dp(8f) }
            }
            val nameTv = TextView(this).apply {
                text = "system/${bios.relativePath} • ${bios.platform}"
                setTextColor(pal.textPrimary)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(12.5f))
                StormThemeEngine.applyFont(this, bold = true)
            }
            val statTv = TextView(this).apply {
                val md5Note = if (bios.actualMd5.isNotEmpty()) " • MD5: ${bios.actualMd5.take(12)}..." else ""
                text = "$statusLabel$md5Note"
                setTextColor(statusColor)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(11f))
                StormThemeEngine.applyFont(this, bold = true)
            }
            info.addView(nameTv)
            info.addView(statTv)
            card.addView(info)

            if (!bios.isDirectory) {
                card.addView(
                    createActionBtn(StormVectorIcons.FOLDER, "Импорт BIOS", accentFilled = false) {
                        pendingBiosTarget = bios
                        launchSafFilePicker(REQ_PICK_BIOS_FILE)
                    },
                    LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(34f))
                )
            }
            col.addView(card, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(7f) })
        }

        scroll.addView(col)
        return scroll
    }

    // =========================================================================
    // TAB 5: НАСТРОЙКИ (9 Themes, 13 Accents, 10 Fonts, 11 Languages, Scale, Gamepads, Updates)
    // =========================================================================

    private fun buildSettingsTabView(): View {
        val pal = StormThemeEngine.currentTheme
        val accent = StormThemeEngine.activeAccentColor
        val emuPrefs = getSharedPreferences(PREFS_EMU, Context.MODE_PRIVATE)

        val scroll = ScrollView(this)
        val col = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(12f), dp(10f), dp(12f), dp(24f))
        }

        // 1. 9 Dynamic Themes (MIDNIGHT default)
        col.addView(createGroupHeader(StormLocalization.tr("settings_theme")))
        val themeRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        for (t in StormThemeEngine.THEMES) {
            val sel = StormThemeEngine.currentTheme.id == t.id
            themeRow.addView(
                createFilterPill(t.name, sel) {
                    StormThemeEngine.setTheme(t.id, this, syncDefaultAccent = false)
                    savePersistedAppState()
                    rebuildAdaptiveShell()
                },
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(34f)).apply { rightMargin = dp(6f) }
            )
        }
        col.addView(HorizontalScrollView(this).apply { isHorizontalScrollBarEnabled = false; addView(themeRow) },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(10f) })

        // 2. 13 Accent Colors
        col.addView(createGroupHeader(StormLocalization.tr("settings_accent")))
        val accentRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        for (a in StormThemeEngine.ACCENTS) {
            val sel = StormThemeEngine.currentAccent.id == a.id
            val pill = TextView(this).apply {
                text = a.name
                gravity = Gravity.CENTER
                setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(11.5f))
                setPadding(dp(12f), 0, dp(12f), 0)
                StormThemeEngine.applyFont(this, bold = true)
                background = StormThemeEngine.createAccentButtonDrawable(this@MainActivity, a.color, 14f)
                setTextColor(StormThemeEngine.contrastTextForFill(a.color))
                alpha = if (sel) 1.0f else 0.72f
                setOnClickListener {
                    StormThemeEngine.setAccent(a.id, this@MainActivity)
                    savePersistedAppState()
                    rebuildAdaptiveShell()
                }
            }
            accentRow.addView(pill, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(34f)).apply { rightMargin = dp(6f) })
        }
        col.addView(HorizontalScrollView(this).apply { isHorizontalScrollBarEnabled = false; addView(accentRow) },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(10f) })

        // 3. 10 Fonts
        col.addView(createGroupHeader(StormLocalization.tr("settings_font")))
        val fontRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        for (f in StormThemeEngine.FONTS) {
            val sel = StormThemeEngine.currentFont.id == f.id
            fontRow.addView(
                createFilterPill(f.name, sel) {
                    StormThemeEngine.setFont(f.id, this)
                    savePersistedAppState()
                    rebuildAdaptiveShell()
                },
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(34f)).apply { rightMargin = dp(6f) }
            )
        }
        col.addView(HorizontalScrollView(this).apply { isHorizontalScrollBarEnabled = false; addView(fontRow) },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(10f) })

        // 4. 11 Languages
        col.addView(createGroupHeader(StormLocalization.tr("settings_language")))
        val langRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        for (l in StormLocalization.LANGUAGES) {
            val sel = StormLocalization.currentLanguage == l.code
            langRow.addView(
                createFilterPill("${l.nativeName} (${l.code.uppercase(Locale.ROOT)})", sel) {
                    StormLocalization.setLanguage(l.code, this)
                    savePersistedAppState()
                    rebuildAdaptiveShell()
                },
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(34f)).apply { rightMargin = dp(6f) }
            )
        }
        col.addView(HorizontalScrollView(this).apply { isHorizontalScrollBarEnabled = false; addView(langRow) },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(10f) })

        // 5. UI Scale (85% - 130%)
        col.addView(createGroupHeader("Масштаб интерфейса (85% - 130%)"))
        val scaleRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        for (sc in listOf(0.85f, 0.95f, 1.00f, 1.10f, 1.20f, 1.30f)) {
            val sel = kotlin.math.abs(uiScale - sc) < 0.02f
            scaleRow.addView(
                createFilterPill("${(sc * 100).roundToInt()}%", sel) {
                    uiScale = sc
                    savePersistedAppState()
                    rebuildAdaptiveShell()
                },
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(34f)).apply { rightMargin = dp(6f) }
            )
        }
        col.addView(HorizontalScrollView(this).apply { isHorizontalScrollBarEnabled = false; addView(scaleRow) },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(12f) })

        // 6. Emulation & Touch Overlay Preferences
        col.addView(createGroupHeader(StormLocalization.tr("touch_overlay_title")))
        val prefsCard = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = StormThemeEngine.createVolumetricCardDrawable(this@MainActivity, cornerDp = 14f)
            setPadding(dp(12f), dp(10f), dp(12f), dp(10f))
        }
        prefsCard.addView(buildSettingToggleRow(StormLocalization.tr("settings_fps_hud"), emuPrefs.getBoolean("show_fps_hud", true)) {
            emuPrefs.edit().putBoolean("show_fps_hud", it).apply()
        })
        prefsCard.addView(buildSettingToggleRow(StormLocalization.tr("settings_integer_scale"), emuPrefs.getBoolean("integer_scale", false)) {
            emuPrefs.edit().putBoolean("integer_scale", it).putInt("aspect_mode", if (it) NativeLibretroBridge.ASPECT_INTEGER else NativeLibretroBridge.ASPECT_CORE_FIT).apply()
        })

        val touchOverlaySample = MobileTouchGamepadOverlay(this)
        prefsCard.addView(buildSettingToggleRow(StormLocalization.tr("touch_haptics"), touchOverlaySample.hapticsEnabled) {
            touchOverlaySample.hapticsEnabled = it
            touchOverlaySample.saveLayouts()
        })

        val touchResetBtn = createActionBtn(StormVectorIcons.REFRESH, StormLocalization.tr("touch_reset_layout"), accentFilled = false) {
            touchOverlaySample.resetAllLayouts()
            Toast.makeText(this, StormLocalization.tr("touch_reset_layout"), Toast.LENGTH_SHORT).show()
        }
        prefsCard.addView(touchResetBtn, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(36f)).apply { topMargin = dp(6f) })
        col.addView(prefsCard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(12f) })

        // 7. Connected Gamepads & Hotkey Profiles
        col.addView(createGroupHeader(StormLocalization.tr("controllers_title")))
        val padCard = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = StormThemeEngine.createVolumetricCardDrawable(this@MainActivity, cornerDp = 14f)
            setPadding(dp(12f), dp(10f), dp(12f), dp(10f))
        }
        connectedControllers = MobileControllerProfileManager.enumerateConnectedControllers(this)
        if (connectedControllers.isEmpty()) {
            val noneTv = TextView(this).apply {
                text = StormLocalization.tr("controllers_none")
                setTextColor(pal.textSecondary)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(12f))
            }
            padCard.addView(noneTv)
        } else {
            for (dev in connectedControllers) {
                val devRow = LinearLayout(this).apply {
                    orientation = LinearLayout.HORIZONTAL
                    gravity = Gravity.CENTER_VERTICAL
                    setPadding(0, dp(4f), 0, dp(4f))
                }
                val devInfo = TextView(this).apply {
                    text = "P${dev.playerPort + 1}: ${dev.name} (${dev.controllerType} • ${dev.vendorProductHex})"
                    setTextColor(pal.textPrimary)
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(12f))
                    StormThemeEngine.applyFont(this, bold = true)
                    layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply { rightMargin = dp(8f) }
                }
                val cfgBtn = createActionBtn(StormVectorIcons.GAMEPAD, StormLocalization.tr("controllers_hotkeys"), accentFilled = false) {
                    showControllerProfileDialog(dev)
                }
                devRow.addView(devInfo)
                devRow.addView(cfgBtn)
                padCard.addView(devRow)
            }
        }
        padCard.addView(
            createActionBtn(StormVectorIcons.REFRESH, StormLocalization.tr("controllers_refresh"), accentFilled = false) {
                connectedControllers = MobileControllerProfileManager.enumerateConnectedControllers(this)
                renderActiveTabContent()
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(36f)).apply { topMargin = dp(8f) }
        )
        col.addView(padCard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(12f) })

        // 8. Save Sync & Storage Root Info
        col.addView(createGroupHeader(StormLocalization.tr("settings_storage_root")))
        val storageCard = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = StormThemeEngine.createVolumetricCardDrawable(this@MainActivity, cornerDp = 14f)
            setPadding(dp(12f), dp(10f), dp(12f), dp(10f))
        }
        val srmCount = File(stormRoot, "saves").listFiles()?.count { it.extension.equals("srm", true) } ?: 0
        val stateCount = File(stormRoot, "states").listFiles()?.count { it.name.contains(".state") && !it.name.endsWith(".png") } ?: 0
        val storageTv = TextView(this).apply {
            text = "Путь: ${stormRoot.absolutePath}\nSRAM (.srm): $srmCount • Быстрые слоты (.state): $stateCount"
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(12f))
        }
        storageCard.addView(storageTv)
        col.addView(storageCard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(12f) })

        // 9. Проверить обновления (AutoUpdateHelper) & О программе (STORM RETROARCH 2.4.0)
        val bottomBtns = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        bottomBtns.addView(
            createActionBtn(StormVectorIcons.CLOUD, StormLocalization.tr("settings_check_updates"), accentFilled = true) {
                checkAndInstallAppUpdate()
            },
            LinearLayout.LayoutParams(0, dp(42f), 1f).apply { rightMargin = dp(6f) }
        )
        bottomBtns.addView(
            createActionBtn(StormVectorIcons.SHIELD, "О программе", accentFilled = false) {
                showAboutDialog()
            },
            LinearLayout.LayoutParams(0, dp(42f), 1f)
        )
        col.addView(bottomBtns)

        scroll.addView(col)
        return scroll
    }

    private fun buildSettingToggleRow(label: String, checked: Boolean, onToggle: (Boolean) -> Unit): View {
        val pal = StormThemeEngine.currentTheme
        return LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(0, dp(3f), 0, dp(3f))
            val cb = CheckBox(this@MainActivity).apply {
                isChecked = checked
                setOnCheckedChangeListener { _, isChecked -> onToggle(isChecked) }
            }
            val tv = TextView(this@MainActivity).apply {
                text = label
                setTextColor(pal.textPrimary)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(12f))
            }
            addView(cb)
            addView(tv)
        }
    }

    private fun showControllerProfileDialog(dev: MobileControllerDevice) {
        val pal = StormThemeEngine.currentTheme
        val profile = MobileControllerProfileManager.getOrCreateProfile(dev, this)

        val dlg = Dialog(this)
        dlg.requestWindowFeature(Window.FEATURE_NO_TITLE)
        dlg.window?.setBackgroundDrawable(ColorDrawable(Color.TRANSPARENT))
        attachEscDismiss(dlg)

        var waitingHotkeyAction: String? = null
        var waitingStatusTv: TextView? = null

        val card = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = StormThemeEngine.createVolumetricCardDrawable(this@MainActivity, cornerDp = 18f)
            setPadding(dp(16f), dp(16f), dp(16f), dp(16f))
        }

        val title = TextView(this).apply {
            text = "${dev.name} (${dev.controllerType})"
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(14.5f))
            StormThemeEngine.applyFont(this, bold = true)
        }
        card.addView(title)

        val hintTv = TextView(this).apply {
            text = "Нажмите на действие, затем нажмите кнопку на геймпаде для назначения:"
            setTextColor(pal.textSecondary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(11.5f))
        }
        waitingStatusTv = hintTv
        card.addView(hintTv, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(4f); bottomMargin = dp(8f) })

        val rowsContainer = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }

        fun rebuildBindingRows() {
            rowsContainer.removeAllViews()
            for (action in MobileControllerProfileManager.HOTKEY_ACTIONS) {
                val curCode = profile.hotkeyBindings[action] ?: 0
                val r = LinearLayout(this).apply {
                    orientation = LinearLayout.HORIZONTAL
                    gravity = Gravity.CENTER_VERTICAL
                    setPadding(0, dp(4f), 0, dp(4f))
                }
                val lbl = TextView(this).apply {
                    text = action
                    setTextColor(pal.textPrimary)
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(12f))
                    layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                }
                val keyChip = createFilterPill(MobileControllerProfileManager.keyCodeToDisplayName(curCode), waitingHotkeyAction == action) {
                    waitingHotkeyAction = action
                    waitingStatusTv?.text = "Нажмите кнопку на геймпаде для: $action..."
                    rebuildBindingRows()
                }
                r.addView(lbl)
                r.addView(keyChip, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(32f)))
                rowsContainer.addView(r)
            }
        }
        rebuildBindingRows()

        dlg.setOnKeyListener { d, keyCode, event ->
            if (event.action == KeyEvent.ACTION_DOWN) {
                val pending = waitingHotkeyAction
                if (pending != null && keyCode != KeyEvent.KEYCODE_BACK && keyCode != KeyEvent.KEYCODE_ESCAPE) {
                    profile.hotkeyBindings[pending] = keyCode
                    waitingHotkeyAction = null
                    waitingStatusTv?.text = "Назначено: $pending -> ${MobileControllerProfileManager.keyCodeToDisplayName(keyCode)}"
                    rebuildBindingRows()
                    return@setOnKeyListener true
                }
                if (keyCode == KeyEvent.KEYCODE_ESCAPE || keyCode == KeyEvent.KEYCODE_BACK) {
                    d.dismiss()
                    return@setOnKeyListener true
                }
            }
            false
        }

        card.addView(ScrollView(this).apply {
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(230f))
            addView(rowsContainer)
        })

        val footer = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        footer.addView(
            createActionBtn(StormVectorIcons.REFRESH, StormLocalization.tr("controllers_reset_profile"), accentFilled = false) {
                MobileControllerProfileManager.resetProfileToDefaults(dev, this)
                dlg.dismiss()
                showControllerProfileDialog(dev)
            },
            LinearLayout.LayoutParams(0, dp(38f), 1f).apply { rightMargin = dp(6f) }
        )
        footer.addView(
            createActionBtn(StormVectorIcons.SAVE, StormLocalization.tr("controllers_save_profile"), accentFilled = true) {
                MobileControllerProfileManager.saveProfile(profile, this)
                Toast.makeText(this, StormLocalization.tr("controllers_save_profile"), Toast.LENGTH_SHORT).show()
                dlg.dismiss()
            },
            LinearLayout.LayoutParams(0, dp(38f), 1f)
        )
        card.addView(footer, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(10f) })

        dlg.setContentView(
            card,
            ViewGroup.LayoutParams((resources.displayMetrics.widthPixels * 0.90f).roundToInt(), ViewGroup.LayoutParams.WRAP_CONTENT)
        )
        dlg.show()
    }

    private fun checkAndInstallAppUpdate() {
        Toast.makeText(this, StormLocalization.tr("update_checking"), Toast.LENGTH_SHORT).show()
        AutoUpdateHelper.checkForUpdateAsync(APP_VERSION) { info ->
            if (!info.updateAvailable) {
                Toast.makeText(this, "${StormLocalization.tr("update_up_to_date")} ($APP_VERSION)", Toast.LENGTH_LONG).show()
                return@checkForUpdateAsync
            }

            val pal = StormThemeEngine.currentTheme
            val dlg = Dialog(this)
            dlg.requestWindowFeature(Window.FEATURE_NO_TITLE)
            dlg.window?.setBackgroundDrawable(ColorDrawable(Color.TRANSPARENT))
            attachEscDismiss(dlg)

            val card = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                background = StormThemeEngine.createVolumetricCardDrawable(this@MainActivity, cornerDp = 18f)
                setPadding(dp(18f), dp(18f), dp(18f), dp(18f))
            }
            val title = TextView(this).apply {
                text = "${StormLocalization.tr("update_available")}: ${info.latestVersion}"
                setTextColor(pal.textPrimary)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(15.5f))
                StormThemeEngine.applyFont(this, bold = true)
            }
            val notes = TextView(this).apply {
                text = info.releaseNotes.ifBlank { "Доступна новая сборка STORM RETROARCH ${info.latestVersion}." }
                setTextColor(pal.textSecondary)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(12f))
            }
            card.addView(title)
            card.addView(ScrollView(this).apply {
                layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(160f)).apply {
                    topMargin = dp(8f)
                    bottomMargin = dp(12f)
                }
                addView(notes)
            })

            val btns = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
            btns.addView(
                createActionBtn(StormVectorIcons.CLOSE, StormLocalization.tr("btn_cancel"), accentFilled = false) {
                    dlg.dismiss()
                },
                LinearLayout.LayoutParams(0, dp(40f), 1f).apply { rightMargin = dp(6f) }
            )
            btns.addView(
                createActionBtn(StormVectorIcons.DOWNLOAD, "Обновить", accentFilled = true) {
                    dlg.dismiss()
                    if (info.apkDownloadUrl.isNotBlank()) {
                        showProgressOperationDialog("STORM RETROARCH ${info.latestVersion}", "Загрузка APK...") { upd, close ->
                            AutoUpdateHelper.downloadUpdateApkAsync(
                                context = this,
                                apkUrl = info.apkDownloadUrl,
                                versionLabel = info.latestVersion,
                                onProgress = { pct, msg -> upd(pct, msg) },
                                onDone = { ok, apkFile, msg ->
                                    close()
                                    if (ok && apkFile != null && apkFile.exists()) {
                                        promptInstallApk(apkFile)
                                    } else {
                                        Toast.makeText(this, msg, Toast.LENGTH_LONG).show()
                                    }
                                }
                            )
                        }
                    } else {
                        try {
                            startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(info.releasePageUrl)))
                        } catch (_: Throwable) {
                        }
                    }
                },
                LinearLayout.LayoutParams(0, dp(40f), 1f)
            )
            card.addView(btns)

            dlg.setContentView(
                card,
                ViewGroup.LayoutParams((resources.displayMetrics.widthPixels * 0.90f).roundToInt(), ViewGroup.LayoutParams.WRAP_CONTENT)
            )
            dlg.show()
        }
    }

    private fun promptInstallApk(apkFile: File) {
        try {
            val uri = StormApkFileProvider.getUriForFile(this, apkFile)
            val intent = Intent(Intent.ACTION_VIEW).apply {
                setDataAndType(uri, "application/vnd.android.package-archive")
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
            }
            startActivity(intent)
        } catch (t: Throwable) {
            Toast.makeText(this, "${StormLocalization.tr("update_ready_install")}: ${apkFile.absolutePath}", Toast.LENGTH_LONG).show()
        }
    }

    private fun showAboutDialog() {
        val pal = StormThemeEngine.currentTheme
        val accent = StormThemeEngine.activeAccentColor

        val dlg = Dialog(this)
        dlg.requestWindowFeature(Window.FEATURE_NO_TITLE)
        dlg.window?.setBackgroundDrawable(ColorDrawable(Color.TRANSPARENT))
        attachEscDismiss(dlg)

        val card = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = StormThemeEngine.createVolumetricCardDrawable(this@MainActivity, cornerDp = 18f)
            setPadding(dp(20f), dp(20f), dp(20f), dp(20f))
        }
        val title = TextView(this).apply {
            text = "STORM RETROARCH $APP_VERSION"
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(17f))
            StormThemeEngine.applyFont(this, bold = true)
        }
        val subtitle = TextView(this).apply {
            text = "STORM AURORA FLUID • Android Mobile & Handheld Studio"
            setTextColor(accent)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(12f))
            StormThemeEngine.applyFont(this, bold = true)
        }
        val body = TextView(this).apply {
            text = "• 360 ядер Libretro и 60+ платформ\n" +
                "• 26 800+ читов (.cht) и живой сканер памяти RAM\n" +
                "• Региональные обложки (JP / US / EU / WORLD) и MOD - M. RUS\n" +
                "• Динамический сенсорный геймпад и Samsung Game Booster\n" +
                "• Рамки HSM Mega Bezel и Duimon\n" +
                "• Корневая папка: /storage/emulated/0/STORM RETROARCH"
            setTextColor(pal.textSecondary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(12.5f))
        }
        card.addView(title)
        card.addView(subtitle, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(3f); bottomMargin = dp(10f) })
        card.addView(body, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(14f) })
        card.addView(
            createActionBtn(StormVectorIcons.CLOSE, StormLocalization.tr("btn_close"), accentFilled = true) {
                dlg.dismiss()
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(40f))
        )

        dlg.setContentView(
            card,
            ViewGroup.LayoutParams((resources.displayMetrics.widthPixels * 0.88f).roundToInt(), ViewGroup.LayoutParams.WRAP_CONTENT)
        )
        dlg.show()
    }

    // === Folder Picker, Direct Path Browser & SAF Activity Results ===

    private fun showAddFolderChooserDialog() {
        val pal = StormThemeEngine.currentTheme
        val dlg = Dialog(this)
        dlg.requestWindowFeature(Window.FEATURE_NO_TITLE)
        dlg.window?.setBackgroundDrawable(ColorDrawable(Color.TRANSPARENT))
        attachEscDismiss(dlg)

        var currentBrowseDir = Environment.getExternalStorageDirectory()

        val card = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = StormThemeEngine.createVolumetricCardDrawable(this@MainActivity, cornerDp = 18f)
            setPadding(dp(16f), dp(16f), dp(16f), dp(16f))
        }
        val title = TextView(this).apply {
            text = StormLocalization.tr("add_rom_folder")
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(15f))
            StormThemeEngine.applyFont(this, bold = true)
        }
        val pathLabel = TextView(this).apply {
            text = currentBrowseDir.absolutePath
            setTextColor(pal.textSecondary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(11.5f))
        }
        card.addView(title)
        card.addView(pathLabel, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(3f); bottomMargin = dp(8f) })

        val dirListCol = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }

        fun populateDirs() {
            pathLabel.text = currentBrowseDir.absolutePath
            dirListCol.removeAllViews()
            currentBrowseDir.parentFile?.let { parent ->
                if (parent.canRead()) {
                    dirListCol.addView(createActionBtn(StormVectorIcons.FOLDER, ".. (Вверх)", accentFilled = false) {
                        currentBrowseDir = parent
                        populateDirs()
                    }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(36f)).apply { bottomMargin = dp(5f) })
                }
            }
            val subDirs = currentBrowseDir.listFiles { f -> f.isDirectory && !f.name.startsWith(".") }
                ?.sortedBy { it.name.lowercase(Locale.ROOT) } ?: emptyList()
            for (sub in subDirs) {
                dirListCol.addView(createActionBtn(StormVectorIcons.FOLDER, sub.name, accentFilled = false) {
                    currentBrowseDir = sub
                    populateDirs()
                }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(36f)).apply { bottomMargin = dp(5f) })
            }
        }
        populateDirs()

        card.addView(ScrollView(this).apply {
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(230f)).apply { bottomMargin = dp(10f) }
            addView(dirListCol)
        })

        val footer = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        footer.addView(
            createActionBtn(StormVectorIcons.SEARCH, "Системный выбор", accentFilled = false) {
                dlg.dismiss()
                try {
                    startActivityForResult(Intent(Intent.ACTION_OPEN_DOCUMENT_TREE), REQ_PICK_ROM_FOLDER)
                } catch (_: Throwable) {
                }
            },
            LinearLayout.LayoutParams(0, dp(40f), 1f).apply { rightMargin = dp(6f) }
        )
        footer.addView(
            createActionBtn(StormVectorIcons.CHECK, "Выбрать эту папку", accentFilled = true) {
                customRomDirs.add(currentBrowseDir.absolutePath)
                savePersistedAppState()
                dlg.dismiss()
                triggerLibraryScan(autoFetchMissingCovers = false)
            },
            LinearLayout.LayoutParams(0, dp(40f), 1f)
        )
        card.addView(footer)

        dlg.setContentView(
            card,
            ViewGroup.LayoutParams((resources.displayMetrics.widthPixels * 0.92f).roundToInt(), ViewGroup.LayoutParams.WRAP_CONTENT)
        )
        dlg.show()
    }

    private fun launchSafFilePicker(reqCode: Int) {
        try {
            val intent = Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
                addCategory(Intent.CATEGORY_OPENABLE)
                type = "*/*"
            }
            startActivityForResult(intent, reqCode)
        } catch (_: Throwable) {
            Toast.makeText(this, "Файловый диалог недоступен", Toast.LENGTH_SHORT).show()
        }
    }

    @Deprecated("Handled via standard Activity API")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode == REQ_MANAGE_STORAGE) {
            initializeStormDirectoryTree()
            triggerLibraryScan(autoFetchMissingCovers = false)
            renderActiveTabContent()
            return
        }
        if (resultCode != RESULT_OK || data == null) return
        val uri = data.data ?: return

        when (requestCode) {
            REQ_PICK_ROM_FOLDER -> {
                val resolved = resolveTreeUriToFilePath(uri)
                if (resolved != null && File(resolved).exists()) {
                    customRomDirs.add(resolved)
                    savePersistedAppState()
                    triggerLibraryScan(autoFetchMissingCovers = false)
                }
            }
            REQ_PICK_ROM_FILE -> {
                importPickedRomUri(uri)
            }
            REQ_PICK_BIOS_FILE -> {
                val ok = MobileCheatAndBiosManager.importBiosFile(this, uri, pendingBiosTarget)
                pendingBiosTarget = null
                Toast.makeText(this, if (ok) StormLocalization.tr("bios_present") else "Ошибка импорта BIOS", Toast.LENGTH_SHORT).show()
                renderActiveTabContent()
            }
        }
    }

    private fun resolveTreeUriToFilePath(treeUri: Uri): String? {
        return try {
            val docId = DocumentsContract.getTreeDocumentId(treeUri)
            val split = docId.split(":")
            if (split.size >= 2) {
                val vol = split[0]
                val rel = split[1]
                if (vol.equals("primary", ignoreCase = true)) {
                    "${Environment.getExternalStorageDirectory().absolutePath}/$rel"
                } else {
                    "/storage/$vol/$rel"
                }
            } else null
        } catch (_: Throwable) {
            null
        }
    }

    private fun importPickedRomUri(uri: Uri) {
        bgExecutor.execute {
            try {
                var displayName = "imported_game.bin"
                contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { c ->
                    if (c.moveToFirst()) {
                        val idx = c.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                        if (idx >= 0) displayName = c.getString(idx) ?: displayName
                    }
                }
                val dstDir = File(stormRoot, "roms")
                dstDir.mkdirs()
                val dstFile = File(dstDir, displayName)
                contentResolver.openInputStream(uri)?.use { inp ->
                    FileOutputStream(dstFile).use { out ->
                        inp.copyTo(out, 32 * 1024)
                    }
                }
                mainHandler.post {
                    Toast.makeText(this, "Импортирован ROM: $displayName", Toast.LENGTH_SHORT).show()
                    triggerLibraryScan(autoFetchMissingCovers = true)
                }
            } catch (t: Throwable) {
                mainHandler.post {
                    Toast.makeText(this, "Не удалось импортировать файл: ${t.message}", Toast.LENGTH_LONG).show()
                }
            }
        }
    }

    // === Reusable Dialogs & Single-Icon UI Helpers ===

    private fun showQuickLanguageDialog() {
        val pal = StormThemeEngine.currentTheme
        val dlg = Dialog(this)
        dlg.requestWindowFeature(Window.FEATURE_NO_TITLE)
        dlg.window?.setBackgroundDrawable(ColorDrawable(Color.TRANSPARENT))
        attachEscDismiss(dlg)

        val card = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = StormThemeEngine.createVolumetricCardDrawable(this@MainActivity, cornerDp = 18f)
            setPadding(dp(16f), dp(16f), dp(16f), dp(16f))
        }
        val hdr = TextView(this).apply {
            text = StormLocalization.tr("settings_language")
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(14.5f))
            StormThemeEngine.applyFont(this, bold = true)
        }
        card.addView(hdr, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(8f) })

        val grid = GridLayout(this).apply { columnCount = 2 }
        for (lang in StormLocalization.LANGUAGES) {
            val sel = StormLocalization.currentLanguage == lang.code
            val pill = createFilterPill("${lang.nativeName} (${lang.code.uppercase(Locale.ROOT)})", sel) {
                StormLocalization.setLanguage(lang.code, this)
                savePersistedAppState()
                dlg.dismiss()
                rebuildAdaptiveShell()
            }
            grid.addView(pill, GridLayout.LayoutParams().apply {
                width = 0
                height = dp(36f)
                columnSpec = GridLayout.spec(GridLayout.UNDEFINED, 1f)
                setMargins(dp(4f), dp(4f), dp(4f), dp(4f))
            })
        }
        card.addView(grid)
        dlg.setContentView(
            card,
            ViewGroup.LayoutParams((resources.displayMetrics.widthPixels * 0.90f).roundToInt(), ViewGroup.LayoutParams.WRAP_CONTENT)
        )
        dlg.show()
    }

    private fun showProgressOperationDialog(
        titleText: String,
        initialStatus: String,
        starter: (updateProgress: (Int, String) -> Unit, closeDialog: () -> Unit) -> Unit
    ) {
        val pal = StormThemeEngine.currentTheme
        val dlg = Dialog(this)
        dlg.requestWindowFeature(Window.FEATURE_NO_TITLE)
        dlg.setCancelable(false)
        dlg.window?.setBackgroundDrawable(ColorDrawable(Color.TRANSPARENT))

        val card = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = StormThemeEngine.createVolumetricCardDrawable(this@MainActivity, cornerDp = 16f)
            setPadding(dp(18f), dp(18f), dp(18f), dp(18f))
        }
        val titleTv = TextView(this).apply {
            text = titleText
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(14.5f))
            StormThemeEngine.applyFont(this, bold = true)
        }
        val statusTv = TextView(this).apply {
            text = initialStatus
            setTextColor(pal.textSecondary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(12f))
        }
        val progress = ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal).apply {
            max = 100
            progress = 2
        }
        card.addView(titleTv)
        card.addView(statusTv, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(6f); bottomMargin = dp(10f) })
        card.addView(progress, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(10f)))

        dlg.setContentView(
            card,
            ViewGroup.LayoutParams((resources.displayMetrics.widthPixels * 0.86f).roundToInt(), ViewGroup.LayoutParams.WRAP_CONTENT)
        )
        dlg.show()

        starter(
            { pct, msg ->
                mainHandler.post {
                    progress.progress = pct.coerceIn(0, 100)
                    statusTv.text = "$msg ($pct%)"
                }
            },
            {
                mainHandler.post {
                    if (dlg.isShowing) dlg.dismiss()
                }
            }
        )
    }

    private fun attachEscDismiss(dlg: Dialog) {
        dlg.setOnKeyListener { d, keyCode, event ->
            if (event.action == KeyEvent.ACTION_DOWN &&
                (keyCode == KeyEvent.KEYCODE_ESCAPE || keyCode == KeyEvent.KEYCODE_BACK)
            ) {
                d.dismiss()
                true
            } else false
        }
    }

    private fun createGroupHeader(textStr: String): TextView {
        return TextView(this).apply {
            text = textStr
            setTextColor(StormThemeEngine.activeAccentColor)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(12.5f))
            StormThemeEngine.applyFont(this, bold = true)
            setPadding(0, dp(2f), 0, dp(6f))
        }
    }

    private fun createStyledEdit(hintStr: String): EditText {
        val pal = StormThemeEngine.currentTheme
        return EditText(this).apply {
            hint = hintStr
            setHintTextColor(pal.textSecondary)
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(12f))
            background = StormThemeEngine.createRecessedInputDrawable(this@MainActivity, cornerDp = 10f)
            setPadding(dp(10f), dp(4f), dp(10f), dp(4f))
            isSingleLine = true
        }
    }

    private fun createMiniTag(label: String, bgColor: Int, fgColor: Int): TextView {
        return TextView(this).apply {
            text = label
            setTextColor(fgColor)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(9.5f))
            setPadding(dp(7f), dp(2f), dp(7f), dp(2f))
            StormThemeEngine.applyFont(this, bold = true)
            background = GradientDrawable().apply {
                shape = GradientDrawable.RECTANGLE
                cornerRadius = dpF(6f)
                setColor(bgColor)
            }
        }
    }

    private fun createFilterPill(label: String, selected: Boolean, onClick: () -> Unit): TextView {
        val pal = StormThemeEngine.currentTheme
        val accent = StormThemeEngine.activeAccentColor
        return TextView(this).apply {
            text = label
            gravity = Gravity.CENTER
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(11.5f))
            setPadding(dp(12f), 0, dp(12f), 0)
            maxLines = 1
            StormThemeEngine.applyFont(this, bold = true)
            background = StormThemeEngine.createFilterChipDrawable(this@MainActivity, selected, accent, 14f)
            setTextColor(if (selected) StormThemeEngine.contrastTextForFill(accent) else pal.textPrimary)
            StormThemeEngine.attachSpringTouchFeedback(this)
            isFocusable = true
            setOnClickListener { onClick() }
        }
    }

    private fun createIconOnlyBtn(iconName: String, tint: Int, onClick: () -> Unit): LinearLayout {
        return LinearLayout(this).apply {
            gravity = Gravity.CENTER
            background = StormThemeEngine.createSurfaceButtonDrawable(this@MainActivity, 10f)
            StormThemeEngine.attachSpringTouchFeedback(this)
            isClickable = true
            isFocusable = true
            setOnClickListener { onClick() }
            val iv = ImageView(this@MainActivity).apply {
                setImageDrawable(StormVectorIcons.create(this@MainActivity, iconName, tint, 16f))
            }
            addView(iv, LinearLayout.LayoutParams(dp(16f), dp(16f)))
        }
    }

    private fun createActionBtn(iconName: String, label: String, accentFilled: Boolean, onClick: () -> Unit): LinearLayout {
        val pal = StormThemeEngine.currentTheme
        val accent = StormThemeEngine.activeAccentColor
        val fg = if (accentFilled) StormThemeEngine.contrastTextForFill(accent) else pal.textPrimary

        return LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER
            background = if (accentFilled) {
                StormThemeEngine.createAccentButtonDrawable(this@MainActivity, accent, 11f)
            } else {
                StormThemeEngine.createSurfaceButtonDrawable(this@MainActivity, 11f)
            }
            setPadding(dp(10f), dp(4f), dp(10f), dp(4f))
            StormThemeEngine.attachSpringTouchFeedback(this)
            isClickable = true
            isFocusable = true
            setOnClickListener { onClick() }

            val iv = ImageView(this@MainActivity).apply {
                setImageDrawable(StormVectorIcons.create(this@MainActivity, iconName, fg, 15f))
            }
            addView(iv, LinearLayout.LayoutParams(dp(15f), dp(15f)).apply { rightMargin = dp(6f) })

            val tv = TextView(this@MainActivity).apply {
                text = label
                setTextColor(fg)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, sp(11.5f))
                maxLines = 1
                ellipsize = TextUtils.TruncateAt.END
                StormThemeEngine.applyFont(this, bold = true)
            }
            addView(tv)
        }
    }

    private fun dp(v: Float): Int = StormThemeEngine.dpToPx(this, v * uiScale)
    private fun dpF(v: Float): Float = StormThemeEngine.dpToPxF(this, v * uiScale)
    private fun sp(v: Float): Float = v * uiScale
}
