package com.storm.retroarch

import android.app.Activity
import android.app.Dialog
import android.app.GameManager
import android.app.GameState
import android.content.Context
import android.content.SharedPreferences
import android.content.res.ColorStateList
import android.content.res.Configuration
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Color
import android.graphics.drawable.ColorDrawable
import android.graphics.drawable.GradientDrawable
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioTrack
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.os.VibrationEffect
import android.os.Vibrator
import android.text.InputType
import android.util.TypedValue
import android.view.Gravity
import android.view.InputDevice
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.Surface
import android.view.SurfaceHolder
import android.view.SurfaceView
import android.view.View
import android.view.ViewGroup
import android.view.Window
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.view.WindowManager
import android.view.animation.DecelerateInterpolator
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.HorizontalScrollView
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.SeekBar
import android.widget.TextView
import android.widget.Toast
import java.io.File
import java.io.FileOutputStream
import java.io.RandomAccessFile
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder
import java.security.MessageDigest
import java.util.concurrent.Executors
import org.json.JSONArray
import org.json.JSONObject
import java.nio.ByteBuffer
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import kotlin.math.abs
import kotlin.math.pow
import kotlin.math.roundToInt

class EmulationActivity : Activity() {

    companion object {
        const val EXTRA_ROM_PATH = "rom_path"
        const val EXTRA_ROM_NAME = "rom_name"
        const val EXTRA_PLATFORM = "platform"
        const val EXTRA_CORE_SO_PATH = "core_so_path"
        const val EXTRA_CORE_NAME = "core_name"
        const val EXTRA_THEME_ID = "theme_id"
        const val EXTRA_ACCENT_IDX = "accent_idx"
        const val EXTRA_LANG_CODE = "lang_code"

        private const val PREFS_UNIFIED = "storm_retroarch_prefs"
        private const val PREFS_EMU_LEGACY = "storm_retroarch_emu_prefs"

        private const val GYRO_OFF = 0
        private const val GYRO_LEFT_STICK = 1
        private const val GYRO_RIGHT_STICK = 2
    }

    private lateinit var rootLayout: FrameLayout
    private lateinit var surfaceView: SurfaceView
    private lateinit var bezelImageView: ImageView
    private lateinit var touchOverlay: MobileTouchGamepadOverlay
    private lateinit var fpsHudText: TextView

    private var romPath: String = ""
    private var effectiveRomPath: String = ""
    private var romName: String = "Game"
    private var romBaseStem: String = "game"
    private var platformName: String = "SNES"
    private var coreSoPath: String = ""
    private var coreName: String = "Libretro Core"

    private val stormRoot = File(MobileCoreRegistry.STORM_PUBLIC_ROOT)
    private val savesDir = File(stormRoot, "saves")
    private val statesDir = File(stormRoot, "states")
    private val systemDir = File(stormRoot, "system")
    private val screenshotsDir = File(stormRoot, "screenshots")

    private val mainHandler = Handler(Looper.getMainLooper())

    @Volatile private var emuRunning = false
    @Volatile private var emuPaused = false
    @Volatile private var coreLoadedOk = false
    @Volatile private var fastForwardMultiplier = 1
    @Volatile private var rewindHeld = false
    @Volatile private var rewindEnabled = true
    @Volatile private var highRefresh120Hz = true
    @Volatile private var audioGainDb = 0f
    @Volatile private var audioGainLinear = 1.0f
    @Volatile private var frameSkip = 0
    @Volatile private var activeSaveSlot = 0

    private var emuThread: Thread? = null
    private var audioTrack: AudioTrack? = null
    private var currentSampleRate = 0

    // Physical controller live state
    private var selectButtonHeld = false
    private var prevMotionBitmask = 0

    // Gyroscope Tilt-to-Stick sensor state
    private var sensorManager: SensorManager? = null
    private var gyroSensor: Sensor? = null
    private var gyroRegistered = false
    private var gyroTiltMode = GYRO_OFF
    private var gyroCalibrated = false
    private var gyroBasePitch = 0f
    private var gyroBaseRoll = 0f
    private var gyroFilteredX = 0f
    private var gyroFilteredY = 0f
    private val rotMatrix = FloatArray(9)
    private val remappedRotMatrix = FloatArray(9)
    private val orientAngles = FloatArray(3)

    // Cheats & RAM Scanner state
    private var gameCheatFile: File? = null
    private val gameCheatEntries = ArrayList<MobileCheatEntry>()
    private var ramScanWidthBytes = 2
    private val frozenRamAddresses = HashSet<Int>()

    // Active quick studio overlay
    private var quickDrawerDialog: Dialog? = null
    private var lastAppliedSurfaceHeight = -1
    private var activeSessionStartElapsedMs = 0L
    private var gameIdExtra: String = ""

    private val gyroSensorListener = object : SensorEventListener {
        override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) {}

