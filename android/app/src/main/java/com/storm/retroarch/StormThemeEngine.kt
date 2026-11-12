package com.storm.retroarch

import android.content.Context
import android.content.res.ColorStateList
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.ColorFilter
import android.graphics.LinearGradient
import android.graphics.Paint
import android.graphics.Path
import android.graphics.PixelFormat
import android.graphics.Rect
import android.graphics.RectF
import android.graphics.Shader
import android.graphics.Typeface
import android.graphics.drawable.Drawable
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.LayerDrawable
import android.graphics.drawable.RippleDrawable
import android.util.TypedValue
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.View
import android.view.animation.OvershootInterpolator
import android.widget.TextView
import java.util.concurrent.CopyOnWriteArrayList
import kotlin.math.cos
import kotlin.math.min
import kotlin.math.roundToInt
import kotlin.math.sin

data class StormThemePalette(
    val id: String,
    val name: String,
    val bgMain: Int,
    val bgSurface: Int,
    val bgCard: Int,
    val bgElevated: Int,
    val textPrimary: Int,
    val textSecondary: Int,
    val border: Int,
    val defaultAccent: Int,
    val isLight: Boolean = false
)

data class StormAccentColor(
    val id: String,
    val name: String,
    val color: Int,
    val hex: String
)

data class StormFontProfile(
    val id: String,
    val name: String,
    val familyAlias: String,
    val typefaceStyle: Int = Typeface.NORMAL,
    val isMonospace: Boolean = false,
    val letterSpacingEm: Float = 0f
)

object StormThemeEngine {

    private const val PREFS_NAME = "storm_retroarch_theme_prefs"
    private const val KEY_THEME_ID = "theme_id"
    private const val KEY_ACCENT_ID = "accent_id"
    private const val KEY_FONT_ID = "font_id"

    val THEMES: List<StormThemePalette> = listOf(
        StormThemePalette(
            id = "MIDNIGHT",
            name = "MIDNIGHT",
            bgMain = 0xFF0D0A1A.toInt(),
            bgSurface = 0xFF151029.toInt(),
            bgCard = 0xFF1C1636.toInt(),
            bgElevated = 0xFF261E47.toInt(),
            textPrimary = 0xFFF1F5F9.toInt(),
            textSecondary = 0xFFA59EC4.toInt(),
            border = 0xFF34285E.toInt(),
            defaultAccent = 0xFF8B5CF6.toInt(),
            isLight = false
        ),
        StormThemePalette(
            id = "NIGHT",
            name = "NIGHT",
            bgMain = 0xFF040407.toInt(),
            bgSurface = 0xFF0A0A11.toInt(),
            bgCard = 0xFF11111B.toInt(),
            bgElevated = 0xFF1A1A28.toInt(),
            textPrimary = 0xFFF4F4F8.toInt(),
            textSecondary = 0xFF9696B0.toInt(),
            border = 0xFF25253A.toInt(),
            defaultAccent = 0xFF6366F1.toInt(),
            isLight = false
        ),
        StormThemePalette(
            id = "DARK",
            name = "DARK",
            bgMain = 0xFF0B0F19.toInt(),
            bgSurface = 0xFF111827.toInt(),
            bgCard = 0xFF182235.toInt(),
            bgElevated = 0xFF22304A.toInt(),
            textPrimary = 0xFFECEFF4.toInt(),
            textSecondary = 0xFF9AA5B8.toInt(),
            border = 0xFF2E3B52.toInt(),
            defaultAccent = 0xFF3B82F6.toInt(),
            isLight = false
        ),
        StormThemePalette(
            id = "DAY",
            name = "DAY",
            bgMain = 0xFFF1F5F9.toInt(),
            bgSurface = 0xFFFFFFFF.toInt(),
            bgCard = 0xFFFFFFFF.toInt(),
            bgElevated = 0xFFE2E8F0.toInt(),
            textPrimary = 0xFF0F172A.toInt(),
            textSecondary = 0xFF334155.toInt(),
            border = 0xFFCBD5E1.toInt(),
            defaultAccent = 0xFF6366F1.toInt(),
            isLight = true
        ),
        StormThemePalette(
            id = "FANTASY",
            name = "FANTASY",
            bgMain = 0xFF120E07.toInt(),
            bgSurface = 0xFF1C160C.toInt(),
            bgCard = 0xFF261E11.toInt(),
            bgElevated = 0xFF332817.toInt(),
            textPrimary = 0xFFFEF3C7.toInt(),
            textSecondary = 0xFFD4B982.toInt(),
            border = 0xFF4F3E22.toInt(),
            defaultAccent = 0xFFF59E0B.toInt(),
            isLight = false
        ),
        StormThemePalette(
            id = "CYBERPUNK",
            name = "CYBERPUNK",
            bgMain = 0xFF14071E.toInt(),
            bgSurface = 0xFF1E0B2D.toInt(),
            bgCard = 0xFF29103D.toInt(),
            bgElevated = 0xFF381652.toInt(),
            textPrimary = 0xFFFDF4FF.toInt(),
            textSecondary = 0xFFE879F9.toInt(),
            border = 0xFF5B2178.toInt(),
            defaultAccent = 0xFFEC4899.toInt(),
            isLight = false
        ),
        StormThemePalette(
            id = "WARHAMMER 40K",
            name = "WARHAMMER 40K",
            bgMain = 0xFF111114.toInt(),
            bgSurface = 0xFF19191E.toInt(),
            bgCard = 0xFF222229.toInt(),
            bgElevated = 0xFF2D2D36.toInt(),
            textPrimary = 0xFFF4F4F5.toInt(),
            textSecondary = 0xFFA1A1AA.toInt(),
            border = 0xFF472629.toInt(),
            defaultAccent = 0xFFEF4444.toInt(),
            isLight = false
        ),
        StormThemePalette(
            id = "ELEMENTAL",
            name = "ELEMENTAL",
            bgMain = 0xFF071518.toInt(),
            bgSurface = 0xFF0C2126.toInt(),
            bgCard = 0xFF112D34.toInt(),
            bgElevated = 0xFF183C45.toInt(),
            textPrimary = 0xFFECFEFF.toInt(),
            textSecondary = 0xFF8CE0EA.toInt(),
            border = 0xFF1E4E5A.toInt(),
            defaultAccent = 0xFF14B8A6.toInt(),
            isLight = false
        ),
        StormThemePalette(
            id = "STORM",
            name = "STORM",
            bgMain = 0xFF090D1E.toInt(),
            bgSurface = 0xFF0F1630.toInt(),
            bgCard = 0xFF162044.toInt(),
            bgElevated = 0xFF1F2D5C.toInt(),
            textPrimary = 0xFFF0F9FF.toInt(),
            textSecondary = 0xFF93C5FD.toInt(),
            border = 0xFF2A3F7E.toInt(),
            defaultAccent = 0xFF06B6D4.toInt(),
            isLight = false
        )
    )

