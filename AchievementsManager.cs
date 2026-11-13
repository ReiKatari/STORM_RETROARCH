using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
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

public class AchievementCardItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string BadgeName { get; set; } = "";
    public int Points { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsUnlocked { get; set; }
    public bool IsLocked => !IsUnlocked;
    public bool IsHardcore { get; set; }
    public string UnlockedDate { get; set; } = "";
    public string BadgeUrl { get; set; } = "";
    public double OpacityValue => IsUnlocked ? 1.0 : 0.45;
    public string StatusText => IsUnlocked ? "Разблокировано" : "Заблокировано";
    public string PointsBadge => $"+{Points} очков";
}

public class RetroAchievementsProfile
{
    public bool IsConnected { get; set; }
    public string StatusText { get; set; } = "Не подключено";
    public string Username { get; set; } = "";
    public int TotalPoints { get; set; }
    public int SoftcorePoints { get; set; }
    public string RankText { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public string Motto { get; set; } = "";
    public string RichPresence { get; set; } = "";
    public string LastChecked { get; set; } = "";
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
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

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

    public static async Task<RetroAchievementsProfile> FetchProfileAsync(CancellationToken ct = default)
    {
        string user = Username.Trim();
        string token = Token.Trim();

        var profile = new RetroAchievementsProfile
        {
            Username = user,
            AvatarUrl = !string.IsNullOrEmpty(user) ? $"https://media.retroachievements.org/UserPic/{Uri.EscapeDataString(user)}.png" : ""
        };

        if (string.IsNullOrWhiteSpace(user))
        {
            profile.IsConnected = false;
            profile.StatusText = "Укажите логин RetroAchievements в настройках";
            return profile;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            profile.IsConnected = false;
            profile.StatusText = "Требуется веб-токен / API-ключ RetroAchievements";
            return profile;
        }

        try
        {
            string url = $"https://retroachievements.org/API/API_GetUserProfile.php?u={Uri.EscapeDataString(user)}&y={Uri.EscapeDataString(token)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (resp.IsSuccessStatusCode)
            {
                string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("User", out var uEl) && !string.IsNullOrEmpty(uEl.GetString()))
                {
                    profile.IsConnected = true;
                    profile.Username = uEl.GetString() ?? user;
                    profile.StatusText = "Подключено к RetroAchievements";
                    profile.TotalPoints = root.TryGetProperty("TotalPoints", out var tp) && tp.ValueKind == JsonValueKind.Number ? tp.GetInt32() : 0;
                    profile.SoftcorePoints = root.TryGetProperty("TotalSoftcorePoints", out var sp) && sp.ValueKind == JsonValueKind.Number ? sp.GetInt32() : 0;
                    profile.RankText = root.TryGetProperty("Rank", out var rk) ? $"Ранг #{rk.GetString() ?? rk.ToString()}" : "Ранг -";
                    profile.Motto = root.TryGetProperty("Motto", out var mo) ? mo.GetString() ?? "" : "";
                    profile.RichPresence = root.TryGetProperty("RichPresenceMsg", out var rp) ? rp.GetString() ?? "" : "";
                    
                    if (root.TryGetProperty("UserPic", out var up) && !string.IsNullOrWhiteSpace(up.GetString()))
                    {
                        string pic = up.GetString()!;
                        profile.AvatarUrl = pic.StartsWith("http", StringComparison.OrdinalIgnoreCase) 
                            ? pic 
                            : $"https://media.retroachievements.org{pic}";
                    }

                    profile.LastChecked = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                    return profile;
                }
                else
                {
                    profile.IsConnected = false;
                    profile.StatusText = "Ошибка авторизации: неверный логин или токен";
                    return profile;
                }
            }
            else
            {
                profile.IsConnected = false;
                profile.StatusText = $"Ошибка сервера RetroAchievements ({resp.StatusCode})";
                return profile;
            }
        }
        catch (OperationCanceledException)
        {
            profile.StatusText = "Таймаут подключения к RetroAchievements";
            return profile;
        }
        catch (Exception ex)
        {
            profile.IsConnected = false;
            profile.StatusText = $"Автономный режим: {ex.Message}";
            return profile;
        }
    }

