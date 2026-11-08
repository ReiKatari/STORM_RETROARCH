using System.Collections.Concurrent;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StormRetroArch;

public class RomItem : INotifyPropertyChanged
{
    private string _name = "";
    private string _filePath = "";
    private string _extension = "";
    private string _platform = "";
    private string _coreDll = "";
    private string _coreDisplayName = "";
    private long _fileSizeBytes;
    private string _formattedSize = "";
    private bool _isCoreInstalled;
    private string _statusText = "";
    private double _progressValue;
    private bool _isDownloading;
    private ImageSource? _icon;
    private bool _isFavorite;
    private DateTime? _lastPlayed;
    private string _lastPlayedFormatted = "-";

    // Metadata Badges (Ported from STORM GAMES WORLD)
    private string _year = "";
    private string _region = "";
    private string _languageBadge = "";
    private string _versionBadge = "";

    // Playtime, Launch & Library Metadata (1.6.0)
    private long _totalPlaySeconds;
    private int _launchCount;
    private string _completionStatus = "Не начато";
    private int _starRating = 0;
    private string _userNotes = "";
    private string _customCollection = "";

    public long TotalPlaySeconds
    {
        get => _totalPlaySeconds;
        set
        {
            if (SetField(ref _totalPlaySeconds, value))
            {
                OnPropertyChanged(nameof(PlaytimeText));
            }
        }
    }

    public string PlaytimeText => GameMetadataStore.FormatPlaytime(_totalPlaySeconds);

    public int LaunchCount
    {
        get => _launchCount;
        set => SetField(ref _launchCount, value);
    }

    public string CompletionStatus
    {
        get => _completionStatus;
        set
        {
            if (SetField(ref _completionStatus, value))
            {
                OnPropertyChanged(nameof(HasCompletionBadge));
            }
        }
    }

    public bool HasCompletionBadge => !string.IsNullOrEmpty(_completionStatus) && _completionStatus != "Не начато";

    public int StarRating
    {
        get => _starRating;
        set
        {
            if (SetField(ref _starRating, value))
            {
                OnPropertyChanged(nameof(StarRatingDisplay));
            }
        }
    }

    public string StarRatingDisplay => _starRating > 0
        ? (new string('★', Math.Clamp(_starRating, 0, 5)) + new string('☆', 5 - Math.Clamp(_starRating, 0, 5)))
        : "☆☆☆☆☆";

    public string UserNotes
    {
        get => _userNotes;
        set => SetField(ref _userNotes, value);
    }

    public string CustomCollection
    {
        get => _customCollection;
        set => SetField(ref _customCollection, value);
    }

    public string Title => Name;
    public string Crc32 { get; set; } = "";

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Year
    {
        get => _year;
        set
        {
            if (SetField(ref _year, value))
            {
                OnPropertyChanged(nameof(HasYear));
            }
        }
    }

    public string Region
    {
        get => _region;
        set
        {
            if (SetField(ref _region, value))
            {
                OnPropertyChanged(nameof(HasRegion));
            }
        }
    }

    public string LanguageBadge
    {
        get => _languageBadge;
        set
        {
            if (SetField(ref _languageBadge, value))
            {
                OnPropertyChanged(nameof(HasLanguageBadge));
            }
        }
    }

    public string VersionBadge
    {
        get => _versionBadge;
        set
        {
            string clean = value ?? "";
            clean = Regex.Replace(clean, @"(?<=^|[\s?(\[])[vV](?=\d)", "").Trim();
            if (SetField(ref _versionBadge, clean))
            {
                OnPropertyChanged(nameof(HasVersionBadge));
                OnPropertyChanged(nameof(Version));
            }
        }
    }

    public string Version => VersionBadge;

    public bool HasYear => !string.IsNullOrEmpty(Year);
    public bool HasRegion => !string.IsNullOrEmpty(Region);
    public bool HasLanguageBadge => !string.IsNullOrEmpty(LanguageBadge);
    public bool HasVersionBadge => !string.IsNullOrEmpty(VersionBadge);

    public string FilePath
    {
        get => _filePath;
        set
        {
            if (SetField(ref _filePath, value))
            {
                ParseAndUpdateFromFileName(Path.GetFileName(value));
            }
        }
    }

    public string Extension
    {
        get => _extension;
        set => SetField(ref _extension, value);
    }

    public string Platform
    {
        get => _platform;
        set => SetField(ref _platform, value);
    }

    public string CoreDll
    {
        get => _coreDll;
        set => SetField(ref _coreDll, value);
    }

    public string CoreDisplayName
    {
        get => _coreDisplayName;
        set => SetField(ref _coreDisplayName, value);
    }