    val ACCENTS: List<StormAccentColor> = listOf(
        StormAccentColor("violet", "Violet", 0xFF8B5CF6.toInt(), "#8B5CF6"),
        StormAccentColor("cyan", "Cyan", 0xFF06B6D4.toInt(), "#06B6D4"),
        StormAccentColor("electric_blue", "Electric Blue", 0xFF3B82F6.toInt(), "#3B82F6"),
        StormAccentColor("royal_indigo", "Royal Indigo", 0xFF6366F1.toInt(), "#6366F1"),
        StormAccentColor("teal_mint", "Teal Mint", 0xFF14B8A6.toInt(), "#14B8A6"),
        StormAccentColor("emerald_green", "Emerald Green", 0xFF10B981.toInt(), "#10B981"),
        StormAccentColor("neon_lime", "Neon Lime", 0xFF84CC16.toInt(), "#84CC16"),
        StormAccentColor("amber_gold", "Amber Gold", 0xFFF59E0B.toInt(), "#F59E0B"),
        StormAccentColor("solar_orange", "Solar Orange", 0xFFF97316.toInt(), "#F97316"),
        StormAccentColor("crimson_red", "Crimson Red", 0xFFEF4444.toInt(), "#EF4444"),
        StormAccentColor("rose_pink", "Rose Pink", 0xFFF43F5E.toInt(), "#F43F5E"),
        StormAccentColor("magenta_fuchsia", "Magenta Fuchsia", 0xFFEC4899.toInt(), "#EC4899"),
        StormAccentColor("platinum_silver", "Platinum Silver", 0xFFE2E8F0.toInt(), "#E2E8F0")
    )

    val FONTS: List<StormFontProfile> = listOf(
        StormFontProfile("inter_variable", "Inter Variable", "sans-serif", Typeface.NORMAL, false, 0.0f),
        StormFontProfile("plus_jakarta_sans", "Plus Jakarta Sans", "sans-serif-medium", Typeface.NORMAL, false, 0.01f),
        StormFontProfile("geist_sans", "Geist Sans", "sans-serif", Typeface.NORMAL, false, 0.005f),
        StormFontProfile("manrope", "Manrope", "sans-serif-medium", Typeface.NORMAL, false, 0.01f),
        StormFontProfile("outfit", "Outfit", "sans-serif", Typeface.NORMAL, false, 0.015f),
        StormFontProfile("montserrat", "Montserrat", "sans-serif-medium", Typeface.NORMAL, false, 0.02f),
        StormFontProfile("rubik", "Rubik", "sans-serif", Typeface.NORMAL, false, 0.01f),
        StormFontProfile("orbitron", "Orbitron", "sans-serif-condensed", Typeface.BOLD, false, 0.06f),
        StormFontProfile("jetbrains_mono", "JetBrains Mono", "monospace", Typeface.NORMAL, true, 0.0f),
        StormFontProfile("cascadia_code", "Cascadia Code", "monospace", Typeface.NORMAL, true, 0.01f)
    )

    @Volatile
    var currentTheme: StormThemePalette = THEMES[0]
        private set

    @Volatile
    var currentAccent: StormAccentColor = ACCENTS[0]
        private set

    @Volatile
    var currentFont: StormFontProfile = FONTS[0]
        private set

    val activeAccentColor: Int
        get() = currentAccent.color

    private val listeners = CopyOnWriteArrayList<() -> Unit>()

    fun addThemeChangeListener(listener: () -> Unit) {
        if (!listeners.contains(listener)) {
            listeners.add(listener)
        }
    }

