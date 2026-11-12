package com.storm.retroarch

import android.animation.ValueAnimator
import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.DashPathEffect
import android.graphics.LinearGradient
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.graphics.Shader
import android.graphics.Typeface
import android.os.Environment
import android.util.AttributeSet
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
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
    }

    enum class LayoutProfile(val id: String, val displayName: String) {
        NES("NES", "NES (2 кнопки + Turbo)"),
        SNES("SNES", "SNES / GBA / NDS (4 кнопки + L/R)"),
        GENESIS_6BTN("GENESIS_6BTN", "Sega Genesis (6 кнопок)"),
        PSX_DUALSHOCK("PSX_DUALSHOCK", "DualShock / 3D (2 стика + L1-R3)"),
        ARCADE("ARCADE", "Arcade (8-поз. стик + 6 кнопок)");

        companion object {
            fun fromId(id: String?): LayoutProfile {
                if (id.isNullOrBlank()) return SNES
                return entries.firstOrNull { it.id.equals(id.trim(), ignoreCase = true) } ?: SNES
            }

            fun resolveForPlatform(platformRaw: String?): LayoutProfile {
                val p = (platformRaw ?: "").trim().uppercase()
                return when {
                    p.contains("NES") && !p.contains("SNES") && !p.contains("GENESIS") -> NES
                    p.contains("FAMICOM") && !p.contains("SUPER") -> NES
                    p.contains("GAME BOY") && !p.contains("ADVANCE") -> NES
                    p == "GB" || p == "GBC" || p.contains("ATARI 2600") || p.contains("ATARI 7800") || p.contains("MASTER SYSTEM") || p == "SMS" || p == "GG" || p.contains("GAME GEAR") -> NES
                    p.contains("GENESIS") || p.contains("MEGA DRIVE") || p == "MD" || p.contains("32X") || p.contains("SEGA CD") || p.contains("SATURN") -> GENESIS_6BTN
                    p.contains("ARCADE") || p.contains("FBNEO") || p.contains("MAME") || p.contains("CPS") || p.contains("NEO GEO") || p == "NEOGEO" -> ARCADE
                    p.contains("PSX") || p.contains("PS1") || p.contains("PLAYSTATION") || p.contains("PS2") || p.contains("PSP") ||
                        p.contains("N64") || p.contains("NINTENDO 64") || p.contains("DREAMCAST") || p == "DC" ||
                        p.contains("3DS") || p.contains("GAMECUBE") || p.contains("WII") || p.contains("SWITCH") -> PSX_DUALSHOCK
                    else -> SNES
                }
            }
        }
    }

    enum class ControlGroup(val key: String, val labelRu: String) {
        DPAD("dpad", "D-Pad"),
        FACE_BUTTONS("face", "Кнопки"),
        LEFT_STICK("lstick", "Левый стик"),
        RIGHT_STICK("rstick", "Правый стик"),
        SHOULDERS("shoulders", "Шифты L/R"),
        START_SELECT("start_select", "Start / Select")
    }

    data class GroupPosition(var xPct: Float, var yPct: Float)

    private enum class SymbolKind {
        TEXT, CROSS, CIRCLE, SQUARE, TRIANGLE, MENU, FAST_FORWARD, REWIND
    }

    private data class VirtualButton(
        val key: String,
        val group: ControlGroup?,
        val libretroId: Int,
        val label: String,
        val symbol: SymbolKind = SymbolKind.TEXT,
        var cx: Float = 0f,
        var cy: Float = 0f,
        var radius: Float = 0f,
        var isCapsule: Boolean = false,
        var capsuleHalfW: Float = 0f,
        var capsuleHalfH: Float = 0f,
        var isTurbo: Boolean = false,
        var isUtility: Boolean = false
    )

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

        private const val PREFS_NAME = "storm_touch_layouts_prefs"
        private const val MIN_SCALE = 0.65f
        private const val MAX_SCALE = 1.50f
        private const val MIN_ALPHA = 0.15f
        private const val MAX_ALPHA = 0.95f
    }

    var callback: Callback? = null
    var hapticsEnabled: Boolean = true
    var accentColor: Int = 0xFF8B5CF6.toInt()
        set(value) {
            field = value
            invalidate()
        }

    var layoutProfile: LayoutProfile = LayoutProfile.SNES
        private set

    var buttonScale: Float = 1.0f
        private set

    var overlayAlpha: Float = 0.72f
        private set

    var isEditMode: Boolean = false
        set(value) {
            if (field != value) {
                field = value
                releaseAllInputs()
                if (!value) {
                    saveLayouts()
                }
                rebuildGeometry()
                invalidate()
            }
        }

    var isFastForwardActive: Boolean = false
        set(value) {
            field = value
            invalidate()
        }

    private var physicalControllerActive: Boolean = false
    private var visibilityFactor: Float = 1.0f
    private var fadeAnimator: ValueAnimator? = null

    private val customGroupPositions = HashMap<String, GroupPosition>()
    private val groupBounds = EnumMapLike<ControlGroup, RectF>()
    private val buttons = ArrayList<VirtualButton>(24)

    // D-Pad / Arcade stick geometry
    private var dpadCx = 0f
    private var dpadCy = 0f
    private var dpadRadius = 0f

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

    private var dpadPointerId = MotionEvent.INVALID_POINTER_ID
    private var arcadeKnobDx = 0f
    private var arcadeKnobDy = 0f

    // Editor state
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
            postDelayed(this, 33L)
        }
    }

    // Pre-allocated paints & paths (zero allocations in onDraw)
    private val density = resources.displayMetrics.density
    private val baseFillPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }
    private val bevelStrokePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE }
    private val glowPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE }
    private val highlightPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE }
    private val symbolPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeCap = Paint.Cap.ROUND
        strokeJoin = Paint.Join.ROUND
    }
    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        textAlign = Paint.Align.CENTER
        typeface = Typeface.create("sans-serif-medium", Typeface.BOLD)
    }
    private val editorBoxPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeWidth = 1.8f * density
        pathEffect = DashPathEffect(floatArrayOf(6f * density, 4f * density), 0f)
    }
    private val editorHudPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }
    private val tempRect = RectF()
    private val dpadPath = Path()
    private val arrowPath = Path()

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

    fun setPlatform(platformName: String?) {
        setLayoutProfile(LayoutProfile.resolveForPlatform(platformName))
    }

    fun setLayoutProfile(profile: LayoutProfile) {
        if (layoutProfile != profile) {
            releaseAllInputs()
            layoutProfile = profile
        }
        rebuildGeometry()
        invalidate()
    }

    fun setButtonScale(scale: Float, persist: Boolean = true) {
        buttonScale = scale.coerceIn(MIN_SCALE, MAX_SCALE)
        if (persist) saveLayouts()
        rebuildGeometry()
        invalidate()
    }

    fun setOverlayAlpha(alpha: Float, persist: Boolean = true) {
        overlayAlpha = alpha.coerceIn(MIN_ALPHA, MAX_ALPHA)
        if (persist) saveLayouts()
        invalidate()
    }

    fun setPhysicalControllerActive(active: Boolean) {
        if (physicalControllerActive == active) return
        physicalControllerActive = active
        if (active) {
            releaseAllInputs()
        }
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
        overlayAlpha = 0.72f
        saveLayouts()
        rebuildGeometry()
        invalidate()
    }

    fun resetAllLayouts() {
        customGroupPositions.clear()
        buttonScale = 1.0f
        overlayAlpha = 0.72f
        saveLayouts()
        rebuildGeometry()
        invalidate()
    }

    fun loadLayouts() {
        try {
            val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            var rawJson = prefs.getString("layouts_json", null)
            val external = settingsFile()
            if (external.exists() && external.length() > 2L) {
                rawJson = external.readText(Charsets.UTF_8)
            }
            if (!rawJson.isNullOrBlank()) {
                val root = JSONObject(rawJson)
                buttonScale = root.optDouble("buttonScale", 1.0).toFloat().coerceIn(MIN_SCALE, MAX_SCALE)
                overlayAlpha = root.optDouble("overlayAlpha", 0.72).toFloat().coerceIn(MIN_ALPHA, MAX_ALPHA)
                hapticsEnabled = root.optBoolean("hapticsEnabled", true)
                val groupsObj = root.optJSONObject("groups")
                if (groupsObj != null) {
                    customGroupPositions.clear()
                    val keys = groupsObj.keys()
                    while (keys.hasNext()) {
                        val k = keys.next()
                        val item = groupsObj.optJSONObject(k) ?: continue
                        val x = item.optDouble("x", 0.5).toFloat().coerceIn(0.05f, 0.95f)
                        val y = item.optDouble("y", 0.5).toFloat().coerceIn(0.05f, 0.95f)
                        customGroupPositions[k] = GroupPosition(x, y)
                    }
                }
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
            val groupsObj = JSONObject()
            for ((k, pos) in customGroupPositions) {
                val item = JSONObject()
                item.put("x", pos.xPct.toDouble())
                item.put("y", pos.yPct.toDouble())
                groupsObj.put(k, item)
            }
            root.put("groups", groupsObj)
            val serialized = root.toString(2)
            context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
                .edit()
                .putString("layouts_json", serialized)
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
        val dualShock = layoutProfile == LayoutProfile.PSX_DUALSHOCK
        return if (land) {
            when (group) {
                ControlGroup.DPAD -> if (dualShock) GroupPosition(0.13f, 0.54f) else GroupPosition(0.14f, 0.68f)
                ControlGroup.FACE_BUTTONS -> if (dualShock) GroupPosition(0.87f, 0.54f) else GroupPosition(0.85f, 0.68f)
                ControlGroup.LEFT_STICK -> GroupPosition(0.27f, 0.80f)
                ControlGroup.RIGHT_STICK -> GroupPosition(0.73f, 0.80f)
                ControlGroup.SHOULDERS -> GroupPosition(0.50f, 0.16f)
                ControlGroup.START_SELECT -> GroupPosition(0.50f, 0.90f)
            }
        } else {
            when (group) {
                ControlGroup.DPAD -> if (dualShock) GroupPosition(0.22f, 0.66f) else GroupPosition(0.24f, 0.74f)
                ControlGroup.FACE_BUTTONS -> if (dualShock) GroupPosition(0.78f, 0.66f) else GroupPosition(0.76f, 0.74f)
                ControlGroup.LEFT_STICK -> GroupPosition(0.30f, 0.86f)
                ControlGroup.RIGHT_STICK -> GroupPosition(0.70f, 0.86f)
                ControlGroup.SHOULDERS -> GroupPosition(0.50f, 0.52f)
                ControlGroup.START_SELECT -> GroupPosition(0.50f, 0.95f)
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
    }

    override fun onDetachedFromWindow() {
        releaseAllInputs()
        fadeAnimator?.cancel()
        removeCallbacks(turboTicker)
        super.onDetachedFromWindow()
    }

    private fun rebuildGeometry() {
        val w = width.toFloat()
        val h = height.toFloat()
        if (w <= 10f || h <= 10f) return

        buttons.clear()
        groupBounds.clear()
        val land = w >= h
        val minDim = min(w, h)
        val unit = (minDim * if (land) 0.078f else 0.072f) * buttonScale

        // Top utility pills (Menu, Rewind, Fast-Forward) - Menu stays in top-left/top-center discreetly
        val pillH = max(15f * density, 16f * density)
        val pillW = max(36f * density, 40f * density)
        val topY = max(24f * density, h * 0.055f)
        buttons.add(
            VirtualButton(
                key = "UTIL_MENU",
                group = null,
                libretroId = -1,
                label = "МЕНЮ",
                symbol = SymbolKind.MENU,
                cx = max(46f * density, w * 0.07f),
                cy = topY,
                isCapsule = true,
                capsuleHalfW = pillW * 0.92f,
                capsuleHalfH = pillH,
                isUtility = true
            )
        )
        buttons.add(
            VirtualButton(
                key = "UTIL_REWIND",
                group = null,
                libretroId = -2,
                label = "REW",
                symbol = SymbolKind.REWIND,
                cx = w * 0.50f - pillW * 1.05f,
                cy = topY,
                isCapsule = true,
                capsuleHalfW = pillW * 0.78f,
                capsuleHalfH = pillH * 0.92f,
                isUtility = true
            )
        )
        buttons.add(
            VirtualButton(
                key = "UTIL_FF",
                group = null,
                libretroId = -3,
                label = "FF",
                symbol = SymbolKind.FAST_FORWARD,
                cx = w * 0.50f + pillW * 1.05f,
                cy = topY,
                isCapsule = true,
                capsuleHalfW = pillW * 0.78f,
                capsuleHalfH = pillH * 0.92f,
                isUtility = true
            )
        )

        // D-Pad / Arcade Joystick
        val dpadPos = resolveGroupPos(ControlGroup.DPAD)
        dpadRadius = unit * 1.95f
        dpadCx = (dpadPos.xPct * w).coerceIn(dpadRadius + 8f * density, w - dpadRadius - 8f * density)
        dpadCy = (dpadPos.yPct * h).coerceIn(dpadRadius + 8f * density, h - dpadRadius - 8f * density)
        groupBounds[ControlGroup.DPAD] = RectF(
            dpadCx - dpadRadius * 1.12f,
            dpadCy - dpadRadius * 1.12f,
            dpadCx + dpadRadius * 1.12f,
            dpadCy + dpadRadius * 1.12f
        )

        // Start / Select
        val ssPos = resolveGroupPos(ControlGroup.START_SELECT)
        val ssCx = (ssPos.xPct * w).coerceIn(unit * 2.4f, w - unit * 2.4f)
        val ssCy = (ssPos.yPct * h).coerceIn(unit * 0.9f, h - unit * 0.8f)
        val capW = unit * 0.92f
        val capH = unit * 0.44f
        val ssGap = unit * 1.18f
        val selectLabel = when (layoutProfile) {
            LayoutProfile.GENESIS_6BTN -> "MODE"
            LayoutProfile.ARCADE -> "COIN"
            else -> "SELECT"
        }
        buttons.add(
            VirtualButton(
                key = "BTN_SELECT",
                group = ControlGroup.START_SELECT,
                libretroId = RETRO_DEVICE_ID_JOYPAD_SELECT,
                label = selectLabel,
                cx = ssCx - ssGap,
                cy = ssCy,
                isCapsule = true,
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
                cx = ssCx + ssGap,
                cy = ssCy,
                isCapsule = true,
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

        // Face Buttons
        val facePos = resolveGroupPos(ControlGroup.FACE_BUTTONS)
        val faceSpread = unit * 2.3f
        val fCx = (facePos.xPct * w).coerceIn(faceSpread, w - faceSpread)
        val fCy = (facePos.yPct * h).coerceIn(faceSpread, h - faceSpread)
        val btnR = unit * 0.78f

        when (layoutProfile) {
            LayoutProfile.NES -> {
                val dx = unit * 1.05f
                val dy = unit * 0.28f
                buttons.add(VirtualButton("BTN_B", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_B, "B", cx = fCx - dx, cy = fCy + dy, radius = btnR * 1.06f))
                buttons.add(VirtualButton("BTN_A", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_A, "A", cx = fCx + dx, cy = fCy - dy, radius = btnR * 1.06f))
                buttons.add(VirtualButton("TURBO_B", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_B, "TB", cx = fCx - dx, cy = fCy - unit * 1.45f, radius = btnR * 0.80f, isTurbo = true))
                buttons.add(VirtualButton("TURBO_A", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_A, "TA", cx = fCx + dx, cy = fCy - unit * 1.95f, radius = btnR * 0.80f, isTurbo = true))
            }
            LayoutProfile.SNES -> {
                val d = unit * 1.28f
                buttons.add(VirtualButton("BTN_Y", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_Y, "Y", cx = fCx - d, cy = fCy, radius = btnR))
                buttons.add(VirtualButton("BTN_X", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_X, "X", cx = fCx, cy = fCy - d, radius = btnR))
                buttons.add(VirtualButton("BTN_B", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_B, "B", cx = fCx, cy = fCy + d, radius = btnR))
                buttons.add(VirtualButton("BTN_A", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_A, "A", cx = fCx + d, cy = fCy, radius = btnR))
                buildShoulders(w, h, unit, dualTriggers = false)
            }
            LayoutProfile.GENESIS_6BTN -> {
                val col = unit * 1.52f
                val row = unit * 0.86f
                val topR = btnR * 0.78f
                val botR = btnR * 0.94f
                // Genesis Plus GX 6-button standard mapping: A=Y, B=B, C=A, X=L, Y=X, Z=R
                buttons.add(VirtualButton("GEN_X", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_L, "X", cx = fCx - col, cy = fCy - row + unit * 0.22f, radius = topR))
                buttons.add(VirtualButton("GEN_Y", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_X, "Y", cx = fCx, cy = fCy - row, radius = topR))
                buttons.add(VirtualButton("GEN_Z", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_R, "Z", cx = fCx + col, cy = fCy - row - unit * 0.22f, radius = topR))
                buttons.add(VirtualButton("GEN_A", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_Y, "A", cx = fCx - col, cy = fCy + row + unit * 0.22f, radius = botR))
                buttons.add(VirtualButton("GEN_B", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_B, "B", cx = fCx, cy = fCy + row, radius = botR))
                buttons.add(VirtualButton("GEN_C", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_A, "C", cx = fCx + col, cy = fCy + row - unit * 0.22f, radius = botR))
            }
            LayoutProfile.PSX_DUALSHOCK -> {
                val d = unit * 1.28f
                buttons.add(VirtualButton("PS_SQ", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_Y, "", SymbolKind.SQUARE, cx = fCx - d, cy = fCy, radius = btnR))
                buttons.add(VirtualButton("PS_TR", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_X, "", SymbolKind.TRIANGLE, cx = fCx, cy = fCy - d, radius = btnR))
                buttons.add(VirtualButton("PS_CR", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_B, "", SymbolKind.CROSS, cx = fCx, cy = fCy + d, radius = btnR))
                buttons.add(VirtualButton("PS_CI", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_A, "", SymbolKind.CIRCLE, cx = fCx + d, cy = fCy, radius = btnR))
                buildShoulders(w, h, unit, dualTriggers = true)
            }
            LayoutProfile.ARCADE -> {
                val col = unit * 1.54f
                val row = unit * 0.88f
                val arcR = btnR * 0.92f
                buttons.add(VirtualButton("ARC_1", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_Y, "1", cx = fCx - col, cy = fCy - row + unit * 0.24f, radius = arcR))
                buttons.add(VirtualButton("ARC_2", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_X, "2", cx = fCx, cy = fCy - row, radius = arcR))
                buttons.add(VirtualButton("ARC_3", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_L, "3", cx = fCx + col, cy = fCy - row - unit * 0.14f, radius = arcR))
                buttons.add(VirtualButton("ARC_4", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_B, "4", cx = fCx - col, cy = fCy + row + unit * 0.24f, radius = arcR))
                buttons.add(VirtualButton("ARC_5", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_A, "5", cx = fCx, cy = fCy + row, radius = arcR))
                buttons.add(VirtualButton("ARC_6", ControlGroup.FACE_BUTTONS, RETRO_DEVICE_ID_JOYPAD_R, "6", cx = fCx + col, cy = fCy + row - unit * 0.14f, radius = arcR))
            }
        }

        computeGroupBoundFromButtons(ControlGroup.FACE_BUTTONS)

        // Analog sticks for DualShock / 3D consoles
        hasAnalogSticks = layoutProfile == LayoutProfile.PSX_DUALSHOCK
        if (hasAnalogSticks) {
            val stickR = unit * 1.38f
            val lPos = resolveGroupPos(ControlGroup.LEFT_STICK)
            leftStickRadius = stickR
            leftStickCx = (lPos.xPct * w).coerceIn(stickR + 8f * density, w - stickR - 8f * density)
            leftStickCy = (lPos.yPct * h).coerceIn(stickR + 8f * density, h - stickR - 8f * density)
            groupBounds[ControlGroup.LEFT_STICK] = RectF(
                leftStickCx - stickR * 1.15f,
                leftStickCy - stickR * 1.15f,
                leftStickCx + stickR * 1.15f,
                leftStickCy + stickR * 1.15f
            )

            val rPos = resolveGroupPos(ControlGroup.RIGHT_STICK)
            rightStickRadius = stickR
            rightStickCx = (rPos.xPct * w).coerceIn(stickR + 8f * density, w - stickR - 8f * density)
            rightStickCy = (rPos.yPct * h).coerceIn(stickR + 8f * density, h - stickR - 8f * density)
            groupBounds[ControlGroup.RIGHT_STICK] = RectF(
                rightStickCx - stickR * 1.15f,
                rightStickCy - stickR * 1.15f,
                rightStickCx + stickR * 1.15f,
                rightStickCy + stickR * 1.15f
            )
        }

        // Editor HUD bar geometry
        val barW = min(w - 24f * density, 380f * density)
        val barH = 74f * density
        val barLeft = (w - barW) * 0.5f
        val barTop = max(topY + pillH + 10f * density, 54f * density)
        editorBarRect.set(barLeft, barTop, barLeft + barW, barTop + barH)

        val row1Y = barTop + 22f * density
        val row2Y = barTop + 53f * density
        val smallBtnW = 30f * density
        val smallBtnH = 22f * density
        btnScaleDownRect.set(barLeft + 88f * density, row1Y - smallBtnH * 0.5f, barLeft + 88f * density + smallBtnW, row1Y + smallBtnH * 0.5f)
        btnScaleUpRect.set(barLeft + 152f * density, row1Y - smallBtnH * 0.5f, barLeft + 152f * density + smallBtnW, row1Y + smallBtnH * 0.5f)
        btnAlphaDownRect.set(barRight() - 98f * density, row1Y - smallBtnH * 0.5f, barRight() - 98f * density + smallBtnW, row1Y + smallBtnH * 0.5f)
        btnAlphaUpRect.set(barRight() - 36f * density, row1Y - smallBtnH * 0.5f, barRight() - 36f * density + smallBtnW, row1Y + smallBtnH * 0.5f)

        btnResetRect.set(barLeft + 14f * density, row2Y - 12f * density, barLeft + barW * 0.48f, row2Y + 12f * density)
        btnDoneRect.set(barLeft + barW * 0.52f, row2Y - 12f * density, barRight() - 14f * density, row2Y + 12f * density)
    }

    private fun barRight(): Float = editorBarRect.right

    private fun buildShoulders(w: Float, h: Float, unit: Float, dualTriggers: Boolean) {
        val shPos = resolveGroupPos(ControlGroup.SHOULDERS)
        val shCy = (shPos.yPct * h).coerceIn(unit * 0.9f, h - unit * 0.9f)
        val shCenterOffset = (shPos.xPct - 0.50f) * w
        val padHalfW = unit * 1.18f
        val padHalfH = unit * 0.50f
        val leftX = (w * 0.13f + shCenterOffset).coerceIn(padHalfW + 8f * density, w * 0.46f)
        val rightX = (w * 0.87f + shCenterOffset).coerceIn(w * 0.54f, w - padHalfW - 8f * density)

        if (!dualTriggers) {
            buttons.add(VirtualButton("BTN_L", ControlGroup.SHOULDERS, RETRO_DEVICE_ID_JOYPAD_L, "L", cx = leftX, cy = shCy, isCapsule = true, capsuleHalfW = padHalfW, capsuleHalfH = padHalfH))
            buttons.add(VirtualButton("BTN_R", ControlGroup.SHOULDERS, RETRO_DEVICE_ID_JOYPAD_R, "R", cx = rightX, cy = shCy, isCapsule = true, capsuleHalfW = padHalfW, capsuleHalfH = padHalfH))
        } else {
            val innerGap = padHalfW * 2.08f
            buttons.add(VirtualButton("BTN_L2", ControlGroup.SHOULDERS, RETRO_DEVICE_ID_JOYPAD_L2, "L2", cx = leftX, cy = shCy - padHalfH * 1.1f, isCapsule = true, capsuleHalfW = padHalfW * 0.92f, capsuleHalfH = padHalfH * 0.88f))
            buttons.add(VirtualButton("BTN_L1", ControlGroup.SHOULDERS, RETRO_DEVICE_ID_JOYPAD_L, "L1", cx = leftX, cy = shCy + padHalfH * 1.25f, isCapsule = true, capsuleHalfW = padHalfW * 0.92f, capsuleHalfH = padHalfH * 0.88f))
            buttons.add(VirtualButton("BTN_L3", ControlGroup.SHOULDERS, RETRO_DEVICE_ID_JOYPAD_L3, "L3", cx = min(w * 0.44f, leftX + innerGap), cy = shCy + padHalfH * 0.15f, isCapsule = true, capsuleHalfW = padHalfW * 0.66f, capsuleHalfH = padHalfH * 0.82f))

            buttons.add(VirtualButton("BTN_R2", ControlGroup.SHOULDERS, RETRO_DEVICE_ID_JOYPAD_R2, "R2", cx = rightX, cy = shCy - padHalfH * 1.1f, isCapsule = true, capsuleHalfW = padHalfW * 0.92f, capsuleHalfH = padHalfH * 0.88f))
            buttons.add(VirtualButton("BTN_R1", ControlGroup.SHOULDERS, RETRO_DEVICE_ID_JOYPAD_R, "R1", cx = rightX, cy = shCy + padHalfH * 1.25f, isCapsule = true, capsuleHalfW = padHalfW * 0.92f, capsuleHalfH = padHalfH * 0.88f))
            buttons.add(VirtualButton("BTN_R3", ControlGroup.SHOULDERS, RETRO_DEVICE_ID_JOYPAD_R3, "R3", cx = max(w * 0.56f, rightX - innerGap), cy = shCy + padHalfH * 0.15f, isCapsule = true, capsuleHalfW = padHalfW * 0.66f, capsuleHalfH = padHalfH * 0.82f))
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

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        if (width <= 0 || height <= 0) return

        val effectiveAlpha = if (isEditMode) 0.92f else (overlayAlpha * visibilityFactor)

        // Always draw discrete Menu pill even when physical controller hides the rest of the overlay
        val menuBtn = buttons.firstOrNull { it.key == "UTIL_MENU" }
        if (menuBtn != null) {
            val menuAlpha = if (physicalControllerActive && !isEditMode) 0.42f else max(0.45f, effectiveAlpha)
            drawVirtualButton(canvas, menuBtn, menuAlpha, false)
        }

        if (effectiveAlpha <= 0.015f && !isEditMode) return

        // Draw Rewind & Fast-Forward pills
        for (b in buttons) {
            if (b.key == "UTIL_MENU") continue
            if (b.isUtility) {
                val activeHighlight = (b.key == "UTIL_FF" && isFastForwardActive) || (b.key == "UTIL_REWIND" && rewindHeld)
                drawVirtualButton(canvas, b, effectiveAlpha * 0.92f, activeHighlight)
            }
        }

        // Draw D-Pad or Arcade Stick
        if (layoutProfile == LayoutProfile.ARCADE) {
            drawArcadeStick(canvas, dpadCx, dpadCy, dpadRadius, arcadeKnobDx, arcadeKnobDy, effectiveAlpha)
        } else {
            drawDpad(canvas, effectiveAlpha)
        }

        // Draw Analog Sticks
        if (hasAnalogSticks) {
            drawAnalogStick(canvas, leftStickCx, leftStickCy, leftStickRadius, leftKnobDx, leftKnobDy, "L3", effectiveAlpha, leftStickPointerId != MotionEvent.INVALID_POINTER_ID)
            drawAnalogStick(canvas, rightStickCx, rightStickCy, rightStickRadius, rightKnobDx, rightKnobDy, "R3", effectiveAlpha, rightStickPointerId != MotionEvent.INVALID_POINTER_ID)
        }

        // Draw all console buttons
        for (b in buttons) {
            if (b.isUtility) continue
            val isPressed = isButtonVisuallyPressed(b)
            drawVirtualButton(canvas, b, effectiveAlpha, isPressed)
        }

        if (isEditMode) {
            drawEditorOverlay(canvas)
        }
    }

    private fun isButtonVisuallyPressed(b: VirtualButton): Boolean {
        for (set in pointerButtons.values) {
            if (set.contains(b.key)) return true
        }
        return false
    }

    private fun drawDpad(canvas: Canvas, alphaFactor: Float) {
        val arm = dpadRadius * 0.34f
        val r = dpadRadius
        val corner = arm * 0.28f
        val baseAlpha = (alphaFactor * 210f).roundToInt().coerceIn(0, 255)
        val strokeAlpha = (alphaFactor * 235f).roundToInt().coerceIn(0, 255)

        // Outer subtle shadow disc
        baseFillPaint.shader = null
        baseFillPaint.color = Color.argb((alphaFactor * 75f).roundToInt().coerceIn(0, 255), 8, 6, 18)
        canvas.drawCircle(dpadCx, dpadCy + 2f * density, r * 1.04f, baseFillPaint)

        // Cross path
        dpadPath.reset()
        dpadPath.addRoundRect(
            RectF(dpadCx - arm, dpadCy - r, dpadCx + arm, dpadCy + r),
            corner, corner, Path.Direction.CW
        )
        dpadPath.addRoundRect(
            RectF(dpadCx - r, dpadCy - arm, dpadCx + r, dpadCy + arm),
            corner, corner, Path.Direction.CW
        )

        baseFillPaint.shader = LinearGradient(
            dpadCx, dpadCy - r, dpadCx, dpadCy + r,
            Color.argb(baseAlpha, 44, 38, 72),
            Color.argb(baseAlpha, 20, 16, 36),
            Shader.TileMode.CLAMP
        )
        canvas.drawPath(dpadPath, baseFillPaint)
        baseFillPaint.shader = null

        bevelStrokePaint.strokeWidth = 1.6f * density
        bevelStrokePaint.color = withAlpha(accentColor, (strokeAlpha * 0.65f).roundToInt())
        canvas.drawPath(dpadPath, bevelStrokePaint)

        // Directional highlights & arrows
        drawDpadDirection(canvas, dpadCx, dpadCy - r * 0.62f, arm * 0.56f, 0, digitalState[RETRO_DEVICE_ID_JOYPAD_UP], alphaFactor)
        drawDpadDirection(canvas, dpadCx, dpadCy + r * 0.62f, arm * 0.56f, 2, digitalState[RETRO_DEVICE_ID_JOYPAD_DOWN], alphaFactor)
        drawDpadDirection(canvas, dpadCx - r * 0.62f, dpadCy, arm * 0.56f, 3, digitalState[RETRO_DEVICE_ID_JOYPAD_LEFT], alphaFactor)
        drawDpadDirection(canvas, dpadCx + r * 0.62f, dpadCy, arm * 0.56f, 1, digitalState[RETRO_DEVICE_ID_JOYPAD_RIGHT], alphaFactor)

        // Center pivot dimple
        baseFillPaint.color = Color.argb((alphaFactor * 140f).roundToInt(), 12, 10, 24)
        canvas.drawCircle(dpadCx, dpadCy, arm * 0.36f, baseFillPaint)
    }

    private fun drawDpadDirection(canvas: Canvas, cx: Float, cy: Float, sz: Float, dir: Int, active: Boolean, alphaFactor: Float) {
        if (active) {
            baseFillPaint.shader = null
            baseFillPaint.color = withAlpha(accentColor, (alphaFactor * 195f).roundToInt())
            canvas.drawCircle(cx, cy, sz * 1.15f, baseFillPaint)
        }
        arrowPath.reset()
        when (dir) {
            0 -> { // UP
                arrowPath.moveTo(cx, cy - sz * 0.55f)
                arrowPath.lineTo(cx - sz * 0.52f, cy + sz * 0.35f)
                arrowPath.lineTo(cx + sz * 0.52f, cy + sz * 0.35f)
            }
            1 -> { // RIGHT
                arrowPath.moveTo(cx + sz * 0.55f, cy)
                arrowPath.lineTo(cx - sz * 0.35f, cy - sz * 0.52f)
                arrowPath.lineTo(cx - sz * 0.35f, cy + sz * 0.52f)
            }
            2 -> { // DOWN
                arrowPath.moveTo(cx, cy + sz * 0.55f)
                arrowPath.lineTo(cx - sz * 0.52f, cy - sz * 0.35f)
                arrowPath.lineTo(cx + sz * 0.52f, cy - sz * 0.35f)
            }
            3 -> { // LEFT
                arrowPath.moveTo(cx - sz * 0.55f, cy)
                arrowPath.lineTo(cx + sz * 0.35f, cy - sz * 0.52f)
                arrowPath.lineTo(cx + sz * 0.35f, cy + sz * 0.52f)
            }
        }
        arrowPath.close()
        baseFillPaint.shader = null
        baseFillPaint.color = if (active) {
            Color.argb((alphaFactor * 255f).roundToInt(), 255, 255, 255)
        } else {
            Color.argb((alphaFactor * 200f).roundToInt(), 226, 232, 240)
        }
        canvas.drawPath(arrowPath, baseFillPaint)
    }

    private fun drawArcadeStick(canvas: Canvas, cx: Float, cy: Float, radius: Float, dx: Float, dy: Float, alphaFactor: Float) {
        drawAnalogStick(canvas, cx, cy, radius, dx, dy, "8-WAY", alphaFactor, dpadPointerId != MotionEvent.INVALID_POINTER_ID)
    }

    private fun drawAnalogStick(
        canvas: Canvas,
        cx: Float,
        cy: Float,
        radius: Float,
        knobDx: Float,
        knobDy: Float,
        subLabel: String,
        alphaFactor: Float,
        engaged: Boolean
    ) {
        val baseA = (alphaFactor * 155f).roundToInt().coerceIn(0, 255)
        baseFillPaint.shader = null
        baseFillPaint.color = Color.argb(baseA, 16, 13, 30)
        canvas.drawCircle(cx, cy, radius, baseFillPaint)

        bevelStrokePaint.strokeWidth = (if (engaged) 2.2f else 1.4f) * density
        bevelStrokePaint.color = withAlpha(accentColor, (alphaFactor * (if (engaged) 240f else 135f)).roundToInt())
        canvas.drawCircle(cx, cy, radius, bevelStrokePaint)

        // Shaft line
        symbolPaint.strokeWidth = 3.2f * density
        symbolPaint.color = Color.argb((alphaFactor * 165f).roundToInt(), 148, 163, 184)
        canvas.drawLine(cx, cy, cx + knobDx, cy + knobDy, symbolPaint)

        // 3D Volumetric thumb cap
        val capR = radius * 0.50f
        val kx = cx + knobDx
        val ky = cy + knobDy
        baseFillPaint.shader = LinearGradient(
            kx, ky - capR, kx, ky + capR,
            if (engaged) lightenColor(accentColor, 0.25f, (alphaFactor * 240f).roundToInt())
            else Color.argb((alphaFactor * 225f).roundToInt(), 62, 54, 98),
            if (engaged) withAlpha(accentColor, (alphaFactor * 235f).roundToInt())
            else Color.argb((alphaFactor * 225f).roundToInt(), 28, 23, 48),
            Shader.TileMode.CLAMP
        )
        canvas.drawCircle(kx, ky, capR, baseFillPaint)
        baseFillPaint.shader = null

        highlightPaint.strokeWidth = 1.3f * density
        highlightPaint.color = Color.argb((alphaFactor * 150f).roundToInt(), 255, 255, 255)
        canvas.drawCircle(kx, ky, capR * 0.72f, highlightPaint)

        if (!engaged) {
            textPaint.textSize = max(9f * density, capR * 0.42f)
            textPaint.color = Color.argb((alphaFactor * 175f).roundToInt(), 226, 232, 240)
            val fm = textPaint.fontMetrics
            canvas.drawText(subLabel, kx, ky - (fm.ascent + fm.descent) * 0.5f, textPaint)
        }
    }

    private fun drawVirtualButton(canvas: Canvas, b: VirtualButton, alphaFactor: Float, pressed: Boolean) {
        val pressOffset = if (pressed) 1.8f * density else 0f
        val scaleAdj = if (pressed) 0.95f else 1.0f
        val topAlpha = (alphaFactor * (if (pressed) 245f else 210f)).roundToInt().coerceIn(0, 255)
        val strokeAlpha = (alphaFactor * (if (pressed) 255f else 185f)).roundToInt().coerceIn(0, 255)

        val topColor = when {
            pressed -> lightenColor(accentColor, 0.22f, topAlpha)
            b.isTurbo -> Color.argb(topAlpha, 68, 42, 92)
            else -> Color.argb(topAlpha, 48, 40, 78)
        }
        val bottomColor = when {
            pressed -> withAlpha(accentColor, topAlpha)
            b.isTurbo -> Color.argb(topAlpha, 34, 20, 52)
            else -> Color.argb(topAlpha, 22, 18, 40)
        }

        if (b.isCapsule) {
            val hw = b.capsuleHalfW * scaleAdj
            val hh = b.capsuleHalfH * scaleAdj
            val cy = b.cy + pressOffset
            tempRect.set(b.cx - hw, cy - hh, b.cx + hw, cy + hh)

            // Outer drop shadow
            baseFillPaint.shader = null
            baseFillPaint.color = Color.argb((alphaFactor * 85f).roundToInt(), 5, 4, 12)
            canvas.drawRoundRect(
                tempRect.left, tempRect.top + 2f * density,
                tempRect.right, tempRect.bottom + 2.5f * density,
                hh, hh, baseFillPaint
            )

            baseFillPaint.shader = LinearGradient(
                b.cx, tempRect.top, b.cx, tempRect.bottom,
                topColor, bottomColor, Shader.TileMode.CLAMP
            )
            canvas.drawRoundRect(tempRect, hh, hh, baseFillPaint)
            baseFillPaint.shader = null

            bevelStrokePaint.strokeWidth = (if (pressed) 2.0f else 1.4f) * density
            bevelStrokePaint.color = withAlpha(accentColor, strokeAlpha)
            canvas.drawRoundRect(tempRect, hh, hh, bevelStrokePaint)

            drawButtonSymbolOrLabel(canvas, b, b.cx, cy, min(hw, hh), alphaFactor, pressed)
        } else {
            val r = b.radius * scaleAdj
            val cy = b.cy + pressOffset

            baseFillPaint.shader = null
            baseFillPaint.color = Color.argb((alphaFactor * 90f).roundToInt(), 5, 4, 12)
            canvas.drawCircle(b.cx, cy + 2.2f * density, r * 1.03f, baseFillPaint)

            baseFillPaint.shader = LinearGradient(
                b.cx, cy - r, b.cx, cy + r,
                topColor, bottomColor, Shader.TileMode.CLAMP
            )
            canvas.drawCircle(b.cx, cy, r, baseFillPaint)
            baseFillPaint.shader = null

            // Specular inner rim
            highlightPaint.strokeWidth = 1.1f * density
            highlightPaint.color = Color.argb((alphaFactor * 95f).roundToInt(), 255, 255, 255)
            canvas.drawArc(
                RectF(b.cx - r * 0.86f, cy - r * 0.86f, b.cx + r * 0.86f, cy + r * 0.86f),
                200f, 140f, false, highlightPaint
            )

            bevelStrokePaint.strokeWidth = (if (pressed) 2.2f else 1.5f) * density
            bevelStrokePaint.color = withAlpha(accentColor, strokeAlpha)
            canvas.drawCircle(b.cx, cy, r, bevelStrokePaint)

            drawButtonSymbolOrLabel(canvas, b, b.cx, cy, r, alphaFactor, pressed)
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
        symbolPaint.strokeWidth = max(2.0f * density, refRadius * 0.13f)
        when (b.symbol) {
            SymbolKind.CROSS -> {
                symbolPaint.color = Color.argb(fgAlpha, 125, 211, 252)
                val s = refRadius * 0.42f
                canvas.drawLine(cx - s, cy - s, cx + s, cy + s, symbolPaint)
                canvas.drawLine(cx + s, cy - s, cx - s, cy + s, symbolPaint)
            }
            SymbolKind.CIRCLE -> {
                symbolPaint.color = Color.argb(fgAlpha, 251, 113, 133)
                canvas.drawCircle(cx, cy, refRadius * 0.44f, symbolPaint)
            }
            SymbolKind.SQUARE -> {
                symbolPaint.color = Color.argb(fgAlpha, 244, 114, 182)
                val s = refRadius * 0.40f
                canvas.drawRoundRect(RectF(cx - s, cy - s, cx + s, cy + s), 2f * density, 2f * density, symbolPaint)
            }
            SymbolKind.TRIANGLE -> {
                symbolPaint.color = Color.argb(fgAlpha, 52, 211, 153)
                val s = refRadius * 0.46f
                arrowPath.reset()
                arrowPath.moveTo(cx, cy - s)
                arrowPath.lineTo(cx - s * 0.92f, cy + s * 0.72f)
                arrowPath.lineTo(cx + s * 0.92f, cy + s * 0.72f)
                arrowPath.close()
                canvas.drawPath(arrowPath, symbolPaint)
            }
            SymbolKind.MENU -> {
                symbolPaint.strokeWidth = 1.8f * density
                symbolPaint.color = Color.argb(fgAlpha, 248, 250, 252)
                val iconX = cx - b.capsuleHalfW * 0.52f
                val wLine = 6.5f * density
                canvas.drawLine(iconX - wLine, cy - 4f * density, iconX + wLine, cy - 4f * density, symbolPaint)
                canvas.drawLine(iconX - wLine, cy, iconX + wLine, cy, symbolPaint)
                canvas.drawLine(iconX - wLine, cy + 4f * density, iconX + wLine, cy + 4f * density, symbolPaint)

                textPaint.textSize = 10.5f * density
                textPaint.color = Color.argb(fgAlpha, 248, 250, 252)
                val fm = textPaint.fontMetrics
                canvas.drawText(b.label, cx + 6f * density, cy - (fm.ascent + fm.descent) * 0.5f, textPaint)
            }
            SymbolKind.REWIND -> {
                symbolPaint.strokeWidth = 1.8f * density
                symbolPaint.color = if (pressed) Color.WHITE else Color.argb(fgAlpha, 226, 232, 240)
                val s = 4.8f * density
                arrowPath.reset()
                arrowPath.moveTo(cx - 1f * density, cy - s)
                arrowPath.lineTo(cx - 8f * density, cy)
                arrowPath.lineTo(cx - 1f * density, cy + s)
                arrowPath.moveTo(cx + 7f * density, cy - s)
                arrowPath.lineTo(cx, cy)
                arrowPath.lineTo(cx + 7f * density, cy + s)
                canvas.drawPath(arrowPath, symbolPaint)
            }
            SymbolKind.FAST_FORWARD -> {
                symbolPaint.strokeWidth = 1.8f * density
                symbolPaint.color = if (pressed) Color.WHITE else Color.argb(fgAlpha, 226, 232, 240)
                val s = 4.8f * density
                arrowPath.reset()
                arrowPath.moveTo(cx - 7f * density, cy - s)
                arrowPath.lineTo(cx, cy)
                arrowPath.lineTo(cx - 7f * density, cy + s)
                arrowPath.moveTo(cx + 1f * density, cy - s)
                arrowPath.lineTo(cx + 8f * density, cy)
                arrowPath.lineTo(cx + 1f * density, cy + s)
                canvas.drawPath(arrowPath, symbolPaint)
            }
            SymbolKind.TEXT -> {
                textPaint.textSize = if (b.isCapsule) {
                    max(9.5f * density, refRadius * 0.72f)
                } else {
                    max(11f * density, refRadius * (if (b.label.length > 1) 0.54f else 0.72f))
                }
                textPaint.color = if (pressed) {
                    Color.argb(fgAlpha, 255, 255, 255)
                } else {
                    Color.argb(fgAlpha, 241, 245, 249)
                }
                val fm = textPaint.fontMetrics
                canvas.drawText(b.label, cx, cy - (fm.ascent + fm.descent) * 0.5f, textPaint)
            }
        }
    }

    private fun drawEditorOverlay(canvas: Canvas) {
        for ((group, rect) in groupBounds.entries()) {
            val selected = draggingGroup == group
            editorBoxPaint.color = if (selected) Color.argb(255, 56, 189, 248) else withAlpha(accentColor, 220)
            canvas.drawRoundRect(rect, 10f * density, 10f * density, editorBoxPaint)

            textPaint.textSize = 10f * density
            textPaint.color = Color.WHITE
            canvas.drawText(group.labelRu, rect.centerX(), max(12f * density, rect.top - 4f * density), textPaint)
        }

        // Top floating editor HUD
        editorHudPaint.color = Color.argb(235, 18, 14, 34)
        canvas.drawRoundRect(editorBarRect, 14f * density, 14f * density, editorHudPaint)
        bevelStrokePaint.strokeWidth = 1.5f * density
        bevelStrokePaint.color = withAlpha(accentColor, 230)
        canvas.drawRoundRect(editorBarRect, 14f * density, 14f * density, bevelStrokePaint)

        textPaint.textSize = 11f * density
        textPaint.color = Color.argb(245, 241, 245, 249)
        val row1TextY = editorBarRect.top + 26f * density
        canvas.drawText("Масштаб ${(buttonScale * 100).roundToInt()}%", editorBarRect.left + 46f * density, row1TextY, textPaint)
        drawEditorMiniBtn(canvas, btnScaleDownRect, "-", false)
        drawEditorMiniBtn(canvas, btnScaleUpRect, "+", false)

        canvas.drawText("Прозр. ${(overlayAlpha * 100).roundToInt()}%", editorBarRect.right - 138f * density, row1TextY, textPaint)
        drawEditorMiniBtn(canvas, btnAlphaDownRect, "-", false)
        drawEditorMiniBtn(canvas, btnAlphaUpRect, "+", false)

        drawEditorMiniBtn(canvas, btnResetRect, "Сброс раскладки", false)
        drawEditorMiniBtn(canvas, btnDoneRect, "Готово", true)
    }

    private fun drawEditorMiniBtn(canvas: Canvas, rect: RectF, text: String, primary: Boolean) {
        editorHudPaint.color = if (primary) accentColor else Color.argb(220, 42, 34, 70)
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

        // Wake overlay smoothly if physical controller had hidden it
        if (physicalControllerActive && action == MotionEvent.ACTION_DOWN) {
            val x = event.getX(actionIdx)
            val y = event.getY(actionIdx)
            val menuBtn = buttons.firstOrNull { it.key == "UTIL_MENU" }
            if (menuBtn != null && hitTestButton(menuBtn, x, y, 1.15f)) {
                pulseHaptic()
                callback?.onQuickMenuRequested()
                return true
            }
            setPhysicalControllerActive(false)
            return true
        }

        if (isEditMode) {
            return handleEditModeTouch(event)
        }

        when (action) {
            MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                val x = event.getX(actionIdx)
                val y = event.getY(actionIdx)
                val consumed = handlePointerDown(pointerId, x, y)
                if (!consumed && action == MotionEvent.ACTION_DOWN) {
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
                val x = event.getX(idx)
                val y = event.getY(idx)
                if (btnScaleDownRect.contains(x, y)) {
                    pulseHaptic()
                    setButtonScale(buttonScale - 0.08f)
                    return true
                }
                if (btnScaleUpRect.contains(x, y)) {
                    pulseHaptic()
                    setButtonScale(buttonScale + 0.08f)
                    return true
                }
                if (btnAlphaDownRect.contains(x, y)) {
                    pulseHaptic()
                    setOverlayAlpha(overlayAlpha - 0.08f)
                    return true
                }
                if (btnAlphaUpRect.contains(x, y)) {
                    pulseHaptic()
                    setOverlayAlpha(overlayAlpha + 0.08f)
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

                // Check group drag hit
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
                val group = draggingGroup
                if (group != null && dragPointerId != MotionEvent.INVALID_POINTER_ID) {
                    val pIndex = event.findPointerIndex(dragPointerId)
                    if (pIndex >= 0 && width > 0 && height > 0) {
                        val nx = ((event.getX(pIndex) + dragOffsetX) / width).coerceIn(0.07f, 0.93f)
                        val ny = ((event.getY(pIndex) + dragOffsetY) / height).coerceIn(0.08f, 0.94f)
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
        // 1. Utility buttons check
        for (b in buttons) {
            if (!b.isUtility) continue
            if (hitTestButton(b, x, y, 1.12f)) {
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

        // 4. Console buttons (supports pressing 1 or 2 adjacent face buttons via thumb chord!)
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
            if (hypot(x - dpadCx, y - dpadCy) <= dpadRadius * 1.85f) {
                updateDpadFromTouch(x, y, triggerHaptics = true)
            } else {
                dpadPointerId = MotionEvent.INVALID_POINTER_ID
                arcadeKnobDx = 0f
                arcadeKnobDy = 0f
                recomputeDigitalOutputs(triggerHaptics = false)
            }
            return
        }

        val currentSet = pointerButtons[pid]
        if (currentSet != null && currentSet.contains("UTIL_REWIND")) {
            return
        }

        // Allow smooth thumb sliding across face/shoulder buttons (e.g. B -> A, Y -> B) or sliding onto D-Pad
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
            leftKnobDx = 0f
            leftKnobDy = 0f
            callback?.onAnalogStick(STICK_LEFT, 0, 0)
        }
        if (pid == rightStickPointerId) {
            rightStickPointerId = MotionEvent.INVALID_POINTER_ID
            rightKnobDx = 0f
            rightKnobDy = 0f
            callback?.onAnalogStick(STICK_RIGHT, 0, 0)
        }
        if (pid == dpadPointerId) {
            dpadPointerId = MotionEvent.INVALID_POINTER_ID
            arcadeKnobDx = 0f
            arcadeKnobDy = 0f
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
        val maxTravel = (if (stickIndex == STICK_LEFT) leftStickRadius else rightStickRadius) * 0.65f
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
        val deadzone = 0.10f
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

        val deadzone = dpadRadius * 0.17f
        if (dist < deadzone) {
            dpadUpActive = false
            dpadDownActive = false
            dpadLeftActive = false
            dpadRightActive = false
        } else {
            // Overlapping 67.5-degree angular cones give effortless 8-way diagonals on thumb roll
            val angle = atan2(dy, dx) // -PI..PI, 0 = RIGHT, PI/2 = DOWN, -PI/2 = UP
            val cone = (PI * 0.375).toFloat() // 67.5 deg
            dpadRightActive = abs(angleDiff(angle, 0f)) <= cone
            dpadDownActive = abs(angleDiff(angle, (PI * 0.5).toFloat())) <= cone
            dpadLeftActive = abs(angleDiff(angle, PI.toFloat())) <= cone
            dpadUpActive = abs(angleDiff(angle, (-PI * 0.5).toFloat())) <= cone
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
            // Slightly expanded 1.18x radius for face buttons enables smooth B->A / Y->B thumb chord & roll
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
                if (btn.isTurbo) {
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
            nextStateApplyWithTurbo(next)
            postDelayed(turboTicker, 33L)
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

    private fun nextStateApplyWithTurbo(next: BooleanArray) {
        for (heldKeys in pointerButtons.values) {
            for (k in heldKeys) {
                val btn = buttons.firstOrNull { it.key == k } ?: continue
                if (btn.isTurbo && btn.libretroId in 0..15) {
                    next[btn.libretroId] = true
                }
            }
        }
    }

    fun releaseAllInputs() {
        pointerButtons.clear()
        dpadPointerId = MotionEvent.INVALID_POINTER_ID
        dpadUpActive = false
        dpadDownActive = false
        dpadLeftActive = false
        dpadRightActive = false
        arcadeKnobDx = 0f
        arcadeKnobDy = 0f

        if (leftStickPointerId != MotionEvent.INVALID_POINTER_ID || leftKnobDx != 0f || leftKnobDy != 0f) {
            leftStickPointerId = MotionEvent.INVALID_POINTER_ID
            leftKnobDx = 0f
            leftKnobDy = 0f
            callback?.onAnalogStick(STICK_LEFT, 0, 0)
        }
        if (rightStickPointerId != MotionEvent.INVALID_POINTER_ID || rightKnobDx != 0f || rightKnobDy != 0f) {
            rightStickPointerId = MotionEvent.INVALID_POINTER_ID
            rightKnobDx = 0f
            rightKnobDy = 0f
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
            performHapticFeedback(
                HapticFeedbackConstants.VIRTUAL_KEY,
                HapticFeedbackConstants.FLAG_IGNORE_GLOBAL_SETTING
            )
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
}
