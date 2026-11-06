using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace StormRetroArch;

public class CheatItem : INotifyPropertyChanged
{
    private int _index;
    private string _description = "";
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
    public string GameName { get; set; } = "";
    public string Platform { get; set; } = "Библиотека игр";
    public string FilePath { get; set; } = "";
    public int CheatCount { get; set; }
    public bool IsLibraryGame { get; set; }
    public string CheatCountBadge => $"{CheatCount} читов";
    public List<CheatItem> Cheats { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class CheatManager
{
    public static readonly string CheatsDir = Path.Combine(CoreManager.AppDir, "cheats");

    public static string NormalizeGameKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var sb = new StringBuilder();
        foreach (char c in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    public static void CleanupDuplicateAndSampleCheats(IEnumerable<RomItem>? libraryGames)
    {
        try
        {
            if (!Directory.Exists(CheatsDir)) return;

            var validKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (libraryGames != null)
            {
                foreach (var rom in libraryGames)
                {
                    if (!string.IsNullOrWhiteSpace(rom.Name))
                        validKeys.Add(NormalizeGameKey(rom.Name));
                    if (!string.IsNullOrWhiteSpace(rom.FilePath))
                        validKeys.Add(NormalizeGameKey(Path.GetFileNameWithoutExtension(rom.FilePath)));
                }
            }

            // Always preserve Battletoads as standard baseline
            validKeys.Add(NormalizeGameKey("Battletoads"));

            // Remove legacy sample directories if not matching user library
            string[] sampleDirs = new[]
            {
                Path.Combine(CheatsDir, "Nintendo NES и Famicom"),
                Path.Combine(CheatsDir, "Nintendo NES"),
                Path.Combine(CheatsDir, "Super Nintendo"),
                Path.Combine(CheatsDir, "Sega Mega Drive"),
                Path.Combine(CheatsDir, "Game Boy Advance"),
                Path.Combine(CheatsDir, "Sony PlayStation 1")
            };

            foreach (var sDir in sampleDirs)
            {
                if (Directory.Exists(sDir))
                {
                    var files = Directory.GetFiles(sDir, "*.cht");
                    foreach (var f in files)
                    {
                        string gKey = NormalizeGameKey(Path.GetFileNameWithoutExtension(f));
                        if (!validKeys.Contains(gKey))
                        {
                            try { File.Delete(f); } catch { }
                        }
                    }
                    try
                    {
                        if (Directory.GetFiles(sDir).Length == 0 && Directory.GetDirectories(sDir).Length == 0)
                        {
                            Directory.Delete(sDir);
                        }
                    }
                    catch { }
                }
            }

            // Deduplicate across all directories
            var allCht = Directory.GetFiles(CheatsDir, "*.cht", SearchOption.AllDirectories);
            var groups = allCht.GroupBy(f => NormalizeGameKey(Path.GetFileNameWithoutExtension(f)))
                               .Where(g => !string.IsNullOrEmpty(g.Key) && g.Count() > 1);

            foreach (var g in groups)
            {
                var sorted = g.OrderByDescending(f => f.Contains("Игры из вашей библиотеки", StringComparison.OrdinalIgnoreCase))
                              .ThenByDescending(f => { try { return new FileInfo(f).Length; } catch { return 0L; } })
                              .ToList();

                for (int i = 1; i < sorted.Count; i++)
                {
                    try { File.Delete(sorted[i]); } catch { }
                }
            }
        }
        catch { }
    }

    static CheatManager()
    {
        try
        {
            if (!Directory.Exists(CheatsDir)) Directory.CreateDirectory(CheatsDir);
            EnsureSampleCheats();
        }
        catch { }
    }

    private static string CleanDescription(string desc)
    {
        if (string.IsNullOrWhiteSpace(desc)) return "";
        // Strip English parenthetical glosses if Russian text is present
        if (Regex.IsMatch(desc, "[А-Яа-яЁё]"))
        {
            desc = Regex.Replace(desc, @"\s*\([A-Za-z0-9\s\-\+/,:.'""]+\)", "").Trim();
        }
        return desc;
    }

    public static void EnsureCheatsForLibraryGames(IEnumerable<RomItem>? libraryGames)
    {
        try
        {
            if (!Directory.Exists(CheatsDir)) Directory.CreateDirectory(CheatsDir);
            string libDir = Path.Combine(CheatsDir, "Игры из вашей библиотеки");
            if (!Directory.Exists(libDir)) Directory.CreateDirectory(libDir);

            CleanupDuplicateAndSampleCheats(libraryGames);

            // Safe RAM Cheats for Battletoads with version marker
            string markerFile = Path.Combine(CheatsDir, ".storm_cheats_v2.ok");
            string btCht = Path.Combine(libDir, "Battletoads.cht");
            if (!File.Exists(markerFile) || !File.Exists(btCht))
            {
                SaveCht(btCht, BuildDefaultCheatsForGame("Battletoads", "Nintendo Entertainment System"));
                try { File.WriteAllText(markerFile, "STORM RETROARCH 1.5.0 SAFE RAM CHEATS OK", Encoding.UTF8); } catch { }
            }

            if (libraryGames == null) return;

            foreach (var rom in libraryGames)
            {
                if (string.IsNullOrWhiteSpace(rom.Name)) continue;
                string safeName = string.Join("_", rom.Name.Split(Path.GetInvalidFileNameChars())).Trim();
                if (string.IsNullOrEmpty(safeName)) continue;

                string chtPath = Path.Combine(libDir, safeName + ".cht");
                if (File.Exists(chtPath)) continue;

                var cheats = BuildDefaultCheatsForGame(rom.Name, rom.Platform);
                SaveCht(chtPath, cheats);
            }
        }
        catch { }
    }

    public static List<CheatItem> BuildDefaultCheatsForGame(string gameName, string platform)
    {
        string g = (gameName ?? "").Trim();
        string p = (platform ?? "").Trim();

        if (g.Contains("Battletoads", StringComparison.OrdinalIgnoreCase))
        {
            return new List<CheatItem>
            {
                new() { Index = 0, Description = "Бесконечные жизни первого игрока", Code = "0025:05", IsEnabled = false },
                new() { Index = 1, Description = "Бесконечные жизни второго игрока", Code = "0026:05", IsEnabled = false },
                new() { Index = 2, Description = "Полное здоровье первого игрока", Code = "0027:0C", IsEnabled = false },
                new() { Index = 3, Description = "Полное здоровье второго игрока", Code = "0028:0C", IsEnabled = false },
                new() { Index = 4, Description = "Девять продолжений", Code = "0011:09", IsEnabled = false },
                new() { Index = 5, Description = "Защита после получения урона", Code = "057D:20", IsEnabled = false }
            };
        }

        if (g.Contains("Armor Alley", StringComparison.OrdinalIgnoreCase))
        {
            return new List<CheatItem>
            {
                new() { Index = 0, Description = "Бесконечный военный бюджет и средства (9999)", Code = "0040:01A4=270F", IsEnabled = false },
                new() { Index = 1, Description = "Неуязвимая броня боевого вертолёта", Code = "0040:01B0=00FF", IsEnabled = false },
                new() { Index = 2, Description = "Бесконечный боезапас ракет, бомб и пулемёта", Code = "0040:01B8=0063", IsEnabled = false },
                new() { Index = 3, Description = "Бесконечное топливо при дальних вылетах", Code = "0040:01BC=00FF", IsEnabled = false },
                new() { Index = 4, Description = "Неуязвимый фургон снабжения и пехота конвоя", Code = "0040:0210=00FF", IsEnabled = false },
                new() { Index = 5, Description = "Максимальная боевая премия за миссию", Code = "0040:01C4=270F", IsEnabled = false }
            };
        }

        if (p.Contains("MS-DOS", StringComparison.OrdinalIgnoreCase) || p.Contains("DOS", StringComparison.OrdinalIgnoreCase))
        {
            return new List<CheatItem>
            {
                new() { Index = 0, Description = "Бесконечное здоровье и защита героя", Code = "0040:0100=00FF", IsEnabled = false },
                new() { Index = 1, Description = "Бесконечный боезапас и снаряжение", Code = "0040:0104=0063", IsEnabled = false },
                new() { Index = 2, Description = "Максимум средств, золота и ресурсов (9999)", Code = "0040:0108=270F", IsEnabled = false },
                new() { Index = 3, Description = "Открыть все уровни и миссии кампании", Code = "0040:0110=00FF", IsEnabled = false }
            };
        }

        if (p.Contains("Nintendo Entertainment System", StringComparison.OrdinalIgnoreCase) || p.Contains("NES", StringComparison.OrdinalIgnoreCase))
        {
            return new List<CheatItem>
            {
                new() { Index = 0, Description = "Бесконечные жизни (99 жизней)", Code = "0032:63", IsEnabled = false },
                new() { Index = 1, Description = "Неуязвимость и мигающий щит", Code = "0045:02", IsEnabled = false },
                new() { Index = 2, Description = "Полный запас энергии и здоровья", Code = "0050:FF", IsEnabled = false },
                new() { Index = 3, Description = "Максимальное оружие и боезапас", Code = "0064:FF", IsEnabled = false }
            };
        }

        return new List<CheatItem>
        {
            new() { Index = 0, Description = "Бесконечные жизни и продолжения", Code = "000100:0063", IsEnabled = false },
            new() { Index = 1, Description = "Полный запас здоровья и неуязвимость", Code = "000104:03E7", IsEnabled = false },
            new() { Index = 2, Description = "Бесконечный боезапас, магия и энергия", Code = "000108:03E7", IsEnabled = false },
            new() { Index = 3, Description = "Максимум золота, очков и ресурсов", Code = "00010C:FFFF", IsEnabled = false }
        };
    }

    public static List<CheatFileItem> ScanCheats(IEnumerable<RomItem>? libraryGames = null, bool onlyLibraryGames = false)
    {
        EnsureCheatsForLibraryGames(libraryGames);
        var result = new List<CheatFileItem>();
        try
        {
            if (!Directory.Exists(CheatsDir)) return result;

            var libKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (libraryGames != null)
            {
                foreach (var r in libraryGames)
                {
                    if (!string.IsNullOrWhiteSpace(r.Name))
                        libKeys.Add(NormalizeGameKey(r.Name));
                    if (!string.IsNullOrWhiteSpace(r.FilePath))
                        libKeys.Add(NormalizeGameKey(Path.GetFileNameWithoutExtension(r.FilePath)));
                }
            }

            var files = Directory.GetFiles(CheatsDir, "*.cht", SearchOption.AllDirectories);
            foreach (var f in files)
            {
                var cheats = ParseCht(f);
                string gameName = Path.GetFileNameWithoutExtension(f);
                string cleanTitle = Regex.Replace(gameName, @"\s*\((USA|World|Europe|Japan|En,Fr,De).*\)$", "", RegexOptions.IgnoreCase).Trim();
                string relDir = Path.GetDirectoryName(Path.GetRelativePath(CheatsDir, f)) ?? "";
                string platform = string.IsNullOrEmpty(relDir) || relDir == "."
                    ? "Игры библиотеки"
                    : relDir.Replace("\\", " / ")
                            .Replace("Nintendo - Nintendo Entertainment System", "Nintendo NES / Famicom")
                            .Replace("Nintendo - Super Nintendo Entertainment System", "Super Nintendo")
                            .Replace("Sega - Mega Drive - Genesis", "Sega Mega Drive")
                            .Replace("Nintendo - Game Boy Advance", "Game Boy Advance")
                            .Replace("Sony - PlayStation", "Sony PlayStation 1");

                string gKey = NormalizeGameKey(gameName);
                string cKey = NormalizeGameKey(cleanTitle);

                bool isLib = relDir.Contains("библиотеки", StringComparison.OrdinalIgnoreCase) ||
                             libKeys.Contains(gKey) ||
                             libKeys.Contains(cKey);

                result.Add(new CheatFileItem
                {
                    GameName = cleanTitle,
                    Platform = platform,
                    FilePath = f,
                    CheatCount = cheats.Count,
                    IsLibraryGame = isLib,
                    Cheats = cheats
                });
            }
        }
        catch { }

        // Deduplicate by NormalizeGameKey, prioritizing library games, then cheat count
        return result
            .GroupBy(x => NormalizeGameKey(x.GameName), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g => g.OrderByDescending(x => x.IsLibraryGame).ThenByDescending(x => x.CheatCount).First())
            .Where(x => !onlyLibraryGames || x.IsLibraryGame)
            .OrderByDescending(x => x.IsLibraryGame)
            .ThenBy(x => x.GameName)
            .ToList();
    }

    public static List<CheatItem> ParseCht(string filePath)
    {
        var list = new List<CheatItem>();
        if (!File.Exists(filePath)) return list;

        try
        {
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            var map = new Dictionary<int, (string desc, string code, bool enable)>();
            bool modified = false;

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
                                tuple = ("", "", false);

                            if (propPart == "desc")
                            {
                                string cleaned = CleanDescription(val);
                                if (cleaned != val) modified = true;
                                tuple.desc = cleaned;
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
                    Code = kvp.Value.code,
                    IsEnabled = kvp.Value.enable
                });
            }

            if (modified && list.Count > 0)
            {
                try { SaveCht(filePath, list); } catch { }
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
            sb.AppendLine($"cheat{i}_desc = \"{CleanDescription(c.Description)}\"");
            sb.AppendLine($"cheat{i}_code = \"{c.Code}\"");
            sb.AppendLine($"cheat{i}_enable = {(c.IsEnabled ? "true" : "false")}");
        }

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
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
                ZipFile.ExtractToDirectory(zipPath, CheatsDir, true);
            });

            try { File.Delete(zipPath); } catch { }
            return true;
        }
        catch
        {
            EnsureSampleCheats();
            return false;
        }
    }

    private static void EnsureSampleCheats()
    {
        try
        {
            if (!Directory.Exists(CheatsDir)) Directory.CreateDirectory(CheatsDir);
            string libDir = Path.Combine(CheatsDir, "Игры из вашей библиотеки");
            if (!Directory.Exists(libDir)) Directory.CreateDirectory(libDir);

            string markerFile = Path.Combine(CheatsDir, ".storm_cheats_v2.ok");
            string btCht = Path.Combine(libDir, "Battletoads.cht");
            if (!File.Exists(markerFile) || !File.Exists(btCht))
            {
                SaveCht(btCht, BuildDefaultCheatsForGame("Battletoads", "Nintendo Entertainment System"));
                try { File.WriteAllText(markerFile, "STORM RETROARCH 1.5.0 SAFE RAM CHEATS OK", Encoding.UTF8); } catch { }
            }
        }
        catch { }
    }
}