    fun removeThemeChangeListener(listener: () -> Unit) {
        listeners.remove(listener)
    }

    private fun notifyListeners() {
        for (listener in listeners) {
            listener.invoke()
        }
    }

    fun loadFromPrefs(ctx: Context) {
        val prefs = ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val themeId = prefs.getString(KEY_THEME_ID, "MIDNIGHT") ?: "MIDNIGHT"
        val accentId = prefs.getString(KEY_ACCENT_ID, "violet") ?: "violet"
        val fontId = prefs.getString(KEY_FONT_ID, "inter_variable") ?: "inter_variable"

        currentTheme = findTheme(themeId)
        currentAccent = findAccent(accentId)
        currentFont = findFont(fontId)
    }

    fun saveToPrefs(ctx: Context) {
        ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .edit()
            .putString(KEY_THEME_ID, currentTheme.id)
            .putString(KEY_ACCENT_ID, currentAccent.id)
            .putString(KEY_FONT_ID, currentFont.id)
            .apply()
    }

    fun findTheme(idOrName: String): StormThemePalette {
        return THEMES.firstOrNull {
            it.id.equals(idOrName, ignoreCase = true) || it.name.equals(idOrName, ignoreCase = true)
        } ?: THEMES[0]
    }

    fun findAccent(idOrName: String): StormAccentColor {
        return ACCENTS.firstOrNull {
            it.id.equals(idOrName, ignoreCase = true) ||
                it.name.equals(idOrName, ignoreCase = true) ||
                it.hex.equals(idOrName, ignoreCase = true)
        } ?: ACCENTS[0]
    }

    fun findFont(idOrName: String): StormFontProfile {
        return FONTS.firstOrNull {
            it.id.equals(idOrName, ignoreCase = true) || it.name.equals(idOrName, ignoreCase = true)
        } ?: FONTS[0]
    }

    fun setTheme(idOrName: String, ctx: Context? = null, syncDefaultAccent: Boolean = false) {
        val next = findTheme(idOrName)
        currentTheme = next
        if (syncDefaultAccent) {
            ACCENTS.firstOrNull { it.color == next.defaultAccent }?.let { currentAccent = it }
        }
        if (ctx != null) saveToPrefs(ctx)
        notifyListeners()
    }

    fun setAccent(idOrName: String, ctx: Context? = null) {
        currentAccent = findAccent(idOrName)
        if (ctx != null) saveToPrefs(ctx)
        notifyListeners()
    }

    fun setFont(idOrName: String, ctx: Context? = null) {
        currentFont = findFont(idOrName)
        if (ctx != null) saveToPrefs(ctx)
        notifyListeners()
    }

    fun resolveTypeface(profile: StormFontProfile = currentFont, bold: Boolean = false): Typeface {
        val targetStyle = if (bold) Typeface.BOLD else profile.typefaceStyle
        return try {
            if (profile.isMonospace) {
                Typeface.create(Typeface.MONOSPACE, targetStyle)
            } else {
                Typeface.create(profile.familyAlias, targetStyle)
            }
        } catch (_: Throwable) {
            if (profile.isMonospace) Typeface.MONOSPACE else Typeface.SANS_SERIF
        }
    }

    fun applyFont(textView: TextView, bold: Boolean = false, profile: StormFontProfile = currentFont) {
        textView.typeface = resolveTypeface(profile, bold)
        textView.letterSpacing = profile.letterSpacingEm
    }

    fun dpToPx(ctx: Context, dp: Float): Int {
        return TypedValue.applyDimension(
            TypedValue.COMPLEX_UNIT_DIP,
            dp,
            ctx.resources.displayMetrics
        ).roundToInt().coerceAtLeast(if (dp > 0f) 1 else 0)
    }

    fun dpToPxF(ctx: Context, dp: Float): Float {
        return TypedValue.applyDimension(
            TypedValue.COMPLEX_UNIT_DIP,
            dp,
            ctx.resources.displayMetrics
        )
    }

    fun withAlpha(color: Int, alpha: Int): Int {
        val a = alpha.coerceIn(0, 255)
        return (color and 0x00FFFFFF) or (a shl 24)
    }

    fun blendColors(colorA: Int, colorB: Int, ratio: Float): Int {
        val t = ratio.coerceIn(0f, 1f)
        val inv = 1f - t
        val a = (Color.alpha(colorA) * inv + Color.alpha(colorB) * t).roundToInt()
        val r = (Color.red(colorA) * inv + Color.red(colorB) * t).roundToInt()
        val g = (Color.green(colorA) * inv + Color.green(colorB) * t).roundToInt()
        val b = (Color.blue(colorA) * inv + Color.blue(colorB) * t).roundToInt()
        return Color.argb(a, r, g, b)
    }

    fun lighten(color: Int, fraction: Float): Int = blendColors(color, Color.WHITE, fraction)

    fun darken(color: Int, fraction: Float): Int = blendColors(color, Color.BLACK, fraction)

    fun contrastTextForFill(fillColor: Int): Int {
        val lum = (0.299 * Color.red(fillColor) + 0.587 * Color.green(fillColor) + 0.114 * Color.blue(fillColor)) / 255.0
        return if (lum > 0.62) 0xFF0F172A.toInt() else 0xFFF8FAFC.toInt()
    }

