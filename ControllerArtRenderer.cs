using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace StormRetroArch;

/// <summary>
/// Генератор студийных аппаратных иллюстраций контроллеров (960x520)
/// для всех 12 аппаратных форм-факторов с поддержкой замороженного кэширования (Frozen Cache 0.0 ms)
/// и динамической подсветки активных кнопок.
/// </summary>
public static class ControllerArtRenderer
{
    public const int CanvasWidth = 960;
    public const int CanvasHeight = 520;

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    private static readonly object _cacheLock = new();
    private static readonly Dictionary<string, Bitmap> _baseLayerCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, BitmapSource> _highlightCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> _highlightLruQueue = new();
    private const int MaxHighlightCacheEntries = 32;

    public static readonly string[] PlatformProfiles =
    {
        "Xbox Series / XInput (ПК)",
        "Sony PlayStation (DualShock / DualSense)",
        "Nintendo Switch Pro Controller",
        "Аркадный стик (Arcade FightStick 8-Btn Vewlix)",
        "Бесрычажный аркадный (HitBox / Leverless)",
        "Авиационный джойстик и РУД (Flight Stick / HOTAS)",
        "Гоночный руль и педали (Racing Wheel & Pedals)",
        "Sega Mega Drive / 8BitDo M30 (6 кнопок)",
        "Super Nintendo (SNES)",
        "Nintendo NES / Dendy",
        "Nintendo 64",
        "MS-DOS Клавиатура и мышь ПК"
    };