    public long FileSizeBytes
    {
        get => _fileSizeBytes;
        set
        {
            if (SetField(ref _fileSizeBytes, value))
            {
                FormattedSize = FormatFileSize(value);
            }
        }
    }

    public string FormattedSize
    {
        get => _formattedSize;
        set => SetField(ref _formattedSize, value);
    }

    public bool IsCoreInstalled
    {
        get => _isCoreInstalled;
        set => SetField(ref _isCoreInstalled, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        set => SetField(ref _progressValue, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set => SetField(ref _isDownloading, value);
    }

    public ImageSource? Icon
    {
        get
        {
            if (_icon == null)
            {
                _icon = GetCachedPlatformIcon(Platform);
            }
            return _icon;
        }
        set => SetField(ref _icon, value);
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (SetField(ref _isFavorite, value))
            {
                OnPropertyChanged(nameof(FavoriteIconData));
                OnPropertyChanged(nameof(FavoriteStarBrush));
                OnPropertyChanged(nameof(FavoriteBgBrush));
                OnPropertyChanged(nameof(FavoriteBorderBrush));
                OnPropertyChanged(nameof(FavoriteLabel));
                OnPropertyChanged(nameof(FavoriteTooltip));
            }
        }
    }

    public string FavoriteIconData => _isFavorite
        ? "M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z"
        : "M22 9.24l-7.19-.62L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21 12 17.27 18.18 21l-1.63-7.03L22 9.24zM12 15.4l-3.76 2.27 1-4.28-3.32-2.88 4.38-.38L12 6.1l1.71 4.04 4.38.38-3.32 2.88 1 4.28L12 15.4z";

    public string FavoriteStarBrush => _isFavorite ? "#FBBF24" : "#94A3B8";
    public string FavoriteBgBrush => _isFavorite ? "#28F59E0B" : "#18142C";
    public string FavoriteBorderBrush => _isFavorite ? "#F59E0B" : "#3A315E";
    public string FavoriteLabel => _isFavorite ? "В избранном" : "В избранное";
    public string FavoriteTooltip => _isFavorite ? "В избранном (нажмите, чтобы убрать из Избранного)" : "Добавить игру в Избранное";

    public DateTime? LastPlayed
    {
        get => _lastPlayed;
        set
        {
            if (SetField(ref _lastPlayed, value))
            {
                LastPlayedFormatted = value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm") : "-";
            }
        }
    }

    public string LastPlayedFormatted
    {
        get => _lastPlayedFormatted;
        set => SetField(ref _lastPlayedFormatted, value);
    }

    public static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F1} MB";
        return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
    }

    public void ParseAndUpdateFromFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return;
        var (cleanName, yr, reg, lang, ver) = ParseFileName(fileName);
        if (!string.IsNullOrEmpty(cleanName)) Name = cleanName;
        if (!string.IsNullOrEmpty(yr)) Year = yr;
        if (!string.IsNullOrEmpty(reg)) Region = reg;
        if (!string.IsNullOrEmpty(lang)) LanguageBadge = lang;
        if (!string.IsNullOrEmpty(ver)) VersionBadge = ver;
    }

    public static (string CleanName, string Year, string Region, string LanguageBadge, string VersionBadge) ParseFileName(string fileName)
    {
        string baseName = Path.GetFileNameWithoutExtension(fileName).Trim();
        if (string.IsNullOrEmpty(baseName)) return ("", "", "", "", "");

        // 1. Content counter like (1G+1M)
        string contentPart = "";
        var mCnt = Regex.Match(baseName, @"\(((\d+G(?:\+\d+[UDM])*))\)\s*$", RegexOptions.IgnoreCase);
        if (mCnt.Success)
        {
            contentPart = mCnt.Groups[1].Value.ToUpperInvariant();
            baseName = baseName.Substring(0, mCnt.Index).Trim();
        }

        // 2. Version / CRC / Serial like (1.0 - 9806CB84) or (1.0)
        string verPart = "";
        var mVer = Regex.Match(baseName, @"\(([^()\[\]]+?)\)\s*$");
        if (mVer.Success && !Regex.IsMatch(mVer.Groups[1].Value.Trim(), @"^(19\d{2}|20\d{2}|USA|Europe|Japan|World)$", RegexOptions.IgnoreCase))
        {
            string vStr = mVer.Groups[1].Value.Trim();
            var parts = vStr.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                string verNum = parts[0].Trim();
                if (verNum.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    verNum = verNum.Substring(1).Trim();
                verPart = verNum;
            }
            baseName = baseName.Substring(0, mVer.Index).Trim();
        }

        // 3. Year like (1991)
        string year = "";
        var mYr = Regex.Match(baseName, @"\((19\d{2}|20\d{2})\)");
        if (mYr.Success)
        {
            year = mYr.Groups[1].Value;
        }

        // 4. Square brackets [...]
        var bracketMatches = Regex.Matches(baseName, @"\[([^\[\]]+)\]");
        string region = "";
        string langBadge = "";

        var knownRegions = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "WW", "US", "EU", "JP", "RU", "AS", "KR", "CN", "TW", "BR", "AU", "DE", "FR", "ES", "IT", "UK", "NL", "SE", "CA"
        };

        foreach (Match match in bracketMatches)
        {
            string tok = match.Groups[1].Value.Trim();
            if (tok.StartsWith("MOD -", StringComparison.OrdinalIgnoreCase) || tok.StartsWith("MOD-", StringComparison.OrdinalIgnoreCase))
            {
                langBadge = tok;
            }
            else if (knownRegions.Contains(tok))
            {
                if (string.IsNullOrEmpty(region)) region = tok.ToUpperInvariant();
            }
            else if (tok.Equals("RUS", StringComparison.OrdinalIgnoreCase) ||
                     tok.Equals("ENG", StringComparison.OrdinalIgnoreCase) ||
                     tok.Equals("JPN", StringComparison.OrdinalIgnoreCase) ||
                     tok.Equals("GER", StringComparison.OrdinalIgnoreCase) ||
                     tok.Equals("FRA", StringComparison.OrdinalIgnoreCase) ||
                     tok.Equals("SPA", StringComparison.OrdinalIgnoreCase) ||
                     tok.Equals("ITA", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(langBadge)) langBadge = tok.ToUpperInvariant();
            }
        }

        // 5. Legacy region parens
        if (string.IsNullOrEmpty(region))
        {
            if (Regex.IsMatch(baseName, @"\(USA\)", RegexOptions.IgnoreCase)) region = "US";
            else if (Regex.IsMatch(baseName, @"\(Europe\)", RegexOptions.IgnoreCase)) region = "EU";
            else if (Regex.IsMatch(baseName, @"\(Japan\)", RegexOptions.IgnoreCase)) region = "JP";
            else if (Regex.IsMatch(baseName, @"\(World\)", RegexOptions.IgnoreCase)) region = "WW";
        }

        // 6. Combined version badge (Never prefix with 'v')
        string cleanVerPart = verPart.TrimStart('v', 'V').Trim();
        string combinedVersion = cleanVerPart;
        if (!string.IsNullOrEmpty(contentPart))
        {
            combinedVersion = !string.IsNullOrEmpty(combinedVersion) ? $"{combinedVersion} • {contentPart}" : contentPart;
        }

        // 7. Clean Title: Strictly the title BEFORE year / brackets / parens
        var mEarliest = Regex.Match(baseName, @"(\((19\d{2}|20\d{2})\)|\[|\((USA|Europe|Japan|World))", RegexOptions.IgnoreCase);
        string cleanTitle = mEarliest.Success ? baseName.Substring(0, mEarliest.Index).Trim() : baseName;
        cleanTitle = Regex.Replace(cleanTitle, @"\[[^\[\]]*\]|\([^()]*\)", "").Trim();
        cleanTitle = Regex.Replace(cleanTitle, @"\s+", " ").Trim();

        return (cleanTitle, year, region, langBadge, combinedVersion);
    }

    private static readonly ConcurrentDictionary<string, BitmapSource> _platformIconCache = new(StringComparer.OrdinalIgnoreCase);

    public static BitmapSource GetCachedPlatformIcon(string? platform)
    {
        string key = string.IsNullOrWhiteSpace(platform) ? "RETRO" : platform.Trim();
        return _platformIconCache.GetOrAdd(key, CreatePlatformIconBitmap);
    }

    private static BitmapSource CreatePlatformIconBitmap(string platformKey)
    {
        try
        {
            int w = 280, h = 380;
            using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

                // 1. Dark Gradient Background
                using (var bgBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new System.Drawing.Rectangle(0, 0, w, h),
                    System.Drawing.Color.FromArgb(24, 27, 38),
                    System.Drawing.Color.FromArgb(11, 12, 16),
                    System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                {
                    g.FillRectangle(bgBrush, 0, 0, w, h);
                }

                // Compute color scheme from Platform hash
                uint hash = (uint)Math.Abs(platformKey.GetHashCode());
                byte accentR = (byte)(80 + (hash & 0x7F));
                byte accentG = (byte)(80 + ((hash >> 7) & 0x7F));
                byte accentB = (byte)(140 + ((hash >> 14) & 0x7F));
                var accentColor = System.Drawing.Color.FromArgb(accentR, accentG, accentB);

                // 2. Top Neon Stripe
                using (var neonPen = new System.Drawing.Pen(accentColor, 4f))
                {
                    g.DrawLine(neonPen, 0, 2, w, 2);
                }

                // 3. Subtle background radial glow
                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    path.AddEllipse(20, 60, 240, 200);
                    using var pgb = new System.Drawing.Drawing2D.PathGradientBrush(path)
                    {
                        CenterColor = System.Drawing.Color.FromArgb(40, accentColor),
                        SurroundColors = new[] { System.Drawing.Color.FromArgb(0, 0, 0, 0) }
                    };
                    g.FillPath(pgb, path);
                }

                // 4. Platform pill badge at top
                string platBadge = platformKey.ToUpperInvariant();
                if (platBadge.Length > 20) platBadge = platBadge.Substring(0, 20);
                using (var pillBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(45, 50, 70)))
                using (var pillPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(80, accentColor), 1f))
                using (var fontBadge = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold))
                {
                    var badgeSize = g.MeasureString(platBadge, fontBadge);
                    float badgeW = Math.Max(70, badgeSize.Width + 24);
                    float badgeH = 26;
                    float badgeX = (w - badgeW) / 2f;
                    float badgeY = 22;

                    using var badgePath = new System.Drawing.Drawing2D.GraphicsPath();
                    badgePath.AddArc(badgeX, badgeY, badgeH, badgeH, 90, 180);
                    badgePath.AddArc(badgeX + badgeW - badgeH, badgeY, badgeH, badgeH, 270, 180);
                    badgePath.CloseFigure();
                    g.FillPath(pillBrush, badgePath);
                    g.DrawPath(pillPen, badgePath);

                    using var badgeTextBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(220, 225, 235));
                    using var sfBadge = new System.Drawing.StringFormat { Alignment = System.Drawing.StringAlignment.Center, LineAlignment = System.Drawing.StringAlignment.Center };
                    g.DrawString(platBadge, fontBadge, badgeTextBrush, new System.Drawing.RectangleF(badgeX, badgeY, badgeW, badgeH), sfBadge);
                }

                // 5. Stylized Central Emblem (Retro Gamepad / Cartridge silhouette)
                float cx = w / 2f;
                float cy = 160f;
                using (var emblemPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(120, accentColor), 2f))
                using (var emblemFill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(30, accentColor)))
                {
                    g.FillEllipse(emblemFill, cx - 60, cy - 35, 120, 70);
                    g.DrawEllipse(emblemPen, cx - 60, cy - 35, 120, 70);

                    // D-Pad cross on left
                    g.FillRectangle(emblemPen.Brush, cx - 42, cy - 14, 20, 6);
                    g.FillRectangle(emblemPen.Brush, cx - 35, cy - 21, 6, 20);

                    // Action buttons on right
                    g.FillEllipse(emblemPen.Brush, cx + 22, cy - 10, 8, 8);
                    g.FillEllipse(emblemPen.Brush, cx + 34, cy - 2, 8, 8);
                }

                // 6. Platform Title Text
                using (var titleFont = new System.Drawing.Font("Segoe UI", 13f, System.Drawing.FontStyle.Bold))
                using (var titleBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White))
                using (var sf = new System.Drawing.StringFormat
                {
                    Alignment = System.Drawing.StringAlignment.Center,
                    LineAlignment = System.Drawing.StringAlignment.Near,
                    Trimming = System.Drawing.StringTrimming.EllipsisWord
                })
                {
                    var titleRect = new System.Drawing.RectangleF(16, 230, w - 32, 85);
                    g.DrawString(platformKey, titleFont, titleBrush, titleRect, sf);
                }

                // 7. Footer text
                using (var footerFont = new System.Drawing.Font("Segoe UI", 10f, System.Drawing.FontStyle.Regular))
                using (var footerBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(160, 170, 185)))
                using (var sfFooter = new System.Drawing.StringFormat
                {
                    Alignment = System.Drawing.StringAlignment.Center,
                    LineAlignment = System.Drawing.StringAlignment.Center
                })
                {
                    g.DrawString("STORM RETROARCH", footerFont, footerBrush, new System.Drawing.RectangleF(16, 335, w - 32, 25), sfFooter);
                }

                // 8. Outer Card Border
                using (var borderPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(42, 46, 61), 1f))
                {
                    g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
                }
            }

            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;

            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.DecodePixelWidth = 240;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            var empty = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
            empty.Freeze();
            return empty;
        }
    }

    public void GeneratePixelIcon()
    {
        if (_icon == null)
        {
            Icon = GetCachedPlatformIcon(Platform);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
