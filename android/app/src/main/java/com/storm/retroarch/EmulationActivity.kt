package com.storm.retroarch

import android.app.Activity
import android.app.Dialog
import android.app.GameManager
import android.app.GameState
import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Color
import android.graphics.drawable.ColorDrawable
import android.graphics.drawable.GradientDrawable
import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioTrack
import android.os.Build
import android.os.Bundle
import android.os.Environment
import android.os.Handler
import android.os.Looper
import android.os.VibrationEffect
import android.os.Vibrator
import android.text.InputType
import android.util.TypedValue
import android.view.Gravity
import android.view.InputDevice
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.SurfaceHolder
import android.view.SurfaceView
import android.view.View
import android.view.ViewGroup
import android.view.Window
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.view.WindowManager
import android.widget.Button
import android.widget.CheckBox
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.HorizontalScrollView
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import android.widget.Toast
import java.io.File
import java.io.FileOutputStream
import java.nio.ByteBuffer
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import kotlin.math.abs
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

        private const val PREFS_EMU = "storm_retroarch_emu_prefs"
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
    @Volatile private var activeSaveSlot = 0

    private var emuThread: Thread? = null
    private var audioTrack: AudioTrack? = null
    private var currentSampleRate = 0

    // Physical controller live state
    private var selectButtonHeld = false
    private var prevMotionBitmask = 0

    // Cheats & RAM Scanner state
    private var gameCheatFile: File? = null
    private val gameCheatEntries = ArrayList<MobileCheatEntry>()
    private var ramScanWidthBytes = 2
    private val frozenRamAddresses = HashSet<Int>()

    // Active quick drawer
    private var quickDrawerDialog: Dialog? = null

    private val fpsHudUpdater = object : Runnable {
        override fun run() {
            if (isFinishing || isDestroyed) return
            if (fpsHudText.visibility == View.VISIBLE && coreLoadedOk) {
                val fps = if (NativeLibretroBridge.isLibraryLoaded) {
                    NativeLibretroBridge.nativeGetMeasuredFps()
                } else 0.0
                val modeTag = when {
                    rewindHeld -> " • REW"
                    fastForwardMultiplier > 1 -> " • ${fastForwardMultiplier}x"
                    else -> ""
                }
                fpsHudText.text = String.format(Locale.US, "%.1f FPS • %s%s", fps, coreName, modeTag)
            }
            mainHandler.postDelayed(this, 500L)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        requestWindowFeature(Window.FEATURE_NO_TITLE)
        super.onCreate(savedInstanceState)

        StormThemeEngine.loadFromPrefs(this)
        StormLocalization.loadFromPrefs(this)

        intent?.let { inp ->
            romPath = inp.getStringExtra(EXTRA_ROM_PATH) ?: ""
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

        ensureDirectories()
        val rawFile = File(romPath)
        romBaseStem = rawFile.nameWithoutExtension.ifBlank { sanitizeName(romName) }
        effectiveRomPath = if (rawFile.exists()) {
            MobileLibraryAndCoverEngine.applyIpsOrBpsIfPresent(this, rawFile).absolutePath
        } else {
            romPath
        }

        configureImmersiveAndRefreshRate()
        setAndroidGameModeState(isInterruptible = false)
        buildActivityLayout()
        detectInitialControllerPresence()
        startEmulationEngine()
        mainHandler.postDelayed(fpsHudUpdater, 500L)
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

    private fun configureImmersiveAndRefreshRate() {
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            val lp = window.attributes
            lp.layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES
            window.attributes = lp
        }

        // Request 90 / 120 / 144 Hz high refresh rate mode if supported by display
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            try {
                @Suppress("DEPRECATION")
                val disp = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) display else windowManager.defaultDisplay
                val modes = disp?.supportedModes
                if (!modes.isNullOrEmpty()) {
                    val activeMode = disp.mode
                    val best = modes
                        .filter { it.physicalWidth == activeMode.physicalWidth && it.physicalHeight == activeMode.physicalHeight }
                        .maxByOrNull { it.refreshRate }
                        ?: modes.maxByOrNull { it.refreshRate }
                    if (best != null) {
                        val lp = window.attributes
                        lp.preferredDisplayModeId = best.modeId
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

    private fun buildActivityLayout() {
        val prefs = getSharedPreferences(PREFS_EMU, Context.MODE_PRIVATE)
        val showFps = prefs.getBoolean("show_fps_hud", true)

        rootLayout = FrameLayout(this).apply {
            setBackgroundColor(Color.BLACK)
            keepScreenOn = true
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
            FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
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
            FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
        )

        fpsHudText = TextView(this).apply {
            setTextColor(Color.WHITE)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f)
            StormThemeEngine.applyFont(this, bold = true)
            setPadding(dp(10f), dp(4f), dp(10f), dp(4f))
            background = GradientDrawable().apply {
                shape = GradientDrawable.RECTANGLE
                cornerRadius = dpF(10f)
                setColor(Color.argb(155, 14, 10, 28))
                setStroke(dp(1f), StormThemeEngine.withAlpha(StormThemeEngine.activeAccentColor, 165))
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
                    rewindHeld = holding
                }
            }
        }
        rootLayout.addView(
            touchOverlay,
            FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
        )

        setContentView(rootLayout)
    }

    private fun applyOptionalBezelOverlay() {
        val prefs = getSharedPreferences(PREFS_EMU, Context.MODE_PRIVATE)
        if (!prefs.getBoolean("enable_bezel_overlay", false)) {
            bezelImageView.visibility = View.GONE
            return
        }
        val bezelsRoot = MobileCheatAndBiosManager.getBezelsDir()
        if (!bezelsRoot.exists()) return
        val normKey = MobileCoreRegistry.normalizePlatformKey(platformName, "")
        val candidate = bezelsRoot.walkTopDown()
            .maxDepth(4)
            .firstOrNull { f ->
                f.isFile && f.extension.equals("png", ignoreCase = true) &&
                    f.nameWithoutExtension.lowercase(Locale.ROOT).contains(normKey.ifBlank { "bezel" })
            }
        if (candidate != null) {
            val bmp = BitmapFactory.decodeFile(candidate.absolutePath)
            if (bmp != null) {
                bezelImageView.setImageBitmap(bmp)
                bezelImageView.visibility = View.VISIBLE
            }
        }
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

        val prefs = getSharedPreferences(PREFS_EMU, Context.MODE_PRIVATE)
        val initialShader = prefs.getInt("shader_mode", NativeLibretroBridge.SHADER_PIXEL_PERFECT)
        val initialAspect = prefs.getInt(
            "aspect_mode",
            if (prefs.getBoolean("integer_scale", false)) NativeLibretroBridge.ASPECT_INTEGER
            else NativeLibretroBridge.ASPECT_CORE_FIT
        )

        emuRunning = true
        emuPaused = false

        emuThread = Thread({
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

            // Auto-load & apply saved .cht cheats for this game
            loadAndApplyGameCheats()

            val targetFps = NativeLibretroBridge.nativeGetFps().let { if (it in 20.0..120.0) it else 60.0 }
            val frameDurationNs = (1_000_000_000.0 / targetFps).toLong()
            val audioBuf = ShortArray(8192)
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

                if (rewindHeld) {
                    val popped = NativeLibretroBridge.nativePopRewindSnapshot()
                    if (popped) {
                        // Render the restored snapshot cleanly
                        NativeLibretroBridge.nativeRunFrame()
                    }
                    NativeLibretroBridge.nativePullAudioSamples(audioBuf, audioBuf.size)
                } else {
                    val steps = fastForwardMultiplier.coerceIn(1, 4)
                    for (s in 0 until steps) {
                        NativeLibretroBridge.nativeRunFrame()
                        val pulled = NativeLibretroBridge.nativePullAudioSamples(audioBuf, audioBuf.size)
                        if (pulled > 0 && s == 0) {
                            val sr = NativeLibretroBridge.nativeGetSampleRate()
                            if (sr > 8000 && sr != currentSampleRate) {
                                initOrUpdateAudioTrack(sr)
                            }
                            try {
                                audioTrack?.write(audioBuf, 0, pulled, AudioTrack.WRITE_NON_BLOCKING)
                            } catch (_: Throwable) {
                            }
                        }
                    }
                    frameCounter++
                    if (frameCounter % 3L == 0L) {
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

                nextFrameDeadline += frameDurationNs
                val sleepNs = nextFrameDeadline - System.nanoTime()
                if (sleepNs > 1_500_000L) {
                    try {
                        Thread.sleep(sleepNs / 1_000_000L, (sleepNs % 1_000_000L).toInt())
                    } catch (_: InterruptedException) {
                        break
                    }
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
            priority = Thread.MAX_PRIORITY
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

            audioTrack = builder.build().also { it.play() }
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

    private fun cycleFastForwardSpeed() {
        fastForwardMultiplier = when (fastForwardMultiplier) {
            1 -> 2
            2 -> 4
            else -> 1
        }
        touchOverlay.isFastForwardActive = fastForwardMultiplier > 1
        Toast.makeText(this, "${StormLocalization.tr("quick_fast_forward")}: ${fastForwardMultiplier}x", Toast.LENGTH_SHORT).show()
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

        // Route digital D-Pad HAT + L2/R2 trigger thresholds via profile manager
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

        // Apply digital transitions for HAT & triggers (bits UP, DOWN, LEFT, RIGHT, L2, R2)
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

        // Route true 16-bit analog stick axes (-32767..32767)
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
            MobileControllerProfileManager.HOTKEY_REWIND -> rewindHeld = pressed
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
        val msg = if (ok) "${StormLocalization.tr("saves_quick_save")}: #${slot}" else "Save State Failed"
        Toast.makeText(this, msg, Toast.LENGTH_SHORT).show()
        return ok
    }

    private fun performLoadState(slot: Int): Boolean {
        if (!coreLoadedOk || !NativeLibretroBridge.isLibraryLoaded) return false
        val sf = stateFile(slot)
        if (!sf.exists()) {
            Toast.makeText(this, "${StormLocalization.tr("saves_slot")} #$slot: ${StormLocalization.tr("saves_empty_slot")}", Toast.LENGTH_SHORT).show()
            return false
        }
        val ok = NativeLibretroBridge.nativeLoadState(sf.absolutePath)
        val msg = if (ok) "${StormLocalization.tr("saves_quick_load")}: #${slot}" else "Load State Failed"
        Toast.makeText(this, msg, Toast.LENGTH_SHORT).show()
        return ok
    }

    private fun captureScreenshotToGallery(preCaptured: Bitmap? = null): File? {
        val bmp = preCaptured ?: captureCurrentFrameBitmap()
        if (bmp == null) {
            Toast.makeText(this, "Frame buffer unavailable", Toast.LENGTH_SHORT).show()
            return null
        }
        return try {
            screenshotsDir.mkdirs()
            val stamp = SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US).format(Date())
            val out = File(screenshotsDir, "${romBaseStem}_$stamp.png")
            FileOutputStream(out).use { fos ->
                bmp.compress(Bitmap.CompressFormat.PNG, 100, fos)
            }
            Toast.makeText(this, "${StormLocalization.tr("quick_screenshot")}: ${out.name}", Toast.LENGTH_SHORT).show()
            out
        } catch (_: Throwable) {
            null
        }
    }

    // === In-Game Slide-Over Quick Drawer ===

    fun showInGameQuickDrawer() {
        if (isFinishing || isDestroyed) return
        if (quickDrawerDialog?.isShowing == true) return

        // Capture live frame BEFORE pausing so Save State & Screenshot inside drawer have exact frame!
        val snapshotBeforePause = captureCurrentFrameBitmap()
        touchOverlay.releaseAllInputs()
        emuPaused = true
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
                setAndroidGameModeState(isInterruptible = false)
            }
            applyImmersiveSystemBars()
        }

        // Outer scrim (tap outside drawer dismisses!)
        val scrim = FrameLayout(this).apply {
            setBackgroundColor(Color.argb(150, 4, 3, 12))
            setOnClickListener { dialog.dismiss() }
        }

        val drawerWidth = minOf((resources.displayMetrics.widthPixels * 0.86f).roundToInt(), dp(420f))
        val drawerCard = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            isClickable = true
            background = GradientDrawable(
                GradientDrawable.Orientation.TL_BR,
                intArrayOf(StormThemeEngine.lighten(pal.bgSurface, 0.04f), pal.bgMain)
            ).apply {
                setStroke(dp(1.2f), accent)
            }
            setPadding(dp(16f), dp(18f), dp(16f), dp(18f))
        }

        // Header row: Title + FPS/Core badge + Close button
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
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 16f)
            maxLines = 1
            StormThemeEngine.applyFont(this, bold = true)
        }
        val measuredFps = if (NativeLibretroBridge.isLibraryLoaded) NativeLibretroBridge.nativeGetMeasuredFps() else 60.0
        val coreBadge = TextView(this).apply {
            text = String.format(Locale.US, "STORM RETROARCH 2.4.0 • %s • %.1f FPS", coreName, measuredFps)
            setTextColor(accent)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f)
            StormThemeEngine.applyFont(this, bold = true)
        }
        titleCol.addView(headerTitle)
        titleCol.addView(coreBadge)
        headerRow.addView(titleCol)

        val closeBtn = createIconActionButton(StormVectorIcons.CLOSE, "", isAccent = false) {
            dialog.dismiss()
        }
        headerRow.addView(closeBtn, LinearLayout.LayoutParams(dp(38f), dp(38f)))
        drawerCard.addView(headerRow)

        val scroll = ScrollView(this).apply {
            isVerticalScrollBarEnabled = false
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f).apply {
                topMargin = dp(12f)
            }
        }
        val body = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, 0, 0, dp(12f))
        }

        // 1. Resume Primary Button
        body.addView(
            createIconActionButton(StormVectorIcons.PLAY, StormLocalization.tr("quick_resume"), isAccent = true) {
                dialog.dismiss()
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(44f)).apply { bottomMargin = dp(12f) }
        )

        // 2. Save / Load State Slots 0-9 with live PNG thumbnail preview
        body.addView(createSectionLabel("${StormLocalization.tr("saves_title")} (0 - 9)"))
        val slotPreviewCard = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            background = StormThemeEngine.createVolumetricCardDrawable(this@EmulationActivity, cornerDp = 12f)
            setPadding(dp(10f), dp(8f), dp(10f), dp(8f))
        }
        val slotThumbView = ImageView(this).apply {
            scaleType = ImageView.ScaleType.CENTER_CROP
            setBackgroundColor(Color.argb(160, 10, 8, 20))
        }
        slotPreviewCard.addView(slotThumbView, LinearLayout.LayoutParams(dp(92f), dp(64f)))

        val slotMetaCol = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                leftMargin = dp(10f)
            }
        }
        val slotStatusLabel = TextView(this).apply {
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f)
            StormThemeEngine.applyFont(this, bold = true)
        }
        val slotTimeLabel = TextView(this).apply {
            setTextColor(pal.textSecondary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f)
            StormThemeEngine.applyFont(this, bold = false)
        }
        slotMetaCol.addView(slotStatusLabel)
        slotMetaCol.addView(slotTimeLabel)
        slotPreviewCard.addView(slotMetaCol)
        body.addView(slotPreviewCard, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(8f) })

        val slotChipsRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        val slotChipViews = ArrayList<TextView>(10)

        fun refreshSlotPreview() {
            val sf = stateFile(activeSaveSlot)
            val pf = statePreviewFile(activeSaveSlot)
            slotStatusLabel.text = "${StormLocalization.tr("saves_slot")} #$activeSaveSlot"
            if (sf.exists()) {
                val dt = SimpleDateFormat("yyyy-MM-dd HH:mm", Locale.US).format(Date(sf.lastModified()))
                slotTimeLabel.text = "$dt (${sf.length() / 1024} KB)"
                val bmp = if (pf.exists()) BitmapFactory.decodeFile(pf.absolutePath) else snapshotBeforePause
                slotThumbView.setImageBitmap(bmp)
            } else {
                slotTimeLabel.text = StormLocalization.tr("saves_empty_slot")
                slotThumbView.setImageBitmap(snapshotBeforePause)
            }
            for ((idx, tv) in slotChipViews.withIndex()) {
                val sel = idx == activeSaveSlot
                tv.background = StormThemeEngine.createFilterChipDrawable(this, sel, accent, 10f)
                tv.setTextColor(if (sel) StormThemeEngine.contrastTextForFill(accent) else pal.textPrimary)
            }
        }

        for (slot in 0..9) {
            val chip = TextView(this).apply {
                text = "#$slot"
                gravity = Gravity.CENTER
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
                setPadding(dp(12f), dp(6f), dp(12f), dp(6f))
                StormThemeEngine.applyFont(this, bold = true)
                setOnClickListener {
                    activeSaveSlot = slot
                    refreshSlotPreview()
                }
            }
            slotChipViews.add(chip)
            slotChipsRow.addView(chip, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { rightMargin = dp(6f) })
        }
        val slotScroll = HorizontalScrollView(this).apply {
            isHorizontalScrollBarEnabled = false
            addView(slotChipsRow)
        }
        body.addView(slotScroll, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(8f) })
        refreshSlotPreview()

        val saveLoadRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        saveLoadRow.addView(
            createIconActionButton(StormVectorIcons.SAVE, StormLocalization.tr("saves_quick_save"), isAccent = true) {
                performSaveState(activeSaveSlot, snapshotBeforePause)
                refreshSlotPreview()
            },
            LinearLayout.LayoutParams(0, dp(42f), 1f).apply { rightMargin = dp(6f) }
        )
        saveLoadRow.addView(
            createIconActionButton(StormVectorIcons.LOAD, StormLocalization.tr("saves_quick_load"), isAccent = false) {
                if (performLoadState(activeSaveSlot)) {
                    dialog.dismiss()
                }
            },
            LinearLayout.LayoutParams(0, dp(42f), 1f)
        )
        body.addView(saveLoadRow, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(12f) })

        // 3. Rewind & Fast-Forward (1x / 2x / 4x)
        val rewCount = if (NativeLibretroBridge.isLibraryLoaded) NativeLibretroBridge.nativeGetRewindCount() else 0
        body.addView(createSectionLabel("${StormLocalization.tr("quick_fast_forward")} / ${StormLocalization.tr("quick_rewind")} ($rewCount)"))
        val speedRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        for (spd in listOf(1, 2, 4)) {
            val isSel = fastForwardMultiplier == spd
            val btn = TextView(this).apply {
                text = "${spd}x"
                gravity = Gravity.CENTER
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f)
                StormThemeEngine.applyFont(this, bold = true)
                background = StormThemeEngine.createFilterChipDrawable(this@EmulationActivity, isSel, accent, 10f)
                setTextColor(if (isSel) StormThemeEngine.contrastTextForFill(accent) else pal.textPrimary)
                setOnClickListener {
                    fastForwardMultiplier = spd
                    touchOverlay.isFastForwardActive = spd > 1
                    dialog.dismiss()
                }
            }
            speedRow.addView(btn, LinearLayout.LayoutParams(0, dp(38f), 1f).apply { rightMargin = dp(6f) })
        }
        val rewindStepBtn = createIconActionButton(StormVectorIcons.REWIND, "Rewind", isAccent = false) {
            if (NativeLibretroBridge.isLibraryLoaded) {
                var poppedAny = false
                repeat(15) {
                    if (NativeLibretroBridge.nativePopRewindSnapshot()) poppedAny = true
                }
                if (poppedAny) {
                    Toast.makeText(this, StormLocalization.tr("quick_rewind"), Toast.LENGTH_SHORT).show()
                    dialog.dismiss()
                } else {
                    Toast.makeText(this, "Rewind buffer empty", Toast.LENGTH_SHORT).show()
                }
            }
        }
        speedRow.addView(rewindStepBtn, LinearLayout.LayoutParams(0, dp(38f), 1.3f))
        body.addView(speedRow, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(12f) })

        // 4. Video Filter (Pixel-Perfect, Smooth Bilinear, CRT Scanlines, LCD Grid)
        body.addView(createSectionLabel("Video Filter"))
        val curShader = if (NativeLibretroBridge.isLibraryLoaded) NativeLibretroBridge.nativeGetShaderMode() else 0
        val shaderOptions = listOf(
            NativeLibretroBridge.SHADER_PIXEL_PERFECT to "Pixel-Perfect",
            NativeLibretroBridge.SHADER_BILINEAR_SMOOTH to "Smooth Bilinear",
            NativeLibretroBridge.SHADER_CRT_SCANLINES to "CRT Scanlines",
            NativeLibretroBridge.SHADER_LCD_GRID to "LCD Grid"
        )
        body.addView(buildHorizontalModeChips(shaderOptions, curShader) { mode ->
            NativeLibretroBridge.nativeSetShaderMode(mode)
            getSharedPreferences(PREFS_EMU, Context.MODE_PRIVATE).edit().putInt("shader_mode", mode).apply()
        })

        // 5. Aspect Ratio (Core Provided, 4:3, 16:9, Integer)
        body.addView(createSectionLabel("Aspect Ratio"))
        val curAspect = if (NativeLibretroBridge.isLibraryLoaded) NativeLibretroBridge.nativeGetAspectMode() else 0
        val aspectOptions = listOf(
            NativeLibretroBridge.ASPECT_CORE_FIT to "Core Provided",
            NativeLibretroBridge.ASPECT_4_3 to "4:3",
            NativeLibretroBridge.ASPECT_16_9 to "16:9",
            NativeLibretroBridge.ASPECT_INTEGER to "Integer"
        )
        body.addView(buildHorizontalModeChips(aspectOptions, curAspect) { mode ->
            NativeLibretroBridge.nativeSetAspectMode(mode)
            getSharedPreferences(PREFS_EMU, Context.MODE_PRIVATE).edit().putInt("aspect_mode", mode).apply()
        })

        // 6. Cheats (.cht) & Live Native RAM Scanner
        body.addView(createSectionLabel(StormLocalization.tr("cheats_title")))
        val cheatRamRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        cheatRamRow.addView(
            createIconActionButton(StormVectorIcons.CHEATS, "Читы (.cht) (${gameCheatEntries.count { it.isEnabled }}/${gameCheatEntries.size})", isAccent = false) {
                showInGameCheatsDialog()
            },
            LinearLayout.LayoutParams(0, dp(42f), 1f).apply { rightMargin = dp(6f) }
        )
        cheatRamRow.addView(
            createIconActionButton(StormVectorIcons.RAM, StormLocalization.tr("ram_scanner_title"), isAccent = false) {
                showLiveRamScannerDialog()
            },
            LinearLayout.LayoutParams(0, dp(42f), 1f)
        )
        body.addView(cheatRamRow, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(12f) })

        // 7. Touch Controls Layout & Live Editor
        body.addView(createSectionLabel(StormLocalization.tr("touch_overlay_title")))
        val layoutOptions = MobileTouchGamepadOverlay.LayoutProfile.entries.mapIndexed { i, lp -> i to lp.id }
        val currentLayoutIdx = MobileTouchGamepadOverlay.LayoutProfile.entries.indexOf(touchOverlay.layoutProfile).coerceAtLeast(0)
        body.addView(buildHorizontalModeChips(layoutOptions, currentLayoutIdx) { idx ->
            val prof = MobileTouchGamepadOverlay.LayoutProfile.entries[idx]
            touchOverlay.setLayoutProfile(prof)
            touchOverlay.setPhysicalControllerActive(false)
        })

        body.addView(
            createIconActionButton(StormVectorIcons.SLIDERS, StormLocalization.tr("touch_edit_layout"), isAccent = false) {
                touchOverlay.setPhysicalControllerActive(false)
                touchOverlay.isEditMode = true
                dialog.dismiss()
                emuPaused = false
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(42f)).apply { bottomMargin = dp(10f) }
        )

        // 8. Screenshot, Reset Game & Exit to Library
        val utilRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        utilRow.addView(
            createIconActionButton(StormVectorIcons.CAMERA, StormLocalization.tr("quick_screenshot"), isAccent = false) {
                captureScreenshotToGallery(snapshotBeforePause)
            },
            LinearLayout.LayoutParams(0, dp(42f), 1f).apply { rightMargin = dp(6f) }
        )
        utilRow.addView(
            createIconActionButton(StormVectorIcons.REFRESH, StormLocalization.tr("quick_restart"), isAccent = false) {
                if (NativeLibretroBridge.isLibraryLoaded && coreLoadedOk) {
                    NativeLibretroBridge.nativeReset()
                }
                dialog.dismiss()
            },
            LinearLayout.LayoutParams(0, dp(42f), 1f)
        )
        body.addView(utilRow, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(10f) })

        body.addView(
            createIconActionButton(StormVectorIcons.CLOSE, StormLocalization.tr("quick_exit"), isAccent = false) {
                dialog.dismiss()
                finishAndSave()
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(44f))
        )

        scroll.addView(body)
        drawerCard.addView(scroll)

        scrim.addView(
            drawerCard,
            FrameLayout.LayoutParams(drawerWidth, ViewGroup.LayoutParams.MATCH_PARENT, Gravity.END)
        )

        dialog.setContentView(scrim)
        quickDrawerDialog = dialog
        dialog.show()
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
                bottomMargin = dp(10f)
            }
            addView(row)
        }
    }

    // === In-Game Cheats Quick Toggle Dialog ===

    private fun showInGameCheatsDialog() {
        val pal = StormThemeEngine.currentTheme
        val accent = StormThemeEngine.activeAccentColor

        val dlg = Dialog(this)
        dlg.requestWindowFeature(Window.FEATURE_NO_TITLE)
        dlg.window?.setBackgroundDrawable(ColorDrawable(Color.TRANSPARENT))

        val card = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = StormThemeEngine.createVolumetricCardDrawable(this@EmulationActivity, cornerDp = 16f)
            setPadding(dp(16f), dp(16f), dp(16f), dp(16f))
        }

        val title = TextView(this).apply {
            text = "${StormLocalization.tr("cheats_title")} - $romName"
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 15f)
            StormThemeEngine.applyFont(this, bold = true)
        }
        card.addView(title)

        val listContainer = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, dp(8f), 0, dp(8f))
        }

        fun rebuildRows() {
            listContainer.removeAllViews()
            if (gameCheatEntries.isEmpty()) {
                val empty = TextView(this).apply {
                    text = "Для этой игры пока нет кодов .cht. Добавьте свой код ниже или скачайте базу читов во вкладке Читы."
                    setTextColor(pal.textSecondary)
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
                    setPadding(0, dp(10f), 0, dp(10f))
                }
                listContainer.addView(empty)
            } else {
                for (entry in gameCheatEntries) {
                    val row = LinearLayout(this).apply {
                        orientation = LinearLayout.HORIZONTAL
                        gravity = Gravity.CENTER_VERTICAL
                        setPadding(dp(8f), dp(6f), dp(8f), dp(6f))
                    }
                    val cb = CheckBox(this).apply {
                        isChecked = entry.isEnabled
                        setOnCheckedChangeListener { _, checked ->
                            entry.isEnabled = checked
                        }
                    }
                    val info = LinearLayout(this).apply {
                        orientation = LinearLayout.VERTICAL
                        layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
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
                    }
                    info.addView(descTv)
                    info.addView(codeTv)
                    row.addView(cb)
                    row.addView(info)
                    listContainer.addView(row)
                }
            }
        }
        rebuildRows()

        val scroll = ScrollView(this).apply {
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(240f)).apply {
                topMargin = dp(8f)
                bottomMargin = dp(8f)
            }
            addView(listContainer)
        }
        card.addView(scroll)

        // Add custom cheat row
        val descInput = EditText(this).apply {
            hint = "Описание чита"
            setHintTextColor(pal.textSecondary)
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            background = StormThemeEngine.createRecessedInputDrawable(this@EmulationActivity)
            setPadding(dp(10f), dp(6f), dp(10f), dp(6f))
        }
        val codeInput = EditText(this).apply {
            hint = "Код (напр. 0099-01-63)"
            setHintTextColor(pal.textSecondary)
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            background = StormThemeEngine.createRecessedInputDrawable(this@EmulationActivity)
            setPadding(dp(10f), dp(6f), dp(10f), dp(6f))
        }
        card.addView(descInput, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(6f) })
        card.addView(codeInput, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(8f) })

        val btnBar = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        btnBar.addView(
            createIconActionButton(StormVectorIcons.CHEATS, StormLocalization.tr("cheats_add_code"), isAccent = false) {
                val c = codeInput.text.toString().trim()
                if (c.isNotEmpty()) {
                    val d = descInput.text.toString().trim().ifBlank { "Custom Cheat #${gameCheatEntries.size + 1}" }
                    gameCheatEntries.add(MobileCheatEntry(gameCheatEntries.size, d, d, c, true))
                    descInput.setText("")
                    codeInput.setText("")
                    rebuildRows()
                }
            },
            LinearLayout.LayoutParams(0, dp(40f), 1f).apply { rightMargin = dp(6f) }
        )
        btnBar.addView(
            createIconActionButton(StormVectorIcons.SAVE, StormLocalization.tr("cheats_save"), isAccent = true) {
                gameCheatFile?.let { MobileCheatAndBiosManager.saveChtFile(it, gameCheatEntries) }
                pushActiveCheatsToCore()
                Toast.makeText(this, StormLocalization.tr("cheats_save"), Toast.LENGTH_SHORT).show()
                dlg.dismiss()
            },
            LinearLayout.LayoutParams(0, dp(40f), 1f)
        )
        card.addView(btnBar)

        dlg.setContentView(
            card,
            ViewGroup.LayoutParams((resources.displayMetrics.widthPixels * 0.90f).roundToInt(), ViewGroup.LayoutParams.WRAP_CONTENT)
        )
        dlg.show()
    }

    // === Live Native RAM Scanner Dialog ===

    private fun showLiveRamScannerDialog() {
        val pal = StormThemeEngine.currentTheme
        val accent = StormThemeEngine.activeAccentColor

        val dlg = Dialog(this)
        dlg.requestWindowFeature(Window.FEATURE_NO_TITLE)
        dlg.window?.setBackgroundDrawable(ColorDrawable(Color.TRANSPARENT))

        val card = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = StormThemeEngine.createVolumetricCardDrawable(this@EmulationActivity, cornerDp = 16f)
            setPadding(dp(16f), dp(16f), dp(16f), dp(16f))
        }

        val ramSize = if (NativeLibretroBridge.isLibraryLoaded) NativeLibretroBridge.nativeGetSystemRamSize() else 0
        val title = TextView(this).apply {
            text = "${StormLocalization.tr("ram_scanner_title")} (${ramSize / 1024} KB RAM)"
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 15f)
            StormThemeEngine.applyFont(this, bold = true)
        }
        card.addView(title)

        // Width selector: 8 / 16 / 32-bit
        val widthOptions = listOf(1 to "8-bit (1B)", 2 to "16-bit (2B)", 4 to "32-bit (4B)")
        card.addView(buildHorizontalModeChips(widthOptions, ramScanWidthBytes) { w -> ramScanWidthBytes = w })

        val valueInput = EditText(this).apply {
            hint = "Значение (напр. 99 или 1000)"
            inputType = InputType.TYPE_CLASS_NUMBER or InputType.TYPE_NUMBER_FLAG_SIGNED
            setHintTextColor(pal.textSecondary)
            setTextColor(pal.textPrimary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f)
            background = StormThemeEngine.createRecessedInputDrawable(this@EmulationActivity)
            setPadding(dp(12f), dp(8f), dp(12f), dp(8f))
        }
        card.addView(valueInput, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(8f) })

        val matchesLabel = TextView(this).apply {
            text = "${StormLocalization.tr("ram_matches_found")}: 0"
            setTextColor(accent)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
            StormThemeEngine.applyFont(this, bold = true)
        }
        card.addView(matchesLabel)

        val candidatesList = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
        }

        fun renderCandidates(totalMatches: Int) {
            matchesLabel.text = "${StormLocalization.tr("ram_matches_found")}: $totalMatches"
            candidatesList.removeAllViews()
            if (!NativeLibretroBridge.isLibraryLoaded) return
            val packedArr = NativeLibretroBridge.nativeRamGetCandidates(64)
            for (packed in packedArr) {
                val addr = NativeLibretroBridge.unpackCandidateAddress(packed)
                val curVal = NativeLibretroBridge.unpackCandidateValue(packed)
                val row = LinearLayout(this).apply {
                    orientation = LinearLayout.HORIZONTAL
                    gravity = Gravity.CENTER_VERTICAL
                    setPadding(0, dp(4f), 0, dp(4f))
                }
                val addrTv = TextView(this).apply {
                    text = String.format(Locale.US, "0x%06X = %d", addr, curVal)
                    setTextColor(pal.textPrimary)
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
                    layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                }
                val writeBtn = TextView(this).apply {
                    text = "Write"
                    setTextColor(StormThemeEngine.contrastTextForFill(accent))
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f)
                    setPadding(dp(10f), dp(5f), dp(10f), dp(5f))
                    background = StormThemeEngine.createAccentButtonDrawable(this@EmulationActivity, accent, 8f)
                    setOnClickListener {
                        val targetV = valueInput.text.toString().trim().toLongOrNull() ?: curVal
                        NativeLibretroBridge.nativeRamWriteValue(addr, ramScanWidthBytes, targetV)
                        Toast.makeText(this@EmulationActivity, String.format(Locale.US, "0x%06X <- %d", addr, targetV), Toast.LENGTH_SHORT).show()
                    }
                }
                val isFrozen = frozenRamAddresses.contains(addr)
                val freezeBtn = TextView(this).apply {
                    text = if (isFrozen) "Unfreeze" else "Freeze"
                    setTextColor(pal.textPrimary)
                    setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f)
                    setPadding(dp(10f), dp(5f), dp(10f), dp(5f))
                    background = StormThemeEngine.createFilterChipDrawable(this@EmulationActivity, isFrozen, accent, 8f)
                    setOnClickListener {
                        val nextFreeze = !frozenRamAddresses.contains(addr)
                        val targetV = valueInput.text.toString().trim().toLongOrNull() ?: curVal
                        NativeLibretroBridge.nativeRamSetFrozen(addr, ramScanWidthBytes, targetV, nextFreeze)
                        if (nextFreeze) frozenRamAddresses.add(addr) else frozenRamAddresses.remove(addr)
                        text = if (nextFreeze) "Unfreeze" else "Freeze"
                        background = StormThemeEngine.createFilterChipDrawable(this@EmulationActivity, nextFreeze, accent, 8f)
                    }
                }
                row.addView(addrTv)
                row.addView(writeBtn, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { rightMargin = dp(6f) })
                row.addView(freezeBtn)
                candidatesList.addView(row)
            }
        }

        // Scan action buttons: First Scan, Exact, Increased, Decreased
        val scanRow1 = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        scanRow1.addView(
            createIconActionButton(StormVectorIcons.SEARCH, StormLocalization.tr("ram_scan_new"), isAccent = true) {
                val v = valueInput.text.toString().trim().toLongOrNull() ?: 0L
                val count = NativeLibretroBridge.nativeRamScanFirst(ramScanWidthBytes, v)
                renderCandidates(count)
            },
            LinearLayout.LayoutParams(0, dp(38f), 1f).apply { rightMargin = dp(6f) }
        )
        scanRow1.addView(
            createIconActionButton(StormVectorIcons.CHECK, StormLocalization.tr("ram_scan_exact"), isAccent = false) {
                val v = valueInput.text.toString().trim().toLongOrNull() ?: 0L
                val count = NativeLibretroBridge.nativeRamScanNext(NativeLibretroBridge.RAM_SCAN_EXACT, v)
                renderCandidates(count)
            },
            LinearLayout.LayoutParams(0, dp(38f), 1f)
        )
        card.addView(scanRow1, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(6f); bottomMargin = dp(6f) })

        val scanRow2 = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        val incBtn = createSimpleChipButton(StormLocalization.tr("ram_scan_increased")) {
            val count = NativeLibretroBridge.nativeRamScanNext(NativeLibretroBridge.RAM_SCAN_INCREASED, 0L)
            renderCandidates(count)
        }
        val decBtn = createSimpleChipButton(StormLocalization.tr("ram_scan_decreased")) {
            val count = NativeLibretroBridge.nativeRamScanNext(NativeLibretroBridge.RAM_SCAN_DECREASED, 0L)
            renderCandidates(count)
        }
        scanRow2.addView(incBtn, LinearLayout.LayoutParams(0, dp(36f), 1f).apply { rightMargin = dp(6f) })
        scanRow2.addView(decBtn, LinearLayout.LayoutParams(0, dp(36f), 1f))
        card.addView(scanRow2)

        val candScroll = ScrollView(this).apply {
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(190f)).apply {
                topMargin = dp(8f)
                bottomMargin = dp(8f)
            }
            addView(candidatesList)
        }
        card.addView(candScroll)
        renderCandidates(NativeLibretroBridge.nativeRamGetCandidates(64).size)

        card.addView(
            createIconActionButton(StormVectorIcons.CLOSE, StormLocalization.tr("btn_close"), isAccent = false) {
                dlg.dismiss()
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(40f))
        )

        dlg.setContentView(
            card,
            ViewGroup.LayoutParams((resources.displayMetrics.widthPixels * 0.92f).roundToInt(), ViewGroup.LayoutParams.WRAP_CONTENT)
        )
        dlg.show()
    }

    // === UI Builders (Single Icon Mandate Compliant) ===

    private fun createSectionLabel(textStr: String): TextView {
        return TextView(this).apply {
            text = textStr
            setTextColor(StormThemeEngine.currentTheme.textSecondary)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f)
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
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f)
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
        onClick: () -> Unit
    ): LinearLayout {
        val pal = StormThemeEngine.currentTheme
        val accent = StormThemeEngine.activeAccentColor
        val fgColor = if (isAccent) StormThemeEngine.contrastTextForFill(accent) else pal.textPrimary

        return LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER
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
                    maxLines = 1
                    StormThemeEngine.applyFont(this, bold = true)
                }
                addView(tv, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                    leftMargin = dp(7f)
                })
            }
        }
    }

    private fun finishAndSave() {
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
        touchOverlay.releaseAllInputs()
        flushSramSafe()
        emuPaused = true
        super.onPause()
    }

    override fun onResume() {
        super.onResume()
        applyImmersiveSystemBars()
        if (quickDrawerDialog?.isShowing != true && !touchOverlay.isEditMode) {
            emuPaused = false
        }
    }

    override fun onDestroy() {
        mainHandler.removeCallbacks(fpsHudUpdater)
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