        override fun onSensorChanged(event: SensorEvent) {
            if (gyroTiltMode == GYRO_OFF || emuPaused || !coreLoadedOk || !NativeLibretroBridge.isLibraryLoaded) return
            val pitch: Float
            val roll: Float

            if (event.sensor.type == Sensor.TYPE_GAME_ROTATION_VECTOR || event.sensor.type == Sensor.TYPE_ROTATION_VECTOR) {
                SensorManager.getRotationMatrixFromVector(rotMatrix, event.values)
                val dispRot = getDisplayRotationSafe()
                when (dispRot) {
                    Surface.ROTATION_90 -> SensorManager.remapCoordinateSystem(rotMatrix, SensorManager.AXIS_Y, SensorManager.AXIS_MINUS_X, remappedRotMatrix)
                    Surface.ROTATION_180 -> SensorManager.remapCoordinateSystem(rotMatrix, SensorManager.AXIS_MINUS_X, SensorManager.AXIS_MINUS_Y, remappedRotMatrix)
                    Surface.ROTATION_270 -> SensorManager.remapCoordinateSystem(rotMatrix, SensorManager.AXIS_MINUS_Y, SensorManager.AXIS_X, remappedRotMatrix)
                    else -> System.arraycopy(rotMatrix, 0, remappedRotMatrix, 0, 9)
                }
                SensorManager.getOrientation(remappedRotMatrix, orientAngles)
                pitch = orientAngles[1] // Forward / back tilt in radians
                roll = orientAngles[2]  // Left / right tilt in radians
            } else if (event.sensor.type == Sensor.TYPE_ACCELEROMETER && event.values.size >= 3) {
                val dispRot = getDisplayRotationSafe()
                val ax = event.values[0]
                val ay = event.values[1]
                val rawX: Float
                val rawY: Float
                when (dispRot) {
                    Surface.ROTATION_90 -> { rawX = ay; rawY = ax }
                    Surface.ROTATION_270 -> { rawX = -ay; rawY = -ax }
                    Surface.ROTATION_180 -> { rawX = ax; rawY = -ay }
                    else -> { rawX = -ax; rawY = ay }
                }
                roll = (rawX / 9.81f).coerceIn(-1f, 1f) * 0.55f
                pitch = (rawY / 9.81f).coerceIn(-1f, 1f) * 0.55f
            } else {
                return
            }

            if (!gyroCalibrated) {
                gyroBasePitch = pitch
                gyroBaseRoll = roll
                gyroCalibrated = true
            }

            // Full deflection at ~24 degrees (0.42 rad) tilt from comfortable resting posture
            val maxTiltRad = 0.42f
            val targetX = ((roll - gyroBaseRoll) / maxTiltRad).coerceIn(-1f, 1f)
            val targetY = (-(pitch - gyroBasePitch) / maxTiltRad).coerceIn(-1f, 1f)

            gyroFilteredX += (targetX - gyroFilteredX) * 0.34f
            gyroFilteredY += (targetY - gyroFilteredY) * 0.34f

            val deadzone = 0.08f
            val normX = if (abs(gyroFilteredX) < deadzone) 0f else gyroFilteredX
            val normY = if (abs(gyroFilteredY) < deadzone) 0f else gyroFilteredY

            val stickIdx = if (gyroTiltMode == GYRO_RIGHT_STICK) {
                NativeLibretroBridge.RETRO_DEVICE_INDEX_ANALOG_RIGHT
            } else {
                NativeLibretroBridge.RETRO_DEVICE_INDEX_ANALOG_LEFT
            }

            val axisX = (normX * 32767f).roundToInt().coerceIn(-32767, 32767)
            val axisY = (normY * 32767f).roundToInt().coerceIn(-32767, 32767)
            NativeLibretroBridge.nativeSetAnalog(0, stickIdx, NativeLibretroBridge.RETRO_DEVICE_ID_ANALOG_X, axisX)
            NativeLibretroBridge.nativeSetAnalog(0, stickIdx, NativeLibretroBridge.RETRO_DEVICE_ID_ANALOG_Y, axisY)
            touchOverlay.setGyroStickPreview(stickIdx, normX, normY)
        }
    }

    private val fpsHudUpdater = object : Runnable {
        override fun run() {
            if (isFinishing || isDestroyed) return
            if (fpsHudText.visibility == View.VISIBLE && coreLoadedOk) {
                val fps = if (NativeLibretroBridge.isLibraryLoaded) {
                    NativeLibretroBridge.nativeGetMeasuredFps()
                } else 0.0
                val ms = if (fps > 1.0) 1000.0 / fps else 0.0
                val modeTag = when {
                    rewindHeld -> " | REW"
                    fastForwardMultiplier > 1 -> " | ${fastForwardMultiplier}X"
                    touchOverlay.isTurboLatchActive -> " | TURBO"
                    else -> ""
                }
                fpsHudText.text = String.format(Locale.US, "%.1f FPS (%.1f ms) | %s%s", fps, ms, coreName, modeTag)
            }
            mainHandler.postDelayed(this, 1000L)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        requestWindowFeature(Window.FEATURE_NO_TITLE)
        super.onCreate(savedInstanceState)

        StormThemeEngine.loadFromPrefs(this)
        StormLocalization.loadFromPrefs(this)

        intent?.let { inp ->
            romPath = inp.getStringExtra(EXTRA_ROM_PATH) ?: ""
            gameIdExtra = inp.getStringExtra("game_id") ?: ""
            romName = inp.getStringExtra(EXTRA_ROM_NAME)?.takeIf { it.isNotBlank() }
                ?: File(romPath).nameWithoutExtension.ifBlank { "Game" }
            platformName = inp.getStringExtra(EXTRA_PLATFORM) ?: "SNES"
            coreSoPath = inp.getStringExtra(EXTRA_CORE_SO_PATH) ?: ""
            coreName = inp.getStringExtra(EXTRA_CORE_NAME) ?: "Libretro"

            inp.getStringExtra(EXTRA_THEME_ID)?.takeIf { it.isNotBlank() }?.let {
                StormThemeEngine.setTheme(it, this)
            }
            val accentIdx = inp.getIntExtra(EXTRA_ACCENT_IDX, -1)
            if (accentIdx in StormThemeEngine.ACCENTS.indices) {
                StormThemeEngine.setAccent(StormThemeEngine.ACCENTS[accentIdx].id, this)
            }
            inp.getStringExtra(EXTRA_LANG_CODE)?.takeIf { it.isNotBlank() }?.let {
                StormLocalization.setLanguage(it, this)
            }
        }

        loadUnifiedSharedSettings()
        ensureDirectories()

        val rawFile = File(romPath)
        romBaseStem = rawFile.nameWithoutExtension.ifBlank { sanitizeName(romName) }
        effectiveRomPath = if (rawFile.exists()) {
            MobileLibraryAndCoverEngine.applyIpsOrBpsIfPresent(this, rawFile).absolutePath
        } else {
            romPath
        }

        sensorManager = getSystemService(Context.SENSOR_SERVICE) as? SensorManager
        gyroSensor = sensorManager?.getDefaultSensor(Sensor.TYPE_GAME_ROTATION_VECTOR)
            ?: sensorManager?.getDefaultSensor(Sensor.TYPE_ROTATION_VECTOR)
            ?: sensorManager?.getDefaultSensor(Sensor.TYPE_ACCELEROMETER)

        configureImmersiveAndRefreshRate()
        setAndroidGameModeState(isInterruptible = false)
        buildActivityLayout()
        detectInitialControllerPresence()
        updateGyroRegistration()
        startEmulationEngine()
        mainHandler.postDelayed(fpsHudUpdater, 500L)
    }

    // === Unified SharedPreferences Reader & Sync ("storm_retroarch_prefs" + Legacy Fallback) ===

    private fun unifiedPrefs(): SharedPreferences = getSharedPreferences(PREFS_UNIFIED, Context.MODE_PRIVATE)
    private fun legacyPrefs(): SharedPreferences = getSharedPreferences(PREFS_EMU_LEGACY, Context.MODE_PRIVATE)

    private fun readPrefBool(primaryKey: String, legacyKey: String, defaultVal: Boolean): Boolean {
        val up = unifiedPrefs()
        if (up.contains(primaryKey)) {
            val raw = up.all[primaryKey]
            return when (raw) {
                is Boolean -> raw
                is String -> raw.equals("true", ignoreCase = true) || raw == "1"
                is Number -> raw.toInt() != 0
                else -> defaultVal
            }
        }
        val lp = legacyPrefs()
        if (lp.contains(legacyKey)) return lp.getBoolean(legacyKey, defaultVal)
        return defaultVal
    }

    private fun readPrefFloat(primaryKey: String, defaultVal: Float): Float {
        val raw = unifiedPrefs().all[primaryKey] ?: return defaultVal
        return when (raw) {
            is Float -> raw
            is Double -> raw.toFloat()
            is Int -> raw.toFloat()
            is Long -> raw.toFloat()
            is String -> raw.toFloatOrNull() ?: defaultVal
            else -> defaultVal
        }
    }

    private fun readPrefInt(primaryKey: String, defaultVal: Int): Int {
        val raw = unifiedPrefs().all[primaryKey] ?: return defaultVal
        return when (raw) {
            is Int -> raw
            is Long -> raw.toInt()
            is Float -> raw.roundToInt()
            is Double -> raw.roundToInt()
            is String -> raw.toIntOrNull() ?: defaultVal
            else -> defaultVal
        }
    }

    private fun resolveGyroModeFromPrefs(): Int {
        val raw = unifiedPrefs().all["gyroTiltStick"] ?: return GYRO_OFF
        return when (raw) {
            is Boolean -> if (raw) GYRO_LEFT_STICK else GYRO_OFF
            is Number -> when (raw.toInt()) {
                1 -> GYRO_LEFT_STICK
                2 -> GYRO_RIGHT_STICK
                else -> GYRO_OFF
            }
            is String -> {
                val s = raw.trim().lowercase(Locale.ROOT)
                when {
                    s == "right" || s == "r" || s.contains("прав") -> GYRO_RIGHT_STICK
                    s == "left" || s == "l" || s == "true" || s == "on" || s.contains("лев") -> GYRO_LEFT_STICK
                    else -> GYRO_OFF
                }
            }
            else -> GYRO_OFF
        }
    }

    private fun persistGyroMode(mode: Int) {
        gyroTiltMode = mode
        gyroCalibrated = false
        gyroFilteredX = 0f
        gyroFilteredY = 0f
        val strVal = when (mode) {
            GYRO_LEFT_STICK -> "left"
            GYRO_RIGHT_STICK -> "right"
            else -> "off"
        }
        unifiedPrefs().edit().putString("gyroTiltStick", strVal).apply()
        if (mode == GYRO_OFF && coreLoadedOk && NativeLibretroBridge.isLibraryLoaded) {
            NativeLibretroBridge.nativeSetAnalog(0, 0, NativeLibretroBridge.RETRO_DEVICE_ID_ANALOG_X, 0)
            NativeLibretroBridge.nativeSetAnalog(0, 0, NativeLibretroBridge.RETRO_DEVICE_ID_ANALOG_Y, 0)
            NativeLibretroBridge.nativeSetAnalog(0, 1, NativeLibretroBridge.RETRO_DEVICE_ID_ANALOG_X, 0)
            NativeLibretroBridge.nativeSetAnalog(0, 1, NativeLibretroBridge.RETRO_DEVICE_ID_ANALOG_Y, 0)
        }
        if (::touchOverlay.isInitialized) {
            touchOverlay.setGyroStickPreview(MobileTouchGamepadOverlay.STICK_LEFT, 0f, 0f)
            touchOverlay.setGyroStickPreview(MobileTouchGamepadOverlay.STICK_RIGHT, 0f, 0f)
        }
        updateGyroRegistration()
    }

    private fun resolveInitialShaderMode(): Int {
        val up = unifiedPrefs()
        val raw = up.all["shaderPreset"]
        if (raw != null) {
            when (raw) {
                is Number -> return raw.toInt().coerceIn(0, 3)
                is String -> {
                    val s = raw.trim().lowercase(Locale.ROOT)
                    return when {
                        s.contains("crt") || s.contains("scanline") -> NativeLibretroBridge.SHADER_CRT_SCANLINES
                        s.contains("lcd") || s.contains("grid") -> NativeLibretroBridge.SHADER_LCD_GRID
                        s.contains("bilinear") || s.contains("smooth") -> NativeLibretroBridge.SHADER_BILINEAR_SMOOTH
                        s == "1" -> NativeLibretroBridge.SHADER_BILINEAR_SMOOTH
                        s == "2" -> NativeLibretroBridge.SHADER_CRT_SCANLINES
                        s == "3" -> NativeLibretroBridge.SHADER_LCD_GRID
                        else -> NativeLibretroBridge.SHADER_PIXEL_PERFECT
                    }
                }
            }
        }
        return legacyPrefs().getInt("shader_mode", NativeLibretroBridge.SHADER_PIXEL_PERFECT).coerceIn(0, 3)
    }

    private fun persistShaderMode(mode: Int) {
        val name = when (mode) {
            NativeLibretroBridge.SHADER_BILINEAR_SMOOTH -> "Bilinear"
            NativeLibretroBridge.SHADER_CRT_SCANLINES -> "CRT Scanlines"
            NativeLibretroBridge.SHADER_LCD_GRID -> "LCD Grid"
            else -> "Pixel-Perfect"
        }
        unifiedPrefs().edit().putString("shaderPreset", name).apply()
        legacyPrefs().edit().putInt("shader_mode", mode).apply()
    }

    private fun resolveInitialAspectMode(): Int {
        val up = unifiedPrefs()
        val raw = up.all["aspectRatio"]
        if (raw != null) {
            when (raw) {
                is Number -> return raw.toInt().coerceIn(0, 4)
                is String -> {
                    val s = raw.trim().lowercase(Locale.ROOT)
                    return when {
                        s.contains("4:3") -> NativeLibretroBridge.ASPECT_4_3
                        s.contains("16:9") || s.contains("wide") -> NativeLibretroBridge.ASPECT_16_9
                        s.contains("int") || s.contains("pixel") -> NativeLibretroBridge.ASPECT_INTEGER
                        s.contains("stretch") || s.contains("full") -> NativeLibretroBridge.ASPECT_STRETCH
                        else -> NativeLibretroBridge.ASPECT_CORE_FIT
                    }
                }
            }
        }
        val lp = legacyPrefs()
        return lp.getInt(
            "aspect_mode",
            if (lp.getBoolean("integer_scale", false)) NativeLibretroBridge.ASPECT_INTEGER
            else NativeLibretroBridge.ASPECT_CORE_FIT
        ).coerceIn(0, 4)
    }

    private fun persistAspectMode(mode: Int) {
        val label = when (mode) {
            NativeLibretroBridge.ASPECT_4_3 -> "4:3"
            NativeLibretroBridge.ASPECT_16_9 -> "16:9"
            NativeLibretroBridge.ASPECT_INTEGER -> "Integer"
            NativeLibretroBridge.ASPECT_STRETCH -> "Stretch"
            else -> "Auto"
        }
        unifiedPrefs().edit().putString("aspectRatio", label).apply()
        legacyPrefs().edit()
            .putInt("aspect_mode", mode)
            .putBoolean("integer_scale", mode == NativeLibretroBridge.ASPECT_INTEGER)
            .apply()
    }

    private fun updateAudioGain(db: Float) {
        audioGainDb = db.coerceIn(-24f, 12f)
        audioGainLinear = 10.0.pow((audioGainDb / 20.0)).toFloat()
        try {
            audioTrack?.setVolume(audioGainLinear.coerceIn(0f, 1f))
        } catch (_: Throwable) {
        }
    }

    private fun loadUnifiedSharedSettings() {
        rewindEnabled = readPrefBool("rewindEnabled", "rewind_enabled", true)
        highRefresh120Hz = readPrefBool("highRefresh120Hz", "high_refresh_120hz", true)
        frameSkip = readPrefInt("frameSkip", 0).coerceIn(0, 3)
        updateAudioGain(readPrefFloat("audioGainDb", 0f))
        gyroTiltMode = resolveGyroModeFromPrefs()
    }

    private fun ensureDirectories() {
        for (d in listOf(savesDir, statesDir, systemDir, screenshotsDir)) {
            try {
                if (!d.exists()) d.mkdirs()
            } catch (_: Throwable) {
            }
        }
    }

    private fun sanitizeName(s: String): String {
        return s.replace(Regex("[\\\\/:*?\"<>|]"), "_").trim().ifBlank { "game" }
    }

    private fun srmFile(): File = File(savesDir, "$romBaseStem.srm")

    private fun stateFile(slot: Int): File = File(statesDir, "$romBaseStem.state$slot")

    private fun statePreviewFile(slot: Int): File = File(statesDir, "$romBaseStem.state$slot.png")

    private fun getDisplayRotationSafe(): Int {
        return try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                display?.rotation ?: Surface.ROTATION_0
            } else {
                @Suppress("DEPRECATION")
                windowManager.defaultDisplay?.rotation ?: Surface.ROTATION_0
            }
        } catch (_: Throwable) {
            Surface.ROTATION_0
        }
    }

    private fun configureImmersiveAndRefreshRate() {
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            val lp = window.attributes
            lp.layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES
            window.attributes = lp
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            try {
                @Suppress("DEPRECATION")
                val disp = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) display else windowManager.defaultDisplay
                val modes = disp?.supportedModes
                if (!modes.isNullOrEmpty()) {
                    val activeMode = disp.mode
                    val sameRes = modes.filter {
                        it.physicalWidth == activeMode.physicalWidth && it.physicalHeight == activeMode.physicalHeight
                    }.ifEmpty { modes.toList() }
                    val chosen = if (highRefresh120Hz) {
                        sameRes.maxByOrNull { it.refreshRate }
                    } else {
                        sameRes.minByOrNull { abs(it.refreshRate - 60f) } ?: sameRes.firstOrNull()
                    }
                    if (chosen != null) {
                        val lp = window.attributes
                        lp.preferredDisplayModeId = chosen.modeId
                        window.attributes = lp
                    }
                }
            } catch (_: Throwable) {
            }
        }

        applyImmersiveSystemBars()
    }

    private fun applyImmersiveSystemBars() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            window.setDecorFitsSystemWindows(false)
            window.insetsController?.let { controller ->
                controller.hide(WindowInsets.Type.statusBars() or WindowInsets.Type.navigationBars())
                controller.systemBarsBehavior = WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            }
        } else {
            @Suppress("DEPRECATION")
            window.decorView.systemUiVisibility = (
                View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                    or View.SYSTEM_UI_FLAG_FULLSCREEN
                    or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                    or View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                    or View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                    or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                )
        }
    }

    private fun setAndroidGameModeState(isInterruptible: Boolean) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            try {
                val gm = getSystemService(GameManager::class.java)
                val mode = if (isInterruptible) {
                    GameState.MODE_GAMEPLAY_INTERRUPTIBLE
                } else {
                    GameState.MODE_GAMEPLAY_UNINTERRUPTIBLE
                }
                gm?.setGameState(GameState(false, mode))
            } catch (_: Throwable) {
            }
        }
    }

    private fun updateGyroRegistration() {
        val sm = sensorManager ?: return
        val sensor = gyroSensor ?: return
        val shouldRun = gyroTiltMode != GYRO_OFF && !emuPaused && !isFinishing && !isDestroyed
        if (shouldRun && !gyroRegistered) {
            gyroCalibrated = false
            gyroRegistered = sm.registerListener(gyroSensorListener, sensor, SensorManager.SENSOR_DELAY_GAME)
        } else if (!shouldRun && gyroRegistered) {
            sm.unregisterListener(gyroSensorListener)
            gyroRegistered = false
        }
    }

    private fun syncViewportBoundsWithHandheldDeck() {
        if (!::rootLayout.isInitialized || !::surfaceView.isInitialized || !::touchOverlay.isInitialized) return
        val totalH = rootLayout.height
        val totalW = rootLayout.width
        if (totalH <= 0 || totalW <= 0) return

        val ratio = touchOverlay.getActiveViewportBottomRatio()
        val targetH = if (ratio < 0.99f) {
            (totalH * ratio).roundToInt().coerceAtLeast(120)
        } else {
            ViewGroup.LayoutParams.MATCH_PARENT
        }

        if (targetH != lastAppliedSurfaceHeight) {
            lastAppliedSurfaceHeight = targetH
            val sLp = surfaceView.layoutParams as? FrameLayout.LayoutParams
                ?: FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, targetH)
            sLp.width = ViewGroup.LayoutParams.MATCH_PARENT
            sLp.height = targetH
            sLp.gravity = Gravity.TOP or Gravity.CENTER_HORIZONTAL
            surfaceView.layoutParams = sLp

            val bLp = bezelImageView.layoutParams as? FrameLayout.LayoutParams
            if (bLp != null) {
                bLp.width = ViewGroup.LayoutParams.MATCH_PARENT
                bLp.height = targetH
                bLp.gravity = Gravity.TOP
                bezelImageView.layoutParams = bLp
            }
        }
    }

    override fun onConfigurationChanged(newConfig: Configuration) {
        super.onConfigurationChanged(newConfig)
        gyroCalibrated = false
        lastAppliedSurfaceHeight = -2
        rootLayout.post { syncViewportBoundsWithHandheldDeck() }
    }

    private fun buildActivityLayout() {
        val showFps = readPrefBool("fpsHud", "show_fps_hud", true)

        rootLayout = FrameLayout(this).apply {
            setBackgroundColor(Color.rgb(6, 5, 13))
            keepScreenOn = true
            addOnLayoutChangeListener { _, _, _, _, _, _, _, _, _ ->
                post { syncViewportBoundsWithHandheldDeck() }
            }
        }

        surfaceView = SurfaceView(this).apply {
            holder.addCallback(object : SurfaceHolder.Callback {
                override fun surfaceCreated(holder: SurfaceHolder) {
                    if (NativeLibretroBridge.isLibraryLoaded) {
                        NativeLibretroBridge.nativeSetSurface(holder.surface)
                    }
                }

                override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {
                    if (NativeLibretroBridge.isLibraryLoaded) {
                        NativeLibretroBridge.nativeSetSurface(holder.surface)
                        NativeLibretroBridge.nativeOnSurfaceChanged(width, height)
                    }
                }

                override fun surfaceDestroyed(holder: SurfaceHolder) {
                    if (NativeLibretroBridge.isLibraryLoaded) {
                        NativeLibretroBridge.nativeSetSurface(null)
                    }
                }
            })
            // Forward direct game-viewport touches (NDS/3DS stylus, DOS mouse, lightgun)
            setOnTouchListener { v, ev ->
                if (!NativeLibretroBridge.isLibraryLoaded || !coreLoadedOk) return@setOnTouchListener false
                val w = v.width.coerceAtLeast(1)
                val h = v.height.coerceAtLeast(1)
                val nx = (((ev.x / w) * 2f - 1f).coerceIn(-1f, 1f) * 32767f).roundToInt()
                val ny = (((ev.y / h) * 2f - 1f).coerceIn(-1f, 1f) * 32767f).roundToInt()
                when (ev.actionMasked) {
                    MotionEvent.ACTION_DOWN, MotionEvent.ACTION_MOVE -> {
                        NativeLibretroBridge.nativeSetPointer(nx, ny, true)
                        true
                    }
                    MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                        NativeLibretroBridge.nativeSetPointer(nx, ny, false)
                        true
                    }
                    else -> false
                }
            }
        }
        rootLayout.addView(
            surfaceView,
            FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT, Gravity.TOP)
        )

        bezelImageView = ImageView(this).apply {
            scaleType = ImageView.ScaleType.FIT_XY
            isClickable = false
            isFocusable = false
            visibility = View.GONE
        }
        applyOptionalBezelOverlay()
        rootLayout.addView(
            bezelImageView,
            FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT, Gravity.TOP)
        )

        fpsHudText = TextView(this).apply {
            setTextColor(Color.WHITE)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 10.8f)
            fontFeatureSettings = "tnum"
            StormThemeEngine.applyFont(this, bold = true)
            setPadding(dp(10f), dp(4f), dp(10f), dp(4f))
            background = GradientDrawable().apply {
                shape = GradientDrawable.RECTANGLE
                cornerRadius = dpF(10f)
                setColor(Color.argb(175, 14, 11, 28))
                setStroke(dp(1f), StormThemeEngine.withAlpha(StormThemeEngine.activeAccentColor, 175))
            }
            visibility = if (showFps) View.VISIBLE else View.GONE
            text = coreName
        }
        rootLayout.addView(
            fpsHudText,
            FrameLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                gravity = Gravity.TOP or Gravity.END
                topMargin = dp(14f)
                rightMargin = dp(14f)
            }
        )

        touchOverlay = MobileTouchGamepadOverlay(this).apply {
            accentColor = StormThemeEngine.activeAccentColor
            syncPreferencesFromUnifiedStore()
            setPlatform(platformName)
            callback = object : MobileTouchGamepadOverlay.Callback {
                override fun onDigitalButton(libretroId: Int, pressed: Boolean) {
                    if (NativeLibretroBridge.isLibraryLoaded && coreLoadedOk) {
                        NativeLibretroBridge.nativeSetButton(0, libretroId, pressed)
                    }
                }

                override fun onAnalogStick(stickIndex: Int, axisX: Int, axisY: Int) {
                    if (NativeLibretroBridge.isLibraryLoaded && coreLoadedOk) {
                        NativeLibretroBridge.nativeSetAnalog(
                            0,
                            stickIndex,
                            NativeLibretroBridge.RETRO_DEVICE_ID_ANALOG_X,
                            axisX
                        )
                        NativeLibretroBridge.nativeSetAnalog(
                            0,
                            stickIndex,
                            NativeLibretroBridge.RETRO_DEVICE_ID_ANALOG_Y,
                            axisY
                        )
                    }
                }

                override fun onQuickMenuRequested() {
                    showInGameQuickDrawer()
                }

                override fun onFastForwardToggle() {
                    cycleFastForwardSpeed()
                }

                override fun onRewindHold(holding: Boolean) {
                    if (rewindEnabled) {
                        rewindHeld = holding
                    } else if (holding) {
                        Toast.makeText(this@EmulationActivity, "Перемотка отключена в настройках", Toast.LENGTH_SHORT).show()
                    }
                }

                override fun onDeckVisibilityChanged() {
                    rootLayout.post { syncViewportBoundsWithHandheldDeck() }
                }
            }
        }
        rootLayout.addView(
            touchOverlay,
            FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
        )

        setContentView(rootLayout)
        rootLayout.post { syncViewportBoundsWithHandheldDeck() }
    }

    private fun applyOptionalBezelOverlay(overridePreset: String? = null) {
        val preset = overridePreset ?: unifiedPrefs().getString("bezelPreset", "none") ?: "none"
        if (preset == "none" || preset == "Без рамки" || preset == "No Bezel") {
            bezelImageView.visibility = View.GONE
            return
        }
        val bezelsRoot = MobileCheatAndBiosManager.getBezelsDir()
        var bmp: Bitmap? = null
        if (bezelsRoot.exists()) {
            val normKey = preset.lowercase(Locale.ROOT).replace(" ", "_")
            val candidate = bezelsRoot.walkTopDown().maxDepth(4).firstOrNull { f ->
                f.isFile && f.extension.equals("png", ignoreCase = true) &&
                    f.nameWithoutExtension.lowercase(Locale.ROOT).contains(normKey)
            }
            if (candidate != null) {
                bmp = BitmapFactory.decodeFile(candidate.absolutePath)
            }
        }
        if (bmp == null) {
            val dm = resources.displayMetrics
            val w = dm.widthPixels.coerceAtLeast(640)
            val h = dm.heightPixels.coerceAtLeast(360)
            bmp = generateBezelArtworkBitmap(preset, w, h)
        }
        if (bmp != null) {
            bezelImageView.setImageBitmap(bmp)
            bezelImageView.visibility = View.VISIBLE
        } else {
            bezelImageView.visibility = View.GONE
        }
    }

    private fun generateBezelArtworkBitmap(preset: String, w: Int, h: Int): Bitmap {
        val bmp = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888)
        val canvas = android.graphics.Canvas(bmp)
        val paint = android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG)
        val aspect = 4f / 3f
        val innerH = (h * 0.88f).roundToInt()
        val innerW = (innerH * aspect).roundToInt().coerceAtMost((w * 0.88f).roundToInt())
        val innerX = (w - innerW) / 2
        val innerY = (h - innerH) / 2
        val innerRect = android.graphics.RectF(innerX.toFloat(), innerY.toFloat(), (innerX + innerW).toFloat(), (innerY + innerH).toFloat())

        when {
            preset.contains("Neon", ignoreCase = true) -> {
                paint.color = Color.rgb(12, 10, 22)
                canvas.drawRect(0f, 0f, w.toFloat(), h.toFloat(), paint)
                paint.style = android.graphics.Paint.Style.STROKE
                paint.strokeWidth = 6f
                paint.color = Color.rgb(6, 182, 212)
                canvas.drawRoundRect(innerRect.left - 8f, innerRect.top - 8f, innerRect.right + 8f, innerRect.bottom + 8f, 16f, 16f, paint)
                paint.color = Color.rgb(236, 72, 153)
                canvas.drawRoundRect(innerRect.left - 14f, innerRect.top - 14f, innerRect.right + 14f, innerRect.bottom + 14f, 20f, 20f, paint)
            }
            preset.contains("Sony", ignoreCase = true) -> {
                paint.color = Color.rgb(28, 28, 32)
                canvas.drawRect(0f, 0f, w.toFloat(), h.toFloat(), paint)
                paint.style = android.graphics.Paint.Style.STROKE
                paint.strokeWidth = 14f
                paint.color = Color.rgb(18, 18, 20)
                canvas.drawRoundRect(innerRect.left - 7f, innerRect.top - 7f, innerRect.right + 7f, innerRect.bottom + 7f, 24f, 24f, paint)
            }
            preset.contains("GBA", ignoreCase = true) -> {
                paint.color = Color.rgb(42, 28, 68)
                canvas.drawRect(0f, 0f, w.toFloat(), h.toFloat(), paint)
                paint.style = android.graphics.Paint.Style.STROKE
                paint.strokeWidth = 10f
                paint.color = Color.rgb(20, 16, 32)
                canvas.drawRoundRect(innerRect.left - 5f, innerRect.top - 5f, innerRect.right + 5f, innerRect.bottom + 5f, 18f, 18f, paint)
            }
            preset.contains("SNES", ignoreCase = true) -> {
                paint.color = Color.rgb(48, 48, 56)
                canvas.drawRect(0f, 0f, w.toFloat(), h.toFloat(), paint)
                paint.style = android.graphics.Paint.Style.STROKE
                paint.strokeWidth = 8f
                paint.color = Color.rgb(139, 92, 246)
                canvas.drawRoundRect(innerRect.left - 4f, innerRect.top - 4f, innerRect.right + 4f, innerRect.bottom + 4f, 12f, 12f, paint)
            }
            preset.contains("Mega", ignoreCase = true) -> {
                paint.color = Color.rgb(20, 20, 24)
                canvas.drawRect(0f, 0f, w.toFloat(), h.toFloat(), paint)
                paint.style = android.graphics.Paint.Style.STROKE
                paint.strokeWidth = 8f
                paint.color = Color.rgb(239, 68, 68)
                canvas.drawRoundRect(innerRect.left - 4f, innerRect.top - 4f, innerRect.right + 4f, innerRect.bottom + 4f, 14f, 14f, paint)
            }
            else -> {
                paint.color = Color.rgb(14, 11, 24)
                canvas.drawRect(0f, 0f, w.toFloat(), h.toFloat(), paint)
                paint.style = android.graphics.Paint.Style.STROKE
                paint.strokeWidth = 6f
                paint.color = StormThemeEngine.activeAccentColor
                canvas.drawRoundRect(innerRect.left - 4f, innerRect.top - 4f, innerRect.right + 4f, innerRect.bottom + 4f, 16f, 16f, paint)
            }
        }

        paint.style = android.graphics.Paint.Style.FILL
        paint.xfermode = android.graphics.PorterDuffXfermode(android.graphics.PorterDuff.Mode.CLEAR)
        canvas.drawRoundRect(innerRect, 12f, 12f, paint)
        paint.xfermode = null
        return bmp
    }

    private fun detectInitialControllerPresence() {
        val controllers = MobileControllerProfileManager.enumerateConnectedControllers(this)
        if (controllers.isNotEmpty()) {
            touchOverlay.setPhysicalControllerActive(true)
        }
    }

    private fun startEmulationEngine() {
        if (!NativeLibretroBridge.isLibraryLoaded) {
            Toast.makeText(this, "JNI bridge unavailable", Toast.LENGTH_LONG).show()
            finish()
            return
        }
        if (coreSoPath.isBlank() || !File(coreSoPath).exists()) {
            Toast.makeText(this, StormLocalization.tr("core_not_installed"), Toast.LENGTH_LONG).show()
            finish()
            return
        }
        if (effectiveRomPath.isBlank() || !File(effectiveRomPath).exists()) {
            Toast.makeText(this, "ROM file not found: $effectiveRomPath", Toast.LENGTH_LONG).show()
            finish()
            return
        }

        val initialShader = resolveInitialShaderMode()
        val initialAspect = resolveInitialAspectMode()

        emuRunning = true
        emuPaused = false

        emuThread = Thread({
            android.os.Process.setThreadPriority(android.os.Process.THREAD_PRIORITY_DISPLAY)
            val loaded = NativeLibretroBridge.nativeLoadCoreAndGame(
                coreSoPath,
                effectiveRomPath,
                systemDir.absolutePath,
                savesDir.absolutePath
            )
            if (!loaded) {
                mainHandler.post {
                    Toast.makeText(this, StormLocalization.tr("core_failed"), Toast.LENGTH_LONG).show()
                    finish()
                }
                return@Thread
            }

            coreLoadedOk = true
            NativeLibretroBridge.nativeSetShaderMode(initialShader)
            NativeLibretroBridge.nativeSetAspectMode(initialAspect)

            // Auto-load .srm battery save if present
            val srm = srmFile()
            if (srm.exists() && srm.length() > 0L) {
                NativeLibretroBridge.nativeLoadSram(srm.absolutePath)
            }

            // Auto-load & apply active .cht cheats for this game on launch
            loadAndApplyGameCheats()

            val targetFps = NativeLibretroBridge.nativeGetFps().let { if (it in 20.0..120.0) it else 60.0 }
            val frameDurationNs = (1_000_000_000.0 / targetFps).toLong()
            val audioBuf = ShortArray(8192)
        val audioResidualBuf = ShortArray(8192)
        var audioResidualCount = 0
        var audioResidualOffset = 0
            var frameCounter = 0L
            var sramAutosaveCounter = 0
            var nextFrameDeadline = System.nanoTime()

            initOrUpdateAudioTrack(NativeLibretroBridge.nativeGetSampleRate())

            while (emuRunning) {
                if (emuPaused) {
                    try {
                        Thread.sleep(16L)
                    } catch (_: InterruptedException) {
                        break
                    }
                    nextFrameDeadline = System.nanoTime()
                    continue
                }

                if (rewindHeld && rewindEnabled) {
                    val popped = NativeLibretroBridge.nativePopRewindSnapshot()
                    if (popped) {
                        NativeLibretroBridge.nativeRunFrame()
                    }
                    NativeLibretroBridge.nativePullAudioSamples(audioBuf, audioBuf.size)
                } else {
                    // Drain any residual unwritten samples before new frame
                    if (audioResidualCount > 0) {
                        try {
                            val resWritten = audioTrack?.write(audioResidualBuf, audioResidualOffset, audioResidualCount, AudioTrack.WRITE_NON_BLOCKING) ?: 0
                            if (resWritten > 0) {
                                audioResidualOffset += resWritten
                                audioResidualCount -= resWritten
                                if (audioResidualCount == 0) audioResidualOffset = 0
                            }
                        } catch (_: Throwable) {}
                    }

                    val ffSteps = fastForwardMultiplier.coerceIn(1, 4)
                    val totalSteps = if (ffSteps > 1) ffSteps else (1 + frameSkip.coerceIn(0, 3))
                    for (s in 0 until totalSteps) {
                        NativeLibretroBridge.nativeRunFrame()
                        val pulled = NativeLibretroBridge.nativePullAudioSamples(audioBuf, audioBuf.size)
                        if (pulled > 0 && (ffSteps == 1 || s == 0)) {
                            val sr = NativeLibretroBridge.nativeGetSampleRate()
                            if (sr > 8000 && sr != currentSampleRate) {
                                initOrUpdateAudioTrack(sr)
                                audioResidualCount = 0
                                audioResidualOffset = 0
                            }
                            if (audioGainLinear > 1.02f) {
                                val g = audioGainLinear
                                for (i in 0 until pulled) {
                                    audioBuf[i] = (audioBuf[i] * g).roundToInt().coerceIn(-32768, 32767).toShort()
                                }
                            }
                            try {
                                var curOffset = 0
                                var remaining = pulled
                                while (remaining > 0 && emuRunning) {
                                    val written = audioTrack?.write(audioBuf, curOffset, remaining, AudioTrack.WRITE_NON_BLOCKING) ?: 0
                                    if (written > 0) {
                                        curOffset += written
                                        remaining -= written
                                    } else if (written == 0) {
                                        val copyCount = minOf(remaining, audioResidualBuf.size)
                                        System.arraycopy(audioBuf, curOffset, audioResidualBuf, 0, copyCount)
                                        audioResidualOffset = 0
                                        audioResidualCount = copyCount
                                        try { Thread.sleep(1) } catch (_: InterruptedException) {}
                                        break
                                    } else {
                                        break
                                    }
                                }
                            } catch (_: Throwable) {
                            }
                        }
                    }
                    frameCounter++
                    if (rewindEnabled && frameCounter % 3L == 0L) {
                        NativeLibretroBridge.nativePushRewindSnapshot()
                    }
                }

                // Trigger rumble if core requests vibration
                val rumble = NativeLibretroBridge.nativeGetRumbleStrength(0)
                if (rumble > 4096) {
                    pulseControllerRumble(rumble)
                }

                // Periodic safe SRAM flush every ~30 seconds of active play
                sramAutosaveCounter++
                if (sramAutosaveCounter >= 1800) {
                    sramAutosaveCounter = 0
                    flushSramSafe()
                }

                val isEco = unifiedPrefs().getBoolean("ecoAntiHeatMode", false)
                val paceMultiplier = if (isEco) 2 else (if (fastForwardMultiplier == 1 && frameSkip > 0) (1 + frameSkip) else 1)
                nextFrameDeadline += frameDurationNs * paceMultiplier
                val sleepNs = nextFrameDeadline - System.nanoTime()
                if (sleepNs > 100_000L) {
                    java.util.concurrent.locks.LockSupport.parkNanos(sleepNs)
                } else if (sleepNs < -frameDurationNs * 3) {
                    nextFrameDeadline = System.nanoTime()
                }
            }

            flushSramSafe()
            releaseAudioTrack()
            if (coreLoadedOk) {
                coreLoadedOk = false
                NativeLibretroBridge.nativeUnload()
            }
        }, "StormLibretroEmuThread").apply {
                        start()
        }
    }

    private fun initOrUpdateAudioTrack(sampleRateRaw: Int) {
        val sr = if (sampleRateRaw in 8000..192000) sampleRateRaw else 44100
        if (audioTrack != null && currentSampleRate == sr) return
        releaseAudioTrack()
        currentSampleRate = sr

        try {
            val minBuf = AudioTrack.getMinBufferSize(
                sr,
                AudioFormat.CHANNEL_OUT_STEREO,
                AudioFormat.ENCODING_PCM_16BIT
            ).coerceAtLeast(4096)

            val attrs = AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_GAME)
                .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
                .build()

            val fmt = AudioFormat.Builder()
                .setSampleRate(sr)
                .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                .setChannelMask(AudioFormat.CHANNEL_OUT_STEREO)
                .build()

            val builder = AudioTrack.Builder()
                .setAudioAttributes(attrs)
                .setAudioFormat(fmt)
                .setBufferSizeInBytes(minBuf * 2)
                .setTransferMode(AudioTrack.MODE_STREAM)

            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                builder.setPerformanceMode(AudioTrack.PERFORMANCE_MODE_LOW_LATENCY)
            }

            audioTrack = builder.build().also {
                it.setVolume(audioGainLinear.coerceIn(0f, 1f))
                it.play()
            }
        } catch (_: Throwable) {
        }
    }

    private fun releaseAudioTrack() {
        try {
            audioTrack?.pause()
            audioTrack?.flush()
            audioTrack?.release()
        } catch (_: Throwable) {
        }
        audioTrack = null
    }

    private fun pulseControllerRumble(strength: Int) {
        try {
            @Suppress("DEPRECATION")
            val vib = getSystemService(Context.VIBRATOR_SERVICE) as? Vibrator ?: return
            if (!vib.hasVibrator()) return
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                val amp = ((strength / 65535f) * 255f).roundToInt().coerceIn(1, 255)
                vib.vibrate(VibrationEffect.createOneShot(18L, amp))
            } else {
                @Suppress("DEPRECATION")
                vib.vibrate(15L)
            }
        } catch (_: Throwable) {
        }
    }

    private fun flushSramSafe() {
        if (!coreLoadedOk || !NativeLibretroBridge.isLibraryLoaded) return
        try {
            savesDir.mkdirs()
            NativeLibretroBridge.nativeSaveSram(srmFile().absolutePath)
        } catch (_: Throwable) {
        }
    }

    private fun loadAndApplyGameCheats() {
        try {
            val normPlat = MobileCoreRegistry.normalizePlatformKey(platformName, File(romPath).extension)
            val dummyRom = MobileRomItem(
                name = MobileLibraryAndCoverEngine.cleanDisplayTitle(romBaseStem),
                rawNoIntroName = romBaseStem,
                filePath = romPath,
                extension = File(romPath).extension.lowercase(Locale.ROOT),
                platform = platformName,
                platformKey = normPlat,
                regionCode = "WORLD",
                isRussianMod = false,
                assignedCoreId = "",
                sizeBytes = File(romPath).length()
            )
            val profile = MobileCheatAndBiosManager.matchCheatsForLibrary(listOf(dummyRom)).firstOrNull()
            if (profile != null) {
                val f = File(profile.chtFilePath)
                gameCheatFile = f
                gameCheatEntries.clear()
                if (f.exists()) {
                    gameCheatEntries.addAll(MobileCheatAndBiosManager.parseChtFile(f))
                }
            } else {
                val fallbackDir = File(MobileCheatAndBiosManager.getCheatsRootDir(), normPlat.ifBlank { "custom" })
                fallbackDir.mkdirs()
                gameCheatFile = File(fallbackDir, "$romBaseStem.cht")
                gameCheatEntries.clear()
                if (gameCheatFile!!.exists()) {
                    gameCheatEntries.addAll(MobileCheatAndBiosManager.parseChtFile(gameCheatFile!!))
                }
            }
            pushActiveCheatsToCore()
        } catch (_: Throwable) {
        }
    }

    private fun pushActiveCheatsToCore() {
        if (!coreLoadedOk || !NativeLibretroBridge.isLibraryLoaded) return
        NativeLibretroBridge.nativeResetCheats()
        for ((idx, entry) in gameCheatEntries.withIndex()) {
            if (entry.isEnabled && entry.code.isNotBlank()) {
                NativeLibretroBridge.nativeApplyCheat(idx, true, entry.code)
            }
        }
    }

    private fun setFastForwardMultiplier(multiplier: Int, showToast: Boolean = false) {
        fastForwardMultiplier = multiplier.coerceIn(1, 4)
        touchOverlay.isFastForwardActive = fastForwardMultiplier > 1
        touchOverlay.fastForwardSpeedLabel = if (fastForwardMultiplier > 1) "FAST ${fastForwardMultiplier}X" else "FAST 2X"
        if (showToast) {
            Toast.makeText(this, "${StormLocalization.tr("quick_fast_forward")}: ${fastForwardMultiplier}x", Toast.LENGTH_SHORT).show()
        }
    }

    private fun cycleFastForwardSpeed() {
        val next = when (fastForwardMultiplier) {
            1 -> 2
            2 -> 4
            else -> 1
        }
        setFastForwardMultiplier(next, showToast = true)
    }

    // === Physical Controller & Keyboard Routing ===

    override fun onKeyDown(keyCode: Int, event: KeyEvent): Boolean {
        if (keyCode == KeyEvent.KEYCODE_BACK || keyCode == KeyEvent.KEYCODE_ESCAPE) {
            if (touchOverlay.isEditMode) {
                touchOverlay.isEditMode = false
                return true
            }
            if (quickDrawerDialog?.isShowing == true) {
                quickDrawerDialog?.dismiss()
            } else {
                showInGameQuickDrawer()
            }
            return true
        }

        val isGamepadEvent = isExternalGamepadDevice(event.device)
        if (isGamepadEvent) {
            touchOverlay.setPhysicalControllerActive(true)
        }

        if (keyCode == KeyEvent.KEYCODE_BUTTON_SELECT) {
            selectButtonHeld = true
        }

        if (event.repeatCount == 0) {
            val hotkey = MobileControllerProfileManager.mapKeyEventToHotkey(
                event.device,
                keyCode,
                selectButtonHeld,
                this
            )
            if (hotkey != null) {
                executeHotkeyAction(hotkey, pressed = true)
                return true
            }
        }

        val retroId = MobileControllerProfileManager.mapKeyEventToRetroPadId(event.device, keyCode, this)
        if (retroId != null && coreLoadedOk && NativeLibretroBridge.isLibraryLoaded) {
            NativeLibretroBridge.nativeSetButton(0, retroId, true)
            return true
        }
        return super.onKeyDown(keyCode, event)
    }

    override fun onKeyUp(keyCode: Int, event: KeyEvent): Boolean {
        if (keyCode == KeyEvent.KEYCODE_BACK || keyCode == KeyEvent.KEYCODE_ESCAPE) {
            return true
        }
        if (keyCode == KeyEvent.KEYCODE_BUTTON_SELECT) {
            selectButtonHeld = false
        }

        val hotkey = MobileControllerProfileManager.mapKeyEventToHotkey(
            event.device,
            keyCode,
            selectButtonHeld,
            this
        )
        if (hotkey == MobileControllerProfileManager.HOTKEY_REWIND) {
            rewindHeld = false
            return true
        }

        val retroId = MobileControllerProfileManager.mapKeyEventToRetroPadId(event.device, keyCode, this)
        if (retroId != null && coreLoadedOk && NativeLibretroBridge.isLibraryLoaded) {
            NativeLibretroBridge.nativeSetButton(0, retroId, false)
            return true
        }
        return super.onKeyUp(keyCode, event)
    }

    override fun onGenericMotionEvent(event: MotionEvent): Boolean {
        val src = event.source
        val isJoystick = (src and InputDevice.SOURCE_JOYSTICK) == InputDevice.SOURCE_JOYSTICK ||
            (src and InputDevice.SOURCE_GAMEPAD) == InputDevice.SOURCE_GAMEPAD
        if (!isJoystick || event.action != MotionEvent.ACTION_MOVE) {
            return super.onGenericMotionEvent(event)
        }

        if (!coreLoadedOk || !NativeLibretroBridge.isLibraryLoaded) {
            return super.onGenericMotionEvent(event)
        }

        val mask = MobileControllerProfileManager.extractMotionRetroPadBitmask(event, this)
        val hatX = event.getAxisValue(MotionEvent.AXIS_HAT_X)
        val hatY = event.getAxisValue(MotionEvent.AXIS_HAT_Y)
        val lx = event.getAxisValue(MotionEvent.AXIS_X)
        val ly = event.getAxisValue(MotionEvent.AXIS_Y)
        val rx = event.getAxisValue(MotionEvent.AXIS_Z)
        val ry = event.getAxisValue(MotionEvent.AXIS_RZ)

        if (mask != 0 || abs(lx) > 0.2f || abs(ly) > 0.2f || abs(rx) > 0.2f || abs(ry) > 0.2f || abs(hatX) > 0.2f || abs(hatY) > 0.2f) {
            touchOverlay.setPhysicalControllerActive(true)
        }

        val digitalIds = intArrayOf(
            MobileControllerProfileManager.RETRO_ID_UP,
            MobileControllerProfileManager.RETRO_ID_DOWN,
            MobileControllerProfileManager.RETRO_ID_LEFT,
            MobileControllerProfileManager.RETRO_ID_RIGHT,
            MobileControllerProfileManager.RETRO_ID_L2,
            MobileControllerProfileManager.RETRO_ID_R2
        )
        for (id in digitalIds) {
            val bit = 1 shl id
            val wasDown = (prevMotionBitmask and bit) != 0
            val nowDown = (mask and bit) != 0
            if (wasDown != nowDown) {
                NativeLibretroBridge.nativeSetButton(0, id, nowDown)
            }
        }
        prevMotionBitmask = mask

        val deadzone = event.device?.let { MobileControllerProfileManager.getOrCreateProfile(it, this).analogDeadzone } ?: 0.18f
        fun scaleAxis(v: Float): Int = if (abs(v) < deadzone) 0 else (v.coerceIn(-1f, 1f) * 32767f).roundToInt()

        NativeLibretroBridge.nativeSetAnalog(0, NativeLibretroBridge.RETRO_DEVICE_INDEX_ANALOG_LEFT, NativeLibretroBridge.RETRO_DEVICE_ID_ANALOG_X, scaleAxis(lx))
        NativeLibretroBridge.nativeSetAnalog(0, NativeLibretroBridge.RETRO_DEVICE_INDEX_ANALOG_LEFT, NativeLibretroBridge.RETRO_DEVICE_ID_ANALOG_Y, scaleAxis(ly))
        NativeLibretroBridge.nativeSetAnalog(0, NativeLibretroBridge.RETRO_DEVICE_INDEX_ANALOG_RIGHT, NativeLibretroBridge.RETRO_DEVICE_ID_ANALOG_X, scaleAxis(rx))
        NativeLibretroBridge.nativeSetAnalog(0, NativeLibretroBridge.RETRO_DEVICE_INDEX_ANALOG_RIGHT, NativeLibretroBridge.RETRO_DEVICE_ID_ANALOG_Y, scaleAxis(ry))
        return true
    }

    private fun isExternalGamepadDevice(device: InputDevice?): Boolean {
        if (device == null) return false
        val s = device.sources
        return (s and InputDevice.SOURCE_GAMEPAD) == InputDevice.SOURCE_GAMEPAD ||
            (s and InputDevice.SOURCE_JOYSTICK) == InputDevice.SOURCE_JOYSTICK
    }

    private fun executeHotkeyAction(action: String, pressed: Boolean) {
        when (action) {
            MobileControllerProfileManager.HOTKEY_QUICK_SAVE -> if (pressed) performSaveState(activeSaveSlot)
            MobileControllerProfileManager.HOTKEY_QUICK_LOAD -> if (pressed) performLoadState(activeSaveSlot)
            MobileControllerProfileManager.HOTKEY_REWIND -> if (rewindEnabled) rewindHeld = pressed
            MobileControllerProfileManager.HOTKEY_FAST_FORWARD -> if (pressed) cycleFastForwardSpeed()
            MobileControllerProfileManager.HOTKEY_QUICK_MENU -> if (pressed) showInGameQuickDrawer()
            MobileControllerProfileManager.HOTKEY_SCREENSHOT -> if (pressed) captureScreenshotToGallery()
        }
    }

    // === Frame Capture, Save/Load State & Screenshots ===

    private fun captureCurrentFrameBitmap(): Bitmap? {
        if (!coreLoadedOk || !NativeLibretroBridge.isLibraryLoaded) return null
        return try {
            val w = NativeLibretroBridge.nativeGetFrameWidth()
            val h = NativeLibretroBridge.nativeGetFrameHeight()
            val rgba = NativeLibretroBridge.nativeCaptureFrameRgba() ?: return null
            if (w <= 0 || h <= 0 || rgba.size < w * h * 4) return null
            val bmp = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888)
            bmp.copyPixelsFromBuffer(ByteBuffer.wrap(rgba))
            bmp
        } catch (_: Throwable) {
            null
        }
    }

    private fun performSaveState(slot: Int, previewBitmap: Bitmap? = null): Boolean {
        if (!coreLoadedOk || !NativeLibretroBridge.isLibraryLoaded) return false
        val bmp = previewBitmap ?: captureCurrentFrameBitmap()
        val ok = NativeLibretroBridge.nativeSaveState(stateFile(slot).absolutePath)
        flushSramSafe()
        if (ok && bmp != null) {
            try {
                FileOutputStream(statePreviewFile(slot)).use { fos ->
                    bmp.compress(Bitmap.CompressFormat.PNG, 92, fos)
                }
            } catch (_: Throwable) {
            }
        }
        val msg = if (ok) "Сохранено в слот #$slot" else "Ошибка сохранения слота #$slot"
        Toast.makeText(this, msg, Toast.LENGTH_SHORT).show()
        return ok
    }

    private fun performLoadState(slot: Int): Boolean {
        if (!coreLoadedOk || !NativeLibretroBridge.isLibraryLoaded) return false
        val sf = stateFile(slot)
        if (!sf.exists()) {
            Toast.makeText(this, "Слот #$slot пуст", Toast.LENGTH_SHORT).show()
            return false
        }
        val ok = NativeLibretroBridge.nativeLoadState(sf.absolutePath)
        val msg = if (ok) "Загружен слот #$slot" else "Ошибка загрузки слота #$slot"
        Toast.makeText(this, msg, Toast.LENGTH_SHORT).show()
        return ok
    }

    private fun sanitizeSafeComponent(name: String): String {
        return name.replace(Regex("""[\\/:*?"<>|]"""), "_").replace(Regex("""\s+"""), " ").trim()
    }

    private fun captureScreenshotToGallery(preCaptured: Bitmap? = null): File? {
        val bmp = preCaptured ?: captureCurrentFrameBitmap()
        if (bmp == null) {
            Toast.makeText(this, "Кадр игры недоступен", Toast.LENGTH_SHORT).show()
            return null
        }
        return try {
            val pType = MobileLibraryAndCoverEngine.resolvePlatformType(romPath, platformName, platformName)
            val pFolder = sanitizeSafeComponent(platformName.ifEmpty { "Console" })
            val gFolder = sanitizeSafeComponent(MobileLibraryAndCoverEngine.cleanDisplayTitle(romBaseStem).ifEmpty { romName })
            val targetDir = File(File(File(File(MobileCoreRegistry.STORM_PUBLIC_ROOT, "screenshots"), pType), pFolder), gFolder)
            targetDir.mkdirs()
            val stamp = SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US).format(Date())
            val out = File(targetDir, "${romBaseStem}_$stamp.png")
            FileOutputStream(out).use { fos ->
                bmp.compress(Bitmap.CompressFormat.PNG, 100, fos)
            }
            Toast.makeText(this, "Скриншот сохранен: ${out.name}", Toast.LENGTH_SHORT).show()
            out
        } catch (_: Throwable) {
            null
        }
    }

    // === STORM AURORA FLUID Glassmorphic Slide-Over Quick Studio ===

    private fun createStyledDropdown(
        label: String,
        options: List<String>,
        selectedIndex: Int,
        pal: StormThemePalette,
        accent: Int,
        onSelected: (Int) -> Unit
    ): LinearLayout {
        val row = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, dp(4f), 0, dp(6f))
        }
        val labelTv = TextView(this).apply {
            text = label
            setTextColor(pal.textSecondary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f)
            StormThemeEngine.applyFont(this, bold = false)
        }
        val spinner = android.widget.Spinner(this).apply {
            val adapter = object : android.widget.ArrayAdapter<String>(this@EmulationActivity, android.R.layout.simple_spinner_dropdown_item, options) {
                override fun getView(position: Int, convertView: View?, parent: ViewGroup): View {
                    val tv = super.getView(position, convertView, parent) as TextView
                    tv.setTextColor(pal.textPrimary)
                    tv.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f)
                    StormThemeEngine.applyFont(tv, bold = true)
                    return tv
                }
                override fun getDropDownView(position: Int, convertView: View?, parent: ViewGroup): View {
                    val tv = super.getDropDownView(position, convertView, parent) as TextView
                    tv.setTextColor(pal.textPrimary)
                    tv.setBackgroundColor(Color.rgb(18, 14, 30))
                    tv.setPadding(dp(12f), dp(10f), dp(12f), dp(10f))
                    StormThemeEngine.applyFont(tv, bold = false)
                    return tv
                }
            }
            this.adapter = adapter
            setSelection(selectedIndex.coerceIn(0, options.size - 1), false)
            background = GradientDrawable().apply {
                shape = GradientDrawable.RECTANGLE
                cornerRadius = dpF(8f)
                setColor(Color.argb(160, 22, 18, 40))
                setStroke(dp(1f), StormThemeEngine.withAlpha(accent, 140))
            }
            setPadding(dp(10f), dp(8f), dp(10f), dp(8f))
            onItemSelectedListener = object : android.widget.AdapterView.OnItemSelectedListener {
                override fun onItemSelected(parent: android.widget.AdapterView<*>?, view: View?, pos: Int, id: Long) {
                    onSelected(pos)
                }
                override fun onNothingSelected(parent: android.widget.AdapterView<*>?) {}
            }
        }
        row.addView(labelTv)
        row.addView(spinner, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(42f)).apply { topMargin = dp(2f) })
        return row
    }

