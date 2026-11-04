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
}
