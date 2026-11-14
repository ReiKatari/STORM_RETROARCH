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
    private string _rawNoIntroName = "";
    private string _regionDisplay = "";
    private string _audioBadge = "";
    private string _gameCode = "";
    private string _revisionTag = "";

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
                OnPropertyChanged(nameof(SlotLaunch));
            }
        }
    }

    public string PlaytimeText => GameMetadataStore.FormatPlaytime(_totalPlaySeconds);

    public int LaunchCount
    {
        get => _launchCount;
        set
        {
            if (SetField(ref _launchCount, value))
            {
                OnPropertyChanged(nameof(SlotLaunch));
            }
        }
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
                OnPropertyChanged(nameof(SlotRating));
            }
        }
    }

    public string StarRatingDisplay => _starRating > 0
        ? $"{Math.Clamp(_starRating, 0, 5)}/5"
        : "0/5";

    public bool HasStarRating => _starRating > 0;

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
                OnPropertyChanged(nameof(SlotYear));
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
                OnPropertyChanged(nameof(SlotRegion));
                OnPropertyChanged(nameof(SlotAudio));
            }
        }
    }

    public string RawNoIntroName
    {
        get => _rawNoIntroName;
        set => SetField(ref _rawNoIntroName, value);
    }

    public string RegionDisplay
    {
        get => _regionDisplay;
        set => SetField(ref _regionDisplay, value);
    }

    public string LanguageBadge
    {
        get => _languageBadge;
        set
        {
            if (SetField(ref _languageBadge, value))
            {
                OnPropertyChanged(nameof(HasLanguageBadge));
                OnPropertyChanged(nameof(SlotTranslation));
                OnPropertyChanged(nameof(SlotAudio));
            }
        }
    }

    public string AudioBadge
    {
        get => _audioBadge;
        set
        {
            if (SetField(ref _audioBadge, value))
            {
                OnPropertyChanged(nameof(SlotAudio));
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
                OnPropertyChanged(nameof(SlotFiles));
            }
        }
    }

    public string Version => VersionBadge;

    public string GameCode
    {
        get => _gameCode;
        set => SetField(ref _gameCode, value);
    }

    public string RevisionTag
    {
        get => _revisionTag;
        set => SetField(ref _revisionTag, value);
    }

    public void EnsureRomHeaderMetadata()
    {
        if (!string.IsNullOrEmpty(_gameCode) && !string.IsNullOrEmpty(_revisionTag))
            return;

        if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
            return;

        try
        {
            var meta = CoverArtService.ReadRomHeaderMetadata(FilePath);
            if (!string.IsNullOrEmpty(meta.GameCode) && string.IsNullOrEmpty(_gameCode))
            {
                GameCode = meta.GameCode;
            }
            if (!string.IsNullOrEmpty(meta.RevisionTag) && string.IsNullOrEmpty(_revisionTag))
            {
                RevisionTag = meta.RevisionTag;
            }
            if (!string.IsNullOrEmpty(meta.Region) && string.IsNullOrEmpty(Region))
            {
                Region = meta.Region;
            }
        }
        catch { }
    }

    public bool HasYear => !string.IsNullOrEmpty(Year);
    public bool HasRegion => !string.IsNullOrEmpty(Region);
    public bool HasLanguageBadge => !string.IsNullOrEmpty(LanguageBadge);
    public bool HasVersionBadge => !string.IsNullOrEmpty(VersionBadge);

    // Structured Fixed-Slot Card Metadata Properties (7 consistent card slots)
    public string SlotYear => !string.IsNullOrWhiteSpace(_year) ? _year : "-";
    public string SlotRegion => !string.IsNullOrWhiteSpace(_region) ? _region : "GLOBAL";
    public string SlotTranslation => !string.IsNullOrWhiteSpace(_languageBadge) ? _languageBadge : "Оригинал";
    public string SlotAudio => !string.IsNullOrWhiteSpace(_audioBadge) ? _audioBadge : ResolveDefaultAudioSlot(_languageBadge, _region);
    public string SlotFiles => !string.IsNullOrWhiteSpace(_versionBadge)
        ? _versionBadge
        : (!string.IsNullOrWhiteSpace(_extension) ? $"1G • {_extension.TrimStart('.').ToUpperInvariant()}" : "1G");
    public string SlotRating => StarRatingDisplay;
    public string SlotLaunch => _launchCount > 0 ? $"{PlaytimeText} ({_launchCount})" : "Не запускалась";

    private static string ResolveDefaultAudioSlot(string? langBadge, string? region)
    {
        string lb = (langBadge ?? "").Trim().ToUpperInvariant();
        string reg = (region ?? "").Trim().ToUpperInvariant();
        if (lb.Contains("SND") || lb.Contains("DUB") || lb.Contains("VOICE") || lb.Contains("S. RUS") || lb.Contains("ОЗВУЧ"))
            return "RUS";
        if (lb == "RUS" || reg == "RU") return "RUS";
        if (lb == "ENG" || reg == "US" || reg == "EU" || reg == "WORLD") return "ENG";
        if (lb == "JPN" || reg == "JP") return "JPN";
        return "Оригинал";
    }

    public static string ParseAudioBadge(string fileName, string langBadge, string region)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return ResolveDefaultAudioSlot(langBadge, region);
        string baseName = Path.GetFileNameWithoutExtension(fileName);
        var matches = Regex.Matches(baseName, @"\[([^\[\]]+)\]");
        foreach (Match m in matches)
        {
            string tok = m.Groups[1].Value.Trim().ToUpperInvariant();
            if (tok.Contains("VOX") || tok.Contains("SND") || tok.Contains("DUB") || tok.Contains("VOICE") || tok.Contains("ОЗВУЧ"))
            {
                if (tok.Contains("RUS")) return "RUS";
                if (tok.Contains("ENG")) return "ENG";
                if (tok.Contains("JPN")) return "JPN";
                return tok;
            }
            if (tok.Contains("S. RUS") || tok.Contains("M. RUS + S"))
                return "RUS";
        }
        return ResolveDefaultAudioSlot(langBadge, region);
    }

    public string FilePath
    {
        get => _filePath;
        set
        {
            if (SetField(ref _filePath, value))
            {
                ParseAndUpdateFromFileName(Path.GetFileName(value));
                if (string.IsNullOrEmpty(Extension) && !string.IsNullOrEmpty(value))
                {
                    try { Extension = Path.GetExtension(value).ToLowerInvariant(); } catch { }
                }
                DetectPlatformAndCoreFromFile(value);
            }
        }
    }

    public void DetectPlatformAndCoreFromFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        try
        {
            var (plat, dll, name) = CoreManager.GetBestCoreForRom(filePath);
            if (!string.IsNullOrEmpty(plat) && (string.IsNullOrEmpty(Platform) || Platform == "Неизвестная платформа" || Platform == "Sony PlayStation / DVD" || Platform == "Arcade / Neo Geo"))
            {
                Platform = plat;
            }
            if (!string.IsNullOrEmpty(dll) && (string.IsNullOrEmpty(CoreDll) || CoreDll == "swanstation_libretro.dll" || CoreDll == "fbneo_libretro.dll" || CoreDll.Equals("beetle_saturn_libretro.dll", StringComparison.OrdinalIgnoreCase)))
            {
                CoreDll = dll;
                CoreDisplayName = name;
            }
        }
        catch { }
    }

    public string Extension
    {
        get => _extension;
        set
        {
            if (SetField(ref _extension, value))
            {
                OnPropertyChanged(nameof(SlotFiles));
            }
        }
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
        var (cleanName, yr, reg, lang, ver, rawNoIntro, regDisp) = ParseFileNameDetailed(fileName, FilePath);
        if (!string.IsNullOrEmpty(cleanName)) Name = cleanName;
        if (!string.IsNullOrEmpty(yr)) Year = yr;
        if (!string.IsNullOrEmpty(reg)) Region = reg;
        if (!string.IsNullOrEmpty(regDisp)) RegionDisplay = regDisp;
        if (!string.IsNullOrEmpty(lang)) LanguageBadge = lang;
        if (!string.IsNullOrEmpty(ver)) VersionBadge = ver;
        if (!string.IsNullOrEmpty(rawNoIntro)) RawNoIntroName = rawNoIntro;
        AudioBadge = ParseAudioBadge(fileName, LanguageBadge, Region);
        ParseGameCodeAndRevision(fileName);
    }

    public void ParseGameCodeAndRevision(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return;

        // 1. Revision from version or rev tokens: e.g. 1.1 -> Rev 1, 1.2 -> Rev 2, Rev A, Rev 1
        var mRevExplicit = Regex.Match(fileName, @"\((Rev\s*[A-Za-z0-9]+)\)", RegexOptions.IgnoreCase);
        if (mRevExplicit.Success)
        {
            RevisionTag = mRevExplicit.Groups[1].Value.Trim();
        }
        else
        {
            var mVer = Regex.Match(fileName, @"\((?:v)?(\d+)\.(\d+)(?:\s*-\s*([^()]+))?\)", RegexOptions.IgnoreCase);
            if (mVer.Success)
            {
                int major = int.TryParse(mVer.Groups[1].Value, out int maj) ? maj : 0;
                int minor = int.TryParse(mVer.Groups[2].Value, out int min) ? min : 0;
                if (major >= 1 && minor > 0)
                {
                    RevisionTag = $"Rev {minor}";
                }

                if (mVer.Groups[3].Success && string.IsNullOrEmpty(_gameCode))
                {
                    string candCode = mVer.Groups[3].Value.Trim();
                    if (!Regex.IsMatch(candCode, @"^[0-9A-Fa-f]{8}$")) // Not just a CRC32
                    {
                        GameCode = candCode;
                    }
                }
            }
        }

        // 2. Serial code scanning: SHVC-*, SNS-*, SLPS-*, SLUS-*, SLES-*, SCUS-*, SCES-*, MK-*, AGB-*, NTR-*, NUS-*, DOL-*
        if (string.IsNullOrEmpty(_gameCode))
        {
            var mSerial = Regex.Match(fileName, @"\b(SHVC-[A-Z0-9]+|SNS-[A-Z0-9]+|SLPS[-_]\d+|SLUS[-_]\d+|SLES[-_]\d+|SCUS[-_]\d+|SCES[-_]\d+|SCPS[-_]\d+|MK-\d+|AGB-[A-Z0-9]+|NTR-[A-Z0-9]+|NUS-[A-Z0-9]+|DOL-[A-Z0-9]+)\b", RegexOptions.IgnoreCase);
            if (mSerial.Success)
            {
                GameCode = mSerial.Groups[1].Value.Trim().ToUpperInvariant();
            }
        }
    }

    public static (string CleanName, string Year, string Region, string LanguageBadge, string VersionBadge) ParseFileName(string fileName)
    {
        var res = ParseFileNameDetailed(fileName, null);
        return (res.CleanName, res.Year, res.Region, res.LanguageBadge, res.VersionBadge);
    }

    public static (string CleanName, string Year, string Region, string LanguageBadge, string VersionBadge, string RawNoIntroName, string RegionDisplay) ParseFileNameDetailed(string fileName, string? filePath = null)
    {
        string baseName = Path.GetFileNameWithoutExtension(fileName).Trim();
        if (string.IsNullOrEmpty(baseName)) return ("", "", "", "", "", "", "");

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
        if (mVer.Success && !Regex.IsMatch(mVer.Groups[1].Value.Trim(), @"^(19\d{2}|20\d{2}|USA|Europe|Japan|World|Asia|Korea|Russia)$", RegexOptions.IgnoreCase))
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

        // 4. Square brackets [...] and language badges
        var bracketMatches = Regex.Matches(baseName, @"\[([^\[\]]+)\]");
        string explicitRegionInBrackets = "";
        string langBadge = "";

        var knownBracketRegions = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
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
            else if (knownBracketRegions.Contains(tok))
            {
                if (string.IsNullOrEmpty(explicitRegionInBrackets)) explicitRegionInBrackets = tok.ToUpperInvariant();
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
            else if (tok.Contains("RUS", StringComparison.OrdinalIgnoreCase) || tok.StartsWith("T+Rus", StringComparison.OrdinalIgnoreCase) || tok.StartsWith("T-Rus", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(langBadge)) langBadge = "RUS";
            }
        }

        // 5. Region detection from baseName and filePath
        string region = "";
        string regionDisplay = "";

        // Check combined regions first
        if (Regex.IsMatch(baseName, @"\((?:Japan,\s*USA|JU)\)", RegexOptions.IgnoreCase))
        {
            region = "JP-US";
            regionDisplay = "Япония / США";
        }
        else if (Regex.IsMatch(baseName, @"\((?:USA,\s*Europe|UE)\)", RegexOptions.IgnoreCase))
        {
            region = "US-EU";
            regionDisplay = "США / Европа";
        }
        else if (Regex.IsMatch(baseName, @"\((?:Japan|J|JP|Jpn|Asia|Japan,\s*Korea)\)|\bNTSC-J\b", RegexOptions.IgnoreCase))
        {
            region = "JP";
            regionDisplay = "Япония (NTSC-J)";
        }
        else if (Regex.IsMatch(baseName, @"\((?:Europe|E|EU|Euro|PAL|UK|Germany|G|France|F|Spain|S|Italy|I|Australia)\)", RegexOptions.IgnoreCase))
        {
            region = "EU";
            regionDisplay = "Европа (PAL)";
        }
        else if (Regex.IsMatch(baseName, @"\((?:USA|U|US|USA,\s*Canada|North\s*America)\)|\bNTSC-U\b", RegexOptions.IgnoreCase))
        {
            region = "US";
            regionDisplay = "США (NTSC-U)";
        }
        else if (Regex.IsMatch(baseName, @"\((?:World|W|WW)\)", RegexOptions.IgnoreCase))
        {
            region = "WORLD";
            regionDisplay = "Мир (World)";
        }
        else if (Regex.IsMatch(baseName, @"\((?:Korea|K|Kor)\)", RegexOptions.IgnoreCase))
        {
            region = "KR";
            regionDisplay = "Корея";
        }
        else if (Regex.IsMatch(baseName, @"\((?:Russia|R|Rus)\)", RegexOptions.IgnoreCase))
        {
            region = "RU";
            regionDisplay = "Россия";
        }
        else if (!string.IsNullOrEmpty(explicitRegionInBrackets))
        {
            region = explicitRegionInBrackets switch
            {
                "JP" or "AS" => "JP",
                "US" or "CA" => "US",
                "EU" or "UK" or "DE" or "FR" or "ES" or "IT" => "EU",
                "WW" => "WORLD",
                "KR" => "KR",
                "RU" => "RU",
                _ => explicitRegionInBrackets
            };
            regionDisplay = region switch
            {
                "JP" => "Япония (NTSC-J)",
                "US" => "США (NTSC-U)",
                "EU" => "Европа (PAL)",
                "WORLD" => "Мир (World)",
                "KR" => "Корея",
                "RU" => "Россия",
                _ => region
            };
        }

        // Folder check if region still empty
        if (string.IsNullOrEmpty(region) && !string.IsNullOrWhiteSpace(filePath))
        {
            string normPath = filePath.Replace('/', '\\');
            var segments = normPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            foreach (var seg in segments)
            {
                string segUp = seg.Trim().ToUpperInvariant();
                if (segUp is "FAMICOM" or "SUPER FAMICOM" or "JAPAN" or "NTSC-J")
                {
                    region = "JP";
                    regionDisplay = "Япония (NTSC-J)";
                    break;
                }
                if (segUp is "EUROPE" or "PAL")
                {
                    region = "EU";
                    regionDisplay = "Европа (PAL)";
                    break;
                }
                if (segUp is "USA" or "NTSC-U" or "NORTH AMERICA")
                {
                    region = "US";
                    regionDisplay = "США (NTSC-U)";
                    break;
                }
            }
        }

        // 6. Form RawNoIntroName:
        // Name without extension, with square brackets stripped, multi-track (Track X) stripped,
        // and GoodTools abbreviations converted to No-Intro Libretro standard:
        // (J) -> (Japan), (U) -> (USA), (E) -> (Europe), (W) -> (World), (JU) -> (Japan, USA), (UE) -> (USA, Europe), (K) -> (Korea)
        string noTrack = Regex.Replace(baseName, @"\s*\((?:Track|track)\s*\d+\)", "", RegexOptions.IgnoreCase);
        string noBrackets = Regex.Replace(noTrack, @"\s*\[[^\]]*\]", "").Trim();

        string rawNoIntro = Regex.Replace(noBrackets, @"\(([A-Za-z0-9,\s]+)\)", m =>
        {
            string code = m.Groups[1].Value.Trim().ToUpperInvariant();
            return code switch
            {
                "J" or "JP" or "JPN" => "(Japan)",
                "U" or "US" => "(USA)",
                "E" or "EU" or "EURO" => "(Europe)",
                "W" => "(World)",
                "JU" => "(Japan, USA)",
                "UE" => "(USA, Europe)",
                "K" or "KOR" => "(Korea)",
                "R" or "RUS" => "(Russia)",
                _ => m.Value
            };
        });
        rawNoIntro = Regex.Replace(rawNoIntro, @"\s+", " ").Trim();

        // 7. Combined version badge (Never prefix with 'v')
        string cleanVerPart = verPart.TrimStart('v', 'V').Trim();
        string combinedVersion = cleanVerPart;
        if (!string.IsNullOrEmpty(contentPart))
        {
            combinedVersion = !string.IsNullOrEmpty(combinedVersion) ? $"{combinedVersion} • {contentPart}" : contentPart;
        }

        // 8. Clean Title: Strictly the title BEFORE year / brackets / parens
        var mEarliest = Regex.Match(baseName, @"(\((19\d{2}|20\d{2})\)|\[|\((USA|Europe|Japan|World|J|U|E|W|JU|UE|K|JP|US|EU|RU|Russia))", RegexOptions.IgnoreCase);
        string cleanTitle = mEarliest.Success ? baseName.Substring(0, mEarliest.Index).Trim() : baseName;
        cleanTitle = Regex.Replace(cleanTitle, @"\[[^\[\]]*\]|\([^()]*\)", "").Trim();
        cleanTitle = Regex.Replace(cleanTitle, @"\s+", " ").Trim();
        if (string.IsNullOrEmpty(cleanTitle)) cleanTitle = baseName;

        return (cleanTitle, year, region, langBadge, combinedVersion, rawNoIntro, regionDisplay);
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