fun showInGameQuickDrawer() {
        if (isFinishing || isDestroyed) return
        if (quickDrawerDialog?.isShowing == true) return

        val snapshotBeforePause = captureCurrentFrameBitmap()
        touchOverlay.releaseAllInputs()
        flushSramSafe()
        emuPaused = true
        updateGyroRegistration()
        setAndroidGameModeState(isInterruptible = true)

        val pal = StormThemeEngine.currentTheme
        val accent = StormThemeEngine.activeAccentColor

        val dialog = Dialog(this, android.R.style.Theme_Translucent_NoTitleBar_Fullscreen)
        dialog.setCanceledOnTouchOutside(true)
        dialog.setOnKeyListener { d, keyCode, event ->
            if (event.action == KeyEvent.ACTION_DOWN &&
                (keyCode == KeyEvent.KEYCODE_ESCAPE || keyCode == KeyEvent.KEYCODE_BACK)
            ) {
                d.dismiss()
                true
            } else false
        }
        dialog.setOnDismissListener {
            quickDrawerDialog = null
            if (!touchOverlay.isEditMode) {
                emuPaused = false
                updateGyroRegistration()
                setAndroidGameModeState(isInterruptible = false)
            }
            syncViewportBoundsWithHandheldDeck()
            applyImmersiveSystemBars()
        }

        val scrim = FrameLayout(this).apply {
            setBackgroundColor(Color.argb(168, 4, 3, 12))
            alpha = 0f
            setOnClickListener { dialog.dismiss() }
        }

        val screenW = resources.displayMetrics.widthPixels
        val drawerWidth = minOf((screenW * 0.88f).roundToInt(), dp(440f))

        val drawerCard = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            isClickable = true
            background = createGlassStudioDrawerDrawable(accent)
            setPadding(dp(16f), dp(18f), dp(16f), dp(18f))
            translationX = drawerWidth.toFloat()
        }

        // 1. HEADER: Game Title + Platform + Live FPS / Frame-Time Badge + Close
        val headerRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        val titleCol = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        }
        val headerTitle = TextView(this).apply {
            text = romName
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 16.5f)
            maxLines = 2
            StormThemeEngine.applyFont(this, bold = true)
        }
        val platformSubtitle = TextView(this).apply {
            text = "$platformName  |  $coreName"
            setTextColor(pal.textSecondary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.2f)
            maxLines = 2
            StormThemeEngine.applyFont(this, bold = false)
        }
        val measuredFps = if (NativeLibretroBridge.isLibraryLoaded) NativeLibretroBridge.nativeGetMeasuredFps() else 60.0
        val frameMs = if (measuredFps > 1.0) 1000.0 / measuredFps else 16.7
        val telemetryPill = TextView(this).apply {
            text = String.format(Locale.US, "STORM RETROARCH 2.7.4  |  %.1f FPS  |  %.1f ms", measuredFps, frameMs)
            fontFeatureSettings = "tnum"
            setTextColor(StormThemeEngine.lighten(accent, 0.25f))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 10.5f)
            StormThemeEngine.applyFont(this, bold = true)
            setPadding(dp(8f), dp(3f), dp(8f), dp(3f))
            background = GradientDrawable().apply {
                cornerRadius = dpF(8f)
                setColor(StormThemeEngine.withAlpha(accent, 34))
                setStroke(dp(1f), StormThemeEngine.withAlpha(accent, 110))
            }
        }
        titleCol.addView(headerTitle)
        titleCol.addView(platformSubtitle, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            topMargin = dp(2f)
            bottomMargin = dp(5f)
        })
        titleCol.addView(telemetryPill, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        headerRow.addView(titleCol)

        val closeTopBtn = createIconActionButton(StormVectorIcons.CLOSE, "", isAccent = false) {
            dialog.dismiss()
        }
        headerRow.addView(closeTopBtn, LinearLayout.LayoutParams(dp(38f), dp(38f)).apply { leftMargin = dp(8f) })
        drawerCard.addView(headerRow)

        // Primary 3D "Продолжить" button right below header
        drawerCard.addView(
            createIconActionButton(StormVectorIcons.PLAY, "Продолжить", isAccent = true) {
                dialog.dismiss()
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                topMargin = dp(12f)
                bottomMargin = dp(8f)
            }
        )

        val scroll = ScrollView(this).apply {
            isVerticalScrollBarEnabled = false
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f)
        }
        val body = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, dp(4f), 0, dp(12f))
        }

        // 2. VISUAL SAVE / LOAD STATE STRIP (SLOTS 0 - 9)
        val saveCard = createStudioGroupCard()
        saveCard.addView(createSectionLabel("Быстрые сохранения (Слоты 0 - 9)"))

        val slotPreviewRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            background = StormThemeEngine.createRecessedInputDrawable(this@EmulationActivity, cornerDp = 10f)
            setPadding(dp(8f), dp(8f), dp(10f), dp(8f))
        }
        val slotThumbView = ImageView(this).apply {
            scaleType = ImageView.ScaleType.CENTER_CROP
            setBackgroundColor(Color.argb(190, 8, 6, 18))
        }
        slotPreviewRow.addView(slotThumbView, LinearLayout.LayoutParams(dp(96f), dp(66f)))

        val slotMetaCol = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                leftMargin = dp(10f)
            }
        }
        val slotTitleTv = TextView(this).apply {
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f)
            StormThemeEngine.applyFont(this, bold = true)
        }
        val slotTimeTv = TextView(this).apply {
            setTextColor(pal.textSecondary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f)
            fontFeatureSettings = "tnum"
            StormThemeEngine.applyFont(this, bold = false)
        }
        slotMetaCol.addView(slotTitleTv)
        slotMetaCol.addView(slotTimeTv, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(3f) })
        slotPreviewRow.addView(slotMetaCol)
        saveCard.addView(slotPreviewRow, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(8f) })

        val slotChipsRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        val slotChipViews = ArrayList<TextView>(10)

        fun refreshSlotPreview() {
            val sf = stateFile(activeSaveSlot)
            val pf = statePreviewFile(activeSaveSlot)
            slotTitleTv.text = "Слот #$activeSaveSlot"
            if (sf.exists()) {
                val dt = SimpleDateFormat("yyyy-MM-dd HH:mm", Locale.US).format(Date(sf.lastModified()))
                val kb = (sf.length() / 1024L).coerceAtLeast(1L)
                slotTimeTv.text = "$dt  |  $kb КБ"
                val bmp = if (pf.exists()) BitmapFactory.decodeFile(pf.absolutePath) else snapshotBeforePause
                slotThumbView.setImageBitmap(bmp)
            } else {
                slotTimeTv.text = "Свободный слот (готов к записи)"
                slotThumbView.setImageBitmap(snapshotBeforePause)
            }
            for ((idx, tv) in slotChipViews.withIndex()) {
                val sel = idx == activeSaveSlot
                val hasData = stateFile(idx).exists()
                tv.text = if (hasData) "$idx *" else "$idx"
                tv.background = StormThemeEngine.createFilterChipDrawable(this, sel, accent, 10f)
                tv.setTextColor(
                    when {
                        sel -> StormThemeEngine.contrastTextForFill(accent)
                        hasData -> StormThemeEngine.lighten(accent, 0.28f)
                        else -> pal.textPrimary
                    }
                )
            }
        }

        for (slot in 0..9) {
            val chip = TextView(this).apply {
                gravity = Gravity.CENTER
                fontFeatureSettings = "tnum"
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
                setPadding(dp(13f), dp(6f), dp(13f), dp(6f))
                StormThemeEngine.applyFont(this, bold = true)
                setOnClickListener {
                    activeSaveSlot = slot
                    refreshSlotPreview()
                }
            }
            slotChipViews.add(chip)
            slotChipsRow.addView(chip, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { rightMargin = dp(6f) })
        }
        saveCard.addView(
            HorizontalScrollView(this).apply {
                isHorizontalScrollBarEnabled = false
                addView(slotChipsRow)
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(8f) }
        )
        refreshSlotPreview()

        val saveLoadBtnRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        saveLoadBtnRow.addView(
            createIconActionButton(StormVectorIcons.SAVE, "Сохранить", isAccent = true) {
                performSaveState(activeSaveSlot, snapshotBeforePause)
                refreshSlotPreview()
            },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply { rightMargin = dp(6f) }
        )
        saveLoadBtnRow.addView(
            createIconActionButton(StormVectorIcons.LOAD, "Загрузить", isAccent = false) {
                if (performLoadState(activeSaveSlot)) {
                    dialog.dismiss()
                }
            },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        )
        saveCard.addView(saveLoadBtnRow)
        body.addView(saveCard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(10f) })

        // 3. SPEED, REWIND, RESTART & SCREENSHOT CONTROLS
        val speedCard = createStudioGroupCard()
        val rewCount = if (NativeLibretroBridge.isLibraryLoaded) NativeLibretroBridge.nativeGetRewindCount() else 0
        speedCard.addView(createSectionLabel("Скорость и управление временем (буфер: $rewCount)"))

        val speedModes = listOf(1 to "1x Норма", 2 to "Ускорение 2x", 4 to "Ускорение 4x")
        speedCard.addView(buildHorizontalModeChips(speedModes, fastForwardMultiplier) { spd ->
            setFastForwardMultiplier(spd, showToast = false)
        })

        val timeToolsRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        timeToolsRow.addView(
            createIconActionButton(StormVectorIcons.REWIND, "Перемотка назад", isAccent = false) {
                if (!rewindEnabled) {
                    Toast.makeText(this, "Включите перемотку ниже", Toast.LENGTH_SHORT).show()
                } else if (NativeLibretroBridge.isLibraryLoaded) {
                    var poppedAny = false
                    repeat(18) {
                        if (NativeLibretroBridge.nativePopRewindSnapshot()) poppedAny = true
                    }
                    if (poppedAny) {
                        Toast.makeText(this, "Перемотка назад выполнена", Toast.LENGTH_SHORT).show()
                        dialog.dismiss()
                    } else {
                        Toast.makeText(this, "Буфер перемотки пуст", Toast.LENGTH_SHORT).show()
                    }
                }
            },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1.15f).apply { rightMargin = dp(6f) }
        )
        timeToolsRow.addView(
            createIconActionButton(StormVectorIcons.CAMERA, "Скриншот", isAccent = false) {
                captureScreenshotToGallery(snapshotBeforePause)
            },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 0.95f)
        )
        speedCard.addView(timeToolsRow, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(6f) })

        speedCard.addView(
            createIconActionButton(StormVectorIcons.REFRESH, "Перезапуск игры", isAccent = false) {
                if (NativeLibretroBridge.isLibraryLoaded && coreLoadedOk) {
                    NativeLibretroBridge.nativeReset()
                }
                dialog.dismiss()
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(40f))
        )
        body.addView(speedCard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(10f) })

        // 4. IN-GAME CHEATS (.CHT) & LIVE RAM SCANNER
        val cheatCard = createStudioGroupCard()
        cheatCard.addView(createSectionLabel("Читы и живой сканер памяти RAM"))
        val cheatBtnLabelTvHolder = arrayOfNulls<TextView>(1)
        val cheatRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        val activeCheatCount = gameCheatEntries.count { it.isEnabled }
        val cheatsBtn = createIconActionButton(
            StormVectorIcons.CHEATS,
            "Читы .cht ($activeCheatCount/${gameCheatEntries.size})",
            isAccent = activeCheatCount > 0,
            outLabelRef = cheatBtnLabelTvHolder
        ) {
            showInGameCheatsDialog {
                val nowActive = gameCheatEntries.count { it.isEnabled }
                cheatBtnLabelTvHolder[0]?.text = "Читы .cht ($nowActive/${gameCheatEntries.size})"
            }
        }
        cheatRow.addView(cheatsBtn, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply { rightMargin = dp(6f) })
        cheatRow.addView(
            createIconActionButton(StormVectorIcons.RAM, "Сканер RAM", isAccent = frozenRamAddresses.isNotEmpty()) {
                showLiveRamScannerDialog()
            },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        )
        cheatCard.addView(cheatRow)
        body.addView(cheatCard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(10f) })

        // 5. LIVE 3D TOUCH GAMEPAD & DISPLAY TWEAKER
        val touchCard = createStudioGroupCard()
        touchCard.addView(createSectionLabel("3D Геймпад: цветовая тема кнопок"))
        val colorThemeOptions = MobileTouchGamepadOverlay.ButtonColorTheme.entries.mapIndexed { idx, th ->
            idx to th.displayName
        }
        val initialThemeIdx = MobileTouchGamepadOverlay.ButtonColorTheme.entries.indexOf(touchOverlay.buttonColorTheme).coerceAtLeast(0)
        touchCard.addView(buildHorizontalModeChips(colorThemeOptions, initialThemeIdx) { idx ->
            val chosen = MobileTouchGamepadOverlay.ButtonColorTheme.entries[idx]
            touchOverlay.setButtonColorTheme(chosen, persist = true)
        })

        touchCard.addView(createSectionLabel("Раскладка геймпада"))
        val presetOptions = listOf(
            MobileTouchGamepadOverlay.LayoutProfile.CONSOLE_PRO,
            MobileTouchGamepadOverlay.LayoutProfile.ARCADE_6BTN,
            MobileTouchGamepadOverlay.LayoutProfile.SNES,
            MobileTouchGamepadOverlay.LayoutProfile.GENESIS_6BTN,
            MobileTouchGamepadOverlay.LayoutProfile.NES,
            MobileTouchGamepadOverlay.LayoutProfile.RPG_MINIMAL,
            MobileTouchGamepadOverlay.LayoutProfile.HIDDEN
        )
        val presetChipPairs = presetOptions.mapIndexed { i, lp -> i to lp.displayName }
        val curPresetIdx = presetOptions.indexOf(touchOverlay.layoutProfile).coerceAtLeast(0)
        touchCard.addView(buildHorizontalModeChips(presetChipPairs, curPresetIdx) { idx ->
            val prof = presetOptions[idx]
            touchOverlay.setLayoutProfile(prof, persistPresetPref = true)
            if (prof != MobileTouchGamepadOverlay.LayoutProfile.HIDDEN) {
                touchOverlay.setPhysicalControllerActive(false)
            }
            syncViewportBoundsWithHandheldDeck()
        })

        // Opacity Slider (25% - 100%)
        touchCard.addView(
            buildStyledSliderRow(
                title = "Прозрачность кнопок",
                minPct = 25,
                maxPct = 100,
                initialPct = (touchOverlay.overlayAlpha * 100f).roundToInt().coerceIn(25, 100),
                accent = accent
            ) { pct, persistNow ->
                touchOverlay.setOverlayAlpha(pct / 100f, persist = persistNow)
            }
        )

        // Scale Slider (70% - 140%)
        touchCard.addView(
            buildStyledSliderRow(
                title = "Масштаб кнопок",
                minPct = 70,
                maxPct = 140,
                initialPct = (touchOverlay.buttonScale * 100f).roundToInt().coerceIn(70, 140),
                accent = accent
            ) { pct, persistNow ->
                touchOverlay.setButtonScale(pct / 100f, persist = persistNow)
            }
        )

        // Gyroscope Tilt-to-Stick Option
        touchCard.addView(createSectionLabel("Гироскоп (наклон корпуса в стик)"))
        val gyroOptions = listOf(
            GYRO_OFF to "Выкл",
            GYRO_LEFT_STICK to "Левый стик",
            GYRO_RIGHT_STICK to "Правый стик"
        )
        touchCard.addView(buildHorizontalModeChips(gyroOptions, gyroTiltMode) { mode ->
            persistGyroMode(mode)
            if (mode != GYRO_OFF) {
                Toast.makeText(this, "Гироскоп откалиброван по текущему наклону", Toast.LENGTH_SHORT).show()
            }
        })

        // Edit Button Positions button
        touchCard.addView(
            createIconActionButton(StormVectorIcons.SLIDERS, "Настроить позиции кнопок", isAccent = false) {
                if (touchOverlay.layoutProfile == MobileTouchGamepadOverlay.LayoutProfile.HIDDEN) {
                    touchOverlay.setLayoutProfile(MobileTouchGamepadOverlay.LayoutProfile.resolveForPlatform(platformName), persistPresetPref = true)
                }
                touchOverlay.setPhysicalControllerActive(false)
                touchOverlay.isEditMode = true
                dialog.dismiss()
                emuPaused = false
                updateGyroRegistration()
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(42f)).apply { topMargin = dp(2f) }
        )
        body.addView(touchCard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(10f) })

        // 6. VIDEO & GRAPHICS STUDIO CONTROLS (Grouped Sections + Styled Dropdowns)
        val displayCard = createStudioGroupCard()
        displayCard.addView(createSectionLabel("Видео и графика"))

        // 6.1 Shader Mode Dropdown (0..7)
        val shaderLabels = listOf(
            "Без фильтра",
            "CRT Royale",
            "CRT Geom",
            "CRT Lottes",
            "LCD Grid",
            "xBRZ 4x / Мягкий",
            "VHS / NTSC",
            "Scanlines (Тонкие)"
        )
        val shaderModeMap = listOf(
            NativeLibretroBridge.SHADER_NONE,
            NativeLibretroBridge.SHADER_CRT_ROYALE,
            NativeLibretroBridge.SHADER_CRT_GEOM,
            NativeLibretroBridge.SHADER_CRT_LOTTES,
            NativeLibretroBridge.SHADER_LCD_GRID,
            NativeLibretroBridge.SHADER_SMOOTH_SOFT,
            NativeLibretroBridge.SHADER_VHS_NTSC,
            NativeLibretroBridge.SHADER_SCANLINES_SUBTLE
        )
        val curShaderRaw = if (NativeLibretroBridge.isLibraryLoaded) NativeLibretroBridge.nativeGetShaderMode() else resolveInitialShaderMode()
        val curShaderIdx = shaderModeMap.indexOf(curShaderRaw).coerceAtLeast(0)

        displayCard.addView(createStyledDropdown("Фильтр / Шейдер", shaderLabels, curShaderIdx, pal, accent) { pos ->
            val chosenMode = shaderModeMap[pos]
            if (NativeLibretroBridge.isLibraryLoaded) {
                NativeLibretroBridge.nativeSetShaderMode(chosenMode)
            }
            persistShaderMode(chosenMode)
        })

        // 6.2 Screen Bezel Overlay Dropdown
        val bezelPresets = listOf(
            "Без рамки",
            "Arcade Neon",
            "Sony CRT",
            "Handheld GBA",
            "SNES Classic",
            "Mega Drive",
            "STORM Studio"
        )
        val curBezelPreset = unifiedPrefs().getString("bezelPreset", "none") ?: "none"
        val curBezelIdx = when (curBezelPreset) {
            "Arcade Neon" -> 1
            "Sony CRT" -> 2
            "Handheld GBA" -> 3
            "SNES Classic" -> 4
            "Mega Drive" -> 5
            "STORM Studio" -> 6
            else -> 0
        }
        displayCard.addView(createStyledDropdown("Рамка экрана", bezelPresets, curBezelIdx, pal, accent) { pos ->
            val chosenPreset = bezelPresets[pos]
            unifiedPrefs().edit().putString("bezelPreset", if (pos == 0) "none" else chosenPreset).apply()
            applyOptionalBezelOverlay(if (pos == 0) "none" else chosenPreset)
        })

        // 6.3 Internal Resolution Scale (1x - 4x)
        val resOptions = listOf("1x (Нативное)", "2x (HD 720p)", "3x (Full HD)", "4x (Ultra HD 4K)")
        val curScale = if (NativeLibretroBridge.isLibraryLoaded) NativeLibretroBridge.nativeGetInternalScale().coerceIn(1, 4) else 1
        displayCard.addView(createStyledDropdown("Разрешение (1x-4x)", resOptions, curScale - 1, pal, accent) { pos ->
            val scaleVal = pos + 1
            if (NativeLibretroBridge.isLibraryLoaded) {
                NativeLibretroBridge.nativeSetInternalScale(scaleVal)
            }
            unifiedPrefs().edit().putInt("internalScale", scaleVal).apply()
        })

        // 6.4 Aspect Ratio Mode
        val aspectLabels = listOf("Авто (Core Fit)", "4:3 (Классика)", "16:9 (Широкий)", "На весь экран", "1:1 / Целые пиксели")
        val aspectValues = listOf(
            NativeLibretroBridge.ASPECT_CORE_FIT,
            NativeLibretroBridge.ASPECT_4_3,
            NativeLibretroBridge.ASPECT_16_9,
            NativeLibretroBridge.ASPECT_STRETCH,
            NativeLibretroBridge.ASPECT_INTEGER
        )
        val curAspectRaw = if (NativeLibretroBridge.isLibraryLoaded) NativeLibretroBridge.nativeGetAspectMode() else resolveInitialAspectMode()
        val curAspectIdx = aspectValues.indexOf(curAspectRaw).coerceAtLeast(0)
        displayCard.addView(createStyledDropdown("Соотношение сторон", aspectLabels, curAspectIdx, pal, accent) { pos ->
            val modeVal = aspectValues[pos]
            if (NativeLibretroBridge.isLibraryLoaded) {
                NativeLibretroBridge.nativeSetAspectMode(modeVal)
            }
            persistAspectMode(modeVal)
        })

        // 6.5 Viewport Zoom Mode
        val zoomLabels = listOf("Авто (По размеру)", "Целые множители (Integer)", "100% (Оригинал 1:1)")
        val curZoom = if (NativeLibretroBridge.isLibraryLoaded) NativeLibretroBridge.nativeGetViewportZoom().coerceIn(0, 2) else 0
        displayCard.addView(createStyledDropdown("Масштаб", zoomLabels, curZoom, pal, accent) { pos ->
            if (NativeLibretroBridge.isLibraryLoaded) {
                NativeLibretroBridge.nativeSetViewportZoom(pos)
            }
            unifiedPrefs().edit().putInt("viewportZoom", pos).apply()
        })

        body.addView(displayCard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(10f) })

        // 6.6 PERFORMANCE & SOUND STUDIO CONTROLS
        val perfCard = createStudioGroupCard()
        perfCard.addView(createSectionLabel("Производительность и звук"))

        val isEcoMode = unifiedPrefs().getBoolean("ecoAntiHeatMode", false)
        val ecoToggleRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        ecoToggleRow.addView(
            buildCompactToggleChip("Эко-режим (Меньше нагрева)", isEcoMode, accent) { on ->
                unifiedPrefs().edit().putBoolean("ecoAntiHeatMode", on).apply()
                Toast.makeText(this, if (on) "Эко-режим активен: низкий нагрев" else "Эко-режим выключен", Toast.LENGTH_SHORT).show()
            },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply { rightMargin = dp(6f) }
        )
        ecoToggleRow.addView(
            buildCompactToggleChip("FPS HUD", fpsHudText.visibility == View.VISIBLE, accent) { on ->
                fpsHudText.visibility = if (on) View.VISIBLE else View.GONE
                unifiedPrefs().edit().putBoolean("fpsHud", on).apply()
                legacyPrefs().edit().putBoolean("show_fps_hud", on).apply()
            },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        )
        perfCard.addView(ecoToggleRow, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(6f) })

        val extraTogglesRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        extraTogglesRow.addView(
            buildCompactToggleChip("Виброотклик", touchOverlay.hapticsEnabled, accent) { on ->
                touchOverlay.hapticsEnabled = on
                touchOverlay.saveLayouts()
            },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply { rightMargin = dp(6f) }
        )
        extraTogglesRow.addView(
            buildCompactToggleChip("Перемотка", rewindEnabled, accent) { on ->
                rewindEnabled = on
                unifiedPrefs().edit().putBoolean("rewindEnabled", on).apply()
                legacyPrefs().edit().putBoolean("rewind_enabled", on).apply()
            },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        )
        perfCard.addView(extraTogglesRow, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))

        body.addView(perfCard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(12f) })

        // 6.2 RetroAchievements Quick Viewer
        val achBtn = createIconActionButton(StormVectorIcons.STAR, "Достижения игры", isAccent = false) {
            showInGameAchievementsDialog()
        }.apply {
            background = StormThemeEngine.createVolumetricButtonDrawable(
                this@EmulationActivity,
                Color.rgb(24, 48, 64),
                Color.rgb(12, 28, 40),
                accent,
                12f
            )
        }
        body.addView(achBtn, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(8f) })

        // 7. EXIT TO STUDIO
        val exitBtn = createIconActionButton(StormVectorIcons.CLOSE, "Выйти в студию", isAccent = false) {
            dialog.dismiss()
            finishAndSave()
        }.apply {
            background = StormThemeEngine.createVolumetricButtonDrawable(
                this@EmulationActivity,
                Color.rgb(88, 24, 42),
                Color.rgb(48, 12, 24),
                Color.rgb(244, 63, 94),
                12f
            )
        }
        body.addView(exitBtn, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))

        scroll.addView(body)
        drawerCard.addView(scroll)

        scrim.addView(
            drawerCard,
            FrameLayout.LayoutParams(drawerWidth, ViewGroup.LayoutParams.MATCH_PARENT, Gravity.END)
        )

        dialog.setContentView(scrim)
        quickDrawerDialog = dialog
        dialog.show()

        // Smooth 60-120 FPS slide-over entrance choreography
        scrim.animate().alpha(1f).setDuration(180L).start()
        drawerCard.animate()
            .translationX(0f)
            .setDuration(220L)
            .setInterpolator(DecelerateInterpolator())
            .start()
    }

    private fun showInGameAchievementsDialog() {
        val pal = StormThemeEngine.currentTheme
        val accent = StormThemeEngine.activeAccentColor

        val dlg = Dialog(this, android.R.style.Theme_Translucent_NoTitleBar_Fullscreen)
        dlg.setCanceledOnTouchOutside(true)

        val backdrop = FrameLayout(this).apply {
            setBackgroundColor(Color.argb(175, 4, 3, 12))
            setPadding(dp(16f), dp(20f), dp(16f), dp(20f))
            setOnClickListener { dlg.dismiss() }
        }

        val card = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            isClickable = true
            background = createGlassStudioDrawerDrawable(accent, cornerDp = 18f)
            setPadding(dp(16f), dp(16f), dp(16f), dp(16f))
        }

        val header = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        val title = TextView(this).apply {
            text = "Достижения - $romName"
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 15.5f)
            maxLines = 1
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            StormThemeEngine.applyFont(this, bold = true)
        }
        header.addView(title)
        header.addView(
            createIconActionButton(StormVectorIcons.CLOSE, "", isAccent = false) { dlg.dismiss() },
            LinearLayout.LayoutParams(dp(34f), dp(34f))
        )
        card.addView(header)

        val statusTv = TextView(this).apply {
            text = "Загрузка достижений RetroAchievements..."
            setTextColor(pal.textSecondary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            setPadding(0, dp(8f), 0, dp(8f))
            StormThemeEngine.applyFont(this, bold = false)
        }
        card.addView(statusTv)

        val scroll = ScrollView(this).apply {
            isFillViewport = true
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f)
        }
        val listContainer = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, dp(4f), 0, dp(8f))
        }
        scroll.addView(listContainer)
        card.addView(scroll)

        backdrop.addView(
            card,
            FrameLayout.LayoutParams(
                minOf((resources.displayMetrics.widthPixels * 0.92f).roundToInt(), dp(520f)),
                minOf((resources.displayMetrics.heightPixels * 0.85f).roundToInt(), dp(640f)),
                Gravity.CENTER
            )
        )
        dlg.setContentView(backdrop)
        dlg.show()

        Executors.newSingleThreadExecutor().execute {
            val up = unifiedPrefs()
            val user = up.getString("raUsername", "") ?: ""
            val token = up.getString("raToken", "") ?: ""
            val isHardcore = up.getBoolean("raHardcore", false)

            var achList = JSONArray()
            var totalPts = 0

            if (user.isNotBlank()) {
                try {
                    val romFile = File(romPath)
                    var raGameId = 0
                    if (romFile.exists()) {
                        val md = MessageDigest.getInstance("MD5")
                        val buf = ByteArray(minOf(romFile.length(), 2L * 1024L * 1024L).toInt())
                        RandomAccessFile(romFile, "r").use { it.readFully(buf) }
                        val md5Str = md.digest(buf).joinToString("") { "%02x".format(it) }
                        val idConn = URL("https://retroachievements.org/dorequest.php?r=gameid&m=$md5Str").openConnection() as HttpURLConnection
                        idConn.connectTimeout = 3500
                        idConn.readTimeout = 3500
                        idConn.setRequestProperty("User-Agent", "STORM-RETROARCH/2.7.4")
                        if (idConn.responseCode in 200..299) {
                            val idText = idConn.inputStream.bufferedReader().readText()
                            try {
                                raGameId = JSONObject(idText).optInt("GameID", 0)
                            } catch (_: Throwable) {
                                raGameId = idText.trim().toIntOrNull() ?: 0
                            }
                        }
                    }

                    if (raGameId > 0 && token.isNotBlank()) {
                        val encU = URLEncoder.encode(user, "UTF-8")
                        val encT = URLEncoder.encode(token, "UTF-8")
                        val progConn = URL("https://retroachievements.org/API/API_GetGameInfoAndUserProgress.php?u=$encU&y=$encT&g=$raGameId").openConnection() as HttpURLConnection
                        progConn.connectTimeout = 4000
                        progConn.readTimeout = 4000
                        progConn.setRequestProperty("User-Agent", "STORM-RETROARCH/2.7.4")
                        if (progConn.responseCode in 200..299) {
                            val respJson = JSONObject(progConn.inputStream.bufferedReader().readText())
                            val achMap = respJson.optJSONObject("Achievements")
                            if (achMap != null) {
                                val kIter = achMap.keys()
                                while (kIter.hasNext()) {
                                    val k = kIter.next()
                                    val item = achMap.getJSONObject(k)
                                    val date = if (isHardcore) item.optString("DateEarnedHardcore") else item.optString("DateEarned")
                                    val unlocked = date.isNotBlank() && date != "null"
                                    achList.put(
                                        JSONObject()
                                            .put("title", item.optString("Title", ""))
                                            .put("desc", item.optString("Description", ""))
                                            .put("pts", item.optInt("Points", 0))
                                            .put("unlocked", unlocked)
                                    )
                                    if (unlocked) totalPts += item.optInt("Points", 0)
                                }
                            }
                        }
                    }
                } catch (_: Throwable) {
                }
            }

            runOnUiThread {
                if (dlg.isShowing) {
                    listContainer.removeAllViews()
                    if (achList.length() == 0) {
                        statusTv.text = if (user.isBlank()) {
                            "Профиль RetroAchievements не подключен в Студии (Вкладка 'Сообщество')."
                        } else {
                            "Для этой игры или версии ROM достижения не найдены на сервере RA."
                        }
                    } else {
                        val unlockedCount = (0 until achList.length()).count { achList.getJSONObject(it).optBoolean("unlocked") }
                        statusTv.text = "Разблокировано: $unlockedCount / ${achList.length()} ($totalPts очков) • $user"

                        for (i in 0 until achList.length()) {
                            val a = achList.getJSONObject(i)
                            val isUnlocked = a.optBoolean("unlocked")
                            val aTitle = a.optString("title")
                            val aDesc = a.optString("desc")
                            val aPts = a.optInt("pts")

                            val row = LinearLayout(this).apply {
                                orientation = LinearLayout.HORIZONTAL
                                gravity = Gravity.CENTER_VERTICAL
                                setPadding(dp(10f), dp(8f), dp(10f), dp(8f))
                                background = createGlassStudioDrawerDrawable(
                                    if (isUnlocked) Color.rgb(16, 185, 129) else Color.rgb(60, 60, 80),
                                    cornerDp = 10f
                                )
                            }
                            val textCol = LinearLayout(this).apply {
                                orientation = LinearLayout.VERTICAL
                                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                            }
                            val tView = TextView(this).apply {
                                text = aTitle
                                setTextColor(if (isUnlocked) Color.rgb(250, 250, 250) else pal.textSecondary)
                                setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f)
                                StormThemeEngine.applyFont(this, bold = true)
                            }
                            val dView = TextView(this).apply {
                                text = aDesc
                                setTextColor(pal.textSecondary)
                                setTextSize(TypedValue.COMPLEX_UNIT_SP, 10.5f)
                                StormThemeEngine.applyFont(this, bold = false)
                            }
                            textCol.addView(tView)
                            textCol.addView(dView)
                            row.addView(textCol)

                            val ptsPill = TextView(this).apply {
                                text = "+$aPts"
                                setTextColor(if (isUnlocked) Color.rgb(52, 211, 153) else pal.textSecondary)
                                setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f)
                                fontFeatureSettings = "tnum"
                                StormThemeEngine.applyFont(this, bold = true)
                            }
                            row.addView(ptsPill)

                            listContainer.addView(
                                row,
                                LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                                    bottomMargin = dp(6f)
                                }
                            )
                        }
                    }
                }
            }
        }
    }

    // === Glassmorphic In-Game Cheats (.cht) Modal ===

    private fun showInGameCheatsDialog(onUpdated: () -> Unit = {}) {
        val pal = StormThemeEngine.currentTheme
        val accent = StormThemeEngine.activeAccentColor

        val dlg = Dialog(this, android.R.style.Theme_Translucent_NoTitleBar_Fullscreen)
        dlg.setCanceledOnTouchOutside(true)

        val backdrop = FrameLayout(this).apply {
            setBackgroundColor(Color.argb(175, 4, 3, 12))
            setPadding(dp(18f), dp(24f), dp(18f), dp(24f))
            setOnClickListener { dlg.dismiss() }
        }

        val card = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            isClickable = true
            background = createGlassStudioDrawerDrawable(accent, cornerDp = 18f)
            setPadding(dp(16f), dp(16f), dp(16f), dp(16f))
        }

        val header = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        val title = TextView(this).apply {
            text = "Читы (.cht) - $romName"
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 15.5f)
            maxLines = 1
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            StormThemeEngine.applyFont(this, bold = true)
        }
        header.addView(title)
        header.addView(
            createIconActionButton(StormVectorIcons.CLOSE, "", isAccent = false) { dlg.dismiss() },
            LinearLayout.LayoutParams(dp(34f), dp(34f))
        )
        card.addView(header)

        val listContainer = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, dp(8f), 0, dp(8f))
        }

        fun syncAndApplyLive() {
            gameCheatFile?.let { MobileCheatAndBiosManager.saveChtFile(it, gameCheatEntries) }
            pushActiveCheatsToCore()
            onUpdated()
        }

        fun rebuildRows() {
            listContainer.removeAllViews()
            if (gameCheatEntries.isEmpty()) {
                val empty = TextView(this).apply {
                    text = "Для этой игры еще нет сохраненных кодов .cht. Добавьте код GameGenie / ActionReplay / ProActionReplay ниже - он применится мгновенно без выхода из игры."
                    setTextColor(pal.textSecondary)
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
                    setPadding(dp(4f), dp(12f), dp(4f), dp(12f))
                }
                listContainer.addView(empty)
            } else {
                for (entry in gameCheatEntries) {
                    val row = LinearLayout(this).apply {
                        orientation = LinearLayout.HORIZONTAL
                        gravity = Gravity.CENTER_VERTICAL
                        background = StormThemeEngine.createVolumetricCardDrawable(this@EmulationActivity, cornerDp = 10f)
                        setPadding(dp(10f), dp(8f), dp(10f), dp(8f))
                    }
                    val info = LinearLayout(this).apply {
                        orientation = LinearLayout.VERTICAL
                        layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                            rightMargin = dp(8f)
                        }
                    }
                    val descTv = TextView(this).apply {
                        text = entry.description
                        setTextColor(pal.textPrimary)
                        setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f)
                        StormThemeEngine.applyFont(this, bold = true)
                    }
                    val codeTv = TextView(this).apply {
                        text = entry.code
                        setTextColor(pal.textSecondary)
                        setTextSize(TypedValue.COMPLEX_UNIT_SP, 10.5f)
                        fontFeatureSettings = "tnum"
                    }
                    info.addView(descTv)
                    info.addView(codeTv)

                    val stateBtn = TextView(this).apply {
                        gravity = Gravity.CENTER
                        setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f)
                        setPadding(dp(12f), dp(6f), dp(12f), dp(6f))
                        StormThemeEngine.applyFont(this, bold = true)

                        fun renderToggleVisual() {
                            text = if (entry.isEnabled) "ВКЛ" else "ВЫКЛ"
                            background = StormThemeEngine.createFilterChipDrawable(this@EmulationActivity, entry.isEnabled, accent, 9f)
                            setTextColor(if (entry.isEnabled) StormThemeEngine.contrastTextForFill(accent) else pal.textSecondary)
                        }
                        renderToggleVisual()
                        setOnClickListener {
                            entry.isEnabled = !entry.isEnabled
                            renderToggleVisual()
                            syncAndApplyLive()
                        }
                    }

                    row.addView(info)
                    row.addView(stateBtn)
                    listContainer.addView(row, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                        bottomMargin = dp(6f)
                    })
                }
            }
        }
        rebuildRows()

        val scroll = ScrollView(this).apply {
            isVerticalScrollBarEnabled = false
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(220f)).apply {
                topMargin = dp(8f)
                bottomMargin = dp(8f)
            }
            addView(listContainer)
        }
        card.addView(scroll)

        val descInput = EditText(this).apply {
            hint = "Название чита (напр. Бесконечные жизни)"
            setHintTextColor(pal.textSecondary)
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            background = StormThemeEngine.createRecessedInputDrawable(this@EmulationActivity)
            setPadding(dp(10f), dp(7f), dp(10f), dp(7f))
        }
        val codeInput = EditText(this).apply {
            hint = "Код чита (напр. 7E0DBE09 или 0099-01-63)"
            setHintTextColor(pal.textSecondary)
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            background = StormThemeEngine.createRecessedInputDrawable(this@EmulationActivity)
            setPadding(dp(10f), dp(7f), dp(10f), dp(7f))
        }
        card.addView(descInput, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(6f) })
        card.addView(codeInput, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(8f) })

        val btnBar = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        btnBar.addView(
            createIconActionButton(StormVectorIcons.CHEATS, "Добавить код", isAccent = false) {
                val c = codeInput.text.toString().trim()
                if (c.isNotEmpty()) {
                    val d = descInput.text.toString().trim().ifBlank { "Чит #${gameCheatEntries.size + 1}" }
                    gameCheatEntries.add(MobileCheatEntry(gameCheatEntries.size, d, d, c, true))
                    descInput.setText("")
                    codeInput.setText("")
                    rebuildRows()
                    syncAndApplyLive()
                }
            },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply { rightMargin = dp(6f) }
        )
        btnBar.addView(
            createIconActionButton(StormVectorIcons.CHECK, "Применить и закрыть", isAccent = true) {
                syncAndApplyLive()
                Toast.makeText(this, "Читы применены к ядру", Toast.LENGTH_SHORT).show()
                dlg.dismiss()
            },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1.15f)
        )
        card.addView(btnBar)

        val modalW = minOf((resources.displayMetrics.widthPixels * 0.92f).roundToInt(), dp(480f))
        backdrop.addView(card, FrameLayout.LayoutParams(modalW, ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.CENTER))
        dlg.setContentView(backdrop)
        dlg.show()
    }

    // === Glassmorphic Live Native RAM Scanner Modal (8 / 16 / 32-bit) ===

    private fun showLiveRamScannerDialog() {
        val pal = StormThemeEngine.currentTheme
        val accent = StormThemeEngine.activeAccentColor

        val dlg = Dialog(this, android.R.style.Theme_Translucent_NoTitleBar_Fullscreen)
        dlg.setCanceledOnTouchOutside(true)

        val backdrop = FrameLayout(this).apply {
            setBackgroundColor(Color.argb(175, 4, 3, 12))
            setPadding(dp(16f), dp(20f), dp(16f), dp(20f))
            setOnClickListener { dlg.dismiss() }
        }

        val card = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            isClickable = true
            background = createGlassStudioDrawerDrawable(accent, cornerDp = 18f)
            setPadding(dp(16f), dp(16f), dp(16f), dp(16f))
        }

        val ramSize = if (NativeLibretroBridge.isLibraryLoaded) NativeLibretroBridge.nativeGetSystemRamSize() else 0
        val header = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        val title = TextView(this).apply {
            text = "Живой сканер RAM (${ramSize / 1024} КБ)"
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 15.5f)
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            StormThemeEngine.applyFont(this, bold = true)
        }
        header.addView(title)
        header.addView(
            createIconActionButton(StormVectorIcons.CLOSE, "", isAccent = false) { dlg.dismiss() },
            LinearLayout.LayoutParams(dp(34f), dp(34f))
        )
        card.addView(header, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(8f) })

        val widthOptions = listOf(1 to "8-бит (1B)", 2 to "16-бит (2B)", 4 to "32-бит (4B)")
        card.addView(buildHorizontalModeChips(widthOptions, ramScanWidthBytes) { w -> ramScanWidthBytes = w })

        val valueInput = EditText(this).apply {
            hint = "Искомое / новое значение (напр. 99 или 5000)"
            inputType = InputType.TYPE_CLASS_NUMBER or InputType.TYPE_NUMBER_FLAG_SIGNED
            setHintTextColor(pal.textSecondary)
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f)
            fontFeatureSettings = "tnum"
            background = StormThemeEngine.createRecessedInputDrawable(this@EmulationActivity)
            setPadding(dp(12f), dp(8f), dp(12f), dp(8f))
        }
        card.addView(valueInput, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(8f) })

        val statusRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        val matchesLabel = TextView(this).apply {
            text = "Найдено адресов: 0"
            setTextColor(StormThemeEngine.lighten(accent, 0.22f))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            fontFeatureSettings = "tnum"
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            StormThemeEngine.applyFont(this, bold = true)
        }
        val clearFreezeBtn = createSimpleChipButton("Сбросить заморозку (${frozenRamAddresses.size})") {
            if (NativeLibretroBridge.isLibraryLoaded) {
                NativeLibretroBridge.nativeRamClearFrozen()
            }
            frozenRamAddresses.clear()
            Toast.makeText(this, "Все замороженные адреса освобождены", Toast.LENGTH_SHORT).show()
        }
        statusRow.addView(matchesLabel)
        statusRow.addView(clearFreezeBtn)
        card.addView(statusRow, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(6f) })

        val candidatesList = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
        }

        fun renderCandidates(totalMatches: Int) {
            matchesLabel.text = "Найдено адресов: $totalMatches"
            clearFreezeBtn.text = "Сброс заморозки (${frozenRamAddresses.size})"
            candidatesList.removeAllViews()
            if (!NativeLibretroBridge.isLibraryLoaded) return
            val packedArr = NativeLibretroBridge.nativeRamGetCandidates(64)
            for (packed in packedArr) {
                val addr = NativeLibretroBridge.unpackCandidateAddress(packed)
                val curVal = NativeLibretroBridge.unpackCandidateValue(packed)
                val row = LinearLayout(this).apply {
                    orientation = LinearLayout.HORIZONTAL
                    gravity = Gravity.CENTER_VERTICAL
                    background = StormThemeEngine.createVolumetricCardDrawable(this@EmulationActivity, cornerDp = 9f)
                    setPadding(dp(10f), dp(6f), dp(8f), dp(6f))
                }
                val addrTv = TextView(this).apply {
                    text = String.format(Locale.US, "0x%06X  =  %d", addr, curVal)
                    fontFeatureSettings = "tnum"
                    setTextColor(pal.textPrimary)
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
                    StormThemeEngine.applyFont(this, bold = true)
                    layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                }
                val writeBtn = TextView(this).apply {
                    text = "Записать"
                    setTextColor(StormThemeEngine.contrastTextForFill(accent))
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f)
                    StormThemeEngine.applyFont(this, bold = true)
                    setPadding(dp(10f), dp(5f), dp(10f), dp(5f))
                    background = StormThemeEngine.createAccentButtonDrawable(this@EmulationActivity, accent, 8f)
                    setOnClickListener {
                        val targetV = valueInput.text.toString().trim().toLongOrNull() ?: curVal
                        NativeLibretroBridge.nativeRamWriteValue(addr, ramScanWidthBytes, targetV)
                        addrTv.text = String.format(Locale.US, "0x%06X  =  %d", addr, targetV)
                        Toast.makeText(this@EmulationActivity, String.format(Locale.US, "0x%06X <- %d", addr, targetV), Toast.LENGTH_SHORT).show()
                    }
                }
                val isFrozen = frozenRamAddresses.contains(addr)
                val freezeBtn = TextView(this).apply {
                    text = if (isFrozen) "Заморожен" else "Заморозить"
                    setTextColor(if (isFrozen) StormThemeEngine.contrastTextForFill(accent) else pal.textPrimary)
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f)
                    StormThemeEngine.applyFont(this, bold = true)
                    setPadding(dp(10f), dp(5f), dp(10f), dp(5f))
                    background = StormThemeEngine.createFilterChipDrawable(this@EmulationActivity, isFrozen, accent, 8f)
                    setOnClickListener {
                        val nextFreeze = !frozenRamAddresses.contains(addr)
                        val targetV = valueInput.text.toString().trim().toLongOrNull() ?: curVal
                        NativeLibretroBridge.nativeRamSetFrozen(addr, ramScanWidthBytes, targetV, nextFreeze)
                        if (nextFreeze) frozenRamAddresses.add(addr) else frozenRamAddresses.remove(addr)
                        text = if (nextFreeze) "Заморожен" else "Заморозить"
                        setTextColor(if (nextFreeze) StormThemeEngine.contrastTextForFill(accent) else pal.textPrimary)
                        background = StormThemeEngine.createFilterChipDrawable(this@EmulationActivity, nextFreeze, accent, 8f)
                        clearFreezeBtn.text = "Сброс заморозки (${frozenRamAddresses.size})"
                    }
                }
                row.addView(addrTv)
                row.addView(writeBtn, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { rightMargin = dp(6f) })
                row.addView(freezeBtn)
                candidatesList.addView(row, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(5f) })
            }
        }

        val scanRow1 = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        scanRow1.addView(
            createIconActionButton(StormVectorIcons.SEARCH, "Первый поиск", isAccent = true) {
                val v = valueInput.text.toString().trim().toLongOrNull() ?: 0L
                val count = NativeLibretroBridge.nativeRamScanFirst(ramScanWidthBytes, v)
                renderCandidates(count)
            },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply { rightMargin = dp(6f) }
        )
        scanRow1.addView(
            createIconActionButton(StormVectorIcons.CHECK, "След. поиск (Точно)", isAccent = false) {
                val v = valueInput.text.toString().trim().toLongOrNull() ?: 0L
                val count = NativeLibretroBridge.nativeRamScanNext(NativeLibretroBridge.RAM_SCAN_EXACT, v)
                renderCandidates(count)
            },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1.15f)
        )
        card.addView(scanRow1, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(6f) })

        val scanRow2 = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        scanRow2.addView(
            createSimpleChipButton("Выросло") {
                renderCandidates(NativeLibretroBridge.nativeRamScanNext(NativeLibretroBridge.RAM_SCAN_INCREASED, 0L))
            },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply { rightMargin = dp(6f) }
        )
        scanRow2.addView(
            createSimpleChipButton("Уменьшилось") {
                renderCandidates(NativeLibretroBridge.nativeRamScanNext(NativeLibretroBridge.RAM_SCAN_DECREASED, 0L))
            },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply { rightMargin = dp(6f) }
        )
        scanRow2.addView(
            createSimpleChipButton("Изменилось") {
                renderCandidates(NativeLibretroBridge.nativeRamScanNext(NativeLibretroBridge.RAM_SCAN_CHANGED, 0L))
            },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        )
        card.addView(scanRow2)

        val candScroll = ScrollView(this).apply {
            isVerticalScrollBarEnabled = false
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(195f)).apply {
                topMargin = dp(8f)
                bottomMargin = dp(8f)
            }
            addView(candidatesList)
        }
        card.addView(candScroll)
        if (NativeLibretroBridge.isLibraryLoaded) {
            renderCandidates(NativeLibretroBridge.nativeRamGetCandidates(64).size)
        }

        val modalW = minOf((resources.displayMetrics.widthPixels * 0.94f).roundToInt(), dp(500f))
        backdrop.addView(card, FrameLayout.LayoutParams(modalW, ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.CENTER))
        dlg.setContentView(backdrop)
        dlg.show()
    }

    // === Glassmorphic & Volumetric 3D UI Builders (Zero Emojis, Single Icon Mandate) ===

    private fun createGlassStudioDrawerDrawable(accent: Int, cornerDp: Float = 0f): GradientDrawable {
        val pal = StormThemeEngine.currentTheme
        return GradientDrawable(
            GradientDrawable.Orientation.TL_BR,
            intArrayOf(
                StormThemeEngine.withAlpha(StormThemeEngine.lighten(pal.bgElevated, 0.08f), 246),
                StormThemeEngine.withAlpha(pal.bgSurface, 248),
                StormThemeEngine.withAlpha(StormThemeEngine.darken(pal.bgMain, 0.14f), 252)
            )
        ).apply {
            if (cornerDp > 0f) cornerRadius = dpF(cornerDp)
            setStroke(dp(1.3f), StormThemeEngine.withAlpha(accent, 195))
        }
    }

    private fun createStudioGroupCard(): LinearLayout {
        return LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = StormThemeEngine.createVolumetricCardDrawable(this@EmulationActivity, cornerDp = 14f)
            setPadding(dp(12f), dp(10f), dp(12f), dp(10f))
        }
    }

    private fun buildStyledSliderRow(
        title: String,
        minPct: Int,
        maxPct: Int,
        initialPct: Int,
        accent: Int,
        onChanged: (pct: Int, persistNow: Boolean) -> Unit
    ): View {
        val pal = StormThemeEngine.currentTheme
        val wrap = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, dp(2f), 0, dp(6f))
        }
        val topRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        val labelTv = TextView(this).apply {
            text = title
            setTextColor(pal.textSecondary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f)
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            StormThemeEngine.applyFont(this, bold = true)
        }
        val valTv = TextView(this).apply {
            text = "$initialPct%"
            fontFeatureSettings = "tnum"
            setTextColor(StormThemeEngine.lighten(accent, 0.25f))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f)
            StormThemeEngine.applyFont(this, bold = true)
        }
        topRow.addView(labelTv)
        topRow.addView(valTv)
        wrap.addView(topRow)

        val seek = SeekBar(this).apply {
            max = maxPct - minPct
            progress = (initialPct - minPct).coerceIn(0, maxPct - minPct)
            progressTintList = ColorStateList.valueOf(accent)
            thumbTintList = ColorStateList.valueOf(StormThemeEngine.lighten(accent, 0.25f))
            progressBackgroundTintList = ColorStateList.valueOf(StormThemeEngine.withAlpha(pal.textSecondary, 75))
            setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
                override fun onProgressChanged(seekBar: SeekBar?, progress: Int, fromUser: Boolean) {
                    val pct = minPct + progress
                    valTv.text = "$pct%"
                    if (fromUser) onChanged(pct, false)
                }

                override fun onStartTrackingTouch(seekBar: SeekBar?) {}

                override fun onStopTrackingTouch(seekBar: SeekBar?) {
                    val pct = minPct + (seekBar?.progress ?: 0)
                    onChanged(pct, true)
                }
            })
        }
        wrap.addView(seek, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        return wrap
    }

    private fun buildCompactToggleChip(
        label: String,
        initialOn: Boolean,
        accent: Int,
        onToggle: (Boolean) -> Unit
    ): TextView {
        val pal = StormThemeEngine.currentTheme
        var state = initialOn
        return TextView(this).apply {
            gravity = Gravity.CENTER
            minimumHeight = dp(40f)
            maxLines = 2
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f)
            setPadding(dp(8f), dp(6f), dp(8f), dp(6f))
            StormThemeEngine.applyFont(this, bold = true)

            fun render() {
                text = label
                background = StormThemeEngine.createFilterChipDrawable(this@EmulationActivity, state, accent, 10f)
                setTextColor(if (state) StormThemeEngine.contrastTextForFill(accent) else pal.textSecondary)
            }
            render()
            StormThemeEngine.attachSpringTouchFeedback(this)
            setOnClickListener {
                state = !state
                render()
                onToggle(state)
            }
        }
    }

    private fun buildHorizontalModeChips(
        options: List<Pair<Int, String>>,
        initialValue: Int,
        onSelected: (Int) -> Unit
    ): View {
        val pal = StormThemeEngine.currentTheme
        val accent = StormThemeEngine.activeAccentColor
        var activeVal = initialValue
        val row = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        val views = ArrayList<Pair<Int, TextView>>(options.size)

        fun updateVisuals() {
            for ((v, tv) in views) {
                val sel = v == activeVal
                tv.background = StormThemeEngine.createFilterChipDrawable(this, sel, accent, 10f)
                tv.setTextColor(if (sel) StormThemeEngine.contrastTextForFill(accent) else pal.textPrimary)
            }
        }

        for ((value, label) in options) {
            val chip = TextView(this).apply {
                text = label
                gravity = Gravity.CENTER
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f)
                setPadding(dp(12f), dp(7f), dp(12f), dp(7f))
                StormThemeEngine.applyFont(this, bold = true)
                StormThemeEngine.attachSpringTouchFeedback(this)
                setOnClickListener {
                    activeVal = value
                    updateVisuals()
                    onSelected(value)
                }
            }
            views.add(value to chip)
            row.addView(chip, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { rightMargin = dp(6f) })
        }
        updateVisuals()

        return HorizontalScrollView(this).apply {
            isHorizontalScrollBarEnabled = false
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                bottomMargin = dp(8f)
            }
            addView(row)
        }
    }

    private fun createSectionLabel(textStr: String): TextView {
        return TextView(this).apply {
            text = textStr
            setTextColor(StormThemeEngine.currentTheme.textSecondary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.2f)
            StormThemeEngine.applyFont(this, bold = true)
            setPadding(0, dp(2f), 0, dp(5f))
        }
    }

    private fun createSimpleChipButton(label: String, onClick: () -> Unit): TextView {
        val pal = StormThemeEngine.currentTheme
        return TextView(this).apply {
            text = label
            gravity = Gravity.CENTER
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f)
            setPadding(dp(10f), dp(5f), dp(10f), dp(5f))
            StormThemeEngine.applyFont(this, bold = true)
            background = StormThemeEngine.createSurfaceButtonDrawable(this@EmulationActivity, 10f)
            StormThemeEngine.attachSpringTouchFeedback(this)
            setOnClickListener { onClick() }
        }
    }

    private fun createIconActionButton(
        iconName: String,
        label: String,
        isAccent: Boolean,
        outLabelRef: Array<TextView?>? = null,
        onClick: () -> Unit
    ): LinearLayout {
        val pal = StormThemeEngine.currentTheme
        val accent = StormThemeEngine.activeAccentColor
        val fgColor = if (isAccent) StormThemeEngine.contrastTextForFill(accent) else pal.textPrimary

        return LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER
            minimumHeight = dp(42f)
            background = if (isAccent) {
                StormThemeEngine.createAccentButtonDrawable(this@EmulationActivity, accent, 12f)
            } else {
                StormThemeEngine.createSurfaceButtonDrawable(this@EmulationActivity, 12f)
            }
            setPadding(dp(10f), dp(6f), dp(10f), dp(6f))
            StormThemeEngine.attachSpringTouchFeedback(this)
            isClickable = true
            isFocusable = true
            setOnClickListener { onClick() }

            val iv = ImageView(this@EmulationActivity).apply {
                setImageDrawable(StormVectorIcons.create(this@EmulationActivity, iconName, fgColor, 17f))
            }
            addView(iv, LinearLayout.LayoutParams(dp(17f), dp(17f)))

            if (label.isNotEmpty()) {
                val tv = TextView(this@EmulationActivity).apply {
                    text = label
                    setTextColor(fgColor)
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
                    maxLines = 2
                    gravity = Gravity.CENTER
                    StormThemeEngine.applyFont(this, bold = true)
                }
                if (outLabelRef != null && outLabelRef.isNotEmpty()) {
                    outLabelRef[0] = tv
                }
                addView(tv, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                    leftMargin = dp(7f)
                })
            }
        }
    }

    private fun flushPlaytimeDelta() {
        val started = activeSessionStartElapsedMs
        if (started > 0L) {
            activeSessionStartElapsedMs = 0L
            val deltaSec = ((SystemClock.elapsedRealtime() - started) / 1000L).coerceIn(0L, 86400L)
            if (deltaSec > 0L && (gameIdExtra.isNotBlank() || romPath.isNotBlank())) {
                MobileLibraryAndCoverEngine.recordPlaySession(
                    context = this,
                    rom = null,
                    gameIdOrPath = gameIdExtra.ifBlank { romPath },
                    addedSeconds = deltaSec,
                    countLaunch = false
                )
            }
        }
    }

    private fun finishAndSave() {
        flushPlaytimeDelta()
        flushSramSafe()
        emuRunning = false
        finish()
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) {
            applyImmersiveSystemBars()
        }
    }

    override fun onPause() {
        flushPlaytimeDelta()
        touchOverlay.releaseAllInputs()
        flushSramSafe()
        emuPaused = true
        updateGyroRegistration()
        super.onPause()
    }

    override fun onResume() {
        super.onResume()
        applyImmersiveSystemBars()
        if (quickDrawerDialog?.isShowing != true && !touchOverlay.isEditMode) {
            emuPaused = false
            if (activeSessionStartElapsedMs == 0L) {
                activeSessionStartElapsedMs = SystemClock.elapsedRealtime()
            }
        }
        updateGyroRegistration()
        syncViewportBoundsWithHandheldDeck()
    }

    override fun onDestroy() {
        flushPlaytimeDelta()
        mainHandler.removeCallbacks(fpsHudUpdater)
        if (gyroRegistered) {
            sensorManager?.unregisterListener(gyroSensorListener)
            gyroRegistered = false
        }
        quickDrawerDialog?.dismiss()
        flushSramSafe()
        emuRunning = false
        emuPaused = false
        try {
            emuThread?.join(900L)
        } catch (_: Throwable) {
        }
        releaseAudioTrack()
        super.onDestroy()
    }

    private fun dp(v: Float): Int = StormThemeEngine.dpToPx(this, v)
    private fun dpF(v: Float): Float = StormThemeEngine.dpToPxF(this, v)
}
