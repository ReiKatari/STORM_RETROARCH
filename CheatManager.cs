using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace StormRetroArch;

public class CheatItem : INotifyPropertyChanged
{
    private int _index;
    private string _description = "";
    private string _originalDescription = "";
    private string _code = "";
    private bool _isEnabled;
    private string _cheatType = "Код памяти / Патч";

    public int Index
    {
        get => _index;
        set { if (_index != value) { _index = value; OnPropertyChanged(); } }
    }

    public string Description
    {
        get => _description;
        set { if (_description != value) { _description = value; OnPropertyChanged(); } }
    }

    public string OriginalDescription
    {
        get => _originalDescription;
        set { if (_originalDescription != value) { _originalDescription = value; OnPropertyChanged(); } }
    }

    public string Code
    {
        get => _code;
        set { if (_code != value) { _code = value; OnPropertyChanged(); } }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { if (_isEnabled != value) { _isEnabled = value; OnPropertyChanged(); } }
    }

    public string CheatType
    {
        get => _cheatType;
        set { if (_cheatType != value) { _cheatType = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class CheatFileItem : INotifyPropertyChanged
{
    private string _gameName = "";
    private string _cleanTitle = "";
    private string _regionTag = "";
    private string _platform = "Библиотека игр";
    private string _rawPlatformFolder = "";
    private string _filePath = "";
    private bool _isLibraryGame;
    private bool _isDynamicTrainerProfile;
    private string _matchedRomPath = "";
    private int _cheatCount;
    private List<CheatItem>? _cheats;
    private bool _isLoaded;

    public string GameName
    {
        get => _gameName;
        set { if (_gameName != value) { _gameName = value; OnPropertyChanged(); } }
    }

    public string CleanTitle
    {
        get => _cleanTitle;
        set { if (_cleanTitle != value) { _cleanTitle = value; OnPropertyChanged(); } }
    }

    public string RegionTag
    {
        get => _regionTag;
        set { if (_regionTag != value) { _regionTag = value; OnPropertyChanged(); } }
    }

    public string Platform
    {
        get => _platform;
        set { if (_platform != value) { _platform = value; OnPropertyChanged(); } }
    }

    public string RawPlatformFolder
    {
        get => _rawPlatformFolder;
        set { if (_rawPlatformFolder != value) { _rawPlatformFolder = value; OnPropertyChanged(); } }
    }

    public string FilePath
    {
        get => _filePath;
        set { if (_filePath != value) { _filePath = value; OnPropertyChanged(); } }
    }

    public bool IsLibraryGame
    {
        get => _isLibraryGame;
        set { if (_isLibraryGame != value) { _isLibraryGame = value; OnPropertyChanged(); } }
    }

    public bool IsDynamicTrainerProfile
    {
        get => _isDynamicTrainerProfile;
        set { if (_isDynamicTrainerProfile != value) { _isDynamicTrainerProfile = value; OnPropertyChanged(); } }
    }

    public string MatchedRomPath
    {
        get => _matchedRomPath;
        set { if (_matchedRomPath != value) { _matchedRomPath = value; OnPropertyChanged(); } }
    }

    public int CheatCount
    {
        get => _cheatCount;
        set
        {
            if (_cheatCount != value)
            {
                _cheatCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CheatCountBadge));
            }
        }
    }

    public string CheatCountBadge
    {
        get
        {
            if (CheatCount > 0) return $"{CheatCount} читов";
            if (IsDynamicTrainerProfile) return "Тренер ОЗУ";
            return "База .cht";
        }
    }

    public List<CheatItem> Cheats
    {
        get
        {
            EnsureLoaded();
            return _cheats ??= new List<CheatItem>();
        }
        set
        {
            _cheats = value;
            _isLoaded = true;
            CheatCount = value?.Count ?? 0;
            OnPropertyChanged();
        }
    }

    public void EnsureLoaded()
    {
        if (_isLoaded) return;
        _isLoaded = true;

        try
        {
            if (!string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath))
            {
                _cheats = CheatManager.ParseCht(FilePath);
            }
            else if (IsDynamicTrainerProfile)
            {
                _cheats = CheatManager.BuildDefaultCheatsForGame(GameName, Platform);
            }
            else
            {
                _cheats = new List<CheatItem>();
            }
        }
        catch
        {
            _cheats = new List<CheatItem>();
        }

        CheatCount = _cheats.Count;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class CheatManager
{
    private static IEnumerable<RomItem>? _cachedLibraryGames;

    public static IEnumerable<RomItem>? CachedLibraryGames
    {
        get => _cachedLibraryGames;
        set => _cachedLibraryGames = value;
    }

    public static string CheatsDir
    {
        get
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(CoreManager.InstallDir) && Directory.Exists(CoreManager.InstallDir))
                {
                    string installCheats = Path.Combine(CoreManager.InstallDir, "cheats");
                    if (Directory.Exists(installCheats)) return installCheats;
                }

                string appCheats = Path.Combine(CoreManager.AppDir, "cheats");
                if (Directory.Exists(appCheats)) return appCheats;

                if (!string.IsNullOrWhiteSpace(CoreManager.InstallDir) && Directory.Exists(CoreManager.InstallDir))
                {
                    string installCheats = Path.Combine(CoreManager.InstallDir, "cheats");
                    Directory.CreateDirectory(installCheats);
                    return installCheats;
                }

                Directory.CreateDirectory(appCheats);
                return appCheats;
            }
            catch
            {
                return Path.Combine(CoreManager.AppDir, "cheats");
            }
        }
    }

    public static string NormalizeGameKey(string name) => NormalizeTitleKey(name);

