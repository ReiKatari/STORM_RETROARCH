using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StormRetroArch;

/// <summary>
/// Высокопроизводительный движок визуальных эффектов STORM:
/// - Аппаратные векторные оверлеи шейдеров (Scanlines, CRT Royale, CRT Geom, CRT Lottes, LCD Grid, VHS NTSC, xBRZ)
/// - Автоматическая генерация превью шейдеров и экранных рамок (Bezels)
/// - Кэширование замороженных (Frozen) кистей и битмапов для 60-120 FPS без просадок памяти
/// </summary>
public static class LiveVisualEffectsEngine
{
    private static readonly ConcurrentDictionary<string, ImageSource> _shaderPreviewCache = new();
    private static readonly ConcurrentDictionary<string, ImageSource?> _bezelPreviewCache = new();

    // Замороженные кисти оверлеев WPF (нулевые аллокации в кадре)
    private static readonly System.Windows.Media.Brush ScanlineSubtleBrush;
    private static readonly System.Windows.Media.Brush ScanlineMediumBrush;
    private static readonly System.Windows.Media.Brush PhosphorTriadBrush;
    private static readonly System.Windows.Media.Brush LcdGridBrush;
    private static readonly System.Windows.Media.Brush DotMaskBrush;
    private static readonly System.Windows.Media.Brush CrtVignetteBrush;
    private static readonly System.Windows.Media.Brush CrtGeomVignetteBrush;
    private static readonly System.Windows.Media.Brush VhsNoiseBrush;
    private static readonly System.Windows.Media.Brush XbrzFilterBrush;

