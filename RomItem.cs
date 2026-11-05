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
        get => _icon;
        set => SetField(ref _icon, value);
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetField(ref _isFavorite, value);
    }

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

    public void GeneratePixelIcon()
    {
        try
        {
            int w = 32, h = 32;
            var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            byte[] pixels = new byte[w * h * 4];

            uint hash = (uint)Math.Abs(Platform.GetHashCode());
            byte r = (byte)(60 + (hash & 0x7F));
            byte g = (byte)(60 + ((hash >> 7) & 0x7F));
            byte b = (byte)(80 + ((hash >> 14) & 0x7F));

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = (y * w + x) * 4;
                    bool isBorder = x == 0 || x == w - 1 || y == 0 || y == h - 1;
                    bool isCartridgeNotch = (y < 4 && (x < 4 || x > w - 5));
                    bool isLabelArea = (y >= 8 && y <= 22 && x >= 5 && x <= w - 6);

                    if (isCartridgeNotch)
                    {
                        pixels[idx] = 0;
                        pixels[idx + 1] = 0;
                        pixels[idx + 2] = 0;
                        pixels[idx + 3] = 0;
                    }
                    else if (isBorder)
                    {
                        pixels[idx] = 20;
                        pixels[idx + 1] = 16;
                        pixels[idx + 2] = 30;
                        pixels[idx + 3] = 255;
                    }
                    else if (isLabelArea)
                    {
                        pixels[idx] = (byte)Math.Min(255, b + 40);
                        pixels[idx + 1] = (byte)Math.Min(255, g + 20);
                        pixels[idx + 2] = (byte)Math.Min(255, r + 40);
                        pixels[idx + 3] = 255;
                    }
                    else
                    {
                        pixels[idx] = b;
                        pixels[idx + 1] = g;
                        pixels[idx + 2] = r;
                        pixels[idx + 3] = 255;
                    }
                }
            }

            wb.WritePixels(new System.Windows.Int32Rect(0, 0, w, h), pixels, w * 4, 0);
            wb.Freeze();
            Icon = wb;
        }
        catch { }
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