    public static string NormalizeTitleKey(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        string s = title;

        // 1. Strip parenthetical and bracketed tokens
        s = Regex.Replace(s, @"\[[^\]]*\]", " ");
        s = Regex.Replace(s, @"\([^)]*\)", " ");

        // 2. Roman numerals normalized to Arabic numbers
        s = Regex.Replace(s, @"\bviii\b", "8", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bvii\b", "7", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\biii\b", "3", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\biv\b", "4", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bvi\b", "6", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bix\b", "9", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bii\b", "2", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bv\b", "5", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bx\b", "10", RegexOptions.IgnoreCase);

        // 3. Keep only letters and digits
        var sb = new StringBuilder();
        foreach (char c in s.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    public static string CleanGameTitle(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        string cleaned = Regex.Replace(name, @"\[[^\]]*\]", " ");
        cleaned = Regex.Replace(cleaned, @"\s*\([^)]*\)", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return string.IsNullOrEmpty(cleaned) ? name.Trim() : cleaned;
    }

    public static string DetectRegionTag(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        if (Regex.IsMatch(name, @"\b(RUS|Russia|Russian|MOD - M\. RUS)\b", RegexOptions.IgnoreCase)) return "RU";
        if (Regex.IsMatch(name, @"\b(Japan|Japanese|JPN)\b", RegexOptions.IgnoreCase)) return "JP";
        if (Regex.IsMatch(name, @"\b(USA|US)\b", RegexOptions.IgnoreCase)) return "US";
        if (Regex.IsMatch(name, @"\b(Europe|EUR|EU|En,Fr,De|En,Fr,De,Es,It)\b", RegexOptions.IgnoreCase)) return "EU";
        if (Regex.IsMatch(name, @"\b(World)\b", RegexOptions.IgnoreCase)) return "WORLD";
        if (Regex.IsMatch(name, @"\((J)\)", RegexOptions.IgnoreCase)) return "JP";
        if (Regex.IsMatch(name, @"\((U)\)", RegexOptions.IgnoreCase)) return "US";
        if (Regex.IsMatch(name, @"\((E)\)", RegexOptions.IgnoreCase)) return "EU";
        if (Regex.IsMatch(name, @"\((W)\)", RegexOptions.IgnoreCase)) return "WORLD";
        return "";
    }

    public static string NormalizeRegionCode(string? region)
    {
        if (string.IsNullOrWhiteSpace(region)) return "";
        string r = region.Trim().ToUpperInvariant();
        return r switch
        {
            "USA" or "US" or "U" => "US",
            "EUROPE" or "EUR" or "EU" or "E" => "EU",
            "JAPAN" or "JPN" or "JP" or "J" => "JP",
            "RUSSIA" or "RUS" or "RU" => "RU",
            "WORLD" or "WW" or "W" => "WORLD",
            _ => r
        };
    }

    public static string MapPlatformFolderToDisplayName(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || folder == "." || folder.Contains("библиотеки", StringComparison.OrdinalIgnoreCase))
            return "Игры библиотеки";

        string trimmed = folder.Trim().Trim('/', '\\');
        string baseFolder = Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(baseFolder)) baseFolder = trimmed;

        return baseFolder switch
        {
            "Nintendo - Nintendo Entertainment System" => "Nintendo Entertainment System",
            "Nintendo - Super Nintendo Entertainment System" => "Super Nintendo (SNES)",
            "Sega - Mega Drive - Genesis" => "Sega Genesis / Mega Drive",
            "Sony - PlayStation" => "Sony PlayStation 1",
            "Sony - PlayStation 2" => "Sony PlayStation 2",
            "Sony - PlayStation Portable" => "Sony PlayStation Portable",
            "Sony - PlayStation Vita" => "Sony PlayStation Vita",
            "Nintendo - Game Boy Advance" => "Game Boy Advance",
            "Nintendo - Game Boy" or "Nintendo - Game Boy Color" => "Game Boy / Color",
            "Nintendo - Nintendo 64" or "Nintendo - Nintendo 64 (Aleck64)" or "Nintendo - Nintendo 64 (Unreleased)" or "Nintendo - Nintendo 64 (iQue)" => "Nintendo 64",
            "Nintendo - Nintendo DS" => "Nintendo DS / DSi",
            "Nintendo - Nintendo 3DS" => "Nintendo 3DS",
            "Nintendo - GameCube" or "Nintendo - Wii" => "Nintendo GameCube / Wii",
            "Sega - Dreamcast" => "Sega Dreamcast",
            "Sega - Saturn" => "Sega Saturn",
            "Sega - Master System - Mark III" or "Sega - Game Gear" => "Sega Master System / Game Gear",
            "Sega - 32X" => "Sega 32X",
            "Sega - Mega-CD - Sega CD" => "Sega CD",
            "Sega - SG-1000" => "Sega SG-1000",
            "NEC - PC Engine - TurboGrafx 16" or "NEC - PC Engine CD - TurboGrafx-CD" or "NEC - PC Engine SuperGrafx" => "PC Engine / TurboGrafx-16",
            "NEC - PC-FX" => "NEC PC-FX",
            "FBNeo - Arcade Games" or "MAME" or "Arcade" => "Arcade Classics",
            "DOS" => "MS-DOS PC",
            "Commodore - Amiga" => "Commodore Amiga",
            "Commodore - 64" => "Commodore 64",
            "SNK - Neo Geo Pocket" or "SNK - Neo Geo Pocket Color" => "Neo Geo Pocket / Color",
            "Bandai - WonderSwan" or "Bandai - WonderSwan Color" => "WonderSwan / Color",
            "Atari - 2600" => "Atari 2600",
            "Atari - 5200" => "Atari 5200",
            "Atari - 7800" => "Atari 7800",
            "Atari - 8-bit Family" => "Atari 8-bit",
            "Atari - Lynx" => "Atari Lynx",
            "Atari - Jaguar" => "Atari Jaguar",
            "Nintendo - Virtual Boy" => "Nintendo Virtual Boy",
            "Nintendo - Family Computer Disk System" => "Famicom Disk System",
            "Sinclair - ZX Spectrum +3" or "Sinclair - ZX Spectrum" => "ZX Spectrum",
            "Coleco - ColecoVision" => "ColecoVision",
            "Mattel - Intellivision" => "Intellivision",
            "Microsoft - MSX" or "Microsoft - MSX2" => "MSX / MSX2",
            _ => CleanFallbackPlatform(baseFolder)
        };
    }

    private static string CleanFallbackPlatform(string folder)
    {
        if (folder.Contains(" - "))
        {
            var parts = folder.Split(" - ", StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) return $"{parts[0].Trim()} {parts[1].Trim()}";
        }
        return folder.Replace('\\', ' ').Replace('/', ' ').Trim();
    }

    public static string GetCanonicalPlatformGroup(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "unknown";
        string s = input.ToLowerInvariant();

        if (s.Contains("snes") || s.Contains("super nintendo") || s.Contains("super famicom")) return "snes";
        if (s.Contains("entertainment system") || s.Contains("nes") || s.Contains("famicom")) return "nes";
        if (s.Contains("mega drive") || s.Contains("genesis") || s.Contains("32x") || s.Contains("sega cd") || s.Contains("mega-cd") || s.Contains("sg-1000") || s.Contains("master system") || s.Contains("game gear")) return "sega";
        if (s.Contains("playstation 2") || s.Contains("ps2")) return "ps2";
        if (s.Contains("playstation portable") || s.Contains("psp")) return "psp";
        if (s.Contains("playstation vita") || s.Contains("psv") || s.Contains("vita")) return "vita";
        if (s.Contains("playstation") || s.Contains("ps1") || s.Contains("psx")) return "ps1";
        if (s.Contains("nintendo 64") || s.Contains("n64")) return "n64";
        if (s.Contains("game boy advance") || s.Contains("gba")) return "gba";
        if (s.Contains("game boy") || s.Contains("gbc") || s.Contains("dmg")) return "gb";
        if (s.Contains("nintendo 3ds") || s.Contains("3ds")) return "3ds";
        if (s.Contains("nintendo ds") || s.Contains("nds") || s.Contains("dsi")) return "nds";
        if (s.Contains("gamecube") || s.Contains("wii")) return "gc_wii";
        if (s.Contains("dreamcast")) return "dc";
        if (s.Contains("saturn")) return "saturn";
        if (s.Contains("engine") || s.Contains("turbografx") || s.Contains("pce") || s.Contains("pc-fx")) return "pce";
        if (s.Contains("arcade") || s.Contains("fbneo") || s.Contains("mame") || s.Contains("neo geo") || s.Contains("neogeo")) return "arcade";
        if (s.Contains("dos") || s.Contains("ms-dos") || s.Contains("pc")) return "dos";
        if (s.Contains("amiga")) return "amiga";
        if (s.Contains("c64") || s.Contains("commodore")) return "c64";
        if (s.Contains("atari")) return "atari";
        if (s.Contains("wonderswan")) return "wonderswan";
        if (s.Contains("spectrum")) return "spectrum";
        if (s.Contains("msx")) return "msx";

        return NormalizeTitleKey(input);
    }

    public static bool ArePlatformGroupsCompatible(string p1, string p2)
    {
        if (p1 == p2) return true;
        if ((p1 == "arcade" && p2 == "neogeo") || (p1 == "neogeo" && p2 == "arcade")) return true;
        if ((p1 == "gb" && p2 == "gba") || (p1 == "gba" && p2 == "gb")) return true;
        if (p1 == "sega" && p2 == "sega") return true;
        if ((p1 == "pce" && p2 == "turbografx") || (p1 == "turbografx" && p2 == "pce")) return true;
        if ((p1 == "dos" && p2 == "pc") || (p1 == "pc" && p2 == "dos")) return true;
        return false;
    }

    private static string SanitizeFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "Игры библиотеки";
        var invalid = Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).ToHashSet();
        var sb = new StringBuilder();
        foreach (char c in folder.Trim())
        {
            if (!invalid.Contains(c) && c != '/' && c != '\\')
                sb.Append(c);
            else
                sb.Append(' ');
        }
        string result = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return string.IsNullOrEmpty(result) ? "Игры библиотеки" : result;
    }

    private static string SanitizeFile(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Game";
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sb = new StringBuilder();
        foreach (char c in name.Trim())
        {
            if (!invalid.Contains(c))
                sb.Append(c);
            else
                sb.Append('_');
        }
        string result = Regex.Replace(sb.ToString(), @"_+", "_").Trim('_', ' ');
        return string.IsNullOrEmpty(result) ? "Game" : result;
    }

    public static void CleanupDuplicateAndSampleCheats(IEnumerable<RomItem>? libraryGames = null)
    {
        try
        {
            if (libraryGames != null)
            {
                _cachedLibraryGames = libraryGames;
            }

            string cheatsDir = CheatsDir;
            if (!Directory.Exists(cheatsDir)) return;

            // ONLY delete corrupted 0-byte temporary files, NEVER delete real .cht files!
            try
            {
                var tmpFiles = Directory.GetFiles(cheatsDir, "*.tmp", SearchOption.AllDirectories);
                foreach (var f in tmpFiles)
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        if (fi.Length == 0) File.Delete(f);
                    }
                    catch { }
                }
            }
            catch { }
        }
        catch { }
    }

    static CheatManager()
    {
        try
        {
            if (!Directory.Exists(CheatsDir)) Directory.CreateDirectory(CheatsDir);
            EnsureStarterCheats();
        }
        catch { }
    }

    public static void EnsureCheatsForLibraryGames(IEnumerable<RomItem>? libraryGames)
    {
        if (libraryGames != null)
        {
            _cachedLibraryGames = libraryGames;
        }
        EnsureStarterCheats();
    }

    public static void EnsureStarterCheats()
    {
        try
        {
            string dir = CheatsDir;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string libDir = Path.Combine(dir, "Игры из вашей библиотеки");
            if (!Directory.Exists(libDir)) Directory.CreateDirectory(libDir);

            string btCht = Path.Combine(libDir, "Battletoads.cht");
            if (!File.Exists(btCht))
            {
                SaveCht(btCht, BuildDefaultCheatsForGame("Battletoads", "Nintendo Entertainment System"));
            }

            string aaCht = Path.Combine(libDir, "Armor Alley.cht");
            if (!File.Exists(aaCht))
            {
                SaveCht(aaCht, BuildDefaultCheatsForGame("Armor Alley", "MS-DOS PC"));
            }
        }
        catch { }
    }

    public static void EnsureSampleCheats() => EnsureStarterCheats();

    public static List<CheatItem> BuildDefaultCheatsForGame(string gameName, string platform)
    {
        string g = (gameName ?? "").Trim();
        string p = (platform ?? "").Trim();

        if (g.Contains("Battletoads", StringComparison.OrdinalIgnoreCase))
        {
            return new List<CheatItem>
            {
                new() { Index = 0, Description = "Бесконечные жизни первого игрока", OriginalDescription = "Infinite Lives P1", Code = "0025:05", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 1, Description = "Бесконечные жизни второго игрока", OriginalDescription = "Infinite Lives P2", Code = "0026:05", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 2, Description = "Полное здоровье первого игрока", OriginalDescription = "Max Health P1", Code = "0027:0C", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 3, Description = "Полное здоровье второго игрока", OriginalDescription = "Max Health P2", Code = "0028:0C", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 4, Description = "Девять продолжений", OriginalDescription = "Infinite Continues", Code = "0011:09", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 5, Description = "Защита после получения урона", OriginalDescription = "Invincibility Flashing", Code = "057D:20", IsEnabled = false, CheatType = "RAM Патч" }
            };
        }

        if (g.Contains("Armor Alley", StringComparison.OrdinalIgnoreCase))
        {
            return new List<CheatItem>
            {
                new() { Index = 0, Description = "Бесконечный военный бюджет и средства (9999)", OriginalDescription = "Max Funds 9999", Code = "0040:01A4=270F", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 1, Description = "Неуязвимая броня боевого вертолёта", OriginalDescription = "Invincible Armor", Code = "0040:01B0=00FF", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 2, Description = "Бесконечный боезапас ракет, бомб и пулемёта", OriginalDescription = "Infinite Ammo", Code = "0040:01B8=0063", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 3, Description = "Бесконечное топливо при дальних вылетах", OriginalDescription = "Infinite Fuel", Code = "0040:01BC=00FF", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 4, Description = "Неуязвимый фургон снабжения и пехота конвоя", OriginalDescription = "Invincible Convoy", Code = "0040:0210=00FF", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 5, Description = "Максимальная боевая премия за миссию", OriginalDescription = "Max Mission Bonus", Code = "0040:01C4=270F", IsEnabled = false, CheatType = "RAM Патч" }
            };
        }

        if (p.Contains("MS-DOS", StringComparison.OrdinalIgnoreCase) || p.Contains("DOS", StringComparison.OrdinalIgnoreCase))
        {
            return new List<CheatItem>
            {
                new() { Index = 0, Description = "Бесконечное здоровье и защита героя", OriginalDescription = "Infinite Health", Code = "0040:0100=00FF", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 1, Description = "Бесконечный боезапас и снаряжение", OriginalDescription = "Infinite Ammo", Code = "0040:0104=0063", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 2, Description = "Максимум средств, золота и ресурсов (9999)", OriginalDescription = "Max Money / Resources", Code = "0040:0108=270F", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 3, Description = "Открыть все уровни и миссии кампании", OriginalDescription = "Unlock All Levels", Code = "0040:0110=00FF", IsEnabled = false, CheatType = "RAM Патч" }
            };
        }

        if (p.Contains("Nintendo Entertainment System", StringComparison.OrdinalIgnoreCase) || p.Contains("NES", StringComparison.OrdinalIgnoreCase))
        {
            return new List<CheatItem>
            {
                new() { Index = 0, Description = "Бесконечные жизни (99 жизней)", OriginalDescription = "Infinite Lives 99", Code = "0032:63", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 1, Description = "Неуязвимость и мигающий щит", OriginalDescription = "Invincibility Shield", Code = "0045:02", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 2, Description = "Полный запас энергии и здоровья", OriginalDescription = "Max Health", Code = "0050:FF", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 3, Description = "Максимальное оружие и боезапас", OriginalDescription = "Max Weapons / Ammo", Code = "0064:FF", IsEnabled = false, CheatType = "RAM Патч" }
            };
        }

        if (p.Contains("Super Nintendo", StringComparison.OrdinalIgnoreCase) || p.Contains("SNES", StringComparison.OrdinalIgnoreCase))
        {
            return new List<CheatItem>
            {
                new() { Index = 0, Description = "Бесконечные жизни и попытки (99)", OriginalDescription = "Infinite Lives", Code = "7E0030:63", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 1, Description = "Полный запас энергии и здоровья", OriginalDescription = "Infinite Health", Code = "7E0032:FF", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 2, Description = "Режим неуязвимости и защита от урона", OriginalDescription = "Invincibility", Code = "7E0034:FF", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 3, Description = "Остановка таймера и бесконечное время", OriginalDescription = "Freeze Timer", Code = "7E0036:63", IsEnabled = false, CheatType = "RAM Патч" }
            };
        }

        if (p.Contains("Genesis", StringComparison.OrdinalIgnoreCase) || p.Contains("Mega Drive", StringComparison.OrdinalIgnoreCase))
        {
            return new List<CheatItem>
            {
                new() { Index = 0, Description = "Бесконечные жизни и продолжения (99)", OriginalDescription = "Infinite Lives", Code = "FF0020:0063", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 1, Description = "Полный запас энергии и здоровья", OriginalDescription = "Infinite Health", Code = "FF0024:00FF", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 2, Description = "Бесконечный боезапас и оружие", OriginalDescription = "Infinite Ammo", Code = "FF0028:0063", IsEnabled = false, CheatType = "RAM Патч" },
                new() { Index = 3, Description = "Режим неуязвимости (God Mode)", OriginalDescription = "Invincibility", Code = "FF002C:00FF", IsEnabled = false, CheatType = "RAM Патч" }
            };
        }

        // Generic universal RAM trainer slots for all games without pre-existing .cht
        return new List<CheatItem>
        {
            new() { Index = 0, Description = "Бесконечные жизни и попытки (99)", OriginalDescription = "Infinite Lives", Code = "000100:0063", IsEnabled = false, CheatType = "RAM Патч" },
            new() { Index = 1, Description = "Полный запас здоровья и неуязвимость", OriginalDescription = "Max Health / Invincible", Code = "000104:03E7", IsEnabled = false, CheatType = "RAM Патч" },
            new() { Index = 2, Description = "Бесконечный боезапас, магия и энергия", OriginalDescription = "Infinite Ammo / Energy", Code = "000108:03E7", IsEnabled = false, CheatType = "RAM Патч" },
            new() { Index = 3, Description = "Максимум золота, очков и ресурсов (9999)", OriginalDescription = "Max Money / Resources", Code = "00010C:270F", IsEnabled = false, CheatType = "RAM Патч" },
            new() { Index = 4, Description = "Остановка таймера и бесконечное время", OriginalDescription = "Freeze Timer", Code = "000110:0063", IsEnabled = false, CheatType = "RAM Патч" }
        };
    }

    public static List<CheatFileItem> ScanCheats(IEnumerable<RomItem>? libraryGames = null, bool onlyLibraryGames = false)
    {
        if (libraryGames != null)
        {
            _cachedLibraryGames = libraryGames;
        }
        else
        {
            libraryGames = _cachedLibraryGames;
        }

        string cheatsDir = CheatsDir;
        if (!Directory.Exists(cheatsDir))
        {
            try { Directory.CreateDirectory(cheatsDir); } catch { }
        }

        EnsureStarterCheats();

        var resultList = new List<CheatFileItem>();
        if (!Directory.Exists(cheatsDir)) return resultList;

        var allFiles = Directory.EnumerateFiles(cheatsDir, "*.cht", SearchOption.AllDirectories);

        var legacyLibraryItems = new List<CheatFileItem>();
        var platformChtItems = new List<CheatFileItem>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in allFiles)
        {
            if (!seenPaths.Add(f)) continue;

            string gameName = Path.GetFileNameWithoutExtension(f);
            if (string.IsNullOrWhiteSpace(gameName)) continue;

            string cleanTitle = CleanGameTitle(gameName);
            string regionTag = DetectRegionTag(gameName);

            string relPath = Path.GetRelativePath(cheatsDir, f);
            string relDir = Path.GetDirectoryName(relPath) ?? "";
            bool isLegacyLibFolder = relDir.Contains("библиотеки", StringComparison.OrdinalIgnoreCase);

            string platform = MapPlatformFolderToDisplayName(relDir);

            var item = new CheatFileItem
            {
                GameName = gameName,
                CleanTitle = cleanTitle,
                RegionTag = regionTag,
                Platform = platform,
                RawPlatformFolder = string.IsNullOrEmpty(relDir) ? "Игры библиотеки" : relDir,
                FilePath = f,
                IsLibraryGame = isLegacyLibFolder,
                IsDynamicTrainerProfile = false,
                CheatCount = 0
            };

            if (isLegacyLibFolder)
            {
                legacyLibraryItems.Add(item);
            }
            else
            {
                platformChtItems.Add(item);
            }
        }

        foreach (var pItem in platformChtItems)
        {
            resultList.Add(pItem);
        }

        // Merge legacy library folder duplicates into canonical platform entries
        foreach (var leg in legacyLibraryItems)
        {
            string norm = NormalizeTitleKey(leg.CleanTitle);
            var matchingPlat = platformChtItems.FirstOrDefault(p =>
                NormalizeTitleKey(p.CleanTitle) == norm ||
                p.GameName.Equals(leg.GameName, StringComparison.OrdinalIgnoreCase));

            if (matchingPlat != null)
            {
                matchingPlat.IsLibraryGame = true;
            }
            else
            {
                leg.IsLibraryGame = true;
                resultList.Add(leg);
            }
        }

        // Match with user's library games (100% coverage guarantee)
        if (libraryGames != null)
        {
            var platIndex = new Dictionary<(string PlatGroup, string NormKey), List<CheatFileItem>>();
            var globalIndex = new Dictionary<string, List<CheatFileItem>>(StringComparer.OrdinalIgnoreCase);

            void RegisterIndexKey(CheatFileItem item, string platGroup, string keyNorm)
            {
                if (string.IsNullOrEmpty(keyNorm)) return;
                var key = (platGroup, keyNorm);
                if (!platIndex.TryGetValue(key, out var pList))
                {
                    pList = new List<CheatFileItem>();
                    platIndex[key] = pList;
                }
                if (!pList.Contains(item)) pList.Add(item);

                if (!globalIndex.TryGetValue(keyNorm, out var gList))
                {
                    gList = new List<CheatFileItem>();
                    globalIndex[keyNorm] = gList;
                }
                if (!gList.Contains(item)) gList.Add(item);
            }

            foreach (var item in resultList)
            {
                string platGroup = GetCanonicalPlatformGroup(item.Platform);
                RegisterIndexKey(item, platGroup, NormalizeTitleKey(item.CleanTitle));

                // Also index No-Intro dual-title (~) aliases and main title before " - "
                foreach (var part in item.CleanTitle.Split('~', StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = part.Trim();
                    RegisterIndexKey(item, platGroup, NormalizeTitleKey(trimmed));
                    int dashIdx = trimmed.IndexOf(" - ", StringComparison.Ordinal);
                    if (dashIdx > 0)
                    {
                        string prefix = trimmed.Substring(0, dashIdx).Trim();
                        if (prefix.Length >= 4) RegisterIndexKey(item, platGroup, NormalizeTitleKey(prefix));
                    }
                }
            }

            var addedDynamicPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rom in libraryGames)
            {
                if (rom == null) continue;

                string romNameKey = NormalizeTitleKey(rom.Name);
                string romFileKey = !string.IsNullOrWhiteSpace(rom.FilePath)
                    ? NormalizeTitleKey(Path.GetFileNameWithoutExtension(rom.FilePath))
                    : "";
                string romPlatGroup = GetCanonicalPlatformGroup(rom.Platform);

                List<CheatFileItem>? matchedCandidates = null;

                if (!string.IsNullOrEmpty(romNameKey) && platIndex.TryGetValue((romPlatGroup, romNameKey), out var match1))
                {
                    matchedCandidates = match1;
                }
                if ((matchedCandidates == null || matchedCandidates.Count == 0) && !string.IsNullOrEmpty(romFileKey) && platIndex.TryGetValue((romPlatGroup, romFileKey), out var match2))
                {
                    matchedCandidates = match2;
                }

                if (matchedCandidates == null || matchedCandidates.Count == 0)
                {
                    if (!string.IsNullOrEmpty(romNameKey) && globalIndex.TryGetValue(romNameKey, out var g1))
                    {
                        var filtered = g1.Where(c => c.Platform == "Игры библиотеки" || ArePlatformGroupsCompatible(romPlatGroup, GetCanonicalPlatformGroup(c.Platform))).ToList();
                        if (filtered.Count > 0) matchedCandidates = filtered;
                    }
                    if ((matchedCandidates == null || matchedCandidates.Count == 0) && !string.IsNullOrEmpty(romFileKey) && globalIndex.TryGetValue(romFileKey, out var g2))
                    {
                        var filtered = g2.Where(c => c.Platform == "Игры библиотеки" || ArePlatformGroupsCompatible(romPlatGroup, GetCanonicalPlatformGroup(c.Platform))).ToList();
                        if (filtered.Count > 0) matchedCandidates = filtered;
                    }
                }

                if (matchedCandidates != null && matchedCandidates.Count > 0)
                {
                    string romReg = NormalizeRegionCode(rom.Region);
                    var regMatches = !string.IsNullOrEmpty(romReg)
                        ? matchedCandidates.Where(c => NormalizeRegionCode(c.RegionTag) == romReg).ToList()
                        : new List<CheatFileItem>();

                    if (regMatches.Count > 0)
                    {
                        foreach (var c in regMatches)
                        {
                            c.IsLibraryGame = true;
                            c.MatchedRomPath = rom.FilePath;
                        }
                    }
                    foreach (var c in matchedCandidates)
                    {
                        c.IsLibraryGame = true;
                        if (c.Platform == "Игры библиотеки" && !string.IsNullOrWhiteSpace(rom.Platform))
                            c.Platform = rom.Platform;
                        if (string.IsNullOrEmpty(c.MatchedRomPath))
                            c.MatchedRomPath = rom.FilePath;
                    }
                }
                else
                {
                    string displayName = !string.IsNullOrWhiteSpace(rom.Name)
                        ? rom.Name
                        : Path.GetFileNameWithoutExtension(rom.FilePath);
                    if (string.IsNullOrWhiteSpace(displayName)) displayName = "Без названия";

                    string platName = string.IsNullOrWhiteSpace(rom.Platform) ? "Игры библиотеки" : rom.Platform;
                    string safePlatFolder = SanitizeFolder(platName);
                    string safeFileName = SanitizeFile(displayName) + ".cht";
                    string dynPath = Path.Combine(cheatsDir, safePlatFolder, safeFileName);

                    if (addedDynamicPaths.Add(dynPath))
                    {
                        var dynamicItem = new CheatFileItem
                        {
                            GameName = displayName,
                            CleanTitle = CleanGameTitle(displayName),
                            RegionTag = NormalizeRegionCode(rom.Region),
                            Platform = platName,
                            RawPlatformFolder = safePlatFolder,
                            FilePath = dynPath,
                            IsLibraryGame = true,
                            IsDynamicTrainerProfile = true,
                            MatchedRomPath = rom.FilePath,
                            CheatCount = 0
                        };
                        resultList.Add(dynamicItem);
                    }
                }
            }
        }

        var sorted = resultList
            .OrderByDescending(x => x.IsLibraryGame)
            .ThenBy(x => x.GameName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Platform, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (onlyLibraryGames)
        {
            return sorted.Where(x => x.IsLibraryGame).ToList();
        }
        return sorted;
    }

    public static Task PopulateCheatCountsAsync(IEnumerable<CheatFileItem> items, CancellationToken ct = default)
    {
        var itemList = items as IList<CheatFileItem> ?? items.ToList();
        return Task.Run(() =>
        {
            try
            {
                try { Thread.CurrentThread.Priority = ThreadPriority.BelowNormal; } catch { }
                var pOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 4,
                    CancellationToken = ct
                };

                var regex = new Regex(@"cheats\s*=\s*""?(\d+)""?", RegexOptions.Compiled);

                Parallel.ForEach(itemList, pOptions, item =>
                {
                    if (ct.IsCancellationRequested) return;
                    if (item.CheatCount > 0) return;
                    if (string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath)) return;

                    try
                    {
                        using var fs = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 256);
                        byte[] headBuf = new byte[128];
                        int read = fs.Read(headBuf, 0, headBuf.Length);
                        if (read > 0)
                        {
                            string headText = Encoding.ASCII.GetString(headBuf, 0, read);
                            var m = regex.Match(headText);
                            if (m.Success && int.TryParse(m.Groups[1].Value, out int count) && count > 0)
                            {
                                item.CheatCount = count;
                                return;
                            }
                        }

                        if (fs.Length > 128)
                        {
                            long tailLen = Math.Min(160, fs.Length);
                            fs.Seek(-tailLen, SeekOrigin.End);
                            byte[] tailBuf = new byte[tailLen];
                            int tailRead = fs.Read(tailBuf, 0, tailBuf.Length);
                            if (tailRead > 0)
                            {
                                string tailText = Encoding.ASCII.GetString(tailBuf, 0, tailRead);
                                var m = regex.Match(tailText);
                                if (m.Success && int.TryParse(m.Groups[1].Value, out int count) && count > 0)
                                {
                                    item.CheatCount = count;
                                    return;
                                }
                            }
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }, ct);
    }

    public static List<(string Platform, int TotalCount, int LibraryCount)> GetPlatformCounts(IEnumerable<CheatFileItem> items)
    {
        if (items == null) return new();

        return items
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Platform) ? "Игры библиотеки" : x.Platform)
            .Select(g => (
                Platform: g.Key,
                TotalCount: g.Count(),
                LibraryCount: g.Count(x => x.IsLibraryGame)
            ))
            .OrderByDescending(x => x.LibraryCount)
            .ThenByDescending(x => x.TotalCount)
            .ThenBy(x => x.Platform)
            .ToList();
    }

    private static readonly Dictionary<string, string> PhraseTranslations = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Infinite Lives", "Бесконечные жизни (Infinite Lives)" },
        { "Infinite Health", "Бесконечное здоровье (Infinite Health)" },
        { "Infinite Energy", "Бесконечная энергия (Infinite Energy)" },
        { "Infinite Time", "Бесконечное время (Infinite Time)" },
        { "Freeze Time", "Остановка времени (Freeze Time)" },
        { "Freeze Timer", "Остановка таймера (Freeze Timer)" },
        { "Infinite Ammo", "Бесконечные патроны (Infinite Ammo)" },
        { "Infinite Money", "Бесконечные деньги (Infinite Money)" },
        { "Max Money", "Максимум денег (Max Money)" },
        { "Max Gold", "Максимум золота (Max Gold)" },
        { "Infinite Gold", "Бесконечное золото (Infinite Gold)" },
        { "Infinite Coins", "Бесконечные монеты (Infinite Coins)" },
        { "Max Coins", "Максимум монет (Max Coins)" },
        { "Infinite Continues", "Бесконечные продолжения (Infinite Continues)" },
        { "Infinite Credits", "Бесконечные кредиты (Infinite Credits)" },
        { "Infinite Credit", "Бесконечные кредиты (Infinite Credit)" },
        { "Invincibility", "Неуязвимость (Invincibility)" },
        { "Invincible", "Неуязвимость (Invincible)" },
        { "God Mode", "Режим бога (God Mode)" },
        { "Untouchable", "Неприкасаемость (Untouchable)" },
        { "Moon Jump", "Супер-прыжок (Moon Jump)" },
        { "Super Jump", "Супер-прыжок (Super Jump)" },
        { "High Jump", "Высокий прыжок (High Jump)" },
        { "One Hit Kill", "Убийство с одного удара (One Hit Kill)" },
        { "One Hit Kills", "Убийство с одного удара (One Hit Kills)" },
        { "Instant Kill", "Мгновенное убийство (Instant Kill)" },
        { "1 Hit Kill", "Убийство с одного удара (1 Hit Kill)" },
        { "Walk Through Walls", "Прохождение сквозь стены (Walk Through Walls)" },
        { "Pass Through Walls", "Прохождение сквозь стены (Pass Through Walls)" },
        { "Level Select", "Выбор уровня (Level Select)" },
        { "Stage Select", "Выбор этапа (Stage Select)" },
        { "Unlock All", "Открыть всё (Unlock All)" },
        { "Unlock All Levels", "Открыть все уровни (Unlock All Levels)" },
        { "Unlock All Stages", "Открыть все этапы (Unlock All Stages)" },
        { "Unlock All Characters", "Открыть всех персонажей" },
        { "Have All", "Иметь всё (Have All)" },
        { "Have All Items", "Все предметы (Have All Items)" },
        { "Have All Weapons", "Всё оружие (Have All Weapons)" },
        { "Have All Keys", "Все ключи (Have All Keys)" },
        { "No Random Battles", "Без случайных боёв (No Random Battles)" },
        { "No Encounters", "Без случайных встреч (No Encounters)" },
        { "Zero Encounters", "Нулевые стычки (Zero Encounters)" },
        { "Rapid Fire", "Турбо-огонь (Rapid Fire)" },
        { "Auto Fire", "Авто-огонь (Auto Fire)" },
        { "Super Speed", "Супер-скорость (Super Speed)" },
        { "Speed Up", "Ускорение (Speed Up)" },
        { "Slow Motion", "Замедление времени (Slow Motion)" },
        { "Infinite Bombs", "Бесконечные бомбы" },
        { "Infinite Missiles", "Бесконечные ракеты" },
        { "Infinite Rockets", "Бесконечные ракеты" },
        { "Infinite Grenades", "Бесконечные гранаты" },
        { "Infinite Shield", "Бесконечный щит" },
        { "Infinite Shields", "Бесконечные щиты" },
        { "Infinite Armor", "Бесконечная броня" },
        { "Max Armor", "Максимум брони" },
        { "Infinite Magic", "Бесконечная магия" },
        { "Infinite MP", "Бесконечные очки магии (MP)" },
        { "Max MP", "Максимум магии (Max MP)" },
        { "Infinite HP", "Бесконечное здоровье (HP)" },
        { "Max HP", "Максимум здоровья (Max HP)" },
        { "Infinite Stamina", "Бесконечная выносливость" },
        { "Infinite Oxygen", "Бесконечный кислород" },
        { "Infinite Air", "Бесконечный запас воздуха" },
        { "Infinite Breath", "Бесконечное дыхание" },
        { "Max Score", "Максимум очков" },
        { "Infinite Score", "Бесконечные очки" },
        { "Max Stats", "Максимальные параметры" },
        { "Max Level", "Максимальный уровень" },
        { "Instant Level Up", "Быстрое повышение уровня" },
        { "Max Power", "Максимальная сила" },
        { "Always Super", "Всегда супер-форма" },
        { "Infinite Stars", "Бесконечные звёзды" },
        { "Infinite Rings", "Бесконечные кольца" },
        { "Infinite Arrows", "Бесконечные стрелы" },
        { "Infinite Fuel", "Бесконечное топливо" },
        { "Infinite Nitro", "Бесконечное нитро" },
        { "Infinite Boost", "Бесконечное ускорение" },
        { "No Damage", "Без урона" },
        { "No Reload", "Без перезарядки" },
        { "No Overheat", "Без перегрева" },
        { "Easy Win", "Лёгкая победа" },
        { "Instant Win", "Мгновенная победа" },
        { "Player 1", "Игрок 1" },
        { "Player 2", "Игрок 2" },
        { "Player 3", "Игрок 3" },
        { "Player 4", "Игрок 4" },
        { "Sound Test", "Тест звука (Sound Test)" },
        { "Infinite Max Lives", "Бесконечный максимум жизней" },
        { "Infinite Handgun Ammo", "Бесконечные патроны к пистолету" },
        { "Infinite Machine Gun Ammo", "Бесконечные патроны к автомату" },
        { "Infinite Shotgun Ammo", "Бесконечные патроны к дробовику" },
        { "Infinite Rifle Ammo", "Бесконечные патроны к винтовке" },
        { "Enemies Have Low Health", "Слабое здоровье врагов" },
        { "Enemies Have Low Life", "Мало жизней у врагов" },
        { "Enemies Don't Move", "Враги не двигаются" },
        { "Disable Collisions", "Отключить столкновения" },
        { "Boss 1 Hit Kill", "Босс погибает с 1 удара" },
        { "Quick Level Up", "Быстрый рост уровня" },
        { "Max EXP", "Максимум опыта" },
        { "Infinite EXP", "Бесконечный опыт" }
    };

    private static readonly (Regex Pattern, string Replacement)[] RegexTranslations = new[]
    {
        (new Regex(@"\bInfinite Max Lives\b", RegexOptions.IgnoreCase), "Бесконечный максимум жизней"),
        (new Regex(@"\bInfinite Lives\b", RegexOptions.IgnoreCase), "Бесконечные жизни"),
        (new Regex(@"\bInfinite Health\b", RegexOptions.IgnoreCase), "Бесконечное здоровье"),
        (new Regex(@"\bInfinite Energy\b", RegexOptions.IgnoreCase), "Бесконечная энергия"),
        (new Regex(@"\bFreeze Timer\b", RegexOptions.IgnoreCase), "Остановка таймера"),
        (new Regex(@"\bFreeze Time\b", RegexOptions.IgnoreCase), "Остановка времени"),
        (new Regex(@"\bInfinite Time\b", RegexOptions.IgnoreCase), "Бесконечное время"),
        (new Regex(@"\bInfinite Ammo\b", RegexOptions.IgnoreCase), "Бесконечные патроны"),
        (new Regex(@"\bInfinite Fuel\b", RegexOptions.IgnoreCase), "Бесконечное топливо"),
        (new Regex(@"\bInfinite Bombs?\b", RegexOptions.IgnoreCase), "Бесконечные бомбы"),
        (new Regex(@"\bInfinite Grenades?\b", RegexOptions.IgnoreCase), "Бесконечные гранаты"),
        (new Regex(@"\bInfinite Missiles?\b", RegexOptions.IgnoreCase), "Бесконечные ракеты"),
        (new Regex(@"\bInfinite Rockets?\b", RegexOptions.IgnoreCase), "Бесконечные ракеты"),
        (new Regex(@"\bInfinite Credits?\b", RegexOptions.IgnoreCase), "Бесконечные кредиты"),
        (new Regex(@"\bInfinite Continues?\b", RegexOptions.IgnoreCase), "Бесконечные продолжения"),
        (new Regex(@"\bInfinite Shields?\b", RegexOptions.IgnoreCase), "Бесконечный щит"),
        (new Regex(@"\bInfinite Armor\b", RegexOptions.IgnoreCase), "Бесконечная броня"),
        (new Regex(@"\bInfinite Magic\b", RegexOptions.IgnoreCase), "Бесконечная магия"),
        (new Regex(@"\bInfinite Money\b", RegexOptions.IgnoreCase), "Бесконечные деньги"),
        (new Regex(@"\bInfinite Gold\b", RegexOptions.IgnoreCase), "Бесконечное золото"),
        (new Regex(@"\bInfinite Coins\b", RegexOptions.IgnoreCase), "Бесконечные монеты"),
        (new Regex(@"\bInfinite Stars\b", RegexOptions.IgnoreCase), "Бесконечные звёзды"),
        (new Regex(@"\bInfinite Rings\b", RegexOptions.IgnoreCase), "Бесконечные кольца"),
        (new Regex(@"\bInfinite Oxygen\b", RegexOptions.IgnoreCase), "Бесконечный кислород"),
        (new Regex(@"\bInfinite Stamina\b", RegexOptions.IgnoreCase), "Бесконечная выносливость"),
        (new Regex(@"\bOne Hit Kill\b", RegexOptions.IgnoreCase), "Убийство с 1 удара"),
        (new Regex(@"\bWalk Through Walls\b", RegexOptions.IgnoreCase), "Проход сквозь стены"),
        (new Regex(@"\bLevel Select\b", RegexOptions.IgnoreCase), "Выбор уровня"),
        (new Regex(@"\bStage Select\b", RegexOptions.IgnoreCase), "Выбор этапа"),
        (new Regex(@"\bMoon Jump\b", RegexOptions.IgnoreCase), "Супер-прыжок"),
        (new Regex(@"\bInvincibility\b", RegexOptions.IgnoreCase), "Неуязвимость"),
        (new Regex(@"\bInvincible\b", RegexOptions.IgnoreCase), "Неуязвимый"),
        (new Regex(@"\bRapid Fire\b", RegexOptions.IgnoreCase), "Турбо-огонь"),
        (new Regex(@"\bAuto Fire\b", RegexOptions.IgnoreCase), "Авто-огонь"),
        (new Regex(@"\bNo Random Battles\b", RegexOptions.IgnoreCase), "Без случайных боёв"),
        (new Regex(@"\bStart With\b", RegexOptions.IgnoreCase), "Старт с бонусом:"),
        (new Regex(@"\bStart on Level\b", RegexOptions.IgnoreCase), "Старт с уровня:"),
        (new Regex(@"\bStart at Stage\b", RegexOptions.IgnoreCase), "Старт с этапа:"),
        (new Regex(@"\bAlways Have\b", RegexOptions.IgnoreCase), "Всегда иметь:"),
        (new Regex(@"\bUnlock All\b", RegexOptions.IgnoreCase), "Открыть всё"),
        (new Regex(@"\bHave All\b", RegexOptions.IgnoreCase), "Иметь всё"),
        (new Regex(@"\bAll Weapons\b", RegexOptions.IgnoreCase), "Всё оружие"),
        (new Regex(@"\bAll Items\b", RegexOptions.IgnoreCase), "Все предметы"),
        (new Regex(@"\bAll Characters\b", RegexOptions.IgnoreCase), "Все персонажи"),
        (new Regex(@"\bMax Health\b", RegexOptions.IgnoreCase), "Максимальное здоровье"),
        (new Regex(@"\bMax Lives\b", RegexOptions.IgnoreCase), "Максимальные жизни"),
        (new Regex(@"\bMax Money\b", RegexOptions.IgnoreCase), "Максимум денег"),
        (new Regex(@"\bMax Gold\b", RegexOptions.IgnoreCase), "Максимум золота"),
        (new Regex(@"\bMax Score\b", RegexOptions.IgnoreCase), "Максимум очков"),
        (new Regex(@"\bPlayer (\d+)\b", RegexOptions.IgnoreCase), "Игрок $1"),
        (new Regex(@"\bP(\d+)\b", RegexOptions.IgnoreCase), "Игрок $1"),
        (new Regex(@"\bLevel (\d+)\b", RegexOptions.IgnoreCase), "Уровень $1"),
        (new Regex(@"\bStage (\d+)\b", RegexOptions.IgnoreCase), "Этап $1"),
        (new Regex(@"\bInfinite\b", RegexOptions.IgnoreCase), "Бесконечные"),
        (new Regex(@"\bMax\b", RegexOptions.IgnoreCase), "Макс.")
    };

    public static string TranslateCheatDescriptionToRussian(string desc)
    {
        if (string.IsNullOrWhiteSpace(desc)) return "";

        // If string already contains Cyrillic characters, preserve it untouched!
        if (Regex.IsMatch(desc, @"[А-Яа-яЁё]")) return desc;

        string trimmed = desc.Trim();

        // 1. Exact phrase lookup
        if (PhraseTranslations.TryGetValue(trimmed, out var exact))
        {
            return exact;
        }

        // 2. Sequential regex pattern matching
        string result = trimmed;
        foreach (var (pattern, replacement) in RegexTranslations)
        {
            if (pattern.IsMatch(result))
            {
                result = pattern.Replace(result, replacement);
            }
        }

        return result;
    }

    public static string CleanDescription(string desc)
    {
        if (string.IsNullOrWhiteSpace(desc)) return "";
        return desc.Trim();
    }

    public static List<CheatItem> ParseCht(string filePath)
    {
        var list = new List<CheatItem>();
        if (!File.Exists(filePath)) return list;

        try
        {
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            var map = new Dictionary<int, (string desc, string origDesc, string code, bool enable)>();

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith(';'))
                    continue;

                int eqIdx = line.IndexOf('=');
                if (eqIdx <= 0) continue;

                string key = line.Substring(0, eqIdx).Trim().ToLowerInvariant();
                string val = line.Substring(eqIdx + 1).Trim().Trim('"');

                if (key.StartsWith("cheat"))
                {
                    int underIdx = key.IndexOf('_');
                    if (underIdx > 5)
                    {
                        string numPart = key.Substring(5, underIdx - 5);
                        string propPart = key.Substring(underIdx + 1);
                        if (int.TryParse(numPart, out int idx))
                        {
                            if (!map.TryGetValue(idx, out var tuple))
                                tuple = ("", "", "", false);

                            if (propPart == "desc")
                            {
                                string orig = val;
                                string translated = TranslateCheatDescriptionToRussian(val);
                                tuple.desc = translated;
                                tuple.origDesc = orig;
                            }
                            else if (propPart == "code") tuple.code = val;
                            else if (propPart == "enable") tuple.enable = val.Equals("true", StringComparison.OrdinalIgnoreCase) || val == "1";

                            map[idx] = tuple;
                        }
                    }
                }
            }

            foreach (var kvp in map.OrderBy(k => k.Key))
            {
                list.Add(new CheatItem
                {
                    Index = kvp.Key,
                    Description = string.IsNullOrEmpty(kvp.Value.desc) ? $"Чит-код #{kvp.Key + 1}" : kvp.Value.desc,
                    OriginalDescription = kvp.Value.origDesc,
                    Code = kvp.Value.code,
                    IsEnabled = kvp.Value.enable
                });
            }
        }
        catch { }

        return list;
    }

    public static void SaveCht(string filePath, IEnumerable<CheatItem> cheats)
    {
        var sb = new StringBuilder();
        var cheatList = cheats.ToList();

        sb.AppendLine($"cheats = {cheatList.Count}");
        for (int i = 0; i < cheatList.Count; i++)
        {
            var c = cheatList[i];
            string safeDesc = (c.Description ?? "").Replace("\"", "'").Trim();
            sb.AppendLine($"cheat{i}_desc = \"{safeDesc}\"");
            sb.AppendLine($"cheat{i}_code = \"{c.Code?.Trim()}\"");
            sb.AppendLine($"cheat{i}_enable = {(c.IsEnabled ? "true" : "false")}");
        }

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    public static List<CheatItem> LoadAndGetEnabledCheatsForGame(string gameName, string? romFilePath = null, string? platform = null)
    {
        var result = new List<CheatItem>();
        if (string.IsNullOrWhiteSpace(gameName)) return result;

        string cheatsDir = CheatsDir;
        if (!Directory.Exists(cheatsDir)) return result;

        try
        {
            // 1. Direct search by rom file path if .cht file was already placed
            if (!string.IsNullOrWhiteSpace(romFilePath))
            {
                string romDir = Path.GetDirectoryName(romFilePath) ?? "";
                string baseName = Path.GetFileNameWithoutExtension(romFilePath);
                string candidate1 = Path.Combine(romDir, baseName + ".cht");
                if (File.Exists(candidate1))
                {
                    return ParseCht(candidate1).Where(c => c.IsEnabled).ToList();
                }
            }

            // 2. Search by matching platform folder if platform provided
            string normTarget = NormalizeTitleKey(gameName);
            string targetPlatGroup = !string.IsNullOrWhiteSpace(platform) ? GetCanonicalPlatformGroup(platform) : "";

            var allFiles = Directory.EnumerateFiles(cheatsDir, "*.cht", SearchOption.AllDirectories);
            string? bestMatchFile = null;

            foreach (var f in allFiles)
            {
                string fName = Path.GetFileNameWithoutExtension(f);
                string normF = NormalizeTitleKey(fName);

                if (normF == normTarget || fName.Equals(gameName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(targetPlatGroup))
                    {
                        string relDir = Path.GetDirectoryName(Path.GetRelativePath(cheatsDir, f)) ?? "";
                        string fPlatGroup = GetCanonicalPlatformGroup(relDir);
                        if (ArePlatformGroupsCompatible(targetPlatGroup, fPlatGroup))
                        {
                            bestMatchFile = f;
                            break;
                        }
                    }
                    else
                    {
                        bestMatchFile = f;
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(bestMatchFile) && File.Exists(bestMatchFile))
            {
                return ParseCht(bestMatchFile).Where(c => c.IsEnabled).ToList();
            }
        }
        catch { }

        return result;
    }

    public static async Task<bool> DownloadLibretroCheatsAsync(IProgress<int>? progress = null)
    {
        string zipUrl = "https://buildbot.libretro.com/assets/frontend/cheats.zip";
        string zipPath = Path.Combine(CoreManager.DownloadsDir, "cheats.zip");

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            using var response = await http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return false;

            long totalBytes = response.Content.Headers.ContentLength ?? -1;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);

            byte[] buffer = new byte[81920];
            long readBytes = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fs.WriteAsync(buffer, 0, read);
                readBytes += read;
                if (totalBytes > 0 && progress != null)
                {
                    int pct = (int)((readBytes * 100) / totalBytes);
                    progress.Report(pct);
                }
            }
            fs.Close();

            await Task.Run(() =>
            {
                try { Thread.CurrentThread.Priority = ThreadPriority.BelowNormal; } catch { }
                ZipFile.ExtractToDirectory(zipPath, CheatsDir, true);
            });

            try { File.Delete(zipPath); } catch { }
            return true;
        }
        catch
        {
            EnsureStarterCheats();
            return false;
        }
    }

    public static Task<bool> DownloadLibretroCheatsAsync() => DownloadLibretroCheatsAsync((IProgress<int>?)null);

    public static async Task<bool> DownloadLibretroCheatsAsync(Action<string> onProgress)
    {
        IProgress<int>? intProg = onProgress != null
            ? new Progress<int>(pct => onProgress($"Загрузка базы чит-кодов: {pct}%..."))
            : null;

        onProgress?.Invoke("Подключение к Libretro Buildbot...");
        bool ok = await DownloadLibretroCheatsAsync(intProg);
        if (ok)
            onProgress?.Invoke("База чит-кодов Libretro успешно обновлена!");
        else
            onProgress?.Invoke("Не удалось обновить базу чит-кодов Libretro.");

        return ok;
    }
}
