using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace StormRetroArch;

public class CheatItem : INotifyPropertyChanged
{
    private bool _isEnabled;
    private string _desc = "";
    private string _code = "";
    private string _cheatType = "Game Genie / Raw";

    public int Index { get; set; }

    public string Description
    {
        get => _desc;
        set { if (_desc != value) { _desc = value; OnPropertyChanged(); } }
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
    public string Platform { get; set; } = "RetroArch";
    public string FilePath { get; set; } = "";
    public int CheatCount { get; set; }
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

    public static List<CheatFileItem> ScanCheats()
    {
        var result = new List<CheatFileItem>();
        try
        {
            if (!Directory.Exists(CheatsDir)) return result;

            var files = Directory.GetFiles(CheatsDir, "*.cht", SearchOption.AllDirectories);
            foreach (var f in files)
            {
                var cheats = ParseCht(f);
                string gameName = Path.GetFileNameWithoutExtension(f);
                string relDir = Path.GetDirectoryName(Path.GetRelativePath(CheatsDir, f)) ?? "";
                string platform = string.IsNullOrEmpty(relDir) ? "Custom" : relDir.Replace("\\", " / ");

                result.Add(new CheatFileItem
                {
                    GameName = gameName,
                    Platform = platform,
                    FilePath = f,
                    CheatCount = cheats.Count,
                    Cheats = cheats
                });
            }
        }
        catch { }

        return result;
    }

    public static List<CheatItem> ParseCht(string filePath)
    {
        var list = new List<CheatItem>();
        if (!File.Exists(filePath)) return list;

        try
        {
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            var map = new Dictionary<int, (string desc, string code, bool enable)>();

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith(';'))
                    continue;

                int eqIdx = line.IndexOf('=');
                if (eqIdx <= 0) continue;

                string key = line.Substring(0, eqIdx).Trim().ToLowerInvariant();
                string val = line.Substring(eqIdx + 1).Trim().Trim('"');

                // cheatX_desc, cheatX_code, cheatX_enable
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

                            if (propPart == "desc") tuple.desc = val;
                            else if (propPart == "code") tuple.code = val;
                            else if (propPart == "enable") tuple.enable = val.Equals("true", StringComparison.OrdinalIgnoreCase) || val == "1";

                            map[idx] = tuple;
                        }
                    }
                }
            }

            foreach (var kvp in map)
            {
                list.Add(new CheatItem
                {
                    Index = kvp.Key,
                    Description = string.IsNullOrEmpty(kvp.Value.desc) ? $"Чит #{kvp.Key + 1}" : kvp.Value.desc,
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
        int count = 0;
        foreach (var c in cheats) count++;

        sb.AppendLine($"cheats = {count}");
        int i = 0;
        foreach (var c in cheats)
        {
            sb.AppendLine($"cheat{i}_desc = \"{c.Description}\"");
            sb.AppendLine($"cheat{i}_code = \"{c.Code}\"");
            sb.AppendLine($"cheat{i}_enable = {(c.IsEnabled ? "true" : "false")}");
            i++;
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
            // Sample Cheats for NES
            string nesDir = Path.Combine(CheatsDir, "Nintendo - Nintendo Entertainment System");
            if (!Directory.Exists(nesDir)) Directory.CreateDirectory(nesDir);
            string smbCht = Path.Combine(nesDir, "Super Mario Bros. (World).cht");
            if (!File.Exists(smbCht))
            {
                File.WriteAllText(smbCht,
                    "cheats = 4\n" +
                    "cheat0_desc = \"Бесконечные жизни (Infinite Lives)\"\n" +
                    "cheat0_code = \"SXIOPO\"\n" +
                    "cheat0_enable = false\n" +
                    "cheat1_desc = \"Всегда Супер Марио (Always Big Mario)\"\n" +
                    "cheat1_code = \"0756-01-01\"\n" +
                    "cheat1_enable = false\n" +
                    "cheat2_desc = \"Неуязвимость от врагов (Invincibility Star)\"\n" +
                    "cheat2_code = \"079F-01-20\"\n" +
                    "cheat2_enable = false\n" +
                    "cheat3_desc = \"Супер высокий прыжок (Moon Jump)\"\n" +
                    "cheat3_code = \"YSAOPE + YEAOZA\"\n" +
                    "cheat3_enable = false\n", Encoding.UTF8);
            }

            // Sample Cheats for SNES
            string snesDir = Path.Combine(CheatsDir, "Nintendo - Super Nintendo Entertainment System");
            if (!Directory.Exists(snesDir)) Directory.CreateDirectory(snesDir);
            string smwCht = Path.Combine(snesDir, "Super Mario World (USA).cht");
            if (!File.Exists(smwCht))
            {
                File.WriteAllText(smwCht,
                    "cheats = 3\n" +
                    "cheat0_desc = \"Бесконечные жизни 99 (Infinite Lives 99)\"\n" +
                    "cheat0_code = \"7E0DBE63\"\n" +
                    "cheat0_enable = false\n" +
                    "cheat1_desc = \"Бесконечное время (Infinite Time)\"\n" +
                    "cheat1_code = \"7E0F3128\"\n" +
                    "cheat1_enable = false\n" +
                    "cheat2_desc = \"Плащ Супермена / Перо (Permanent Cape)\"\n" +
                    "cheat2_code = \"7E001902\"\n" +
                    "cheat2_enable = false\n", Encoding.UTF8);
            }

            // Sample Cheats for Sega Genesis
            string genDir = Path.Combine(CheatsDir, "Sega - Mega Drive - Genesis");
            if (!Directory.Exists(genDir)) Directory.CreateDirectory(genDir);
            string sonicCht = Path.Combine(genDir, "Sonic The Hedgehog (USA, Europe).cht");
            if (!File.Exists(sonicCht))
            {
                File.WriteAllText(sonicCht,
                    "cheats = 3\n" +
                    "cheat0_desc = \"Бесконечные кольца 99 (Infinite Rings 99)\"\n" +
                    "cheat0_code = \"FFFE20:0063\"\n" +
                    "cheat0_enable = false\n" +
                    "cheat1_desc = \"Бесконечные жизни 9 (Infinite Lives 9)\"\n" +
                    "cheat1_code = \"FFFE12:0009\"\n" +
                    "cheat1_enable = false\n" +
                    "cheat2_desc = \"Неуязвимость (Invincibility)\"\n" +
                    "cheat2_code = \"FFD030:0002\"\n" +
                    "cheat2_enable = false\n", Encoding.UTF8);
            }

            // Sample Cheats for GBA
            string gbaDir = Path.Combine(CheatsDir, "Nintendo - Game Boy Advance");
            if (!Directory.Exists(gbaDir)) Directory.CreateDirectory(gbaDir);
            string metroidCht = Path.Combine(gbaDir, "Metroid - Fusion (USA).cht");
            if (!File.Exists(metroidCht))
            {
                File.WriteAllText(metroidCht,
                    "cheats = 3\n" +
                    "cheat0_desc = \"Максимальная энергия 999 (Max Energy 999)\"\n" +
                    "cheat0_code = \"03001314:03E7\"\n" +
                    "cheat0_enable = false\n" +
                    "cheat1_desc = \"Бесконечные ракеты 999 (Infinite Missiles 999)\"\n" +
                    "cheat1_code = \"03001318:03E7\"\n" +
                    "cheat1_enable = false\n" +
                    "cheat2_desc = \"Все улучшения и способности (All Upgrades)\"\n" +
                    "cheat2_code = \"03001328:FFFF\"\n" +
                    "cheat2_enable = false\n", Encoding.UTF8);
            }

            // Sample Cheats for PS1
            string ps1Dir = Path.Combine(CheatsDir, "Sony - PlayStation");
            if (!Directory.Exists(ps1Dir)) Directory.CreateDirectory(ps1Dir);
            string castlevaniaCht = Path.Combine(ps1Dir, "Castlevania - Symphony of the Night (USA).cht");
            if (!File.Exists(castlevaniaCht))
            {
                File.WriteAllText(castlevaniaCht,
                    "cheats = 3\n" +
                    "cheat0_desc = \"Максимальное HP Алукарда (Max HP 9999)\"\n" +
                    "cheat0_code = \"80097BA0 270F\"\n" +
                    "cheat0_enable = false\n" +
                    "cheat1_desc = \"Максимальное MP Магия (Max MP 9999)\"\n" +
                    "cheat1_code = \"80097BA4 270F\"\n" +
                    "cheat1_enable = false\n" +
                    "cheat2_desc = \"Бесконечное золото 999999 (Max Gold)\"\n" +
                    "cheat2_code = \"80097BF8 FFFF\"\n" +
                    "cheat2_enable = false\n", Encoding.UTF8);
            }
        }
        catch { }
    }
}