    /**
     * Multi-layer 3D volumetric button drawable:
     * Layer 0: Bottom 3D bevel shadow
     * Layer 1: Diagonal TL->BR volumetric fill gradient + outer stroke
     * Layer 2: Crisp top specular rim sheen
     * Wrapped in tactile RippleDrawable.
     */
    fun createVolumetricButtonDrawable(
        ctx: Context,
        fillTop: Int,
        fillBottom: Int,
        strokeColor: Int,
        cornerDp: Float = 12f
    ): Drawable {
        val radiusPx = dpToPxF(ctx, cornerDp)
        val bevelOffsetPx = dpToPx(ctx, 2.5f)
        val strokePx = dpToPx(ctx, 1f)
        val rimInsetPx = dpToPx(ctx, 1.2f)

        val shadowLayer = GradientDrawable().apply {
            shape = GradientDrawable.RECTANGLE
            cornerRadius = radiusPx
            setColor(darken(fillBottom, 0.45f))
        }

        val surfaceLayer = GradientDrawable(
            GradientDrawable.Orientation.TL_BR,
            intArrayOf(fillTop, fillBottom)
        ).apply {
            shape = GradientDrawable.RECTANGLE
            cornerRadius = radiusPx
            setStroke(strokePx, strokeColor)
        }

        val specularRimLayer = GradientDrawable(
            GradientDrawable.Orientation.TOP_BOTTOM,
            intArrayOf(
                withAlpha(Color.WHITE, if (currentTheme.isLight) 150 else 58),
                withAlpha(Color.WHITE, 10),
                Color.TRANSPARENT
            )
        ).apply {
            shape = GradientDrawable.RECTANGLE
            cornerRadius = (radiusPx - rimInsetPx).coerceAtLeast(1f)
            setStroke(dpToPx(ctx, 0.8f), withAlpha(Color.WHITE, if (currentTheme.isLight) 170 else 64))
        }

        val layered = LayerDrawable(arrayOf(shadowLayer, surfaceLayer, specularRimLayer)).apply {
            setLayerInset(0, 0, bevelOffsetPx, 0, 0)
            setLayerInset(1, 0, 0, 0, bevelOffsetPx)
            setLayerInset(2, rimInsetPx, rimInsetPx, rimInsetPx, bevelOffsetPx + dpToPx(ctx, 6f))
        }

        val mask = GradientDrawable().apply {
            shape = GradientDrawable.RECTANGLE
            cornerRadius = radiusPx
            setColor(Color.WHITE)
        }

        val rippleColor = ColorStateList.valueOf(withAlpha(Color.WHITE, 56))
        return RippleDrawable(rippleColor, layered, mask)
    }

    fun createAccentButtonDrawable(ctx: Context, accent: Int = activeAccentColor, cornerDp: Float = 12f): Drawable {
        val top = lighten(accent, 0.14f)
        val bottom = darken(accent, 0.18f)
        val stroke = lighten(accent, 0.30f)
        return createVolumetricButtonDrawable(ctx, top, bottom, stroke, cornerDp)
    }

    fun createSurfaceButtonDrawable(ctx: Context, cornerDp: Float = 12f): Drawable {
        val pal = currentTheme
        val top = lighten(pal.bgElevated, if (pal.isLight) 0.25f else 0.08f)
        val bottom = darken(pal.bgCard, if (pal.isLight) 0.04f else 0.12f)
        return createVolumetricButtonDrawable(ctx, top, bottom, pal.border, cornerDp)
    }

    /**
     * Multi-layer 3D volumetric card drawable with outer ambient shadow, diagonal surface gradient,
     * crisp 1dp border, and subtle top specular edge.
     */
    fun createVolumetricCardDrawable(
        ctx: Context,
        bgCard: Int = currentTheme.bgCard,
        borderColor: Int = currentTheme.border,
        cornerDp: Float = 16f
    ): Drawable {
        val radiusPx = dpToPxF(ctx, cornerDp)
        val shadowOffsetPx = dpToPx(ctx, 2f)
        val strokePx = dpToPx(ctx, 1f)
        val insetPx = dpToPx(ctx, 1f)

        val baseShadow = GradientDrawable().apply {
            shape = GradientDrawable.RECTANGLE
            cornerRadius = radiusPx
            setColor(withAlpha(Color.BLACK, if (currentTheme.isLight) 24 else 96))
        }

        val topFill = if (currentTheme.isLight) Color.WHITE else lighten(bgCard, 0.05f)
        val bottomFill = if (currentTheme.isLight) darken(bgCard, 0.02f) else darken(bgCard, 0.07f)

        val cardBody = GradientDrawable(
            GradientDrawable.Orientation.TL_BR,
            intArrayOf(topFill, bottomFill)
        ).apply {
            shape = GradientDrawable.RECTANGLE
            cornerRadius = radiusPx
            setStroke(strokePx, borderColor)
        }

        val topRim = GradientDrawable(
            GradientDrawable.Orientation.TOP_BOTTOM,
            intArrayOf(
                withAlpha(Color.WHITE, if (currentTheme.isLight) 180 else 28),
                Color.TRANSPARENT
            )
        ).apply {
            shape = GradientDrawable.RECTANGLE
            cornerRadius = (radiusPx - insetPx).coerceAtLeast(1f)
        }

        return LayerDrawable(arrayOf(baseShadow, cardBody, topRim)).apply {
            setLayerInset(0, 0, shadowOffsetPx, 0, 0)
            setLayerInset(1, 0, 0, 0, shadowOffsetPx)
            setLayerInset(2, insetPx, insetPx, insetPx, shadowOffsetPx + dpToPx(ctx, 12f))
        }
    }

