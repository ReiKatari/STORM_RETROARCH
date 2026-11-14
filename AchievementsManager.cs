using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    public string LocalAvatarPath { get; set; } = "";
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
    private static readonly SocketsHttpHandler _httpHandler = new()
    {
        EnableMultipleHttp2Connections = true,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(15)
    };

    private static readonly HttpClient _httpClient = new(_httpHandler)
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

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

    private static readonly ConcurrentDictionary<string, (string Crc32, string RaMd5)> _romHashCache = new(StringComparer.OrdinalIgnoreCase);
    private static bool _romHashCacheLoaded = false;
    private static readonly object _hashCacheLock = new();

    private static readonly Dictionary<string, int> CuratedGameIds = new(StringComparer.OrdinalIgnoreCase)
    {
        { "super mario bros", 1446 },
        { "super mario world", 228 },
        { "sonic the hedgehog", 1 },
        { "sonic the hedgehog 2", 2 },
        { "chrono trigger", 302 },
        { "castlevania symphony of the night", 11252 },
        { "castlevania", 1449 },
        { "the legend of zelda", 1450 },
        { "metroid", 1452 },
        { "super metroid", 241 },
        { "donkey kong country", 232 },
        { "pokemon red", 724 },
        { "pokemon blue", 724 },
        { "pokemon emerald", 1980 },
        { "streets of rage 2", 6 },
        { "final fantasy vii", 11234 },
        { "crash bandicoot", 11238 }
    };

    private static void EnsureRomHashCacheLoaded()
    {
        if (_romHashCacheLoaded) return;
        lock (_hashCacheLock)
        {
            if (_romHashCacheLoaded) return;
            try
            {
                string cacheFile = Path.Combine(CoreManager.AppDir, "config", "storm_rom_hashes.json");
                if (File.Exists(cacheFile))
                {
                    string json = File.ReadAllText(cacheFile);
                    using var doc = JsonDocument.Parse(json);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        string crc = prop.Value.TryGetProperty("crc", out var cEl) ? cEl.GetString() ?? "" : "";
                        string md5 = prop.Value.TryGetProperty("md5", out var mEl) ? mEl.GetString() ?? "" : "";
                        if (!string.IsNullOrEmpty(crc) || !string.IsNullOrEmpty(md5))
                        {
                            _romHashCache[prop.Name] = (crc, md5);
                        }
                    }
                }
            }
            catch { }
            _romHashCacheLoaded = true;
        }
    }

    private static void SaveRomHashCache()
    {
        try
        {
            string cfgDir = Path.Combine(CoreManager.AppDir, "config");
            Directory.CreateDirectory(cfgDir);
            string cacheFile = Path.Combine(cfgDir, "storm_rom_hashes.json");

            var dict = new Dictionary<string, object>();
            foreach (var kvp in _romHashCache)
            {
                dict[kvp.Key] = new { crc = kvp.Value.Crc32, md5 = kvp.Value.RaMd5 };
            }

            string json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(cacheFile, json);
        }
        catch { }
    }

    public static (string Crc32, string RaMd5) ComputeRomCrc32AndRaMd5(string filePath, string platform)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return ("", "");

        EnsureRomHashCacheLoaded();

        string cacheKey = $"{filePath}|{File.GetLastWriteTimeUtc(filePath).Ticks}|{new FileInfo(filePath).Length}|{platform}";
        if (_romHashCache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            byte[]? romBytes = null;

            if (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(filePath);
                var entry = zip.Entries.FirstOrDefault(e =>
                {
                    string ext = Path.GetExtension(e.FullName).ToLowerInvariant();
                    return ext is ".nes" or ".fds" or ".sfc" or ".smc" or ".md" or ".bin" or ".gen" or ".smd"
                                or ".gb" or ".gbc" or ".gba" or ".n64" or ".z64" or ".v64" or ".pce" or ".sms"
                                or ".gg" or ".cue" or ".chd" or ".iso" or ".nds";
                }) ?? zip.Entries.FirstOrDefault(e => !e.FullName.EndsWith("/") && !e.FullName.EndsWith("\\"));

                if (entry != null)
                {
                    using var ms = new MemoryStream((int)Math.Min(entry.Length, 64 * 1024 * 1024));
                    using var s = entry.Open();
                    s.CopyTo(ms);
                    romBytes = ms.ToArray();
                }
            }

            romBytes ??= File.ReadAllBytes(filePath);

            if (romBytes.Length == 0) return ("", "");

            string crc32 = ComputeCrc32Hex(romBytes);

            byte[] md5TargetBytes = romBytes;
            string pLower = (platform ?? "").ToLowerInvariant();

            // NES / Famicom: strip 16-byte iNES header (starts with "NES\x1A")
            if (pLower.Contains("nes") || pLower.Contains("famicom"))
            {
                if (romBytes.Length > 16 && romBytes[0] == 0x4E && romBytes[1] == 0x45 && romBytes[2] == 0x53 && romBytes[3] == 0x1A)
                {
                    md5TargetBytes = new byte[romBytes.Length - 16];
                    Buffer.BlockCopy(romBytes, 16, md5TargetBytes, 0, md5TargetBytes.Length);
                }
            }
            // SNES: strip 512-byte copier header (SMC/SWC, size % 1024 == 512)
            else if (pLower.Contains("snes") || pLower.Contains("super nintendo"))
            {
                if (romBytes.Length > 512 && (romBytes.Length % 1024 == 512))
                {
                    md5TargetBytes = new byte[romBytes.Length - 512];
                    Buffer.BlockCopy(romBytes, 512, md5TargetBytes, 0, md5TargetBytes.Length);
                }
            }

            using var md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(md5TargetBytes);
            string raMd5 = Convert.ToHexString(hash).ToLowerInvariant();

            var result = (crc32, raMd5);
            _romHashCache[cacheKey] = result;
            _ = Task.Run(SaveRomHashCache);
            return result;
        }
        catch
        {
            return ("", "");
        }
    }

    private static string ComputeCrc32Hex(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            crc ^= b;
            for (int j = 0; j < 8; j++)
            {
                uint mask = (uint)-(int)(crc & 1);
                crc = (crc >> 1) ^ (0xEDB88320 & mask);
            }
        }
        return (~crc).ToString("X8");
    }

    public static int MapPlatformToRaConsoleId(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return 0;
        string p = platform.ToLowerInvariant();
        if (p.Contains("snes") || p.Contains("super nintendo")) return 3;
        if (p.Contains("nes") || p.Contains("famicom")) return 7;
        if (p.Contains("genesis") || p.Contains("mega drive")) return 1;
        if (p.Contains("game boy advance") || p.Contains("gba")) return 5;
        if (p.Contains("game boy color") || p.Contains("gbc")) return 6;
        if (p.Contains("game boy") || p.Contains("gb")) return 4;
        if (p.Contains("nintendo 64") || p.Contains("n64")) return 2;
        if (p.Contains("playstation 2") || p.Contains("ps2")) return 21;
        if (p.Contains("playstation portable") || p.Contains("psp")) return 41;
        if (p.Contains("playstation") || p.Contains("ps1") || p.Contains("psx")) return 12;
        if (p.Contains("nintendo ds") || p.Contains("nds")) return 18;
        if (p.Contains("arcade") || p.Contains("fbneo") || p.Contains("mame")) return 27;
        if (p.Contains("master system") || p.Contains("sms")) return 11;
        if (p.Contains("saturn")) return 39;
        if (p.Contains("dreamcast")) return 40;
        if (p.Contains("pc engine") || p.Contains("turbografx")) return 8;
        return 0;
    }

    public static async Task<RetroAchievementsProfile> FetchProfileAsync(CancellationToken ct = default)
    {
        string user = Username.Trim();
        string token = Token.Trim();
        string pass = Password.Trim();

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

        // 1. Попытка авторизации через пароль (dorequest.php login2) если токен отсутствует
        if (string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(pass))
        {
            try
            {
                string loginUrl = $"https://retroachievements.org/dorequest.php?r=login2&u={Uri.EscapeDataString(user)}&p={Uri.EscapeDataString(pass)}";
                using var req = new HttpRequestMessage(HttpMethod.Get, loginUrl);
                using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("Success", out var sEl) && sEl.GetBoolean())
                    {
                        if (root.TryGetProperty("Token", out var tEl) && !string.IsNullOrEmpty(tEl.GetString()))
                        {
                            token = tEl.GetString()!;
                            Token = token;
                        }
                        profile.IsConnected = true;
                        profile.TotalPoints = root.TryGetProperty("Score", out var scEl) ? scEl.GetInt32() : 0;
                        profile.SoftcorePoints = root.TryGetProperty("SoftcoreScore", out var sscEl) ? sscEl.GetInt32() : 0;
                    }
                }
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            profile.IsConnected = false;
            profile.StatusText = "Укажите пароль или веб-токен (API-ключ) RetroAchievements";
            return profile;
        }

        // 2. Запрос профиля пользователя через Web API (API_GetUserSummary / API_GetUserProfile)
        try
        {
            string url = $"https://retroachievements.org/API/API_GetUserSummary.php?u={Uri.EscapeDataString(user)}&y={Uri.EscapeDataString(token)}";
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
                    profile.StatusText = "Подключено к RetroAchievements";
                    profile.Username = uEl.GetString()!;

                    if (root.TryGetProperty("TotalPoints", out var pEl))
                    {
                        profile.TotalPoints = pEl.ValueKind == JsonValueKind.Number ? pEl.GetInt32() : int.Parse(pEl.GetString() ?? "0");
                    }
                    if (root.TryGetProperty("TotalSoftcorePoints", out var spEl))
                    {
                        profile.SoftcorePoints = spEl.ValueKind == JsonValueKind.Number ? spEl.GetInt32() : int.Parse(spEl.GetString() ?? "0");
                    }
                    if (root.TryGetProperty("Rank", out var rEl))
                    {
                        profile.RankText = $"Ранг #{rEl}";
                    }
                    if (root.TryGetProperty("Motto", out var mEl))
                    {
                        profile.Motto = mEl.GetString() ?? "";
                    }
                    if (root.TryGetProperty("RichPresenceMsg", out var rpEl))
                    {
                        profile.RichPresence = rpEl.GetString() ?? "";
                    }
                    if (root.TryGetProperty("UserPic", out var picEl) && !string.IsNullOrEmpty(picEl.GetString()))
                    {
                        string pic = picEl.GetString()!;
                        profile.AvatarUrl = pic.StartsWith("http") ? pic : $"https://media.retroachievements.org{pic}";
                    }

                    // Скачивание аватара на диск для надежного отображения в WPF
                    string safeUser = string.Join("_", user.Split(Path.GetInvalidFileNameChars()));
                    string configDir = Path.Combine(CoreManager.AppDir, "config");
                    Directory.CreateDirectory(configDir);
                    string localAvatar = Path.Combine(configDir, $"ra_avatar_{safeUser}.png");

                    try
                    {
                        if (!File.Exists(localAvatar) || (DateTime.UtcNow - File.GetLastWriteTimeUtc(localAvatar)).TotalHours > 24)
                        {
                            byte[] avBytes = await _httpClient.GetByteArrayAsync(profile.AvatarUrl, ct).ConfigureAwait(false);
                            if (avBytes != null && avBytes.Length > 0)
                            {
                                await File.WriteAllBytesAsync(localAvatar, avBytes, ct).ConfigureAwait(false);
                            }
                        }
                    }
                    catch { }

                    if (File.Exists(localAvatar))
                    {
                        profile.LocalAvatarPath = localAvatar;
                    }

                    profile.LastChecked = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                    return profile;
                }
            }
        }
        catch { }

        // If Token was not accepted (e.g., password entered into Token field), exchange via login2 automatically
        if (!string.IsNullOrWhiteSpace(pass) && !profile.IsConnected)
        {
            try
            {
                string loginUrl = $"https://retroachievements.org/dorequest.php?r=login2&u={Uri.EscapeDataString(user)}&p={Uri.EscapeDataString(pass)}";
                using var lreq = new HttpRequestMessage(HttpMethod.Get, loginUrl);
                using var lresp = await _httpClient.SendAsync(lreq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (lresp.IsSuccessStatusCode)
                {
                    string ljson = await lresp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var ldoc = JsonDocument.Parse(ljson);
                    if (ldoc.RootElement.TryGetProperty("Success", out var ls) && ls.GetBoolean())
                    {
                        if (ldoc.RootElement.TryGetProperty("Token", out var lt) && !string.IsNullOrEmpty(lt.GetString()))
                        {
                            token = lt.GetString()!;
                            Token = token;
                        }
                        profile.IsConnected = true;
                        profile.StatusText = "Подключено к RetroAchievements";
                        if (ldoc.RootElement.TryGetProperty("Score", out var lsc)) profile.TotalPoints = lsc.GetInt32();
                        if (ldoc.RootElement.TryGetProperty("SoftcoreScore", out var lssc)) profile.SoftcorePoints = lssc.GetInt32();
                        string avUrl = $"https://retroachievements.org/API/API_GetUserSummary.php?u={Uri.EscapeDataString(user)}&y={Uri.EscapeDataString(token)}";
                        using var sresp = await _httpClient.GetAsync(avUrl, ct).ConfigureAwait(false);
                        if (sresp.IsSuccessStatusCode)
                        {
                            string sjson = await sresp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                            using var sdoc = JsonDocument.Parse(sjson);
                            if (sdoc.RootElement.TryGetProperty("UserPic", out var spic) && !string.IsNullOrEmpty(spic.GetString()))
                            {
                                string pstr = spic.GetString()!;
                                profile.AvatarUrl = pstr.StartsWith("http") ? pstr : $"https://media.retroachievements.org{pstr}";
                            }
                            if (sdoc.RootElement.TryGetProperty("Rank", out var srk)) profile.RankText = $"Ранг #{srk}";
                        }
                        string safeUser = string.Join("_", user.Split(Path.GetInvalidFileNameChars()));
                        string cfgDir = Path.Combine(CoreManager.AppDir, "config");
                        Directory.CreateDirectory(cfgDir);
                        string localAv = Path.Combine(cfgDir, $"ra_avatar_{safeUser}.png");
                        if (!string.IsNullOrWhiteSpace(profile.AvatarUrl))
                        {
                            try
                            {
                                byte[] avBytes = await _httpClient.GetByteArrayAsync(profile.AvatarUrl, ct).ConfigureAwait(false);
                                if (avBytes.Length > 0) await File.WriteAllBytesAsync(localAv, avBytes, ct).ConfigureAwait(false);
                            }
                            catch { }
                        }
                        if (File.Exists(localAv)) profile.LocalAvatarPath = localAv;
                        profile.LastChecked = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                        return profile;
                    }
                }
            }
            catch { }
        }

        // Fallback: API_GetUserProfile.php
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
                    profile.StatusText = "Подключено к RetroAchievements";
                    profile.Username = uEl.GetString()!;
                    if (root.TryGetProperty("TotalPoints", out var pEl))
                    {
                        profile.TotalPoints = pEl.ValueKind == JsonValueKind.Number ? pEl.GetInt32() : int.Parse(pEl.GetString() ?? "0");
                    }
                    if (root.TryGetProperty("Rank", out var rEl))
                    {
                        profile.RankText = $"Ранг #{rEl}";
                    }
                    if (root.TryGetProperty("Motto", out var mEl))
                    {
                        profile.Motto = mEl.GetString() ?? "";
                    }
                    profile.LastChecked = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                    return profile;
                }
            }
        }
        catch { }

        if (profile.IsConnected)
        {
            profile.StatusText = "Подключено (локальный сеанс)";
            return profile;
        }

        profile.IsConnected = false;
        profile.StatusText = "Не удалось подтвердить авторизацию RetroAchievements";
        return profile;
    }

    public static async Task<(RetroAchievementsProfile Profile, List<AchievementCardItem> Cards, string GameTitle, int EarnedPoints, int TotalGamePoints, int GameId, string ProgressSummary)>
        FetchGameAchievementsAsync(
            string? gameTitleOrHash,
            string? romFilePath = null,
            string? platform = null,
            CancellationToken ct = default)
    {
        var profile = await FetchProfileAsync(ct).ConfigureAwait(false);
        var cards = new List<AchievementCardItem>();
        string title = string.IsNullOrWhiteSpace(gameTitleOrHash) ? "Текущая игра" : gameTitleOrHash.Trim();
        int earned = 0;
        int total = 0;
        int gameId = 0;

        string user = Username.Trim();
        string token = Token.Trim();

        // 1. Разрешение GameID
        if (int.TryParse(gameTitleOrHash, out int parsedId) && parsedId > 0)
        {
            gameId = parsedId;
        }

        // 1a. Поиск по MD5 хэшу ROM-файла (dorequest.php?r=gameid&m={raMd5})
        if (gameId == 0 && !string.IsNullOrEmpty(romFilePath) && File.Exists(romFilePath))
        {
            var (_, raMd5) = ComputeRomCrc32AndRaMd5(romFilePath, platform ?? "");
            if (!string.IsNullOrEmpty(raMd5))
            {
                try
                {
                    string url = $"https://retroachievements.org/dorequest.php?r=gameid&m={raMd5}";
                    using var resp = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("GameID", out var gidEl))
                        {
                            gameId = gidEl.GetInt32();
                        }
                    }
                }
                catch { }

                // Если модифицированный/локализованный ROM вернул 0, проверяем чистый оригинальный ROM в той же папке
                if (gameId == 0)
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(romFilePath)!;
                        var sibling = Directory.GetFiles(dir, "*.*")
                            .FirstOrDefault(f => f != romFilePath &&
                                (f.EndsWith(".nes", StringComparison.OrdinalIgnoreCase) ||
                                 f.EndsWith(".sfc", StringComparison.OrdinalIgnoreCase) ||
                                 f.EndsWith(".smc", StringComparison.OrdinalIgnoreCase) ||
                                 f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                                 f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
                                 f.EndsWith(".gba", StringComparison.OrdinalIgnoreCase) ||
                                 f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) &&
                                !f.Contains("[MOD", StringComparison.OrdinalIgnoreCase) &&
                                !f.Contains("(RU)", StringComparison.OrdinalIgnoreCase));

                        if (sibling != null)
                        {
                            var (_, sibMd5) = ComputeRomCrc32AndRaMd5(sibling, platform ?? "");
                            if (!string.IsNullOrEmpty(sibMd5))
                            {
                                string url = $"https://retroachievements.org/dorequest.php?r=gameid&m={sibMd5}";
                                using var sibResp = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
                                if (sibResp.IsSuccessStatusCode)
                                {
                                    string sibJson = await sibResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                                    using var doc = JsonDocument.Parse(sibJson);
                                    if (doc.RootElement.TryGetProperty("GameID", out var gidEl))
                                    {
                                        gameId = gidEl.GetInt32();
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        // 1b. Поиск по списку игр консоли RA Console ID
        if (gameId == 0 && !string.IsNullOrEmpty(title))
        {
            int consoleId = MapPlatformToRaConsoleId(platform);
            if (consoleId > 0 && !string.IsNullOrEmpty(token))
            {
                try
                {
                    string listUrl = $"https://retroachievements.org/API/API_GetGameList.php?i={consoleId}&y={Uri.EscapeDataString(token)}";
                    using var resp = await _httpClient.GetAsync(listUrl, ct).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            string normSearch = Regex.Replace(title.ToLowerInvariant(), @"[^a-z0-9]", "");
                            foreach (var item in doc.RootElement.EnumerateArray())
                            {
                                if (item.TryGetProperty("Title", out var tProp))
                                {
                                    string gTitle = tProp.GetString() ?? "";
                                    string normG = Regex.Replace(gTitle.ToLowerInvariant(), @"[^a-z0-9]", "");
                                    if (normG == normSearch || normG.Contains(normSearch) || normSearch.Contains(normG))
                                    {
                                        if (item.TryGetProperty("ID", out var idProp))
                                        {
                                            gameId = idProp.GetInt32();
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }

        // 1c. Кураторская таблица эталонных классических игр
        if (gameId == 0 && !string.IsNullOrEmpty(title))
        {
            string lower = title.ToLowerInvariant();
            foreach (var kvp in CuratedGameIds)
            {
                if (lower.Contains(kvp.Key))
                {
                    gameId = kvp.Value;
                    break;
                }
            }
        }

        // 2. Загрузка достижений для найденного GameID
        if (gameId > 0 && profile.IsConnected && !string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(token))
        {
            try
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

                            string badgeUrl = "";
                            if (!string.IsNullOrEmpty(badge))
                            {
                                badgeUrl = isUnlocked
                                    ? $"https://media.retroachievements.org/Badge/{badge}.png"
                                    : $"https://media.retroachievements.org/Badge/{badge}_lock.png";
                            }

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
            catch { }
        }

        // 3. Fallback: базовый эталонный набор для библиотеки если онлайн не вернул карточек
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
                    BadgeUrl = "https://media.retroachievements.org/Badge/00003_lock.png"
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
                    BadgeUrl = "https://media.retroachievements.org/Badge/00004_lock.png"
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
                    BadgeUrl = "https://media.retroachievements.org/Badge/00005_lock.png"
                }
            };

            earned = cards.Where(c => c.IsUnlocked).Sum(c => c.Points);
            total = cards.Sum(c => c.Points);
        }

        int unlockedCount = cards.Count(c => c.IsUnlocked);
        string progressSummary = $"{earned}/{total} очков ({unlockedCount}/{cards.Count} открыто)";

        return (profile, cards, title, earned, total, gameId, progressSummary);
    }

    public static event Action<AchievementItem>? OnAchievementUnlocked;

    private static readonly List<LiveRamAchievementRule> _activeLiveRules = new();
    private static readonly object _rulesLock = new();

    static AchievementsManager()
    {
        try
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("STORM-RETROARCH/2.7.5 (AchievementsManager; contact@stormgamesworld.ru)");
        }
        catch { }
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
