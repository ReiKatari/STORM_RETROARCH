using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace StormRetroArch;

public static class UpdateManager
{
    public static Version NormalizeVersion(Version? v)
    {
        if (v == null) return new Version(1, 0, 0);
        return new Version(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build));
    }

    public static Version CurrentVersion
    {
        get
        {
            var v = typeof(UpdateManager).Assembly.GetName().Version;
            return v != null ? NormalizeVersion(v) : new Version(1, 0, 0);
        }
    }

    public static string CurrentVersionString
    {
        get
        {
            var v = CurrentVersion;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public const string GitHubRepo = "ReiKatari/STORM_RETROARCH";
    public const string GitHubReleasesWebUrl = "https://github.com/ReiKatari/STORM_RETROARCH/releases";

    public const string MirrorRepoSlug = "STORM_RETROARCH";
    public const string MirrorWebUrl = "https://stormgamesworld.ru/programs?repo=STORM_RETROARCH";
    public const string MirrorApiUrl = "https://stormgamesworld.ru/api/programs?repo=STORM_RETROARCH";
    public const string MirrorUploadsBaseUrl = "https://stormgamesworld.ru/uploads/programs/STORM_RETROARCH";

    public static async Task CheckForUpdatesAsync(bool silent = true)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(7) };
            client.DefaultRequestHeaders.Add("User-Agent", "STORM-RETROARCH");
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            var ghTask = CheckGitHubLatestAsync(client);
            var mirrorTask = CheckStormMirrorLatestAsync(client);
            await Task.WhenAll(ghTask, mirrorTask);

            UpdateInfo? ghInfo = await ghTask;
            UpdateInfo? mirrorInfo = await mirrorTask;

            if (ghInfo == null && mirrorInfo == null)
            {
                if (!silent) ShowError();
                return;
            }

            UpdateInfo bestInfo;
            if (ghInfo != null && mirrorInfo != null)
            {
                Version ghVer = NormalizeVersion(ghInfo.Version);
                Version mirVer = NormalizeVersion(mirrorInfo.Version);
                if (mirVer > ghVer)
                {
                    bestInfo = mirrorInfo;
                    if (!string.IsNullOrWhiteSpace(ghInfo.SetupDownloadUrl))
                        bestInfo.MirrorSetupDownloadUrl = ghInfo.SetupDownloadUrl;
                }
                else if (ghVer > mirVer)
                {
                    bestInfo = ghInfo;
                    if (!string.IsNullOrWhiteSpace(mirrorInfo.SetupDownloadUrl))
                        bestInfo.MirrorSetupDownloadUrl = mirrorInfo.SetupDownloadUrl;
                }
                else
                {
                    bestInfo = ghInfo;
                    bestInfo.SourceName = "GitHub + STORM GAMES WORLD";
                    if (!string.IsNullOrWhiteSpace(mirrorInfo.SetupDownloadUrl))
                        bestInfo.MirrorSetupDownloadUrl = mirrorInfo.SetupDownloadUrl;
                    if (string.IsNullOrWhiteSpace(bestInfo.SetupDownloadUrl))
                        bestInfo.SetupDownloadUrl = mirrorInfo.SetupDownloadUrl;
                    if (string.IsNullOrWhiteSpace(bestInfo.Changelog))
                        bestInfo.Changelog = mirrorInfo.Changelog;
                }
            }
            else
            {
                bestInfo = mirrorInfo ?? ghInfo!;
            }

            if (string.IsNullOrWhiteSpace(bestInfo.MirrorSetupDownloadUrl))
            {
                string fallbackFile = !string.IsNullOrWhiteSpace(bestInfo.SetupFileName)
                    ? bestInfo.SetupFileName
                    : $"STORM_RETROARCH_{bestInfo.VersionString}_Setup.exe";
                bestInfo.MirrorSetupDownloadUrl = $"{MirrorUploadsBaseUrl}/{fallbackFile}";
            }

            Version remoteNorm = NormalizeVersion(bestInfo.Version);
            Version currentNorm = NormalizeVersion(CurrentVersion);

            if (remoteNorm > currentNorm)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MainWindow.Instance?.ShowUpdateModal(bestInfo);
                });
            }
            else
            {
                if (!silent) ShowUpToDate();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateManager Check Error]: {ex.Message}");
            if (!silent) ShowError();
        }
    }

    private static async Task<UpdateInfo?> CheckGitHubLatestAsync(HttpClient client)
    {
        try
        {
            string url = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
            var resp = await client.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;

            string json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
            string rawVer = tagName.Trim().TrimStart('v', 'V');
            if (!Version.TryParse(rawVer, out var remoteVer)) return null;

            var info = new UpdateInfo
            {
                Version = remoteVer,
                VersionString = $"{remoteVer.Major}.{remoteVer.Minor}.{remoteVer.Build}",
                Title = root.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "",
                Changelog = root.TryGetProperty("body", out var bEl) ? bEl.GetString() ?? "" : "",
                ReleaseUrl = root.TryGetProperty("html_url", out var uEl) ? uEl.GetString() ?? GitHubReleasesWebUrl : GitHubReleasesWebUrl,
                SourceName = "GitHub"
            };

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var aName) ? aName.GetString() ?? "" : "";
                    if (name.EndsWith("_Setup.exe", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        info.SetupFileName = name;
                        info.SetupDownloadUrl = asset.TryGetProperty("browser_download_url", out var dlUrl) ? dlUrl.GetString() ?? "" : "";
                        info.SetupFileSize = asset.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                        break;
                    }
                }
            }

            return info;
        }
        catch { return null; }
    }

    private static async Task<UpdateInfo?> CheckStormMirrorLatestAsync(HttpClient client)
    {
        try
        {
            var resp = await client.GetAsync(MirrorApiUrl);
            if (!resp.IsSuccessStatusCode) return null;

            string json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement target = root;
            if (root.ValueKind == JsonValueKind.Array)
            {
                if (root.GetArrayLength() == 0) return null;
                target = root[0];
            }
            else if (root.TryGetProperty("program", out var progEl))
            {
                target = progEl;
            }

            string verStr = "";
            if (target.TryGetProperty("version", out var vEl)) verStr = vEl.GetString() ?? "";
            else if (target.TryGetProperty("latest_version", out var lvEl)) verStr = lvEl.GetString() ?? "";
            verStr = verStr.Trim().TrimStart('v', 'V');

            if (!Version.TryParse(verStr, out var remoteVer)) return null;

            var info = new UpdateInfo
            {
                Version = remoteVer,
                VersionString = $"{remoteVer.Major}.{remoteVer.Minor}.{remoteVer.Build}",
                Title = target.TryGetProperty("title", out var tEl) ? tEl.GetString() ?? "" : "",
                Changelog = target.TryGetProperty("changelog", out var cEl) ? cEl.GetString() ?? "" : "",
                ReleaseUrl = target.TryGetProperty("url", out var uEl) ? uEl.GetString() ?? MirrorWebUrl : MirrorWebUrl,
                SourceName = "STORM GAMES WORLD"
            };

            if (target.TryGetProperty("setup_file", out var sFile))
            {
                info.SetupFileName = sFile.GetString() ?? "";
            }
            if (target.TryGetProperty("download_url", out var dlUrl))
            {
                info.SetupDownloadUrl = dlUrl.GetString() ?? "";
            }

            return info;
        }
        catch { return null; }
    }

    public static async Task DownloadAndExecuteInstallerAsync(UpdateInfo info, IProgress<(long received, long total, int percent)>? progress = null)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "STORM_RETROARCH_UPDATES");
        Directory.CreateDirectory(tempDir);

        string fileName = !string.IsNullOrWhiteSpace(info.SetupFileName)
            ? info.SetupFileName
            : $"STORM_RETROARCH_{info.VersionString}_Setup.exe";
        string filePath = Path.Combine(tempDir, fileName);

        string[] urls = { info.SetupDownloadUrl, info.MirrorSetupDownloadUrl };
        bool success = false;

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.Add("User-Agent", "STORM-RETROARCH");

        foreach (var url in urls)
        {
            if (string.IsNullOrWhiteSpace(url)) continue;
            try
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode) continue;

                long totalBytes = response.Content.Headers.ContentLength ?? info.SetupFileSize;
                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                byte[] buffer = new byte[8192];
                long totalRead = 0;
                int read;

                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read);
                    totalRead += read;
                    if (totalBytes > 0)
                    {
                        int percent = (int)((totalRead * 100) / totalBytes);
                        progress?.Report((totalRead, totalBytes, percent));
                    }
                }

                success = true;
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Download Attempt Error]: {ex.Message}");
            }
        }

        if (success && File.Exists(filePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось запустить файл обновления: {ex.Message}", "STORM RETROARCH", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            MessageBox.Show("Не удалось загрузить файл установщика. Попробуйте обновиться вручную с сайта.", "STORM RETROARCH", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void ShowUpToDate()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            MessageBox.Show(
                $"У вас установлена самая актуальная версия STORM RETROARCH {CurrentVersionString}.",
                "STORM RETROARCH",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        });
    }

    private static void ShowError()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            MessageBox.Show(
                "Не удалось проверить наличие обновлений. Проверьте подключение к сети.",
                "STORM RETROARCH",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        });
    }
}
