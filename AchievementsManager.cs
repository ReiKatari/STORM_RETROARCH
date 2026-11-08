using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace StormRetroArch;

public class AchievementItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int Points { get; set; } = 10;
    public string BadgeUrl { get; set; } = "";
    public bool Unlocked { get; set; }
    public DateTime? UnlockedAt { get; set; }
}

public class LiveRamAchievementRule
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int Points { get; set; } = 10;
    public uint RamAddress { get; set; }
    public byte Width { get; set; } = 1; // 1 = byte, 2 = uint16, 4 = uint32
    public uint TargetValue { get; set; }
    public bool Unlocked { get; set; }
}

public class CheevosGameSupportItem
{
    public string GameTitle { get; set; } = "";
    public string Platform { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Crc32 { get; set; } = "";
    public string SupportStatus { get; set; } = "Готов к проверке";
    public string BadgeBg { get; set; } = "#1A38BDF8";
    public string BadgeBorder { get; set; } = "#38BDF8";
    public string BadgeText { get; set; } = "#7DD3FC";
}

public static class AchievementsManager
{
    public static string Username
    {
        get => RetroArchConfigManager.GetString("cheevos_username", "");
        set
        {
            RetroArchConfigManager.SetString("cheevos_username", value);
            RetroArchConfigManager.Save();
        }
    }

    public static string Token
    {
        get => RetroArchConfigManager.GetString("cheevos_token", "");
        set
        {
            RetroArchConfigManager.SetString("cheevos_token", value);
            RetroArchConfigManager.Save();
        }
    }

    public static string Password
    {
        get => RetroArchConfigManager.GetString("cheevos_password", "");
        set
        {
            RetroArchConfigManager.SetString("cheevos_password", value);
            RetroArchConfigManager.Save();
        }
    }

    public static bool EnableCheevos
    {
        get => RetroArchConfigManager.GetBool("cheevos_enable", true);
        set
        {
            RetroArchConfigManager.SetBool("cheevos_enable", value);
            RetroArchConfigManager.Save();
        }
    }

    public static bool HardcoreMode
    {
        get => RetroArchConfigManager.GetBool("cheevos_hardcore_mode_enable", true);
        set
        {
            RetroArchConfigManager.SetBool("cheevos_hardcore_mode_enable", value);
            RetroArchConfigManager.Save();
        }
    }

    public static bool Leaderboards
    {
        get => RetroArchConfigManager.GetBool("cheevos_leaderboards_enable", true);
        set
        {
            RetroArchConfigManager.SetBool("cheevos_leaderboards_enable", value);
            RetroArchConfigManager.Save();
        }
    }

    public static bool RichPresence
    {
        get => RetroArchConfigManager.GetBool("cheevos_richpresence_enable", true);
        set
        {
            RetroArchConfigManager.SetBool("cheevos_richpresence_enable", value);
            RetroArchConfigManager.Save();
        }
    }

    public static bool Badges
    {
        get => RetroArchConfigManager.GetBool("cheevos_badges_enable", true);
        set
        {
            RetroArchConfigManager.SetBool("cheevos_badges_enable", value);
            RetroArchConfigManager.Save();
        }
    }

    public static bool AutoScreenshot
    {
        get => RetroArchConfigManager.GetBool("cheevos_auto_screenshot", false);
        set
        {
            RetroArchConfigManager.SetBool("cheevos_auto_screenshot", value);
            RetroArchConfigManager.Save();
        }
    }

    public static bool TestUnofficial
    {
        get => RetroArchConfigManager.GetBool("cheevos_test_unofficial", false);
        set
        {
            RetroArchConfigManager.SetBool("cheevos_test_unofficial", value);
            RetroArchConfigManager.Save();
        }
    }

    public static List<CheevosGameSupportItem> ScanLibraryGamesForCheevos(IEnumerable<RomItem> games)
    {
        var result = new List<CheevosGameSupportItem>();
        foreach (var g in games)
        {
            result.Add(new CheevosGameSupportItem
            {
                GameTitle = g.Title,
                Platform = g.Platform,
                FilePath = g.FilePath,
                Crc32 = g.Crc32,
                SupportStatus = !string.IsNullOrEmpty(g.Crc32) ? "CRC32 проверен" : "Поддерживается Libretro",
                BadgeBg = "#1A10B981",
                BadgeBorder = "#10B981",
                BadgeText = "#6EE7B7"
            });
        }
        return result;
    }

