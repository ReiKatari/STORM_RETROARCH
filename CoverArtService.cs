using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
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
        EnsureMediaIndexStarted();
    }

    private class StormMediaIndex
    {
        public readonly Dictionary<string, string> Covers = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Logos = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<string>> Screenshots = new(StringComparer.OrdinalIgnoreCase);

        public StormMediaIndex()
        {
            try
            {
                // Index COVERS
                string coversDir = @"K:\STORM GAMES WORLD\public\images\COVERS";
                if (Directory.Exists(coversDir))
                {
                    IndexFolder(coversDir, Covers);
                }

                // Index LOGOS
                string logosDir = @"K:\STORM GAMES WORLD\public\images\LOGOS";
                if (Directory.Exists(logosDir))
                {
                    IndexFolder(logosDir, Logos);
                }

                // Index SCREENSHOTS
                string ssDir = @"K:\STORM GAMES WORLD\public\images\SCREENSHOTS";
                if (Directory.Exists(ssDir))
                {
                    IndexScreenshots(ssDir, Screenshots);
                }
            }
            catch { }
        }

        private static void IndexFolder(string root, Dictionary<string, string> dict)
        {
            var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories);
            foreach (var f in files)
            {
                if (!IsImageFile(f)) continue;
                string fn = Path.GetFileName(f);
                string key = NormalizeLookupKey(fn);

                // Handle nested cover.webp / folder.jpg
                if (fn.StartsWith("cover.", StringComparison.OrdinalIgnoreCase) ||
                    fn.StartsWith("folder.", StringComparison.OrdinalIgnoreCase))
                {
                    string parent = Path.GetFileName(Path.GetDirectoryName(f) ?? "");
                    key = NormalizeLookupKey(parent);
                }

                if (!string.IsNullOrEmpty(key) && !dict.ContainsKey(key))
                {
                    dict[key] = f;
                }
            }
        }

        private static void IndexScreenshots(string root, Dictionary<string, List<string>> dict)
        {
            var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories);
            foreach (var f in files)
            {
                if (!IsImageFile(f)) continue;
                string fn = Path.GetFileName(f);
                string baseName = Regex.Replace(Path.GetFileNameWithoutExtension(fn), @"[_\-\s]+\d+$", "");
                string key = NormalizeLookupKey(baseName);
                if (!string.IsNullOrEmpty(key))
                {
                    if (!dict.TryGetValue(key, out var list))
                    {
                        list = new List<string>();
                        dict[key] = list;
                    }
                    if (list.Count < 10) list.Add(f);
                }
            }
        }

        private static bool IsImageFile(string path)
        {
            string ext = Path.GetExtension(path);
            return ext.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static volatile StormMediaIndex? _mediaIndexInstance;
    private static readonly object _mediaIndexLock = new();
    private static Task? _mediaIndexTask;

    public static void EnsureMediaIndexStarted()
    {
        if (_mediaIndexInstance != null || _mediaIndexTask != null) return;
        lock (_mediaIndexLock)
        {
            if (_mediaIndexInstance != null || _mediaIndexTask != null) return;
            _mediaIndexTask = Task.Run(() =>
            {
                try
                {
                    _mediaIndexInstance = new StormMediaIndex();
                }
                catch { }
            });
        }
    }

    private static StormMediaIndex? MediaIndex
    {
        get
        {
            EnsureMediaIndexStarted();
            return _mediaIndexInstance;
        }
    }

    public static string NormalizeLookupKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        string clean = Path.GetFileNameWithoutExtension(name);
        clean = Regex.Replace(clean, @"\[[^\]]*\]|\([^)]*\)", " ");
        clean = clean.ToLowerInvariant();
        clean = Regex.Replace(clean, @"[^a-z0-9а-яё]", "");
        return clean;
    }

    public static Task LoadCoversParallelAsync(IEnumerable<RomItem> items, bool forceRefresh = false)
    {
        if (items == null) return Task.CompletedTask;
        var list = items.ToList();
        if (list.Count == 0) return Task.CompletedTask;

        return Task.Run(async () =>
        {
            using var sem = new SemaphoreSlim(8, 8);
            var tasks = list.Select(async item =>
            {
                await sem.WaitAsync().ConfigureAwait(false);
                try
                {
                    await LoadCoverAsync(item, forceRefresh).ConfigureAwait(false);
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
        });
    }

    public static async Task LoadCoverAsync(RomItem item, bool forceRefresh = false)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Name)) return;

        try
        {
            string safePlatform = SanitizeFileName(item.Platform);
            string safeName = SanitizeFileName(item.Name);
            string cacheDir = Path.Combine(CoreManager.AppDir, "thumbnails", safePlatform);
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

            string cacheFile = Path.Combine(cacheDir, $"{safeName}.png");

            // 1. Check local thumbnails cache (unless forceRefresh)
            if (!forceRefresh && File.Exists(cacheFile))
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
            else if (forceRefresh && File.Exists(cacheFile))
            {
                try { File.Delete(cacheFile); } catch { }
            }

            // 2. Fast O(1) in-memory index from K:\STORM GAMES WORLD\public\images\COVERS
            string localCover = FindLocalStormCover(item.Name, item.Region, item.Platform);
            if (!string.IsNullOrEmpty(localCover) && File.Exists(localCover))
            {
                byte[] localBytes = await File.ReadAllBytesAsync(localCover).ConfigureAwait(false);
                if (IsSupportedBitmapBytes(localBytes) && TryApplyBytesToItemAndCache(item, localBytes, cacheFile))
                {
                    return;
                }
            }

            // 3. Online Libretro Thumbnails Server (with underscore colon replacement)
            byte[]? downloadedBytes = await FetchLibretroThumbnailAsync(item.Name, item.Region, item.Platform).ConfigureAwait(false);
            if (downloadedBytes != null && IsSupportedBitmapBytes(downloadedBytes))
            {
                if (TryApplyBytesToItemAndCache(item, downloadedBytes, cacheFile)) return;
            }

            // 4. Wikipedia REST API Fallback
            byte[]? wikiBytes = await FetchWikipediaCoverAsync(item.Name).ConfigureAwait(false);
            if (wikiBytes != null && IsSupportedBitmapBytes(wikiBytes))
            {
                if (TryApplyBytesToItemAndCache(item, wikiBytes, cacheFile)) return;
            }

            // 5. Fallback to 280x380 studio poster
            if (item.Icon == null)
            {
                Application.Current?.Dispatcher.BeginInvoke(() => item.GeneratePixelIcon());
            }
        }
        catch
        {
            if (item.Icon == null)
            {
                Application.Current?.Dispatcher.BeginInvoke(() => item.GeneratePixelIcon());
            }
        }
    }

    public static bool IsSupportedBitmapBytes(byte[]? bytes)
    {
        if (bytes == null || bytes.Length < 32) return false;
        // PNG magic: 89 50 4E 47
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true;
        // JPEG magic: FF D8 FF
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true;
        // BMP magic: 42 4D
        if (bytes[0] == 0x42 && bytes[1] == 0x4D) return true;
        // WebP magic: 'RIFF' at 0..3 and 'WEBP' at 8..11
        if (bytes.Length >= 12 &&
            bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return true;
        return false;
    }

    private static bool TryApplyBytesToItem(RomItem item, byte[] imageBytes)
    {
        try
        {
            using var ms = new MemoryStream(imageBytes);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.DecodePixelWidth = 240;
            bi.EndInit();
            bi.Freeze();

            Application.Current?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
            {
                item.Icon = bi;
            });
            return true;
        }
        catch { }
        return false;
    }

    private static bool TryApplyBytesToItemAndCache(RomItem item, byte[] imageBytes, string cacheFile)
    {
        try
        {
            using var ms = new MemoryStream(imageBytes);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.DecodePixelWidth = 240;
            bi.EndInit();
            bi.Freeze();

            try
            {
                string? dir = Path.GetDirectoryName(cacheFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bi));
                using var fs = File.Create(cacheFile);
                encoder.Save(fs);
            }
            catch { }

            Application.Current?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
            {
                item.Icon = bi;
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string FindLocalStormCover(string cleanName, string region, string platform)
    {
        try
        {
            string key = NormalizeLookupKey(cleanName);
            if (string.IsNullOrEmpty(key)) return "";
            var covers = MediaIndex?.Covers; if (covers == null) return "";
            if (covers.TryGetValue(key, out var path) && File.Exists(path))
            {
                return path;
            }

            // Instant O(N) in-memory prefix match without re-scanning K:\ disk
            foreach (var kv in covers)
            {
                if (kv.Key.Length >= 5 && (kv.Key.StartsWith(key, StringComparison.OrdinalIgnoreCase) || key.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    if (File.Exists(kv.Value)) return kv.Value;
                }
            }
        }
        catch { }
        return "";
    }

    private static bool IsSupportedImageExtension(string path)
    {
        string ext = Path.GetExtension(path);
        return ext.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
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
            int limit = folder == "Named_Boxarts" ? Math.Min(candidates.Count, 14) : Math.Min(candidates.Count, 6);
            for (int idx = 0; idx < limit; idx++)
            {
                string cand = candidates[idx];
                string encSys = Uri.EscapeDataString(sysDir);
                string encCand = Uri.EscapeDataString(cand);

                string cdnUrl = $"https://thumbnails.libretro.com/{encSys}/{folder}/{encCand}.png";
                var data = await TryDownloadBufferWithSymlink(cdnUrl).ConfigureAwait(false);
                if (data != null && IsSupportedBitmapBytes(data)) return data;
            }
        }

        return null;
    }

    private static async Task<byte[]?> FetchWikipediaCoverAsync(string rawTitle)
    {
        string clean = Regex.Replace(rawTitle, @"\[[^\]]*\]|\([^)]*\)", "").Trim();
        clean = Regex.Replace(clean, @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(clean)) return null;

        string[] queries = { $"{clean} (video game)", clean };
        foreach (var q in queries)
        {
            try
            {
                string url = $"https://en.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(q)}";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.UserAgent.ParseAdd("STORM-RETROARCH/2.2.0 (Game Media Engine; contact@stormgamesworld.ru)");
                using var resp = await _httpClient.SendAsync(req).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) continue;

                string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string? imgUrl = null;
                if (root.TryGetProperty("originalimage", out var orig) && orig.TryGetProperty("source", out var origSrc))
                {
                    imgUrl = origSrc.GetString();
                }
                else if (root.TryGetProperty("thumbnail", out var thumb) && thumb.TryGetProperty("source", out var thumbSrc))
                {
                    imgUrl = thumbSrc.GetString();
                }

                if (!string.IsNullOrEmpty(imgUrl))
                {
                    using var imgReq = new HttpRequestMessage(HttpMethod.Get, imgUrl);
                    imgReq.Headers.UserAgent.ParseAdd("STORM-RETROARCH/2.2.0 (Game Media Engine)");
                    using var imgResp = await _httpClient.SendAsync(imgReq).ConfigureAwait(false);
                    if (imgResp.IsSuccessStatusCode)
                    {
                        byte[] bytes = await imgResp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        if (IsSupportedBitmapBytes(bytes)) return bytes;
                    }
                }
            }
            catch { }
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

        // 1. If title contains " - ", generate "_ " variants FIRST!
        if (colonNorm.Contains(" - "))
        {
            string underAll = colonNorm.Replace(" - ", "_ ");
            PushBase(underAll);

            int firstDash = colonNorm.IndexOf(" - ");
            if (firstDash > 0)
            {
                string underFirst = colonNorm.Substring(0, firstDash) + "_ " + colonNorm.Substring(firstDash + 3);
                PushBase(underFirst);
            }

            if (colonNorm.Contains(" - The "))
            {
                PushBase(colonNorm.Replace(" - The ", "_ The "));
                PushBase(colonNorm.Replace(" - The ", "_ "));
            }
        }

        // 2. Standard normalized title
        if (Regex.IsMatch(colonNorm, @"^the\s+", RegexOptions.IgnoreCase))
        {
            string withoutThe = Regex.Replace(colonNorm, @"^the\s+", "", RegexOptions.IgnoreCase).Trim();
            PushBase($"{withoutThe}, The");
        }
        PushBase(colonNorm);

        // 3. Roman <-> Arabic swaps (II <-> 2, III <-> 3, etc.)
        (string From, string To)[] swaps =
        {
            (" II", " 2"), (" 2", " II"),
            (" III", " 3"), (" 3", " III"),
            (" IV", " 4"), (" 4", " IV"),
            (" V", " 5"), (" 5", " V")
        };
        foreach (var (fromStr, toStr) in swaps)
        {
            if (colonNorm.Contains(fromStr))
            {
                string swapped = colonNorm.Replace(fromStr, toStr);
                PushBase(swapped.Replace(" - ", "_ "));
                PushBase(swapped);
            }
        }

        // 4. Known expansions (e.g. Dungeon Master II)
        if (colonNorm.Contains("Dungeon Master II", StringComparison.OrdinalIgnoreCase) &&
            !colonNorm.Contains("Skullkeep", StringComparison.OrdinalIgnoreCase))
        {
            PushBase("Dungeon Master II_ The Legend of Skullkeep");
            PushBase("Dungeon Master II - The Legend of Skullkeep");
        }

        // 5. Subtitle after dash (e.g. "Forgotten Realms - Unlimited Adventures" -> "Unlimited Adventures") & prefix before dash
        if (colonNorm.Contains(" - "))
        {
            var parts = colonNorm.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                string suffixTitle = parts[parts.Length - 1].Trim();
                if (suffixTitle.Length >= 5) PushBase(suffixTitle);
            }
            string prefix = parts[0].Trim();
            PushBase(prefix);
        }

        var results = new List<string>();
        bool isDos = (platform ?? "").Contains("DOS", StringComparison.OrdinalIgnoreCase);
        if (isDos)
        {
            // Libretro DOS thumbnails use bare titles without (USA)/(Europe) tags first
            foreach (var b in baseTitles)
            {
                if (!results.Contains(b)) results.Add(b);
            }
            foreach (var suf in regionSuffixes.Take(2))
            {
                foreach (var b in baseTitles.Take(3))
                {
                    string cand = $"{b} {suf}";
                    if (!results.Contains(cand)) results.Add(cand);
                }
            }
        }
        else
        {
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
        }

        return results;
    }

    public static ImageSource? LoadClearLogo(RomItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Name)) return null;
        try
        {
            string safePlatform = SanitizeFileName(item.Platform);
            string safeName = SanitizeFileName(item.Name);
            string localLogoCache = Path.Combine(CoreManager.AppDir, "thumbnails", safePlatform, $"{safeName}_logo.png");
            if (File.Exists(localLogoCache))
            {
                return LoadBitmapFromPath(localLogoCache);
            }

            string key = NormalizeLookupKey(item.Name);
            var logos = MediaIndex?.Logos; if (logos == null) return null;
            if (logos.TryGetValue(key, out var stormPath) && File.Exists(stormPath))
            {
                return LoadBitmapFromPath(stormPath);
            }
        }
        catch { }
        return null;
    }

    public static List<ImageSource> LoadGameScreenshots(RomItem item)
    {
        var list = new List<ImageSource>();
        if (item == null || string.IsNullOrWhiteSpace(item.Name)) return list;
        try
        {
            string key = NormalizeLookupKey(item.Name);
            var screenshots = MediaIndex?.Screenshots; if (screenshots == null) return list;
            if (screenshots.TryGetValue(key, out var paths))
            {
                foreach (var p in paths)
                {
                    var img = LoadBitmapFromPath(p);
                    if (img != null) list.Add(img);
                    if (list.Count >= 6) break;
                }
            }
        }
        catch { }
        return list;
    }

    public static List<string> FindGameManualPdfs(RomItem item)
    {
        var list = new List<string>();
        if (item == null) return list;
        try
        {
            // 1. Check game folder and its parent
            if (!string.IsNullOrEmpty(item.FilePath))
            {
                string? gameDir = Path.GetDirectoryName(item.FilePath);
                if (!string.IsNullOrEmpty(gameDir) && Directory.Exists(gameDir))
                {
                    foreach (var pdf in Directory.GetFiles(gameDir, "*.pdf", SearchOption.TopDirectoryOnly))
                    {
                        if (!list.Contains(pdf)) list.Add(pdf);
                    }

                    string? parentDir = Directory.GetParent(gameDir)?.FullName;
                    if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                    {
                        foreach (var pdf in Directory.GetFiles(parentDir, "*.pdf", SearchOption.TopDirectoryOnly))
                        {
                            if (!list.Contains(pdf)) list.Add(pdf);
                        }
                    }
                }
            }

            // 2. Check STORM ecosystem books & manuals directories
            string[] stormManualDirs =
            {
                @"K:\STORM GAMES WORLD\public\manuals",
                @"K:\STORM GAMES WORLD\books",
                Path.Combine(CoreManager.AppDir, "manuals")
            };

            string normName = NormalizeLookupKey(item.Name);
            foreach (var d in stormManualDirs)
            {
                if (!Directory.Exists(d)) continue;
                foreach (var pdf in Directory.GetFiles(d, "*.pdf", SearchOption.AllDirectories))
                {
                    string normPdf = NormalizeLookupKey(Path.GetFileNameWithoutExtension(pdf));
                    if (normPdf.Contains(normName) || normName.Contains(normPdf))
                    {
                        if (!list.Contains(pdf)) list.Add(pdf);
                    }
                }
            }
        }
        catch { }
        return list;
    }

    public static bool SetCustomCoverFromFile(RomItem item, string imageFilePath)
    {
        if (item == null || string.IsNullOrWhiteSpace(imageFilePath) || !File.Exists(imageFilePath)) return false;
        try
        {
            byte[] bytes = File.ReadAllBytes(imageFilePath);
            if (!IsSupportedBitmapBytes(bytes)) return false;

            string safePlatform = SanitizeFileName(item.Platform);
            string safeName = SanitizeFileName(item.Name);
            string cacheDir = Path.Combine(CoreManager.AppDir, "thumbnails", safePlatform);
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

            string cacheFile = Path.Combine(cacheDir, $"{safeName}.png");
            return TryApplyBytesToItemAndCache(item, bytes, cacheFile);
        }
        catch { }
        return false;
    }

    private static BitmapImage? LoadBitmapFromPath(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 240;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
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
