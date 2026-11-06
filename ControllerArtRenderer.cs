using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Windows.Media.Imaging;

namespace StormRetroArch;

/// <summary>
/// Генератор фотореалистичных студийных иллюстраций контроллеров (960×520)
/// с единым бесшовным силуэтом аппаратного корпуса (Unified Bezier Contour)
/// для всех 9 поддерживаемых платформ с динамической неоновой подсветкой активных кнопок.
/// </summary>
public static class ControllerArtRenderer
{
    public const int CanvasWidth = 960;
    public const int CanvasHeight = 520;

    public static readonly string[] PlatformProfiles =
    {
        "Xbox Series / XInput (ПК)",
        "Sony PlayStation (DualShock / DualSense)",
        "Nintendo NES / Dendy",
        "Super Nintendo (SNES)",
        "Sega Mega Drive / Genesis (6 кнопок)",
        "Nintendo 64",
        "Game Boy Advance",
        "Аркадный стик (FightStick)",
        "MS-DOS Клавиатура и джойстик"
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
            return "Sega Mega Drive / Genesis (6 кнопок)";

        if (p.Contains("PlayStation", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("PS1", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("PS2", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("PSX", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("PSP", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("SwanStation", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("PCSX", StringComparison.OrdinalIgnoreCase))
            return "Sony PlayStation (DualShock / DualSense)";

        if (p.Contains("Nintendo 64", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("N64", StringComparison.OrdinalIgnoreCase))
            return "Nintendo 64";

        if (p.Contains("Game Boy", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("GBA", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("GBC", StringComparison.OrdinalIgnoreCase))
            return "Game Boy Advance";

        if (p.Contains("Arcade", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("FBNeo", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("MAME", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("Neo Geo", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("CPS", StringComparison.OrdinalIgnoreCase))
            return "Аркадный стик (FightStick)";

        if (p.Contains("DOS", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("PC", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("Компьютер", StringComparison.OrdinalIgnoreCase))
            return "MS-DOS Клавиатура и джойстик";

        return "Xbox Series / XInput (ПК)";
    }

    public static BitmapSource RenderController(string profileName, int playerIndex = 1, string? activeRetroKey = null)
    {
        string normKey = (activeRetroKey ?? "").Trim().ToLowerInvariant();
        if (normKey.StartsWith("input_player"))
        {
            int lastUnderscore = normKey.LastIndexOf('_');
            if (lastUnderscore >= 0) normKey = normKey[(lastUnderscore + 1)..];
        }

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
            DrawPlatformBadge(g, profileName);
            DrawPlayerIndicator(g, playerIndex);

            // 3. Controller Hardware Rendering with Unified Single Silhouette
            switch (profileName)
            {
                case "Nintendo NES / Dendy":
                    DrawNesController(g, normKey);
                    break;
                case "Super Nintendo (SNES)":
                    DrawSnesController(g, normKey);
                    break;
                case "Sega Mega Drive / Genesis (6 кнопок)":
                    DrawGenesisController(g, normKey);
                    break;
                case "Sony PlayStation (DualShock / DualSense)":
                    DrawPlayStationController(g, normKey);
                    break;
                case "Nintendo 64":
                    DrawN64Controller(g, normKey);
                    break;
                case "Game Boy Advance":
                    DrawGbaController(g, normKey);
                    break;
                case "Аркадный стик (FightStick)":
                    DrawArcadeStick(g, normKey);
                    break;
                case "MS-DOS Клавиатура и джойстик":
                    DrawMsDosController(g, normKey);
                    break;
                default:
                    DrawXboxController(g, normKey);
                    break;
            }
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;

        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
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
        var badgeRect = new Rectangle(24, 20, 360, 36);
        using var path = CreateRoundedRect(badgeRect, 8);
        using var fill = new SolidBrush(Color.FromArgb(190, 22, 17, 44));
        using var stroke = new Pen(Color.FromArgb(80, 139, 92, 246), 1.2f);
        g.FillPath(fill, path);
        g.DrawPath(stroke, path);

        using var iconBrush = new SolidBrush(Color.FromArgb(255, 167, 139, 250));
        g.FillEllipse(iconBrush, 36, 31, 14, 14);

        using var font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
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

    // ==================== 1. XBOX SERIES / XINPUT (UNIFIED BEZIER SILHOUETTE) ====================

    private static void DrawXboxController(Graphics g, string activeKey)
    {
        // 1. Draw Shoulder Bumpers & Triggers BEHIND the shell contour
        DrawShoulderTrigger(g, 255, 108, 96, 44, "LT / L2", activeKey is "l2" or "lt");
        DrawShoulderTrigger(g, 609, 108, 96, 44, "RT / R2", activeKey is "r2" or "rt");
        DrawShoulderBumper(g, 290, 128, 130, 42, "LB / L1", activeKey is "l" or "lb");
        DrawShoulderBumper(g, 540, 128, 130, 42, "RB / R1", activeKey is "r" or "rb");

        // 2. Build ONE Single Closed GraphicsPath tracing authentic Xbox Wireless Controller contour
        using var bodyPath = new GraphicsPath();
        bodyPath.AddLine(400, 135, 560, 135); // Top bridge
        bodyPath.AddBezier(560, 135, 590, 135, 620, 145, 640, 155); // Right shoulder flare
        bodyPath.AddBezier(640, 155, 690, 175, 735, 230, 745, 300); // Right flank
        bodyPath.AddBezier(745, 300, 755, 370, 740, 425, 695, 435); // Right grip outer to tip
        bodyPath.AddBezier(695, 435, 665, 435, 650, 410, 645, 385); // Right tip round
        bodyPath.AddBezier(645, 385, 640, 345, 600, 310, 540, 310); // Right inner to crotch
        bodyPath.AddBezier(540, 310, 500, 315, 460, 315, 420, 310); // Underside center arch
        bodyPath.AddBezier(420, 310, 360, 310, 320, 345, 315, 385); // Left inner to crotch
        bodyPath.AddBezier(315, 385, 310, 410, 295, 435, 265, 435); // Left tip round
        bodyPath.AddBezier(265, 435, 220, 425, 205, 370, 215, 300); // Left grip outer
        bodyPath.AddBezier(215, 300, 225, 230, 270, 175, 320, 155); // Left flank
        bodyPath.AddBezier(320, 155, 340, 145, 370, 135, 400, 135); // Left shoulder flare to top
        bodyPath.CloseFigure();

        // Photorealistic Soft Drop Shadow following exact shell contour
        DrawPathDropShadow(g, bodyPath, 14);

        // Fill SINGLE unified shell with rich 3D matte charcoal/graphite gradient
        using (var shellBrush = new LinearGradientBrush(new Point(480, 135), new Point(480, 435),
            Color.FromArgb(255, 52, 56, 70),
            Color.FromArgb(255, 20, 22, 30)))
        {
            ColorBlend cb = new ColorBlend(4)
            {
                Colors = new[]
                {
                    Color.FromArgb(255, 56, 61, 76), // Top rim highlight
                    Color.FromArgb(255, 38, 41, 54), // Mid body
                    Color.FromArgb(255, 26, 28, 38), // Lower waist
                    Color.FromArgb(255, 18, 20, 28)  // Deep bottom shadow
                },
                Positions = new[] { 0f, 0.25f, 0.70f, 1f }
            };
            shellBrush.InterpolationColors = cb;
            g.FillPath(shellBrush, bodyPath);
        }

        // ONE Clean Perimeter Rim Highlight (NO internal borders cutting through the body!)
        using (var rimPen = new Pen(Color.FromArgb(120, 148, 163, 184), 1.5f))
        {
            g.DrawPath(rimPen, bodyPath);
        }

        // 3. Center Hardware Elements: Xbox Nexus Guide Jewel with metallic ring
        DrawXboxJewelButton(g, 480, 185, 36, activeKey is "guide" or "home");

        // View, Menu & Share buttons
        DrawSmallRoundButton(g, 430, 218, 16, "◀", activeKey is "select" or "back" or "view");
        DrawSmallRoundButton(g, 530, 218, 16, "▶", activeKey is "start" or "menu");
        DrawShareButton(g, 480, 245, 14, 10, activeKey is "share");

        // 4. Asymmetric Thumbsticks: Left Stick (High), Right Stick (Low)
        DrawAnalogThumbstick(g, 345, 220, 68, activeKey is "l3" or "leftstick");
        DrawAnalogThumbstick(g, 545, 300, 68, activeKey is "r3" or "rightstick");

        // 5. Metallic Hybrid Faceted D-Pad (Left Low)
        DrawXboxHybridDPad(g, 415, 300, 72, activeKey);

        // 6. Action Buttons (Right High: Y, X, B, A in authentic Xbox colors)
        int abX = 615;
        int abY = 220;
        DrawActionButton(g, abX, abY - 38, 28, "Y", Color.FromArgb(255, 245, 158, 11), activeKey is "y");
        DrawActionButton(g, abX - 38, abY, 28, "X", Color.FromArgb(255, 59, 130, 246), activeKey is "x");
        DrawActionButton(g, abX + 38, abY, 28, "B", Color.FromArgb(255, 239, 68, 68), activeKey is "b");
        DrawActionButton(g, abX, abY + 38, 28, "A", Color.FromArgb(255, 16, 185, 129), activeKey is "a");
    }

    // ==================== 2. SONY PLAYSTATION DUALSENSE (TWO-TONE UNIFIED SILHOUETTE) ====================

    private static void DrawPlayStationController(Graphics g, string activeKey)
    {
        // 1. Shoulders drawn BEHIND the shell
        DrawShoulderTrigger(g, 260, 105, 90, 42, "L2", activeKey is "l2");
        DrawShoulderTrigger(g, 610, 105, 90, 42, "R2", activeKey is "r2");
        DrawShoulderBumper(g, 295, 125, 120, 40, "L1", activeKey is "l" or "l1");
        DrawShoulderBumper(g, 545, 125, 120, 40, "R1", activeKey is "r" or "r1");

        // 2. DualSense Unified Outer Shell (Smooth Bezier Contour)
        using var bodyPath = new GraphicsPath();
        bodyPath.AddLine(410, 130, 550, 130);
        bodyPath.AddBezier(550, 130, 590, 130, 635, 142, 655, 155);
        bodyPath.AddBezier(655, 155, 705, 180, 745, 240, 755, 315);
        bodyPath.AddBezier(755, 315, 760, 385, 745, 435, 705, 440);
        bodyPath.AddBezier(705, 440, 675, 440, 655, 415, 650, 385);
        bodyPath.AddBezier(650, 385, 645, 345, 605, 305, 545, 305);
        bodyPath.AddBezier(545, 305, 510, 310, 450, 310, 415, 305);
        bodyPath.AddBezier(415, 305, 355, 305, 315, 345, 310, 385);
        bodyPath.AddBezier(310, 385, 305, 415, 285, 440, 255, 440);
        bodyPath.AddBezier(255, 440, 215, 435, 200, 385, 205, 315);
        bodyPath.AddBezier(205, 315, 215, 240, 255, 180, 305, 155);
        bodyPath.AddBezier(305, 155, 325, 142, 370, 130, 410, 130);
        bodyPath.CloseFigure();

        DrawPathDropShadow(g, bodyPath, 14);

        // Fill outer body with DualSense white/silver slate gradient
        using (var whiteBrush = new LinearGradientBrush(new Point(480, 130), new Point(480, 440),
            Color.FromArgb(255, 236, 240, 248),
            Color.FromArgb(255, 160, 170, 190)))
        {
            g.FillPath(whiteBrush, bodyPath);
        }

        // DualSense Black Inner Core (Lower center section between grips)
        using (var corePath = new GraphicsPath())
        {
            corePath.AddBezier(335, 275, 410, 285, 550, 285, 625, 275);
            corePath.AddBezier(625, 275, 645, 345, 605, 305, 545, 305);
            corePath.AddBezier(545, 305, 510, 310, 450, 310, 415, 305);
            corePath.AddBezier(415, 305, 355, 305, 315, 345, 335, 275);
            corePath.CloseFigure();

            using var coreBrush = new LinearGradientBrush(new Point(480, 270), new Point(480, 320),
                Color.FromArgb(255, 32, 35, 45),
                Color.FromArgb(255, 18, 20, 26));
            g.FillPath(coreBrush, corePath);
        }

        using (var rimPen = new Pen(Color.FromArgb(160, 255, 255, 255), 1.5f))
        {
            g.DrawPath(rimPen, bodyPath);
        }

        // DualSense Central Touchpad with Blue Lightbar Glow Strips
        DrawDualSenseTouchpad(g, 480, 190);

        // Create & Options buttons
        DrawSmallRoundButton(g, 390, 170, 16, "|<<", activeKey is "select" or "share");
        DrawSmallRoundButton(g, 570, 170, 16, ">>|", activeKey is "start" or "options");

        // PlayStation Logo Button
        DrawPlayStationJewel(g, 480, 280, 24, activeKey is "guide" or "home");

        // Symmetrical Twin Analog Sticks (x=395 and x=565, y=305)
        DrawAnalogThumbstick(g, 395, 305, 66, activeKey is "l3");
        DrawAnalogThumbstick(g, 565, 305, 66, activeKey is "r3");

        // Split 4-Piece Directional D-Pad on Left
        DrawPlayStationDPad(g, 315, 225, 82, activeKey);

        // Glossy PlayStation Sacred Symbol Action Buttons: △ (#2DD4BF), ○ (#FB7185), ✕ (#60A5FA), □ (#E879F9)
        int abX = 645;
        int abY = 225;
        DrawActionButton(g, abX, abY - 38, 28, "△", Color.FromArgb(255, 45, 212, 191), activeKey is "x");
        DrawActionButton(g, abX - 38, abY, 28, "□", Color.FromArgb(255, 232, 121, 249), activeKey is "y");
        DrawActionButton(g, abX + 38, abY, 28, "○", Color.FromArgb(255, 251, 113, 133), activeKey is "a");
        DrawActionButton(g, abX, abY + 38, 28, "✕", Color.FromArgb(255, 96, 165, 250), activeKey is "b");
    }

    // ==================== 3. NINTENDO NES / DENDY ====================

    private static void DrawNesController(Graphics g, string activeKey)
    {
        int cx = CanvasWidth / 2;
        int cy = CanvasHeight / 2 + 35;
        int w = 550;
        int h = 230;

        var chassisRect = new Rectangle(cx - w / 2, cy - h / 2, w, h);
        using var bodyPath = CreateRoundedRect(chassisRect, 14);

        DrawPathDropShadow(g, bodyPath, 14);

        // Authentic Light-Gray shell (#D4D6DC -> #B4B8C4)
        using (var grad = new LinearGradientBrush(new Point(cx, cy - h / 2), new Point(cx, cy + h / 2),
            Color.FromArgb(255, 212, 214, 220),
            Color.FromArgb(255, 180, 184, 196)))
        {
            g.FillPath(grad, bodyPath);
        }

        using (var pen = new Pen(Color.FromArgb(255, 100, 110, 125), 2f))
        {
            g.DrawPath(pen, bodyPath);
        }

        // Dark Charcoal Inner Recessed Faceplate (#23252C)
        var bezelRect = new Rectangle(cx - w / 2 + 16, cy - h / 2 + 16, w - 32, h - 32);
        using (var bPath = CreateRoundedRect(bezelRect, 8))
        {
            using var bFill = new SolidBrush(Color.FromArgb(255, 35, 37, 44));
            g.FillPath(bFill, bPath);
        }

        // Horizontal silver ribs across center
        using (var linePen = new Pen(Color.FromArgb(90, 200, 205, 215), 2f))
        {
            for (int ly = cy - 56; ly <= cy + 56; ly += 14)
            {
                g.DrawLine(linePen, cx - 85, ly, cx + 85, ly);
            }
        }

        // Crisp red legends
        using (var font = new Font("Segoe UI", 9f, FontStyle.Bold))
        using (var redBrush = new SolidBrush(Color.FromArgb(255, 239, 68, 68)))
        {
            g.DrawString("STORM NES", font, redBrush, cx - 42, cy - 65);
            g.DrawString("SELECT", font, redBrush, cx - 62, cy + 48);
            g.DrawString("START", font, redBrush, cx + 22, cy + 48);
        }

        // Left Cross D-Pad with white directional arrows & center dimple
        DrawStandardDPad(g, cx - 165, cy, 96, activeKey);

        // Select & Start black rubber pills
        DrawRubberPillButton(g, cx - 38, cy + 28, 38, 12, "", activeKey is "select");
        DrawRubberPillButton(g, cx + 46, cy + 28, 38, 12, "", activeKey is "start");

        // Two square light-gray bezels framing bright red glossy concave B and A buttons
        var plateB = new Rectangle(cx + 120, cy - 25, 48, 64);
        var plateA = new Rectangle(cx + 185, cy - 25, 48, 64);
        using (var plateBrush = new SolidBrush(Color.FromArgb(255, 48, 50, 58)))
        using (var plateStroke = new Pen(Color.FromArgb(255, 120, 125, 135), 1.2f))
        {
            g.FillRectangle(plateBrush, plateB);
            g.DrawRectangle(plateStroke, plateB);
            g.FillRectangle(plateBrush, plateA);
            g.DrawRectangle(plateStroke, plateA);
        }

        DrawActionButton(g, cx + 144, cy + 2, 28, "B", Color.FromArgb(255, 220, 38, 38), activeKey is "b" or "y");
        DrawActionButton(g, cx + 209, cy + 2, 28, "A", Color.FromArgb(255, 220, 38, 38), activeKey is "a" or "x");
    }

    // ==================== 4. SUPER NINTENDO / SNES (DOGBONE UNIFIED SILHOUETTE) ====================

    private static void DrawSnesController(Graphics g, string activeKey)
    {
        // 1. Shoulders drawn BEHIND the shell
        DrawShoulderBumper(g, 260, 160, 120, 38, "L", activeKey is "l");
        DrawShoulderBumper(g, 580, 160, 120, 38, "R", activeKey is "r");

        // 2. Build ONE Single Closed Dogbone GraphicsPath
        using var bodyPath = new GraphicsPath();
        bodyPath.AddBezier(380, 215, 430, 225, 530, 225, 580, 215); // Top waist
        bodyPath.AddBezier(580, 215, 630, 190, 690, 195, 735, 235); // Right lobe top
        bodyPath.AddBezier(735, 235, 755, 265, 755, 295, 735, 325); // Right lobe outer
        bodyPath.AddBezier(735, 325, 690, 365, 630, 370, 580, 345); // Right lobe bottom
        bodyPath.AddBezier(580, 345, 530, 335, 430, 335, 380, 345); // Bottom waist
        bodyPath.AddBezier(380, 345, 330, 370, 270, 365, 225, 325); // Left lobe bottom
        bodyPath.AddBezier(225, 325, 205, 295, 205, 265, 225, 235); // Left lobe outer
        bodyPath.AddBezier(225, 235, 270, 195, 330, 190, 380, 215); // Left lobe top
        bodyPath.CloseFigure();

        DrawPathDropShadow(g, bodyPath, 14);

        // Classic light-gray SNES shell
        using (var grad = new LinearGradientBrush(new Point(480, 190), new Point(480, 370),
            Color.FromArgb(255, 230, 232, 238),
            Color.FromArgb(255, 175, 180, 195)))
        {
            g.FillPath(grad, bodyPath);
        }

        using (var pen = new Pen(Color.FromArgb(255, 120, 125, 138), 2f))
        {
            g.DrawPath(pen, bodyPath);
        }

        // Left circular well for D-Pad
        using (var wellBrush = new SolidBrush(Color.FromArgb(255, 205, 208, 218)))
        {
            g.FillEllipse(wellBrush, 245, 215, 130, 130);
        }
        DrawStandardDPad(g, 310, 280, 96, activeKey);

        // Angled rubber Select & Start pills
        DrawAngledPillButton(g, 445, 295, 34, 12, "SELECT", activeKey is "select");
        DrawAngledPillButton(g, 505, 295, 34, 12, "START", activeKey is "start");

        // Right darker circular faceplate housing tilted 4-color button diamond
        using (var rightPlateBrush = new SolidBrush(Color.FromArgb(255, 195, 198, 210)))
        {
            g.FillEllipse(rightPlateBrush, 585, 215, 130, 130);
        }

        // Tilted 4-color diamond: X (blue), Y (green), A (red), B (yellow)
        int abX = 650;
        int abY = 280;
        DrawActionButton(g, abX, abY - 40, 26, "X", Color.FromArgb(255, 59, 130, 246), activeKey is "x");
        DrawActionButton(g, abX - 40, abY, 26, "Y", Color.FromArgb(255, 34, 197, 94), activeKey is "y");
        DrawActionButton(g, abX, abY + 40, 26, "B", Color.FromArgb(255, 234, 179, 8), activeKey is "b");
        DrawActionButton(g, abX + 40, abY, 26, "A", Color.FromArgb(255, 239, 68, 68), activeKey is "a");
    }

    // ==================== 5. SEGA MEGA DRIVE / GENESIS (UNIFIED CRESCENT SILHOUETTE) ====================

    private static void DrawGenesisController(Graphics g, string activeKey)
    {
        // Unified Crescent / Kidney-Bean single GraphicsPath
        using var bodyPath = new GraphicsPath();
        bodyPath.AddBezier(230, 240, 340, 160, 420, 155, 480, 155); // Top arch left
        bodyPath.AddBezier(480, 155, 540, 155, 620, 160, 730, 240); // Top arch right
        bodyPath.AddBezier(730, 240, 755, 300, 745, 375, 705, 410); // Right horn outer
        bodyPath.AddBezier(705, 410, 675, 420, 645, 400, 635, 365); // Right horn tip
        bodyPath.AddBezier(635, 365, 590, 310, 540, 280, 480, 280); // Underside arch right
        bodyPath.AddBezier(480, 280, 420, 280, 370, 310, 325, 365); // Underside arch left
        bodyPath.AddBezier(325, 365, 315, 400, 285, 420, 255, 410); // Left horn tip
        bodyPath.AddBezier(255, 410, 215, 375, 205, 300, 230, 240); // Left horn outer
        bodyPath.CloseFigure();

        DrawPathDropShadow(g, bodyPath, 14);

        using (var grad = new LinearGradientBrush(new Point(480, 155), new Point(480, 420),
            Color.FromArgb(255, 42, 42, 46),
            Color.FromArgb(255, 18, 18, 22)))
        {
            g.FillPath(grad, bodyPath);
        }

        using (var pen = new Pen(Color.FromArgb(255, 80, 85, 95), 2f))
        {
            g.DrawPath(pen, bodyPath);
        }

        // Large 8-way floating circular D-pad disc on left
        DrawSegaDiscDPad(g, 330, 285, 104, activeKey);

        // Angled Start pill in center
        DrawSmallRoundButton(g, 475, 250, 22, "START", activeKey is "start", Color.FromArgb(255, 220, 38, 38));

        // 6 Action Buttons arranged in dual sweeping tiers
        int bX = 560;
        int bY = 295;

        // Bottom row: A, B, C (larger 26px)
        DrawActionButton(g, bX, bY, 26, "A", Color.FromArgb(255, 50, 52, 58), activeKey is "b" or "a");
        DrawActionButton(g, bX + 50, bY - 6, 26, "B", Color.FromArgb(255, 50, 52, 58), activeKey is "a" or "b");
        DrawActionButton(g, bX + 100, bY - 14, 26, "C", Color.FromArgb(255, 50, 52, 58), activeKey is "r" or "c");

        // Top row: X, Y, Z (slightly smaller 22px)
        DrawActionButton(g, bX + 2, bY - 45, 22, "X", Color.FromArgb(255, 45, 47, 52), activeKey is "y" or "x");
        DrawActionButton(g, bX + 50, bY - 51, 22, "Y", Color.FromArgb(255, 45, 47, 52), activeKey is "x" or "y");
        DrawActionButton(g, bX + 98, bY - 57, 22, "Z", Color.FromArgb(255, 45, 47, 52), activeKey is "l" or "z");
    }

    // ==================== 6. NINTENDO 64 (UNIFIED 3-PRONG TRIDENT SILHOUETTE) ====================

    private static void DrawN64Controller(Graphics g, string activeKey)
    {
        // 1. Shoulders drawn BEHIND the shell
        DrawShoulderBumper(g, 260, 175, 90, 32, "L", activeKey is "l");
        DrawShoulderBumper(g, 610, 175, 90, 32, "R", activeKey is "r");

        // 2. Build ONE Single Closed M-Shaped Trident GraphicsPath
        using var bodyPath = new GraphicsPath();
        bodyPath.AddBezier(250, 200, 340, 140, 410, 145, 480, 145); // Top bridge left
        bodyPath.AddBezier(480, 145, 550, 145, 620, 140, 710, 200); // Top bridge right
        bodyPath.AddBezier(710, 200, 745, 260, 735, 360, 690, 420); // Right grip outer
        bodyPath.AddBezier(690, 420, 665, 435, 640, 420, 625, 380); // Right grip tip
        bodyPath.AddBezier(625, 380, 610, 310, 575, 240, 545, 235); // Right inner to crotch
        bodyPath.AddBezier(545, 235, 535, 280, 525, 360, 505, 430); // Center prong right
        bodyPath.AddBezier(505, 430, 480, 440, 480, 440, 455, 430); // Center prong tip
        bodyPath.AddBezier(455, 430, 435, 360, 425, 280, 415, 235); // Center prong left
        bodyPath.AddBezier(415, 235, 385, 240, 350, 310, 335, 380); // Left inner to crotch
        bodyPath.AddBezier(335, 380, 320, 420, 295, 435, 270, 420); // Left grip tip
        bodyPath.AddBezier(270, 420, 225, 360, 215, 260, 250, 200); // Left grip outer
        bodyPath.CloseFigure();

        DrawPathDropShadow(g, bodyPath, 14);

        using (var grad = new LinearGradientBrush(new Point(480, 140), new Point(480, 440),
            Color.FromArgb(255, 160, 172, 192),
            Color.FromArgb(255, 80, 92, 115)))
        {
            g.FillPath(grad, bodyPath);
        }

        using (var pen = new Pen(Color.FromArgb(255, 55, 65, 85), 2f))
        {
            g.DrawPath(pen, bodyPath);
        }

        // Center Prong: Octagonal Gate Well with Analog Stick
        DrawN64OctagonalGate(g, 480, 315, 74, activeKey is "l3" or "analog");

        // Red Start Button in Center
        DrawSmallRoundButton(g, 480, 230, 22, "START", activeKey is "start", Color.FromArgb(255, 220, 38, 38));

        // Left Prong: Cross D-Pad
        DrawStandardDPad(g, 330, 260, 84, activeKey);

        // Right Prong: Blue A, Green B & 4 Yellow C-Buttons
        int abX = 620;
        int abY = 260;
        DrawActionButton(g, abX - 35, abY + 35, 26, "B", Color.FromArgb(255, 16, 185, 129), activeKey is "b" or "y");
        DrawActionButton(g, abX, abY + 50, 26, "A", Color.FromArgb(255, 37, 99, 235), activeKey is "a");

        // 4 Yellow C-Buttons
        DrawActionButton(g, abX + 15, abY - 45, 20, "▲", Color.FromArgb(255, 234, 179, 8), activeKey is "r2" or "up");
        DrawActionButton(g, abX + 15, abY + 5, 20, "▼", Color.FromArgb(255, 234, 179, 8), activeKey is "down");
        DrawActionButton(g, abX - 10, abY - 20, 20, "◄", Color.FromArgb(255, 234, 179, 8), activeKey is "left");
        DrawActionButton(g, abX + 40, abY - 20, 20, "►", Color.FromArgb(255, 234, 179, 8), activeKey is "right");
    }

    // ==================== 7. GAME BOY ADVANCE ====================

    private static void DrawGbaController(Graphics g, string activeKey)
    {
        int cx = CanvasWidth / 2;
        int cy = CanvasHeight / 2 + 35;
        int w = 580;
        int h = 260;

        DrawShoulderBumper(g, cx - 210, cy - 145, 110, 30, "L", activeKey is "l");
        DrawShoulderBumper(g, cx + 100, cy - 145, 110, 30, "R", activeKey is "r");

        var chassisRect = new Rectangle(cx - w / 2, cy - h / 2, w, h);
        using var bodyPath = CreateRoundedRect(chassisRect, 30);

        DrawPathDropShadow(g, bodyPath, 14);

        using (var grad = new LinearGradientBrush(new Point(cx, cy - h / 2), new Point(cx, cy + h / 2),
            Color.FromArgb(255, 99, 102, 241),
            Color.FromArgb(255, 67, 56, 202)))
        {
            g.FillPath(grad, bodyPath);
        }

        using (var pen = new Pen(Color.FromArgb(255, 49, 46, 129), 2f))
        {
            g.DrawPath(pen, bodyPath);
        }

        // Center 3:2 Backlit LCD Screen Bezel
        var screenRect = new Rectangle(cx - 120, cy - 70, 240, 160);
        using (var sPath = CreateRoundedRect(screenRect, 10))
        {
            using var sFill = new SolidBrush(Color.FromArgb(255, 15, 23, 42));
            using var sPen = new Pen(Color.FromArgb(255, 71, 85, 105), 2f);
            g.FillPath(sFill, sPath);
            g.DrawPath(sPen, sPath);

            using var font = new Font("Segoe UI", 11f, FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.FromArgb(180, 148, 163, 184));
            g.DrawString("GAME BOY ADVANCE", font, textBrush, cx - 78, cy - 5);
        }

        DrawStandardDPad(g, cx - 195, cy, 88, activeKey);
        DrawAngledPillButton(g, cx - 180, cy + 75, 30, 10, "SELECT", activeKey is "select");
        DrawAngledPillButton(g, cx - 140, cy + 75, 30, 10, "START", activeKey is "start");

        DrawActionButton(g, cx + 165, cy + 18, 28, "B", Color.FromArgb(255, 51, 65, 85), activeKey is "b" or "y");
        DrawActionButton(g, cx + 215, cy - 8, 28, "A", Color.FromArgb(255, 51, 65, 85), activeKey is "a" or "x");
    }

    // ==================== 8. АРКАДНЫЙ СТИК (FIGHTSTICK) ====================

    private static void DrawArcadeStick(Graphics g, string activeKey)
    {
        int cx = CanvasWidth / 2;
        int cy = CanvasHeight / 2 + 35;
        int w = 620;
        int h = 260;

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

        var innerRect = new Rectangle(cx - w / 2 + 16, cy - h / 2 + 16, w - 32, h - 32);
        using (var inPath = CreateRoundedRect(innerRect, 10))
        {
            using var inFill = new SolidBrush(Color.FromArgb(255, 24, 24, 28));
            g.FillPath(inFill, inPath);
        }

        // Sanwa Joystick Lever with shadow & balltop
        int joyX = cx - 170;
        int joyY = cy;
        using (var washBrush = new SolidBrush(Color.FromArgb(255, 10, 10, 12)))
        {
            g.FillEllipse(washBrush, joyX - 45, joyY - 45, 90, 90);
        }
        using (var shaftPen = new Pen(Color.FromArgb(255, 200, 200, 210), 10f))
        {
            g.DrawLine(shaftPen, joyX, joyY, joyX, joyY - 30);
        }
        bool stickActive = activeKey is "up" or "down" or "left" or "right" or "stick";
        DrawActionButton(g, joyX, joyY - 35, 42, "", Color.FromArgb(255, 239, 68, 68), stickActive);

        // 8 Action Buttons (Vewlix layout)
        int abBaseX = cx + 25;
        int abBaseY = cy;
        DrawActionButton(g, abBaseX, abBaseY - 25, 26, "X", Color.FromArgb(255, 59, 130, 246), activeKey is "x");
        DrawActionButton(g, abBaseX + 50, abBaseY - 30, 26, "Y", Color.FromArgb(255, 234, 179, 8), activeKey is "y");
        DrawActionButton(g, abBaseX + 100, abBaseY - 25, 26, "RB", Color.FromArgb(255, 239, 68, 68), activeKey is "r");
        DrawActionButton(g, abBaseX + 150, abBaseY - 15, 26, "LB", Color.FromArgb(255, 147, 51, 234), activeKey is "l");

        DrawActionButton(g, abBaseX, abBaseY + 35, 26, "A", Color.FromArgb(255, 34, 197, 94), activeKey is "a");
        DrawActionButton(g, abBaseX + 50, abBaseY + 30, 26, "B", Color.FromArgb(255, 249, 115, 22), activeKey is "b");
        DrawActionButton(g, abBaseX + 100, abBaseY + 35, 26, "RT", Color.FromArgb(255, 239, 68, 68), activeKey is "r2");
        DrawActionButton(g, abBaseX + 150, abBaseY + 45, 26, "LT", Color.FromArgb(255, 147, 51, 234), activeKey is "l2");

        DrawSmallRoundButton(g, cx + 180, cy - 90, 18, "COIN", activeKey is "select");
        DrawSmallRoundButton(g, cx + 230, cy - 90, 18, "START", activeKey is "start");
    }

    // ==================== 9. MS-DOS КЛАВИАТУРА И ДЖОЙСТИК ====================

    private static void DrawMsDosController(Graphics g, string activeKey)
    {
        int cx = CanvasWidth / 2;
        int cy = CanvasHeight / 2 + 35;

        // Mechanical Keyboard Cluster
        int kbX = cx - 290;
        int kbY = cy - 100;
        int kbW = 320;
        int kbH = 220;

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

        DrawKeyCap(g, kbX + 20, kbY + 20, 36, 30, "ESC", activeKey is "select" or "esc");
        DrawKeyCap(g, kbX + 70, kbY + 20, 36, 30, "1", false);
        DrawKeyCap(g, kbX + 120, kbY + 20, 36, 30, "2", false);
        DrawKeyCap(g, kbX + 170, kbY + 20, 36, 30, "F1", false);
        DrawKeyCap(g, kbX + 230, kbY + 20, 68, 30, "ENTER", activeKey is "start" or "return");

        DrawKeyCap(g, kbX + 70, kbY + 60, 36, 30, "W", activeKey is "up" or "w");
        DrawKeyCap(g, kbX + 20, kbY + 100, 36, 30, "A", activeKey is "left" or "a");
        DrawKeyCap(g, kbX + 70, kbY + 100, 36, 30, "S", activeKey is "down" or "s");
        DrawKeyCap(g, kbX + 120, kbY + 100, 36, 30, "D", activeKey is "right" or "d");

        DrawKeyCap(g, kbX + 230, kbY + 60, 36, 30, "▲", activeKey is "up");
        DrawKeyCap(g, kbX + 180, kbY + 100, 36, 30, "◄", activeKey is "left");
        DrawKeyCap(g, kbX + 230, kbY + 100, 36, 30, "▼", activeKey is "down");
        DrawKeyCap(g, kbX + 280, kbY + 100, 36, 30, "►", activeKey is "right");

        DrawKeyCap(g, kbX + 20, kbY + 150, 48, 32, "CTRL", activeKey is "b" or "ctrl");
        DrawKeyCap(g, kbX + 78, kbY + 150, 48, 32, "ALT", activeKey is "y" or "alt");
        DrawKeyCap(g, kbX + 136, kbY + 150, 160, 32, "ПРОБЕЛ (FIRE)", activeKey is "a" or "space");

        // Classic DA-15 PC Gamepad
        int padX = cx + 80;
        int padY = cy;
        int padW = 220;
        int padH = 180;

        var pRect = new Rectangle(padX - padW / 2, padY - padH / 2, padW, padH);
        using (var pPath = CreateRoundedRect(pRect, 18))
        {
            DrawPathDropShadow(g, pPath, 10);
            using var grad = new LinearGradientBrush(new Point(padX, padY - padH / 2), new Point(padX, padY + padH / 2),
                Color.FromArgb(255, 212, 212, 216),
                Color.FromArgb(255, 161, 161, 170));
            g.FillPath(grad, pPath);

            using var pPen = new Pen(Color.FromArgb(255, 113, 113, 122), 2f);
            g.DrawPath(pPen, pPath);
        }

        DrawStandardDPad(g, padX - 50, padY, 68, activeKey);
        DrawActionButton(g, padX + 45, padY - 25, 22, "1", Color.FromArgb(255, 220, 38, 38), activeKey is "b");
        DrawActionButton(g, padX + 75, padY - 5, 22, "2", Color.FromArgb(255, 234, 179, 8), activeKey is "a");
        DrawActionButton(g, padX + 45, padY + 25, 22, "3", Color.FromArgb(255, 37, 99, 235), activeKey is "y");
        DrawActionButton(g, padX + 15, padY + 5, 22, "4", Color.FromArgb(255, 16, 185, 129), activeKey is "x");
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
        g.ResetTransform();
    }

    private static void DrawShoulderBumper(Graphics g, int x, int y, int w, int h, string label, bool isActive)
    {
        var rect = new Rectangle(x, y, w, h);
        using var path = CreateRoundedRect(rect, 8);
        using var fill = new LinearGradientBrush(new Point(x, y), new Point(x, y + h),
            isActive ? Color.FromArgb(255, 167, 139, 250) : Color.FromArgb(255, 51, 65, 85),
            isActive ? Color.FromArgb(255, 109, 40, 217) : Color.FromArgb(255, 30, 41, 59));
        g.FillPath(fill, path);

        using var stroke = new Pen(isActive ? Color.FromArgb(255, 236, 72, 153) : Color.FromArgb(255, 71, 85, 105), 1.5f);
        g.DrawPath(stroke, path);

        using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.White);
        var size = g.MeasureString(label, font);
        g.DrawString(label, font, textBrush, x + (w - size.Width) / 2, y + (h - size.Height) / 2);

        if (isActive)
        {
            DrawActiveGlowRing(g, x + w / 2, y + h / 2, Math.Max(w, h) / 2 + 10);
        }
    }

    private static void DrawShoulderTrigger(Graphics g, int x, int y, int w, int h, string label, bool isActive)
    {
        var rect = new Rectangle(x, y, w, h);
        using var path = CreateRoundedRect(rect, 10);
        using var fill = new LinearGradientBrush(new Point(x, y), new Point(x, y + h),
            isActive ? Color.FromArgb(255, 147, 51, 234) : Color.FromArgb(255, 30, 41, 59),
            isActive ? Color.FromArgb(255, 79, 70, 229) : Color.FromArgb(255, 15, 23, 42));
        g.FillPath(fill, path);

        using var stroke = new Pen(isActive ? Color.FromArgb(255, 236, 72, 153) : Color.FromArgb(255, 51, 65, 85), 1.5f);
        g.DrawPath(stroke, path);

        using var font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.White);
        var size = g.MeasureString(label, font);
        g.DrawString(label, font, textBrush, x + (w - size.Width) / 2, y + (h - size.Height) / 2);

        if (isActive)
        {
            DrawActiveGlowRing(g, x + w / 2, y + h / 2, Math.Max(w, h) / 2 + 10);
        }
    }

    private static void DrawAnalogThumbstick(Graphics g, int cx, int cy, int diameter, bool isActive)
    {
        int r = diameter / 2;

        // Recessed base well
        using (var wellBrush = new SolidBrush(Color.FromArgb(255, 15, 18, 26)))
        {
            g.FillEllipse(wellBrush, cx - r - 4, cy - r - 4, diameter + 8, diameter + 8);
        }

        // Textured rubber cap
        using (var capBrush = new LinearGradientBrush(new Point(cx - r, cy - r), new Point(cx + r, cy + r),
            Color.FromArgb(255, 58, 68, 86),
            Color.FromArgb(255, 18, 22, 32)))
        {
            g.FillEllipse(capBrush, cx - r, cy - r, diameter, diameter);
        }

        // Knurled rim
        using (var rimPen = new Pen(Color.FromArgb(255, 75, 85, 99), 2f))
        {
            g.DrawEllipse(rimPen, cx - r + 4, cy - r + 4, diameter - 8, diameter - 8);
        }

        // Inner concave thumb dish
        using (var dishBrush = new SolidBrush(Color.FromArgb(255, 24, 28, 38)))
        {
            g.FillEllipse(dishBrush, cx - r + 10, cy - r + 10, diameter - 20, diameter - 20);
        }

        if (isActive)
        {
            DrawActiveGlowRing(g, cx, cy, r + 8);
        }
    }

    private static void DrawStandardDPad(Graphics g, int cx, int cy, int size, string activeKey)
    {
        int armW = size / 3;
        int half = size / 2;
        int halfArm = armW / 2;

        using (var path = new GraphicsPath())
        {
            path.AddPolygon(new[]
            {
                new Point(cx - halfArm, cy - half),
                new Point(cx + halfArm, cy - half),
                new Point(cx + halfArm, cy - halfArm),
                new Point(cx + half, cy - halfArm),
                new Point(cx + half, cy + halfArm),
                new Point(cx + halfArm, cy + halfArm),
                new Point(cx + halfArm, cy + half),
                new Point(cx - halfArm, cy + half),
                new Point(cx - halfArm, cy + halfArm),
                new Point(cx - half, cy + halfArm),
                new Point(cx - half, cy - halfArm),
                new Point(cx - halfArm, cy - halfArm)
            });

            using var grad = new LinearGradientBrush(new Point(cx, cy - half), new Point(cx, cy + half),
                Color.FromArgb(255, 55, 65, 81),
                Color.FromArgb(255, 24, 24, 27));
            g.FillPath(grad, path);

            using var pen = new Pen(Color.FromArgb(255, 113, 113, 122), 2f);
            g.DrawPath(pen, path);
        }

        using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var arrowBrush = new SolidBrush(Color.FromArgb(220, 212, 212, 216));
        g.DrawString("▲", font, arrowBrush, cx - 6, cy - half + 4);
        g.DrawString("▼", font, arrowBrush, cx - 6, cy + half - 18);
        g.DrawString("◄", font, arrowBrush, cx - half + 5, cy - 8);
        g.DrawString("►", font, arrowBrush, cx + half - 18, cy - 8);

        if (activeKey is "up") DrawActiveGlowRing(g, cx, cy - half / 2, halfArm + 6);
        if (activeKey is "down") DrawActiveGlowRing(g, cx, cy + half / 2, halfArm + 6);
        if (activeKey is "left") DrawActiveGlowRing(g, cx - half / 2, cy, halfArm + 6);
        if (activeKey is "right") DrawActiveGlowRing(g, cx + half / 2, cy, halfArm + 6);
    }

    private static void DrawXboxHybridDPad(Graphics g, int cx, int cy, int diameter, string activeKey)
    {
        int r = diameter / 2;
        // Recessed circular faceted dish
        using (var dishBrush = new LinearGradientBrush(new Point(cx - r, cy - r), new Point(cx + r, cy + r),
            Color.FromArgb(255, 45, 48, 60),
            Color.FromArgb(255, 18, 20, 26)))
        {
            g.FillEllipse(dishBrush, cx - r, cy - r, diameter, diameter);
        }
        using (var dishPen = new Pen(Color.FromArgb(255, 75, 85, 105), 1.5f))
        {
            g.DrawEllipse(dishPen, cx - r, cy - r, diameter, diameter);
        }

        // Raised metallic cross
        DrawStandardDPad(g, cx, cy, diameter - 12, activeKey);
    }

    private static void DrawPlayStationDPad(Graphics g, int cx, int cy, int size, string activeKey)
    {
        int r = 16;
        int d = size / 2 - 8;

        DrawSegmentedDpadButton(g, cx, cy - d, r, "▲", activeKey is "up");
        DrawSegmentedDpadButton(g, cx, cy + d, r, "▼", activeKey is "down");
        DrawSegmentedDpadButton(g, cx - d, cy, r, "◄", activeKey is "left");
        DrawSegmentedDpadButton(g, cx + d, cy, r, "►", activeKey is "right");
    }

    private static void DrawSegmentedDpadButton(Graphics g, int x, int y, int r, string symbol, bool isActive)
    {
        using (var fill = new SolidBrush(isActive ? Color.FromArgb(255, 147, 51, 234) : Color.FromArgb(255, 30, 41, 59)))
        {
            g.FillEllipse(fill, x - r, y - r, r * 2, r * 2);
        }
        using (var pen = new Pen(isActive ? Color.FromArgb(255, 236, 72, 153) : Color.FromArgb(255, 71, 85, 105), 1.5f))
        {
            g.DrawEllipse(pen, x - r, y - r, r * 2, r * 2);
        }
        using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var brush = new SolidBrush(Color.White);
        var size = g.MeasureString(symbol, font);
        g.DrawString(symbol, font, brush, x - size.Width / 2, y - size.Height / 2);

        if (isActive)
        {
            DrawActiveGlowRing(g, x, y, r + 6);
        }
    }

    private static void DrawSegaDiscDPad(Graphics g, int cx, int cy, int diameter, string activeKey)
    {
        int r = diameter / 2;
        using (var grad = new LinearGradientBrush(new Point(cx - r, cy - r), new Point(cx + r, cy + r),
            Color.FromArgb(255, 60, 60, 68),
            Color.FromArgb(255, 24, 24, 28)))
        {
            g.FillEllipse(grad, cx - r, cy - r, diameter, diameter);
        }

        using (var pen = new Pen(Color.FromArgb(255, 82, 82, 91), 2f))
        {
            g.DrawEllipse(pen, cx - r, cy - r, diameter, diameter);
        }

        DrawStandardDPad(g, cx, cy, diameter - 26, activeKey);
    }

    private static void DrawN64OctagonalGate(Graphics g, int cx, int cy, int size, bool isActive)
    {
        int r = size / 2;
        PointF[] octPoints = new PointF[8];
        for (int i = 0; i < 8; i++)
        {
            double angle = (i * 45.0 + 22.5) * Math.PI / 180.0;
            octPoints[i] = new PointF((float)(cx + r * Math.Cos(angle)), (float)(cy + r * Math.Sin(angle)));
        }

        using (var gateBrush = new SolidBrush(Color.FromArgb(255, 28, 32, 44)))
        {
            g.FillPolygon(gateBrush, octPoints);
        }
        using (var gatePen = new Pen(Color.FromArgb(255, 80, 92, 115), 2f))
        {
            g.DrawPolygon(gatePen, octPoints);
        }

        DrawAnalogThumbstick(g, cx, cy, size - 14, isActive);
    }

    private static void DrawDualSenseTouchpad(Graphics g, int cx, int cy)
    {
        Point[] padPoints = new[]
        {
            new Point(cx - 75, cy - 55),
            new Point(cx + 75, cy - 55),
            new Point(cx + 65, cy + 30),
            new Point(cx - 65, cy + 30)
        };

        using (var padBrush = new LinearGradientBrush(new Point(cx, cy - 55), new Point(cx, cy + 30),
            Color.FromArgb(255, 36, 40, 52),
            Color.FromArgb(255, 22, 24, 32)))
        {
            g.FillPolygon(padBrush, padPoints);
        }
        using (var padPen = new Pen(Color.FromArgb(255, 75, 85, 105), 1.5f))
        {
            g.DrawPolygon(padPen, padPoints);
        }

        // Blue lightbar glow strips along left & right edges
        using var glowPen = new Pen(Color.FromArgb(180, 56, 189, 248), 3f);
        g.DrawLine(glowPen, cx - 74, cy - 50, cx - 64, cy + 25);
        g.DrawLine(glowPen, cx + 74, cy - 50, cx + 64, cy + 25);
    }

    private static void DrawActionButton(Graphics g, int x, int y, int diameter, string legend, Color baseColor, bool isActive)
    {
        int r = diameter / 2;

        using (var rimBrush = new SolidBrush(Color.FromArgb(255, 24, 24, 27)))
        {
            g.FillEllipse(rimBrush, x - r - 2, y - r - 2, diameter + 4, diameter + 4);
        }

        Color topColor = isActive ? Color.FromArgb(255, 244, 114, 182) : Lighten(baseColor);
        Color botColor = isActive ? Color.FromArgb(255, 147, 51, 234) : Darken(baseColor);

        using (var domeBrush = new LinearGradientBrush(new Point(x - r, y - r), new Point(x + r, y + r), topColor, botColor))
        {
            g.FillEllipse(domeBrush, x - r, y - r, diameter, diameter);
        }

        using (var glintBrush = new SolidBrush(Color.FromArgb(100, 255, 255, 255)))
        {
            g.FillEllipse(glintBrush, x - r / 2, y - r + 3, r, r / 2);
        }

        if (!string.IsNullOrEmpty(legend))
        {
            using var font = new Font("Segoe UI", Math.Max(8f, diameter * 0.42f), FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.White);
            var size = g.MeasureString(legend, font);
            g.DrawString(legend, font, textBrush, x - size.Width / 2, y - size.Height / 2);
        }

        if (isActive)
        {
            DrawActiveGlowRing(g, x, y, r + 8);
        }
    }

    private static void DrawXboxJewelButton(Graphics g, int x, int y, int diameter, bool isActive)
    {
        int r = diameter / 2;
        // Chrome outer bevel ring
        using (var ringBrush = new LinearGradientBrush(new Point(x - r - 3, y - r - 3), new Point(x + r + 3, y + r + 3),
            Color.FromArgb(255, 220, 225, 235),
            Color.FromArgb(255, 75, 85, 105)))
        {
            g.FillEllipse(ringBrush, x - r - 3, y - r - 3, diameter + 6, diameter + 6);
        }

        // Inner black dome
        using (var domeBrush = new SolidBrush(Color.FromArgb(255, 18, 20, 28)))
        {
            g.FillEllipse(domeBrush, x - r, y - r, diameter, diameter);
        }

        // Illuminated Xbox Nexus Logo
        using (var logoBrush = new SolidBrush(isActive ? Color.FromArgb(255, 34, 211, 238) : Color.FromArgb(255, 255, 255, 255)))
        {
            g.FillEllipse(logoBrush, x - 8, y - 8, 16, 16);
        }

        if (isActive)
        {
            DrawActiveGlowRing(g, x, y, r + 8);
        }
    }

    private static void DrawPlayStationJewel(Graphics g, int x, int y, int diameter, bool isActive)
    {
        int r = diameter / 2;
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
        var size = g.MeasureString("PS", font);
        g.DrawString("PS", font, brush, x - size.Width / 2, y - size.Height / 2);

        if (isActive)
        {
            DrawActiveGlowRing(g, x, y, r + 8);
        }
    }

    private static void DrawShareButton(Graphics g, int x, int y, int w, int h, bool isActive)
    {
        var rect = new Rectangle(x - w / 2, y - h / 2, w, h);
        using var path = CreateRoundedRect(rect, 4);
        using var fill = new SolidBrush(isActive ? Color.FromArgb(255, 167, 139, 250) : Color.FromArgb(255, 60, 65, 80));
        g.FillPath(fill, path);

        if (isActive)
        {
            DrawActiveGlowRing(g, x, y, Math.Max(w, h) / 2 + 6);
        }
    }

    private static void DrawSmallRoundButton(Graphics g, int x, int y, int diameter, string symbol, bool isActive, Color? customColor = null)
    {
        int r = diameter / 2;
        Color c = customColor ?? Color.FromArgb(255, 71, 85, 105);
        using (var fill = new SolidBrush(isActive ? Color.FromArgb(255, 167, 139, 250) : c))
        {
            g.FillEllipse(fill, x - r, y - r, diameter, diameter);
        }
        using (var pen = new Pen(Color.FromArgb(255, 148, 163, 184), 1f))
        {
            g.DrawEllipse(pen, x - r, y - r, diameter, diameter);
        }
        if (!string.IsNullOrEmpty(symbol) && symbol.Length <= 3)
        {
            using var font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            using var brush = new SolidBrush(Color.White);
            var size = g.MeasureString(symbol, font);
            g.DrawString(symbol, font, brush, x - size.Width / 2, y - size.Height / 2);
        }

        if (isActive)
        {
            DrawActiveGlowRing(g, x, y, r + 6);
        }
    }

    private static void DrawRubberPillButton(Graphics g, int x, int y, int w, int h, string label, bool isActive)
    {
        var rect = new Rectangle(x - w / 2, y - h / 2, w, h);
        using var path = CreateRoundedRect(rect, h / 2);
        using var fill = new SolidBrush(isActive ? Color.FromArgb(255, 167, 139, 250) : Color.FromArgb(255, 39, 39, 42));
        g.FillPath(fill, path);

        if (!string.IsNullOrEmpty(label))
        {
            using var font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.FromArgb(255, 220, 38, 38));
            var size = g.MeasureString(label, font);
            g.DrawString(label, font, textBrush, x - size.Width / 2, y + h / 2 + 2);
        }

        if (isActive)
        {
            DrawActiveGlowRing(g, x, y, Math.Max(w, h) / 2 + 6);
        }
    }

    private static void DrawAngledPillButton(Graphics g, int x, int y, int w, int h, string label, bool isActive)
    {
        var rect = new Rectangle(x - w / 2, y - h / 2, w, h);
        using var path = CreateRoundedRect(rect, h / 2);
        using var fill = new SolidBrush(isActive ? Color.FromArgb(255, 167, 139, 250) : Color.FromArgb(255, 82, 82, 91));
        g.FillPath(fill, path);

        if (!string.IsNullOrEmpty(label))
        {
            using var font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.FromArgb(255, 113, 113, 122));
            var size = g.MeasureString(label, font);
            g.DrawString(label, font, textBrush, x - size.Width / 2, y + h / 2 + 2);
        }

        if (isActive)
        {
            DrawActiveGlowRing(g, x, y, Math.Max(w, h) / 2 + 6);
        }
    }

    private static void DrawKeyCap(Graphics g, int x, int y, int w, int h, string label, bool isActive)
    {
        var rect = new Rectangle(x, y, w, h);
        using var path = CreateRoundedRect(rect, 4);

        using var grad = new LinearGradientBrush(new Point(x, y), new Point(x, y + h),
            isActive ? Color.FromArgb(255, 167, 139, 250) : Color.FromArgb(255, 63, 63, 70),
            isActive ? Color.FromArgb(255, 109, 40, 217) : Color.FromArgb(255, 39, 39, 42));
        g.FillPath(grad, path);

        using var pen = new Pen(isActive ? Color.FromArgb(255, 236, 72, 153) : Color.FromArgb(255, 82, 82, 91), 1f);
        g.DrawPath(pen, path);

        using var font = new Font("Segoe UI", Math.Min(8.5f, h * 0.35f), FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.White);
        var size = g.MeasureString(label, font);
        g.DrawString(label, font, textBrush, x + (w - size.Width) / 2, y + (h - size.Height) / 2);

        if (isActive)
        {
            DrawActiveGlowRing(g, x + w / 2, y + h / 2, Math.Max(w, h) / 2 + 8);
        }
    }

    private static void DrawActiveGlowRing(Graphics g, int x, int y, int radius)
    {
        using (var glowPen = new Pen(Color.FromArgb(70, 6, 182, 212), 8f))
        {
            g.DrawEllipse(glowPen, x - radius - 2, y - radius - 2, (radius + 2) * 2, (radius + 2) * 2);
        }
        using (var ringPen = new Pen(Color.FromArgb(240, 34, 211, 238), 3f))
        {
            g.DrawEllipse(ringPen, x - radius, y - radius, radius * 2, radius * 2);
        }
    }

    private static Color Lighten(Color color, float factor = 0.28f)
    {
        return Color.FromArgb(color.A,
            Math.Min(255, (int)(color.R + (255 - color.R) * factor)),
            Math.Min(255, (int)(color.G + (255 - color.G) * factor)),
            Math.Min(255, (int)(color.B + (255 - color.B) * factor)));
    }

    private static Color Darken(Color color, float factor = 0.35f)
    {
        return Color.FromArgb(color.A,
            Math.Max(0, (int)(color.R * (1f - factor))),
            Math.Max(0, (int)(color.G * (1f - factor))),
            Math.Max(0, (int)(color.B * (1f - factor))));
    }

    private static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();
        if (radius == 0)
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
