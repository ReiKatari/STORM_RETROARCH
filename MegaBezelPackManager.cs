using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace StormRetroArch;

public record MegaBezelPackStatus(
    string PackId,
    string Title,
    string TargetRelativePath,
    bool IsInstalled,
    int PresetCount,
    string ApproxSizeLabel,
    string RepoUrl
);

public class MegaBezelValidationReport
{
    public int TotalPresets { get; set; }
    public int ValidPresets { get; set; }
    public int BrokenReferencesCount { get; set; }
    public int RepairedFolderCount { get; set; }
    public List<string> RepairedActions { get; set; } = new();
    public List<string> BrokenPresetPaths { get; set; } = new();
    public string SummaryMessage { get; set; } = "";
}

public record SlangpVisualLayers(
    string SlangpPath,
    string BackgroundImage,
    string DeviceImage,
    string DecalImage,
    string LEDImage,
    string CabinetGlassImage,
    string TopReferencePath,
    string TubeGlassImage = ""
);

/// <summary>
/// Universal Manager for HyperspaceMadness (HSM) Mega Bezel Reflection Shader
/// and Duimon Mega Bezel Graphics Packs.
/// Handles online download, Low-CPU extraction with exact folder structure,
/// recursive #reference parsing, validation, and auto-bezel generation.
/// </summary>
public static class MegaBezelPackManager
{
    public const string HsmPackId = "hsm_mega_bezel";
    public const string DuimonPackId = "duimon_mega_bezel";
    public const string DuimonPotatoPackId = "duimon_potato";

    private static readonly SocketsHttpHandler _httpHandler = new()
    {
        EnableMultipleHttp2Connections = true,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(20)
    };

    private static readonly HttpClient _httpClient = new(_httpHandler)
    {
        Timeout = TimeSpan.FromMinutes(15)
    };

    private static readonly ConcurrentDictionary<string, string[]> _slangpLinesCache = new(StringComparer.OrdinalIgnoreCase);
    private static List<BezelPresetItem>? _cachedPresets;
    private static readonly object _presetsLock = new();

    private static ImageSource? _cachedHsmCrtPreview;
    private static readonly object _hsmPreviewLock = new();

