using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
    string TopReferencePath
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

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(15)
    };

    static MegaBezelPackManager()
    {
        try
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("STORM-RETROARCH/2.3.0 (MegaBezelManager; contact@stormgamesworld.ru)");
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

        bool dirExists = Directory.Exists(targetDir);
        int presetCount = 0;
        if (dirExists)
        {
            try
            {
                presetCount = Directory.GetFiles(targetDir, "*.slangp", SearchOption.AllDirectories).Length;
            }
            catch { }
        }

        bool isInstalled = dirExists && presetCount > 0;

        return normId switch
        {
            HsmPackId => new MegaBezelPackStatus(
                HsmPackId,
                "HSM Mega Bezel Reflection Shader",
                @"shaders\shaders_slang\bezel\Mega_Bezel",
                isInstalled,
                presetCount,
                "14.7 МБ",
                "https://github.com/HyperspaceMadness/Mega_Bezel"),

            DuimonPackId => new MegaBezelPackStatus(
                DuimonPackId,
                "Duimon Mega Bezel Complete 4K",
                @"shaders\Mega_Bezel_Packs\Duimon-Mega-Bezel",
                isInstalled,
                presetCount,
                "1.2 ГБ",
                "https://github.com/Duimon/Duimon-Mega-Bezel"),

            DuimonPotatoPackId => new MegaBezelPackStatus(
                DuimonPotatoPackId,
                "Duimon Mega Bezel Potato 1080p",
                @"shaders\Mega_Bezel_Packs\Duimon-Mega-Bezel-Potato",
                isInstalled,
                presetCount,
                "240 МБ",
                "https://github.com/Duimon/Duimon-Mega-Bezel-Potato"),

            _ => new MegaBezelPackStatus(
                packId,
                $"Mega Bezel Pack ({packId})",
                Path.GetRelativePath(GetInstallRoot(), targetDir),
                isInstalled,
                presetCount,
                "-",
                "")
        };
    }

    public static async Task<string> QueryLatestReleaseZipUrlAsync(string repoOwner, string repoName, string fallbackUrl)
    {
        try
        {
            string apiUrl = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases/latest";
            using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (resp.IsSuccessStatusCode)
            {
                string json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        if (asset.TryGetProperty("name", out var nameProp) &&
                            asset.TryGetProperty("browser_download_url", out var urlProp))
                        {
                            string assetName = nameProp.GetString() ?? "";
                            if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                return urlProp.GetString() ?? fallbackUrl;
                            }
                        }
                    }
                }

                if (doc.RootElement.TryGetProperty("zipball_url", out var zipball))
                {
                    string? zUrl = zipball.GetString();
                    if (!string.IsNullOrEmpty(zUrl)) return zUrl;
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

        // Auto-install dependency: Duimon requires HSM Mega Bezel base reflection shader
        if (normId == DuimonPackId || normId == DuimonPotatoPackId)
        {
            var hsmStatus = GetPackStatus(HsmPackId);
            if (!hsmStatus.IsInstalled)
            {
                onStatus?.Invoke("Установка обязательного базового пакета HSM Mega Bezel Reflection Shader (14.7 МБ)...");
                var hsmResult = await InstallPackOnlineAsync(HsmPackId, progress, onStatus, ct);
                if (!hsmResult.Success)
                {
                    return (false, $"Не удалось установить базовый пакет HSM Mega Bezel: {hsmResult.Message}");
                }
            }
        }

        string downloadUrl;
        switch (normId)
        {
            case HsmPackId:
                downloadUrl = await QueryLatestReleaseZipUrlAsync(
                    "HyperspaceMadness", "Mega_Bezel",
                    "https://github.com/HyperspaceMadness/Mega_Bezel/releases/download/Mega_Bezel_V1.17.1_2024-04-14/Mega_Bezel_V1.17.1_2024-04-14.zip");
                break;

            case DuimonPackId:
                downloadUrl = await QueryLatestReleaseZipUrlAsync(
                    "Duimon", "Duimon-Mega-Bezel",
                    "https://github.com/Duimon/Duimon-Mega-Bezel/releases/download/v1.1.7.0/Duimon-Mega-Bezel_v1.1.7.0.zip");
                break;

            case DuimonPotatoPackId:
                downloadUrl = await QueryLatestReleaseZipUrlAsync(
                    "Duimon", "Duimon-Mega-Bezel-Potato",
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
            onStatus?.Invoke($"Загрузка архива {normId}...");
            using (var resp = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                long? totalBytes = resp.Content.Headers.ContentLength;

                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                await using var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true);

                byte[] buffer = new byte[65536];
                long totalRead = 0;
                int read;

                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, read, ct);
                    totalRead += read;
                    if (totalBytes.HasValue && totalBytes.Value > 0)
                    {
                        double p = (double)totalRead / totalBytes.Value;
                        progress?.Report(p * 0.5); // First 50% for download
                    }
                }
            }

            onStatus?.Invoke($"Распаковка {normId} в фоновом режиме Low-CPU...");
            var extractResult = await ExtractArchiveLowCpuAsync(normId, tempZip, progress, onStatus, ct);
            if (!extractResult.Success)
            {
                return extractResult;
            }

            onStatus?.Invoke("Проверка целостности и исправление структуры...");
            ValidateAndRepairStructure();

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
                    // Look for entry containing Presets/Base_CRT_Presets/
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
                    // Duimon or Potato: look for zzz_global_params/ or Graphics/ or Presets/
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

                int processed = 0;
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
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                        entry.ExtractToFile(destPath, overwrite: true);
                    }

                    processed++;
                    if (processed % 64 == 0)
                    {
                        double p = 0.5 + ((double)processed / totalEntries) * 0.5;
                        progress?.Report(p);
                        await Task.Yield();
                    }
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

        // 4. Validate #reference links across all installed packs
        var allSlangp = new List<string>();
        if (Directory.Exists(duimonDir))
            allSlangp.AddRange(Directory.GetFiles(duimonDir, "*.slangp", SearchOption.AllDirectories));
        if (Directory.Exists(potatoDir))
            allSlangp.AddRange(Directory.GetFiles(potatoDir, "*.slangp", SearchOption.AllDirectories));
        if (Directory.Exists(hsmDir))
            allSlangp.AddRange(Directory.GetFiles(hsmDir, "*.slangp", SearchOption.AllDirectories));

        report.TotalPresets = allSlangp.Count;

        foreach (var file in allSlangp)
        {
            bool broken = false;
            try
            {
                string dir = Path.GetDirectoryName(file)!;
                foreach (var line in File.ReadLines(file))
                {
                    string t = line.Trim();
                    if (t.StartsWith("#reference", StringComparison.OrdinalIgnoreCase))
                    {
                        int q1 = t.IndexOf('"');
                        int q2 = t.LastIndexOf('"');
                        if (q1 >= 0 && q2 > q1)
                        {
                            string refRel = t.Substring(q1 + 1, q2 - q1 - 1).Trim();
                            string fullRef = Path.GetFullPath(Path.Combine(dir, refRel));
                            if (!File.Exists(fullRef))
                            {
                                broken = true;
                                break;
                            }
                        }
                    }
                }
            }
            catch { }

            if (broken)
            {
                report.BrokenReferencesCount++;
                report.BrokenPresetPaths.Add(file);
            }
            else
            {
                report.ValidPresets++;
            }
        }

        report.SummaryMessage = $"Mega Bezel: {report.ValidPresets} пресетов валидны, {report.BrokenReferencesCount} битых ссылок, {report.RepairedFolderCount} вложенных папок исправлено.";
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

        return new SlangpVisualLayers(
            slangpFilePath,
            bg ?? "",
            dev ?? "",
            dec ?? "",
            led ?? "",
            glass ?? "",
            topRef
        );
    }

    private static void TraverseSlangpReferences(string filePath, Dictionary<string, string> layers, ref string topRef, int depth)
    {
        if (depth >= 6 || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        string dir = Path.GetDirectoryName(filePath)!;
        string? referencedPath = null;

        try
        {
            foreach (var rawLine in File.ReadLines(filePath))
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
                            key.Equals("CabinetGlassImage", StringComparison.OrdinalIgnoreCase))
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
        }
        catch { }

        if (!string.IsNullOrEmpty(referencedPath) && File.Exists(referencedPath))
        {
            TraverseSlangpReferences(referencedPath, layers, ref topRef, depth + 1);
        }
    }

    public static List<BezelPresetItem> ScanInstalledPresets()
    {
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

        return result;
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

            // Cap per folder smartly to prioritize [Bezel], [STD], [ADV], [POTATO] presets
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
                        PreviewImage = !string.IsNullOrEmpty(imgPath) ? BezelManager.LoadPreviewImage(imgPath) : null
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
                           name.Contains("POTATO", StringComparison.OrdinalIgnoreCase);
                })
                .Take(12);

            foreach (var sFile in baseSlangps)
            {
                string name = Path.GetFileNameWithoutExtension(sFile);
                result.Add(new BezelPresetItem
                {
                    Name = $"{name} (HSM Mega Bezel)",
                    Platform = "Универсальный CRT",
                    Description = "Базовый шейдер отражения кинескопа HSM Mega Bezel",
                    SlangpPath = sFile,
                    PackSource = "HSM Mega Bezel",
                    IconColor = "#A855F7",
                    AspectRatio = "4:3 ЭЛТ",
                    IsCustom = false
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

        if (p.Contains("mame") || p.Contains("arcade") || p.Contains("fbneo")) return "Arcade";
        if (p.Contains("neo geo pocket") || p.Contains("ngpc")) return "Neo Geo Pocket";
        if (p.Contains("neo geo") || p.Contains("neogeo") || p.Contains("mvs") || p.Contains("aes")) return "SNK Neo Geo";

        if (p.Contains("dos") || p.Contains("ms-dos") || p.Contains("pc-dos")) return "MS-DOS";
        if (p.Contains("xbox 360")) return "Xbox 360";
        if (p.Contains("xbox")) return "Xbox";

        if (p.Contains("amiga")) return "Commodore Amiga";
        if (p.Contains("c64") || p.Contains("commodore 64")) return "Commodore 64";
        if (p.Contains("pc engine") || p.Contains("turbografx") || p.Contains("pce")) return "PC Engine / TurboGrafx-16";
        if (p.Contains("atari 2600")) return "Atari 2600";
        if (p.Contains("atari 7800")) return "Atari 7800";
        if (p.Contains("lynx")) return "Atari Lynx";
        if (p.Contains("jaguar")) return "Atari Jaguar";
        if (p.Contains("wonderswan")) return "WonderSwan";
        if (p.Contains("3do")) return "Panasonic 3DO";

        return "Универсальный";
    }
}