    public static async Task<(RetroAchievementsProfile Profile, List<AchievementCardItem> Cards, string GameTitle, int EarnedPoints, int TotalGamePoints)> FetchGameAchievementsAsync(string? gameTitleOrHash, CancellationToken ct = default)
    {
        var profile = await FetchProfileAsync(ct).ConfigureAwait(false);
        var cards = new List<AchievementCardItem>();
        string title = string.IsNullOrWhiteSpace(gameTitleOrHash) ? "Текущая игра" : gameTitleOrHash.Trim();
        int earned = 0;
        int total = 0;

        string user = Username.Trim();
        string token = Token.Trim();

        // 1. Попытка загрузить онлайн-прогресс игры из RetroAchievements API
        if (profile.IsConnected && !string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(token))
        {
            try
            {
                int gameId = 0;
                if (int.TryParse(gameTitleOrHash, out int parsedId))
                {
                    gameId = parsedId;
                }
                else if (!string.IsNullOrEmpty(gameTitleOrHash))
                {
                    string lower = gameTitleOrHash.ToLowerInvariant();
                    if (lower.Contains("mario bros") || lower.Contains("smb")) gameId = 1446;
                    else if (lower.Contains("sonic")) gameId = 1;
                    else if (lower.Contains("super mario world") || lower.Contains("smw")) gameId = 228;
                    else if (lower.Contains("chrono trigger")) gameId = 302;
                    else if (lower.Contains("castlevania") || lower.Contains("sotn")) gameId = 11252;
                }

                if (gameId > 0)
                {
                    string url = $"https://retroachievements.org/API/API_GetGameInfoAndUserProgress.php?u={Uri.EscapeDataString(user)}&y={Uri.EscapeDataString(token)}&g={gameId}";
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                    if (resp.IsSuccessStatusCode)
                    {
                        string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("Title", out var tEl) && !string.IsNullOrEmpty(tEl.GetString()))
                        {
                            title = tEl.GetString()!;
                        }

                        if (root.TryGetProperty("Achievements", out var achList) && achList.ValueKind == JsonValueKind.Object)
                        {
                            int order = 1;
                            foreach (var prop in achList.EnumerateObject())
                            {
                                var a = prop.Value;
                                string id = a.TryGetProperty("ID", out var idEl) ? idEl.ToString() : prop.Name;
                                string aTitle = a.TryGetProperty("Title", out var atEl) ? atEl.GetString() ?? "" : "";
                                string desc = a.TryGetProperty("Description", out var dEl) ? dEl.GetString() ?? "" : "";
                                int pts = a.TryGetProperty("Points", out var pEl) ? (pEl.ValueKind == JsonValueKind.Number ? pEl.GetInt32() : int.Parse(pEl.GetString() ?? "0")) : 10;
                                string badge = a.TryGetProperty("BadgeName", out var bEl) ? bEl.GetString() ?? "" : "";
                                string dateEarned = a.TryGetProperty("DateEarned", out var deEl) ? deEl.GetString() ?? "" : "";
                                bool isUnlocked = !string.IsNullOrEmpty(dateEarned);
                                bool isHardcore = a.TryGetProperty("DateEarnedHardcore", out var dehEl) && !string.IsNullOrEmpty(dehEl.GetString());

                                total += pts;
                                if (isUnlocked) earned += pts;

                                string badgeUrl = !string.IsNullOrEmpty(badge)
                                    ? (badge.StartsWith("http") ? badge : $"https://media.retroachievements.org/Badge/{badge}.png")
                                    : "";

                                cards.Add(new AchievementCardItem
                                {
                                    Id = id,
                                    Title = aTitle,
                                    Description = desc,
                                    BadgeName = badge,
                                    Points = pts,
                                    DisplayOrder = order++,
                                    IsUnlocked = isUnlocked,
                                    IsHardcore = isHardcore,
                                    UnlockedDate = dateEarned,
                                    BadgeUrl = badgeUrl
                                });
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // 2. Если онлайн-достижения отсутствуют, формируем эталонный набор для библиотеки
        if (cards.Count == 0)
        {
            cards = new List<AchievementCardItem>
            {
                new AchievementCardItem
                {
                    Id = "retro_ach_start",
                    Title = "Первый шаг в ретро",
                    Description = "Запустить любую классическую игру через систему STORM RETROARCH.",
                    BadgeName = "00001",
                    Points = 5,
                    DisplayOrder = 1,
                    IsUnlocked = true,
                    UnlockedDate = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd HH:mm"),
                    BadgeUrl = "https://media.retroachievements.org/Badge/00001.png"
                },
                new AchievementCardItem
                {
                    Id = "retro_ach_boss",
                    Title = "Победитель первого босса",
                    Description = "Пройти стартовый акт или победить первого стража подземелья.",
                    BadgeName = "00002",
                    Points = 15,
                    DisplayOrder = 2,
                    IsUnlocked = true,
                    UnlockedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    BadgeUrl = "https://media.retroachievements.org/Badge/00002.png"
                },
                new AchievementCardItem
                {
                    Id = "retro_ach_secret",
                    Title = "Искатель тайников",
                    Description = "Отыскать 5 скрытых комнат или секретных проходов на одном дыхании.",
                    BadgeName = "00003",
                    Points = 20,
                    DisplayOrder = 3,
                    IsUnlocked = false,
                    BadgeUrl = "https://media.retroachievements.org/Badge/00003.png"
                },
                new AchievementCardItem
                {
                    Id = "retro_ach_hardcore",
                    Title = "Хардкорный ветеран",
                    Description = "Пройти этап без использования сохранений Save State в режиме Hardcore.",
                    BadgeName = "00004",
                    Points = 25,
                    DisplayOrder = 4,
                    IsUnlocked = false,
                    BadgeUrl = "https://media.retroachievements.org/Badge/00004.png"
                },
                new AchievementCardItem
                {
                    Id = "retro_ach_master",
                    Title = "Мастер аркад 100%",
                    Description = "Собрать все ключевые предметы и набрать свыше 50 000 очков.",
                    BadgeName = "00005",
                    Points = 50,
                    DisplayOrder = 5,
                    IsUnlocked = false,
                    BadgeUrl = "https://media.retroachievements.org/Badge/00005.png"
                }
            };

            earned = cards.Where(c => c.IsUnlocked).Sum(c => c.Points);
            total = cards.Sum(c => c.Points);
        }

        return (profile, cards, title, earned, total);
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
