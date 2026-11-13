using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace StormRetroArch;

public class ExternalEmulatorEntry : INotifyPropertyChanged
{
    private string _id = "";
    private string _name = "";
    private string _executablePath = "";
    private string _argumentsTemplate = "\"{ROM}\"";
    private string _platformTags = "";
    private bool _isAutoDetected;
    private bool _isEnabled = true;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string ExecutablePath
    {
        get => _executablePath;
        set
        {
            if (SetField(ref _executablePath, value))
            {
                OnPropertyChanged(nameof(ExistsOnDisk));
                OnPropertyChanged(nameof(StatusLabel));
            }
        }
    }

    public string ArgumentsTemplate
    {
        get => _argumentsTemplate;
        set => SetField(ref _argumentsTemplate, value);
    }

    public string PlatformTags
    {
        get => _platformTags;
        set => SetField(ref _platformTags, value);
    }

    public bool IsAutoDetected
    {
        get => _isAutoDetected;
        set
        {
            if (SetField(ref _isAutoDetected, value))
                OnPropertyChanged(nameof(SourceBadge));
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }

    public bool ExistsOnDisk => !string.IsNullOrWhiteSpace(ExecutablePath) && File.Exists(ExecutablePath);
    public string SourceBadge => IsAutoDetected ? "Авто" : "Вручную";
    public string StatusLabel => ExistsOnDisk ? "Готов" : "Не найден";

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? prop = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(prop);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? prop = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

/// <summary>
/// Manages external standalone emulators (PCSX2, RPCS3, Vita3K, Switch, Ryujinx, Dolphin, DuckStation,
/// PPSSPP, Azahar, Cemu, Xenia), native PC .exe/.lnk launches, and RetroArch .lpl playlist imports.
/// </summary>
public static class ExternalEmulatorBridge
{
    private static readonly object _lock = new();

    private static string StoreFilePath =>
        Path.Combine(CoreManager.AppDir, "playlists", "storm_external_emulators.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed record EmulatorProbeSpec(
        string Id,
        string Name,
        string PlatformTags,
        string ArgumentsTemplate,
        string[] ExeNames,
        string[] FolderHints);

    private static readonly EmulatorProbeSpec[] KnownEmulators =
    {
        new("pcsx2", "PCSX2", "PlayStation 2, PS2", "-batch \"{ROM}\"",
            new[] { "pcsx2-qt.exe", "pcsx2-qtx64.exe", "pcsx2-qtx64-avx2.exe", "pcsx2.exe" },
            new[] { "PCSX2", "PCSX2-Qt" }),
        new("rpcs3", "RPCS3", "PlayStation 3, PS3", "--no-gui \"{ROM}\"",
            new[] { "rpcs3.exe" },
            new[] { "RPCS3", "rpcs3" }),
        new("vita3k", "Vita3K", "PS Vita, PlayStation Vita, PSV", "\"{ROM}\"",
            new[] { "Vita3K.exe", "vita3k.exe" },
            new[] { "Vita3K", "vita3k" }),
        new("storm_switch", "STORM SWITCH", "Nintendo Switch, Switch", "-f -g \"{ROM}\"",
            new[] { "STORM SWITCH.exe", "storm_switch.exe", "eden.exe", "yuzu.exe", "suyu.exe", "citron.exe" },
            new[] { "STORM SWITCH", "Eden", "yuzu", "suyu", "citron" }),
        new("ryujinx", "Ryujinx", "Nintendo Switch, Switch", "\"{ROM}\"",
            new[] { "Ryujinx.exe" },
            new[] { "Ryujinx" }),
        new("dolphin", "Dolphin", "GameCube, Wii, Nintendo GameCube, Nintendo Wii", "-b -e \"{ROM}\"",
            new[] { "Dolphin.exe" },
            new[] { "Dolphin", "Dolphin Emulator" }),
        new("duckstation", "DuckStation", "PlayStation 1, PS1, PSX", "-batch \"{ROM}\"",
            new[] { "duckstation-qt-x64-ReleaseLTCG.exe", "duckstation-nogui-x64-ReleaseLTCG.exe", "duckstation.exe" },
            new[] { "DuckStation" }),
        new("ppsspp", "PPSSPP", "PSP, PlayStation Portable", "\"{ROM}\"",
            new[] { "PPSSPPWindows64.exe", "PPSSPPWindows.exe" },
            new[] { "PPSSPP" }),
        new("azahar", "Azahar", "Nintendo 3DS, 3DS", "\"{ROM}\"",
            new[] { "azahar.exe", "azahar-qt.exe", "lime3ds.exe", "citra-qt.exe" },
            new[] { "Azahar", "Lime3DS", "Citra" }),
        new("cemu", "Cemu", "Wii U, Nintendo Wii U", "-g \"{ROM}\"",
            new[] { "Cemu.exe" },
            new[] { "Cemu", "cemu" }),
        new("xenia", "Xenia", "Xbox 360", "\"{ROM}\"",
            new[] { "xenia_canary.exe", "xenia.exe" },
            new[] { "Xenia", "xenia_canary" })
    };

    public static List<ExternalEmulatorEntry> LoadEmulators()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(StoreFilePath))
                {
                    string json = File.ReadAllText(StoreFilePath);
                    var items = JsonSerializer.Deserialize<List<ExternalEmulatorEntry>>(json, JsonOpts);
                    if (items != null) return items;
                }
            }
            catch { }
            return new List<ExternalEmulatorEntry>();
        }
    }

    public static void SaveEmulators(IEnumerable<ExternalEmulatorEntry> entries)
    {
        lock (_lock)
        {
            try
            {
                string path = StoreFilePath;
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(entries.ToList(), JsonOpts);
                File.WriteAllText(path, json);
            }
            catch { }
        }
    }

    public static List<ExternalEmulatorEntry> DetectInstalledEmulators()
    {
        var existing = LoadEmulators();
        var merged = new List<ExternalEmulatorEntry>(existing);

        var roots = new List<string>();
        void AddRoot(string? dir)
        {
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                roots.Add(dir);
        }

        AddRoot(CoreManager.AppDir);
        AddRoot(Path.GetDirectoryName(CoreManager.AppDir));
        AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        AddRoot(@"C:\Emulators");
        AddRoot(@"D:\Emulators");
        AddRoot(@"N:\");
        AddRoot(@"N:\Emulators");
        AddRoot(@"F:\Emulators");

        var distinctRoots = roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var spec in KnownEmulators)
        {
            string? foundExe = ProbeSpec(spec, distinctRoots);
            if (string.IsNullOrEmpty(foundExe)) continue;

            var existingEntry = merged.FirstOrDefault(e =>
                e.Id.Equals(spec.Id, StringComparison.OrdinalIgnoreCase) ||
                e.ExecutablePath.Equals(foundExe, StringComparison.OrdinalIgnoreCase));

            if (existingEntry != null)
            {
                if (!existingEntry.ExistsOnDisk)
                {
                    existingEntry.ExecutablePath = foundExe;
                }
                continue;
            }

            merged.Add(new ExternalEmulatorEntry
            {
                Id = spec.Id,
                Name = spec.Name,
                ExecutablePath = foundExe,
                ArgumentsTemplate = spec.ArgumentsTemplate,
                PlatformTags = spec.PlatformTags,
                IsAutoDetected = true,
                IsEnabled = true
            });
        }

        SaveEmulators(merged);
        return merged;
    }

    private static string? ProbeSpec(EmulatorProbeSpec spec, List<string> roots)
    {
        foreach (var root in roots)
        {
            foreach (var folder in spec.FolderHints)
            {
                string candidateDir = Path.Combine(root, folder);
                if (!Directory.Exists(candidateDir)) continue;

                foreach (var exe in spec.ExeNames)
                {
                    string direct = Path.Combine(candidateDir, exe);
                    if (File.Exists(direct)) return direct;

                    try
                    {
                        foreach (var subDir in Directory.EnumerateDirectories(candidateDir))
                        {
                            string nested = Path.Combine(subDir, exe);
                            if (File.Exists(nested)) return nested;
                        }
                    }
                    catch { }
                }
            }
        }
        return null;
    }

    public static (bool Success, string Message) LaunchWithExternalEmulator(ExternalEmulatorEntry? emu, RomItem game)
    {
        if (game == null || string.IsNullOrWhiteSpace(game.FilePath))
            return (false, "Файл игры не указан");

        if (!File.Exists(game.FilePath))
            return (false, $"Файл игры не найден: {game.FilePath}");

        string ext = Path.GetExtension(game.FilePath).ToLowerInvariant();
        if (emu == null || ext is ".exe" or ".lnk" or ".bat" or ".cmd")
        {
            try
            {
                var psiDirect = new ProcessStartInfo
                {
                    FileName = game.FilePath,
                    WorkingDirectory = Path.GetDirectoryName(game.FilePath) ?? CoreManager.AppDir,
                    UseShellExecute = true
                };
                Process.Start(psiDirect);
                GameMetadataStore.RecordLaunch(game.Name);
                GameMetadataStore.ApplyToRomItem(game);
                return (true, $"Запущено напрямую: {game.Name}");
            }
            catch (Exception ex)
            {
                return (false, $"Ошибка прямого запуска: {ex.Message}");
            }
        }

        if (!File.Exists(emu.ExecutablePath))
            return (false, $"Исполняемый файл эмулятора {emu.Name} не найден: {emu.ExecutablePath}");

        try
        {
            string template = string.IsNullOrWhiteSpace(emu.ArgumentsTemplate) ? "\"{ROM}\"" : emu.ArgumentsTemplate;
            string args = template.Contains("{ROM}", StringComparison.OrdinalIgnoreCase)
                ? template.Replace("{ROM}", game.FilePath, StringComparison.OrdinalIgnoreCase)
                : $"{template} \"{game.FilePath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = emu.ExecutablePath,
                Arguments = args,
                WorkingDirectory = Path.GetDirectoryName(emu.ExecutablePath) ?? CoreManager.AppDir,
                UseShellExecute = true
            };
            Process.Start(psi);
            GameMetadataStore.RecordLaunch(game.Name);
            GameMetadataStore.ApplyToRomItem(game);
            return (true, $"Запущено через {emu.Name}: {game.Name}");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка запуска {emu.Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Imports RetroArch .lpl playlists (both JSON and legacy 6-line formats) from a directory.
    /// </summary>
    public static List<RomItem> ImportRetroArchPlaylists(
        string retroarchPlaylistsDir,
        IList<RomItem> currentGames,
        out int addedCount,
        out int totalScanned)
    {
        addedCount = 0;
        totalScanned = 0;
        var newItems = new List<RomItem>();

        if (string.IsNullOrWhiteSpace(retroarchPlaylistsDir) || !Directory.Exists(retroarchPlaylistsDir))
            return newItems;

        var existingPaths = new HashSet<string>(
            currentGames.Where(g => !string.IsNullOrWhiteSpace(g.FilePath)).Select(g => g.FilePath),
            StringComparer.OrdinalIgnoreCase);

        string[] lplFiles;
        try
        {
            lplFiles = Directory.GetFiles(retroarchPlaylistsDir, "*.lpl", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return newItems;
        }

        foreach (var lpl in lplFiles)
        {
            var entries = ParseLplFile(lpl);
            foreach (var (rawPath, label, corePath, coreName, dbName) in entries)
            {
                totalScanned++;
                string resolvedPath = ResolvePlaylistRomPath(rawPath);
                if (string.IsNullOrEmpty(resolvedPath) || !File.Exists(resolvedPath)) continue;
                if (!existingPaths.Add(resolvedPath)) continue;

                try
                {
                    var fi = new FileInfo(resolvedPath);
                    var (platform, coreDll, coreDisplay) = CoreManager.IdentifyRom(resolvedPath);

                    if ((string.IsNullOrEmpty(platform) || platform == "Неизвестная платформа") && !string.IsNullOrWhiteSpace(dbName))
                    {
                        platform = Path.GetFileNameWithoutExtension(dbName);
                    }

                    if (string.IsNullOrEmpty(coreDll) && !string.IsNullOrWhiteSpace(corePath) &&
                        !corePath.Equals("DETECT", StringComparison.OrdinalIgnoreCase))
                    {
                        coreDll = Path.GetFileName(corePath);
                    }
                    if (string.IsNullOrEmpty(coreDisplay) && !string.IsNullOrWhiteSpace(coreName) &&
                        !coreName.Equals("DETECT", StringComparison.OrdinalIgnoreCase))
                    {
                        coreDisplay = coreName;
                    }

                    var item = new RomItem
                    {
                        FilePath = resolvedPath,
                        Extension = fi.Extension.ToLowerInvariant(),
                        Platform = platform,
                        CoreDll = coreDll,
                        CoreDisplayName = coreDisplay,
                        FileSizeBytes = fi.Length,
                        IsCoreInstalled = CoreManager.IsCoreInstalled(coreDll),
                        StatusText = CoreManager.IsCoreInstalled(coreDll) ? "Готово к запуску" : "Требуется ядро"
                    };

                    item.ParseAndUpdateFromFileName(fi.Name);
                    if (!string.IsNullOrWhiteSpace(label) && string.IsNullOrWhiteSpace(item.Name))
                    {
                        item.Name = label.Trim();
                    }

                    GameMetadataStore.ApplyToRomItem(item);
                    newItems.Add(item);
                    addedCount++;
                }
                catch { }
            }
        }

        return newItems;
    }

    private static string ResolvePlaylistRomPath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return "";
        string clean = rawPath.Trim();
        // RetroArch .lpl may append "#internal_file" for archives
        int hashIdx = clean.IndexOf('#');
        if (hashIdx > 0)
        {
            string archivePath = clean.Substring(0, hashIdx);
            if (File.Exists(archivePath)) return archivePath;
        }
        return clean;
    }

    private static List<(string Path, string Label, string CorePath, string CoreName, string DbName)> ParseLplFile(string filePath)
    {
        var results = new List<(string, string, string, string, string)>();
        try
        {
            string text = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(text)) return results;

            string trimmed = text.TrimStart();
            if (trimmed.StartsWith("{"))
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in itemsEl.EnumerateArray())
                    {
                        string path = el.TryGetProperty("path", out var p) ? (p.GetString() ?? "") : "";
                        string label = el.TryGetProperty("label", out var l) ? (l.GetString() ?? "") : "";
                        string cPath = el.TryGetProperty("core_path", out var cp) ? (cp.GetString() ?? "") : "";
                        string cName = el.TryGetProperty("core_name", out var cn) ? (cn.GetString() ?? "") : "";
                        string db = el.TryGetProperty("db_name", out var d) ? (d.GetString() ?? "") : "";
                        if (!string.IsNullOrWhiteSpace(path))
                            results.Add((path, label, cPath, cName, db));
                    }
                }
            }
            else
            {
                // Legacy 6-line RetroArch .lpl format: path, label, core_path, core_name, crc32, db_name
                var lines = text.Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i + 5 < lines.Length; i += 6)
                {
                    string path = lines[i].Trim();
                    string label = lines[i + 1].Trim();
                    string cPath = lines[i + 2].Trim();
                    string cName = lines[i + 3].Trim();
                    string db = lines[i + 5].Trim();
                    if (!string.IsNullOrWhiteSpace(path))
                        results.Add((path, label, cPath, cName, db));
                }
            }
        }
        catch { }
        return results;
    }
}