    static LiveVisualEffectsEngine()
    {
        // 1. Scanlines Subtle (2px tile, 1px transparent, 1px 25% black)
        var dbSubtle = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 1, 2),
            ViewportUnits = BrushMappingMode.Absolute
        };
        var dgSubtle = new DrawingGroup();
        dgSubtle.Children.Add(new GeometryDrawing(
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 0, 0, 0)),
            null,
            new RectangleGeometry(new Rect(0, 1, 1, 1))));
        dbSubtle.Drawing = dgSubtle;
        dbSubtle.Freeze();
        ScanlineSubtleBrush = dbSubtle;

        // 2. Scanlines Medium (3px tile, 2px transparent, 1px 40% black)
        var dbMedium = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 1, 3),
            ViewportUnits = BrushMappingMode.Absolute
        };
        var dgMed = new DrawingGroup();
        dgMed.Children.Add(new GeometryDrawing(
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(90, 0, 0, 0)),
            null,
            new RectangleGeometry(new Rect(0, 2, 1, 1))));
        dbMedium.Drawing = dgMed;
        dbMedium.Freeze();
        ScanlineMediumBrush = dbMedium;

        // 3. Phosphor Triad (3px vertical stripes: R, G, B subpixel triad)
        var dbPhosphor = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 3, 3),
            ViewportUnits = BrushMappingMode.Absolute
        };
        var dgPhosphor = new DrawingGroup();
        dgPhosphor.Children.Add(new GeometryDrawing(
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 255, 0, 0)),
            null, new RectangleGeometry(new Rect(0, 0, 1, 3))));
        dgPhosphor.Children.Add(new GeometryDrawing(
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 0, 255, 0)),
            null, new RectangleGeometry(new Rect(1, 0, 1, 3))));
        dgPhosphor.Children.Add(new GeometryDrawing(
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 0, 100, 255)),
            null, new RectangleGeometry(new Rect(2, 0, 1, 3))));
        dbPhosphor.Drawing = dgPhosphor;
        dbPhosphor.Freeze();
        PhosphorTriadBrush = dbPhosphor;

        // 4. LCD Grid (3x3 pixels with dark separation grid)
        var dbLcd = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 4, 4),
            ViewportUnits = BrushMappingMode.Absolute
        };
        var dgLcd = new DrawingGroup();
        dgLcd.Children.Add(new GeometryDrawing(
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(15, 160, 220, 80)),
            null, new RectangleGeometry(new Rect(0, 0, 3, 3))));
        dgLcd.Children.Add(new GeometryDrawing(
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 0, 0, 0)),
            null, new RectangleGeometry(new Rect(3, 0, 1, 4))));
        dgLcd.Children.Add(new GeometryDrawing(
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 0, 0, 0)),
            null, new RectangleGeometry(new Rect(0, 3, 4, 1))));
        dbLcd.Drawing = dgLcd;
        dbLcd.Freeze();
        LcdGridBrush = dbLcd;

        // 5. Dot Mask
        var dbDot = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 4, 4),
            ViewportUnits = BrushMappingMode.Absolute
        };
        var dgDot = new DrawingGroup();
        dgDot.Children.Add(new GeometryDrawing(
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(45, 0, 0, 0)),
            null, new EllipseGeometry(new System.Windows.Point(2, 2), 1, 1)));
        dbDot.Drawing = dgDot;
        dbDot.Freeze();
        DotMaskBrush = dbDot;

        // 6. CRT Vignette
        var radVignette = new RadialGradientBrush
        {
            Center = new System.Windows.Point(0.5, 0.5),
            GradientOrigin = new System.Windows.Point(0.5, 0.5),
            RadiusX = 0.68,
            RadiusY = 0.68
        };
        radVignette.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.0));
        radVignette.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.70));
        radVignette.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(140, 0, 0, 10), 0.95));
        radVignette.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(220, 0, 0, 12), 1.0));
        radVignette.Freeze();
        CrtVignetteBrush = radVignette;

        // 7. CRT Geom Vignette (barrel shape emphasis)
        var radGeom = new RadialGradientBrush
        {
            Center = new System.Windows.Point(0.5, 0.5),
            GradientOrigin = new System.Windows.Point(0.5, 0.5),
            RadiusX = 0.62,
            RadiusY = 0.62
        };
        radGeom.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.0));
        radGeom.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.65));
        radGeom.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(170, 5, 0, 15), 0.92));
        radGeom.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(240, 3, 0, 8), 1.0));
        radGeom.Freeze();
        CrtGeomVignetteBrush = radGeom;

        // 8. VHS Noise / Tape Lines
        var dbVhs = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 1, 16),
            ViewportUnits = BrushMappingMode.Absolute
        };
        var dgVhs = new DrawingGroup();
        dgVhs.Children.Add(new GeometryDrawing(
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 255, 0, 50)),
            null, new RectangleGeometry(new Rect(0, 2, 1, 1))));
        dgVhs.Children.Add(new GeometryDrawing(
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 0, 180, 255)),
            null, new RectangleGeometry(new Rect(0, 10, 1, 1))));
        dbVhs.Drawing = dgVhs;
        dbVhs.Freeze();
        VhsNoiseBrush = dbVhs;

        // 9. xBRZ Subtle Edge Filter (subtle cyan/glow boost)
        var xbrzBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(8, 0, 220, 255));
        xbrzBrush.Freeze();
        XbrzFilterBrush = xbrzBrush;
    }

    /// <summary>
    /// Нормализует строковый идентификатор шейдера к каноническому виду.
    /// Возвращает "none" для пустого значения, "без фильтра" или отключенного режима.
    /// </summary>
    public static string NormalizeShaderId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "none";
        string s = id.Trim().ToLowerInvariant().Replace('_', '-');
        if (s == "none" || s == "off" || s == "disabled" || s == "clean-raw" || s == "clean" || s == "raw" || s.Contains("без фильтра") || s.Contains("чистый пиксель") || s.Contains("no filter") || s.Contains("оригинал"))
            return "none";
        if (s.Contains("royale")) return "crt-royale";
        if (s.Contains("geom")) return "crt-geom";
        if (s.Contains("lottes")) return "crt-lottes";
        if (s.Contains("lcd") || s.Contains("gba") || s.Contains("gameboy")) return "lcd-grid";
        if (s.Contains("xbrz") || s.Contains("xbr") || s.Contains("scalefx") || s.Contains("hq")) return "xbrz-4x";
        if (s.Contains("vhs") || s.Contains("ntsc")) return "vhs-ntsc";
        if (s.Contains("scanline") || s.Contains("scanlines") || s.Contains("240p")) return "scanlines-subtle";
        if (s.Contains("crt")) return "crt-geom";
        return s;
    }

    /// <summary>
    /// Накладывает аппаратный многослойный оверлей поверх панели игрового окна.
    /// Нулевые аллокации в кадре, IsHitTestVisible = false.
    /// </summary>
    public static void ApplyLiveShaderOverlay(System.Windows.Controls.Panel hostGrid, string? presetId)
    {
        if (hostGrid == null) return;
        hostGrid.Children.Clear();

        string canon = NormalizeShaderId(presetId);
        if (canon == "none")
        {
            hostGrid.Visibility = Visibility.Collapsed;
            return;
        }

        hostGrid.Visibility = Visibility.Visible;

        // Слой 1: Сканлинии
        if (canon == "scanlines-subtle" || canon == "crt-royale" || canon == "crt-geom" || canon == "crt-lottes" || canon == "vhs-ntsc")
        {
            var scanRect = new System.Windows.Shapes.Rectangle
            {
                IsHitTestVisible = false,
                Fill = canon == "scanlines-subtle" ? ScanlineSubtleBrush : ScanlineMediumBrush
            };
            hostGrid.Children.Add(scanRect);
        }

        // Слой 2: Триады / Субпиксели / Сетка
        if (canon == "crt-royale")
        {
            var phosRect = new System.Windows.Shapes.Rectangle
            {
                IsHitTestVisible = false,
                Fill = PhosphorTriadBrush
            };
            hostGrid.Children.Add(phosRect);
        }
        else if (canon == "lcd-grid")
        {
            var lcdRect = new System.Windows.Shapes.Rectangle
            {
                IsHitTestVisible = false,
                Fill = LcdGridBrush
            };
            hostGrid.Children.Add(lcdRect);
        }
        else if (canon == "crt-lottes")
        {
            var dotRect = new System.Windows.Shapes.Rectangle
            {
                IsHitTestVisible = false,
                Fill = DotMaskBrush
            };
            hostGrid.Children.Add(dotRect);
        }

        // Слой 3: Геометрия и виньетка ЭЛТ
        if (canon == "crt-geom" || canon == "crt-royale" || canon == "crt-lottes")
        {
            var vigRect = new System.Windows.Shapes.Rectangle
            {
                IsHitTestVisible = false,
                Fill = canon == "crt-geom" ? CrtGeomVignetteBrush : CrtVignetteBrush
            };
            hostGrid.Children.Add(vigRect);
        }

        // Слой 4: Помехи VHS/NTSC
        if (canon == "vhs-ntsc")
        {
            var vhsRect = new System.Windows.Shapes.Rectangle
            {
                IsHitTestVisible = false,
                Fill = VhsNoiseBrush
            };
            hostGrid.Children.Add(vhsRect);
        }

        // Слой 5: xBRZ мягкая подложка
        if (canon == "xbrz-4x")
        {
            var xbrzRect = new System.Windows.Shapes.Rectangle
            {
                IsHitTestVisible = false,
                Fill = XbrzFilterBrush
            };
            hostGrid.Children.Add(xbrzRect);
        }
    }

    /// <summary>
    /// Генерирует качественный ретро-пиксельный скриншот с физической обработкой выбранного шейдера.
    /// Кэшируется в памяти, возвращается замороженный BitmapSource.
    /// </summary>
    public static ImageSource RenderShaderPreviewBitmap(string? presetId, int width = 360, int height = 210)
    {
        string canon = NormalizeShaderId(presetId);
        string cacheKey = $"{canon}|{width}|{height}";

        if (_shaderPreviewCache.TryGetValue(cacheKey, out var cached))
            return cached;

        using var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.SmoothingMode = SmoothingMode.None;

            // 1. Небо (градиент)
            using (var skyBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                new System.Drawing.Point(0, 0),
                new System.Drawing.Point(0, height),
                System.Drawing.Color.FromArgb(16, 12, 38),
                System.Drawing.Color.FromArgb(88, 28, 86)))
            {
                g.FillRectangle(skyBrush, 0, 0, width, height);
            }

            // 2. Дальние горы / город
            using (var mtnBrush = new SolidBrush(System.Drawing.Color.FromArgb(28, 18, 54)))
            {
                var pts = new[]
                {
                    new System.Drawing.Point(0, height * 12 / 20),
                    new System.Drawing.Point(width * 3 / 20, height * 9 / 20),
                    new System.Drawing.Point(width * 7 / 20, height * 13 / 20),
                    new System.Drawing.Point(width * 11 / 20, height * 8 / 20),
                    new System.Drawing.Point(width * 16 / 20, height * 12 / 20),
                    new System.Drawing.Point(width, height * 9 / 20),
                    new System.Drawing.Point(width, height),
                    new System.Drawing.Point(0, height)
                };
                g.FillPolygon(mtnBrush, pts);
            }

            // 3. Неоновая аркадная сетка земли
            int horizon = height * 13 / 20;
            using (var groundBrush = new SolidBrush(System.Drawing.Color.FromArgb(12, 8, 24)))
            {
                g.FillRectangle(groundBrush, 0, horizon, width, height - horizon);
            }
            using (var gridPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(120, 168, 85, 247), 1))
            {
                for (int y = horizon + 4; y < height; y += (y - horizon) / 2 + 3)
                {
                    g.DrawLine(gridPen, 0, y, width, y);
                }
                for (int x = -width / 2; x <= width * 3 / 2; x += width / 8)
                {
                    g.DrawLine(gridPen, width / 2, horizon, x, height);
                }
            }

            // 4. Неоновый храм / ворота в центре
            int cx = width / 2;
            int cy = horizon - 20;
            using (var neonBrush = new SolidBrush(System.Drawing.Color.FromArgb(240, 0, 229, 255)))
            {
                g.FillRectangle(neonBrush, cx - 35, cy - 25, 6, 45);
                g.FillRectangle(neonBrush, cx + 29, cy - 25, 6, 45);
                g.FillRectangle(neonBrush, cx - 42, cy - 29, 84, 6);
                g.FillRectangle(neonBrush, cx - 30, cy - 35, 60, 4);
            }
            using (var orbBrush = new SolidBrush(System.Drawing.Color.FromArgb(255, 244, 63, 94)))
            {
                g.FillEllipse(orbBrush, cx - 8, cy - 18, 16, 16);
            }

            // 5. Ретро-персонаж (пиксельный спрайт)
            int hx = width * 6 / 20;
            int hy = horizon + 12;
            using (var heroBody = new SolidBrush(System.Drawing.Color.FromArgb(255, 245, 158, 11)))
            {
                g.FillRectangle(heroBody, hx - 5, hy - 16, 10, 12);
            }
            using (var heroHead = new SolidBrush(System.Drawing.Color.FromArgb(255, 56, 189, 248)))
            {
                g.FillRectangle(heroHead, hx - 4, hy - 24, 8, 7);
            }
            using (var heroSword = new SolidBrush(System.Drawing.Color.FromArgb(255, 255, 255, 255)))
            {
                g.FillRectangle(heroSword, hx + 6, hy - 28, 2, 20);
                g.FillRectangle(heroSword, hx + 4, hy - 14, 6, 2);
            }

            // 6. Ретро HUD вверху
            using (var hudBg = new SolidBrush(System.Drawing.Color.FromArgb(200, 5, 5, 12)))
            {
                g.FillRectangle(hudBg, 0, 0, width, 18);
            }
            using (var hudFont = new System.Drawing.Font("Consolas", 8, System.Drawing.FontStyle.Bold))
            using (var hudYellow = new SolidBrush(System.Drawing.Color.FromArgb(255, 250, 204, 21)))
            using (var hudCyan = new SolidBrush(System.Drawing.Color.FromArgb(255, 56, 189, 248)))
            using (var hudWhite = new SolidBrush(System.Drawing.Color.FromArgb(255, 240, 240, 250)))
            {
                g.DrawString("1UP", hudFont, hudYellow, 6f, 2f);
                g.DrawString("028400", hudFont, hudWhite, 36f, 2f);
                g.DrawString("STAGE 1-1", hudFont, hudCyan, (float)(cx - 28), 2f);
                g.DrawString("LIFE [====]", hudFont, hudYellow, (float)(width - 86), 2f);
            }

            // 7. Наложение фирменного шейдерного эффекта на уровне пикселей
            ApplyShaderPixelFilter(bmp, canon);
        }

        var source = BitmapToBitmapSource(bmp);
        _shaderPreviewCache[cacheKey] = source;
        return source;
    }

    private static void ApplyShaderPixelFilter(Bitmap bmp, string canon)
    {
        if (canon == "none") return;

        int w = bmp.Width;
        int h = bmp.Height;
        var rect = new Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        unsafe
        {
            byte* ptr = (byte*)data.Scan0;
            int stride = data.Stride;

            // CRT Royale: триады + сканлинии + виньетка
            if (canon == "crt-royale")
            {
                for (int y = 0; y < h; y++)
                {
                    byte* row = ptr + (y * stride);
                    bool scan = (y % 2 == 1);
                    double dy = (y - h / 2.0) / (h / 2.0);

                    for (int x = 0; x < w; x++)
                    {
                        double dx = (x - w / 2.0) / (w / 2.0);
                        double dist = Math.Min(1.0, Math.Sqrt(dx * dx + dy * dy));
                        double vignette = 1.0 - (dist * dist * 0.35);

                        int bIdx = x * 4;
                        int gIdx = bIdx + 1;
                        int rIdx = bIdx + 2;

                        // Сканлиния затемняет строку
                        double sFactor = scan ? 0.70 : 1.05;

                        // Триада субпикселей (X % 3)
                        int tri = x % 3;
                        double rBoost = tri == 0 ? 1.15 : 0.85;
                        double gBoost = tri == 1 ? 1.15 : 0.85;
                        double bBoost = tri == 2 ? 1.15 : 0.85;

                        row[rIdx] = (byte)Math.Clamp((int)(row[rIdx] * sFactor * rBoost * vignette), 0, 255);
                        row[gIdx] = (byte)Math.Clamp((int)(row[gIdx] * sFactor * gBoost * vignette), 0, 255);
                        row[bIdx] = (byte)Math.Clamp((int)(row[bIdx] * sFactor * bBoost * vignette), 0, 255);
                    }
                }
            }
            // CRT Geom: выпуклая виньетка + тёплая гамма
            else if (canon == "crt-geom")
            {
                for (int y = 0; y < h; y++)
                {
                    byte* row = ptr + (y * stride);
                    bool scan = (y % 3 == 2);
                    double dy = (y - h / 2.0) / (h / 2.0);

                    for (int x = 0; x < w; x++)
                    {
                        double dx = (x - w / 2.0) / (w / 2.0);
                        double dist = Math.Min(1.0, Math.Sqrt(dx * dx * 1.1 + dy * dy * 1.1));
                        double vignette = 1.0 - Math.Pow(dist, 2.5) * 0.55;
                        double sFactor = scan ? 0.65 : 1.0;

                        int bIdx = x * 4;
                        int gIdx = bIdx + 1;
                        int rIdx = bIdx + 2;

                        row[rIdx] = (byte)Math.Clamp((int)(row[rIdx] * 1.08 * sFactor * vignette), 0, 255);
                        row[gIdx] = (byte)Math.Clamp((int)(row[gIdx] * 1.02 * sFactor * vignette), 0, 255);
                        row[bIdx] = (byte)Math.Clamp((int)(row[bIdx] * 0.95 * sFactor * vignette), 0, 255);
                    }
                }
            }
            // CRT Lottes: точечная маска + блум
            else if (canon == "crt-lottes")
            {
                for (int y = 0; y < h; y++)
                {
                    byte* row = ptr + (y * stride);
                    for (int x = 0; x < w; x++)
                    {
                        bool dot = (x % 2 == 0) && (y % 2 == 0);
                        double factor = dot ? 1.18 : 0.82;

                        int bIdx = x * 4;
                        int gIdx = bIdx + 1;
                        int rIdx = bIdx + 2;

                        row[rIdx] = (byte)Math.Clamp((int)(row[rIdx] * factor), 0, 255);
                        row[gIdx] = (byte)Math.Clamp((int)(row[gIdx] * factor), 0, 255);
                        row[bIdx] = (byte)Math.Clamp((int)(row[bIdx] * factor), 0, 255);
                    }
                }
            }
            // LCD Grid: сетка 3x3 с тёмными границами + оливково-янтарный тон
            else if (canon == "lcd-grid")
            {
                for (int y = 0; y < h; y++)
                {
                    byte* row = ptr + (y * stride);
                    bool gridY = (y % 3 == 2);
                    for (int x = 0; x < w; x++)
                    {
                        bool gridX = (x % 3 == 2);
                        double factor = (gridX || gridY) ? 0.60 : 1.02;

                        int bIdx = x * 4;
                        int gIdx = bIdx + 1;
                        int rIdx = bIdx + 2;

                        row[rIdx] = (byte)Math.Clamp((int)(row[rIdx] * factor * 0.95), 0, 255);
                        row[gIdx] = (byte)Math.Clamp((int)(row[gIdx] * factor * 1.05), 0, 255);
                        row[bIdx] = (byte)Math.Clamp((int)(row[bIdx] * factor * 0.85), 0, 255);
                    }
                }
            }
            // Scanlines Subtle: мягкие сканлинии через строку
            else if (canon == "scanlines-subtle")
            {
                for (int y = 0; y < h; y++)
                {
                    if (y % 2 == 1)
                    {
                        byte* row = ptr + (y * stride);
                        for (int x = 0; x < w; x++)
                        {
                            int bIdx = x * 4;
                            row[bIdx] = (byte)(row[bIdx] * 65 / 100);
                            row[bIdx + 1] = (byte)(row[bIdx + 1] * 65 / 100);
                            row[bIdx + 2] = (byte)(row[bIdx + 2] * 65 / 100);
                        }
                    }
                }
            }
            // VHS / NTSC: хроматическая аберрация R/B + волновые полосы
            else if (canon == "vhs-ntsc")
            {
                byte[] copy = new byte[h * stride];
                System.Runtime.InteropServices.Marshal.Copy((IntPtr)ptr, copy, 0, copy.Length);

                for (int y = 0; y < h; y++)
                {
                    byte* row = ptr + (y * stride);
                    int wobble = (int)(Math.Sin(y * 0.25) * 2.0);
                    bool glitchLine = (y % 37 == 0 || y % 53 == 0);

                    for (int x = 0; x < w; x++)
                    {
                        int srcX_R = Math.Clamp(x + 2 + wobble, 0, w - 1);
                        int srcX_B = Math.Clamp(x - 2 - wobble, 0, w - 1);

                        int bIdx = x * 4;
                        row[bIdx + 2] = copy[y * stride + srcX_R * 4 + 2]; // Red сдвинут
                        row[bIdx] = copy[y * stride + srcX_B * 4];         // Blue сдвинут

                        if (glitchLine)
                        {
                            row[bIdx] = (byte)Math.Min(255, row[bIdx] + 40);
                            row[bIdx + 1] = (byte)Math.Min(255, row[bIdx + 1] + 40);
                            row[bIdx + 2] = (byte)Math.Min(255, row[bIdx + 2] + 40);
                        }
                    }
                }
            }
            // xBRZ 4x: сглаживание ступенек и лёгкое повышение чёткости
            else if (canon == "xbrz-4x")
            {
                for (int y = 1; y < h - 1; y++)
                {
                    byte* row = ptr + (y * stride);
                    byte* rowAbove = ptr + ((y - 1) * stride);
                    byte* rowBelow = ptr + ((y + 1) * stride);

                    for (int x = 1; x < w - 1; x++)
                    {
                        int bIdx = x * 4;
                        for (int c = 0; c < 3; c++)
                        {
                            int val = (row[bIdx + c] * 4 + rowAbove[bIdx + c] + rowBelow[bIdx + c] + row[bIdx + c - 4] + row[bIdx + c + 4]) / 8;
                            row[bIdx + c] = (byte)Math.Clamp(val, 0, 255);
                        }
                    }
                }
            }
        }

        bmp.UnlockBits(data);
    }

    /// <summary>
    /// Нормализует идентификатор экранной рамки (Bezel).
    /// </summary>
    public static string NormalizeBezelId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "none";
        string s = id.Trim().ToLowerInvariant().Replace('_', '-');
        if (s == "none" || s == "off" || s.Contains("без рамки") || s.Contains("no bezel"))
            return "none";
        if (s.Contains("neon") || s.Contains("arcade")) return "arcade-neon";
        if (s.Contains("sony") || s.Contains("trinitron") || s.Contains("pvm")) return "crt-sony";
        if (s.Contains("gba") || s.Contains("handheld") || s.Contains("game-boy")) return "handheld-gba";
        if (s.Contains("snes") || s.Contains("famicom") || s.Contains("super-nintendo")) return "snes-classic";
        if (s.Contains("genesis") || s.Contains("megadrive") || s.Contains("mega-drive") || s.Contains("sega")) return "megadrive-crt";
        if (s.Contains("aurora") || s.Contains("storm")) return "storm-studio";
        return id.Trim();
    }

    /// <summary>
    /// Рендерит изображение экранной рамки с прозрачным вырезом или образцом игры.
    /// Поддерживает студийные пресеты (arcade-neon, crt-sony, handheld-gba, snes-classic, megadrive-crt, storm-studio)
    /// и реальные файлы .png/.jpg/.slangp.
    /// </summary>
    public static ImageSource? RenderBezelFrameBitmap(string? presetOrPath, int width = 1280, int height = 720, bool withSampleScreen = false)
    {
        string canon = NormalizeBezelId(presetOrPath);
        if (canon == "none" && !withSampleScreen) return null;

        string cacheKey = $"{canon}|{width}|{height}|{withSampleScreen}";
        if (_bezelPreviewCache.TryGetValue(cacheKey, out var cached))
            return cached;

        // 1. Если передан существующий файл изображения
        if (!string.IsNullOrEmpty(presetOrPath) && File.Exists(presetOrPath))
        {
            try
            {
                string ext = Path.GetExtension(presetOrPath).ToLowerInvariant();
                string imagePathToLoad = presetOrPath;

                if (ext == ".slangp")
                {
                    // Поиск текстуры в файле шейдера
                    string dir = Path.GetDirectoryName(presetOrPath) ?? "";
                    string content = File.ReadAllText(presetOrPath);
                    var match = Regex.Match(content, @"(?:BackgroundImage|bezel_texture)\s*=\s*""?([^""\r\n]+)""?", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string rel = match.Groups[1].Value.Trim().Replace('/', Path.DirectorySeparatorChar);
                        string candidate = Path.Combine(dir, rel);
                        if (File.Exists(candidate)) imagePathToLoad = candidate;
                    }
                }

                if (File.Exists(imagePathToLoad) && (imagePathToLoad.EndsWith(".png") || imagePathToLoad.EndsWith(".jpg") || imagePathToLoad.EndsWith(".webp")))
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(Path.GetFullPath(imagePathToLoad));
                    bi.DecodePixelWidth = Math.Min(width, 1920);
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();
                    _bezelPreviewCache[cacheKey] = bi;
                    return bi;
                }
            }
            catch { }
        }

        // 2. Генерация студийного графического безеля
        using var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);

            // Вычисляем область экрана 4:3 по центру рамки
            int screenMarginY = height / 14;
            int screenHeight = height - (screenMarginY * 2);
            int screenWidth = (screenHeight * 4) / 3;
            if (canon == "handheld-gba")
            {
                // Для GBA формат экрана 3:2
                screenWidth = (screenHeight * 3) / 2;
            }
            int screenX = (width - screenWidth) / 2;
            int screenY = screenMarginY;

            // Если запрошен образец игры, рисуем ретро-сцену внутри экрана
            if (withSampleScreen)
            {
                using var sample = new Bitmap(screenWidth, screenHeight);
                using (var sg = Graphics.FromImage(sample))
                {
                    using (var sky = new System.Drawing.Drawing2D.LinearGradientBrush(
                        new System.Drawing.Point(0, 0),
                        new System.Drawing.Point(0, screenHeight),
                        System.Drawing.Color.FromArgb(16, 12, 38),
                        System.Drawing.Color.FromArgb(90, 20, 80)))
                    {
                        sg.FillRectangle(sky, 0, 0, screenWidth, screenHeight);
                    }
                    using (var ground = new SolidBrush(System.Drawing.Color.FromArgb(20, 15, 35)))
                    {
                        sg.FillRectangle(ground, 0, screenHeight * 13 / 20, screenWidth, screenHeight * 7 / 20);
                    }
                    using (var hero = new SolidBrush(System.Drawing.Color.FromArgb(255, 245, 158, 11)))
                    {
                        sg.FillRectangle(hero, screenWidth / 2 - 8, screenHeight * 13 / 20 - 16, 16, 20);
                    }
                }
                g.DrawImage(sample, screenX, screenY);
            }

            // Рисуем корпус в зависимости от пресета
            DrawStudioBezelFrame(g, canon, width, height, screenX, screenY, screenWidth, screenHeight);
        }

        var res = BitmapToBitmapSource(bmp);
        _bezelPreviewCache[cacheKey] = res;
        return res;
    }

    private static void DrawStudioBezelFrame(Graphics g, string canon, int w, int h, int sx, int sy, int sw, int sh)
    {
        // Внешняя маска корпуса (всё, кроме внутреннего экрана)
        using var path = new GraphicsPath();
        path.AddRectangle(new Rectangle(0, 0, w, h));
        path.AddRectangle(new Rectangle(sx, sy, sw, sh));

        System.Drawing.Color bodyColor;
        System.Drawing.Color accentColor;
        string labelText = "";

        switch (canon)
        {
            case "arcade-neon":
                bodyColor = System.Drawing.Color.FromArgb(245, 14, 12, 22);
                accentColor = System.Drawing.Color.FromArgb(255, 244, 63, 94);
                labelText = "ARCADE CLASSICS";
                break;
            case "crt-sony":
                bodyColor = System.Drawing.Color.FromArgb(245, 24, 26, 32);
                accentColor = System.Drawing.Color.FromArgb(255, 59, 130, 246);
                labelText = "Sony Trinitron PVM";
                break;
            case "handheld-gba":
                bodyColor = System.Drawing.Color.FromArgb(250, 49, 46, 129);
                accentColor = System.Drawing.Color.FromArgb(255, 168, 85, 247);
                labelText = "ADVANCE 32-BIT";
                break;
            case "snes-classic":
                bodyColor = System.Drawing.Color.FromArgb(250, 40, 42, 54);
                accentColor = System.Drawing.Color.FromArgb(255, 147, 51, 234);
                labelText = "SUPER NINTENDO";
                break;
            case "megadrive-crt":
                bodyColor = System.Drawing.Color.FromArgb(250, 16, 16, 20);
                accentColor = System.Drawing.Color.FromArgb(255, 234, 179, 8);
                labelText = "16-BIT HIGH DEFINITION";
                break;
            case "storm-studio":
            default:
                bodyColor = System.Drawing.Color.FromArgb(248, 10, 8, 20);
                accentColor = System.Drawing.Color.FromArgb(255, 168, 85, 247);
                labelText = "STORM AURORA FLUID";
                break;
        }

        // Заливка корпуса
        using (var brush = new SolidBrush(bodyColor))
        {
            g.FillPath(brush, path);
        }

        // Неоновый или металлический контур вокруг экрана
        using (var borderPen = new System.Drawing.Pen(accentColor, 3))
        {
            g.DrawRectangle(borderPen, sx - 2, sy - 2, sw + 4, sh + 4);
        }
        using (var innerShadow = new System.Drawing.Pen(System.Drawing.Color.FromArgb(160, 0, 0, 0), 4))
        {
            g.DrawRectangle(innerShadow, sx, sy, sw, sh);
        }

        // Шильдик / логотип на рамке
        using (var font = new System.Drawing.Font("Segoe UI", Math.Max(8, h / 45), System.Drawing.FontStyle.Bold))
        using (var textBrush = new SolidBrush(accentColor))
        {
            var size = g.MeasureString(labelText, font);
            g.DrawString(labelText, font, textBrush, (w - size.Width) / 2, sy + sh + ((h - (sy + sh) - size.Height) / 2));
        }
    }

    private static BitmapSource BitmapToBitmapSource(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            var bs = BitmapSource.Create(
                bitmap.Width, bitmap.Height,
                96, 96,
                System.Windows.Media.PixelFormats.Pbgra32,
                null,
                data.Scan0,
                data.Stride * bitmap.Height,
                data.Stride);
            bs.Freeze();
            return bs;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