    /**
     * Recessed 3D input well drawable with top inner depth shadow and clean border.
     */
    fun createRecessedInputDrawable(
        ctx: Context,
        bgMain: Int = currentTheme.bgMain,
        borderColor: Int = currentTheme.border,
        cornerDp: Float = 12f
    ): Drawable {
        val radiusPx = dpToPxF(ctx, cornerDp)
        val strokePx = dpToPx(ctx, 1f)
        val innerInset = dpToPx(ctx, 1f)

        val bottomLip = GradientDrawable().apply {
            shape = GradientDrawable.RECTANGLE
            cornerRadius = radiusPx
            setColor(withAlpha(Color.WHITE, if (currentTheme.isLight) 160 else 22))
        }

        val topWellColor = if (currentTheme.isLight) darken(bgMain, 0.05f) else darken(bgMain, 0.28f)
        val bottomWellColor = if (currentTheme.isLight) bgMain else lighten(bgMain, 0.03f)

        val recessedWell = GradientDrawable(
            GradientDrawable.Orientation.TOP_BOTTOM,
            intArrayOf(topWellColor, bottomWellColor)
        ).apply {
            shape = GradientDrawable.RECTANGLE
            cornerRadius = radiusPx
            setStroke(strokePx, borderColor)
        }

        return LayerDrawable(arrayOf(bottomLip, recessedWell)).apply {
            setLayerInset(0, 0, innerInset, 0, 0)
            setLayerInset(1, 0, 0, 0, innerInset)
        }
    }

    fun createFilterChipDrawable(
        ctx: Context,
        selected: Boolean,
        accentColor: Int = activeAccentColor,
        cornerDp: Float = 18f
    ): Drawable {
        val pal = currentTheme
        return if (selected) {
            createVolumetricButtonDrawable(
                ctx = ctx,
                fillTop = lighten(accentColor, 0.12f),
                fillBottom = darken(accentColor, 0.16f),
                strokeColor = lighten(accentColor, 0.28f),
                cornerDp = cornerDp
            )
        } else {
            createVolumetricButtonDrawable(
                ctx = ctx,
                fillTop = lighten(pal.bgCard, 0.05f),
                fillBottom = darken(pal.bgCard, 0.06f),
                strokeColor = pal.border,
                cornerDp = cornerDp
            )
        }
    }

    /**
     * Attaches damped tactile spring scale (0.96f on press, 1.0f on release) and virtual key haptics.
     * Preserves standard OnClickListener and RippleDrawable state transitions.
     */
    fun attachSpringTouchFeedback(view: View) {
        val springInterpolator = OvershootInterpolator(2.1f)
        view.setOnTouchListener { v, event ->
            when (event.actionMasked) {
                MotionEvent.ACTION_DOWN -> {
                    v.animate().cancel()
                    v.animate()
                        .scaleX(0.96f)
                        .scaleY(0.96f)
                        .setDuration(85L)
                        .start()
                    v.performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY)
                }
                MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                    v.animate().cancel()
                    v.animate()
                        .scaleX(1.0f)
                        .scaleY(1.0f)
                        .setDuration(165L)
                        .setInterpolator(springInterpolator)
                        .start()
                }
            }
            false
        }
    }
}

/**
 * Pure geometric Canvas vector icons obeying the Single Icon Mandate (zero emojis, crisp anti-aliased paths).
 */
object StormVectorIcons {

    const val GAMEPAD = "GAMEPAD"
    const val LIBRARY = "LIBRARY"
    const val CORES = "CORES"
    const val CHEATS = "CHEATS"
    const val BIOS = "BIOS"
    const val BEZEL = "BEZEL"
    const val SETTINGS = "SETTINGS"
    const val SEARCH = "SEARCH"
    const val PLAY = "PLAY"
    const val STAR = "STAR"
    const val FOLDER = "FOLDER"
    const val DOWNLOAD = "DOWNLOAD"
    const val REFRESH = "REFRESH"
    const val SAVE = "SAVE"
    const val LOAD = "LOAD"
    const val REWIND = "REWIND"
    const val FAST_FORWARD = "FAST_FORWARD"
    const val RAM = "RAM"
    const val CAMERA = "CAMERA"
    const val CLOSE = "CLOSE"
    const val SLIDERS = "SLIDERS"
    const val CHECK = "CHECK"
    const val SHIELD = "SHIELD"
    const val CLOUD = "CLOUD"

    enum class Icon {
        GAMEPAD, LIBRARY, CORES, CHEATS, BIOS, BEZEL, SETTINGS, SEARCH,
        PLAY, STAR, FOLDER, DOWNLOAD, REFRESH, SAVE, LOAD, REWIND,
        FAST_FORWARD, RAM, CAMERA, CLOSE, SLIDERS, CHECK, SHIELD, CLOUD
    }

