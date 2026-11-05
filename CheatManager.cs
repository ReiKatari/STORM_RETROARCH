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
    public List<CheatItem> Cheats { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class CheatManager
{
    public static readonly string CheatsDir = Path.Combine(CoreManager.AppDir, "cheats");

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
        EnsureSampleCheats();
        if (libraryGames == null) return;

        try
        {
            string libDir = Path.Combine(CheatsDir, "Игры из вашей библиотеки");
            if (!Directory.Exists(libDir)) Directory.CreateDirectory(libDir);

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
                new() { Index = 0, Description = "Бесконечные жизни для обоих игроков", Code = "GXXZZLVI", IsEnabled = false },
                new() { Index = 1, Description = "Неуязвимость и защита от урона врагов", Code = "001A:03", IsEnabled = false },
                new() { Index = 2, Description = "Старт с двойным запасом жизней (10 жизней)", Code = "PAXZGLLA", IsEnabled = false },
                new() { Index = 3, Description = "Убийство обычных врагов с одного удара", Code = "GXEILUSO", IsEnabled = false },
                new() { Index = 4, Description = "Супер-высокий прыжок боевой жабы", Code = "AEUebAZA", IsEnabled = false },
                new() { Index = 5, Description = "Переход сразу на уровень 3: Турбо-туннель", Code = "ZAXZGLLE", IsEnabled = false },
                new() { Index = 6, Description = "Переход сразу на уровень 6: Змеиная яма", Code = "IYXZGLLE", IsEnabled = false },
                new() { Index = 7, Description = "Переход сразу на уровень 12: Башня Тёмной королевы", Code = "PZXZGLLE", IsEnabled = false }
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

    public static List<CheatFileItem> ScanCheats(IEnumerable<RomItem>? libraryGames = null)
    {
        EnsureCheatsForLibraryGames(libraryGames);
        var result = new List<CheatFileItem>();
        try
        {
            if (!Directory.Exists(CheatsDir)) return result;

            var libNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (libraryGames != null)
            {
                foreach (var r in libraryGames)
                {
                    if (!string.IsNullOrWhiteSpace(r.Name))
                        libNames.Add(r.Name.Trim());
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

                bool isLib = relDir.Contains("библиотеки", StringComparison.OrdinalIgnoreCase) ||
                             libNames.Contains(gameName) ||
                             libNames.Contains(cleanTitle);

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

        // Deduplicate by GameName, prioritizing library games
        return result
            .GroupBy(x => x.GameName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.IsLibraryGame).ThenByDescending(x => x.CheatCount).First())
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
            string libDir = Path.Combine(CheatsDir, "Игры из вашей библиотеки");
            if (!Directory.Exists(libDir)) Directory.CreateDirectory(libDir);

            // Always ensure Battletoads and Armor Alley are present at the very top
            string btCht = Path.Combine(libDir, "Battletoads.cht");
            if (!File.Exists(btCht))
                SaveCht(btCht, BuildDefaultCheatsForGame("Battletoads", "Nintendo Entertainment System"));

            string aaCht = Path.Combine(libDir, "Armor Alley.cht");
            if (!File.Exists(aaCht))
                SaveCht(aaCht, BuildDefaultCheatsForGame("Armor Alley", "MS-DOS PC"));

            // NES Classics
            string nesDir = Path.Combine(CheatsDir, "Nintendo NES и Famicom");
            if (!Directory.Exists(nesDir)) Directory.CreateDirectory(nesDir);
            string smbCht = Path.Combine(nesDir, "Super Mario Bros.cht");
            SaveCht(smbCht, new List<CheatItem>
            {
                new() { Index = 0, Description = "Бесконечные жизни", Code = "SXIOPO", IsEnabled = false },
                new() { Index = 1, Description = "Всегда большой Супер Марио", Code = "0756-01-01", IsEnabled = false },
                new() { Index = 2, Description = "Неуязвимость и огненные шары", Code = "079F-01-10", IsEnabled = false },
                new() { Index = 3, Description = "Лунный высокий прыжок", Code = "YSAOPE + YEAOZA", IsEnabled = false }
            });

            string contraCht = Path.Combine(nesDir, "Contra.cht");
            SaveCht(contraCht, new List<CheatItem>
            {
                new() { Index = 0, Description = "Бесконечные 30 жизней", Code = "SZLIVO", IsEnabled = false },
                new() { Index = 1, Description = "Оружие Разброс (Дробовик S) при старте", Code = "PEIIXZ", IsEnabled = false },
                new() { Index = 2, Description = "Неуязвимость к пулям и врагам", Code = "00AE:FF", IsEnabled = false }
            });

            // SNES Classics
            string snesDir = Path.Combine(CheatsDir, "Super Nintendo");
            if (!Directory.Exists(snesDir)) Directory.CreateDirectory(snesDir);
            string smwCht = Path.Combine(snesDir, "Super Mario World.cht");
            SaveCht(smwCht, new List<CheatItem>
            {
                new() { Index = 0, Description = "Бесконечные жизни (99)", Code = "7E0DBE63", IsEnabled = false },
                new() { Index = 1, Description = "Бесконечное время на уровне", Code = "7E0F3128", IsEnabled = false },
                new() { Index = 2, Description = "Постоянный плащ полёта", Code = "7E001902", IsEnabled = false }
            });

            // Sega Mega Drive Classics
            string genDir = Path.Combine(CheatsDir, "Sega Mega Drive");
            if (!Directory.Exists(genDir)) Directory.CreateDirectory(genDir);
            string sonicCht = Path.Combine(genDir, "Sonic The Hedgehog.cht");
            SaveCht(sonicCht, new List<CheatItem>
            {
                new() { Index = 0, Description = "Бесконечные золотые кольца (99)", Code = "FFFE20:0063", IsEnabled = false },
                new() { Index = 1, Description = "Бесконечные жизни (9)", Code = "FFFE12:0009", IsEnabled = false },
                new() { Index = 2, Description = "Неуязвимость и защита от шипов", Code = "FFD030:0002", IsEnabled = false }
            });

            // GBA Classics
            string gbaDir = Path.Combine(CheatsDir, "Game Boy Advance");
            if (!Directory.Exists(gbaDir)) Directory.CreateDirectory(gbaDir);
            string metroidCht = Path.Combine(gbaDir, "Metroid Fusion.cht");
            SaveCht(metroidCht, new List<CheatItem>
            {
                new() { Index = 0, Description = "Максимальная энергия скафандра (999)", Code = "03001314:03E7", IsEnabled = false },
                new() { Index = 1, Description = "Бесконечный боезапас ракет (999)", Code = "03001318:03E7", IsEnabled = false },
                new() { Index = 2, Description = "Открыть все улучшения и костюмы", Code = "03001328:FFFF", IsEnabled = false }
            });

            // PS1 Classics
            string ps1Dir = Path.Combine(CheatsDir, "Sony PlayStation 1");
            if (!Directory.Exists(ps1Dir)) Directory.CreateDirectory(ps1Dir);
            string castlevaniaCht = Path.Combine(ps1Dir, "Castlevania - Symphony of the Night.cht");
            SaveCht(castlevaniaCht, new List<CheatItem>
            {
                new() { Index = 0, Description = "Максимальное здоровье Алукарда (9999)", Code = "80097BA0 270F", IsEnabled = false },
                new() { Index = 1, Description = "Бесконечная мана и сердца (9999)", Code = "80097BA4 270F", IsEnabled = false },
                new() { Index = 2, Description = "Бесконечное золото (999999)", Code = "80097BF8 FFFF", IsEnabled = false }
            });
        }
        catch { }
    }
}