    public static InputDeviceFormFactor ProfileNameToFormFactor(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName)) return InputDeviceFormFactor.XboxGamepad;
        string p = profileName.Trim();

        if (p.Contains("DualSense", StringComparison.OrdinalIgnoreCase) || p.Contains("PlayStation", StringComparison.OrdinalIgnoreCase))
            return InputDeviceFormFactor.PlayStationPad;
        if (p.Contains("Switch", StringComparison.OrdinalIgnoreCase))
            return InputDeviceFormFactor.NintendoPad;
        if (p.Contains("HitBox", StringComparison.OrdinalIgnoreCase) || p.Contains("Бесрычажный", StringComparison.OrdinalIgnoreCase) || p.Contains("Leverless", StringComparison.OrdinalIgnoreCase))
            return InputDeviceFormFactor.LeverlessHitBox;
        if (p.Contains("FightStick", StringComparison.OrdinalIgnoreCase) || p.Contains("Аркадный стик", StringComparison.OrdinalIgnoreCase))
            return InputDeviceFormFactor.ArcadeFightStick;
        if (p.Contains("HOTAS", StringComparison.OrdinalIgnoreCase) || p.Contains("РУД", StringComparison.OrdinalIgnoreCase) || p.Contains("Flight", StringComparison.OrdinalIgnoreCase))
            return InputDeviceFormFactor.FlightJoystickHotas;
        if (p.Contains("Wheel", StringComparison.OrdinalIgnoreCase) || p.Contains("Руль", StringComparison.OrdinalIgnoreCase))
            return InputDeviceFormFactor.RacingWheel;
        if (p.Contains("Mega Drive", StringComparison.OrdinalIgnoreCase) || p.Contains("Genesis", StringComparison.OrdinalIgnoreCase) || p.Contains("M30", StringComparison.OrdinalIgnoreCase) || p.Contains("6 кнопок", StringComparison.OrdinalIgnoreCase))
            return InputDeviceFormFactor.SegaSixButton;
        if (p.Contains("SNES", StringComparison.OrdinalIgnoreCase) || p.Contains("Super Nintendo", StringComparison.OrdinalIgnoreCase))
            return InputDeviceFormFactor.SnesPad;
        if (p.Contains("NES", StringComparison.OrdinalIgnoreCase) || p.Contains("Dendy", StringComparison.OrdinalIgnoreCase) || p.Contains("Famicom", StringComparison.OrdinalIgnoreCase))
            return InputDeviceFormFactor.NesPad;
        if (p.Contains("N64", StringComparison.OrdinalIgnoreCase) || p.Contains("Nintendo 64", StringComparison.OrdinalIgnoreCase))
            return InputDeviceFormFactor.N64Pad;
        if (p.Contains("DOS", StringComparison.OrdinalIgnoreCase) || p.Contains("Клавиатура", StringComparison.OrdinalIgnoreCase))
            return InputDeviceFormFactor.KeyboardMouse;

        return InputDeviceFormFactor.XboxGamepad;
    }

    public static string FormFactorToProfileName(InputDeviceFormFactor formFactor) => formFactor switch
    {
        InputDeviceFormFactor.XboxGamepad => "Xbox Series / XInput (ПК)",
        InputDeviceFormFactor.PlayStationPad => "Sony PlayStation (DualShock / DualSense)",
        InputDeviceFormFactor.NintendoPad => "Nintendo Switch Pro Controller",
        InputDeviceFormFactor.ArcadeFightStick => "Аркадный стик (Arcade FightStick 8-Btn Vewlix)",
        InputDeviceFormFactor.LeverlessHitBox => "Бесрычажный аркадный (HitBox / Leverless)",
        InputDeviceFormFactor.FlightJoystickHotas => "Авиационный джойстик и РУД (Flight Stick / HOTAS)",
        InputDeviceFormFactor.RacingWheel => "Гоночный руль и педали (Racing Wheel & Pedals)",
        InputDeviceFormFactor.SegaSixButton => "Sega Mega Drive / 8BitDo M30 (6 кнопок)",
        InputDeviceFormFactor.SnesPad => "Super Nintendo (SNES)",
        InputDeviceFormFactor.NesPad => "Nintendo NES / Dendy",
        InputDeviceFormFactor.N64Pad => "Nintendo 64",
        InputDeviceFormFactor.KeyboardMouse => "MS-DOS Клавиатура и мышь ПК",
        _ => "Xbox Series / XInput (ПК)"
    };

    public static string MapPlatformToControllerProfile(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return "Xbox Series / XInput (ПК)";
        string p = platform.Trim();

        if (p.Contains("NES", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("Famicom", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("Dendy", StringComparison.OrdinalIgnoreCase))
            return "Nintendo NES / Dendy";

        if (p.Contains("SNES", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("Super Nintendo", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("Super Famicom", StringComparison.OrdinalIgnoreCase))
            return "Super Nintendo (SNES)";

        if (p.Contains("Genesis", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("Mega Drive", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("Master System", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("Game Gear", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("Sega", StringComparison.OrdinalIgnoreCase))
            return "Sega Mega Drive / 8BitDo M30 (6 кнопок)";

        if (p.Contains("PlayStation", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("PS1", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("PS2", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("PSX", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("PSP", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("SwanStation", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("PCSX", StringComparison.OrdinalIgnoreCase))
            return "Sony PlayStation (DualShock / DualSense)";

        if (p.Contains("Switch", StringComparison.OrdinalIgnoreCase))
            return "Nintendo Switch Pro Controller";

        if (p.Contains("Nintendo 64", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("N64", StringComparison.OrdinalIgnoreCase))
            return "Nintendo 64";

        if (p.Contains("Game Boy", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("GBA", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("GBC", StringComparison.OrdinalIgnoreCase))
            return "Super Nintendo (SNES)";

        if (p.Contains("Arcade", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("FBNeo", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("MAME", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("Neo Geo", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("CPS", StringComparison.OrdinalIgnoreCase))
            return "Аркадный стик (Arcade FightStick 8-Btn Vewlix)";

        if (p.Contains("Flight", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("HOTAS", StringComparison.OrdinalIgnoreCase))
            return "Авиационный джойстик и РУД (Flight Stick / HOTAS)";

        if (p.Contains("Wheel", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("Racing", StringComparison.OrdinalIgnoreCase))
            return "Гоночный руль и педали (Racing Wheel & Pedals)";

        if (p.Contains("DOS", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("PC", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("Компьютер", StringComparison.OrdinalIgnoreCase))
            return "MS-DOS Клавиатура и мышь ПК";

        return "Xbox Series / XInput (ПК)";
    }

    public static BitmapSource CreateFrozenBitmapSource(Bitmap bmp)
    {
        IntPtr hBitmap = bmp.GetHbitmap();
        try
        {
            var bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bs.Freeze();
            return bs;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                DeleteObject(hBitmap);
            }
        }
    }

    public static BitmapSource RenderController(string profileName, int playerIndex = 1, string? activeRetroKey = null)
    {
        var formFactor = ProfileNameToFormFactor(profileName);
        return RenderController(formFactor, playerIndex, activeRetroKey);
    }

    public static BitmapSource RenderController(InputDeviceFormFactor formFactor, int playerIndex = 1, string? activeRetroKey = null, string? accentHex = null, double lx = 0, double ly = 0, double rx = 0, double ry = 0)
    {
        string normKey = (activeRetroKey ?? "").Trim().ToLowerInvariant();
        if (normKey.StartsWith("input_player"))
        {
            int lastUnderscore = normKey.LastIndexOf('_');
            if (lastUnderscore >= 0) normKey = normKey[(lastUnderscore + 1)..];
        }

        string safeAccent = string.IsNullOrWhiteSpace(accentHex) ? "def" : accentHex.Trim().ToLowerInvariant();
        int qLx = (int)Math.Round(lx * 10);
        int qLy = (int)Math.Round(ly * 10);
        int qRx = (int)Math.Round(rx * 10);
        int qRy = (int)Math.Round(ry * 10);
        string cacheKey = $"{(int)formFactor}_{safeAccent}_{playerIndex}_{normKey}_{qLx}_{qLy}_{qRx}_{qRy}";

        lock (_cacheLock)
        {
            if (_highlightCache.TryGetValue(cacheKey, out var cachedSource))
            {
                return cachedSource;
            }
        }

        string profileTitle = FormFactorToProfileName(formFactor);
        using var bmp = new Bitmap(CanvasWidth, CanvasHeight, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // 1. Studio Blueprint Background
            DrawStudioBackground(g);

            // 2. HUD Badges: Platform Profile & Player Indicator
            DrawPlatformBadge(g, profileTitle);
            DrawPlayerIndicator(g, playerIndex);

            // 3. Render Form Factor Hardware
            switch (formFactor)
            {
                case InputDeviceFormFactor.PlayStationPad:
                    DrawPlayStationController(g, normKey);
                    break;
                case InputDeviceFormFactor.NintendoPad:
                    DrawSwitchProController(g, normKey);
                    break;
                case InputDeviceFormFactor.ArcadeFightStick:
                    DrawArcadeStick(g, normKey);
                    break;
                case InputDeviceFormFactor.LeverlessHitBox:
                    DrawLeverlessHitBox(g, normKey);
                    break;
                case InputDeviceFormFactor.FlightJoystickHotas:
                    DrawFlightStickHotas(g, normKey);
                    break;
                case InputDeviceFormFactor.RacingWheel:
                    DrawRacingWheel(g, normKey);
                    break;
                case InputDeviceFormFactor.SegaSixButton:
                    DrawGenesisController(g, normKey);
                    break;
                case InputDeviceFormFactor.SnesPad:
                    DrawSnesController(g, normKey);
                    break;
                case InputDeviceFormFactor.NesPad:
                    DrawNesController(g, normKey);
                    break;
                case InputDeviceFormFactor.N64Pad:
                    DrawN64Controller(g, normKey);
                    break;
                case InputDeviceFormFactor.KeyboardMouse:
                    DrawMsDosController(g, normKey);
                    break;
                default:
                    DrawXboxController(g, normKey);
                    break;
            }
        }

        var frozen = CreateFrozenBitmapSource(bmp);

        lock (_cacheLock)
        {
            if (_highlightCache.Count >= MaxHighlightCacheEntries && _highlightLruQueue.Count > 0)
            {
                string oldKey = _highlightLruQueue.Dequeue();
                _highlightCache.Remove(oldKey);
            }
            _highlightCache[cacheKey] = frozen;
            _highlightLruQueue.Enqueue(cacheKey);
        }

        return frozen;
    }

    // ==================== BACKGROUND & HUD ====================

    private static void DrawStudioBackground(Graphics g)
    {
        using var bgBrush = new LinearGradientBrush(
            new Point(0, 0), new Point(0, CanvasHeight),
            Color.FromArgb(255, 18, 14, 36),
            Color.FromArgb(255, 26, 20, 50));
        g.FillRectangle(bgBrush, 0, 0, CanvasWidth, CanvasHeight);

        using (var auraPath = new GraphicsPath())
        {
            auraPath.AddEllipse(CanvasWidth / 2 - 380, CanvasHeight / 2 - 180, 760, 380);
            using var auraBrush = new PathGradientBrush(auraPath)
            {
                CenterColor = Color.FromArgb(45, 139, 92, 246),
                SurroundColors = new[] { Color.FromArgb(0, 18, 14, 36) }
            };
            g.FillPath(auraBrush, auraPath);
        }

        using var gridPen = new Pen(Color.FromArgb(14, 167, 139, 250), 1f);
        for (int x = 0; x < CanvasWidth; x += 32)
        {
            g.DrawLine(gridPen, x, 0, x, CanvasHeight);
        }
        for (int y = 0; y < CanvasHeight; y += 32)
        {
            g.DrawLine(gridPen, 0, y, CanvasWidth, y);
        }

        using var borderPen = new Pen(Color.FromArgb(50, 139, 92, 246), 1.5f);
        g.DrawRectangle(borderPen, 1, 1, CanvasWidth - 2, CanvasHeight - 2);
    }

    private static void DrawPlatformBadge(Graphics g, string profileName)
    {
        var badgeRect = new Rectangle(24, 20, 390, 36);
        using var path = CreateRoundedRect(badgeRect, 8);
        using var fill = new SolidBrush(Color.FromArgb(190, 22, 17, 44));
        using var stroke = new Pen(Color.FromArgb(80, 139, 92, 246), 1.2f);
        g.FillPath(fill, path);
        g.DrawPath(stroke, path);

        using var iconBrush = new SolidBrush(Color.FromArgb(255, 167, 139, 250));
        g.FillEllipse(iconBrush, 36, 31, 14, 14);

        using var font = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.White);
        g.DrawString(profileName.ToUpperInvariant(), font, textBrush, 58, 28);
    }

    private static void DrawPlayerIndicator(Graphics g, int playerIndex)
    {
        var barRect = new Rectangle(CanvasWidth - 210, 20, 186, 36);
        using var path = CreateRoundedRect(barRect, 8);
        using var fill = new SolidBrush(Color.FromArgb(190, 22, 17, 44));
        using var stroke = new Pen(Color.FromArgb(80, 139, 92, 246), 1.2f);
        g.FillPath(fill, path);
        g.DrawPath(stroke, path);

        using var font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var labelBrush = new SolidBrush(Color.FromArgb(200, 203, 213, 225));
        g.DrawString($"ИГРОК {playerIndex}", font, labelBrush, CanvasWidth - 200, 30);

        int ledStartX = CanvasWidth - 110;
        for (int i = 1; i <= 4; i++)
        {
            int cx = ledStartX + (i - 1) * 20;
            int cy = 34;
            bool isCurrent = (i == playerIndex || (playerIndex > 4 && i == ((playerIndex - 1) % 4 + 1)));

            if (isCurrent)
            {
                using var glowBrush = new SolidBrush(Color.FromArgb(80, 6, 182, 212));
                g.FillEllipse(glowBrush, cx - 7, cy - 7, 18, 18);

                using var ledBrush = new SolidBrush(Color.FromArgb(255, 34, 211, 238));
                g.FillEllipse(ledBrush, cx - 4, cy - 4, 12, 12);
            }
            else
            {
                using var ledBrush = new SolidBrush(Color.FromArgb(255, 51, 65, 85));
                g.FillEllipse(ledBrush, cx - 3, cy - 3, 10, 10);
            }
        }
    }

    // ==================== 1. XBOX SERIES / XINPUT ====================

    private static void DrawXboxController(Graphics g, string activeKey)
    {
        DrawShoulderTrigger(g, 255, 108, 96, 44, "LT / L2", activeKey is "l2" or "lt");
        DrawShoulderTrigger(g, 609, 108, 96, 44, "RT / R2", activeKey is "r2" or "rt");
        DrawShoulderBumper(g, 275, 148, 88, 32, "LB / L1", activeKey is "l" or "lb");
        DrawShoulderBumper(g, 597, 148, 88, 32, "RB / R1", activeKey is "r" or "rb");

        using var bodyPath = new GraphicsPath();
        bodyPath.AddBezier(new Point(340, 172), new Point(480, 162), new Point(480, 162), new Point(620, 172));
        bodyPath.AddBezier(new Point(620, 172), new Point(715, 195), new Point(765, 275), new Point(748, 412));
        bodyPath.AddBezier(new Point(748, 412), new Point(738, 446), new Point(700, 452), new Point(668, 418));
        bodyPath.AddBezier(new Point(668, 418), new Point(625, 362), new Point(565, 335), new Point(480, 335));
        bodyPath.AddBezier(new Point(480, 335), new Point(395, 335), new Point(335, 362), new Point(292, 418));
        bodyPath.AddBezier(new Point(292, 418), new Point(260, 452), new Point(222, 446), new Point(212, 412));
        bodyPath.AddBezier(new Point(212, 412), new Point(195, 275), new Point(245, 195), new Point(340, 172));
        bodyPath.CloseFigure();

        DrawPathDropShadow(g, bodyPath, 16);

        using (var shellBrush = new LinearGradientBrush(new Point(480, 135), new Point(480, 435),
            Color.FromArgb(255, 39, 39, 42),
            Color.FromArgb(255, 18, 18, 20)))
        {
            g.FillPath(shellBrush, bodyPath);
        }

        using (var rimPen = new Pen(Color.FromArgb(120, 148, 163, 184), 1.5f))
        {
            g.DrawPath(rimPen, bodyPath);
        }

        DrawAnalogThumbstick(g, 345, 250, 72, activeKey is "l3");
        DrawXboxHybridDPad(g, 415, 325, 76, activeKey);
        DrawAnalogThumbstick(g, 550, 325, 72, activeKey is "r3");

        int abxyCx = 615;
        int abxyCy = 250;
        int offset = 32;
        DrawActionButton(g, abxyCx, abxyCy + offset, 28, "A", Color.FromArgb(255, 16, 185, 129), activeKey is "a");
        DrawActionButton(g, abxyCx + offset, abxyCy, 28, "B", Color.FromArgb(255, 244, 63, 94), activeKey is "b");
        DrawActionButton(g, abxyCx - offset, abxyCy, 28, "X", Color.FromArgb(255, 56, 189, 248), activeKey is "x");
        DrawActionButton(g, abxyCx, abxyCy - offset, 28, "Y", Color.FromArgb(255, 251, 191, 36), activeKey is "y");

        DrawXboxJewelButton(g, 480, 205, 34, activeKey is "guide" or "home");
        DrawSmallRoundButton(g, 435, 235, 18, "⧉", activeKey is "select" or "back");
        DrawSmallRoundButton(g, 525, 235, 18, "☰", activeKey is "start");
        DrawShareButton(g, 480, 265, 14, 18, activeKey is "capture" or "share");
    }

    // ==================== 2. SONY DUALSENSE / PLAYSTATION ====================

    private static void DrawPlayStationController(Graphics g, string activeKey)
    {
        DrawShoulderTrigger(g, 260, 105, 96, 44, "L2", activeKey is "l2" or "lt");
        DrawShoulderTrigger(g, 604, 105, 96, 44, "R2", activeKey is "r2" or "rt");
        DrawShoulderBumper(g, 275, 145, 88, 30, "L1", activeKey is "l" or "lb");
        DrawShoulderBumper(g, 597, 145, 88, 30, "R1", activeKey is "r" or "rb");

        using var bodyPath = new GraphicsPath();
        bodyPath.AddBezier(new Point(335, 172), new Point(480, 168), new Point(480, 168), new Point(625, 172));
        bodyPath.AddBezier(new Point(625, 172), new Point(718, 192), new Point(765, 280), new Point(748, 425));
        bodyPath.AddBezier(new Point(748, 425), new Point(738, 458), new Point(700, 460), new Point(668, 422));
        bodyPath.AddBezier(new Point(668, 422), new Point(630, 365), new Point(570, 342), new Point(480, 342));
        bodyPath.AddBezier(new Point(480, 342), new Point(390, 342), new Point(330, 365), new Point(292, 422));
        bodyPath.AddBezier(new Point(292, 422), new Point(260, 460), new Point(222, 458), new Point(212, 425));
        bodyPath.AddBezier(new Point(212, 425), new Point(195, 280), new Point(242, 192), new Point(335, 172));
        bodyPath.CloseFigure();

        DrawPathDropShadow(g, bodyPath, 16);

        using (var whiteBrush = new LinearGradientBrush(new Point(480, 130), new Point(480, 440),
            Color.FromArgb(255, 245, 246, 250),
            Color.FromArgb(255, 203, 213, 225)))
        {
            g.FillPath(whiteBrush, bodyPath);
        }

        using (var corePath = new GraphicsPath())
        {
            corePath.AddEllipse(375, 260, 210, 130);
            using var coreBrush = new LinearGradientBrush(new Point(480, 270), new Point(480, 320),
                Color.FromArgb(255, 30, 41, 59),
                Color.FromArgb(255, 15, 23, 42));
            g.FillPath(coreBrush, corePath);
        }

        using (var rimPen = new Pen(Color.FromArgb(160, 255, 255, 255), 1.5f))
        {
            g.DrawPath(rimPen, bodyPath);
        }

        DrawDualSenseTouchpad(g, 480, 212);
        DrawPlayStationDPad(g, 335, 240, 80, activeKey);

        int abxyCx = 625;
        int abxyCy = 240;
        int offset = 32;
        DrawActionButton(g, abxyCx, abxyCy + offset, 28, "✕", Color.FromArgb(255, 96, 165, 250), activeKey is "b" or "a");
        DrawActionButton(g, abxyCx + offset, abxyCy, 28, "○", Color.FromArgb(255, 248, 113, 113), activeKey is "a" or "b");
        DrawActionButton(g, abxyCx - offset, abxyCy, 28, "□", Color.FromArgb(255, 244, 114, 182), activeKey is "y" or "x");
        DrawActionButton(g, abxyCx, abxyCy - offset, 28, "△", Color.FromArgb(255, 52, 211, 153), activeKey is "x" or "y");

        DrawAnalogThumbstick(g, 425, 325, 68, activeKey is "l3");
        DrawAnalogThumbstick(g, 535, 325, 68, activeKey is "r3");

        DrawPlayStationJewel(g, 480, 290, 26, activeKey is "guide" or "home");
        DrawSmallRoundButton(g, 395, 185, 16, "CREATE", activeKey is "select" or "back");
        DrawSmallRoundButton(g, 565, 185, 16, "OPTIONS", activeKey is "start");
    }

    // ==================== 3. NINTENDO SWITCH PRO CONTROLLER ====================

    private static void DrawSwitchProController(Graphics g, string activeKey)
    {
        DrawShoulderTrigger(g, 260, 110, 96, 42, "ZL", activeKey is "l2" or "lt");
        DrawShoulderTrigger(g, 604, 110, 96, 42, "ZR", activeKey is "r2" or "rt");
        DrawShoulderBumper(g, 275, 148, 88, 30, "L", activeKey is "l" or "lb");
        DrawShoulderBumper(g, 597, 148, 88, 30, "R", activeKey is "r" or "rb");

        using var bodyPath = new GraphicsPath();
        bodyPath.AddBezier(new Point(340, 175), new Point(480, 170), new Point(480, 170), new Point(620, 175));
        bodyPath.AddBezier(new Point(620, 175), new Point(715, 195), new Point(760, 275), new Point(745, 415));
        bodyPath.AddBezier(new Point(745, 415), new Point(735, 448), new Point(698, 452), new Point(665, 418));
        bodyPath.AddBezier(new Point(665, 418), new Point(625, 365), new Point(565, 340), new Point(480, 340));
        bodyPath.AddBezier(new Point(480, 340), new Point(395, 340), new Point(335, 365), new Point(295, 418));
        bodyPath.AddBezier(new Point(295, 418), new Point(262, 452), new Point(225, 448), new Point(215, 415));
        bodyPath.AddBezier(new Point(215, 415), new Point(200, 275), new Point(245, 195), new Point(340, 175));
        bodyPath.CloseFigure();

        DrawPathDropShadow(g, bodyPath, 16);

        // Translucent smoke-grey shell with soft inner circuitry hint
        using (var shellBrush = new LinearGradientBrush(new Point(480, 140), new Point(480, 440),
            Color.FromArgb(255, 45, 48, 56),
            Color.FromArgb(255, 20, 22, 26)))
        {
            g.FillPath(shellBrush, bodyPath);
        }

        using (var rimPen = new Pen(Color.FromArgb(90, 148, 163, 184), 1.5f))
        {
            g.DrawPath(rimPen, bodyPath);
        }

        // Asymmetrical thumbsticks (Left top, Right bottom)
        DrawAnalogThumbstick(g, 340, 250, 72, activeKey is "l3");
        DrawStandardDPad(g, 415, 325, 72, activeKey);
        DrawAnalogThumbstick(g, 550, 325, 72, activeKey is "r3");

        // Action Buttons: Nintendo Orientation (B bottom, A right, Y left, X top)
        int abxyCx = 620;
        int abxyCy = 250;
        int offset = 32;
        DrawActionButton(g, abxyCx, abxyCy + offset, 28, "B", Color.FromArgb(255, 55, 65, 81), activeKey is "b" or "a");
        DrawActionButton(g, abxyCx + offset, abxyCy, 28, "A", Color.FromArgb(255, 55, 65, 81), activeKey is "a" or "b");
        DrawActionButton(g, abxyCx - offset, abxyCy, 28, "Y", Color.FromArgb(255, 55, 65, 81), activeKey is "y" or "x");
        DrawActionButton(g, abxyCx, abxyCy - offset, 28, "X", Color.FromArgb(255, 55, 65, 81), activeKey is "x" or "y");

        // Minus, Plus, Capture, Home
        DrawSmallRoundButton(g, 435, 225, 16, "-", activeKey is "select");
        DrawSmallRoundButton(g, 525, 225, 16, "+", activeKey is "start");
        DrawSmallRoundButton(g, 445, 265, 14, "⌕", activeKey is "capture");
        DrawSmallRoundButton(g, 515, 265, 14, "⌂", activeKey is "home");

        // 4 Player LEDs at bottom
        int ledY = 360;
        for (int i = 0; i < 4; i++)
        {
            int lx = 450 + i * 20;
            using var ledBrush = new SolidBrush(i == 0 ? Color.FromArgb(255, 34, 197, 94) : Color.FromArgb(255, 51, 65, 85));
            g.FillEllipse(ledBrush, lx, ledY, 6, 6);
        }
    }

    // ==================== 4. АРКАДНЫЙ СТИК (VEWLIX 8-BTN) ====================

    private static void DrawArcadeStick(Graphics g, string activeKey)
    {
        int cx = CanvasWidth / 2;
        int cy = CanvasHeight / 2 + 35;
        int w = 640;
        int h = 270;

        var boxRect = new Rectangle(cx - w / 2, cy - h / 2, w, h);
        using var bodyPath = CreateRoundedRect(boxRect, 16);

        DrawPathDropShadow(g, bodyPath, 14);

        using (var grad = new LinearGradientBrush(new Point(cx, cy - h / 2), new Point(cx, cy + h / 2),
            Color.FromArgb(255, 34, 34, 40),
            Color.FromArgb(255, 16, 16, 20)))
        {
            g.FillPath(grad, bodyPath);
        }

        using (var pen = new Pen(Color.FromArgb(255, 82, 82, 91), 2.5f))
        {
            g.DrawPath(pen, bodyPath);
        }

        // Inner panel bezel
        var innerRect = new Rectangle(cx - w / 2 + 16, cy - h / 2 + 16, w - 32, h - 32);
        using (var inPath = CreateRoundedRect(innerRect, 10))
        {
            using var inFill = new SolidBrush(Color.FromArgb(255, 24, 24, 28));
            g.FillPath(inFill, inPath);
        }

        // Top service row
        DrawSmallRoundButton(g, cx - 180, cy - 90, 18, "SELECT", activeKey is "select");
        DrawSmallRoundButton(g, cx - 120, cy - 90, 18, "START", activeKey is "start");
        DrawSmallRoundButton(g, cx - 60, cy - 90, 18, "HOME", activeKey is "guide" or "home");
        DrawSmallRoundButton(g, cx, cy - 90, 18, "TURBO", activeKey is "turbo");

        // Sanwa Joystick Lever with octagonal arrow gates
        int joyX = cx - 170;
        int joyY = cy + 10;
        using (var washBrush = new SolidBrush(Color.FromArgb(255, 10, 10, 12)))
        {
            g.FillEllipse(washBrush, joyX - 45, joyY - 45, 90, 90);
        }
        using (var shaftPen = new Pen(Color.FromArgb(255, 200, 200, 210), 10f))
        {
            g.DrawLine(shaftPen, joyX, joyY, joyX, joyY - 30);
        }
        bool stickActive = activeKey is "up" or "down" or "left" or "right" or "stick";
        DrawActionButton(g, joyX, joyY - 35, 44, "", Color.FromArgb(255, 239, 68, 68), stickActive);

        // 8 Action Buttons (Vewlix 2x4 layout)
        int abBaseX = cx + 25;
        int abBaseY = cy + 10;
        DrawActionButton(g, abBaseX, abBaseY - 30, 28, "X", Color.FromArgb(255, 59, 130, 246), activeKey is "x");
        DrawActionButton(g, abBaseX + 55, abBaseY - 35, 28, "Y", Color.FromArgb(255, 234, 179, 8), activeKey is "y");
        DrawActionButton(g, abBaseX + 110, abBaseY - 30, 28, "RB", Color.FromArgb(255, 239, 68, 68), activeKey is "r" or "rb");
        DrawActionButton(g, abBaseX + 165, abBaseY - 20, 28, "LB", Color.FromArgb(255, 147, 51, 234), activeKey is "l" or "lb");

        DrawActionButton(g, abBaseX, abBaseY + 35, 28, "A", Color.FromArgb(255, 34, 197, 94), activeKey is "a");
        DrawActionButton(g, abBaseX + 55, abBaseY + 30, 28, "B", Color.FromArgb(255, 249, 115, 22), activeKey is "b");
        DrawActionButton(g, abBaseX + 110, abBaseY + 35, 28, "RT", Color.FromArgb(255, 239, 68, 68), activeKey is "r2" or "rt");
        DrawActionButton(g, abBaseX + 165, abBaseY + 45, 28, "LT", Color.FromArgb(255, 147, 51, 234), activeKey is "l2" or "lt");
    }

    // ==================== 5. БЕСРЫЧАЖНЫЙ АРКАДНЫЙ (HITBOX / LEVERLESS) ====================

    private static void DrawLeverlessHitBox(Graphics g, string activeKey)
    {
        int cx = CanvasWidth / 2;
        int cy = CanvasHeight / 2 + 35;
        int w = 640;
        int h = 270;

        var boxRect = new Rectangle(cx - w / 2, cy - h / 2, w, h);
        using var bodyPath = CreateRoundedRect(boxRect, 16);

        DrawPathDropShadow(g, bodyPath, 14);

        using (var grad = new LinearGradientBrush(new Point(cx, cy - h / 2), new Point(cx, cy + h / 2),
            Color.FromArgb(255, 28, 28, 34),
            Color.FromArgb(255, 14, 14, 18)))
        {
            g.FillPath(grad, bodyPath);
        }

        using (var pen = new Pen(Color.FromArgb(255, 148, 163, 184), 2f))
        {
            g.DrawPath(pen, bodyPath);
        }

        // Top service buttons
        DrawSmallRoundButton(g, cx - 180, cy - 90, 16, "SELECT", activeKey is "select");
        DrawSmallRoundButton(g, cx - 120, cy - 90, 16, "START", activeKey is "start");
        DrawSmallRoundButton(g, cx - 60, cy - 90, 16, "HOME", activeKey is "guide" or "home");
        DrawSmallRoundButton(g, cx, cy - 90, 16, "TOUCH", activeKey is "touchpad");

        // Left hand movement buttons (Left, Down, Right: 24mm; Up: 30mm thumb jump)
        DrawActionButton(g, cx - 180, cy + 5, 26, "◄", Color.FromArgb(255, 239, 68, 68), activeKey is "left");
        DrawActionButton(g, cx - 135, cy + 20, 26, "▼", Color.FromArgb(255, 239, 68, 68), activeKey is "down");
        DrawActionButton(g, cx - 90, cy + 15, 26, "►", Color.FromArgb(255, 239, 68, 68), activeKey is "right");
        DrawActionButton(g, cx - 30, cy + 68, 36, "▲", Color.FromArgb(255, 239, 68, 68), activeKey is "up");

        // Right hand action buttons (8 buttons)
        int abBaseX = cx + 55;
        int abBaseY = cy + 10;
        DrawActionButton(g, abBaseX, abBaseY - 30, 26, "X", Color.FromArgb(255, 59, 130, 246), activeKey is "x");
        DrawActionButton(g, abBaseX + 50, abBaseY - 35, 26, "Y", Color.FromArgb(255, 234, 179, 8), activeKey is "y");
        DrawActionButton(g, abBaseX + 100, abBaseY - 30, 26, "RB", Color.FromArgb(255, 239, 68, 68), activeKey is "r" or "rb");
        DrawActionButton(g, abBaseX + 150, abBaseY - 20, 26, "LB", Color.FromArgb(255, 147, 51, 234), activeKey is "l" or "lb");

        DrawActionButton(g, abBaseX, abBaseY + 35, 26, "A", Color.FromArgb(255, 34, 197, 94), activeKey is "a");
        DrawActionButton(g, abBaseX + 50, abBaseY + 30, 26, "B", Color.FromArgb(255, 249, 115, 22), activeKey is "b");
        DrawActionButton(g, abBaseX + 100, abBaseY + 35, 26, "RT", Color.FromArgb(255, 239, 68, 68), activeKey is "r2" or "rt");
        DrawActionButton(g, abBaseX + 150, abBaseY + 45, 26, "LT", Color.FromArgb(255, 147, 51, 234), activeKey is "l2" or "lt");
    }

    // ==================== 6. АВИАЦИОННЫЙ ДЖОЙСТИК И РУД (HOTAS) ====================

    private static void DrawFlightStickHotas(Graphics g, string activeKey)
    {
        int cx = CanvasWidth / 2;
        int cy = CanvasHeight / 2 + 35;

        // 1. Left Unit: Throttle Quadrant (РУД)
        int rudX = cx - 220;
        int rudY = cy;
        int rudW = 200;
        int rudH = 260;

        var rudRect = new Rectangle(rudX - rudW / 2, rudY - rudH / 2, rudW, rudH);
        using (var rudPath = CreateRoundedRect(rudRect, 14))
        {
            DrawPathDropShadow(g, rudPath, 12);
            using var grad = new LinearGradientBrush(new Point(rudX, rudY - rudH / 2), new Point(rudX, rudY + rudH / 2),
                Color.FromArgb(255, 36, 40, 48),
                Color.FromArgb(255, 18, 20, 24));
            g.FillPath(grad, rudPath);
            using var pen = new Pen(Color.FromArgb(255, 100, 116, 139), 2f);
            g.DrawPath(pen, rudPath);
        }

        // Throttle slide track
        using (var trackBrush = new SolidBrush(Color.FromArgb(255, 12, 14, 18)))
        {
            g.FillRectangle(trackBrush, rudX - 35, rudY - 90, 70, 180);
        }
        using (var markPen = new Pen(Color.FromArgb(140, 255, 255, 255), 1.5f))
        {
            for (int y = rudY - 80; y <= rudY + 80; y += 20)
            {
                g.DrawLine(markPen, rudX + 40, y, rudX + 55, y);
            }
        }
        using var rudFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var rudLabelBrush = new SolidBrush(Color.FromArgb(200, 203, 213, 225));
        g.DrawString("РУД (ТЯГА)", rudFont, rudLabelBrush, rudX - 32, rudY + 98);

        // Throttle Handle
        int handleY = rudY - 20;
        var handleRect = new Rectangle(rudX - 45, handleY - 25, 90, 50);
        using (var hPath = CreateRoundedRect(handleRect, 8))
        {
            using var hBrush = new LinearGradientBrush(new Point(rudX, handleY - 25), new Point(rudX, handleY + 25),
                Color.FromArgb(255, 51, 65, 85),
                Color.FromArgb(255, 30, 41, 59));
            g.FillPath(hBrush, hPath);
        }
        DrawActionButton(g, rudX - 25, handleY, 20, "T1", Color.FromArgb(255, 239, 68, 68), activeKey is "l2" or "lt");
        DrawActionButton(g, rudX + 25, handleY, 20, "T2", Color.FromArgb(255, 59, 130, 246), activeKey is "l" or "lb");

        // 2. Right Unit: Flight Stick (РУС)
        int rusX = cx + 180;
        int rusY = cy;
        int rusW = 240;
        int rusH = 260;

        var rusRect = new Rectangle(rusX - rusW / 2, rusY - rusH / 2, rusW, rusH);
        using (var rusPath = CreateRoundedRect(rusRect, 14))
        {
            DrawPathDropShadow(g, rusPath, 12);
            using var grad = new LinearGradientBrush(new Point(rusX, rusY - rusH / 2), new Point(rusX, rusY + rusH / 2),
                Color.FromArgb(255, 36, 40, 48),
                Color.FromArgb(255, 18, 20, 24));
            g.FillPath(grad, rusPath);
            using var pen = new Pen(Color.FromArgb(255, 100, 116, 139), 2f);
            g.DrawPath(pen, rusPath);
        }

        g.DrawString("РУС (ШТУРВАЛ)", rudFont, rudLabelBrush, rusX - 45, rusY + 98);

        // Accordion boot and Stick Head
        int stickX = rusX;
        int stickY = rusY - 10;
        using (var bootBrush = new SolidBrush(Color.FromArgb(255, 20, 24, 30)))
        {
            g.FillEllipse(bootBrush, stickX - 55, stickY + 10, 110, 60);
        }

        // Stick Grip
        var gripRect = new Rectangle(stickX - 35, stickY - 95, 70, 115);
        using (var gripPath = CreateRoundedRect(gripRect, 12))
        {
            using var gBrush = new LinearGradientBrush(new Point(stickX, stickY - 95), new Point(stickX, stickY + 20),
                Color.FromArgb(255, 51, 65, 85),
                Color.FromArgb(255, 30, 41, 59));
            g.FillPath(gBrush, gripPath);
        }

        // 8-Way POV Hat Switch
        DrawActionButton(g, stickX, stickY - 65, 28, "POV", Color.FromArgb(255, 234, 179, 8), activeKey is "up" or "down" or "left" or "right");

        // Primary Index Trigger & Missile Safety Button
        DrawActionButton(g, stickX - 25, stickY - 35, 18, "TRIG", Color.FromArgb(255, 239, 68, 68), activeKey is "a" or "b");
        DrawActionButton(g, stickX + 25, stickY - 35, 18, "FIRE", Color.FromArgb(255, 239, 68, 68), activeKey is "x" or "y");

        // Base toggle switches
        DrawSmallRoundButton(g, rusX - 70, rusY + 65, 16, "GEAR", activeKey is "select");
        DrawSmallRoundButton(g, rusX, rusY + 65, 16, "ARM", activeKey is "start");
        DrawSmallRoundButton(g, rusX + 70, rusY + 65, 16, "FLAPS", activeKey is "guide");
    }

    // ==================== 7. ГОНОЧНЫЙ РУЛЬ И ПЕДАЛИ ====================

    private static void DrawRacingWheel(Graphics g, string activeKey)
    {
        int cx = CanvasWidth / 2;
        int cy = CanvasHeight / 2 + 10;

        // 1. Steering Wheel Outer Rim (280mm)
        int wheelRadius = 135;
        DrawActiveGlowRing(g, cx, cy - 30, wheelRadius);

        using (var rimPen = new Pen(Color.FromArgb(255, 28, 30, 36), 34f))
        {
            g.DrawEllipse(rimPen, cx - wheelRadius, cy - 30 - wheelRadius, wheelRadius * 2, wheelRadius * 2);
        }

        // Red 12-o'clock centering stripe
        using (var redStripe = new Pen(Color.FromArgb(255, 239, 68, 68), 34f))
        {
            g.DrawArc(redStripe, cx - wheelRadius, cy - 30 - wheelRadius, wheelRadius * 2, wheelRadius * 2, 260, 20);
        }

        // Paddle Shifters behind wheel
        DrawActionButton(g, cx - 145, cy - 40, 32, "LB", Color.FromArgb(255, 148, 163, 184), activeKey is "l" or "lb");
        DrawActionButton(g, cx + 145, cy - 40, 32, "RB", Color.FromArgb(255, 148, 163, 184), activeKey is "r" or "rb");

        // Central Wheel Hub & Spokes
        using (var hubBrush = new LinearGradientBrush(new Point(cx, cy - 70), new Point(cx, cy + 10),
            Color.FromArgb(255, 45, 50, 60),
            Color.FromArgb(255, 22, 24, 28)))
        {
            g.FillEllipse(hubBrush, cx - 45, cy - 30 - 45, 90, 90);
        }

        // Shift Light RPM LED Strip (5 Green, 5 Yellow, 5 Red)
        int ledY = cy - 30 - 55;
        for (int i = 0; i < 9; i++)
        {
            int lx = cx - 40 + i * 10;
            Color c = i < 3 ? Color.FromArgb(255, 34, 197, 94) : (i < 6 ? Color.FromArgb(255, 234, 179, 8) : Color.FromArgb(255, 239, 68, 68));
            using var brush = new SolidBrush(c);
            g.FillEllipse(brush, lx, ledY, 6, 6);
        }

        // Spoke Buttons: D-Pad on Left spoke, ABXY on Right spoke
        DrawStandardDPad(g, cx - 80, cy - 30, 52, activeKey);
        DrawActionButton(g, cx + 80, cy - 45, 20, "Y", Color.FromArgb(255, 234, 179, 8), activeKey is "y");
        DrawActionButton(g, cx + 100, cy - 30, 20, "B", Color.FromArgb(255, 244, 63, 94), activeKey is "b");
        DrawActionButton(g, cx + 80, cy - 15, 20, "A", Color.FromArgb(255, 16, 185, 129), activeKey is "a");
        DrawActionButton(g, cx + 60, cy - 30, 20, "X", Color.FromArgb(255, 56, 189, 248), activeKey is "x");

        // 2. 3-Pedal Floor Unit at Bottom
        int pedY = cy + 155;
        var pedBase = new Rectangle(cx - 240, pedY - 30, 480, 75);
        using (var pPath = CreateRoundedRect(pedBase, 8))
        {
            using var pFill = new SolidBrush(Color.FromArgb(255, 30, 32, 38));
            g.FillPath(pFill, pPath);
        }

        // Clutch, Brake, Gas Pedals
        DrawKeyCap(g, cx - 160, pedY - 22, 60, 55, "СЦЕПЛЕНИЕ", activeKey is "l2");
        DrawKeyCap(g, cx - 30, pedY - 22, 60, 55, "ТОРМОЗ", activeKey is "l2" or "lt");
        DrawKeyCap(g, cx + 100, pedY - 22, 60, 55, "ГАЗ", activeKey is "r2" or "rt");
    }

    // ==================== 8. SEGA MEGA DRIVE / 8BITDO M30 ====================

    private static void DrawGenesisController(Graphics g, string activeKey)
    {
        using var bodyPath = new GraphicsPath();
        bodyPath.AddBezier(new Point(330, 210), new Point(480, 195), new Point(480, 195), new Point(630, 210));
        bodyPath.AddBezier(new Point(630, 210), new Point(725, 225), new Point(745, 335), new Point(675, 375));
        bodyPath.AddBezier(new Point(675, 375), new Point(610, 400), new Point(540, 360), new Point(480, 360));
        bodyPath.AddBezier(new Point(480, 360), new Point(420, 360), new Point(350, 400), new Point(285, 375));
        bodyPath.AddBezier(new Point(285, 375), new Point(215, 335), new Point(235, 225), new Point(330, 210));
        bodyPath.CloseFigure();

        DrawPathDropShadow(g, bodyPath, 14);

        using (var grad = new LinearGradientBrush(new Point(480, 200), new Point(480, 390),
            Color.FromArgb(255, 36, 36, 40),
            Color.FromArgb(255, 18, 18, 20)))
        {
            g.FillPath(grad, bodyPath);
        }

        using (var pen = new Pen(Color.FromArgb(255, 82, 82, 91), 2f))
        {
            g.DrawPath(pen, bodyPath);
        }

        DrawSegaDiscDPad(g, 345, 285, 96, activeKey);
        DrawAngledPillButton(g, 480, 250, 36, 16, "START", activeKey is "start");

        int btnX = 575;
        int btnY = 275;
        // Top 3 (X, Y, Z)
        DrawActionButton(g, btnX - 35, btnY - 24, 22, "X", Color.FromArgb(255, 39, 39, 42), activeKey is "y");
        DrawActionButton(g, btnX, btnY - 28, 22, "Y", Color.FromArgb(255, 39, 39, 42), activeKey is "x");
        DrawActionButton(g, btnX + 35, btnY - 24, 22, "Z", Color.FromArgb(255, 39, 39, 42), activeKey is "l");

        // Bottom 3 (A, B, C)
        DrawActionButton(g, btnX - 40, btnY + 22, 28, "A", Color.FromArgb(255, 24, 24, 27), activeKey is "b");
        DrawActionButton(g, btnX, btnY + 20, 28, "B", Color.FromArgb(255, 24, 24, 27), activeKey is "a");
        DrawActionButton(g, btnX + 40, btnY + 22, 28, "C", Color.FromArgb(255, 24, 24, 27), activeKey is "r");
    }

    // ==================== 9. SUPER NINTENDO (SNES) ====================

    private static void DrawSnesController(Graphics g, string activeKey)
    {
        DrawShoulderBumper(g, 305, 180, 80, 24, "L", activeKey is "l" or "lb");
        DrawShoulderBumper(g, 575, 180, 80, 24, "R", activeKey is "r" or "rb");

        using var bodyPath = new GraphicsPath();
        bodyPath.AddBezier(new Point(340, 202), new Point(480, 202), new Point(480, 202), new Point(620, 202));
        bodyPath.AddArc(620, 202, 160, 160, -90, 180);
        bodyPath.AddBezier(new Point(620, 362), new Point(480, 362), new Point(480, 362), new Point(340, 362));
        bodyPath.AddArc(180, 202, 160, 160, 90, 180);
        bodyPath.CloseFigure();

        DrawPathDropShadow(g, bodyPath, 14);

        using (var grad = new LinearGradientBrush(new Point(480, 190), new Point(480, 370),
            Color.FromArgb(255, 228, 228, 231),
            Color.FromArgb(255, 180, 180, 188)))
        {
            g.FillPath(grad, bodyPath);
        }

        using (var pen = new Pen(Color.FromArgb(255, 120, 125, 138), 2f))
        {
            g.DrawPath(pen, bodyPath);
        }

        DrawStandardDPad(g, 260, 282, 84, activeKey);

        DrawAngledPillButton(g, 435, 285, 32, 12, "SELECT", activeKey is "select");
        DrawAngledPillButton(g, 485, 285, 32, 12, "START", activeKey is "start");

        int abxyCx = 700;
        int abxyCy = 282;
        int offset = 28;
        DrawActionButton(g, abxyCx, abxyCy + offset, 26, "B", Color.FromArgb(255, 234, 179, 8), activeKey is "b");
        DrawActionButton(g, abxyCx + offset, abxyCy, 26, "A", Color.FromArgb(255, 239, 68, 68), activeKey is "a");
        DrawActionButton(g, abxyCx - offset, abxyCy, 26, "Y", Color.FromArgb(255, 34, 197, 94), activeKey is "y");
        DrawActionButton(g, abxyCx, abxyCy - offset, 26, "X", Color.FromArgb(255, 59, 130, 246), activeKey is "x");
    }

    // ==================== 10. NINTENDO NES / DENDY ====================

    private static void DrawNesController(Graphics g, string activeKey)
    {
        int cx = CanvasWidth / 2;
        int cy = CanvasHeight / 2 + 25;
        int w = 520;
        int h = 210;

        var chassisRect = new Rectangle(cx - w / 2, cy - h / 2, w, h);
        using var bodyPath = CreateRoundedRect(chassisRect, 14);

        DrawPathDropShadow(g, bodyPath, 14);

        using (var grad = new LinearGradientBrush(new Point(cx, cy - h / 2), new Point(cx, cy + h / 2),
            Color.FromArgb(255, 218, 220, 224),
            Color.FromArgb(255, 160, 165, 175)))
        {
            g.FillPath(grad, bodyPath);
        }

        using (var pen = new Pen(Color.FromArgb(255, 100, 110, 125), 2f))
        {
            g.DrawPath(pen, bodyPath);
        }

        var bezelRect = new Rectangle(cx - w / 2 + 16, cy - h / 2 + 16, w - 32, h - 32);
        using (var bPath = CreateRoundedRect(bezelRect, 8))
        {
            using var bFill = new SolidBrush(Color.FromArgb(255, 35, 37, 44));
            g.FillPath(bFill, bPath);
        }

        DrawStandardDPad(g, cx - 165, cy, 84, activeKey);

        DrawRubberPillButton(g, cx - 45, cy + 30, 36, 12, "SELECT", activeKey is "select");
        DrawRubberPillButton(g, cx + 15, cy + 30, 36, 12, "START", activeKey is "start");

        int btnX = cx + 140;
        int btnY = cy + 25;
        DrawActionButton(g, btnX - 35, btnY, 26, "B", Color.FromArgb(255, 220, 38, 38), activeKey is "b");
        DrawActionButton(g, btnX + 35, btnY, 26, "A", Color.FromArgb(255, 220, 38, 38), activeKey is "a");
    }

    // ==================== 11. NINTENDO 64 ====================

    private static void DrawN64Controller(Graphics g, string activeKey)
    {
        int cx = CanvasWidth / 2;
        int cy = CanvasHeight / 2 + 25;

        using var bodyPath = new GraphicsPath();
        bodyPath.AddBezier(new Point(cx - 190, cy - 80), new Point(cx, cy - 90), new Point(cx, cy - 90), new Point(cx + 190, cy - 80));
        bodyPath.AddBezier(new Point(cx + 190, cy - 80), new Point(cx + 250, cy + 50), new Point(cx + 200, cy + 180), new Point(cx + 140, cy + 180));
        bodyPath.AddBezier(new Point(cx + 140, cy + 180), new Point(cx + 90, cy + 150), new Point(cx + 90, cy + 70), new Point(cx + 45, cy + 70));
        bodyPath.AddBezier(new Point(cx + 45, cy + 70), new Point(cx + 45, cy + 175), new Point(cx + 20, cy + 195), new Point(cx, cy + 195));
        bodyPath.AddBezier(new Point(cx, cy + 195), new Point(cx - 20, cy + 195), new Point(cx - 45, cy + 175), new Point(cx - 45, cy + 70));
        bodyPath.AddBezier(new Point(cx - 45, cy + 70), new Point(cx - 90, cy + 70), new Point(cx - 90, cy + 150), new Point(cx - 140, cy + 180));
        bodyPath.AddBezier(new Point(cx - 140, cy + 180), new Point(cx - 200, cy + 180), new Point(cx - 250, cy + 50), new Point(cx - 190, cy - 80));
        bodyPath.CloseFigure();

        DrawPathDropShadow(g, bodyPath, 16);

        using (var grad = new LinearGradientBrush(new Point(cx, cy - 90), new Point(cx, cy + 195),
            Color.FromArgb(255, 148, 163, 184),
            Color.FromArgb(255, 71, 85, 105)))
        {
            g.FillPath(grad, bodyPath);
        }

        using (var pen = new Pen(Color.FromArgb(255, 30, 41, 59), 2f))
        {
            g.DrawPath(pen, bodyPath);
        }

        DrawStandardDPad(g, cx - 140, cy - 10, 72, activeKey);
        DrawN64OctagonalGate(g, cx, cy + 30, 68, activeKey is "stick" or "l3");
        DrawSmallRoundButton(g, cx, cy - 25, 22, "START", activeKey is "start", Color.FromArgb(255, 220, 38, 38));

        int rX = cx + 140;
        int rY = cy - 10;
        DrawActionButton(g, rX - 25, rY + 30, 24, "B", Color.FromArgb(255, 34, 197, 94), activeKey is "b");
        DrawActionButton(g, rX + 15, rY + 45, 24, "A", Color.FromArgb(255, 59, 130, 246), activeKey is "a");

        int cOffset = 22;
        DrawActionButton(g, rX, rY - cOffset, 18, "▲", Color.FromArgb(255, 234, 179, 8), activeKey is "x");
        DrawActionButton(g, rX + cOffset, rY, 18, "►", Color.FromArgb(255, 234, 179, 8), activeKey is "r2");
        DrawActionButton(g, rX, rY + cOffset, 18, "▼", Color.FromArgb(255, 234, 179, 8), activeKey is "r");
        DrawActionButton(g, rX - cOffset, rY, 18, "◄", Color.FromArgb(255, 234, 179, 8), activeKey is "y");
    }

    // ==================== 12. MS-DOS КЛАВИАТУРА И МЫШЬ ПК ====================

    private static void DrawMsDosController(Graphics g, string activeKey)
    {
        int cx = CanvasWidth / 2;
        int cy = CanvasHeight / 2 + 35;

        // Mechanical Keyboard Cluster
        int kbX = cx - 310;
        int kbY = cy - 110;
        int kbW = 380;
        int kbH = 230;

        var kbRect = new Rectangle(kbX, kbY, kbW, kbH);
        using (var path = CreateRoundedRect(kbRect, 12))
        {
            DrawPathDropShadow(g, path, 10);
            using var grad = new LinearGradientBrush(new Point(kbX, kbY), new Point(kbX, kbY + kbH),
                Color.FromArgb(255, 39, 39, 42),
                Color.FromArgb(255, 24, 24, 27));
            g.FillPath(grad, path);

            using var pen = new Pen(Color.FromArgb(255, 82, 82, 91), 1.5f);
            g.DrawPath(pen, path);
        }

        DrawKeyCap(g, kbX + 15, kbY + 15, 34, 28, "ESC", activeKey is "select" or "esc");
        DrawKeyCap(g, kbX + 55, kbY + 15, 34, 28, "F1", activeKey is "f1");
        DrawKeyCap(g, kbX + 95, kbY + 15, 34, 28, "F2", activeKey is "f2");
        DrawKeyCap(g, kbX + 135, kbY + 15, 34, 28, "F4", activeKey is "f4");
        DrawKeyCap(g, kbX + 175, kbY + 15, 34, 28, "F11", activeKey is "f11");
        DrawKeyCap(g, kbX + 225, kbY + 15, 65, 28, "BACK", activeKey is "back" or "backspace");
        DrawKeyCap(g, kbX + 300, kbY + 15, 65, 28, "ENTER", activeKey is "start" or "return" or "enter");

        // WASD & Action Keys
        DrawKeyCap(g, kbX + 65, kbY + 55, 34, 28, "W", activeKey is "up" or "w");
        DrawKeyCap(g, kbX + 25, kbY + 90, 34, 28, "A", activeKey is "left" or "a");
        DrawKeyCap(g, kbX + 65, kbY + 90, 34, 28, "S", activeKey is "down" or "s");
        DrawKeyCap(g, kbX + 105, kbY + 90, 34, 28, "D", activeKey is "right" or "d");

        // Directional Arrows
        DrawKeyCap(g, kbX + 250, kbY + 55, 34, 28, "▲", activeKey is "up");
        DrawKeyCap(g, kbX + 210, kbY + 90, 34, 28, "◄", activeKey is "left");
        DrawKeyCap(g, kbX + 250, kbY + 90, 34, 28, "▼", activeKey is "down");
        DrawKeyCap(g, kbX + 290, kbY + 90, 34, 28, "►", activeKey is "right");

        DrawKeyCap(g, kbX + 20, kbY + 140, 48, 32, "CTRL", activeKey is "b" or "ctrl");
        DrawKeyCap(g, kbX + 75, kbY + 140, 48, 32, "ALT", activeKey is "y" or "alt");
        DrawKeyCap(g, kbX + 130, kbY + 140, 150, 32, "ПРОБЕЛ (FIRE)", activeKey is "a" or "space");
        DrawKeyCap(g, kbX + 290, kbY + 140, 75, 32, "SHIFT", activeKey is "shift");

        // Ergonomic Gaming Mouse Silhouette
        int mouseX = cx + 180;
        int mouseY = cy;
        int mouseW = 120;
        int mouseH = 190;

        var mRect = new Rectangle(mouseX - mouseW / 2, mouseY - mouseH / 2, mouseW, mouseH);
        using (var mPath = CreateRoundedRect(mRect, 38))
        {
            DrawPathDropShadow(g, mPath, 12);
            using var grad = new LinearGradientBrush(new Point(mouseX, mouseY - mouseH / 2), new Point(mouseX, mouseY + mouseH / 2),
                Color.FromArgb(255, 39, 39, 42),
                Color.FromArgb(255, 20, 20, 24));
            g.FillPath(grad, mPath);

            using var pen = new Pen(Color.FromArgb(255, 148, 163, 184), 1.5f);
            g.DrawPath(pen, mPath);
        }

        // Mouse buttons split
        using (var linePen = new Pen(Color.FromArgb(255, 71, 85, 105), 1.5f))
        {
            g.DrawLine(linePen, mouseX, mouseY - mouseH / 2, mouseX, mouseY - 15);
        }

        // Scroll Wheel
        var wheelRect = new Rectangle(mouseX - 8, mouseY - 65, 16, 34);
        using (var wFill = new SolidBrush(activeKey is "middle" ? Color.FromArgb(255, 34, 211, 238) : Color.FromArgb(255, 82, 82, 91)))
        {
            g.FillRectangle(wFill, wheelRect);
        }

        using var font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var brush = new SolidBrush(Color.FromArgb(200, 203, 213, 225));
        g.DrawString("ЛКМ", font, brush, mouseX - 35, mouseY - 45);
        g.DrawString("ПКМ", font, brush, mouseX + 12, mouseY - 45);
    }

    // ==================== REUSABLE INDUSTRIAL COMPONENT RENDERERS ====================

    private static void DrawPathDropShadow(Graphics g, GraphicsPath path, int offsetY)
    {
        g.TranslateTransform(0, offsetY);
        using (var shadowPen = new Pen(Color.FromArgb(40, 0, 0, 0), 12f) { LineJoin = LineJoin.Round })
        {
            g.DrawPath(shadowPen, path);
        }
        using (var shadowBrush = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
        {
            g.FillPath(shadowBrush, path);
        }
        g.TranslateTransform(0, -offsetY);
    }

    private static void DrawShoulderBumper(Graphics g, int x, int y, int w, int h, string label, bool isActive)
    {
        var rect = new Rectangle(x, y, w, h);
        using var path = CreateRoundedRect(rect, 8);
        using var brush = new LinearGradientBrush(new Point(x, y), new Point(x, y + h),
            isActive ? Color.FromArgb(255, 167, 139, 250) : Color.FromArgb(255, 71, 85, 105),
            isActive ? Color.FromArgb(255, 124, 58, 237) : Color.FromArgb(255, 30, 41, 59));
        g.FillPath(brush, path);

        using var pen = new Pen(isActive ? Color.FromArgb(255, 196, 181, 253) : Color.FromArgb(255, 100, 116, 139), 1.5f);
        g.DrawPath(pen, path);

        using var font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var textBrush = new SolidBrush(isActive ? Color.White : Color.FromArgb(220, 226, 232, 240));
        var sz = g.MeasureString(label, font);
        g.DrawString(label, font, textBrush, x + (w - sz.Width) / 2, y + (h - sz.Height) / 2);
    }

    private static void DrawShoulderTrigger(Graphics g, int x, int y, int w, int h, string label, bool isActive)
    {
        var rect = new Rectangle(x, y, w, h);
        using var path = CreateRoundedRect(rect, 10);
        using var brush = new LinearGradientBrush(new Point(x, y), new Point(x, y + h),
            isActive ? Color.FromArgb(255, 192, 132, 252) : Color.FromArgb(255, 51, 65, 85),
            isActive ? Color.FromArgb(255, 147, 51, 234) : Color.FromArgb(255, 15, 23, 42));
        g.FillPath(brush, path);

        using var pen = new Pen(isActive ? Color.FromArgb(255, 216, 180, 254) : Color.FromArgb(255, 71, 85, 105), 1.5f);
        g.DrawPath(pen, path);

        using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var textBrush = new SolidBrush(isActive ? Color.White : Color.FromArgb(200, 203, 213, 225));
        var sz = g.MeasureString(label, font);
        g.DrawString(label, font, textBrush, x + (w - sz.Width) / 2, y + 6);
    }

    private static void DrawAnalogThumbstick(Graphics g, int cx, int cy, int diameter, bool isActive)
    {
        int r = diameter / 2;

        if (isActive)
        {
            DrawActiveGlowRing(g, cx, cy, r + 8);
        }

        using (var wellBrush = new LinearGradientBrush(new Point(cx - r - 4, cy - r - 4), new Point(cx + r + 4, cy + r + 4),
            Color.FromArgb(255, 15, 23, 42),
            Color.FromArgb(255, 51, 65, 85)))
        {
            g.FillEllipse(wellBrush, cx - r - 4, cy - r - 4, diameter + 8, diameter + 8);
        }

        using (var capBrush = new LinearGradientBrush(new Point(cx - r, cy - r), new Point(cx + r, cy + r),
            isActive ? Color.FromArgb(255, 124, 58, 237) : Color.FromArgb(255, 51, 65, 85),
            isActive ? Color.FromArgb(255, 76, 29, 149) : Color.FromArgb(255, 15, 23, 42)))
        {
            g.FillEllipse(capBrush, cx - r, cy - r, diameter, diameter);
        }

        using (var ringPen = new Pen(isActive ? Color.FromArgb(255, 196, 181, 253) : Color.FromArgb(255, 71, 85, 105), 2f))
        {
            g.DrawEllipse(ringPen, cx - r + 8, cy - r + 8, diameter - 16, diameter - 16);
        }

        using (var crossPen = new Pen(isActive ? Color.FromArgb(255, 221, 214, 254) : Color.FromArgb(255, 100, 116, 139), 1.5f))
        {
            g.DrawLine(crossPen, cx - 6, cy, cx + 6, cy);
            g.DrawLine(crossPen, cx, cy - 6, cx, cy + 6);
        }
    }

    private static void DrawStandardDPad(Graphics g, int cx, int cy, int size, string activeKey)
    {
        int arm = size / 3;
        int half = size / 2;

        using var path = new GraphicsPath();
        path.AddPolygon(new[]
        {
            new Point(cx - arm / 2, cy - half),
            new Point(cx + arm / 2, cy - half),
            new Point(cx + arm / 2, cy - arm / 2),
            new Point(cx + half, cy - arm / 2),
            new Point(cx + half, cy + arm / 2),
            new Point(cx + arm / 2, cy + arm / 2),
            new Point(cx + arm / 2, cy + half),
            new Point(cx - arm / 2, cy + half),
            new Point(cx - arm / 2, cy + arm / 2),
            new Point(cx - half, cy + arm / 2),
            new Point(cx - half, cy - arm / 2),
            new Point(cx - arm / 2, cy - arm / 2)
        });

        using (var grad = new LinearGradientBrush(new Point(cx - half, cy - half), new Point(cx + half, cy + half),
            Color.FromArgb(255, 51, 65, 85),
            Color.FromArgb(255, 15, 23, 42)))
        {
            g.FillPath(grad, path);
        }

        using (var pen = new Pen(Color.FromArgb(255, 100, 116, 139), 1.5f))
        {
            g.DrawPath(pen, path);
        }

        DrawDPadIndicator(g, cx, cy - arm, "▲", activeKey is "up");
        DrawDPadIndicator(g, cx, cy + arm, "▼", activeKey is "down");
        DrawDPadIndicator(g, cx - arm, cy, "◄", activeKey is "left");
        DrawDPadIndicator(g, cx + arm, cy, "►", activeKey is "right");
    }

    private static void DrawDPadIndicator(Graphics g, int x, int y, string arrow, bool isActive)
    {
        using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var brush = new SolidBrush(isActive ? Color.FromArgb(255, 236, 72, 153) : Color.FromArgb(200, 148, 163, 184));
        var sz = g.MeasureString(arrow, font);
        g.DrawString(arrow, font, brush, x - sz.Width / 2, y - sz.Height / 2);
    }

    private static void DrawXboxHybridDPad(Graphics g, int cx, int cy, int diameter, string activeKey)
    {
        int r = diameter / 2;
        using (var dishBrush = new LinearGradientBrush(new Point(cx - r, cy - r), new Point(cx + r, cy + r),
            Color.FromArgb(255, 30, 41, 59),
            Color.FromArgb(255, 15, 23, 42)))
        {
            g.FillEllipse(dishBrush, cx - r, cy - r, diameter, diameter);
        }

        using (var dishPen = new Pen(Color.FromArgb(255, 71, 85, 105), 1.5f))
        {
            g.DrawEllipse(dishPen, cx - r, cy - r, diameter, diameter);
        }

        DrawStandardDPad(g, cx, cy, (int)(diameter * 0.85f), activeKey);
    }

    private static void DrawPlayStationDPad(Graphics g, int cx, int cy, int size, string activeKey)
    {
        int offset = size / 3;
        int btnR = size / 5;

        DrawSegmentedDpadButton(g, cx, cy - offset, btnR, "▲", activeKey is "up");
        DrawSegmentedDpadButton(g, cx, cy + offset, btnR, "▼", activeKey is "down");
        DrawSegmentedDpadButton(g, cx - offset, cy, btnR, "◄", activeKey is "left");
        DrawSegmentedDpadButton(g, cx + offset, cy, btnR, "►", activeKey is "right");
    }

    private static void DrawSegmentedDpadButton(Graphics g, int x, int y, int r, string symbol, bool isActive)
    {
        using (var fill = new SolidBrush(isActive ? Color.FromArgb(255, 124, 58, 237) : Color.FromArgb(255, 30, 41, 59)))
        {
            g.FillEllipse(fill, x - r, y - r, r * 2, r * 2);
        }

        using (var pen = new Pen(isActive ? Color.FromArgb(255, 236, 72, 153) : Color.FromArgb(255, 71, 85, 105), 1.5f))
        {
            g.DrawEllipse(pen, x - r, y - r, r * 2, r * 2);
        }

        using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var brush = new SolidBrush(Color.White);
        var sz = g.MeasureString(symbol, font);
        g.DrawString(symbol, font, brush, x - sz.Width / 2, y - sz.Height / 2);
    }

    private static void DrawSegaDiscDPad(Graphics g, int cx, int cy, int diameter, string activeKey)
    {
        int r = diameter / 2;
        using (var grad = new LinearGradientBrush(new Point(cx - r, cy - r), new Point(cx + r, cy + r),
            Color.FromArgb(255, 45, 45, 50),
            Color.FromArgb(255, 20, 20, 24)))
        {
            g.FillEllipse(grad, cx - r, cy - r, diameter, diameter);
        }

        using (var pen = new Pen(Color.FromArgb(255, 82, 82, 91), 2f))
        {
            g.DrawEllipse(pen, cx - r, cy - r, diameter, diameter);
        }

        DrawStandardDPad(g, cx, cy, (int)(diameter * 0.75f), activeKey);
    }

    private static void DrawN64OctagonalGate(Graphics g, int cx, int cy, int size, bool isActive)
    {
        int r = size / 2;
        var pts = new Point[8];
        for (int i = 0; i < 8; i++)
        {
            double angle = (i * 45 - 22.5) * Math.PI / 180.0;
            pts[i] = new Point((int)(cx + r * Math.Cos(angle)), (int)(cy + r * Math.Sin(angle)));
        }

        using (var gateBrush = new SolidBrush(Color.FromArgb(255, 28, 32, 44)))
        {
            g.FillPolygon(gateBrush, pts);
        }
        using (var gatePen = new Pen(Color.FromArgb(255, 80, 92, 115), 2f))
        {
            g.DrawPolygon(gatePen, pts);
        }

        DrawAnalogThumbstick(g, cx, cy, (int)(size * 0.72f), isActive);
    }

    private static void DrawDualSenseTouchpad(Graphics g, int cx, int cy)
    {
        int w = 150;
        int h = 90;
        var padRect = new Rectangle(cx - w / 2, cy - h / 2, w, h);
        using var path = CreateRoundedRect(padRect, 10);

        using (var padBrush = new LinearGradientBrush(new Point(cx, cy - 55), new Point(cx, cy + 30),
            Color.FromArgb(255, 220, 225, 235),
            Color.FromArgb(255, 185, 195, 210)))
        {
            g.FillPath(padBrush, path);
        }

        using (var padPen = new Pen(Color.FromArgb(255, 75, 85, 105), 1.5f))
        {
            g.DrawPath(padPen, path);
        }

        // Lightbar glow line around touchpad
        using var glowPen = new Pen(Color.FromArgb(180, 56, 189, 248), 3f);
        g.DrawPath(glowPen, path);
    }

    private static Color LightenColor(Color c, float factor)
    {
        int r = (int)(c.R + (255 - c.R) * factor);
        int g = (int)(c.G + (255 - c.G) * factor);
        int b = (int)(c.B + (255 - c.B) * factor);
        return Color.FromArgb(c.A, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
    }

    private static Color DarkenColor(Color c, float factor)
    {
        int r = (int)(c.R * (1f - factor));
        int g = (int)(c.G * (1f - factor));
        int b = (int)(c.B * (1f - factor));
        return Color.FromArgb(c.A, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
    }

    private static void DrawActionButton(Graphics g, int x, int y, int diameter, string legend, Color baseColor, bool isActive)
    {
        int r = diameter / 2;

        if (isActive)
        {
            DrawActiveGlowRing(g, x, y, r + 6);
        }

        using (var rimBrush = new SolidBrush(Color.FromArgb(255, 24, 24, 27)))
        {
            g.FillEllipse(rimBrush, x - r - 2, y - r - 2, diameter + 4, diameter + 4);
        }

        Color topColor = isActive ? Color.White : LightenColor(baseColor, 0.3f);
        Color botColor = isActive ? baseColor : DarkenColor(baseColor, 0.3f);

        using (var domeBrush = new LinearGradientBrush(new Point(x - r, y - r), new Point(x + r, y + r), topColor, botColor))
        {
            g.FillEllipse(domeBrush, x - r, y - r, diameter, diameter);
        }

        using (var glintBrush = new SolidBrush(Color.FromArgb(100, 255, 255, 255)))
        {
            g.FillEllipse(glintBrush, x - r + 3, y - r + 3, diameter - 6, (diameter - 6) / 2);
        }

        if (!string.IsNullOrEmpty(legend))
        {
            using var font = new Font("Segoe UI", Math.Max(8f, diameter * 0.42f), FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.White);
            var sz = g.MeasureString(legend, font);
            g.DrawString(legend, font, textBrush, x - sz.Width / 2, y - sz.Height / 2);
        }
    }

    private static void DrawXboxJewelButton(Graphics g, int x, int y, int diameter, bool isActive)
    {
        int r = diameter / 2;
        if (isActive) DrawActiveGlowRing(g, x, y, r + 6);

        using (var ringBrush = new LinearGradientBrush(new Point(x - r - 3, y - r - 3), new Point(x + r + 3, y + r + 3),
            Color.FromArgb(255, 148, 163, 184),
            Color.FromArgb(255, 30, 41, 59)))
        {
            g.FillEllipse(ringBrush, x - r - 3, y - r - 3, diameter + 6, diameter + 6);
        }

        using (var domeBrush = new SolidBrush(Color.FromArgb(255, 18, 20, 28)))
        {
            g.FillEllipse(domeBrush, x - r, y - r, diameter, diameter);
        }

        using (var logoBrush = new SolidBrush(isActive ? Color.FromArgb(255, 34, 211, 238) : Color.FromArgb(255, 255, 255, 255)))
        {
            using var font = new Font("Segoe UI", 11f, FontStyle.Bold);
            var sz = g.MeasureString("X", font);
            g.DrawString("X", font, logoBrush, x - sz.Width / 2, y - sz.Height / 2);
        }
    }

    private static void DrawPlayStationJewel(Graphics g, int x, int y, int diameter, bool isActive)
    {
        int r = diameter / 2;
        if (isActive) DrawActiveGlowRing(g, x, y, r + 6);

        using (var fill = new SolidBrush(Color.FromArgb(255, 24, 26, 36)))
        {
            g.FillEllipse(fill, x - r, y - r, diameter, diameter);
        }
        using (var pen = new Pen(Color.FromArgb(255, 75, 85, 105), 1.5f))
        {
            g.DrawEllipse(pen, x - r, y - r, diameter, diameter);
        }

        using var font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var brush = new SolidBrush(isActive ? Color.FromArgb(255, 96, 165, 250) : Color.White);
        var sz = g.MeasureString("PS", font);
        g.DrawString("PS", font, brush, x - sz.Width / 2, y - sz.Height / 2);
    }

    private static void DrawShareButton(Graphics g, int x, int y, int w, int h, bool isActive)
    {
        var rect = new Rectangle(x - w / 2, y - h / 2, w, h);
        using var path = CreateRoundedRect(rect, 4);
        using var fill = new SolidBrush(isActive ? Color.FromArgb(255, 167, 139, 250) : Color.FromArgb(255, 60, 65, 80));
        g.FillPath(fill, path);
    }

    private static void DrawSmallRoundButton(Graphics g, int x, int y, int diameter, string symbol, bool isActive, Color? customColor = null)
    {
        int r = diameter / 2;
        Color c = customColor ?? Color.FromArgb(255, 51, 65, 85);

        using (var fill = new SolidBrush(isActive ? Color.FromArgb(255, 167, 139, 250) : c))
        {
            g.FillEllipse(fill, x - r, y - r, diameter, diameter);
        }
        using (var pen = new Pen(Color.FromArgb(255, 148, 163, 184), 1f))
        {
            g.DrawEllipse(pen, x - r, y - r, diameter, diameter);
        }

        if (!string.IsNullOrEmpty(symbol))
        {
            using var font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            using var brush = new SolidBrush(Color.White);
            var sz = g.MeasureString(symbol, font);
            g.DrawString(symbol, font, brush, x - sz.Width / 2, y - sz.Height / 2);
        }
    }

    private static void DrawRubberPillButton(Graphics g, int x, int y, int w, int h, string label, bool isActive)
    {
        var rect = new Rectangle(x, y, w, h);
        using var path = CreateRoundedRect(rect, h / 2);
        using var fill = new SolidBrush(isActive ? Color.FromArgb(255, 167, 139, 250) : Color.FromArgb(255, 39, 39, 42));
        g.FillPath(fill, path);

        if (!string.IsNullOrEmpty(label))
        {
            using var font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.FromArgb(255, 220, 38, 38));
            var sz = g.MeasureString(label, font);
            g.DrawString(label, font, textBrush, x + (w - sz.Width) / 2, y - sz.Height - 2);
        }
    }

    private static void DrawAngledPillButton(Graphics g, int x, int y, int w, int h, string label, bool isActive)
    {
        var rect = new Rectangle(x, y, w, h);
        using var path = CreateRoundedRect(rect, h / 2);
        using var fill = new SolidBrush(isActive ? Color.FromArgb(255, 167, 139, 250) : Color.FromArgb(255, 82, 82, 91));
        g.FillPath(fill, path);

        if (!string.IsNullOrEmpty(label))
        {
            using var font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.FromArgb(255, 113, 113, 122));
            var sz = g.MeasureString(label, font);
            g.DrawString(label, font, textBrush, x + (w - sz.Width) / 2, y + h + 2);
        }
    }

    private static void DrawKeyCap(Graphics g, int x, int y, int w, int h, string label, bool isActive)
    {
        var rect = new Rectangle(x, y, w, h);
        using var path = CreateRoundedRect(rect, 4);

        using var grad = new LinearGradientBrush(new Point(x, y), new Point(x, y + h),
            isActive ? Color.FromArgb(255, 167, 139, 250) : Color.FromArgb(255, 63, 63, 70),
            isActive ? Color.FromArgb(255, 124, 58, 237) : Color.FromArgb(255, 39, 39, 42));
        g.FillPath(grad, path);

        using var pen = new Pen(isActive ? Color.FromArgb(255, 236, 72, 153) : Color.FromArgb(255, 82, 82, 91), 1f);
        g.DrawPath(pen, path);

        using var font = new Font("Segoe UI", Math.Min(8.5f, h * 0.35f), FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.White);
        var sz = g.MeasureString(label, font);
        g.DrawString(label, font, textBrush, x + (w - sz.Width) / 2, y + (h - sz.Height) / 2);
    }

    private static void DrawActiveGlowRing(Graphics g, int x, int y, int radius)
    {
        using (var glowPen = new Pen(Color.FromArgb(70, 6, 182, 212), 8f))
        {
            g.DrawEllipse(glowPen, x - radius, y - radius, radius * 2, radius * 2);
        }
        using (var ringPen = new Pen(Color.FromArgb(240, 34, 211, 238), 3f))
        {
            g.DrawEllipse(ringPen, x - radius, y - radius, radius * 2, radius * 2);
        }
    }

    private static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();

        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