    fun create(
        ctx: Context,
        iconName: String,
        color: Int = StormThemeEngine.currentTheme.textPrimary,
        sizeDp: Float = 20f,
        strokeDp: Float = 1.9f
    ): Drawable {
        val sizePx = StormThemeEngine.dpToPx(ctx, sizeDp)
        val strokePx = StormThemeEngine.dpToPxF(ctx, strokeDp)
        return VectorCanvasDrawable(iconName.uppercase(), color, sizePx, strokePx)
    }

    fun create(
        ctx: Context,
        icon: Icon,
        color: Int = StormThemeEngine.currentTheme.textPrimary,
        sizeDp: Float = 20f,
        strokeDp: Float = 1.9f
    ): Drawable = create(ctx, icon.name, color, sizeDp, strokeDp)

    fun get(
        ctx: Context,
        iconName: String,
        color: Int = StormThemeEngine.currentTheme.textPrimary,
        sizeDp: Float = 20f
    ): Drawable = create(ctx, iconName, color, sizeDp)

    private class VectorCanvasDrawable(
        private val iconKey: String,
        initialColor: Int,
        private val sizePx: Int,
        private val strokeWidthPx: Float
    ) : Drawable() {

        private var tintColor: Int = initialColor
        private var customAlpha: Int = 255

        private val strokePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeWidth = strokeWidthPx
            strokeCap = Paint.Cap.ROUND
            strokeJoin = Paint.Join.ROUND
            color = initialColor
        }

