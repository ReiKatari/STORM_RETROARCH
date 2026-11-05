using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace StormRetroArch;

public static class CoverArtService
{
    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(7)
    };

    static CoverArtService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("STORM-GAMES-WORLD/3.0 (Multi-Platform Media Engine)");
    }

    public static async Task LoadCoverAsync(RomItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Name)) return;

        try
        {
            string safePlatform = SanitizeFileName(item.Platform);
            string safeName = SanitizeFileName(item.Name);
            string cacheDir = Path.Combine(CoreManager.AppDir, "thumbnails", safePlatform);
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

            string cacheFile = Path.Combine(cacheDir, $"{safeName}.png");

            // 1. Check valid PNG/JPG in local thumbnails cache (verify PNG/JPEG magic header, not WebP)
            if (File.Exists(cacheFile))
            {
                byte[] existing = await File.ReadAllBytesAsync(cacheFile).ConfigureAwait(false);
                if (IsSupportedBitmapBytes(existing))
                {
                    if (TryApplyBytesToItem(item, existing)) return;
                }
                else
                {
                    try { File.Delete(cacheFile); } catch { }
                }
            }

            // 2. Check local STORM GAMES WORLD raw PNG/JPG covers (if present)
            string localCover = FindLocalStormCover(item.Name, item.Region, item.Platform);
            if (!string.IsNullOrEmpty(localCover) && File.Exists(localCover))
            {
                byte[] localBytes = await File.ReadAllBytesAsync(localCover).ConfigureAwait(false);
                if (IsSupportedBitmapBytes(localBytes) && TryApplyBytesToItem(item, localBytes))
                {
                    try { await File.WriteAllBytesAsync(cacheFile, localBytes).ConfigureAwait(false); } catch { }
                    return;
                }
            }

            // 3. Online Libretro Thumbnails Server (exact port of K:\STORM GAMES WORLD\src\app\api\media\auto-fetch\route.ts)
            byte[]? downloadedBytes = await FetchLibretroThumbnailAsync(item.Name, item.Region, item.Platform).ConfigureAwait(false);
            if (downloadedBytes != null && IsSupportedBitmapBytes(downloadedBytes))
            {
                await File.WriteAllBytesAsync(cacheFile, downloadedBytes).ConfigureAwait(false);
                if (TryApplyBytesToItem(item, downloadedBytes)) return;
            }

            // 4. Fallback to stylized cartridge/disc icon
            if (item.Icon == null)
            {
                item.GeneratePixelIcon();
            }
        }
        catch
        {
            if (item.Icon == null)
            {
                item.GeneratePixelIcon();
            }
        }
    }

    private static bool IsSupportedBitmapBytes(byte[]? bytes)
    {
        if (bytes == null || bytes.Length < 350) return false;
        // PNG magic: 89 50 4E 47
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true;
        // JPEG magic: FF D8 FF
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true;
        // BMP magic: 42 4D
        if (bytes[0] == 0x42 && bytes[1] == 0x4D) return true;
        return false;
    }

    private static bool TryApplyBytesToItem(RomItem item, byte[] imageBytes)
    {
        try
        {
            BitmapImage? decoded = null;
            using (var ms = new MemoryStream(imageBytes))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                decoded = bmp;
            }
            if (decoded != null)
            {
                item.Icon = decoded;
                return true;
            }
        }
        catch { }
        return false;
    }

    private static string FindLocalStormCover(string cleanName, string region, string platform)
    {
        string root = @"K:\STORM GAMES WORLD\images\COVERS";
        if (!Directory.Exists(root)) return "";

        string safeName = SanitizeFileName(cleanName);
        try
        {
            var files = Directory.GetFiles(root, $"{safeName}*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (files.Length > 0)
            {
                if (!string.IsNullOrEmpty(region))
                {
                    foreach (var f in files)
                    {
                        if (f.Contains($"[{region}]", StringComparison.OrdinalIgnoreCase))
                            return f;
                    }
                }
                return files[0];
            }
        }
        catch { }
        return "";
    }

    private static async Task<byte[]?> FetchLibretroThumbnailAsync(string rawTitle, string region, string platform)
    {
        string sysDir = MapPlatformToLibretroDir(platform);
        if (string.IsNullOrEmpty(sysDir)) return null;

        string ghRepo = sysDir.Replace(' ', '_');
        var candidates = BuildLibretroCandidates(rawTitle, region, platform);

        string[] folders = { "Named_Boxarts", "Named_Titles" };
        foreach (var folder in folders)
        {
            foreach (var cand in candidates)
            {
                string encSys = Uri.EscapeDataString(sysDir);
                string encCand = Uri.EscapeDataString(cand);
                string encGh = Uri.EscapeDataString(ghRepo);

                string cdnUrl = $"https://thumbnails.libretro.com/{encSys}/{folder}/{encCand}.png";
                var data = await TryDownloadBufferWithSymlink(cdnUrl).ConfigureAwait(false);
                if (data != null && IsSupportedBitmapBytes(data)) return data;

                string ghUrl = $"https://raw.githubusercontent.com/libretro-thumbnails/{encGh}/master/{folder}/{encCand}.png";
                data = await TryDownloadBufferWithSymlink(ghUrl).ConfigureAwait(false);
                if (data != null && IsSupportedBitmapBytes(data)) return data;
            }
        }

        return null;
    }

    private static async Task<byte[]?> TryDownloadBufferWithSymlink(string url, int depth = 0)
    {
        if (depth > 2) return null;
        try
        {
            using var res = await _httpClient.GetAsync(url).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;

            byte[] bytes = await res.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (bytes.Length > 4 && bytes.Length < 300)
            {
                string text = System.Text.Encoding.UTF8.GetString(bytes).Trim();
                if (Regex.IsMatch(text, @"^[^\r\n\0]+\.(png|jpg|jpeg)$", RegexOptions.IgnoreCase))
                {
                    int lastSlash = url.LastIndexOf('/');
                    if (lastSlash > 0)
                    {
                        string baseUrl = url.Substring(0, lastSlash + 1);
                        string targetUrl = baseUrl + Uri.EscapeDataString(text);
                        return await TryDownloadBufferWithSymlink(targetUrl, depth + 1).ConfigureAwait(false);
                    }
                }
                return null;
            }

            if (bytes.Length < 350) return null;
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    public static List<string> BuildLibretroCandidates(string rawTitle, string preferredRegion, string platform)
    {
        string clean = Regex.Replace(rawTitle, @"\[[^\]]*\]|\([^)]*\)", "").Trim();
        clean = Regex.Replace(clean, @"\s+", " ").Trim();
        string colonNorm = Regex.Replace(clean, @"\s*:\s*", " - ").Trim();

        string regUpper = (preferredRegion ?? "US").ToUpperInvariant();
        List<string> regionSuffixes;

        if (regUpper.Contains("JP") || regUpper.Contains("JAP"))
        {
            regionSuffixes = new List<string> { "(Japan)", "(Japan, USA)", "(World)", "(USA)", "(Europe)" };
        }
        else if (regUpper.Contains("EU") || regUpper.Contains("PAL"))
        {
            regionSuffixes = new List<string> { "(Europe)", "(USA, Europe)", "(World)", "(USA)", "(Japan)" };
        }
        else if (regUpper.Contains("RU") || regUpper.Contains("RUS"))
        {
            regionSuffixes = new List<string> { "(Russia)", "(USA)", "(USA, Europe)", "(World)", "(Europe)", "(Japan)" };
        }
        else
        {
            regionSuffixes = new List<string> { "(USA)", "(USA, Europe)", "(World)", "(Europe)", "(Japan)" };
        }

        var baseTitles = new List<string>();
        void PushBase(string b)
        {
            string norm = Regex.Replace(b, @"\s*:\s*", " - ");
            norm = Regex.Replace(norm, @"[&*/:`<>?\|""]", "_");
            norm = Regex.Replace(norm, @"\s+", " ").Trim();
            if (!string.IsNullOrEmpty(norm) && !baseTitles.Contains(norm)) baseTitles.Add(norm);
        }

        if (Regex.IsMatch(colonNorm, @"^the\s+", RegexOptions.IgnoreCase))
        {
            string withoutThe = Regex.Replace(colonNorm, @"^the\s+", "", RegexOptions.IgnoreCase).Trim();
            PushBase($"{withoutThe}, The");
        }
        PushBase(colonNorm);

        var results = new List<string>();
        foreach (var suf in regionSuffixes)
        {
            foreach (var b in baseTitles)
            {
                string cand = $"{b} {suf}";
                if (!results.Contains(cand)) results.Add(cand);
            }
        }
        foreach (var b in baseTitles)
        {
            if (!results.Contains(b)) results.Add(b);
        }

        return results;
    }

    public static string MapPlatformToLibretroDir(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return "Nintendo - Nintendo Entertainment System";
        string p = platform.ToUpperInvariant();

        if (p.Contains("SUPER NINTENDO") || p.Contains("SNES") || p.Contains("SUPER FAMICOM"))
            return "Nintendo - Super Nintendo Entertainment System";
        if (p.Contains("NINTENDO 64") || p.Contains("N64"))
            return "Nintendo - Nintendo 64";
        if (p.Contains("GAME BOY ADVANCE") || p.Contains("GBA"))
            return "Nintendo - Game Boy Advance";
        if (p.Contains("GAME BOY COLOR") || p.Contains("GBC"))
            return "Nintendo - Game Boy Color";
        if (p.Contains("GAME BOY") || p.Contains("GAMEBOY"))
            return "Nintendo - Game Boy";
        if (p.Contains("NINTENDO DS") || p.Contains("NDS"))
            return "Nintendo - Nintendo DS";
        if (p.Contains("NINTENDO 3DS") || p.Contains("3DS"))
            return "Nintendo - Nintendo 3DS";
        if (p.Contains("GAMECUBE"))
            return "Nintendo - GameCube";
        if (p.Contains("NES") || p.Contains("FAMICOM") || p.Contains("NINTENDO ENTERTAINMENT"))
            return "Nintendo - Nintendo Entertainment System";
        if (p.Contains("GENESIS") || p.Contains("MEGA DRIVE") || p.Contains("MEGADRIVE"))
            return "Sega - Mega Drive - Genesis";
        if (p.Contains("MASTER SYSTEM"))
            return "Sega - Master System - Mark III";
        if (p.Contains("GAME GEAR"))
            return "Sega - Game Gear";
        if (p.Contains("SATURN"))
            return "Sega - Saturn";
        if (p.Contains("DREAMCAST"))
            return "Sega - Dreamcast";
        if (p.Contains("PLAYSTATION PORTABLE") || p.Contains("PSP"))
            return "Sony - PlayStation Portable";
        if (p.Contains("PLAYSTATION 2") || p.Contains("PS2"))
            return "Sony - PlayStation 2";
        if (p.Contains("PLAYSTATION") || p.Contains("PS1") || p.Contains("PSX"))
            return "Sony - PlayStation";
        if (p.Contains("PC ENGINE") || p.Contains("TURBOGRAFX"))
            return "NEC - PC Engine - TurboGrafx 16";
        if (p.Contains("ATARI 2600"))
            return "Atari - 2600";
        if (p.Contains("ATARI 7800"))
            return "Atari - 7800";
        if (p.Contains("LYNX"))
            return "Atari - Lynx";
        if (p.Contains("JAGUAR"))
            return "Atari - Jaguar";
        if (p.Contains("NEO GEO POCKET"))
            return "SNK - Neo Geo Pocket Color";
        if (p.Contains("WONDERSWAN"))
            return "Bandai - WonderSwan Color";
        if (p.Contains("ARCADE") || p.Contains("FBNEO") || p.Contains("NEO GEO"))
            return "FBNeo - Arcade Games";
        if (p.Contains("DOS") || p.Contains("MS-DOS"))
            return "DOS";

        return "Nintendo - Nintendo Entertainment System";
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Unknown";
        string clean = Regex.Replace(name, @"[\\/:*?""<>|]", "_");
        return clean.Trim();
    }
}