    public static void OpenUserProfile()
    {
        string user = Username;
        string url = string.IsNullOrWhiteSpace(user) 
            ? "https://retroachievements.org/" 
            : $"https://retroachievements.org/user/{Uri.EscapeDataString(user)}";
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
    public static event Action<AchievementItem>? OnAchievementUnlocked;

    private static readonly List<LiveRamAchievementRule> _activeLiveRules = new();
    private static readonly object _rulesLock = new();

    static AchievementsManager()
    {
        InitializeDefaultLiveRules();
    }

    public static void RegisterLiveRule(LiveRamAchievementRule rule)
    {
        lock (_rulesLock)
        {
            _activeLiveRules.Add(rule);
        }
    }

    public static void ResetLiveRules()
    {
        lock (_rulesLock)
        {
            foreach (var rule in _activeLiveRules)
            {
                rule.Unlocked = false;
            }
        }
    }

    public static void InitializeDefaultLiveRules()
    {
        lock (_rulesLock)
        {
            _activeLiveRules.Clear();
            _activeLiveRules.Add(new LiveRamAchievementRule
            {
                Id = "nes_smb_world1",
                Title = "Первый шаг в Грибное Королевство",
                Description = "Пройден уровень 1-1 в Super Mario Bros. (ОЗУ $075F)",
                Points = 10,
                RamAddress = 0x075F,
                Width = 1,
                TargetValue = 1
            });
            _activeLiveRules.Add(new LiveRamAchievementRule
            {
                Id = "snes_smw_yoshi",
                Title = "Друг динозавров",
                Description = "Йоши оседлан в Super Mario World (ОЗУ $187A)",
                Points = 15,
                RamAddress = 0x187A,
                Width = 1,
                TargetValue = 1
            });
            _activeLiveRules.Add(new LiveRamAchievementRule
            {
                Id = "md_sonic_rings",
                Title = "Золотая лихорадка",
                Description = "Собрано 100 колец в Sonic the Hedgehog (ОЗУ $FE20)",
                Points = 20,
                RamAddress = 0xFE20,
                Width = 2,
                TargetValue = 100
            });
            _activeLiveRules.Add(new LiveRamAchievementRule
            {
                Id = "universal_score_milestone",
                Title = "Мастер аркад",
                Description = "Набрано более 10 000 очков в текущей игре",
                Points = 25,
                RamAddress = 0x0100,
                Width = 2,
                TargetValue = 10000
            });
        }
    }

    public static void EvaluateLiveRamTick(ReadOnlySpan<byte> ram)
    {
        if (ram.IsEmpty) return;

        lock (_rulesLock)
        {
            foreach (var rule in _activeLiveRules)
            {
                if (rule.Unlocked) continue;
                if (rule.RamAddress + rule.Width > ram.Length) continue;

                uint val = 0;
                if (rule.Width == 1)
                {
                    val = ram[(int)rule.RamAddress];
                }
                else if (rule.Width == 2 && rule.RamAddress + 1 < (uint)ram.Length)
                {
                    val = (uint)(ram[(int)rule.RamAddress] | (ram[(int)rule.RamAddress + 1] << 8));
                }
                else if (rule.Width == 4 && rule.RamAddress + 3 < (uint)ram.Length)
                {
                    val = (uint)(ram[(int)rule.RamAddress] | 
                                 (ram[(int)rule.RamAddress + 1] << 8) | 
                                 (ram[(int)rule.RamAddress + 2] << 16) | 
                                 (ram[(int)rule.RamAddress + 3] << 24));
                }

                if (val >= rule.TargetValue && rule.TargetValue > 0)
                {
                    rule.Unlocked = true;
                    var item = new AchievementItem
                    {
                        Id = rule.Id,
                        Title = rule.Title,
                        Description = rule.Description,
                        Points = rule.Points,
                        Unlocked = true,
                        UnlockedAt = DateTime.Now
                    };

                    try
                    {
                        OnAchievementUnlocked?.Invoke(item);
                    }
                    catch { }
                }
            }
        }
    }

}
