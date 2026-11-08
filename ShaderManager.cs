using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace StormRetroArch;

public class ShaderPresetItem : INotifyPropertyChanged
{
    private bool _isActive;

    public string Name { get; set; } = "";
    public string Category { get; set; } = "CRT и ЭЛТ мониторы";
    public string Description { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public int PassesCount { get; set; } = 1;
    public string IconPathData { get; set; } = "M21 3H3c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h5v2h8v-2h5c1.1 0 1.99-.9 1.99-2L23 5c0-1.1-.9-2-2-2zm0 14H3V5h18v12z";
    public string IconColor { get; set; } = "#38BDF8";

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class ShaderManager
{
    public static readonly string ShadersDir = Directory.Exists(Path.Combine(CoreManager.InstallDir, "shaders")) ? Path.Combine(CoreManager.InstallDir, "shaders") : Path.Combine(CoreManager.AppDir, "shaders");
    public static readonly string SlangDir = Path.Combine(ShadersDir, "shaders_slang");
    public static readonly string FiltersDir = Path.Combine(CoreManager.AppDir, "filters");

    private static readonly List<ShaderPresetItem> _cachedShaders = new();

    static ShaderManager()
    {
        try
        {
            if (!Directory.Exists(ShadersDir)) Directory.CreateDirectory(ShadersDir);
            if (!Directory.Exists(SlangDir)) Directory.CreateDirectory(SlangDir);
            if (!Directory.Exists(FiltersDir)) Directory.CreateDirectory(FiltersDir);
        }
        catch { }
    }

    public static List<ShaderPresetItem> GetAllShaders()
    {
        _cachedShaders.Clear();

        if (Directory.Exists(ShadersDir))
        {
            try
            {
                var files = Directory.GetFiles(ShadersDir, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".slangp", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".glslp", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".cgp", StringComparison.OrdinalIgnoreCase));

                foreach (var fullPath in files)
                {
                    string rel = Path.GetRelativePath(CoreManager.AppDir, fullPath).Replace('\\', '/');
                    string fileName = Path.GetFileName(fullPath);
                    string baseName = Path.GetFileNameWithoutExtension(fullPath);
                    string cleanTitle = baseName.Replace('-', ' ').Replace('_', ' ').Trim();
                    cleanTitle = char.ToUpperInvariant(cleanTitle[0]) + cleanTitle.Substring(1);

                    int passes = 1;
                    try
                    {
                        using var sr = new StreamReader(fullPath);
                        string? line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            var m = Regex.Match(line, @"^shaders\s*=\s*(\d+)");
                            if (m.Success)
                            {
                                passes = int.Parse(m.Groups[1].Value);
                                break;
                            }
                        }
                    }
                    catch { }

                    string relLower = rel.ToLowerInvariant();
                    string category = "Комбинированные пресеты";
                    string iconData = "M12 2A10 10 0 0 0 2 12a10 10 0 0 0 10 10 10 10 0 0 0 10-10A10 10 0 0 0 12 2z";
                    string iconColor = "#A78BFA";
                    string desc = $"Шейдерный пресет Libretro Slang/GLSL ({FormatPassCountRu(passes)}).";

                    if (relLower.Contains("/crt") || relLower.Contains("crt-"))
                    {
                        category = "CRT и ЭЛТ мониторы";
                        iconData = "M21 3H3c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h5v2h8v-2h5c1.1 0 1.99-.9 1.99-2L23 5c0-1.1-.9-2-2-2zm0 14H3V5h18v12z";
                        iconColor = "#38BDF8";
                        desc = $"Эмуляция ЭЛТ-кинескопа, люминофорных триад, апертурной решётки и сканлиний ({FormatPassCountRu(passes)}).";
                    }
                    else if (relLower.Contains("/anti-aliasing") || relLower.Contains("smaa") || relLower.Contains("fxaa"))
                    {
                        category = "Сглаживание (AA / FXAA / SMAA)";
                        iconData = "M7 2v11h3v9l7-12h-4l4-8z";
                        iconColor = "#10B981";
                        desc = $"Морфологическое и субпиксельное сглаживание зубчатых краёв полигонов ({FormatPassCountRu(passes)}).";
                    }
                    else if (relLower.Contains("/interpolation") || relLower.Contains("/scale") || relLower.Contains("xbr") || relLower.Contains("hq") || relLower.Contains("fsr"))
                    {
                        category = "Апскейл и интерполяция";
                        iconData = "M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z";
                        iconColor = "#FBBF24";
                        desc = $"Апскейлер сверхвысокого разрешения: xBRZ, ScaleFX, Bicubic и FSR ({FormatPassCountRu(passes)}).";
                    }
                    else if (relLower.Contains("/blurs") || relLower.Contains("blur") || relLower.Contains("bloom"))
                    {
                        category = "Размытие и свечение (Bloom / Blur)";
                        iconData = "M12 2a10 10 0 1 0 10 10A10 10 0 0 0 12 2zm1 14.93V17a1 1 0 0 1-2 0v-.07A8 8 0 0 1 4.07 11H4a1 1 0 0 1 0-2h.07A8 8 0 0 1 11 4.07V4a1 1 0 0 1 2 0v.07A8 8 0 0 1 19.93 11H20a1 1 0 0 1 0 2h-.07A8 8 0 0 1 13 16.93z";
                        iconColor = "#EC4899";
                        desc = $"Кинематографичное свечение Kawase Bloom, мягкое размытие и светорассеяние ({FormatPassCountRu(passes)}).";
                    }
                    else if (relLower.Contains("/sharpen") || relLower.Contains("cas") || relLower.Contains("adaptive-sharpen"))
                    {
                        category = "Резкость и чёткость (Sharpen / CAS)";
                        iconData = "M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04c.39-.39.39-1.02 0-1.41l-2.34-2.34a.9959.9959 0 0 0-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z";
                        iconColor = "#34D399";
                        desc = $"Контрастно-адаптивное повышение резкости AMD CAS и Adaptive Sharpen ({FormatPassCountRu(passes)}).";
                    }
                    else if (relLower.Contains("/handheld") || relLower.Contains("/ntsc") || relLower.Contains("/vhs") || relLower.Contains("/border"))
                    {
                        category = "Портативные и ретро (LCD / NTSC / VHS)";
                        iconData = "M17 1.01L7 1c-1.1 0-2 .9-2 2v18c0 1.1.9 2 2 2h10c1.1 0 2-.9 2-2V3c0-1.1-.9-1.99-2-1.99zM17 19H7V5h10v14z";
                        iconColor = "#8B5CF6";
                        desc = $"Аутентичные ЖК-матрицы Game Boy, GBA, NDS, PSP и аналоговые помехи NTSC/VHS ({FormatPassCountRu(passes)}).";
                    }

                    _cachedShaders.Add(new ShaderPresetItem
                    {
                        Name = cleanTitle,
                        Category = category,
                        Description = desc,
                        RelativePath = rel,
                        PassesCount = passes,
                        IconPathData = iconData,
                        IconColor = iconColor
                    });
                }
            }
            catch { }
        }

        // Active shader detection
        string activeShader = RetroArchConfigManager.GetString("video_shader_dir", "");
        bool isEnabled = RetroArchConfigManager.GetBool("video_shader_enable", true);

        foreach (var item in _cachedShaders)
        {
            item.IsActive = isEnabled && !string.IsNullOrEmpty(activeShader) &&
                            (activeShader.Contains(Path.GetFileName(item.RelativePath), StringComparison.OrdinalIgnoreCase));
        }

        return _cachedShaders.OrderBy(s => s.Category).ThenBy(s => s.Name).ToList();
    }

