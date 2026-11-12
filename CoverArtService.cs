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
    private static readonly HttpClient _httpClient = new(new SocketsHttpHandler
    {
        MaxConnectionsPerServer = 16,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public static string ThumbnailsRoot => Directory.Exists(CoreManager.InstallDir)
        ? Path.Combine(CoreManager.InstallDir, "thumbnails")
        : Path.Combine(CoreManager.AppDir, "thumbnails");

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

                if (string.IsNullOrEmpty(key)) continue;

                // Extract platform from relative folder path
                string rel = Path.GetRelativePath(root, f);
                string? dir = Path.GetDirectoryName(rel);
                string platKey = "";
                if (!string.IsNullOrEmpty(dir))
                {
                    var segs = dir.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var seg in segs)
                    {
                        string k = CoreManager.NormalizePlatformKey(seg);
                        if (!string.IsNullOrEmpty(k) && k != "unknown")
                        {
                            platKey = k;
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(platKey))
                {
                    string compoundKey = $"{platKey}__{key}";
                    if (!dict.ContainsKey(compoundKey)) dict[compoundKey] = f;
                }

                if (!dict.ContainsKey(key))
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
            string cacheDir = Path.Combine(ThumbnailsRoot, safePlatform);
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

            // Region-isolated cache key
            string regKey = string.IsNullOrWhiteSpace(item.Region) ? "AUTO" : SanitizeFileName(item.Region.ToUpperInvariant());
            string cacheFile = Path.Combine(cacheDir, $"{safeName}__{regKey}.png");

            // 1. Check local regional cache (unless forceRefresh)
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

            // 2. Check cover next to ROM file in user directory
            if (!string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath))
            {
                try
                {
                    string? romDir = Path.GetDirectoryName(item.FilePath);
                    string romBase = Path.GetFileNameWithoutExtension(item.FilePath);
                    if (!string.IsNullOrEmpty(romDir) && Directory.Exists(romDir))
                    {
                        string[] exts = { ".png", ".jpg", ".jpeg", ".webp" };
                        foreach (var imgExt in exts)
                        {
                            string sidecar = Path.Combine(romDir, romBase + imgExt);
                            if (File.Exists(sidecar))
                            {
                                byte[] sidecarBytes = await File.ReadAllBytesAsync(sidecar).ConfigureAwait(false);
                                if (IsSupportedBitmapBytes(sidecarBytes) && TryApplyBytesToItemAndCache(item, sidecarBytes, cacheFile))
                                    return;
                            }
                        }

                        string[] genericNames = { "cover", "folder" };
                        foreach (var gen in genericNames)
                        {
                            foreach (var imgExt in exts)
                            {
                                string genSidecar = Path.Combine(romDir, gen + imgExt);
                                if (File.Exists(genSidecar))
                                {
                                    byte[] sidecarBytes = await File.ReadAllBytesAsync(genSidecar).ConfigureAwait(false);
                                    if (IsSupportedBitmapBytes(sidecarBytes) && TryApplyBytesToItemAndCache(item, sidecarBytes, cacheFile))
                                        return;
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // Fast reserve check for local StormMediaIndex if Region is empty or RU
            bool tryStormIndexFirst = string.IsNullOrEmpty(item.Region) || item.Region.Equals("RU", StringComparison.OrdinalIgnoreCase);
            if (tryStormIndexFirst)
            {
                string localCover = FindLocalStormCover(item.Name, item.Region, item.Platform);
                if (!string.IsNullOrEmpty(localCover) && File.Exists(localCover))
                {
                    byte[] localBytes = await File.ReadAllBytesAsync(localCover).ConfigureAwait(false);
                    if (IsSupportedBitmapBytes(localBytes) && TryApplyBytesToItemAndCache(item, localBytes, cacheFile))
                        return;
                }
            }

            // 3. Online Libretro Thumbnails Server (thumbnails.libretro.com)
            byte[]? downloadedBytes = await FetchLibretroThumbnailAsync(item).ConfigureAwait(false);
            if (downloadedBytes != null && IsSupportedBitmapBytes(downloadedBytes))
            {
                if (TryApplyBytesToItemAndCache(item, downloadedBytes, cacheFile)) return;
            }

            // 4. Reserve check for local StormMediaIndex if not tried first
            if (!tryStormIndexFirst)
            {
                string localCover = FindLocalStormCover(item.Name, item.Region, item.Platform);
                if (!string.IsNullOrEmpty(localCover) && File.Exists(localCover))
                {
                    byte[] localBytes = await File.ReadAllBytesAsync(localCover).ConfigureAwait(false);
                    if (IsSupportedBitmapBytes(localBytes) && TryApplyBytesToItemAndCache(item, localBytes, cacheFile))
                        return;
                }
            }

            // 5. Wikipedia REST API Fallback (with strict 'game' verification)
            byte[]? wikiBytes = await FetchWikipediaCoverAsync(item.Name).ConfigureAwait(false);
            if (wikiBytes != null && IsSupportedBitmapBytes(wikiBytes))
            {
                if (TryApplyBytesToItemAndCache(item, wikiBytes, cacheFile)) return;
            }

            // 6. Fallback to 280x380 studio poster
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
            var covers = MediaIndex?.Covers;
            if (covers == null) return "";

            string platKey = CoreManager.NormalizePlatformKey(platform);
            if (!string.IsNullOrEmpty(platKey))
            {
                string compoundKey = $"{platKey}__{key}";
                if (covers.TryGetValue(compoundKey, out var exactPath) && File.Exists(exactPath))
                {
                    return exactPath;
                }
            }

            if (covers.TryGetValue(key, out var path) && File.Exists(path))
            {
                return path;
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

    public static Task<byte[]?> FetchLibretroThumbnailAsync(string rawTitle, string region, string platform)
    {
        var dummy = new RomItem { Name = rawTitle, Region = region, Platform = platform };
        return FetchLibretroThumbnailAsync(dummy);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<List<string>>> _libretroDirIndexTasks = new(StringComparer.OrdinalIgnoreCase);

    private static Task<List<string>> GetLibretroDirectoryIndexAsync(string encSys, string folder)
    {
        string cacheKey = $"{encSys}/{folder}";
        return _libretroDirIndexTasks.GetOrAdd(cacheKey, async _ =>
        {
            var pngNames = new List<string>();
            try
            {
                string dirUrl = $"https://thumbnails.libretro.com/{encSys}/{folder}/";
                using var res = await _httpClient.GetAsync(dirUrl).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return pngNames;
                string html = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                foreach (Match m in Regex.Matches(html, @"href=""([^""]+\.png)""", RegexOptions.IgnoreCase))
                {
                    string rawHref = m.Groups[1].Value;
                    if (rawHref.Contains("/") || rawHref.StartsWith("?")) continue;
                    string decoded = Uri.UnescapeDataString(rawHref);
                    if (decoded.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        pngNames.Add(decoded.Substring(0, decoded.Length - 4));
                    }
                }
            }
            catch { }
            return pngNames;
        });
    }

    private static int ScoreServerRegionMatch(string serverEntryName, string preferredRegion)
    {
        string upper = serverEntryName.ToUpperInvariant();
        string reg = (preferredRegion ?? "").Trim().ToUpperInvariant();

        // Penalize mini/collection re-releases slightly compared to original boxarts
        int penalty = (upper.Contains("COLLECTION)") || upper.Contains("MINI)") || upper.Contains("VIRTUAL CONSOLE)") || upper.Contains("(BETA") || upper.Contains("(PROTO") || upper.Contains("(SAMPLE")) ? -15 : 0;

        if (reg is "JP" or "JPN" or "JAPAN")
        {
            if (upper.Contains("(JAPAN)") || upper.Contains("(JAPAN) (")) return 100 + penalty;
            if (upper.Contains("(JAPAN, USA)") || upper.Contains("(JAPAN, KOREA)") || upper.Contains("(JAPAN, EUROPE)")) return 90 + penalty;
            if (upper.Contains("(WORLD)") || upper.Contains("(ASIA)")) return 70 + penalty;
            if (upper.Contains("(USA)")) return 25 + penalty;
            if (upper.Contains("(EUROPE)")) return 20 + penalty;
            return 40 + penalty;
        }
        if (reg is "EU" or "EUR" or "EUROPE" or "PAL")
        {
            if (upper.Contains("(EUROPE)") || upper.Contains("(EUROPE) (")) return 100 + penalty;
            if (upper.Contains("(USA, EUROPE)") || upper.Contains("(JAPAN, EUROPE)") || upper.Contains("(UK)")) return 90 + penalty;
            if (upper.Contains("(GERMANY)") || upper.Contains("(FRANCE)") || upper.Contains("(SPAIN)") || upper.Contains("(ITALY)")) return 82 + penalty;
            if (upper.Contains("(WORLD)")) return 75 + penalty;
            if (upper.Contains("(USA)")) return 30 + penalty;
            if (upper.Contains("(JAPAN)")) return 15 + penalty;
            return 40 + penalty;
        }
        if (reg == "JP-US")
        {
            if (upper.Contains("(JAPAN, USA)")) return 100 + penalty;
            if (upper.Contains("(JAPAN)")) return 92 + penalty;
            if (upper.Contains("(USA)")) return 88 + penalty;
            if (upper.Contains("(WORLD)")) return 75 + penalty;
            return 35 + penalty;
        }
        if (reg == "US-EU")
        {
            if (upper.Contains("(USA, EUROPE)")) return 100 + penalty;
            if (upper.Contains("(USA)")) return 92 + penalty;
            if (upper.Contains("(EUROPE)")) return 88 + penalty;
            if (upper.Contains("(WORLD)")) return 75 + penalty;
            return 35 + penalty;
        }
        if (reg == "KR")
        {
            if (upper.Contains("(KOREA)")) return 100 + penalty;
            if (upper.Contains("(JAPAN, KOREA)") || upper.Contains("(ASIA)")) return 88 + penalty;
            if (upper.Contains("(WORLD)")) return 70 + penalty;
            return 30 + penalty;
        }

        // Default / US / WORLD
        if (upper.Contains("(USA)") || upper.Contains("(USA) (")) return 100 + penalty;
        if (upper.Contains("(USA, EUROPE)") || upper.Contains("(NORTH AMERICA)")) return 92 + penalty;
        if (upper.Contains("(WORLD)")) return 85 + penalty;
        if (upper.Contains("(EUROPE)")) return 45 + penalty;
        if (upper.Contains("(JAPAN)")) return 25 + penalty;
        return 60 + penalty;
    }

    private static List<string> MatchCandidatesInDirectoryIndex(List<string> indexEntries, List<string> exactCandidates, string rawTitle, string preferredRegion)
    {
        var matched = new List<string>();
        var indexSet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in indexEntries)
        {
            if (!indexSet.ContainsKey(e)) indexSet[e] = e;
        }

        // 1. Exact candidate match in strict regional priority order
        foreach (var cand in exactCandidates)
        {
            if (indexSet.TryGetValue(cand, out var actual) && !matched.Contains(actual))
            {
                matched.Add(actual);
            }
        }
        if (matched.Count > 0) return matched;

        // 2. No-Intro dual-title (~ alias) and regional subtitle matching (e.g. "Bare Knuckle II - Shitou e no Requiem ~ Streets of Rage II (Japan, Europe)")
        string targetNorm = NormalizeLookupKey(rawTitle);
        string targetArabic = NormalizeLookupKey(Regex.Replace(rawTitle, @"\bII\b", "2").Replace("III", "3").Replace("IV", "4"));
        if (string.IsNullOrEmpty(targetNorm)) return matched;

        var scored = new List<(string Entry, int Score)>();
        foreach (var entry in indexEntries)
        {
            int parenIdx = entry.IndexOf('(');
            string titlePart = (parenIdx > 0 ? entry.Substring(0, parenIdx) : entry).Trim();
            var tildeAliases = titlePart.Split('~', StringSplitOptions.RemoveEmptyEntries);

            int titleMatchBonus = 0;
            foreach (var alias in tildeAliases)
            {
                string aliasTrim = alias.Trim();
                string aliasNorm = NormalizeLookupKey(aliasTrim);
                string aliasArabic = NormalizeLookupKey(Regex.Replace(aliasTrim, @"\bII\b", "2").Replace("III", "3").Replace("IV", "4"));
                if (aliasNorm == targetNorm || (!string.IsNullOrEmpty(targetArabic) && aliasArabic == targetArabic))
                {
                    titleMatchBonus = 200;
                    break;
                }

                // Check primary title before " - " / "_ " subtitle inside a ~ alias (e.g. "Bare Knuckle II - Shitou e no Requiem")
                int sepIdx = aliasTrim.IndexOf(" - ", StringComparison.Ordinal);
                if (sepIdx < 0) sepIdx = aliasTrim.IndexOf("_ ", StringComparison.Ordinal);
                if (sepIdx > 0)
                {
                    string mainPart = aliasTrim.Substring(0, sepIdx).Trim();
                    string mainNorm = NormalizeLookupKey(mainPart);
                    string mainArabic = NormalizeLookupKey(Regex.Replace(mainPart, @"\bII\b", "2").Replace("III", "3").Replace("IV", "4"));
                    if (mainNorm == targetNorm || (!string.IsNullOrEmpty(targetArabic) && mainArabic == targetArabic))
                    {
                        titleMatchBonus = Math.Max(titleMatchBonus, tildeAliases.Length > 1 ? 185 : 140);
                    }
                }
            }

            if (titleMatchBonus > 0)
            {
                int totalScore = titleMatchBonus + ScoreServerRegionMatch(entry, preferredRegion);
                scored.Add((entry, totalScore));
            }
        }

        foreach (var s in scored.OrderByDescending(x => x.Score))
        {
            if (!matched.Contains(s.Entry)) matched.Add(s.Entry);
            if (matched.Count >= 4) break;
        }

        return matched;
    }

    public static async Task<byte[]?> FetchLibretroThumbnailAsync(RomItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Name)) return null;

        var sysDirs = GetLibretroDirs(item.Platform, item.Extension);
        if (sysDirs.Count == 0) return null;

        var candidates = BuildLibretroCandidates(item.Name, item.Region, item.Platform, item.RawNoIntroName);
        string[] folders = { "Named_Boxarts", "Named_Titles" };

        foreach (var sysDir in sysDirs)
        {
            string encSys = Uri.EscapeDataString(sysDir);
            foreach (var folder in folders)
            {
                var dirIndex = await GetLibretroDirectoryIndexAsync(encSys, folder).ConfigureAwait(false);
                if (dirIndex.Count > 0)
                {
                    var verifiedMatches = MatchCandidatesInDirectoryIndex(dirIndex, candidates, item.Name, item.Region);
                    foreach (var exactServerName in verifiedMatches)
                    {
                        string cdnUrl = $"https://thumbnails.libretro.com/{encSys}/{folder}/{Uri.EscapeDataString(exactServerName)}.png";
                        var data = await TryDownloadBufferWithSymlink(cdnUrl).ConfigureAwait(false);
                        if (data != null && IsSupportedBitmapBytes(data)) return data;
                    }
                }
                else
                {
                    // Fallback direct probe if directory index was unreachable
                    int limit = folder == "Named_Boxarts" ? Math.Min(candidates.Count, 8) : Math.Min(candidates.Count, 4);
                    for (int idx = 0; idx < limit; idx++)
                    {
                        string encCand = Uri.EscapeDataString(candidates[idx]);
                        string cdnUrl = $"https://thumbnails.libretro.com/{encSys}/{folder}/{encCand}.png";
                        var data = await TryDownloadBufferWithSymlink(cdnUrl).ConfigureAwait(false);
                        if (data != null && IsSupportedBitmapBytes(data)) return data;
                    }
                }
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
                req.Headers.UserAgent.ParseAdd("STORM-RETROARCH/2.5.0 (Game Media Engine; contact@stormgamesworld.ru)");
                using var resp = await _httpClient.SendAsync(req).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) continue;

                string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Strict validation: must contain 'game' in description or extract
                string desc = root.TryGetProperty("description", out var descProp) ? (descProp.GetString() ?? "") : "";
                string extract = root.TryGetProperty("extract", out var extProp) ? (extProp.GetString() ?? "") : "";
                string combined = (desc + " " + extract).ToLowerInvariant();
                if (!combined.Contains("game"))
                {
                    continue;
                }

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
                    imgReq.Headers.UserAgent.ParseAdd("STORM-RETROARCH/2.5.0 (Game Media Engine)");
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
        return BuildLibretroCandidates(rawTitle, preferredRegion, platform, "");
    }

    public static List<string> BuildLibretroCandidates(string rawTitle, string preferredRegion, string platform, string rawNoIntro)
    {
        string clean = Regex.Replace(rawTitle, @"\[[^\]]*\]|\([^)]*\)", "").Trim();
        clean = Regex.Replace(clean, @"\s+", " ").Trim();
        string colonNorm = Regex.Replace(clean, @"\s*:\s*", " - ").Trim();

        string regUpper = (preferredRegion ?? "").ToUpperInvariant();
        List<string> primarySuffixes;
        List<string> fallbackSuffixes;

        if (regUpper.Contains("JP-US") || regUpper.Contains("JU"))
        {
            primarySuffixes = new List<string> { "(Japan, USA)", "(Japan)", "(USA)", "(World)" };
            fallbackSuffixes = new List<string> { "(Europe)" };
        }
        else if (regUpper.Contains("US-EU") || regUpper.Contains("UE"))
        {
            primarySuffixes = new List<string> { "(USA, Europe)", "(USA)", "(Europe)", "(World)" };
            fallbackSuffixes = new List<string> { "(Japan)" };
        }
        else if (regUpper.Contains("JP") || regUpper.Contains("JAP"))
        {
            primarySuffixes = new List<string> { "(Japan)", "(Japan, USA)", "(Japan) (En)", "(Japan, Korea)", "(Asia)", "(World)" };
            fallbackSuffixes = new List<string> { "(USA)", "(Europe)" };
        }
        else if (regUpper.Contains("EU") || regUpper.Contains("PAL"))
        {
            primarySuffixes = new List<string> { "(Europe)", "(Europe) (En,Fr,De)", "(Europe) (En,Fr,De,Es,It)", "(Europe) (En,Fr,De,Es)", "(USA, Europe)", "(UK)", "(Germany)", "(France)", "(World)" };
            fallbackSuffixes = new List<string> { "(USA)", "(Japan)" };
        }
        else if (regUpper.Contains("KR"))
        {
            primarySuffixes = new List<string> { "(Korea)", "(Japan, Korea)", "(Asia)", "(World)", "(USA)" };
            fallbackSuffixes = new List<string> { "(Europe)", "(Japan)" };
        }
        else if (regUpper.Contains("RU") || regUpper.Contains("RUS"))
        {
            primarySuffixes = new List<string> { "(Russia)", "(Europe)", "(USA)", "(World)" };
            fallbackSuffixes = new List<string> { "(Japan)" };
        }
        else if (regUpper.Contains("WORLD") || regUpper.Contains("WW"))
        {
            primarySuffixes = new List<string> { "(World)", "(USA, Europe)", "(USA)", "(Europe)", "(Japan)" };
            fallbackSuffixes = new List<string>();
        }
        else // US or empty
        {
            primarySuffixes = new List<string> { "(USA)", "(USA, Europe)", "(World)", "(Europe)", "(Japan)" };
            fallbackSuffixes = new List<string>();
        }

        var baseTitles = new List<string>();
        void PushBase(string b)
        {
            string norm = Regex.Replace(b, @"\s*:\s*", " - ");
            norm = Regex.Replace(norm, @"[&*/:`<>?\|""]", "_");
            norm = Regex.Replace(norm, @"\s+", " ").Trim();
            if (!string.IsNullOrEmpty(norm) && !baseTitles.Contains(norm)) baseTitles.Add(norm);
        }

        // 1. If title contains " - ", generate "_ " variants
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

        // 3. Roman <-> Arabic swaps
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

        // 4. Known expansions
        if (colonNorm.Contains("Dungeon Master II", StringComparison.OrdinalIgnoreCase) &&
            !colonNorm.Contains("Skullkeep", StringComparison.OrdinalIgnoreCase))
        {
            PushBase("Dungeon Master II_ The Legend of Skullkeep");
            PushBase("Dungeon Master II - The Legend of Skullkeep");
        }

        var results = new List<string>();
        void PushResult(string r)
        {
            string norm = Regex.Replace(r, @"[&*/:`<>?\|""]", "_");
            norm = Regex.Replace(norm, @"\s+", " ").Trim();
            if (!string.IsNullOrEmpty(norm) && !results.Contains(norm)) results.Add(norm);
        }

        // Candidate #1: exact RawNoIntroName (standard No-Intro name without brackets)
        if (!string.IsNullOrWhiteSpace(rawNoIntro))
        {
            PushResult(rawNoIntro);
        }

        bool isDos = (platform ?? "").Contains("DOS", StringComparison.OrdinalIgnoreCase);
        if (isDos)
        {
            foreach (var b in baseTitles) PushResult(b);
            foreach (var suf in primarySuffixes.Take(2))
            {
                foreach (var b in baseTitles.Take(3)) PushResult($"{b} {suf}");
            }
        }
        else
        {
            foreach (var suf in primarySuffixes)
            {
                foreach (var b in baseTitles) PushResult($"{b} {suf}");
            }
            foreach (var b in baseTitles) PushResult(b);
            foreach (var suf in fallbackSuffixes)
            {
                foreach (var b in baseTitles) PushResult($"{b} {suf}");
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
        return MapPlatformToLibretroDir(platform, "");
    }

    public static string MapPlatformToLibretroDir(string platform, string ext)
    {
        var dirs = GetLibretroDirs(platform, ext);
        return dirs.Count > 0 ? dirs[0] : "Nintendo - Nintendo Entertainment System";
    }

    public static List<string> GetLibretroDirs(string platform, string ext = "")
    {
        var list = new List<string>();
        string p = (platform ?? "").Trim().ToUpperInvariant();
        string e = (ext ?? "").Trim().ToLowerInvariant();

        void Add(string d)
        {
            if (!string.IsNullOrEmpty(d) && !list.Contains(d)) list.Add(d);
        }

        // Nintendo
        if (p.Contains("SUPER NINTENDO") || p.Contains("SNES") || p.Contains("SUPER FAMICOM"))
            Add("Nintendo - Super Nintendo Entertainment System");
        else if (p.Contains("FAMILY COMPUTER DISK") || p.Contains("FAMICOM DISK") || p.Contains("FDS") || e == ".fds")
            Add("Nintendo - Family Computer Disk System");
        else if (p.Contains("NINTENDO 64") || p.Contains("N64"))
            Add("Nintendo - Nintendo 64");
        else if (p.Contains("GAME BOY ADVANCE") || p.Contains("GBA") || e == ".gba")
            Add("Nintendo - Game Boy Advance");
        else if (p.Contains("GAME BOY COLOR") || p.Contains("GBC") || e == ".gbc")
            Add("Nintendo - Game Boy Color");
        else if (p.Contains("GAME BOY / COLOR") || p.Contains("GAME BOY"))
        {
            if (e == ".gbc") { Add("Nintendo - Game Boy Color"); Add("Nintendo - Game Boy"); }
            else { Add("Nintendo - Game Boy"); Add("Nintendo - Game Boy Color"); }
        }
        else if (p.Contains("NINTENDO 3DS") || p.Contains("3DS"))
            Add("Nintendo - Nintendo 3DS");
        else if (p.Contains("NINTENDO DS") || p.Contains("NDS") || p.Contains("DSI"))
            Add("Nintendo - Nintendo DS");
        else if (p.Contains("VIRTUAL BOY") || p.Contains("VB"))
            Add("Nintendo - Virtual Boy");
        else if (p.Contains("POKEMON MINI") || p.Contains("POKÉMON MINI"))
            Add("Nintendo - Pokemon Mini");
        else if (p.Contains("WII U") || p.Contains("WIIU"))
            Add("Nintendo - Wii U");
        else if (p.Contains("GAMECUBE / WII") || (p.Contains("GAMECUBE") && p.Contains("WII")))
        {
            if (e is ".wbfs" or ".wad" or ".wua") { Add("Nintendo - Wii"); Add("Nintendo - GameCube"); }
            else { Add("Nintendo - GameCube"); Add("Nintendo - Wii"); }
        }
        else if (p.Contains("GAMECUBE"))
            Add("Nintendo - GameCube");
        else if (p.Contains("WII"))
            Add("Nintendo - Wii");
        else if (p.Contains("NES") || p.Contains("FAMICOM") || p.Contains("NINTENDO ENTERTAINMENT") || p.Contains("DENDY"))
            Add("Nintendo - Nintendo Entertainment System");

        // Sega
        if (p.Contains("MEGA-CD") || p.Contains("SEGA CD") || p.Contains("SEGACD"))
        {
            Add("Sega - Mega-CD - Sega CD");
            Add("Sega - Mega Drive - Genesis");
        }
        else if (p.Contains("SEGA 32X") || p.Contains("32X"))
        {
            Add("Sega - 32X");
            Add("Sega - Mega Drive - Genesis");
        }
        else if (p.Contains("MASTER SYSTEM / GAME GEAR") || (p.Contains("MASTER SYSTEM") && p.Contains("GAME GEAR")))
        {
            if (e == ".gg") { Add("Sega - Game Gear"); Add("Sega - Master System - Mark III"); }
            else { Add("Sega - Master System - Mark III"); Add("Sega - Game Gear"); }
        }
        else if (p.Contains("GAME GEAR") || e == ".gg")
            Add("Sega - Game Gear");
        else if (p.Contains("SG-1000") || p.Contains("SG1000"))
            Add("Sega - SG-1000");
        else if (p.Contains("MASTER SYSTEM") || p.Contains("SMS"))
            Add("Sega - Master System - Mark III");
        else if (p.Contains("GENESIS") || p.Contains("MEGA DRIVE") || p.Contains("MEGADRIVE"))
            Add("Sega - Mega Drive - Genesis");
        else if (p.Contains("SATURN"))
            Add("Sega - Saturn");
        else if (p.Contains("DREAMCAST"))
            Add("Sega - Dreamcast");

        // Sony
        if (p.Contains("PLAYSTATION VITA") || p.Contains("PS VITA") || p.Contains("PSVITA"))
            Add("Sony - PlayStation Vita");
        else if (p.Contains("PLAYSTATION PORTABLE") || p.Contains("PSP"))
            Add("Sony - PlayStation Portable");
        else if (p.Contains("PLAYSTATION 3") || p.Contains("PS3"))
            Add("Sony - PlayStation 3");
        else if (p.Contains("PLAYSTATION 2") || p.Contains("PS2"))
            Add("Sony - PlayStation 2");
        else if (p.Contains("PLAYSTATION") || p.Contains("PS1") || p.Contains("PSX"))
            Add("Sony - PlayStation");

        // NEC
        if (p.Contains("PC ENGINE CD") || p.Contains("TURBOGRAFX-CD") || p.Contains("TURBOGRAFX CD"))
            Add("NEC - PC Engine CD - TurboGrafx-CD");
        else if (p.Contains("SUPERGRAFX") || p.Contains("SUPER GRAFX"))
            Add("NEC - PC Engine SuperGrafx");
        else if (p.Contains("PC-FX") || p.Contains("PCFX"))
            Add("NEC - PC-FX");
        else if (p.Contains("PC ENGINE") || p.Contains("TURBOGRAFX") || p.Contains("TG-16") || p.Contains("TG16"))
            Add("NEC - PC Engine - TurboGrafx 16");

        // SNK & Bandai
        if (p.Contains("NEO GEO POCKET") || p.Contains("NGP") || p.Contains("NGPC"))
        {
            Add("SNK - Neo Geo Pocket Color");
            Add("SNK - Neo Geo Pocket");
        }
        else if (p.Contains("NEO GEO CD"))
            Add("SNK - Neo Geo CD");
        else if (p.Contains("NEO GEO") || p.Contains("NEOGEO"))
            Add("SNK - Neo Geo");

        if (p.Contains("WONDERSWAN") || p.Contains("WSWAN"))
        {
            Add("Bandai - WonderSwan Color");
            Add("Bandai - WonderSwan");
        }

        // Arcade
        if (p.Contains("ARCADE") || p.Contains("FBNEO") || p.Contains("MAME"))
        {
            Add("FBNeo - Arcade Games");
            Add("MAME");
        }

        // Computers
        if (p.Contains("DOS") || p.Contains("MS-DOS"))
            Add("DOS");
        if (p.Contains("SCUMMVM") || p.Contains("SCUMM"))
            Add("ScummVM");
        if (p.Contains("AMIGA"))
            Add("Commodore - Amiga");
        if (p.Contains("COMMODORE 64") || p.Contains("C64"))
            Add("Commodore - 64");
        if (p.Contains("ZX SPECTRUM") || p.Contains("SPECTRUM"))
            Add("Sinclair - ZX Spectrum");
        if (p.Contains("AMSTRAD") || p.Contains("CPC"))
            Add("Amstrad - CPC");
        if (p.Contains("MSX2"))
            Add("Microsoft - MSX2");
        else if (p.Contains("MSX"))
            Add("Microsoft - MSX");
        if (p.Contains("PC-98") || p.Contains("PC98"))
            Add("NEC - PC-98");
        if (p.Contains("X68000"))
            Add("Sharp - X68000");

        // Atari
        if (p.Contains("ATARI 2600") || p.Contains("2600"))
            Add("Atari - 2600");
        if (p.Contains("ATARI 5200") || p.Contains("5200"))
            Add("Atari - 5200");
        if (p.Contains("ATARI 7800") || p.Contains("7800"))
            Add("Atari - 7800");
        if (p.Contains("ATARI 800") || p.Contains("ATARI 8-BIT"))
            Add("Atari - 800");
        if (p.Contains("ATARI ST"))
            Add("Atari - ST");
        if (p.Contains("ATARI LYNX") || p.Contains("LYNX"))
            Add("Atari - Lynx");
        if (p.Contains("ATARI JAGUAR") || p.Contains("JAGUAR"))
            Add("Atari - Jaguar");

        // Other classic consoles
        if (p.Contains("3DO"))
            Add("The 3DO Company - 3DO");
        if (p.Contains("COLECOVISION") || p.Contains("COLECO"))
            Add("Coleco - ColecoVision");
        if (p.Contains("INTELLIVISION"))
            Add("Mattel - Intellivision");
        if (p.Contains("VECTREX"))
            Add("GCE - Vectrex");
        if (p.Contains("ODYSSEY"))
            Add("Magnavox - Odyssey2");
        if (p.Contains("CHANNEL F") || p.Contains("CHANNELF"))
            Add("Fairchild - Channel F");
        if (p.Contains("XBOX"))
            Add("Microsoft - Xbox");

        if (list.Count == 0)
        {
            Add("Nintendo - Nintendo Entertainment System");
        }

        return list;
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Unknown";
        string clean = Regex.Replace(name, @"[\\/:*?""<>|]", "_");
        return clean.Trim();
    }
}
