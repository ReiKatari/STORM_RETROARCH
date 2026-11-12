package com.storm.retroarch

import android.animation.ValueAnimator
import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.DashPathEffect
import android.graphics.LinearGradient
import android.graphics.Matrix
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RadialGradient
import android.graphics.RectF
import android.graphics.Shader
import android.graphics.Typeface
import android.os.Environment
import android.util.AttributeSet
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.ScaleGestureDetector
import android.view.View
import android.view.animation.DecelerateInterpolator
import org.json.JSONObject
import java.io.File
import kotlin.math.PI
import kotlin.math.abs
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.max
import kotlin.math.min
import kotlin.math.roundToInt
import kotlin.math.sin

class MobileTouchGamepadOverlay @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyleAttr: Int = 0
) : View(context, attrs, defStyleAttr) {

    interface Callback {
        fun onDigitalButton(libretroId: Int, pressed: Boolean)
        fun onAnalogStick(stickIndex: Int, axisX: Int, axisY: Int)
        fun onQuickMenuRequested()
        fun onFastForwardToggle()
        fun onRewindHold(holding: Boolean)
        fun onTurboLatchChanged(enabled: Boolean) {}
        fun onDeckVisibilityChanged() {}
    }

    enum class ButtonColorTheme(val id: String, val displayName: String) {
        STORM_VIOLET("STORM Violet", "STORM Violet"),
        PLAYSTATION("PlayStation", "PlayStation"),
        SNES("SNES", "SNES"),
        XBOX("Xbox", "Xbox"),
        MINIMAL_GLASS("Minimal Glass", "Minimal Glass");

        companion object {
            fun fromId(raw: String?): ButtonColorTheme {
                val norm = (raw ?: "").trim().lowercase()
                return when {
                    norm.contains("playstation") || norm == "psx" || norm == "ps" || norm == "dualshock" -> PLAYSTATION
                    norm == "snes" || norm.contains("super nintendo") -> SNES
                    norm.contains("xbox") -> XBOX
                    norm.contains("minimal") || norm.contains("glass") -> MINIMAL_GLASS
                    else -> STORM_VIOLET
                }
            }
        }
    }

    enum class LayoutProfile(val id: String, val displayName: String) {
        CONSOLE_PRO("CONSOLE_PRO", "Console Pro"),
        ARCADE_6BTN("ARCADE_6BTN", "Arcade 6-Button"),
        SNES("SNES", "SNES"),
        GENESIS_6BTN("GENESIS_6BTN", "Sega 6-Button"),
        NES("NES", "NES"),
        RPG_MINIMAL("RPG_MINIMAL", "RPG Minimal"),
        HIDDEN("HIDDEN", "Скрыть");

        companion object {
            val PSX_DUALSHOCK: LayoutProfile get() = CONSOLE_PRO
            val ARCADE: LayoutProfile get() = ARCADE_6BTN

            fun fromId(id: String?): LayoutProfile {
                if (id.isNullOrBlank()) return SNES
                val norm = id.trim().uppercase()
                return when {
                    norm == "CONSOLE_PRO" || norm == "CONSOLE PRO" || norm == "PSX_DUALSHOCK" || norm.contains("DUALSHOCK") || norm == "PRO" -> CONSOLE_PRO
                    norm == "ARCADE_6BTN" || norm == "ARCADE 6-BUTTON" || norm == "ARCADE" || norm.contains("6-BUTTON") && norm.contains("ARCADE") -> ARCADE_6BTN
                    norm == "GENESIS_6BTN" || norm == "SEGA 6-BUTTON" || norm.contains("GENESIS") || norm.contains("SEGA") -> GENESIS_6BTN
                    norm == "NES" || norm.contains("2 КНОПКИ") -> NES
                    norm == "RPG_MINIMAL" || norm == "RPG MINIMAL" || norm == "RPG" || norm == "MINIMAL" -> RPG_MINIMAL
                    norm == "HIDDEN" || norm == "HIDE" || norm == "OFF" || norm == "NONE" || norm == "СКРЫТЬ" -> HIDDEN
                    norm == "SNES" -> SNES
                    else -> entries.firstOrNull {
                        it.id.equals(id.trim(), ignoreCase = true) ||
                            it.displayName.equals(id.trim(), ignoreCase = true)
                    } ?: SNES
                }
            }

            fun resolveForPlatform(platformRaw: String?): LayoutProfile {
                val p = (platformRaw ?: "").trim().uppercase()
                return when {
                    p.contains("NES") && !p.contains("SNES") && !p.contains("GENESIS") -> NES
                    p.contains("FAMICOM") && !p.contains("SUPER") -> NES
                    p.contains("GAME BOY") && !p.contains("ADVANCE") -> NES
                    p == "GB" || p == "GBC" || p.contains("ATARI 2600") || p.contains("ATARI 7800") ||
                        p.contains("MASTER SYSTEM") || p == "SMS" || p == "GG" || p.contains("GAME GEAR") -> NES
                    p.contains("GENESIS") || p.contains("MEGA DRIVE") || p == "MD" ||
                        p.contains("32X") || p.contains("SEGA CD") || p.contains("SATURN") -> GENESIS_6BTN
                    p.contains("ARCADE") || p.contains("FBNEO") || p.contains("MAME") ||
                        p.contains("CPS") || p.contains("NEO GEO") || p == "NEOGEO" -> ARCADE_6BTN
                    p.contains("PSX") || p.contains("PS1") || p.contains("PLAYSTATION") || p.contains("PS2") || p.contains("PSP") ||
                        p.contains("N64") || p.contains("NINTENDO 64") || p.contains("DREAMCAST") || p == "DC" ||
                        p.contains("3DS") || p.contains("GAMECUBE") || p.contains("WII") || p.contains("SWITCH") -> CONSOLE_PRO
                    else -> SNES
                }
            }
        }
    }

    enum class ControlGroup(val key: String, val labelRu: String) {
        DPAD("dpad", "Крестовина"),
        FACE_BUTTONS("face", "Кнопки действий"),
        LEFT_STICK("lstick", "Левый стик"),
        RIGHT_STICK("rstick", "Правый стик"),
        SHOULDERS("shoulders", "Курки L / R"),
        START_SELECT("start_select", "Select / Start")
    }

    data class GroupPosition(var xPct: Float, var yPct: Float)

    private enum class SymbolKind {
        TEXT, CROSS, CIRCLE, SQUARE, TRIANGLE, MENU, REWIND, FAST_FORWARD, TURBO
    }

    private enum class ShapeKind {
        ROUND_CAP, PILL, BUMPER_LEFT, BUMPER_RIGHT, TRIGGER_LEFT, TRIGGER_RIGHT
    }

    private enum class FaceRole {
        NONE, SOUTH_B, EAST_A, WEST_Y, NORTH_X, UPPER_C, UPPER_Z
    }

    private class VirtualButton(
        val key: String,
        val group: ControlGroup?,
        val libretroId: Int,
        val label: String,
        val subLabel: String = "",
        val symbol: SymbolKind = SymbolKind.TEXT,
        val shape: ShapeKind = ShapeKind.ROUND_CAP,
        val faceRole: FaceRole = FaceRole.NONE,
        val cx: Float,
        val cy: Float,
        val radius: Float = 0f,
        val capsuleHalfW: Float = 0f,
        val capsuleHalfH: Float = 0f,
        val isTurboDedicated: Boolean = false,
        val supportsTurboLatch: Boolean = false,
        val isUtility: Boolean = false,
        var pressProgress: Float = 0f
    ) {
        val isCapsule: Boolean get() = shape != ShapeKind.ROUND_CAP
    }

    companion object {
        const val RETRO_DEVICE_ID_JOYPAD_B = 0
        const val RETRO_DEVICE_ID_JOYPAD_Y = 1
        const val RETRO_DEVICE_ID_JOYPAD_SELECT = 2
        const val RETRO_DEVICE_ID_JOYPAD_START = 3
        const val RETRO_DEVICE_ID_JOYPAD_UP = 4
        const val RETRO_DEVICE_ID_JOYPAD_DOWN = 5
        const val RETRO_DEVICE_ID_JOYPAD_LEFT = 6
        const val RETRO_DEVICE_ID_JOYPAD_RIGHT = 7
        const val RETRO_DEVICE_ID_JOYPAD_A = 8
        const val RETRO_DEVICE_ID_JOYPAD_X = 9
        const val RETRO_DEVICE_ID_JOYPAD_L = 10
        const val RETRO_DEVICE_ID_JOYPAD_R = 11
        const val RETRO_DEVICE_ID_JOYPAD_L2 = 12
        const val RETRO_DEVICE_ID_JOYPAD_R2 = 13
        const val RETRO_DEVICE_ID_JOYPAD_L3 = 14
        const val RETRO_DEVICE_ID_JOYPAD_R3 = 15

        const val STICK_LEFT = 0
        const val STICK_RIGHT = 1
        const val MAX_AXIS = 32767

        const val SHARED_PREFS_UNIFIED = "storm_retroarch_prefs"
        private const val PREFS_LAYOUTS = "storm_touch_layouts_prefs"

        const val MIN_SCALE = 0.70f
        const val MAX_SCALE = 1.40f
        const val MIN_ALPHA = 0.25f
        const val MAX_ALPHA = 1.00f

        const val PORTRAIT_DECK_TOP_RATIO = 0.48f
        private const val TURBO_HALF_PERIOD_MS = 33L // ~15 Hz full cycle (33ms ON + 33ms OFF)
    }

    var callback: Callback? = null
    var hapticsEnabled: Boolean = true
    var accentColor: Int = 0xFF8B5CF6.toInt()
        set(value) {
            field = value
            rebuildDeckShaders()
            invalidate()
        }

    var buttonColorTheme: ButtonColorTheme = ButtonColorTheme.STORM_VIOLET
        private set

    var layoutProfile: LayoutProfile = LayoutProfile.SNES
        private set

    var buttonScale: Float = 1.0f
        private set

    var overlayAlpha: Float = 0.82f
        private set

    var isTurboLatchActive: Boolean = false
        set(value) {
            if (field != value) {
                field = value
                recomputeDigitalOutputs(triggerHaptics = false)
                callback?.onTurboLatchChanged(value)
                invalidate()
            }
        }

    var fastForwardSpeedLabel: String = "FAST 2X"
        set(value) {
            field = value
            invalidate()
        }

    var isFastForwardActive: Boolean = false
        set(value) {
            field = value
            invalidate()
        }

    var isEditMode: Boolean = false
        set(value) {
            if (field != value) {
                field = value
                releaseAllInputs()
                if (!value) {
                    saveLayouts()
                }
                rebuildGeometry()
                callback?.onDeckVisibilityChanged()
                invalidate()
            }
        }

    private var currentPlatformRaw: String = "SNES"
    private var physicalControllerActive: Boolean = false
    private var visibilityFactor: Float = 1.0f
    private var fadeAnimator: ValueAnimator? = null

    private val customGroupPositions = HashMap<String, GroupPosition>()
    private val groupBounds = EnumMapLike<ControlGroup, RectF>()
    private val buttons = ArrayList<VirtualButton>(28)

    // D-Pad / Arcade stick geometry
    private var dpadCx = 0f
    private var dpadCy = 0f
    private var dpadRadius = 0f
    private val dpadCrossPath = Path()
    private val dpadSkirtPath = Path()
    private val dpadTmpPathA = Path()
    private val dpadTmpPathB = Path()

    // Analog stick geometry & live state
    private var hasAnalogSticks = false
    private var leftStickCx = 0f
    private var leftStickCy = 0f
    private var leftStickRadius = 0f
    private var leftKnobDx = 0f
    private var leftKnobDy = 0f
    private var leftStickPointerId = MotionEvent.INVALID_POINTER_ID

    private var rightStickCx = 0f
    private var rightStickCy = 0f
    private var rightStickRadius = 0f
    private var rightKnobDx = 0f
    private var rightKnobDy = 0f
    private var rightStickPointerId = MotionEvent.INVALID_POINTER_ID

    // Live gyroscope visual preview offsets (-1..1)
    private var gyroLeftNormX = 0f
    private var gyroLeftNormY = 0f
    private var gyroRightNormX = 0f
    private var gyroRightNormY = 0f

    private var dpadPointerId = MotionEvent.INVALID_POINTER_ID
    private var arcadeKnobDx = 0f
    private var arcadeKnobDy = 0f

    // Portrait Handheld Deck cached geometry & shaders
    private val deckRect = RectF()
    private val deckHeaderLipPath = Path()
    private var deckPlateShader: LinearGradient? = null
    private var deckLipGlowShader: LinearGradient? = null

    // Editor state & Pinch scale detector
    private var draggingGroup: ControlGroup? = null
    private var dragPointerId: Int = MotionEvent.INVALID_POINTER_ID
    private var dragOffsetX = 0f
    private var dragOffsetY = 0f
    private val editorBarRect = RectF()
    private val btnScaleDownRect = RectF()
    private val btnScaleUpRect = RectF()
    private val btnAlphaDownRect = RectF()
    private val btnAlphaUpRect = RectF()
    private val btnResetRect = RectF()
    private val btnDoneRect = RectF()

    private val pinchDetector = ScaleGestureDetector(
        context,
        object : ScaleGestureDetector.SimpleOnScaleGestureListener() {
            override fun onScale(detector: ScaleGestureDetector): Boolean {
                if (!isEditMode) return false
                val factor = detector.scaleFactor
                if (abs(factor - 1.0f) > 0.004f) {
                    setButtonScale(buttonScale * factor, persist = false)
                }
                return true
            }

            override fun onScaleEnd(detector: ScaleGestureDetector) {
                if (isEditMode) {
                    saveLayouts()
                }
            }
        }
    )

    // Active digital & utility state
    private val digitalState = BooleanArray(16)
    private val pointerButtons = HashMap<Int, MutableSet<String>>()
    private var rewindHeld = false
    private var turboPhase = false
    private var turboRunning = false

    private val turboTicker = object : Runnable {
        override fun run() {
            if (!turboRunning) return
            turboPhase = !turboPhase
            recomputeDigitalOutputs(triggerHaptics = false)
            invalidate()
            postDelayed(this, TURBO_HALF_PERIOD_MS)
        }
    }

    // Pre-allocated paints, matrices & unit gradients (zero allocations in onDraw)
    private val density = resources.displayMetrics.density
    private val shaderMatrix = Matrix()
    private val fillPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }
    private val strokePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE }
    private val specularArcPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeCap = Paint.Cap.ROUND
    }
    private val symbolPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeCap = Paint.Cap.ROUND
        strokeJoin = Paint.Join.ROUND
    }
    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        textAlign = Paint.Align.CENTER
        typeface = Typeface.create("sans-serif-medium", Typeface.BOLD)
    }
    private val subTextPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        textAlign = Paint.Align.CENTER
        typeface = Typeface.create("sans-serif", Typeface.BOLD)
    }
    private val editorBoxPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeWidth = 1.8f * density
        pathEffect = DashPathEffect(floatArrayOf(6f * density, 4f * density), 0f)
    }
    private val editorHudPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }

    private val tempRect = RectF()
    private val arcRect = RectF()
    private val shapePath = Path()
    private val arrowPath = Path()
    private val cornerRadii = FloatArray(8)

    // Unit-space socket & concave dish shaders transformed via shaderMatrix in onDraw
    private val unitSocketWellShader = RadialGradient(
        -0.18f, -0.22f, 1.25f,
        intArrayOf(0xF004030A.toInt(), 0xEB0E0C1D.toInt(), 0xE01E1A36.toInt()),
        floatArrayOf(0f, 0.72f, 1f),
        Shader.TileMode.CLAMP
    )
    private val unitConcaveDimpleShader = RadialGradient(
        0.24f, 0.28f, 1.15f,
        intArrayOf(0xCC302A52.toInt(), 0xDE141126.toInt(), 0xF2080612.toInt()),
        floatArrayOf(0f, 0.62f, 1f),
        Shader.TileMode.CLAMP
    )
    private val unitStickThumbCapShader = RadialGradient(
        -0.28f, -0.32f, 1.30f,
        intArrayOf(0xFA5B528A.toInt(), 0xF5342E54.toInt(), 0xFA1B1730.toInt(), 0xFF0E0C1B.toInt()),
        floatArrayOf(0f, 0.42f, 0.82f, 1f),
        Shader.TileMode.CLAMP
    )

    // Cached unit-space convex dome shaders keyed by base cap color RGB + theme mode
    private data class CapShaderEntry(
        val domeShader: RadialGradient,
        val haloShader: RadialGradient
    )
    private val capShaderCache = HashMap<Int, CapShaderEntry>(16)

    private class EnumMapLike<K : Enum<K>, V> {
        private val map = HashMap<K, V>()
        operator fun get(key: K): V? = map[key]
        operator fun set(key: K, value: V) { map[key] = value }
        fun clear() = map.clear()
        fun entries(): Set<Map.Entry<K, V>> = map.entries
    }

    init {
        isClickable = false
        isFocusable = false
        setLayerType(LAYER_TYPE_HARDWARE, null)
        loadLayouts()
    }

    private fun settingsFile(): File {
        val root = File(Environment.getExternalStorageDirectory(), "STORM RETROARCH/settings")
        return File(root, "touch_layouts.json")
    }

    private fun readFlexibleFloat(prefs: android.content.SharedPreferences, key: String, fallback: Float): Float {
        val raw = prefs.all[key] ?: return fallback
        return when (raw) {
            is Float -> raw
            is Double -> raw.toFloat()
            is Int -> raw.toFloat()
            is Long -> raw.toFloat()
            is String -> raw.toFloatOrNull() ?: fallback
            else -> fallback
        }
    }

    fun isPortraitDeckVisible(): Boolean {
        if (width <= 0 || height <= 0) return false
        val portrait = height > width
        if (!portrait) return false
        if (layoutProfile == LayoutProfile.HIDDEN) return false
        if (physicalControllerActive && !isEditMode) return false
        return true
    }

    fun getActiveViewportBottomRatio(): Float {
        return if (isPortraitDeckVisible()) PORTRAIT_DECK_TOP_RATIO else 1.0f
    }

    fun setPlatform(platformName: String?) {
        currentPlatformRaw = platformName ?: "SNES"
        val unified = context.getSharedPreferences(SHARED_PREFS_UNIFIED, Context.MODE_PRIVATE)
        val savedPreset = unified.getString("touchPreset", null)?.trim()
        val resolved = if (savedPreset.isNullOrEmpty() || savedPreset.equals("Auto", ignoreCase = true)) {
            LayoutProfile.resolveForPlatform(currentPlatformRaw)
        } else {
            LayoutProfile.fromId(savedPreset)
        }
        setLayoutProfile(resolved, persistPresetPref = false)
    }

    fun setLayoutProfile(profile: LayoutProfile, persistPresetPref: Boolean = false) {
        val changed = layoutProfile != profile
        if (changed) {
            releaseAllInputs()
            layoutProfile = profile
        }
        if (persistPresetPref) {
            context.getSharedPreferences(SHARED_PREFS_UNIFIED, Context.MODE_PRIVATE)
                .edit()
                .putString("touchPreset", profile.id)
                .apply()
        }
        rebuildGeometry()
        if (changed) {
            callback?.onDeckVisibilityChanged()
        }
        invalidate()
    }

    fun setButtonColorTheme(theme: ButtonColorTheme, persist: Boolean = true) {
        buttonColorTheme = theme
        capShaderCache.clear()
        if (persist) {
            context.getSharedPreferences(SHARED_PREFS_UNIFIED, Context.MODE_PRIVATE)
                .edit()
                .putString("touchColorTheme", theme.id)
                .apply()
            saveLayouts()
        }
        rebuildGeometry()
        invalidate()
    }

    fun setButtonColorThemeByName(name: String?, persist: Boolean = true) {
        setButtonColorTheme(ButtonColorTheme.fromId(name), persist)
    }

    fun setButtonScale(scale: Float, persist: Boolean = true) {
        val normalized = if (scale > 2.5f) scale / 100f else scale
        buttonScale = normalized.coerceIn(MIN_SCALE, MAX_SCALE)
        if (persist) {
            context.getSharedPreferences(SHARED_PREFS_UNIFIED, Context.MODE_PRIVATE)
                .edit()
                .putFloat("touchScale", buttonScale)
                .apply()
            saveLayouts()
        }
        rebuildGeometry()
        invalidate()
    }

    fun setOverlayAlpha(alpha: Float, persist: Boolean = true) {
        val normalized = if (alpha > 2.0f) alpha / 100f else alpha
        overlayAlpha = normalized.coerceIn(MIN_ALPHA, MAX_ALPHA)
        if (persist) {
            context.getSharedPreferences(SHARED_PREFS_UNIFIED, Context.MODE_PRIVATE)
                .edit()
                .putFloat("touchOpacity", overlayAlpha)
                .apply()
            saveLayouts()
        }
        invalidate()
    }

    fun setGyroStickPreview(stickIndex: Int, normX: Float, normY: Float) {
        val cx = normX.coerceIn(-1f, 1f)
        val cy = normY.coerceIn(-1f, 1f)
        if (stickIndex == STICK_LEFT) {
            if (abs(gyroLeftNormX - cx) > 0.01f || abs(gyroLeftNormY - cy) > 0.01f) {
                gyroLeftNormX = cx
                gyroLeftNormY = cy
                invalidate()
            }
        } else {
            if (abs(gyroRightNormX - cx) > 0.01f || abs(gyroRightNormY - cy) > 0.01f) {
                gyroRightNormX = cx
                gyroRightNormY = cy
                invalidate()
            }
        }
    }

    fun setPhysicalControllerActive(active: Boolean) {
        if (physicalControllerActive == active) return
        physicalControllerActive = active
        if (active) {
            releaseAllInputs()
        }
        callback?.onDeckVisibilityChanged()
        val target = if (active) 0f else 1f
        fadeAnimator?.cancel()
        fadeAnimator = ValueAnimator.ofFloat(visibilityFactor, target).apply {
            duration = if (active) 200L else 160L
            interpolator = DecelerateInterpolator()
            addUpdateListener { anim ->
                visibilityFactor = anim.animatedValue as Float
                invalidate()
            }
            start()
        }
    }

    fun isPhysicalControllerHidingOverlay(): Boolean = physicalControllerActive

    fun syncPreferencesFromUnifiedStore() {
        val unified = context.getSharedPreferences(SHARED_PREFS_UNIFIED, Context.MODE_PRIVATE)
        if (unified.contains("touchColorTheme")) {
            buttonColorTheme = ButtonColorTheme.fromId(unified.getString("touchColorTheme", buttonColorTheme.id))
            capShaderCache.clear()
        }
        if (unified.contains("touchScale")) {
            val rawS = readFlexibleFloat(unified, "touchScale", buttonScale)
            buttonScale = (if (rawS > 2.5f) rawS / 100f else rawS).coerceIn(MIN_SCALE, MAX_SCALE)
        }
        if (unified.contains("touchOpacity")) {
            val rawA = readFlexibleFloat(unified, "touchOpacity", overlayAlpha)
            overlayAlpha = (if (rawA > 2.0f) rawA / 100f else rawA).coerceIn(MIN_ALPHA, MAX_ALPHA)
        }
        if (unified.contains("touchHaptics")) {
            hapticsEnabled = unified.getBoolean("touchHaptics", hapticsEnabled)
        }
        val presetStr = unified.getString("touchPreset", null)
        if (!presetStr.isNullOrBlank() && !presetStr.equals("Auto", ignoreCase = true)) {
            layoutProfile = LayoutProfile.fromId(presetStr)
        }
        rebuildGeometry()
        callback?.onDeckVisibilityChanged()
        invalidate()
    }

    fun resetCurrentLayout() {
        val orientPrefix = if (isLandscape()) "land" else "port"
        val prefix = "${layoutProfile.id}_${orientPrefix}_"
        val iter = customGroupPositions.keys.iterator()
        while (iter.hasNext()) {
            if (iter.next().startsWith(prefix)) {
                iter.remove()
            }
        }
        buttonScale = 1.0f
        overlayAlpha = 0.82f
        saveLayouts()
        rebuildGeometry()
        invalidate()
    }

    fun resetAllLayouts() {
        customGroupPositions.clear()
        buttonScale = 1.0f
        overlayAlpha = 0.82f
        context.getSharedPreferences(SHARED_PREFS_UNIFIED, Context.MODE_PRIVATE)
            .edit()
            .putFloat("touchScale", buttonScale)
            .putFloat("touchOpacity", overlayAlpha)
            .remove("touchPreset")
            .apply()
        saveLayouts()
        rebuildGeometry()
        invalidate()
    }

    fun loadLayouts() {
        try {
            val prefs = context.getSharedPreferences(PREFS_LAYOUTS, Context.MODE_PRIVATE)
            var rawJson = prefs.getString("layouts_json", null)
            val external = settingsFile()
            if (external.exists() && external.length() > 2L) {
                rawJson = external.readText(Charsets.UTF_8)
            }
            if (!rawJson.isNullOrBlank()) {
                val root = JSONObject(rawJson)
                buttonScale = root.optDouble("buttonScale", 1.0).toFloat().coerceIn(MIN_SCALE, MAX_SCALE)
                overlayAlpha = root.optDouble("overlayAlpha", 0.82).toFloat().coerceIn(MIN_ALPHA, MAX_ALPHA)
                hapticsEnabled = root.optBoolean("hapticsEnabled", true)
                val themeRaw = root.optString("buttonColorTheme", "")
                if (themeRaw.isNotBlank()) {
                    buttonColorTheme = ButtonColorTheme.fromId(themeRaw)
                }
                val groupsObj = root.optJSONObject("groups")
                if (groupsObj != null) {
                    customGroupPositions.clear()
                    val keys = groupsObj.keys()
                    while (keys.hasNext()) {
                        val k = keys.next()
                        val item = groupsObj.optJSONObject(k) ?: continue
                        val x = item.optDouble("x", 0.5).toFloat().coerceIn(0.05f, 0.95f)
                        val y = item.optDouble("y", 0.5).toFloat().coerceIn(0.05f, 0.96f)
                        customGroupPositions[k] = GroupPosition(x, y)
                    }
                }
            }
            // Apply overrides from unified storm_retroarch_prefs if set
            val unified = context.getSharedPreferences(SHARED_PREFS_UNIFIED, Context.MODE_PRIVATE)
            if (unified.contains("touchColorTheme")) {
                buttonColorTheme = ButtonColorTheme.fromId(unified.getString("touchColorTheme", buttonColorTheme.id))
            }
            if (unified.contains("touchScale")) {
                val rawS = readFlexibleFloat(unified, "touchScale", buttonScale)
                buttonScale = (if (rawS > 2.5f) rawS / 100f else rawS).coerceIn(MIN_SCALE, MAX_SCALE)
            }
            if (unified.contains("touchOpacity")) {
                val rawA = readFlexibleFloat(unified, "touchOpacity", overlayAlpha)
                overlayAlpha = (if (rawA > 2.0f) rawA / 100f else rawA).coerceIn(MIN_ALPHA, MAX_ALPHA)
            }
            if (unified.contains("touchHaptics")) {
                hapticsEnabled = unified.getBoolean("touchHaptics", hapticsEnabled)
            }
        } catch (_: Throwable) {
        }
    }

    fun saveLayouts() {
        try {
            val root = JSONObject()
            root.put("buttonScale", buttonScale.toDouble())
            root.put("overlayAlpha", overlayAlpha.toDouble())
            root.put("hapticsEnabled", hapticsEnabled)
            root.put("buttonColorTheme", buttonColorTheme.id)
            val groupsObj = JSONObject()
            for ((k, pos) in customGroupPositions) {
                val item = JSONObject()
                item.put("x", pos.xPct.toDouble())
                item.put("y", pos.yPct.toDouble())
                groupsObj.put(k, item)
            }
            root.put("groups", groupsObj)
            val serialized = root.toString(2)
            context.getSharedPreferences(PREFS_LAYOUTS, Context.MODE_PRIVATE)
                .edit()
                .putString("layouts_json", serialized)
                .apply()
            context.getSharedPreferences(SHARED_PREFS_UNIFIED, Context.MODE_PRIVATE)
                .edit()
                .putFloat("touchScale", buttonScale)
                .putFloat("touchOpacity", overlayAlpha)
                .putBoolean("touchHaptics", hapticsEnabled)
                .putString("touchColorTheme", buttonColorTheme.id)
                .apply()
            val ext = settingsFile()
            ext.parentFile?.mkdirs()
            ext.writeText(serialized, Charsets.UTF_8)
        } catch (_: Throwable) {
        }
    }

    private fun isLandscape(): Boolean = width >= height && width > 0

    private fun groupKey(group: ControlGroup): String {
        val orient = if (isLandscape()) "land" else "port"
        return "${layoutProfile.id}_${orient}_${group.key}"
    }

    private fun defaultGroupPosition(group: ControlGroup, land: Boolean): GroupPosition {
        val dualShock = layoutProfile == LayoutProfile.CONSOLE_PRO
        return if (land) {
            when (group) {
                ControlGroup.DPAD -> if (dualShock) GroupPosition(0.13f, 0.53f) else GroupPosition(0.14f, 0.67f)
                ControlGroup.FACE_BUTTONS -> if (dualShock) GroupPosition(0.87f, 0.53f) else GroupPosition(0.85f, 0.67f)
                ControlGroup.LEFT_STICK -> GroupPosition(0.28f, 0.81f)
                ControlGroup.RIGHT_STICK -> GroupPosition(0.72f, 0.81f)
                ControlGroup.SHOULDERS -> GroupPosition(0.50f, 0.16f)
                ControlGroup.START_SELECT -> GroupPosition(0.50f, 0.91f)
            }
        } else {
            // Portrait Handheld Deck occupies y = 0.48..1.00 so upper half (0.00..0.48) is 100% unobstructed game display!
            when (group) {
                ControlGroup.SHOULDERS -> GroupPosition(0.50f, 0.555f)
                ControlGroup.DPAD -> if (dualShock) GroupPosition(0.23f, 0.685f) else GroupPosition(0.24f, 0.725f)
                ControlGroup.FACE_BUTTONS -> if (dualShock) GroupPosition(0.77f, 0.685f) else GroupPosition(0.76f, 0.725f)
                ControlGroup.LEFT_STICK -> GroupPosition(0.31f, 0.845f)
                ControlGroup.RIGHT_STICK -> GroupPosition(0.69f, 0.845f)
                ControlGroup.START_SELECT -> GroupPosition(0.50f, 0.945f)
            }
        }
    }

    private fun resolveGroupPos(group: ControlGroup): GroupPosition {
        val existing = customGroupPositions[groupKey(group)]
        if (existing != null) return existing
        return defaultGroupPosition(group, isLandscape())
    }

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        super.onSizeChanged(w, h, oldw, oldh)
        rebuildGeometry()
        callback?.onDeckVisibilityChanged()
    }

    override fun onDetachedFromWindow() {
        releaseAllInputs()
        fadeAnimator?.cancel()
        removeCallbacks(turboTicker)
        super.onDetachedFromWindow()
    }

    private fun rebuildDeckShaders() {
        val w = width.toFloat()
        val h = height.toFloat()
        if (w <= 10f || h <= 10f) return
        val deckTop = h * PORTRAIT_DECK_TOP_RATIO
        deckRect.set(0f, deckTop, w, h)
        deckPlateShader = LinearGradient(
            0f, deckTop, 0f, h,
            intArrayOf(
                0xFA1B1730.toInt(),
                0xFC110E20.toInt(),
                0xFE090713.toInt()
            ),
            floatArrayOf(0f, 0.45f, 1f),
            Shader.TileMode.CLAMP
        )
        deckLipGlowShader = LinearGradient(
            0f, deckTop, w, deckTop,
            intArrayOf(
                withAlpha(accentColor, 40),
                lightenColor(accentColor, 0.25f, 235),
                0xEB06B6D4.toInt(),
                lightenColor(accentColor, 0.25f, 235),
                withAlpha(accentColor, 40)
            ),
            floatArrayOf(0f, 0.24f, 0.50f, 0.76f, 1f),
            Shader.TileMode.CLAMP
        )
        deckHeaderLipPath.reset()
        val Dip = 10f * density
        deckHeaderLipPath.moveTo(0f, deckTop + Dip)
        deckHeaderLipPath.quadTo(w * 0.12f, deckTop, w * 0.28f, deckTop)
        deckHeaderLipPath.lineTo(w * 0.72f, deckTop)
        deckHeaderLipPath.quadTo(w * 0.88f, deckTop, w, deckTop + Dip)
    }

    private fun rebuildGeometry() {
        val w = width.toFloat()
        val h = height.toFloat()
        if (w <= 10f || h <= 10f) return

        buttons.clear()
        groupBounds.clear()
        rebuildDeckShaders()

        val land = w >= h
        val minDim = min(w, h)
        val unit = (minDim * if (land) 0.078f else 0.073f) * buttonScale

        // System Pill Controls: MENU, REW, FAST 2X, TURBO
        val pillH = max(14.5f * density, 15.5f * density)
        val pillW = max(34f * density, 38f * density)
        val utilY = if (land || layoutProfile == LayoutProfile.HIDDEN) {
            max(24f * density, h * 0.055f)
        } else {
            // Sit cleanly along the upper control rail of the Portrait Handheld Deck!
            h * PORTRAIT_DECK_TOP_RATIO + 22f * density
        }

        buttons.add(
            VirtualButton(
                key = "UTIL_MENU",
                group = null,
                libretroId = -1,
                label = "MENU",
                symbol = SymbolKind.MENU,
                shape = ShapeKind.PILL,
                cx = if (land || layoutProfile == LayoutProfile.HIDDEN) max(48f * density, w * 0.07f) else w * 0.14f,
                cy = utilY,
                capsuleHalfW = pillW * 0.96f,
                capsuleHalfH = pillH,
                isUtility = true
            )
        )

        if (layoutProfile == LayoutProfile.HIDDEN) {
            hasAnalogSticks = false
            return
        }

        if (land) {
            val centerSpan = pillW * 2.08f
            buttons.add(
                VirtualButton(
                    key = "UTIL_REWIND",
                    group = null,
                    libretroId = -2,
                    label = "REW",
                    symbol = SymbolKind.REWIND,
                    shape = ShapeKind.PILL,
                    cx = w * 0.50f - centerSpan,
                    cy = utilY,
                    capsuleHalfW = pillW * 0.86f,
                    capsuleHalfH = pillH * 0.94f,
                    isUtility = true
                )
            )
            buttons.add(
                VirtualButton(
                    key = "UTIL_FF",
                    group = null,
                    libretroId = -3,
                    label = fastForwardSpeedLabel,
                    symbol = SymbolKind.FAST_FORWARD,
                    shape = ShapeKind.PILL,
                    cx = w * 0.50f,
                    cy = utilY,
                    capsuleHalfW = pillW * 1.02f,
                    capsuleHalfH = pillH * 0.94f,
                    isUtility = true
                )
            )
            buttons.add(
                VirtualButton(
                    key = "UTIL_TURBO",
                    group = null,
                    libretroId = -4,
                    label = "TURBO",
                    symbol = SymbolKind.TURBO,
                    shape = ShapeKind.PILL,
                    cx = w * 0.50f + centerSpan,
                    cy = utilY,
                    capsuleHalfW = pillW * 0.96f,
                    capsuleHalfH = pillH * 0.94f,
                    isUtility = true
                )
            )
        } else {
            buttons.add(
                VirtualButton(
                    key = "UTIL_REWIND",
                    group = null,
                    libretroId = -2,
                    label = "REW",
                    symbol = SymbolKind.REWIND,
                    shape = ShapeKind.PILL,
                    cx = w * 0.38f,
                    cy = utilY,
                    capsuleHalfW = pillW * 0.84f,
                    capsuleHalfH = pillH * 0.92f,
                    isUtility = true
                )
            )
            buttons.add(
                VirtualButton(
                    key = "UTIL_FF",
                    group = null,
                    libretroId = -3,
                    label = fastForwardSpeedLabel,
                    symbol = SymbolKind.FAST_FORWARD,
                    shape = ShapeKind.PILL,
                    cx = w * 0.62f,
                    cy = utilY,
                    capsuleHalfW = pillW * 0.96f,
                    capsuleHalfH = pillH * 0.92f,
                    isUtility = true
                )
            )
            buttons.add(
                VirtualButton(
                    key = "UTIL_TURBO",
                    group = null,
                    libretroId = -4,
                    label = "TURBO",
                    symbol = SymbolKind.TURBO,
                    shape = ShapeKind.PILL,
                    cx = w * 0.86f,
                    cy = utilY,
                    capsuleHalfW = pillW * 0.92f,
                    capsuleHalfH = pillH * 0.92f,
                    isUtility = true
                )
            )
        }

        // Sculpted 8-Way Cross D-Pad / Arcade Stick
        val dpadPos = resolveGroupPos(ControlGroup.DPAD)
        dpadRadius = unit * (if (layoutProfile == LayoutProfile.RPG_MINIMAL) 1.72f else 1.96f)
        val minControlY = if (land) dpadRadius + 8f * density else h * PORTRAIT_DECK_TOP_RATIO + dpadRadius + 28f * density
        dpadCx = (dpadPos.xPct * w).coerceIn(dpadRadius + 10f * density, w - dpadRadius - 10f * density)
        dpadCy = (dpadPos.yPct * h).coerceIn(minControlY, h - dpadRadius - 10f * density)
        buildDpadCrossPaths()
        groupBounds[ControlGroup.DPAD] = RectF(
            dpadCx - dpadRadius * 1.15f,
            dpadCy - dpadRadius * 1.15f,
            dpadCx + dpadRadius * 1.15f,
            dpadCy + dpadRadius * 1.15f
        )

        // Start / Select Chamfered Pills
        val ssPos = resolveGroupPos(ControlGroup.START_SELECT)
        val ssCx = (ssPos.xPct * w).coerceIn(unit * 2.4f, w - unit * 2.4f)
        val ssCy = (ssPos.yPct * h).coerceIn(
            if (land) unit * 0.9f else h * PORTRAIT_DECK_TOP_RATIO + unit,
            h - unit * 0.72f
        )
        val capW = unit * 0.96f
        val capH = unit * 0.44f
        val ssGap = unit * 1.22f
        val selectLabel = when (layoutProfile) {
            LayoutProfile.GENESIS_6BTN -> "MODE"
            LayoutProfile.ARCADE_6BTN -> "COIN"
            else -> "SELECT"
        }
        buttons.add(
            VirtualButton(
                key = "BTN_SELECT",
                group = ControlGroup.START_SELECT,
                libretroId = RETRO_DEVICE_ID_JOYPAD_SELECT,
                label = selectLabel,
                shape = ShapeKind.PILL,
                cx = ssCx - ssGap,
                cy = ssCy,
                capsuleHalfW = capW,
                capsuleHalfH = capH
            )
        )
        buttons.add(
            VirtualButton(
                key = "BTN_START",
                group = ControlGroup.START_SELECT,
                libretroId = RETRO_DEVICE_ID_JOYPAD_START,
                label = "START",
                shape = ShapeKind.PILL,
                cx = ssCx + ssGap,
                cy = ssCy,
                capsuleHalfW = capW,
                capsuleHalfH = capH
            )
        )
        groupBounds[ControlGroup.START_SELECT] = RectF(
            ssCx - ssGap - capW - 8f * density,
            ssCy - capH - 8f * density,
            ssCx + ssGap + capW + 8f * density,
            ssCy + capH + 8f * density
        )

        // 3D Volumetric Action Face Buttons
        val facePos = resolveGroupPos(ControlGroup.FACE_BUTTONS)
        val faceSpread = unit * 2.35f
        val fCx = (facePos.xPct * w).coerceIn(faceSpread, w - faceSpread)
        val fCy = (facePos.yPct * h).coerceIn(
            if (land) faceSpread else h * PORTRAIT_DECK_TOP_RATIO + faceSpread * 0.9f,
            h - faceSpread * 0.85f
        )
        val btnR = unit * 0.80f
        val usePsSymbols = buttonColorTheme == ButtonColorTheme.PLAYSTATION

        when (layoutProfile) {
            LayoutProfile.NES -> {
                val dx = unit * 1.08f
                val dy = unit * 0.28f
                buttons.add(
                    VirtualButton(
                        key = "BTN_B", group = ControlGroup.FACE_BUTTONS, libretroId = RETRO_DEVICE_ID_JOYPAD_B,
                        label = "B", symbol = if (usePsSymbols) SymbolKind.CROSS else SymbolKind.TEXT,
                        subLabel = if (usePsSymbols) "B" else "", faceRole = FaceRole.SOUTH_B,
                        cx = fCx - dx, cy = fCy + dy, radius = btnR * 1.08f, supportsTurboLatch = true
                    )
                )
                buttons.add(
                    VirtualButton(
                        key = "BTN_A", group = ControlGroup.FACE_BUTTONS, libretroId = RETRO_DEVICE_ID_JOYPAD_A,
                        label = "A", symbol = if (usePsSymbols) SymbolKind.CIRCLE else SymbolKind.TEXT,
                        subLabel = if (usePsSymbols) "A" else "", faceRole = FaceRole.EAST_A,
                        cx = fCx + dx, cy = fCy - dy, radius = btnR * 1.08f, supportsTurboLatch = true
                    )
                )
                buttons.add(
                    VirtualButton(
                        key = "TURBO_B", group = ControlGroup.FACE_BUTTONS, libretroId = RETRO_DEVICE_ID_JOYPAD_B,
                        label = "TB", faceRole = FaceRole.WEST_Y,
                        cx = fCx - dx, cy = fCy - unit * 1.48f, radius = btnR * 0.78f, isTurboDedicated = true
                    )
                )
                buttons.add(
                    VirtualButton(
                        key = "TURBO_A", group = ControlGroup.FACE_BUTTONS, libretroId = RETRO_DEVICE_ID_JOYPAD_A,
                        label = "TA", faceRole = FaceRole.NORTH_X,
                        cx = fCx + dx, cy = fCy - unit * 1.98f, radius = btnR * 0.78f, isTurboDedicated = true
                    )
                )
            }
            LayoutProfile.SNES -> {
                addFourDiamondFaceButtons(fCx, fCy, unit * 1.30f, btnR, usePsSymbols)
                buildShoulders(w, h, unit, dualTriggers = false, includeStickClicks = false)
            }
            LayoutProfile.RPG_MINIMAL -> {
                addFourDiamondFaceButtons(fCx, fCy, unit * 1.18f, btnR * 0.92f, usePsSymbols)
            }
            LayoutProfile.CONSOLE_PRO -> {
                addFourDiamondFaceButtons(fCx, fCy, unit * 1.30f, btnR, usePsSymbols)
                buildShoulders(w, h, unit, dualTriggers = true, includeStickClicks = true)
            }
            LayoutProfile.GENESIS_6BTN -> {
                addSixArcFaceButtons(fCx, fCy, unit, btnR, arcadeLabels = false)
            }
            LayoutProfile.ARCADE_6BTN -> {
                addSixArcFaceButtons(fCx, fCy, unit, btnR, arcadeLabels = false)
                buildShoulders(w, h, unit, dualTriggers = false, includeStickClicks = false)
            }
            LayoutProfile.HIDDEN -> {}
        }

        computeGroupBoundFromButtons(ControlGroup.FACE_BUTTONS)

        // Textured Concentric-Ring Dual Analog Thumbsticks (Console Pro)
        hasAnalogSticks = layoutProfile == LayoutProfile.CONSOLE_PRO
        if (hasAnalogSticks) {
            val stickR = unit * 1.38f
            val minStickY = if (land) stickR + 8f * density else h * PORTRAIT_DECK_TOP_RATIO + stickR + 24f * density

            val lPos = resolveGroupPos(ControlGroup.LEFT_STICK)
            leftStickRadius = stickR
            leftStickCx = (lPos.xPct * w).coerceIn(stickR + 10f * density, w - stickR - 10f * density)
            leftStickCy = (lPos.yPct * h).coerceIn(minStickY, h - stickR - 10f * density)
            groupBounds[ControlGroup.LEFT_STICK] = RectF(
                leftStickCx - stickR * 1.15f,
                leftStickCy - stickR * 1.15f,
                leftStickCx + stickR * 1.15f,
                leftStickCy + stickR * 1.15f
            )

            val rPos = resolveGroupPos(ControlGroup.RIGHT_STICK)
            rightStickRadius = stickR
            rightStickCx = (rPos.xPct * w).coerceIn(stickR + 10f * density, w - stickR - 10f * density)
            rightStickCy = (rPos.yPct * h).coerceIn(minStickY, h - stickR - 10f * density)
            groupBounds[ControlGroup.RIGHT_STICK] = RectF(
                rightStickCx - stickR * 1.15f,
                rightStickCy - stickR * 1.15f,
                rightStickCx + stickR * 1.15f,
                rightStickCy + stickR * 1.15f
            )
        }

        // Interactive Layout Editor HUD bar geometry
        val barW = min(w - 24f * density, 392f * density)
        val barH = 76f * density
        val barLeft = (w - barW) * 0.5f
        val barTop = if (land) max(utilY + pillH + 12f * density, 54f * density) else max(28f * density, h * 0.06f)
        editorBarRect.set(barLeft, barTop, barLeft + barW, barTop + barH)

        val row1Y = barTop + 23f * density
        val row2Y = barTop + 54f * density
        val smallBtnW = 30f * density
        val smallBtnH = 22f * density
        btnScaleDownRect.set(barLeft + 92f * density, row1Y - smallBtnH * 0.5f, barLeft + 92f * density + smallBtnW, row1Y + smallBtnH * 0.5f)
        btnScaleUpRect.set(barLeft + 156f * density, row1Y - smallBtnH * 0.5f, barLeft + 156f * density + smallBtnW, row1Y + smallBtnH * 0.5f)
        btnAlphaDownRect.set(barRight() - 98f * density, row1Y - smallBtnH * 0.5f, barRight() - 98f * density + smallBtnW, row1Y + smallBtnH * 0.5f)
        btnAlphaUpRect.set(barRight() - 36f * density, row1Y - smallBtnH * 0.5f, barRight() - 36f * density + smallBtnW, row1Y + smallBtnH * 0.5f)

        btnResetRect.set(barLeft + 14f * density, row2Y - 12f * density, barLeft + barW * 0.48f, row2Y + 12f * density)
        btnDoneRect.set(barLeft + barW * 0.52f, row2Y - 12f * density, barRight() - 14f * density, row2Y + 12f * density)
    }

    private fun addFourDiamondFaceButtons(
        fCx: Float,
        fCy: Float,
        spread: Float,
        btnR: Float,
        usePsSymbols: Boolean
    ) {
        val isXbox = buttonColorTheme == ButtonColorTheme.XBOX
        // Standard Libretro diamond positions: West=Y(X on Xbox), North=X(Y on Xbox), South=B(A on Xbox), East=A(B on Xbox)
        // Keep physical labels authentic to theme while preserving standard Libretro IDs!
        val westLabel = if (isXbox) "X" else "Y"
        val northLabel = if (isXbox) "Y" else "X"
        val southLabel = if (isXbox) "A" else "B"
        val eastLabel = if (isXbox) "B" else "A"

        buttons.add(
            VirtualButton(
                key = "BTN_Y",
                group = ControlGroup.FACE_BUTTONS,
                libretroId = RETRO_DEVICE_ID_JOYPAD_Y,
                label = westLabel,
                subLabel = if (usePsSymbols) "Y" else "",
                symbol = if (usePsSymbols) SymbolKind.SQUARE else SymbolKind.TEXT,
                faceRole = FaceRole.WEST_Y,
                cx = fCx - spread,
                cy = fCy,
                radius = btnR,
                supportsTurboLatch = true
            )
        )
        buttons.add(
            VirtualButton(
                key = "BTN_X",
                group = ControlGroup.FACE_BUTTONS,
                libretroId = RETRO_DEVICE_ID_JOYPAD_X,
                label = northLabel,
                subLabel = if (usePsSymbols) "X" else "",
                symbol = if (usePsSymbols) SymbolKind.TRIANGLE else SymbolKind.TEXT,
                faceRole = FaceRole.NORTH_X,
                cx = fCx,
                cy = fCy - spread,
                radius = btnR,
                supportsTurboLatch = true
            )
        )
        buttons.add(
            VirtualButton(
                key = "BTN_B",
                group = ControlGroup.FACE_BUTTONS,
                libretroId = RETRO_DEVICE_ID_JOYPAD_B,
                label = southLabel,
                subLabel = if (usePsSymbols) "B" else "",
                symbol = if (usePsSymbols) SymbolKind.CROSS else SymbolKind.TEXT,
                faceRole = FaceRole.SOUTH_B,
                cx = fCx,
                cy = fCy + spread,
                radius = btnR,
                supportsTurboLatch = true
            )
        )
        buttons.add(
            VirtualButton(
                key = "BTN_A",
                group = ControlGroup.FACE_BUTTONS,
                libretroId = RETRO_DEVICE_ID_JOYPAD_A,
                label = eastLabel,
                subLabel = if (usePsSymbols) "A" else "",
                symbol = if (usePsSymbols) SymbolKind.CIRCLE else SymbolKind.TEXT,
                faceRole = FaceRole.EAST_A,
                cx = fCx + spread,
                cy = fCy,
                radius = btnR,
                supportsTurboLatch = true
            )
        )
    }

    private fun addSixArcFaceButtons(
        fCx: Float,
        fCy: Float,
        unit: Float,
        btnR: Float,
        arcadeLabels: Boolean
    ) {
        val col = unit * 1.54f
        val row = unit * 0.88f
        val topR = btnR * 0.84f
        val botR = btnR * 0.94f
        // Authentic Sega / Capcom 6-button ergonomic arc: X, Y, Z upper arc + A, B, C lower arc
        buttons.add(
            VirtualButton(
                key = "SIX_X", group = ControlGroup.FACE_BUTTONS, libretroId = RETRO_DEVICE_ID_JOYPAD_L,
                label = if (arcadeLabels) "1" else "X", faceRole = FaceRole.NORTH_X,
                cx = fCx - col, cy = fCy - row + unit * 0.22f, radius = topR, supportsTurboLatch = true
            )
        )
        buttons.add(
            VirtualButton(
                key = "SIX_Y", group = ControlGroup.FACE_BUTTONS, libretroId = RETRO_DEVICE_ID_JOYPAD_X,
                label = if (arcadeLabels) "2" else "Y", faceRole = FaceRole.WEST_Y,
                cx = fCx, cy = fCy - row, radius = topR, supportsTurboLatch = true
            )
        )
        buttons.add(
            VirtualButton(
                key = "SIX_Z", group = ControlGroup.FACE_BUTTONS, libretroId = RETRO_DEVICE_ID_JOYPAD_R,
                label = if (arcadeLabels) "3" else "Z", faceRole = FaceRole.UPPER_Z,
                cx = fCx + col, cy = fCy - row - unit * 0.18f, radius = topR, supportsTurboLatch = true
            )
        )
        buttons.add(
            VirtualButton(
                key = "SIX_A", group = ControlGroup.FACE_BUTTONS, libretroId = RETRO_DEVICE_ID_JOYPAD_Y,
                label = if (arcadeLabels) "4" else "A", faceRole = FaceRole.SOUTH_B,
                cx = fCx - col, cy = fCy + row + unit * 0.22f, radius = botR, supportsTurboLatch = true
            )
        )
        buttons.add(
            VirtualButton(
                key = "SIX_B", group = ControlGroup.FACE_BUTTONS, libretroId = RETRO_DEVICE_ID_JOYPAD_B,
                label = if (arcadeLabels) "5" else "B", faceRole = FaceRole.EAST_A,
                cx = fCx, cy = fCy + row, radius = botR, supportsTurboLatch = true
            )
        )
        buttons.add(
            VirtualButton(
                key = "SIX_C", group = ControlGroup.FACE_BUTTONS, libretroId = RETRO_DEVICE_ID_JOYPAD_A,
                label = if (arcadeLabels) "6" else "C", faceRole = FaceRole.UPPER_C,
                cx = fCx + col, cy = fCy + row - unit * 0.18f, radius = botR, supportsTurboLatch = true
            )
        )
    }

    private fun buildDpadCrossPaths() {
        val arm = dpadRadius * 0.34f
        val r = dpadRadius
        val corner = arm * 0.30f
        dpadTmpPathA.reset()
        dpadTmpPathA.addRoundRect(
            RectF(dpadCx - arm, dpadCy - r, dpadCx + arm, dpadCy + r),
            corner, corner, Path.Direction.CW
        )
        dpadTmpPathB.reset()
        dpadTmpPathB.addRoundRect(
            RectF(dpadCx - r, dpadCy - arm, dpadCx + r, dpadCy + arm),
            corner, corner, Path.Direction.CW
        )
        dpadCrossPath.reset()
        if (!dpadCrossPath.op(dpadTmpPathA, dpadTmpPathB, Path.Op.UNION)) {
            dpadCrossPath.addPath(dpadTmpPathA)
            dpadCrossPath.addPath(dpadTmpPathB)
        }
        dpadSkirtPath.set(dpadCrossPath)
        dpadSkirtPath.offset(0f, 2.6f * density)
    }

    private fun barRight(): Float = editorBarRect.right

    private fun buildShoulders(
        w: Float,
        h: Float,
        unit: Float,
        dualTriggers: Boolean,
        includeStickClicks: Boolean
    ) {
        val land = w >= h
        val shPos = resolveGroupPos(ControlGroup.SHOULDERS)
        val minShY = if (land) unit * 0.9f else h * PORTRAIT_DECK_TOP_RATIO + 48f * density
        val shCy = (shPos.yPct * h).coerceIn(minShY, h - unit * 0.9f)
        val shCenterOffset = (shPos.xPct - 0.50f) * w
        val padHalfW = unit * 1.20f
        val padHalfH = unit * 0.48f
        val leftX = (w * 0.14f + shCenterOffset).coerceIn(padHalfW + 8f * density, w * 0.45f)
        val rightX = (w * 0.86f + shCenterOffset).coerceIn(w * 0.55f, w - padHalfW - 8f * density)

        if (!dualTriggers) {
            buttons.add(
                VirtualButton(
                    key = "BTN_L", group = ControlGroup.SHOULDERS, libretroId = RETRO_DEVICE_ID_JOYPAD_L,
                    label = "L1", shape = ShapeKind.BUMPER_LEFT,
                    cx = leftX, cy = shCy, capsuleHalfW = padHalfW, capsuleHalfH = padHalfH
                )
            )
            buttons.add(
                VirtualButton(
                    key = "BTN_R", group = ControlGroup.SHOULDERS, libretroId = RETRO_DEVICE_ID_JOYPAD_R,
                    label = "R1", shape = ShapeKind.BUMPER_RIGHT,
                    cx = rightX, cy = shCy, capsuleHalfW = padHalfW, capsuleHalfH = padHalfH
                )
            )
        } else {
            if (land) {
                val innerGap = padHalfW * 2.06f
                buttons.add(
                    VirtualButton(
                        key = "BTN_L2", group = ControlGroup.SHOULDERS, libretroId = RETRO_DEVICE_ID_JOYPAD_L2,
                        label = "L2", shape = ShapeKind.TRIGGER_LEFT,
                        cx = leftX, cy = shCy - padHalfH * 1.12f, capsuleHalfW = padHalfW * 0.94f, capsuleHalfH = padHalfH * 0.88f
                    )
                )
                buttons.add(
                    VirtualButton(
                        key = "BTN_L1", group = ControlGroup.SHOULDERS, libretroId = RETRO_DEVICE_ID_JOYPAD_L,
                        label = "L1", shape = ShapeKind.BUMPER_LEFT,
                        cx = leftX, cy = shCy + padHalfH * 1.24f, capsuleHalfW = padHalfW * 0.96f, capsuleHalfH = padHalfH * 0.88f
                    )
                )
                buttons.add(
                    VirtualButton(
                        key = "BTN_R2", group = ControlGroup.SHOULDERS, libretroId = RETRO_DEVICE_ID_JOYPAD_R2,
                        label = "R2", shape = ShapeKind.TRIGGER_RIGHT,
                        cx = rightX, cy = shCy - padHalfH * 1.12f, capsuleHalfW = padHalfW * 0.94f, capsuleHalfH = padHalfH * 0.88f
                    )
                )
                buttons.add(
                    VirtualButton(
                        key = "BTN_R1", group = ControlGroup.SHOULDERS, libretroId = RETRO_DEVICE_ID_JOYPAD_R,
                        label = "R1", shape = ShapeKind.BUMPER_RIGHT,
                        cx = rightX, cy = shCy + padHalfH * 1.24f, capsuleHalfW = padHalfW * 0.96f, capsuleHalfH = padHalfH * 0.88f
                    )
                )
                if (includeStickClicks) {
                    buttons.add(
                        VirtualButton(
                            key = "BTN_L3", group = ControlGroup.SHOULDERS, libretroId = RETRO_DEVICE_ID_JOYPAD_L3,
                            label = "L3", shape = ShapeKind.PILL,
                            cx = min(w * 0.43f, leftX + innerGap), cy = shCy + padHalfH * 0.12f,
                            capsuleHalfW = padHalfW * 0.62f, capsuleHalfH = padHalfH * 0.80f
                        )
                    )
                    buttons.add(
                        VirtualButton(
                            key = "BTN_R3", group = ControlGroup.SHOULDERS, libretroId = RETRO_DEVICE_ID_JOYPAD_R3,
                            label = "R3", shape = ShapeKind.PILL,
                            cx = max(w * 0.57f, rightX - innerGap), cy = shCy + padHalfH * 0.12f,
                            capsuleHalfW = padHalfW * 0.62f, capsuleHalfH = padHalfH * 0.80f
                        )
                    )
                }
            } else {
                // Portrait Handheld Deck: ergonomic side-by-side L1 + L2 and R2 + R1 along the shoulder rail
                val bw = padHalfW * 0.82f
                val bh = padHalfH * 0.86f
                buttons.add(
                    VirtualButton(
                        key = "BTN_L1", group = ControlGroup.SHOULDERS, libretroId = RETRO_DEVICE_ID_JOYPAD_L,
                        label = "L1", shape = ShapeKind.BUMPER_LEFT,
                        cx = w * 0.13f, cy = shCy, capsuleHalfW = bw, capsuleHalfH = bh
                    )
                )
                buttons.add(
                    VirtualButton(
                        key = "BTN_L2", group = ControlGroup.SHOULDERS, libretroId = RETRO_DEVICE_ID_JOYPAD_L2,
                        label = "L2", shape = ShapeKind.TRIGGER_LEFT,
                        cx = w * 0.34f, cy = shCy, capsuleHalfW = bw * 0.92f, capsuleHalfH = bh
                    )
                )
                buttons.add(
                    VirtualButton(
                        key = "BTN_R2", group = ControlGroup.SHOULDERS, libretroId = RETRO_DEVICE_ID_JOYPAD_R2,
                        label = "R2", shape = ShapeKind.TRIGGER_RIGHT,
                        cx = w * 0.66f, cy = shCy, capsuleHalfW = bw * 0.92f, capsuleHalfH = bh
                    )
                )
                buttons.add(
                    VirtualButton(
                        key = "BTN_R1", group = ControlGroup.SHOULDERS, libretroId = RETRO_DEVICE_ID_JOYPAD_R,
                        label = "R1", shape = ShapeKind.BUMPER_RIGHT,
                        cx = w * 0.87f, cy = shCy, capsuleHalfW = bw, capsuleHalfH = bh
                    )
                )
            }
        }
        computeGroupBoundFromButtons(ControlGroup.SHOULDERS)
    }

    private fun computeGroupBoundFromButtons(group: ControlGroup) {
        var minX = Float.MAX_VALUE
        var minY = Float.MAX_VALUE
        var maxX = -Float.MAX_VALUE
        var maxY = -Float.MAX_VALUE
        var found = false
        for (b in buttons) {
            if (b.group != group) continue
            found = true
            val hw = if (b.isCapsule) b.capsuleHalfW else b.radius
            val hh = if (b.isCapsule) b.capsuleHalfH else b.radius
            minX = min(minX, b.cx - hw)
            minY = min(minY, b.cy - hh)
            maxX = max(maxX, b.cx + hw)
            maxY = max(maxY, b.cy + hh)
        }
        if (found) {
            val pad = 8f * density
            groupBounds[group] = RectF(minX - pad, minY - pad, maxX + pad, maxY + pad)
        }
    }

    private fun stepPhysicsAndAnimations(): Boolean {
        var needsNextFrame = false
        // Smooth button depression & rebound physics
        for (b in buttons) {
            val target = if (isButtonVisuallyPressed(b)) 1f else 0f
            val cur = b.pressProgress
            if (abs(cur - target) > 0.01f) {
                b.pressProgress = if (target > cur) {
                    min(1f, cur + 0.44f)
                } else {
                    max(0f, cur - 0.28f)
                }
                if (abs(b.pressProgress - target) > 0.01f) needsNextFrame = true
            } else {
                b.pressProgress = target
            }
        }

        // Smooth spring-back on released analog sticks & arcade stick
        if (leftStickPointerId == MotionEvent.INVALID_POINTER_ID && (abs(leftKnobDx) > 0.35f || abs(leftKnobDy) > 0.35f)) {
            leftKnobDx *= 0.66f
            leftKnobDy *= 0.66f
            if (abs(leftKnobDx) <= 0.35f) leftKnobDx = 0f
            if (abs(leftKnobDy) <= 0.35f) leftKnobDy = 0f
            needsNextFrame = true
        }
        if (rightStickPointerId == MotionEvent.INVALID_POINTER_ID && (abs(rightKnobDx) > 0.35f || abs(rightKnobDy) > 0.35f)) {
            rightKnobDx *= 0.66f
            rightKnobDy *= 0.66f
            if (abs(rightKnobDx) <= 0.35f) rightKnobDx = 0f
            if (abs(rightKnobDy) <= 0.35f) rightKnobDy = 0f
            needsNextFrame = true
        }
        if (dpadPointerId == MotionEvent.INVALID_POINTER_ID && (abs(arcadeKnobDx) > 0.35f || abs(arcadeKnobDy) > 0.35f)) {
            arcadeKnobDx *= 0.66f
            arcadeKnobDy *= 0.66f
            if (abs(arcadeKnobDx) <= 0.35f) arcadeKnobDx = 0f
            if (abs(arcadeKnobDy) <= 0.35f) arcadeKnobDy = 0f
            needsNextFrame = true
        }

        return needsNextFrame
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        if (width <= 0 || height <= 0) return

        val animating = stepPhysicsAndAnimations()
        val effectiveAlpha = if (isEditMode) 0.96f else (overlayAlpha * visibilityFactor)

        // Always draw discrete MENU pill even when Hidden preset or physical controller is active
        val menuBtn = buttons.firstOrNull { it.key == "UTIL_MENU" }
        if (layoutProfile == LayoutProfile.HIDDEN) {
            if (menuBtn != null) {
                drawSculptedCapsuleButton(canvas, menuBtn, 0.56f)
            }
            if (animating) postInvalidateOnAnimation()
            return
        }

        // 1. Portrait Handheld Console Skin Deck (Brushed-Carbon + STORM Aurora Glass)
        if (!isLandscape() && effectiveAlpha > 0.02f) {
            drawPortraitHandheldDeck(canvas, effectiveAlpha)
        }

        if (menuBtn != null) {
            val menuAlpha = if (physicalControllerActive && !isEditMode) 0.44f else max(0.50f, effectiveAlpha)
            drawSculptedCapsuleButton(canvas, menuBtn, menuAlpha)
        }

        if (effectiveAlpha <= 0.015f && !isEditMode) {
            if (animating) postInvalidateOnAnimation()
            return
        }

        // 2. Utility System Pills (REW, FAST 2X, TURBO)
        for (b in buttons) {
            if (b.key == "UTIL_MENU") continue
            if (b.isUtility) {
                drawSculptedCapsuleButton(canvas, b, effectiveAlpha * 0.94f)
            }
        }

        // 3. Sculpted Concave 8-Way Cross D-Pad or 3D Arcade Stick
        if (layoutProfile == LayoutProfile.ARCADE_6BTN) {
            drawTexturedAnalogStick(
                canvas, dpadCx, dpadCy, dpadRadius * 0.96f,
                arcadeKnobDx, arcadeKnobDy, "8-WAY",
                effectiveAlpha, dpadPointerId != MotionEvent.INVALID_POINTER_ID, isArcadeBallTop = true
            )
        } else {
            drawSculptedDpad(canvas, effectiveAlpha)
        }

        // 4. Textured Concentric-Ring Dual Analog Thumbsticks
        if (hasAnalogSticks) {
            val lTravel = leftStickRadius * 0.62f
            val lDx = if (leftStickPointerId != MotionEvent.INVALID_POINTER_ID || abs(leftKnobDx) > 0.4f) leftKnobDx else gyroLeftNormX * lTravel
            val lDy = if (leftStickPointerId != MotionEvent.INVALID_POINTER_ID || abs(leftKnobDy) > 0.4f) leftKnobDy else gyroLeftNormY * lTravel
            val lActive = leftStickPointerId != MotionEvent.INVALID_POINTER_ID || hypot(lDx, lDy) > 2f * density

            val rTravel = rightStickRadius * 0.62f
            val rDx = if (rightStickPointerId != MotionEvent.INVALID_POINTER_ID || abs(rightKnobDx) > 0.4f) rightKnobDx else gyroRightNormX * rTravel
            val rDy = if (rightStickPointerId != MotionEvent.INVALID_POINTER_ID || abs(rightKnobDy) > 0.4f) rightKnobDy else gyroRightNormY * rTravel
            val rActive = rightStickPointerId != MotionEvent.INVALID_POINTER_ID || hypot(rDx, rDy) > 2f * density

            drawTexturedAnalogStick(canvas, leftStickCx, leftStickCy, leftStickRadius, lDx, lDy, "L", effectiveAlpha, lActive, isArcadeBallTop = false)
            drawTexturedAnalogStick(canvas, rightStickCx, rightStickCy, rightStickRadius, rDx, rDy, "R", effectiveAlpha, rActive, isArcadeBallTop = false)
        }

        // 5. 3D Volumetric Action Face Buttons, Shoulders & Start/Select
        for (b in buttons) {
            if (b.isUtility) continue
            if (b.isCapsule) {
                drawSculptedCapsuleButton(canvas, b, effectiveAlpha)
            } else {
                drawVolumetricActionButton(canvas, b, effectiveAlpha)
            }
        }

        // 6. Interactive Layout Editor HUD
        if (isEditMode) {
            drawEditorOverlay(canvas)
        }

        if (animating) {
            postInvalidateOnAnimation()
        }
    }

    private fun drawPortraitHandheldDeck(canvas: Canvas, alphaFactor: Float) {
        val deckAlpha = (max(0.84f, alphaFactor) * 255f).roundToInt().coerceIn(0, 255)
        fillPaint.shader = deckPlateShader
        fillPaint.alpha = deckAlpha
        canvas.drawRect(deckRect, fillPaint)
        fillPaint.shader = null
        fillPaint.alpha = 255

        // Subtle diagonal brushed-carbon micro-texture
        canvas.save()
        canvas.clipRect(deckRect)
        strokePaint.shader = null
        strokePaint.strokeWidth = 1f * density
        strokePaint.color = Color.argb((alphaFactor * 14f).roundToInt().coerceIn(0, 28), 255, 255, 255)
        val step = 14f * density
        var xLine = -deckRect.height()
        while (xLine < deckRect.width() + deckRect.height()) {
            canvas.drawLine(xLine, deckRect.top, xLine + deckRect.height() * 0.55f, deckRect.bottom, strokePaint)
            xLine += step
        }

        // Recessed controller grips (left & right subtle ergonomic pods)
        val podTop = deckRect.top + 38f * density
        val podBot = deckRect.bottom - 10f * density
        fillPaint.color = Color.argb((alphaFactor * 42f).roundToInt().coerceIn(0, 80), 4, 3, 10)
        tempRect.set(8f * density, podTop, deckRect.width() * 0.46f, podBot)
        canvas.drawRoundRect(tempRect, 28f * density, 28f * density, fillPaint)
        tempRect.set(deckRect.width() * 0.54f, podTop, deckRect.width() - 8f * density, podBot)
        canvas.drawRoundRect(tempRect, 28f * density, 28f * density, fillPaint)

        // Subtle center speaker grille slots & engraved STORM badge
        val midX = deckRect.centerX()
        val grilleY = deckRect.top + (deckRect.height() * 0.54f)
        fillPaint.color = Color.argb((alphaFactor * 115f).roundToInt().coerceIn(0, 180), 5, 4, 12)
        for (i in -2..2) {
            val gx = midX + i * 7.5f * density
            tempRect.set(gx - 1.6f * density, grilleY - 7f * density, gx + 1.6f * density, grilleY + 7f * density)
            canvas.drawRoundRect(tempRect, 2f * density, 2f * density, fillPaint)
        }

        subTextPaint.textSize = 8.5f * density
        subTextPaint.letterSpacing = 0.16f
        subTextPaint.color = Color.argb((alphaFactor * 105f).roundToInt().coerceIn(0, 165), 167, 139, 250)
        canvas.drawText("STORM DECK", midX, grilleY + 21f * density, subTextPaint)
        subTextPaint.letterSpacing = 0f
        canvas.restore()

        // Glowing STORM Aurora Fluid top lip seam separating display from controller deck
        strokePaint.shader = deckLipGlowShader
        strokePaint.strokeWidth = 2.2f * density
        canvas.drawPath(deckHeaderLipPath, strokePaint)
        strokePaint.shader = null
    }

    private fun isButtonVisuallyPressed(b: VirtualButton): Boolean {
        if (b.key == "UTIL_FF" && isFastForwardActive) return true
        if (b.key == "UTIL_REWIND" && rewindHeld) return true
        if (b.key == "UTIL_TURBO" && isTurboLatchActive) return true
        for (set in pointerButtons.values) {
            if (set.contains(b.key)) return true
        }
        return false
    }

    private fun resolveFaceRoleColor(role: FaceRole): Int {
        return when (buttonColorTheme) {
            ButtonColorTheme.STORM_VIOLET -> when (role) {
                FaceRole.SOUTH_B -> 0xFF8B5CF6.toInt() // Violet
                FaceRole.EAST_A -> 0xFF06B6D4.toInt()  // Cyan
                FaceRole.WEST_Y -> 0xFF10B981.toInt()  // Emerald
                FaceRole.NORTH_X -> 0xFFF43F5E.toInt() // Rose
                FaceRole.UPPER_C -> 0xFFF59E0B.toInt() // Amber
                FaceRole.UPPER_Z -> 0xFF6366F1.toInt() // Indigo
                FaceRole.NONE -> accentColor
            }
            ButtonColorTheme.PLAYSTATION -> when (role) {
                FaceRole.SOUTH_B -> 0xFF3B82F6.toInt() // Cross Blue
                FaceRole.EAST_A -> 0xFFEF4444.toInt()  // Circle Red
                FaceRole.WEST_Y -> 0xFFEC4899.toInt()  // Square Pink
                FaceRole.NORTH_X -> 0xFF10B981.toInt() // Triangle Green
                FaceRole.UPPER_C -> 0xFF06B6D4.toInt()
                FaceRole.UPPER_Z -> 0xFF8B5CF6.toInt()
                FaceRole.NONE -> accentColor
            }
            ButtonColorTheme.SNES -> when (role) {
                FaceRole.SOUTH_B -> 0xFFEAB308.toInt() // B Yellow
                FaceRole.EAST_A -> 0xFFEF4444.toInt()  // A Red
                FaceRole.WEST_Y -> 0xFF22C55E.toInt()  // Y Green
                FaceRole.NORTH_X -> 0xFF3B82F6.toInt() // X Blue
                FaceRole.UPPER_C -> 0xFF14B8A6.toInt()
                FaceRole.UPPER_Z -> 0xFF8B5CF6.toInt()
                FaceRole.NONE -> accentColor
            }
            ButtonColorTheme.XBOX -> when (role) {
                // On 4-button diamond in Xbox mode: South="A"(#22C55E), East="B"(#EF4444), West="X"(#3B82F6), North="Y"(#F59E0B)
                FaceRole.SOUTH_B -> 0xFF22C55E.toInt()
                FaceRole.EAST_A -> 0xFFEF4444.toInt()
                FaceRole.WEST_Y -> 0xFF3B82F6.toInt()
                FaceRole.NORTH_X -> 0xFFF59E0B.toInt()
                FaceRole.UPPER_C -> 0xFF06B6D4.toInt()
                FaceRole.UPPER_Z -> 0xFF8B5CF6.toInt()
                FaceRole.NONE -> accentColor
            }
            ButtonColorTheme.MINIMAL_GLASS -> 0xFF221F36.toInt()
        }
    }

    private fun getOrBuildCapShaders(baseColor: Int, isGlassTheme: Boolean): CapShaderEntry {
        val cacheKey = (baseColor and 0x00FFFFFF) or (if (isGlassTheme) 0x01000000 else 0)
        capShaderCache[cacheKey]?.let { return it }

        val dome = if (isGlassTheme) {
            RadialGradient(
                -0.28f, -0.32f, 1.32f,
                intArrayOf(
                    0xEE4A446E.toInt(),
                    0xE626223C.toInt(),
                    0xEC151225.toInt(),
                    0xF40B0915.toInt()
                ),
                floatArrayOf(0f, 0.36f, 0.78f, 1f),
                Shader.TileMode.CLAMP
            )
        } else {
            RadialGradient(
                -0.28f, -0.32f, 1.32f,
                intArrayOf(
                    lightenColor(baseColor, 0.56f, 252),
                    withAlpha(baseColor, 248),
                    darkenColor(baseColor, 0.34f, 250),
                    darkenColor(baseColor, 0.58f, 255)
                ),
                floatArrayOf(0f, 0.38f, 0.80f, 1f),
                Shader.TileMode.CLAMP
            )
        }

        val glowTint = if (isGlassTheme) accentColor else baseColor
        val halo = RadialGradient(
            0f, 0f, 1f,
            intArrayOf(
                lightenColor(glowTint, 0.32f, 200),
                withAlpha(glowTint, 105),
                withAlpha(glowTint, 0)
            ),
            floatArrayOf(0f, 0.62f, 1f),
            Shader.TileMode.CLAMP
        )

        val entry = CapShaderEntry(dome, halo)
        capShaderCache[cacheKey] = entry
        return entry
    }

    private fun drawVolumetricActionButton(canvas: Canvas, b: VirtualButton, alphaFactor: Float) {
        val p = b.pressProgress
        val isGlass = buttonColorTheme == ButtonColorTheme.MINIMAL_GLASS
        val roleColor = resolveFaceRoleColor(b.faceRole)
        val glowColor = if (isGlass) accentColor else roleColor
        val shaders = getOrBuildCapShaders(roleColor, isGlass)

        // 1. Stationary Sunken Bezel Socket Ring (stays anchored while cap depresses!)
        val socketR = b.radius * 1.17f
        shaderMatrix.reset()
        shaderMatrix.setScale(socketR, socketR)
        shaderMatrix.postTranslate(b.cx, b.cy)
        unitSocketWellShader.setLocalMatrix(shaderMatrix)
        fillPaint.shader = unitSocketWellShader
        fillPaint.alpha = (alphaFactor * 230f).roundToInt().coerceIn(0, 255)
        canvas.drawCircle(b.cx, b.cy, socketR, fillPaint)
        fillPaint.shader = null
        fillPaint.alpha = 255

        // Socket top-left recessed shadow + bottom-right ambient light catch
        arcRect.set(b.cx - socketR, b.cy - socketR, b.cx + socketR, b.cy + socketR)
        specularArcPaint.strokeWidth = 1.3f * density
        specularArcPaint.color = Color.argb((alphaFactor * 155f).roundToInt().coerceIn(0, 255), 3, 2, 8)
        canvas.drawArc(arcRect, 195f, 140f, false, specularArcPaint)
        specularArcPaint.color = Color.argb((alphaFactor * 78f).roundToInt().coerceIn(0, 255), 203, 213, 225)
        canvas.drawArc(arcRect, 20f, 135f, false, specularArcPaint)

        // 2. Active Neon Halo when pressed or Turbo-pulsed
        val turboFlicker = isTurboLatchActive && b.supportsTurboLatch && p > 0.2f && turboPhase
        val haloStrength = if (turboFlicker) 1f else p
        if (haloStrength > 0.04f) {
            val haloR = b.radius * (1.50f + 0.08f * haloStrength)
            shaderMatrix.reset()
            shaderMatrix.setScale(haloR, haloR)
            shaderMatrix.postTranslate(b.cx, b.cy)
            shaders.haloShader.setLocalMatrix(shaderMatrix)
            fillPaint.shader = shaders.haloShader
            fillPaint.alpha = (alphaFactor * haloStrength * 245f).roundToInt().coerceIn(0, 255)
            canvas.drawCircle(b.cx, b.cy, haloR, fillPaint)
            fillPaint.shader = null
            fillPaint.alpha = 255
        }

        // 3. Dynamic Press Physics: translateY +2.5dp, scale 0.93f
        val pressOffset = 2.5f * density * p
        val scaleAdj = 1.0f - 0.07f * p
        val r = b.radius * scaleAdj
        val cy = b.cy + pressOffset

        // 4. 3D Extruded Cylindrical Side Skirt & Drop Shadow
        val skirtDepth = (3.1f * (1f - 0.74f * p)) * density
        fillPaint.color = Color.argb((alphaFactor * 145f).roundToInt().coerceIn(0, 255), 4, 3, 10)
        canvas.drawCircle(b.cx, cy + skirtDepth + 1.2f * density, r * 1.02f, fillPaint)

        fillPaint.color = if (isGlass) {
            Color.argb((alphaFactor * 235f).roundToInt().coerceIn(0, 255), 14, 12, 26)
        } else {
            darkenColor(roleColor, 0.56f, (alphaFactor * 245f).roundToInt())
        }
        canvas.drawCircle(b.cx, cy + skirtDepth, r, fillPaint)

        // 5. Raised 3D Convex Polycarbonate / Glass Button Cap
        shaderMatrix.reset()
        shaderMatrix.setScale(r, r)
        shaderMatrix.postTranslate(b.cx, cy)
        shaders.domeShader.setLocalMatrix(shaderMatrix)
        fillPaint.shader = shaders.domeShader
        fillPaint.alpha = (alphaFactor * (if (p > 0.3f) 252f else 238f)).roundToInt().coerceIn(0, 255)
        canvas.drawCircle(b.cx, cy, r, fillPaint)
        fillPaint.shader = null
        fillPaint.alpha = 255

        // 6. Crisp Top-Left Specular Crescent Arc (200 deg to 330 deg) + Bottom Bevel Shadow
        val specInset = r * 0.83f
        arcRect.set(b.cx - specInset, cy - specInset, b.cx + specInset, cy + specInset)
        specularArcPaint.strokeWidth = max(1.4f * density, r * 0.075f)
        specularArcPaint.color = Color.argb(
            (alphaFactor * (if (p > 0.5f) 195f else 155f)).roundToInt().coerceIn(0, 255),
            255, 255, 255
        )
        canvas.drawArc(arcRect, 200f, 130f, false, specularArcPaint)

        // Bottom inset bevel shadow
        val botInset = r * 0.89f
        arcRect.set(b.cx - botInset, cy - botInset, b.cx + botInset, cy + botInset)
        specularArcPaint.strokeWidth = max(1.3f * density, r * 0.065f)
        specularArcPaint.color = Color.argb((alphaFactor * 135f).roundToInt().coerceIn(0, 255), 5, 4, 14)
        canvas.drawArc(arcRect, 25f, 130f, false, specularArcPaint)

        // Outer glass edge rim
        strokePaint.strokeWidth = (if (p > 0.4f) 1.9f else 1.2f) * density
        strokePaint.color = if (p > 0.4f) {
            lightenColor(glowColor, 0.42f, (alphaFactor * 250f).roundToInt())
        } else if (isGlass) {
            withAlpha(accentColor, (alphaFactor * 155f).roundToInt())
        } else {
            lightenColor(roleColor, 0.25f, (alphaFactor * 150f).roundToInt())
        }
        canvas.drawCircle(b.cx, cy, r, strokePaint)

        // 7. Crisp Sub-Surface Legend / PlayStation Geometric Symbols
        drawButtonSymbolOrLabel(canvas, b, b.cx, cy, r, alphaFactor, p > 0.35f)
    }

    private fun buildContouredCapsulePath(
        shape: ShapeKind,
        left: Float,
        top: Float,
        right: Float,
        bottom: Float,
        outPath: Path
    ) {
        outPath.reset()
        val h = bottom - top
        val fullR = h * 0.5f
        val tightR = h * 0.24f
        val flareR = h * 0.68f
        tempRect.set(left, top, right, bottom)
        when (shape) {
            ShapeKind.BUMPER_LEFT -> {
                // Ergonomic outer left curve, crisp inner shoulder edge
                cornerRadii[0] = flareR; cornerRadii[1] = flareR
                cornerRadii[2] = tightR; cornerRadii[3] = tightR
                cornerRadii[4] = tightR; cornerRadii[5] = tightR
                cornerRadii[6] = fullR * 0.7f; cornerRadii[7] = fullR * 0.7f
                outPath.addRoundRect(tempRect, cornerRadii, Path.Direction.CW)
            }
            ShapeKind.BUMPER_RIGHT -> {
                cornerRadii[0] = tightR; cornerRadii[1] = tightR
                cornerRadii[2] = flareR; cornerRadii[3] = flareR
                cornerRadii[4] = fullR * 0.7f; cornerRadii[5] = fullR * 0.7f
                cornerRadii[6] = tightR; cornerRadii[7] = tightR
                outPath.addRoundRect(tempRect, cornerRadii, Path.Direction.CW)
            }
            ShapeKind.TRIGGER_LEFT -> {
                cornerRadii[0] = flareR; cornerRadii[1] = flareR
                cornerRadii[2] = fullR * 0.42f; cornerRadii[3] = fullR * 0.42f
                cornerRadii[4] = tightR; cornerRadii[5] = tightR
                cornerRadii[6] = flareR * 0.8f; cornerRadii[7] = flareR * 0.8f
                outPath.addRoundRect(tempRect, cornerRadii, Path.Direction.CW)
            }
            ShapeKind.TRIGGER_RIGHT -> {
                cornerRadii[0] = fullR * 0.42f; cornerRadii[1] = fullR * 0.42f
                cornerRadii[2] = flareR; cornerRadii[3] = flareR
                cornerRadii[4] = flareR * 0.8f; cornerRadii[5] = flareR * 0.8f
                cornerRadii[6] = tightR; cornerRadii[7] = tightR
                outPath.addRoundRect(tempRect, cornerRadii, Path.Direction.CW)
            }
            else -> {
                outPath.addRoundRect(tempRect, fullR, fullR, Path.Direction.CW)
            }
        }
    }

    private fun drawSculptedCapsuleButton(canvas: Canvas, b: VirtualButton, alphaFactor: Float) {
        val p = b.pressProgress
        val latched = (b.key == "UTIL_FF" && isFastForwardActive) ||
            (b.key == "UTIL_TURBO" && isTurboLatchActive) ||
            (b.key == "UTIL_REWIND" && rewindHeld)
        val activeGlowColor = when (b.key) {
            "UTIL_TURBO" -> 0xFF10B981.toInt() // Emerald rapid-fire LED
            "UTIL_FF" -> 0xFF06B6D4.toInt()    // Cyan fast-forward LED
            else -> accentColor
        }

        val scaleAdj = 1.0f - 0.05f * p
        val hw = b.capsuleHalfW * scaleAdj
        val hh = b.capsuleHalfH * scaleAdj
        val cy = b.cy + 2.0f * density * p

        // 1. Sunken Bezel Socket
        val sockPad = 2.4f * density
        buildContouredCapsulePath(b.shape, b.cx - b.capsuleHalfW - sockPad, b.cy - b.capsuleHalfH - sockPad, b.cx + b.capsuleHalfW + sockPad, b.cy + b.capsuleHalfH + sockPad, shapePath)
        fillPaint.shader = null
        fillPaint.color = Color.argb((alphaFactor * 165f).roundToInt().coerceIn(0, 255), 6, 5, 14)
        canvas.drawPath(shapePath, fillPaint)

        // 2. Active Latch / Press Halo
        if (latched || p > 0.1f) {
            strokePaint.strokeWidth = 3.2f * density
            strokePaint.color = withAlpha(activeGlowColor, (alphaFactor * (if (latched) 150f else 120f * p)).roundToInt())
            canvas.drawPath(shapePath, strokePaint)
        }

        // 3. Lower 3D Skirt Extrusion
        val skirtOff = (2.2f * (1f - 0.7f * p)) * density
        buildContouredCapsulePath(b.shape, b.cx - hw, cy - hh + skirtOff, b.cx + hw, cy + hh + skirtOff, shapePath)
        fillPaint.color = Color.argb((alphaFactor * 230f).roundToInt().coerceIn(0, 255), 11, 9, 22)
        canvas.drawPath(shapePath, fillPaint)

        // 4. Sculpted 3D Pill / Bumper Body
        buildContouredCapsulePath(b.shape, b.cx - hw, cy - hh, b.cx + hw, cy + hh, shapePath)
        val topCol = if (latched || p > 0.4f) {
            lightenColor(activeGlowColor, 0.18f, (alphaFactor * 242f).roundToInt())
        } else {
            Color.argb((alphaFactor * 228f).roundToInt().coerceIn(0, 255), 54, 47, 86)
        }
        val botCol = if (latched || p > 0.4f) {
            darkenColor(activeGlowColor, 0.30f, (alphaFactor * 245f).roundToInt())
        } else {
            Color.argb((alphaFactor * 235f).roundToInt().coerceIn(0, 255), 22, 18, 40)
        }
        fillPaint.shader = LinearGradient(b.cx, cy - hh, b.cx, cy + hh, topCol, botCol, Shader.TileMode.CLAMP)
        canvas.drawPath(shapePath, fillPaint)
        fillPaint.shader = null

        // 5. Top Specular Highlight Edge + Outer Rim
        specularArcPaint.strokeWidth = 1.2f * density
        specularArcPaint.color = Color.argb((alphaFactor * 135f).roundToInt().coerceIn(0, 255), 255, 255, 255)
        canvas.drawLine(b.cx - hw * 0.62f, cy - hh + 1.6f * density, b.cx + hw * 0.62f, cy - hh + 1.6f * density, specularArcPaint)

        strokePaint.strokeWidth = (if (latched || p > 0.4f) 1.8f else 1.2f) * density
        strokePaint.color = if (latched || p > 0.4f) {
            lightenColor(activeGlowColor, 0.38f, (alphaFactor * 245f).roundToInt())
        } else {
            withAlpha(accentColor, (alphaFactor * 145f).roundToInt())
        }
        canvas.drawPath(shapePath, strokePaint)

        drawButtonSymbolOrLabel(canvas, b, b.cx, cy, min(hw, hh), alphaFactor, latched || p > 0.35f)
    }

    private fun drawSculptedDpad(canvas: Canvas, alphaFactor: Float) {
        val arm = dpadRadius * 0.34f
        val r = dpadRadius
        val dishR = r * 1.12f

        // 1. Circular Recessed Dish Plate behind Cross D-Pad
        shaderMatrix.reset()
        shaderMatrix.setScale(dishR, dishR)
        shaderMatrix.postTranslate(dpadCx, dpadCy)
        unitSocketWellShader.setLocalMatrix(shaderMatrix)
        fillPaint.shader = unitSocketWellShader
        fillPaint.alpha = (alphaFactor * 225f).roundToInt().coerceIn(0, 255)
        canvas.drawCircle(dpadCx, dpadCy, dishR, fillPaint)
        fillPaint.shader = null
        fillPaint.alpha = 255

        // Dish outer machined bezel + 4 subtle diagonal 8-way ticks
        arcRect.set(dpadCx - dishR, dpadCy - dishR, dpadCx + dishR, dpadCy + dishR)
        specularArcPaint.strokeWidth = 1.4f * density
        specularArcPaint.color = Color.argb((alphaFactor * 150f).roundToInt().coerceIn(0, 255), 4, 3, 10)
        canvas.drawArc(arcRect, 195f, 140f, false, specularArcPaint)
        specularArcPaint.color = Color.argb((alphaFactor * 82f).roundToInt().coerceIn(0, 255), 186, 198, 218)
        canvas.drawArc(arcRect, 20f, 135f, false, specularArcPaint)

        strokePaint.strokeWidth = 1.4f * density
        strokePaint.color = withAlpha(accentColor, (alphaFactor * 95f).roundToInt())
        for (deg in intArrayOf(45, 135, 225, 315)) {
            val rad = (deg * PI / 180.0).toFloat()
            val c = cos(rad)
            val s = sin(rad)
            canvas.drawLine(
                dpadCx + c * (dishR * 0.72f), dpadCy + s * (dishR * 0.72f),
                dpadCx + c * (dishR * 0.88f), dpadCy + s * (dishR * 0.88f),
                strokePaint
            )
        }

        // 2. Active Tilt Offset & 3D Extruded Cross Skirt
        val upOn = digitalState[RETRO_DEVICE_ID_JOYPAD_UP]
        val downOn = digitalState[RETRO_DEVICE_ID_JOYPAD_DOWN]
        val leftOn = digitalState[RETRO_DEVICE_ID_JOYPAD_LEFT]
        val rightOn = digitalState[RETRO_DEVICE_ID_JOYPAD_RIGHT]
        val tiltX = ((if (rightOn) 1f else 0f) - (if (leftOn) 1f else 0f)) * 1.4f * density
        val tiltY = ((if (downOn) 1f else 0f) - (if (upOn) 1f else 0f)) * 1.4f * density

        fillPaint.color = Color.argb((alphaFactor * 235f).roundToInt().coerceIn(0, 255), 9, 8, 18)
        canvas.drawPath(dpadSkirtPath, fillPaint)

        // 3. Sculpted Chamfered 3D Cross Body
        canvas.save()
        canvas.translate(tiltX, tiltY)
        fillPaint.shader = LinearGradient(
            dpadCx - r * 0.5f, dpadCy - r,
            dpadCx + r * 0.5f, dpadCy + r,
            intArrayOf(
                Color.argb((alphaFactor * 242f).roundToInt().coerceIn(0, 255), 62, 55, 96),
                Color.argb((alphaFactor * 242f).roundToInt().coerceIn(0, 255), 34, 29, 58),
                Color.argb((alphaFactor * 248f).roundToInt().coerceIn(0, 255), 18, 15, 34)
            ),
            floatArrayOf(0f, 0.52f, 1f),
            Shader.TileMode.CLAMP
        )
        canvas.drawPath(dpadCrossPath, fillPaint)
        fillPaint.shader = null

        // Chamfered outer rim
        strokePaint.strokeWidth = 1.6f * density
        strokePaint.color = withAlpha(accentColor, (alphaFactor * 165f).roundToInt())
        canvas.drawPath(dpadCrossPath, strokePaint)

        // Top arm specular lip
        specularArcPaint.strokeWidth = 1.3f * density
        specularArcPaint.color = Color.argb((alphaFactor * 130f).roundToInt().coerceIn(0, 255), 255, 255, 255)
        canvas.drawLine(dpadCx - arm * 0.65f, dpadCy - r + 2f * density, dpadCx + arm * 0.65f, dpadCy - r + 2f * density, specularArcPaint)

        // 4. Glowing Directional Chevrons (UP, DOWN, LEFT, RIGHT)
        drawDpadChevron(canvas, dpadCx, dpadCy - r * 0.63f, arm * 0.52f, 0, upOn, alphaFactor)
        drawDpadChevron(canvas, dpadCx + r * 0.63f, dpadCy, arm * 0.52f, 1, rightOn, alphaFactor)
        drawDpadChevron(canvas, dpadCx, dpadCy + r * 0.63f, arm * 0.52f, 2, downOn, alphaFactor)
        drawDpadChevron(canvas, dpadCx - r * 0.63f, dpadCy, arm * 0.52f, 3, leftOn, alphaFactor)

        // 5. Center Concave Pivot Dimple
        val dimpleR = arm * 0.46f
        shaderMatrix.reset()
        shaderMatrix.setScale(dimpleR, dimpleR)
        shaderMatrix.postTranslate(dpadCx, dpadCy)
        unitConcaveDimpleShader.setLocalMatrix(shaderMatrix)
        fillPaint.shader = unitConcaveDimpleShader
        fillPaint.alpha = (alphaFactor * 235f).roundToInt().coerceIn(0, 255)
        canvas.drawCircle(dpadCx, dpadCy, dimpleR, fillPaint)
        fillPaint.shader = null
        fillPaint.alpha = 255

        strokePaint.strokeWidth = 1.0f * density
        strokePaint.color = Color.argb((alphaFactor * 85f).roundToInt().coerceIn(0, 255), 255, 255, 255)
        canvas.drawCircle(dpadCx, dpadCy, dimpleR, strokePaint)
        canvas.restore()
    }

    private fun drawDpadChevron(
        canvas: Canvas,
        cx: Float,
        cy: Float,
        sz: Float,
        dir: Int,
        active: Boolean,
        alphaFactor: Float
    ) {
        if (active) {
            val shaders = getOrBuildCapShaders(accentColor, false)
            val glowR = sz * 1.95f
            shaderMatrix.reset()
            shaderMatrix.setScale(glowR, glowR)
            shaderMatrix.postTranslate(cx, cy)
            shaders.haloShader.setLocalMatrix(shaderMatrix)
            fillPaint.shader = shaders.haloShader
            fillPaint.alpha = (alphaFactor * 245f).roundToInt().coerceIn(0, 255)
            canvas.drawCircle(cx, cy, glowR, fillPaint)
            fillPaint.shader = null
            fillPaint.alpha = 255
        }

        // Crisp sculpted chevron stroke
        arrowPath.reset()
        when (dir) {
            0 -> { // UP
                arrowPath.moveTo(cx - sz * 0.58f, cy + sz * 0.28f)
                arrowPath.lineTo(cx, cy - sz * 0.36f)
                arrowPath.lineTo(cx + sz * 0.58f, cy + sz * 0.28f)
            }
            1 -> { // RIGHT
                arrowPath.moveTo(cx - sz * 0.28f, cy - sz * 0.58f)
                arrowPath.lineTo(cx + sz * 0.36f, cy)
                arrowPath.lineTo(cx - sz * 0.28f, cy + sz * 0.58f)
            }
            2 -> { // DOWN
                arrowPath.moveTo(cx - sz * 0.58f, cy - sz * 0.28f)
                arrowPath.lineTo(cx, cy + sz * 0.36f)
                arrowPath.lineTo(cx + sz * 0.58f, cy - sz * 0.28f)
            }
            3 -> { // LEFT
                arrowPath.moveTo(cx + sz * 0.28f, cy - sz * 0.58f)
                arrowPath.lineTo(cx - sz * 0.36f, cy)
                arrowPath.lineTo(cx + sz * 0.28f, cy + sz * 0.58f)
            }
        }
        symbolPaint.strokeWidth = (if (active) 2.9f else 2.2f) * density
        symbolPaint.color = if (active) {
            Color.argb((alphaFactor * 255f).roundToInt().coerceIn(0, 255), 255, 255, 255)
        } else {
            Color.argb((alphaFactor * 195f).roundToInt().coerceIn(0, 255), 203, 213, 225)
        }
        canvas.drawPath(arrowPath, symbolPaint)
    }

    private fun drawTexturedAnalogStick(
        canvas: Canvas,
        cx: Float,
        cy: Float,
        radius: Float,
        knobDx: Float,
        knobDy: Float,
        subLabel: String,
        alphaFactor: Float,
        engaged: Boolean,
        isArcadeBallTop: Boolean
    ) {
        // 1. Translucent Recessed Outer Boundary Housing
        shaderMatrix.reset()
        shaderMatrix.setScale(radius, radius)
        shaderMatrix.postTranslate(cx, cy)
        unitSocketWellShader.setLocalMatrix(shaderMatrix)
        fillPaint.shader = unitSocketWellShader
        fillPaint.alpha = (alphaFactor * 205f).roundToInt().coerceIn(0, 255)
        canvas.drawCircle(cx, cy, radius, fillPaint)
        fillPaint.shader = null
        fillPaint.alpha = 255

        strokePaint.strokeWidth = (if (engaged) 2.2f else 1.4f) * density
        strokePaint.color = withAlpha(accentColor, (alphaFactor * (if (engaged) 235f else 135f)).roundToInt())
        canvas.drawCircle(cx, cy, radius, strokePaint)

        // 8 Directional Calibration Ticks around Outer Ring
        strokePaint.strokeWidth = 1.3f * density
        strokePaint.color = Color.argb((alphaFactor * 115f).roundToInt().coerceIn(0, 255), 148, 163, 184)
        for (i in 0 until 8) {
            val rad = (i * 45.0 * PI / 180.0).toFloat()
            val c = cos(rad)
            val s = sin(rad)
            val innerF = if (i % 2 == 0) 0.84f else 0.89f
            canvas.drawLine(cx + c * (radius * innerF), cy + s * (radius * innerF), cx + c * (radius * 0.96f), cy + s * (radius * 0.96f), strokePaint)
        }

        // 2. Gimbal Center Dome & Articulated Metallic Shaft
        fillPaint.color = Color.argb((alphaFactor * 195f).roundToInt().coerceIn(0, 255), 24, 21, 44)
        canvas.drawCircle(cx, cy, radius * 0.24f, fillPaint)

        val kx = cx + knobDx
        val ky = cy + knobDy
        symbolPaint.strokeWidth = 5.0f * density
        symbolPaint.color = Color.argb((alphaFactor * 210f).roundToInt().coerceIn(0, 255), 15, 13, 28)
        canvas.drawLine(cx, cy, kx, ky, symbolPaint)
        symbolPaint.strokeWidth = 2.6f * density
        symbolPaint.color = Color.argb((alphaFactor * 185f).roundToInt().coerceIn(0, 255), 148, 163, 184)
        canvas.drawLine(cx, cy, kx, ky, symbolPaint)

        // 3. 3D Rubberized Concentric Thumb Cap (or Glossy Arcade Ball-Top)
        val capR = radius * 0.54f
        fillPaint.color = Color.argb((alphaFactor * 140f).roundToInt().coerceIn(0, 255), 3, 2, 8)
        canvas.drawCircle(kx, ky + 3.0f * density, capR * 1.04f, fillPaint)

        if (isArcadeBallTop) {
            val shaders = getOrBuildCapShaders(accentColor, false)
            shaderMatrix.reset()
            shaderMatrix.setScale(capR, capR)
            shaderMatrix.postTranslate(kx, ky)
            shaders.domeShader.setLocalMatrix(shaderMatrix)
            fillPaint.shader = shaders.domeShader
        } else {
            shaderMatrix.reset()
            shaderMatrix.setScale(capR, capR)
            shaderMatrix.postTranslate(kx, ky)
            unitStickThumbCapShader.setLocalMatrix(shaderMatrix)
            fillPaint.shader = unitStickThumbCapShader
        }
        fillPaint.alpha = (alphaFactor * 248f).roundToInt().coerceIn(0, 255)
        canvas.drawCircle(kx, ky, capR, fillPaint)
        fillPaint.shader = null
        fillPaint.alpha = 255

        // Inner Concave Thumb Grip Dish + Tactile Grip Micro-Dots
        if (!isArcadeBallTop) {
            val dishR = capR * 0.68f
            shaderMatrix.reset()
            shaderMatrix.setScale(dishR, dishR)
            shaderMatrix.postTranslate(kx, ky)
            unitConcaveDimpleShader.setLocalMatrix(shaderMatrix)
            fillPaint.shader = unitConcaveDimpleShader
            fillPaint.alpha = (alphaFactor * 235f).roundToInt().coerceIn(0, 255)
            canvas.drawCircle(kx, ky, dishR, fillPaint)
            fillPaint.shader = null
            fillPaint.alpha = 255

            // Concentric inner ridge
            strokePaint.strokeWidth = 1.2f * density
            strokePaint.color = if (engaged) {
                lightenColor(accentColor, 0.30f, (alphaFactor * 225f).roundToInt())
            } else {
                Color.argb((alphaFactor * 135f).roundToInt().coerceIn(0, 255), 226, 232, 240)
            }
            canvas.drawCircle(kx, ky, dishR, strokePaint)

            // 8 Rubberized Grip Micro-Dots around outer torus
            fillPaint.color = Color.argb((alphaFactor * 155f).roundToInt().coerceIn(0, 255), 203, 213, 225)
            val dotRingR = capR * 0.84f
            val dotR = max(1.1f * density, capR * 0.045f)
            for (i in 0 until 8) {
                val rad = (i * 45.0 * PI / 180.0).toFloat()
                canvas.drawCircle(kx + cos(rad) * dotRingR, ky + sin(rad) * dotRingR, dotR, fillPaint)
            }
        } else {
            // Specular highlight crescent on Arcade Ball-Top
            arcRect.set(kx - capR * 0.80f, ky - capR * 0.80f, kx + capR * 0.80f, ky + capR * 0.80f)
            specularArcPaint.strokeWidth = 1.6f * density
            specularArcPaint.color = Color.argb((alphaFactor * 170f).roundToInt().coerceIn(0, 255), 255, 255, 255)
            canvas.drawArc(arcRect, 200f, 125f, false, specularArcPaint)
        }

        strokePaint.strokeWidth = (if (engaged) 1.8f else 1.2f) * density
        strokePaint.color = if (engaged) {
            lightenColor(accentColor, 0.35f, (alphaFactor * 245f).roundToInt())
        } else {
            withAlpha(accentColor, (alphaFactor * 145f).roundToInt())
        }
        canvas.drawCircle(kx, ky, capR, strokePaint)

        if (!engaged) {
            textPaint.textSize = max(9.5f * density, capR * 0.38f)
            textPaint.color = Color.argb((alphaFactor * 185f).roundToInt().coerceIn(0, 255), 226, 232, 240)
            val fm = textPaint.fontMetrics
            canvas.drawText(subLabel, kx, ky - (fm.ascent + fm.descent) * 0.5f, textPaint)
        }
    }

    private fun drawButtonSymbolOrLabel(
        canvas: Canvas,
        b: VirtualButton,
        cx: Float,
        cy: Float,
        refRadius: Float,
        alphaFactor: Float,
        pressed: Boolean
    ) {
        val fgAlpha = (alphaFactor * 255f).roundToInt().coerceIn(0, 255)
        val shadowAlpha = (alphaFactor * 165f).roundToInt().coerceIn(0, 255)

        when (b.symbol) {
            SymbolKind.CROSS, SymbolKind.CIRCLE, SymbolKind.SQUARE, SymbolKind.TRIANGLE -> {
                val centerShiftY = if (b.subLabel.isNotEmpty()) -refRadius * 0.08f else 0f
                val symCy = cy + centerShiftY
                val sw = max(2.2f * density, refRadius * 0.145f)

                // Dark engraved sub-shadow for crisp contrast on colored 3D dome
                for (pass in 0..1) {
                    val offY = if (pass == 0) 1.1f * density else 0f
                    symbolPaint.strokeWidth = sw
                    symbolPaint.color = if (pass == 0) {
                        Color.argb(shadowAlpha, 6, 4, 14)
                    } else {
                        Color.argb(fgAlpha, 255, 255, 255)
                    }
                    when (b.symbol) {
                        SymbolKind.CROSS -> {
                            val s = refRadius * 0.36f
                            canvas.drawLine(cx - s, symCy - s + offY, cx + s, symCy + s + offY, symbolPaint)
                            canvas.drawLine(cx + s, symCy - s + offY, cx - s, symCy + s + offY, symbolPaint)
                        }
                        SymbolKind.CIRCLE -> {
                            canvas.drawCircle(cx, symCy + offY, refRadius * 0.39f, symbolPaint)
                        }
                        SymbolKind.SQUARE -> {
                            val s = refRadius * 0.35f
                            tempRect.set(cx - s, symCy - s + offY, cx + s, symCy + s + offY)
                            canvas.drawRoundRect(tempRect, 2.2f * density, 2.2f * density, symbolPaint)
                        }
                        SymbolKind.TRIANGLE -> {
                            val s = refRadius * 0.41f
                            arrowPath.reset()
                            arrowPath.moveTo(cx, symCy - s + offY)
                            arrowPath.lineTo(cx - s * 0.92f, symCy + s * 0.72f + offY)
                            arrowPath.lineTo(cx + s * 0.92f, symCy + s * 0.72f + offY)
                            arrowPath.close()
                            canvas.drawPath(arrowPath, symbolPaint)
                        }
                        else -> {}
                    }
                }
                if (b.subLabel.isNotEmpty()) {
                    subTextPaint.textSize = max(7.5f * density, refRadius * 0.28f)
                    subTextPaint.color = Color.argb((fgAlpha * 0.82f).roundToInt(), 241, 245, 249)
                    canvas.drawText(b.subLabel, cx, cy + refRadius * 0.68f, subTextPaint)
                }
            }
            SymbolKind.MENU -> {
                symbolPaint.strokeWidth = 1.7f * density
                symbolPaint.color = Color.argb(fgAlpha, 248, 250, 252)
                val iconX = cx - b.capsuleHalfW * 0.56f
                val wLine = 5.5f * density
                canvas.drawLine(iconX - wLine, cy - 3.6f * density, iconX + wLine, cy - 3.6f * density, symbolPaint)
                canvas.drawLine(iconX - wLine, cy, iconX + wLine, cy, symbolPaint)
                canvas.drawLine(iconX - wLine, cy + 3.6f * density, iconX + wLine, cy + 3.6f * density, symbolPaint)

                textPaint.textSize = 9.8f * density
                textPaint.color = Color.argb(fgAlpha, 248, 250, 252)
                val fm = textPaint.fontMetrics
                canvas.drawText(b.label, cx + 6.5f * density, cy - (fm.ascent + fm.descent) * 0.5f, textPaint)
            }
            SymbolKind.REWIND -> {
                symbolPaint.strokeWidth = 1.6f * density
                symbolPaint.color = if (pressed) Color.WHITE else Color.argb(fgAlpha, 226, 232, 240)
                val iconX = cx - b.capsuleHalfW * 0.52f
                val s = 3.8f * density
                arrowPath.reset()
                arrowPath.moveTo(iconX, cy - s)
                arrowPath.lineTo(iconX - 5f * density, cy)
                arrowPath.lineTo(iconX, cy + s)
                arrowPath.moveTo(iconX + 5f * density, cy - s)
                arrowPath.lineTo(iconX, cy)
                arrowPath.lineTo(iconX + 5f * density, cy + s)
                canvas.drawPath(arrowPath, symbolPaint)

                textPaint.textSize = 9.2f * density
                textPaint.color = symbolPaint.color
                val fm = textPaint.fontMetrics
                canvas.drawText(b.label, cx + 6f * density, cy - (fm.ascent + fm.descent) * 0.5f, textPaint)
            }
            SymbolKind.FAST_FORWARD -> {
                symbolPaint.strokeWidth = 1.6f * density
                symbolPaint.color = if (pressed) Color.WHITE else Color.argb(fgAlpha, 226, 232, 240)
                val iconX = cx - b.capsuleHalfW * 0.62f
                val s = 3.6f * density
                arrowPath.reset()
                arrowPath.moveTo(iconX - 4f * density, cy - s)
                arrowPath.lineTo(iconX + 1f * density, cy)
                arrowPath.lineTo(iconX - 4f * density, cy + s)
                arrowPath.moveTo(iconX + 1f * density, cy - s)
                arrowPath.lineTo(iconX + 6f * density, cy)
                arrowPath.lineTo(iconX + 1f * density, cy + s)
                canvas.drawPath(arrowPath, symbolPaint)

                textPaint.textSize = 8.8f * density
                textPaint.color = symbolPaint.color
                val fm = textPaint.fontMetrics
                canvas.drawText(b.label, cx + 6f * density, cy - (fm.ascent + fm.descent) * 0.5f, textPaint)
            }
            SymbolKind.TURBO -> {
                // Crisp LED indicator dot + TURBO text (zero emojis)
                val ledX = cx - b.capsuleHalfW * 0.62f
                fillPaint.shader = null
                fillPaint.color = if (isTurboLatchActive) {
                    if (turboPhase || !turboRunning) Color.argb(fgAlpha, 52, 211, 153) else Color.argb(fgAlpha, 16, 185, 129)
                } else {
                    Color.argb((fgAlpha * 0.55f).roundToInt(), 148, 163, 184)
                }
                canvas.drawCircle(ledX, cy, 3.1f * density, fillPaint)

                textPaint.textSize = 9.2f * density
                textPaint.color = if (isTurboLatchActive) Color.WHITE else Color.argb(fgAlpha, 226, 232, 240)
                val fm = textPaint.fontMetrics
                canvas.drawText(b.label, cx + 4.5f * density, cy - (fm.ascent + fm.descent) * 0.5f, textPaint)
            }
            SymbolKind.TEXT -> {
                textPaint.textSize = if (b.isCapsule) {
                    max(9.5f * density, refRadius * 0.72f)
                } else {
                    max(11.5f * density, refRadius * (if (b.label.length > 1) 0.54f else 0.74f))
                }
                val fm = textPaint.fontMetrics
                val baseY = cy - (fm.ascent + fm.descent) * 0.5f
                // Engraved shadow for maximum dark/colored dome legibility
                textPaint.color = Color.argb(shadowAlpha, 5, 4, 14)
                canvas.drawText(b.label, cx, baseY + 1.1f * density, textPaint)

                textPaint.color = if (pressed) {
                    Color.argb(fgAlpha, 255, 255, 255)
                } else {
                    Color.argb(fgAlpha, 248, 250, 252)
                }
                canvas.drawText(b.label, cx, baseY, textPaint)
            }
        }
    }

    private fun drawEditorOverlay(canvas: Canvas) {
        for ((group, rect) in groupBounds.entries()) {
            val selected = draggingGroup == group
            editorBoxPaint.color = if (selected) Color.argb(255, 56, 189, 248) else withAlpha(accentColor, 220)
            canvas.drawRoundRect(rect, 12f * density, 12f * density, editorBoxPaint)

            textPaint.textSize = 10f * density
            textPaint.color = Color.WHITE
            canvas.drawText(group.labelRu, rect.centerX(), max(12f * density, rect.top - 4f * density), textPaint)
        }

        // Top floating glassmorphic editor HUD
        editorHudPaint.color = Color.argb(240, 16, 13, 30)
        canvas.drawRoundRect(editorBarRect, 14f * density, 14f * density, editorHudPaint)
        strokePaint.strokeWidth = 1.5f * density
        strokePaint.color = withAlpha(accentColor, 235)
        canvas.drawRoundRect(editorBarRect, 14f * density, 14f * density, strokePaint)

        textPaint.textSize = 11f * density
        textPaint.color = Color.argb(248, 241, 245, 249)
        val row1TextY = editorBarRect.top + 26f * density
        canvas.drawText("Масштаб ${(buttonScale * 100).roundToInt()}%", editorBarRect.left + 48f * density, row1TextY, textPaint)
        drawEditorMiniBtn(canvas, btnScaleDownRect, "-", false)
        drawEditorMiniBtn(canvas, btnScaleUpRect, "+", false)

        canvas.drawText("Прозр. ${(overlayAlpha * 100).roundToInt()}%", editorBarRect.right - 138f * density, row1TextY, textPaint)
        drawEditorMiniBtn(canvas, btnAlphaDownRect, "-", false)
        drawEditorMiniBtn(canvas, btnAlphaUpRect, "+", false)

        drawEditorMiniBtn(canvas, btnResetRect, "Сброс пресета", false)
        drawEditorMiniBtn(canvas, btnDoneRect, "Готово", true)
    }

    private fun drawEditorMiniBtn(canvas: Canvas, rect: RectF, text: String, primary: Boolean) {
        editorHudPaint.color = if (primary) accentColor else Color.argb(225, 44, 36, 74)
        canvas.drawRoundRect(rect, 8f * density, 8f * density, editorHudPaint)
        textPaint.textSize = 11f * density
        textPaint.color = Color.WHITE
        val fm = textPaint.fontMetrics
        canvas.drawText(text, rect.centerX(), rect.centerY() - (fm.ascent + fm.descent) * 0.5f, textPaint)
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        val action = event.actionMasked
        val actionIdx = event.actionIndex
        val pointerId = event.getPointerId(actionIdx)

        // In HIDDEN mode, only intercept taps on the discrete MENU pill; pass all other touches through to SurfaceView!
        if (layoutProfile == LayoutProfile.HIDDEN && !isEditMode) {
            if (action == MotionEvent.ACTION_DOWN) {
                val x = event.getX(actionIdx)
                val y = event.getY(actionIdx)
                val menuBtn = buttons.firstOrNull { it.key == "UTIL_MENU" }
                if (menuBtn != null && hitTestButton(menuBtn, x, y, 1.20f)) {
                    pulseHaptic()
                    callback?.onQuickMenuRequested()
                    return true
                }
            }
            return false
        }

        // Wake overlay smoothly if physical controller had hidden it
        if (physicalControllerActive && !isEditMode && action == MotionEvent.ACTION_DOWN) {
            val x = event.getX(actionIdx)
            val y = event.getY(actionIdx)
            val menuBtn = buttons.firstOrNull { it.key == "UTIL_MENU" }
            if (menuBtn != null && hitTestButton(menuBtn, x, y, 1.18f)) {
                pulseHaptic()
                callback?.onQuickMenuRequested()
                return true
            }
            setPhysicalControllerActive(false)
            return true
        }

        if (isEditMode) {
            pinchDetector.onTouchEvent(event)
            return handleEditModeTouch(event)
        }

        when (action) {
            MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                val x = event.getX(actionIdx)
                val y = event.getY(actionIdx)
                val consumed = handlePointerDown(pointerId, x, y)
                if (!consumed && action == MotionEvent.ACTION_DOWN) {
                    // Consume touches on the Portrait Handheld Deck plate so they don't click the game viewport
                    if (isPortraitDeckVisible() && y >= height * PORTRAIT_DECK_TOP_RATIO) {
                        return true
                    }
                    return false
                }
            }
            MotionEvent.ACTION_MOVE -> {
                for (i in 0 until event.pointerCount) {
                    val pid = event.getPointerId(i)
                    handlePointerMove(pid, event.getX(i), event.getY(i))
                }
            }
            MotionEvent.ACTION_POINTER_UP -> {
                handlePointerUp(pointerId)
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                releaseAllInputs()
            }
        }
        invalidate()
        return true
    }

    private fun handleEditModeTouch(event: MotionEvent): Boolean {
        val action = event.actionMasked
        val idx = event.actionIndex
        val pid = event.getPointerId(idx)
        when (action) {
            MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                if (event.pointerCount > 1) return true
                val x = event.getX(idx)
                val y = event.getY(idx)
                if (btnScaleDownRect.contains(x, y)) {
                    pulseHaptic()
                    setButtonScale(buttonScale - 0.05f)
                    return true
                }
                if (btnScaleUpRect.contains(x, y)) {
                    pulseHaptic()
                    setButtonScale(buttonScale + 0.05f)
                    return true
                }
                if (btnAlphaDownRect.contains(x, y)) {
                    pulseHaptic()
                    setOverlayAlpha(overlayAlpha - 0.05f)
                    return true
                }
                if (btnAlphaUpRect.contains(x, y)) {
                    pulseHaptic()
                    setOverlayAlpha(overlayAlpha + 0.05f)
                    return true
                }
                if (btnResetRect.contains(x, y)) {
                    pulseHaptic()
                    resetCurrentLayout()
                    return true
                }
                if (btnDoneRect.contains(x, y)) {
                    pulseHaptic()
                    isEditMode = false
                    return true
                }

                for ((group, rect) in groupBounds.entries()) {
                    if (rect.contains(x, y)) {
                        draggingGroup = group
                        dragPointerId = pid
                        val pos = resolveGroupPos(group)
                        dragOffsetX = pos.xPct * width - x
                        dragOffsetY = pos.yPct * height - y
                        pulseHaptic()
                        invalidate()
                        return true
                    }
                }
            }
            MotionEvent.ACTION_MOVE -> {
                if (pinchDetector.isInProgress) return true
                val group = draggingGroup
                if (group != null && dragPointerId != MotionEvent.INVALID_POINTER_ID) {
                    val pIndex = event.findPointerIndex(dragPointerId)
                    if (pIndex >= 0 && width > 0 && height > 0) {
                        val minY = if (isLandscape()) 0.08f else PORTRAIT_DECK_TOP_RATIO + 0.04f
                        val nx = ((event.getX(pIndex) + dragOffsetX) / width).coerceIn(0.07f, 0.93f)
                        val ny = ((event.getY(pIndex) + dragOffsetY) / height).coerceIn(minY, 0.95f)
                        customGroupPositions[groupKey(group)] = GroupPosition(nx, ny)
                        rebuildGeometry()
                        invalidate()
                    }
                }
            }
            MotionEvent.ACTION_POINTER_UP -> {
                if (pid == dragPointerId) {
                    draggingGroup = null
                    dragPointerId = MotionEvent.INVALID_POINTER_ID
                    saveLayouts()
                    invalidate()
                }
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                if (draggingGroup != null) {
                    draggingGroup = null
                    dragPointerId = MotionEvent.INVALID_POINTER_ID
                    saveLayouts()
                    invalidate()
                }
            }
        }
        return true
    }

    private fun handlePointerDown(pid: Int, x: Float, y: Float): Boolean {
        // 1. Utility system pills check
        for (b in buttons) {
            if (!b.isUtility) continue
            if (hitTestButton(b, x, y, 1.14f)) {
                pulseHaptic()
                when (b.key) {
                    "UTIL_MENU" -> {
                        callback?.onQuickMenuRequested()
                        return true
                    }
                    "UTIL_FF" -> {
                        callback?.onFastForwardToggle()
                        return true
                    }
                    "UTIL_TURBO" -> {
                        isTurboLatchActive = !isTurboLatchActive
                        return true
                    }
                    "UTIL_REWIND" -> {
                        pointerButtons.getOrPut(pid) { HashSet() }.add(b.key)
                        if (!rewindHeld) {
                            rewindHeld = true
                            callback?.onRewindHold(true)
                        }
                        return true
                    }
                }
            }
        }

        // 2. Analog sticks check
        if (hasAnalogSticks) {
            if (leftStickPointerId == MotionEvent.INVALID_POINTER_ID &&
                hypot(x - leftStickCx, y - leftStickCy) <= leftStickRadius * 1.18f
            ) {
                leftStickPointerId = pid
                pulseHaptic()
                updateAnalogStick(STICK_LEFT, x, y)
                return true
            }
            if (rightStickPointerId == MotionEvent.INVALID_POINTER_ID &&
                hypot(x - rightStickCx, y - rightStickCy) <= rightStickRadius * 1.18f
            ) {
                rightStickPointerId = pid
                pulseHaptic()
                updateAnalogStick(STICK_RIGHT, x, y)
                return true
            }
        }

        // 3. D-Pad / Arcade stick check
        if (dpadPointerId == MotionEvent.INVALID_POINTER_ID &&
            hypot(x - dpadCx, y - dpadCy) <= dpadRadius * 1.18f
        ) {
            dpadPointerId = pid
            updateDpadFromTouch(x, y, triggerHaptics = true)
            return true
        }

        // 4. Console buttons (supports pressing 1 or 2 adjacent face buttons via thumb chord)
        val hits = collectHitButtons(x, y)
        if (hits.isNotEmpty()) {
            pointerButtons[pid] = hits
            recomputeDigitalOutputs(triggerHaptics = true)
            return true
        }
        return false
    }

    private fun handlePointerMove(pid: Int, x: Float, y: Float) {
        if (pid == leftStickPointerId) {
            updateAnalogStick(STICK_LEFT, x, y)
            return
        }
        if (pid == rightStickPointerId) {
            updateAnalogStick(STICK_RIGHT, x, y)
            return
        }
        if (pid == dpadPointerId) {
            if (hypot(x - dpadCx, y - dpadCy) <= dpadRadius * 1.90f) {
                updateDpadFromTouch(x, y, triggerHaptics = true)
            } else {
                dpadPointerId = MotionEvent.INVALID_POINTER_ID
                dpadUpActive = false
                dpadDownActive = false
                dpadLeftActive = false
                dpadRightActive = false
                recomputeDigitalOutputs(triggerHaptics = false)
            }
            return
        }

        val currentSet = pointerButtons[pid]
        if (currentSet != null && currentSet.contains("UTIL_REWIND")) {
            return
        }

        if (dpadPointerId == MotionEvent.INVALID_POINTER_ID &&
            currentSet == null &&
            hypot(x - dpadCx, y - dpadCy) <= dpadRadius * 1.08f
        ) {
            dpadPointerId = pid
            updateDpadFromTouch(x, y, triggerHaptics = true)
            return
        }

        val hits = collectHitButtons(x, y)
        if (hits.isNotEmpty() || currentSet != null) {
            if (hits != currentSet) {
                if (hits.isEmpty()) {
                    pointerButtons.remove(pid)
                } else {
                    pointerButtons[pid] = hits
                }
                recomputeDigitalOutputs(triggerHaptics = true)
            }
        }
    }

    private fun handlePointerUp(pid: Int) {
        if (pid == leftStickPointerId) {
            leftStickPointerId = MotionEvent.INVALID_POINTER_ID
            callback?.onAnalogStick(STICK_LEFT, 0, 0)
        }
        if (pid == rightStickPointerId) {
            rightStickPointerId = MotionEvent.INVALID_POINTER_ID
            callback?.onAnalogStick(STICK_RIGHT, 0, 0)
        }
        if (pid == dpadPointerId) {
            dpadPointerId = MotionEvent.INVALID_POINTER_ID
        }
        val removed = pointerButtons.remove(pid)
        if (removed != null && removed.contains("UTIL_REWIND")) {
            val stillHoldingRewind = pointerButtons.values.any { it.contains("UTIL_REWIND") }
            if (!stillHoldingRewind && rewindHeld) {
                rewindHeld = false
                callback?.onRewindHold(false)
            }
        }
        recomputeDigitalOutputs(triggerHaptics = false)
    }

    private fun updateAnalogStick(stickIndex: Int, touchX: Float, touchY: Float) {
        val cx = if (stickIndex == STICK_LEFT) leftStickCx else rightStickCx
        val cy = if (stickIndex == STICK_LEFT) leftStickCy else rightStickCy
        val maxTravel = (if (stickIndex == STICK_LEFT) leftStickRadius else rightStickRadius) * 0.64f
        if (maxTravel <= 1f) return

        val rawDx = touchX - cx
        val rawDy = touchY - cy
        val dist = hypot(rawDx, rawDy)
        val clampedDist = min(dist, maxTravel)
        val scale = if (dist > 0.001f) clampedDist / dist else 0f
        val kx = rawDx * scale
        val ky = rawDy * scale

        if (stickIndex == STICK_LEFT) {
            leftKnobDx = kx
            leftKnobDy = ky
        } else {
            rightKnobDx = kx
            rightKnobDy = ky
        }

        val normX = kx / maxTravel
        val normY = ky / maxTravel
        val deadzone = 0.09f
        val outX = if (abs(normX) < deadzone) 0 else ((normX.coerceIn(-1f, 1f)) * MAX_AXIS).roundToInt()
        val outY = if (abs(normY) < deadzone) 0 else ((normY.coerceIn(-1f, 1f)) * MAX_AXIS).roundToInt()
        callback?.onAnalogStick(stickIndex, outX, outY)
    }

    private var dpadUpActive = false
    private var dpadDownActive = false
    private var dpadLeftActive = false
    private var dpadRightActive = false

    private fun updateDpadFromTouch(touchX: Float, touchY: Float, triggerHaptics: Boolean) {
        val dx = touchX - dpadCx
        val dy = touchY - dpadCy
        val dist = hypot(dx, dy)
        val maxTravel = dpadRadius * 0.56f
        if (dist > 0.001f) {
            val clamp = min(dist, maxTravel) / dist
            arcadeKnobDx = dx * clamp
            arcadeKnobDy = dy * clamp
        } else {
            arcadeKnobDx = 0f
            arcadeKnobDy = 0f
        }

        // Precision 8-way angular sectors with SOCD impossibility (opposite axes can never trigger together)
        val deadzone = dpadRadius * 0.16f
        if (dist < deadzone) {
            dpadUpActive = false
            dpadDownActive = false
            dpadLeftActive = false
            dpadRightActive = false
        } else {
            val angle = atan2(dy, dx) // -PI..PI, 0=RIGHT, PI/2=DOWN, -PI/2=UP
            val cone = (PI * 0.345).toFloat() // ~62.1 deg half-cone -> clean 55.8 deg pure cardinal + 34.2 deg diagonal zones
            dpadRightActive = abs(angleDiff(angle, 0f)) <= cone
            dpadDownActive = abs(angleDiff(angle, (PI * 0.5).toFloat())) <= cone
            dpadLeftActive = abs(angleDiff(angle, PI.toFloat())) <= cone
            dpadUpActive = abs(angleDiff(angle, (-PI * 0.5).toFloat())) <= cone
            if (dpadLeftActive && dpadRightActive) dpadLeftActive = false
            if (dpadUpActive && dpadDownActive) dpadUpActive = false
        }
        recomputeDigitalOutputs(triggerHaptics)
    }

    private fun angleDiff(a: Float, b: Float): Float {
        var d = a - b
        while (d > PI) d -= (2.0 * PI).toFloat()
        while (d < -PI) d += (2.0 * PI).toFloat()
        return d
    }

    private fun collectHitButtons(x: Float, y: Float): MutableSet<String> {
        val hitKeys = HashSet<String>(2)
        for (b in buttons) {
            if (b.isUtility || b.libretroId < 0) continue
            val tolerance = if (b.group == ControlGroup.FACE_BUTTONS) 1.18f else 1.08f
            if (hitTestButton(b, x, y, tolerance)) {
                hitKeys.add(b.key)
            }
        }
        return hitKeys
    }

    private fun hitTestButton(b: VirtualButton, x: Float, y: Float, scaleTolerance: Float): Boolean {
        return if (b.isCapsule) {
            abs(x - b.cx) <= b.capsuleHalfW * scaleTolerance && abs(y - b.cy) <= b.capsuleHalfH * scaleTolerance
        } else {
            hypot(x - b.cx, y - b.cy) <= b.radius * scaleTolerance
        }
    }

    private fun isRapidFireButton(btn: VirtualButton): Boolean {
        return btn.isTurboDedicated || (isTurboLatchActive && btn.supportsTurboLatch)
    }

    private fun recomputeDigitalOutputs(triggerHaptics: Boolean) {
        val next = BooleanArray(16)
        if (dpadPointerId != MotionEvent.INVALID_POINTER_ID) {
            next[RETRO_DEVICE_ID_JOYPAD_UP] = dpadUpActive
            next[RETRO_DEVICE_ID_JOYPAD_DOWN] = dpadDownActive
            next[RETRO_DEVICE_ID_JOYPAD_LEFT] = dpadLeftActive
            next[RETRO_DEVICE_ID_JOYPAD_RIGHT] = dpadRightActive
        } else {
            dpadUpActive = false
            dpadDownActive = false
            dpadLeftActive = false
            dpadRightActive = false
        }

        var anyTurboHeld = false
        for (heldKeys in pointerButtons.values) {
            for (k in heldKeys) {
                val btn = buttons.firstOrNull { it.key == k } ?: continue
                if (btn.libretroId !in 0..15) continue
                if (isRapidFireButton(btn)) {
                    anyTurboHeld = true
                    if (turboPhase) {
                        next[btn.libretroId] = true
                    }
                } else {
                    next[btn.libretroId] = true
                }
            }
        }

        if (anyTurboHeld && !turboRunning) {
            turboRunning = true
            turboPhase = true
            for (heldKeys in pointerButtons.values) {
                for (k in heldKeys) {
                    val btn = buttons.firstOrNull { it.key == k } ?: continue
                    if (isRapidFireButton(btn) && btn.libretroId in 0..15) {
                        next[btn.libretroId] = true
                    }
                }
            }
            postDelayed(turboTicker, TURBO_HALF_PERIOD_MS)
        } else if (!anyTurboHeld && turboRunning) {
            turboRunning = false
            removeCallbacks(turboTicker)
        }

        var newlyPressed = false
        for (id in 0..15) {
            val prev = digitalState[id]
            val cur = next[id]
            if (prev != cur) {
                digitalState[id] = cur
                if (cur && !prev) newlyPressed = true
                callback?.onDigitalButton(id, cur)
            }
        }

        if (triggerHaptics && newlyPressed) {
            pulseHaptic()
        }
    }

    fun releaseAllInputs() {
        pointerButtons.clear()
        dpadPointerId = MotionEvent.INVALID_POINTER_ID
        dpadUpActive = false
        dpadDownActive = false
        dpadLeftActive = false
        dpadRightActive = false

        if (leftStickPointerId != MotionEvent.INVALID_POINTER_ID) {
            leftStickPointerId = MotionEvent.INVALID_POINTER_ID
            callback?.onAnalogStick(STICK_LEFT, 0, 0)
        }
        if (rightStickPointerId != MotionEvent.INVALID_POINTER_ID) {
            rightStickPointerId = MotionEvent.INVALID_POINTER_ID
            callback?.onAnalogStick(STICK_RIGHT, 0, 0)
        }
        if (rewindHeld) {
            rewindHeld = false
            callback?.onRewindHold(false)
        }
        if (turboRunning) {
            turboRunning = false
            removeCallbacks(turboTicker)
        }
        for (id in 0..15) {
            if (digitalState[id]) {
                digitalState[id] = false
                callback?.onDigitalButton(id, false)
            }
        }
        invalidate()
    }

    private fun pulseHaptic() {
        if (!hapticsEnabled) return
        try {
            if (!performHapticFeedback(
                    HapticFeedbackConstants.KEYBOARD_TAP,
                    HapticFeedbackConstants.FLAG_IGNORE_GLOBAL_SETTING
                )
            ) {
                performHapticFeedback(
                    HapticFeedbackConstants.VIRTUAL_KEY,
                    HapticFeedbackConstants.FLAG_IGNORE_GLOBAL_SETTING
                )
            }
        } catch (_: Throwable) {
        }
    }

    private fun withAlpha(color: Int, alpha: Int): Int {
        return Color.argb(alpha.coerceIn(0, 255), Color.red(color), Color.green(color), Color.blue(color))
    }

    private fun lightenColor(color: Int, fraction: Float, alpha: Int): Int {
        val r = (Color.red(color) + (255 - Color.red(color)) * fraction).roundToInt().coerceIn(0, 255)
        val g = (Color.green(color) + (255 - Color.green(color)) * fraction).roundToInt().coerceIn(0, 255)
        val b = (Color.blue(color) + (255 - Color.blue(color)) * fraction).roundToInt().coerceIn(0, 255)
        return Color.argb(alpha.coerceIn(0, 255), r, g, b)
    }

    private fun darkenColor(color: Int, fraction: Float, alpha: Int): Int {
        val f = (1f - fraction).coerceIn(0f, 1f)
        val r = (Color.red(color) * f).roundToInt().coerceIn(0, 255)
        val g = (Color.green(color) * f).roundToInt().coerceIn(0, 255)
        val b = (Color.blue(color) * f).roundToInt().coerceIn(0, 255)
        return Color.argb(alpha.coerceIn(0, 255), r, g, b)
    }
}