    public static void ApplyShader(ShaderPresetItem item)
    {
        RetroArchConfigManager.SetBool("video_shader_enable", true);
        string fullPath = Path.Combine(CoreManager.AppDir, item.RelativePath);
        RetroArchConfigManager.SetString("video_shader_dir", fullPath);
        RetroArchConfigManager.Save();

        foreach (var s in _cachedShaders)
        {
            s.IsActive = (s == item);
        }
    }

    public static void DisableShaders()
    {
        RetroArchConfigManager.SetBool("video_shader_enable", false);
        RetroArchConfigManager.Save();

        foreach (var s in _cachedShaders)
        {
            s.IsActive = false;
        }
    }

    public static async Task<bool> DownloadAllShadersPackageAsync(IProgress<(double percent, string status)>? progress = null)
    {
        string url = "https://buildbot.libretro.com/assets/frontend/shaders_slang.zip";
        string zipPath = Path.Combine(CoreManager.DownloadsDir, "shaders_slang.zip");

        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(5);

            progress?.Report((5, "Подключение к Libretro CDN..."));

            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                progress?.Report((0, $"Ошибка HTTP {response.StatusCode}"));
                return false;
            }

            long totalBytes = response.Content.Headers.ContentLength ?? -1;
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);

            byte[] buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    double pct = (double)totalRead / totalBytes * 75.0;
                    progress?.Report((pct, $"Загрузка {totalRead / 1024 / 1024} МБ из {totalBytes / 1024 / 1024} МБ..."));
                }
            }

            fileStream.Close();
            progress?.Report((80, "Распаковка шейдеров Slang..."));

            await Task.Run(() =>
            {
                using var archive = ZipFile.OpenRead(zipPath);
                archive.ExtractToDirectory(ShadersDir, overwriteFiles: true);
            });

            try { File.Delete(zipPath); } catch { }

            progress?.Report((100, "Все 450+ шейдеров успешно установлены!"));
            return true;
        }
        catch (Exception ex)
        {
            progress?.Report((0, $"Сбой: {ex.Message}"));
            return false;
        }
    }

    private static string FormatPassCountRu(int n)
    {
        int mod100 = Math.Abs(n) % 100;
        int mod10 = mod100 % 10;
        if (mod100 >= 11 && mod100 <= 19) return $"{n} проходов";
        if (mod10 == 1) return $"{n} проход";
        if (mod10 >= 2 && mod10 <= 4) return $"{n} прохода";
        return $"{n} проходов";
    }

}