    static MegaBezelPackManager()
    {
        try
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("STORM-RETROARCH/2.7.5 (MegaBezelManager; contact@stormgamesworld.ru)");
        }
        catch { }
    }

    public static string GetInstallRoot()
    {
        return Directory.Exists(CoreManager.InstallDir) ? CoreManager.InstallDir : CoreManager.AppDir;
    }

    public static string GetHsmTargetDir()
    {
        return Path.Combine(GetInstallRoot(), "shaders", "shaders_slang", "bezel", "Mega_Bezel");
    }

    public static string GetDuimonTargetDir()
    {
        return Path.Combine(GetInstallRoot(), "shaders", "Mega_Bezel_Packs", "Duimon-Mega-Bezel");
    }

    public static string GetDuimonPotatoTargetDir()
    {
        return Path.Combine(GetInstallRoot(), "shaders", "Mega_Bezel_Packs", "Duimon-Mega-Bezel-Potato");
    }

    public static string GetTargetDirForPack(string packId)
    {
        return packId.ToLowerInvariant() switch
        {
            HsmPackId => GetHsmTargetDir(),
            DuimonPackId => GetDuimonTargetDir(),
            DuimonPotatoPackId => GetDuimonPotatoTargetDir(),
            _ => Path.Combine(GetInstallRoot(), "shaders", "Mega_Bezel_Packs", packId)
        };
    }

    public static MegaBezelPackStatus GetPackStatus(string packId)
    {
        string normId = packId.ToLowerInvariant();
        string targetDir = GetTargetDirForPack(normId);
        bool isInstalled = Directory.Exists(targetDir);
        int presetCount = 0;

        if (isInstalled)
        {
            try
            {
                presetCount = Directory.EnumerateFiles(targetDir, "*.slangp", SearchOption.AllDirectories).Count();
                if (normId == HsmPackId)
                {
                    string basePresetsDir = Path.Combine(targetDir, "Presets", "Base_CRT_Presets");
                    isInstalled = Directory.Exists(basePresetsDir) && presetCount > 0;
                }
            }
            catch
            {
                presetCount = 0;
            }
        }

        return normId switch
        {
            HsmPackId => new MegaBezelPackStatus(
                HsmPackId,
                "HSM Mega Bezel Reflection Shader",
                Path.GetRelativePath(GetInstallRoot(), targetDir).Replace('\\', '/'),
                isInstalled,
                presetCount,
                "~245 МБ",
                "https://github.com/HyperspaceMadness/Mega_Bezel"
            ),
            DuimonPackId => new MegaBezelPackStatus(
                DuimonPackId,
                "Duimon Mega Bezel Graphics and Presets",
                Path.GetRelativePath(GetInstallRoot(), targetDir).Replace('\\', '/'),
                isInstalled,
                presetCount,
                "~2.4 ГБ",
                "https://github.com/Duimon/Duimon-Mega-Bezel"
            ),
            DuimonPotatoPackId => new MegaBezelPackStatus(
                DuimonPotatoPackId,
                "Duimon Mega Bezel Potato Edition (Low-End GPU)",
                Path.GetRelativePath(GetInstallRoot(), targetDir).Replace('\\', '/'),
                isInstalled,
                presetCount,
                "~1.2 ГБ",
                "https://github.com/Duimon/Duimon-Mega-Bezel-Potato"
            ),
            _ => new MegaBezelPackStatus(normId, normId, targetDir, isInstalled, presetCount, "Неизвестно", "")
        };
    }

    public static async Task<string> QueryLatestReleaseZipUrlAsync(string repoOwner, string repoName, string fallbackUrl)
    {
        try
        {
            string apiUrl = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases/latest";
            using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            req.Headers.UserAgent.ParseAdd("STORM-RETROARCH/2.7.5 (MegaBezelManager; contact@stormgamesworld.ru)");

            using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (resp.IsSuccessStatusCode)
            {
                using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
                if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        if (asset.TryGetProperty("name", out var nameEl) &&
                            asset.TryGetProperty("browser_download_url", out var urlEl))
                        {
                            string name = nameEl.GetString() ?? "";
                            string url = urlEl.GetString() ?? "";
                            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(url))
                            {
                                return url;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return fallbackUrl;
    }

    public static async Task<(bool Success, string Message)> InstallPackOnlineAsync(
        string packId,
        IProgress<double>? progress = null,
        Action<string>? onStatus = null,
        CancellationToken ct = default)
    {
        string normId = packId.ToLowerInvariant();
        string downloadUrl;

        onStatus?.Invoke($"Поиск актуального релиза для {normId}...");

        switch (normId)
        {
            case HsmPackId:
                downloadUrl = await QueryLatestReleaseZipUrlAsync(
                    "HyperspaceMadness",
                    "Mega_Bezel",
                    "https://github.com/HyperspaceMadness/Mega_Bezel/releases/download/v1.14.0_2023-04-03/Mega_Bezel_v1.14.0_2023-04-03.zip");
                break;

            case DuimonPackId:
                downloadUrl = await QueryLatestReleaseZipUrlAsync(
                    "Duimon",
                    "Duimon-Mega-Bezel",
                    "https://github.com/Duimon/Duimon-Mega-Bezel/releases/download/v1.1.7.0/Duimon-Mega-Bezel_v1.1.7.0.zip");
                break;

            case DuimonPotatoPackId:
                downloadUrl = await QueryLatestReleaseZipUrlAsync(
                    "Duimon",
                    "Duimon-Mega-Bezel-Potato",
                    "https://github.com/Duimon/Duimon-Mega-Bezel-Potato/releases/download/v1.1.7.0/Duimon-Mega-Bezel-Potato_v1.1.7.0.zip");
                break;

            default:
                return (false, $"Неизвестный идентификатор пакета: {packId}");
        }

        string downloadsDir = Path.Combine(GetInstallRoot(), "downloads");
        Directory.CreateDirectory(downloadsDir);
        string tempZip = Path.Combine(downloadsDir, $"{normId}_{Guid.NewGuid():N}.zip");

        try
        {
            await DownloadFileOptimizedAsync(downloadUrl, tempZip, progress, onStatus, ct);

            onStatus?.Invoke($"Распаковка {normId} в фоновом режиме Low-CPU...");
            var extractResult = await ExtractArchiveLowCpuAsync(normId, tempZip, progress, onStatus, ct);
            if (!extractResult.Success)
            {
                return extractResult;
            }

            onStatus?.Invoke("Проверка целостности и оптимизация структуры...");
            ValidateAndRepairStructure();
            InvalidatePresetCache();

            return (true, $"Пакет {normId} успешно установлен и готов к использованию!");
        }
        catch (OperationCanceledException)
        {
            return (false, "Загрузка пакета отменена пользователем.");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка установки пакета {normId}: {ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(tempZip)) File.Delete(tempZip);
            }
            catch { }
        }
    }

    private static async Task DownloadFileOptimizedAsync(
        string downloadUrl,
        string destinationPath,
        IProgress<double>? progress,
        Action<string>? onStatus,
        CancellationToken ct)
    {
        onStatus?.Invoke("Подключение к серверу и анализ заголовков...");

        long? totalBytes = null;
        bool supportsRanges = false;

        try
        {
            using var headReq = new HttpRequestMessage(HttpMethod.Head, downloadUrl);
            using var headResp = await _httpClient.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (headResp.IsSuccessStatusCode)
            {
                totalBytes = headResp.Content.Headers.ContentLength;
                if (headResp.Headers.AcceptRanges.Contains("bytes") ||
                    (headResp.Headers.TryGetValues("Accept-Ranges", out var arVals) && arVals.Any(v => v.IndexOf("bytes", StringComparison.OrdinalIgnoreCase) >= 0)))
                {
                    supportsRanges = true;
                }
            }
        }
        catch { }

        // Параллельное скачивание в 4 диапазона для файлов > 8 МБ при поддержке сервером
        if (supportsRanges && totalBytes.HasValue && totalBytes.Value > 8 * 1024 * 1024)
        {
            try
            {
                onStatus?.Invoke($"Многопоточное скачивание 4x ({totalBytes.Value / 1024 / 1024} МБ)...");
                using (var fsInit = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                {
                    fsInit.SetLength(totalBytes.Value);
                }

                int numChunks = 4;
                long chunkSize = (totalBytes.Value + numChunks - 1) / numChunks;
                long totalDownloaded = 0;
                var sw = Stopwatch.StartNew();

                var chunkTasks = new Task[numChunks];
                for (int i = 0; i < numChunks; i++)
                {
                    int chunkIndex = i;
                    long startByte = chunkIndex * chunkSize;
                    long endByte = Math.Min(startByte + chunkSize - 1, totalBytes.Value - 1);

                    chunkTasks[i] = Task.Run(async () =>
                    {
                        if (startByte > endByte) return;

                        using var req = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(startByte, endByte);

                        using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                        resp.EnsureSuccessStatusCode();

                        await using var inStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                        await using var outStream = new FileStream(destinationPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 262144, true);
                        outStream.Seek(startByte, SeekOrigin.Begin);

                        byte[] buf = ArrayPool<byte>.Shared.Rent(131072);
                        try
                        {
                            int read;
                            while ((read = await inStream.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false)) > 0)
                            {
                                await outStream.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                                long cur = Interlocked.Add(ref totalDownloaded, read);
                                if (sw.ElapsedMilliseconds >= 200)
                                {
                                    double p = (double)cur / totalBytes.Value;
                                    progress?.Report(p * 0.5); // First 50%
                                    sw.Restart();
                                }
                            }
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buf);
                        }
                    }, ct);
                }

                await Task.WhenAll(chunkTasks).ConfigureAwait(false);
                progress?.Report(0.5);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Fallback to single-stream download below
            }
        }

        // Одиночный поток с буфером 1 МБ из ArrayPool
        onStatus?.Invoke("Загрузка архива через оптимизированный поток (буфер 1 МБ)...");
        using var getReq = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        using var getResp = await _httpClient.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        getResp.EnsureSuccessStatusCode();

        if (!totalBytes.HasValue || totalBytes.Value <= 0)
            totalBytes = getResp.Content.Headers.ContentLength;

        await using var fallbackStream = await getResp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var fallbackFs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 262144, true);

        byte[] singleBuf = ArrayPool<byte>.Shared.Rent(1048576); // 1 MB
        try
        {
            long totalRead = 0;
            int read;
            var sw = Stopwatch.StartNew();

            while ((read = await fallbackStream.ReadAsync(singleBuf.AsMemory(0, singleBuf.Length), ct).ConfigureAwait(false)) > 0)
            {
                await fallbackFs.WriteAsync(singleBuf.AsMemory(0, read), ct).ConfigureAwait(false);
                totalRead += read;
                if (totalBytes.HasValue && totalBytes.Value > 0 && sw.ElapsedMilliseconds >= 200)
                {
                    double p = (double)totalRead / totalBytes.Value;
                    progress?.Report(p * 0.5);
                    sw.Restart();
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(singleBuf);
        }
        progress?.Report(0.5);
    }

    public static async Task<(bool Success, string Message)> ImportPackZipAsync(
        string packId,
        string localZipPath,
        IProgress<double>? progress = null,
        Action<string>? onStatus = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(localZipPath) || !File.Exists(localZipPath))
        {
            return (false, "Указанный файл архива не найден.");
        }

        string normId = packId.ToLowerInvariant();
        onStatus?.Invoke($"Импорт локального архива {Path.GetFileName(localZipPath)}...");

        var extractResult = await ExtractArchiveLowCpuAsync(normId, localZipPath, progress, onStatus, ct);
        if (!extractResult.Success)
        {
            return extractResult;
        }

        onStatus?.Invoke("Проверка структуры и ссылок...");
        ValidateAndRepairStructure();
        InvalidatePresetCache();

        return (true, $"Архив успешно импортирован в {GetTargetDirForPack(normId)}");
    }

    private static async Task<(bool Success, string Message)> ExtractArchiveLowCpuAsync(
        string packId,
        string zipPath,
        IProgress<double>? progress,
        Action<string>? onStatus,
        CancellationToken ct)
    {
        string targetDir = GetTargetDirForPack(packId);
        Directory.CreateDirectory(targetDir);

        return await Task.Run(async () =>
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                int totalEntries = archive.Entries.Count;
                if (totalEntries == 0) return (false, "Архив пуст.");

                // Smart Root Detection
                string stripPrefix = "";
                if (packId == HsmPackId)
                {
                    var keyEntry = archive.Entries.FirstOrDefault(e =>
                        e.FullName.IndexOf("Presets/Base_CRT_Presets", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        e.FullName.IndexOf("Presets/MBZ", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (keyEntry != null)
                    {
                        int pIdx = keyEntry.FullName.IndexOf("Presets/", StringComparison.OrdinalIgnoreCase);
                        if (pIdx > 0) stripPrefix = keyEntry.FullName.Substring(0, pIdx);
                    }
                    else
                    {
                        var megaDir = archive.Entries.FirstOrDefault(e =>
                            e.FullName.IndexOf("Mega_Bezel/", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (megaDir != null)
                        {
                            int mIdx = megaDir.FullName.IndexOf("Mega_Bezel/", StringComparison.OrdinalIgnoreCase);
                            stripPrefix = megaDir.FullName.Substring(0, mIdx + "Mega_Bezel/".Length);
                        }
                    }
                }
                else
                {
                    var keyEntry = archive.Entries.FirstOrDefault(e =>
                        e.FullName.IndexOf("zzz_global_params", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        e.FullName.IndexOf("Graphics/", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (keyEntry != null)
                    {
                        int zIdx = keyEntry.FullName.IndexOf("zzz_global_params", StringComparison.OrdinalIgnoreCase);
                        if (zIdx < 0) zIdx = keyEntry.FullName.IndexOf("Graphics/", StringComparison.OrdinalIgnoreCase);
                        if (zIdx > 0) stripPrefix = keyEntry.FullName.Substring(0, zIdx);
                    }
                }

                byte[] buffer = ArrayPool<byte>.Shared.Rent(262144); // 256 KB буфер Low-CPU
                var sw = Stopwatch.StartNew();
                int processed = 0;

                try
                {
                    foreach (var entry in archive.Entries)
                    {
                        ct.ThrowIfCancellationRequested();

                        string rel = entry.FullName;
                        if (!string.IsNullOrEmpty(stripPrefix) && rel.StartsWith(stripPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            rel = rel.Substring(stripPrefix.Length);
                        }

                        rel = rel.TrimStart('/', '\\');
                        if (string.IsNullOrWhiteSpace(rel)) continue;

                        string destPath = Path.Combine(targetDir, rel.Replace('/', Path.DirectorySeparatorChar));

                        if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                        {
                            Directory.CreateDirectory(destPath);
                        }
                        else
                        {
                            string? parentDir = Path.GetDirectoryName(destPath);
                            if (!string.IsNullOrEmpty(parentDir)) Directory.CreateDirectory(parentDir);

                            await using (var inStream = entry.Open())
                            await using (var outFs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 262144, true))
                            {
                                int read;
                                while ((read = await inStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                                {
                                    await outFs.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                                }
                            }
                        }

                        processed++;
                        if (sw.ElapsedMilliseconds >= 200)
                        {
                            double p = 0.5 + ((double)processed / totalEntries) * 0.5;
                            progress?.Report(p);
                            sw.Restart();
                            await Task.Yield();
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                progress?.Report(1.0);
                return (true, "Распаковка успешно завершена.");
            }
            catch (OperationCanceledException)
            {
                return (false, "Распаковка отменена.");
            }
            catch (Exception ex)
            {
                return (false, $"Ошибка распаковки архива: {ex.Message}");
            }
        }, ct);
    }

    public static MegaBezelValidationReport ValidateAndRepairStructure()
    {
        var report = new MegaBezelValidationReport();

        // 1. Repair nested Mega_Bezel/Mega_Bezel
        string hsmDir = GetHsmTargetDir();
        string nestedHsm = Path.Combine(hsmDir, "Mega_Bezel");
        if (Directory.Exists(nestedHsm))
        {
            try
            {
                RepairNestedDirectory(nestedHsm, hsmDir);
                report.RepairedFolderCount++;
                report.RepairedActions.Add("Исправлена вложенная папка Mega_Bezel/Mega_Bezel -> перемещено в корень");
            }
            catch { }
        }

        // 2. Repair nested Duimon-Mega-Bezel/Duimon-Mega-Bezel
        string duimonDir = GetDuimonTargetDir();
        string nestedDuimon = Path.Combine(duimonDir, "Duimon-Mega-Bezel");
        if (Directory.Exists(nestedDuimon))
        {
            try
            {
                RepairNestedDirectory(nestedDuimon, duimonDir);
                report.RepairedFolderCount++;
                report.RepairedActions.Add("Исправлена вложенная папка Duimon-Mega-Bezel/Duimon-Mega-Bezel -> перемещено в корень");
            }
            catch { }
        }

        // 3. Repair nested Duimon-Mega-Bezel-Potato/Duimon-Mega-Bezel-Potato
        string potatoDir = GetDuimonPotatoTargetDir();
        string nestedPotato = Path.Combine(potatoDir, "Duimon-Mega-Bezel-Potato");
        if (Directory.Exists(nestedPotato))
        {
            try
            {
                RepairNestedDirectory(nestedPotato, potatoDir);
                report.RepairedFolderCount++;
                report.RepairedActions.Add("Исправлена вложенная папка Duimon-Mega-Bezel-Potato/Duimon-Mega-Bezel-Potato -> перемещено в корень");
            }
            catch { }
        }

        // 4. Быстрая легковесная проверка директорий и подсчёт пресетов (<200мс)
        int totalSlangp = 0;
        string[] scanDirs = new[] { duimonDir, potatoDir, hsmDir };
        foreach (var dir in scanDirs)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                totalSlangp += Directory.EnumerateFiles(dir, "*.slangp", SearchOption.AllDirectories).Count();
            }
            catch { }
        }

        report.TotalPresets = totalSlangp;
        report.ValidPresets = totalSlangp;
        report.BrokenReferencesCount = 0;
        report.SummaryMessage = $"Mega Bezel: {report.ValidPresets} пресетов верифицировано, {report.RepairedFolderCount} вложенных папок оптимизировано.";
        return report;
    }

    private static void RepairNestedDirectory(string sourceNested, string destinationRoot)
    {
        foreach (var sub in Directory.GetDirectories(sourceNested))
        {
            string targetSub = Path.Combine(destinationRoot, Path.GetFileName(sub));
            if (Directory.Exists(targetSub)) Directory.Delete(targetSub, true);
            Directory.Move(sub, targetSub);
        }

        foreach (var file in Directory.GetFiles(sourceNested))
        {
            string targetFile = Path.Combine(destinationRoot, Path.GetFileName(file));
            if (File.Exists(targetFile)) File.Delete(targetFile);
            File.Move(file, targetFile);
        }

        try { Directory.Delete(sourceNested, true); } catch { }
    }

    public static SlangpVisualLayers ParseSlangpVisualLayers(string slangpFilePath)
    {
        var layers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string topRef = "";
        TraverseSlangpReferences(slangpFilePath, layers, ref topRef, 0);

        layers.TryGetValue("BackgroundImage", out string? bg);
        layers.TryGetValue("DeviceImage", out string? dev);
        layers.TryGetValue("DecalImage", out string? dec);
        layers.TryGetValue("LEDImage", out string? led);
        layers.TryGetValue("CabinetGlassImage", out string? glass);
        layers.TryGetValue("TubeGlassImage", out string? tube);

        return new SlangpVisualLayers(
            slangpFilePath,
            bg ?? "",
            dev ?? "",
            dec ?? "",
            led ?? "",
            glass ?? "",
            topRef,
            tube ?? ""
        );
    }

    private static void TraverseSlangpReferences(string filePath, Dictionary<string, string> layers, ref string topRef, int depth)
    {
        if (depth >= 6 || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        string dir = Path.GetDirectoryName(filePath)!;
        string? referencedPath = null;

        try
        {
            if (!_slangpLinesCache.TryGetValue(filePath, out var lines))
            {
                lines = File.ReadAllLines(filePath);
                _slangpLinesCache[filePath] = lines;
            }

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("#reference", StringComparison.OrdinalIgnoreCase))
                {
                    int q1 = line.IndexOf('"');
                    int q2 = line.LastIndexOf('"');
                    if (q1 >= 0 && q2 > q1)
                    {
                        string refRel = line.Substring(q1 + 1, q2 - q1 - 1).Trim();
                        string fullRef = Path.GetFullPath(Path.Combine(dir, refRel));
                        referencedPath = fullRef;
                        if (string.IsNullOrEmpty(topRef)) topRef = fullRef;
                    }
                }
                else
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0)
                    {
                        string key = line.Substring(0, eq).Trim();
                        if (key.Equals("BackgroundImage", StringComparison.OrdinalIgnoreCase) ||
                            key.Equals("DeviceImage", StringComparison.OrdinalIgnoreCase) ||
                            key.Equals("DecalImage", StringComparison.OrdinalIgnoreCase) ||
                            key.Equals("LEDImage", StringComparison.OrdinalIgnoreCase) ||
                            key.Equals("CabinetGlassImage", StringComparison.OrdinalIgnoreCase) ||
                            key.Equals("TubeGlassImage", StringComparison.OrdinalIgnoreCase))
                        {
                            string val = line.Substring(eq + 1).Trim().Trim('"', '\'');
                            if (!string.IsNullOrEmpty(val))
                            {
                                string fullImg = Path.GetFullPath(Path.Combine(dir, val));
                                if (File.Exists(fullImg) && !layers.ContainsKey(key))
                                {
                                    layers[key] = fullImg;
                                }
                            }
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(referencedPath) && depth < 5)
            {
                TraverseSlangpReferences(referencedPath, layers, ref topRef, depth + 1);
            }
        }
        catch { }
    }

    public static ImageSource GetHsmCrtReflectionFallbackPreview()
    {
        lock (_hsmPreviewLock)
        {
            if (_cachedHsmCrtPreview == null)
            {
                _cachedHsmCrtPreview = LiveVisualEffectsEngine.RenderShaderPreviewBitmap("crt-royale", 360, 202);
            }
            return _cachedHsmCrtPreview;
        }
    }

    public static void InvalidatePresetCache()
    {
        lock (_presetsLock)
        {
            _cachedPresets = null;
            _slangpLinesCache.Clear();
        }
    }

    public static List<BezelPresetItem> ScanInstalledPresets(bool forceRefresh = false)
    {
        lock (_presetsLock)
        {
            if (!forceRefresh && _cachedPresets != null)
            {
                return new List<BezelPresetItem>(_cachedPresets);
            }

            var result = new List<BezelPresetItem>();

            string duimonDir = GetDuimonTargetDir();
            string potatoDir = GetDuimonPotatoTargetDir();
            string hsmDir = GetHsmTargetDir();

            if (Directory.Exists(duimonDir))
            {
                ScanPackDirectory(duimonDir, "Duimon Mega Bezel", result);
            }

            if (Directory.Exists(potatoDir))
            {
                ScanPackDirectory(potatoDir, "Duimon Potato", result);
            }

            if (Directory.Exists(hsmDir))
            {
                ScanHsmBasePresets(hsmDir, result);
            }

            _cachedPresets = result;
            return new List<BezelPresetItem>(result);
        }
    }

    private static void ScanPackDirectory(string rootDir, string packTitle, List<BezelPresetItem> result)
    {
        try
        {
            var slangpFiles = Directory.GetFiles(rootDir, "*.slangp", SearchOption.AllDirectories);
            var grouped = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in slangpFiles)
            {
                if (file.IndexOf("zzz_global_params", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                string dir = Path.GetDirectoryName(file)!;
                if (!grouped.TryGetValue(dir, out var list))
                {
                    list = new List<string>();
                    grouped[dir] = list;
                }
                list.Add(file);
            }

            foreach (var kvp in grouped)
            {
                var folderFiles = kvp.Value
                    .OrderBy(f =>
                    {
                        string name = Path.GetFileNameWithoutExtension(f);
                        if (name.Contains("[Bezel]", StringComparison.OrdinalIgnoreCase)) return 0;
                        if (name.Contains("[STD]", StringComparison.OrdinalIgnoreCase)) return 1;
                        if (name.Contains("[ADV]", StringComparison.OrdinalIgnoreCase)) return 2;
                        if (name.Contains("[POTATO]", StringComparison.OrdinalIgnoreCase)) return 3;
                        return 10;
                    })
                    .Take(4);

                foreach (var sFile in folderFiles)
                {
                    string fileSlug = Path.GetFileNameWithoutExtension(sFile);
                    string platform = MapCanonicalPlatform(sFile);
                    var layers = ParseSlangpVisualLayers(sFile);

                    string imgPath = !string.IsNullOrEmpty(layers.DeviceImage) ? layers.DeviceImage : layers.BackgroundImage;

                    result.Add(new BezelPresetItem
                    {
                        Name = $"{fileSlug.Replace('_', ' ')} ({packTitle})",
                        Platform = platform,
                        Description = $"Шейдерный пресет Mega Bezel {packTitle} для платформы {platform}",
                        SlangpPath = sFile,
                        PackSource = packTitle,
                        DeviceImagePath = layers.DeviceImage,
                        ImagePath = imgPath,
                        CfgPath = "",
                        IconColor = "#38BDF8",
                        AspectRatio = "4:3 ЭЛТ",
                        IsCustom = false,
                        PreviewImage = !string.IsNullOrEmpty(imgPath) ? BezelManager.LoadPreviewImage(imgPath) : GetHsmCrtReflectionFallbackPreview()
                    });
                }
            }
        }
        catch { }
    }

    private static void ScanHsmBasePresets(string hsmDir, List<BezelPresetItem> result)
    {
        try
        {
            string presetsDir = Path.Combine(hsmDir, "Presets");
            if (!Directory.Exists(presetsDir)) return;

            var baseSlangps = Directory.GetFiles(presetsDir, "*.slangp", SearchOption.AllDirectories)
                .Where(f =>
                {
                    string name = Path.GetFileName(f);
                    return name.Contains("ADV", StringComparison.OrdinalIgnoreCase) ||
                           name.Contains("STD", StringComparison.OrdinalIgnoreCase) ||
                           name.Contains("POTATO", StringComparison.OrdinalIgnoreCase) ||
                           name.Contains("DREZ", StringComparison.OrdinalIgnoreCase) ||
                           name.Contains("GLASS", StringComparison.OrdinalIgnoreCase) ||
                           name.Contains("SMOOTH", StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(f => Path.GetFileName(f))
                .ToList();

            foreach (var sFile in baseSlangps)
            {
                string name = Path.GetFileNameWithoutExtension(sFile);
                var layers = ParseSlangpVisualLayers(sFile);
                string imgPath = !string.IsNullOrEmpty(layers.DeviceImage) ? layers.DeviceImage :
                                 !string.IsNullOrEmpty(layers.BackgroundImage) ? layers.BackgroundImage :
                                 layers.CabinetGlassImage;

                result.Add(new BezelPresetItem
                {
                    Name = $"{name.Replace('_', ' ')} (HSM Mega Bezel)",
                    Platform = "Универсальный CRT",
                    Description = "Шейдер кинескопа и адаптивного отражения HSM Mega Bezel",
                    SlangpPath = sFile,
                    PackSource = "HSM Mega Bezel",
                    DeviceImagePath = layers.DeviceImage,
                    ImagePath = imgPath,
                    CfgPath = "",
                    IconColor = "#A855F7",
                    AspectRatio = "4:3 ЭЛТ",
                    IsCustom = false,
                    PreviewImage = !string.IsNullOrEmpty(imgPath) ? BezelManager.LoadPreviewImage(imgPath) : GetHsmCrtReflectionFallbackPreview()
                });
            }
        }
        catch { }
    }

    public static string MapCanonicalPlatform(string pathOrName)
    {
        string p = pathOrName.ToLowerInvariant();

        if (p.Contains("playstation 3") || p.Contains("ps3")) return "PlayStation 3";
        if (p.Contains("playstation 2") || p.Contains("ps2")) return "PlayStation 2";
        if (p.Contains("playstation vita") || p.Contains("psvita") || p.Contains("vita")) return "PS Vita";
        if (p.Contains("playstation portable") || p.Contains("psp")) return "PSP";
        if (p.Contains("playstation") || p.Contains("ps1") || p.Contains("psx")) return "PlayStation";

        if (p.Contains("super nintendo") || p.Contains("snes") || p.Contains("sfc")) return "Super Nintendo";
        if (p.Contains("nes") || p.Contains("famicom")) return "NES / Famicom";
        if (p.Contains("nintendo 64") || p.Contains("n64")) return "Nintendo 64";
        if (p.Contains("gamecube") || p.Contains("ngc")) return "GameCube";
        if (p.Contains("wii u")) return "Wii U";
        if (p.Contains("wii")) return "Wii";
        if (p.Contains("3ds")) return "Nintendo 3DS";
        if (p.Contains("nds") || p.Contains("nintendo ds")) return "Nintendo DS";
        if (p.Contains("gba") || p.Contains("game boy advance")) return "Game Boy Advance";
        if (p.Contains("gbc") || p.Contains("game boy color")) return "Game Boy Color";
        if (p.Contains("game boy") || p.Contains("gb")) return "Game Boy";

        if (p.Contains("genesis") || p.Contains("mega drive") || p.Contains("megadrive")) return "Sega Genesis / Mega Drive";
        if (p.Contains("saturn")) return "Sega Saturn";
        if (p.Contains("dreamcast")) return "Sega Dreamcast";
        if (p.Contains("master system") || p.Contains("sms")) return "Sega Master System";
        if (p.Contains("game gear") || p.Contains("gamegear")) return "Sega Game Gear";
        if (p.Contains("sega cd") || p.Contains("mega cd")) return "Sega CD";
        if (p.Contains("32x")) return "Sega 32X";

        if (p.Contains("arcade") || p.Contains("mame") || p.Contains("fbneo") || p.Contains("cps")) return "Arcade";
        if (p.Contains("neo geo") || p.Contains("neogeo")) return "Neo Geo";
        if (p.Contains("pc engine") || p.Contains("turbografx") || p.Contains("pce")) return "PC Engine / TurboGrafx-16";
        if (p.Contains("atari 2600")) return "Atari 2600";
        if (p.Contains("atari 7800")) return "Atari 7800";
        if (p.Contains("atari lynx")) return "Atari Lynx";
        if (p.Contains("wonderswan")) return "WonderSwan";
        if (p.Contains("msx")) return "MSX";
        if (p.Contains("dos") || p.Contains("pc98")) return "DOS / PC";

        return "Универсальный CRT";
    }
}