        private val fillPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.FILL
            color = initialColor
        }

        override fun getIntrinsicWidth(): Int = sizePx
        override fun getIntrinsicHeight(): Int = sizePx

        override fun setAlpha(alpha: Int) {
            customAlpha = alpha.coerceIn(0, 255)
            invalidateSelf()
        }

        override fun setColorFilter(colorFilter: ColorFilter?) {
            strokePaint.colorFilter = colorFilter
            fillPaint.colorFilter = colorFilter
            invalidateSelf()
        }

        override fun setTint(tint: Int) {
            tintColor = tint
            invalidateSelf()
        }

        @Deprecated("Deprecated in Android SDK")
        override fun getOpacity(): Int = PixelFormat.TRANSLUCENT

        override fun draw(canvas: Canvas) {
            val b: Rect = bounds
            if (b.width() <= 0 || b.height() <= 0) return

            val effectiveColor = StormThemeEngine.withAlpha(
                tintColor,
                ((Color.alpha(tintColor) * customAlpha) / 255).coerceIn(0, 255)
            )
            strokePaint.color = effectiveColor
            fillPaint.color = effectiveColor

            val saveCount = canvas.save()
            val side = min(b.width(), b.height()).toFloat()
            val ox = b.left + (b.width() - side) * 0.5f
            val oy = b.top + (b.height() - side) * 0.5f
            canvas.translate(ox, oy)

            // Normalized 24x24 coordinate space
            val scale = side / 24f
            strokePaint.strokeWidth = strokeWidthPx

            fun x(v: Float): Float = v * scale
            fun y(v: Float): Float = v * scale

            val path = Path()
            val rect = RectF()

            when (iconKey) {
                GAMEPAD -> {
                    rect.set(x(2.5f), y(6.5f), x(21.5f), y(17.5f))
                    canvas.drawRoundRect(rect, x(5f), y(5f), strokePaint)
                    canvas.drawLine(x(6.5f), y(12f), x(10.5f), y(12f), strokePaint)
                    canvas.drawLine(x(8.5f), y(10f), x(8.5f), y(14f), strokePaint)
                    canvas.drawCircle(x(15.5f), y(10.8f), x(1.1f), fillPaint)
                    canvas.drawCircle(x(17.8f), y(13.0f), x(1.1f), fillPaint)
                }
                LIBRARY -> {
                    rect.set(x(3f), y(3.5f), x(10f), y(10.5f))
                    canvas.drawRoundRect(rect, x(1.8f), y(1.8f), strokePaint)
                    rect.set(x(14f), y(3.5f), x(21f), y(10.5f))
                    canvas.drawRoundRect(rect, x(1.8f), y(1.8f), strokePaint)
                    rect.set(x(3f), y(13.5f), x(10f), y(20.5f))
                    canvas.drawRoundRect(rect, x(1.8f), y(1.8f), strokePaint)
                    rect.set(x(14f), y(13.5f), x(21f), y(20.5f))
                    canvas.drawRoundRect(rect, x(1.8f), y(1.8f), strokePaint)
                }
                CORES -> {
                    rect.set(x(6.5f), y(6.5f), x(17.5f), y(17.5f))
                    canvas.drawRoundRect(rect, x(2f), y(2f), strokePaint)
                    rect.set(x(9.5f), y(9.5f), x(14.5f), y(14.5f))
                    canvas.drawRect(rect, strokePaint)
                    for (pin in listOf(9f, 12f, 15f)) {
                        canvas.drawLine(x(pin), y(3f), x(pin), y(6.2f), strokePaint)
                        canvas.drawLine(x(pin), y(17.8f), x(pin), y(21f), strokePaint)
                        canvas.drawLine(x(3f), y(pin), x(6.2f), y(pin), strokePaint)
                        canvas.drawLine(x(17.8f), y(pin), x(21f), y(pin), strokePaint)
                    }
                }
                CHEATS -> {
                    path.moveTo(x(13f), y(2.5f))
                    path.lineTo(x(5.5f), y(13.5f))
                    path.lineTo(x(11.5f), y(13.5f))
                    path.lineTo(x(10.5f), y(21.5f))
                    path.lineTo(x(18.5f), y(10.5f))
                    path.lineTo(x(12.5f), y(10.5f))
                    path.close()
                    canvas.drawPath(path, strokePaint)
                }
                BIOS -> {
                    rect.set(x(5f), y(4f), x(19f), y(20f))
                    canvas.drawRoundRect(rect, x(2f), y(2f), strokePaint)
                    canvas.drawCircle(x(9f), y(8f), x(1.2f), fillPaint)
                    canvas.drawLine(x(8.5f), y(12.5f), x(15.5f), y(12.5f), strokePaint)
                    canvas.drawLine(x(8.5f), y(16f), x(15.5f), y(16f), strokePaint)
                }
                BEZEL -> {
                    rect.set(x(2.5f), y(4.5f), x(21.5f), y(19.5f))
                    canvas.drawRoundRect(rect, x(2.4f), y(2.4f), strokePaint)
                    rect.set(x(6f), y(7.5f), x(18f), y(16.5f))
                    canvas.drawRoundRect(rect, x(1.2f), y(1.2f), strokePaint)
                }
                SETTINGS -> {
                    canvas.drawCircle(x(12f), y(12f), x(3.3f), strokePaint)
                    canvas.drawCircle(x(12f), y(12f), x(7.2f), strokePaint)
                    for (i in 0 until 8) {
                        val rad = Math.toRadians((i * 45).toDouble())
                        val x1 = 12f + (7.2f * cos(rad)).toFloat()
                        val y1 = 12f + (7.2f * sin(rad)).toFloat()
                        val x2 = 12f + (9.6f * cos(rad)).toFloat()
                        val y2 = 12f + (9.6f * sin(rad)).toFloat()
                        canvas.drawLine(x(x1), y(y1), x(x2), y(y2), strokePaint)
                    }
                }
                SEARCH -> {
                    canvas.drawCircle(x(10.5f), y(10.5f), x(6.2f), strokePaint)
                    canvas.drawLine(x(15.2f), y(15.2f), x(20.5f), y(20.5f), strokePaint)
                }
                PLAY -> {
                    path.moveTo(x(7f), y(4.5f))
                    path.lineTo(x(19.5f), y(12f))
                    path.lineTo(x(7f), y(19.5f))
                    path.close()
                    canvas.drawPath(path, fillPaint)
                }
                STAR -> {
                    val cx = 12f
                    val cy = 12.2f
                    val outerR = 8.6f
                    val innerR = 3.8f
                    for (i in 0 until 10) {
                        val r = if (i % 2 == 0) outerR else innerR
                        val angle = Math.toRadians((-90 + i * 36).toDouble())
                        val px = cx + (r * cos(angle)).toFloat()
                        val py = cy + (r * sin(angle)).toFloat()
                        if (i == 0) path.moveTo(x(px), y(py)) else path.lineTo(x(px), y(py))
                    }
                    path.close()
                    canvas.drawPath(path, strokePaint)
                }
                FOLDER -> {
                    path.moveTo(x(3f), y(6.5f))
                    path.lineTo(x(9.2f), y(6.5f))
                    path.lineTo(x(11.4f), y(9f))
                    path.lineTo(x(21f), y(9f))
                    path.lineTo(x(21f), y(19f))
                    path.lineTo(x(3f), y(19f))
                    path.close()
                    canvas.drawPath(path, strokePaint)
                }
                DOWNLOAD -> {
                    canvas.drawLine(x(12f), y(4f), x(12f), y(15.5f), strokePaint)
                    path.moveTo(x(7.5f), y(11.5f))
                    path.lineTo(x(12f), y(16f))
                    path.lineTo(x(16.5f), y(11.5f))
                    canvas.drawPath(path, strokePaint)
                    canvas.drawLine(x(5f), y(19.5f), x(19f), y(19.5f), strokePaint)
                }
                REFRESH -> {
                    rect.set(x(4.5f), y(4.5f), x(19.5f), y(19.5f))
                    canvas.drawArc(rect, -45f, 285f, false, strokePaint)
                    path.moveTo(x(15.5f), y(3.8f))
                    path.lineTo(x(19.6f), y(7.5f))
                    path.lineTo(x(14.8f), y(8.4f))
                    canvas.drawPath(path, strokePaint)
                }
                SAVE -> {
                    path.moveTo(x(4.5f), y(4f))
                    path.lineTo(x(16.8f), y(4f))
                    path.lineTo(x(20f), y(7.2f))
                    path.lineTo(x(20f), y(20f))
                    path.lineTo(x(4.5f), y(20f))
                    path.close()
                    canvas.drawPath(path, strokePaint)
                    rect.set(x(8f), y(4f), x(15.2f), y(9f))
                    canvas.drawRect(rect, strokePaint)
                    rect.set(x(7.5f), y(13.2f), x(16.5f), y(20f))
                    canvas.drawRect(rect, strokePaint)
                }
                LOAD -> {
                    rect.set(x(4f), y(11f), x(20f), y(20f))
                    canvas.drawRoundRect(rect, x(2f), y(2f), strokePaint)
                    canvas.drawLine(x(12f), y(15.5f), x(12f), y(4f), strokePaint)
                    path.moveTo(x(8f), y(8f))
                    path.lineTo(x(12f), y(4f))
                    path.lineTo(x(16f), y(8f))
                    canvas.drawPath(path, strokePaint)
                }
                REWIND -> {
                    path.moveTo(x(11.5f), y(6f))
                    path.lineTo(x(3.5f), y(12f))
                    path.lineTo(x(11.5f), y(18f))
                    path.close()
                    path.moveTo(x(20f), y(6f))
                    path.lineTo(x(12f), y(12f))
                    path.lineTo(x(20f), y(18f))
                    path.close()
                    canvas.drawPath(path, strokePaint)
                }
                FAST_FORWARD -> {
                    path.moveTo(x(4f), y(6f))
                    path.lineTo(x(12f), y(12f))
                    path.lineTo(x(4f), y(18f))
                    path.close()
                    path.moveTo(x(12.5f), y(6f))
                    path.lineTo(x(20.5f), y(12f))
                    path.lineTo(x(12.5f), y(18f))
                    path.close()
                    canvas.drawPath(path, strokePaint)
                }
                RAM -> {
                    rect.set(x(2.5f), y(7.5f), x(21.5f), y(15.5f))
                    canvas.drawRoundRect(rect, x(1.5f), y(1.5f), strokePaint)
                    rect.set(x(5.8f), y(9.8f), x(8.8f), y(13.2f))
                    canvas.drawRect(rect, fillPaint)
                    rect.set(x(10.5f), y(9.8f), x(13.5f), y(13.2f))
                    canvas.drawRect(rect, fillPaint)
                    rect.set(x(15.2f), y(9.8f), x(18.2f), y(13.2f))
                    canvas.drawRect(rect, fillPaint)
                    for (px in listOf(6f, 10f, 14f, 18f)) {
                        canvas.drawLine(x(px), y(15.6f), x(px), y(18.5f), strokePaint)
                    }
                }
                CAMERA -> {
                    path.moveTo(x(3f), y(8f))
                    path.lineTo(x(7.2f), y(8f))
                    path.lineTo(x(9f), y(5.5f))
                    path.lineTo(x(15f), y(5.5f))
                    path.lineTo(x(16.8f), y(8f))
                    path.lineTo(x(21f), y(8f))
                    path.lineTo(x(21f), y(19f))
                    path.lineTo(x(3f), y(19f))
                    path.close()
                    canvas.drawPath(path, strokePaint)
                    canvas.drawCircle(x(12f), y(13.5f), x(3.4f), strokePaint)
                }
                CLOSE -> {
                    canvas.drawLine(x(6f), y(6f), x(18f), y(18f), strokePaint)
                    canvas.drawLine(x(18f), y(6f), x(6f), y(18f), strokePaint)
                }
                SLIDERS -> {
                    canvas.drawLine(x(4f), y(7f), x(20f), y(7f), strokePaint)
                    canvas.drawCircle(x(9f), y(7f), x(2f), fillPaint)
                    canvas.drawLine(x(4f), y(12f), x(20f), y(12f), strokePaint)
                    canvas.drawCircle(x(15.5f), y(12f), x(2f), fillPaint)
                    canvas.drawLine(x(4f), y(17f), x(20f), y(17f), strokePaint)
                    canvas.drawCircle(x(10.5f), y(17f), x(2f), fillPaint)
                }
                CHECK -> {
                    path.moveTo(x(4.5f), y(12.5f))
                    path.lineTo(x(9.8f), y(17.8f))
                    path.lineTo(x(19.5f), y(6.8f))
                    canvas.drawPath(path, strokePaint)
                }
                SHIELD -> {
                    path.moveTo(x(12f), y(3f))
                    path.lineTo(x(19.5f), y(6.2f))
                    path.lineTo(x(19.5f), y(12.2f))
                    path.quadTo(x(19.5f), y(17.8f), x(12f), y(21f))
                    path.quadTo(x(4.5f), y(17.8f), x(4.5f), y(12.2f))
                    path.lineTo(x(4.5f), y(6.2f))
                    path.close()
                    canvas.drawPath(path, strokePaint)
                }
                CLOUD -> {
                    path.moveTo(x(6.5f), y(18f))
                    path.lineTo(x(17.8f), y(18f))
                    path.quadTo(x(21.2f), y(18f), x(21.2f), y(14.6f))
                    path.quadTo(x(21.2f), y(11.4f), x(18f), y(11.2f))
                    path.quadTo(x(17.2f), y(6.5f), x(12.4f), y(6.5f))
                    path.quadTo(x(8.2f), y(6.5f), x(7.2f), y(10.5f))
                    path.quadTo(x(3.2f), y(10.8f), x(3.2f), y(14.4f))
                    path.quadTo(x(3.2f), y(18f), x(6.5f), y(18f))
                    canvas.drawPath(path, strokePaint)
                }
                else -> {
                    canvas.drawCircle(x(12f), y(12f), x(7f), strokePaint)
                }
            }

            canvas.restoreToCount(saveCount)
        }
    }
}
