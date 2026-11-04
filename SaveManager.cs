using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StormRetroArch;

public class SaveEntryItem : INotifyPropertyChanged
{
    public string GameName { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string SaveType { get; set; } = "Сохранение (.srm)";
    public int Slot { get; set; } = -1;
    public long FileSizeBytes { get; set; }
    public string FormattedSize { get; set; } = "";
    public DateTime ModifiedDate { get; set; }
    public string FormattedDate { get; set; } = "";
    public string ScreenshotPath { get; set; } = "";
    public ImageSource? Screenshot { get; set; }
    public bool HasScreenshot => !string.IsNullOrEmpty(ScreenshotPath) && File.Exists(ScreenshotPath);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class SaveManager
{
    public static readonly string SavesDir = Path.Combine(CoreManager.AppDir, "saves");
    public static readonly string StatesDir = Path.Combine(CoreManager.AppDir, "states");

    static SaveManager()
    {
        try
        {
            if (!Directory.Exists(SavesDir)) Directory.CreateDirectory(SavesDir);
            if (!Directory.Exists(StatesDir)) Directory.CreateDirectory(StatesDir);
        }
        catch { }
    }

    public static List<SaveEntryItem> ScanSavesAndStates()
    {
        var list = new List<SaveEntryItem>();

        // 1. Scan Saves (.srm, .sav, .eep, .nv)
        if (Directory.Exists(SavesDir))
        {
            try
            {
                var files = Directory.GetFiles(SavesDir, "*.*", SearchOption.AllDirectories);
                foreach (var f in files)
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext is ".srm" or ".sav" or ".eep" or ".nv" or ".mcd" or ".psx")
                    {
                        var fi = new FileInfo(f);
                        list.Add(new SaveEntryItem
                        {
                            GameName = Path.GetFileNameWithoutExtension(f),
                            FileName = fi.Name,
                            FilePath = f,
                            SaveType = "Батареечное сохранение (" + ext + ")",
                            Slot = -1,
                            FileSizeBytes = fi.Length,
                            FormattedSize = RomItem.FormatFileSize(fi.Length),
                            ModifiedDate = fi.LastWriteTime,
                            FormattedDate = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                        });
                    }
                }
            }
            catch { }
        }

        // 2. Scan States (.state, .state0..99, .state.auto)
        if (Directory.Exists(StatesDir))
        {
            try
            {
                var files = Directory.GetFiles(StatesDir, "*.*", SearchOption.AllDirectories);
                foreach (var f in files)
                {
                    string name = Path.GetFileName(f);
                    if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;

                    if (name.Contains(".state"))
                    {
                        var fi = new FileInfo(f);
                        int slot = -1;
                        string typeStr = "Быстрый стейт (Слот 0)";

                        int stateIdx = name.LastIndexOf(".state", StringComparison.OrdinalIgnoreCase);
                        if (stateIdx >= 0)
                        {
                            string suffix = name.Substring(stateIdx + 6);
                            if (suffix.Equals(".auto", StringComparison.OrdinalIgnoreCase))
                            {
                                typeStr = "Авто-сохранение (.state.auto)";
                            }
                            else if (int.TryParse(suffix, out int s))
                            {
                                slot = s;
                                typeStr = $"Быстрый стейт (Слот {s})";
                            }
                        }

                        string baseGame = name.Substring(0, stateIdx);
                        string shotPath = Path.Combine(Path.GetDirectoryName(f) ?? StatesDir, name + ".png");

                        ImageSource? img = null;
                        if (File.Exists(shotPath))
                        {
                            try
                            {
                                var bmp = new BitmapImage();
                                bmp.BeginInit();
                                bmp.UriSource = new Uri(shotPath);
                                bmp.DecodePixelWidth = 180;
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.EndInit();
                                bmp.Freeze();
                                img = bmp;
                            }
                            catch { }
                        }

                        list.Add(new SaveEntryItem
                        {
                            GameName = baseGame,
                            FileName = fi.Name,
                            FilePath = f,
                            SaveType = typeStr,
                            Slot = slot,
                            FileSizeBytes = fi.Length,
                            FormattedSize = RomItem.FormatFileSize(fi.Length),
                            ModifiedDate = fi.LastWriteTime,
                            FormattedDate = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                            ScreenshotPath = shotPath,
                            Screenshot = img
                        });
                    }
                }
            }
            catch { }
        }

        return list.OrderByDescending(s => s.ModifiedDate).ToList();
    }

    public static string CreateZipBackup()
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string backupName = $"STORM_SAVES_BACKUP_{timestamp}.zip";
        string targetZip = Path.Combine(CoreManager.AppDir, backupName);

        if (File.Exists(targetZip)) File.Delete(targetZip);

        using (var zip = ZipFile.Open(targetZip, ZipArchiveMode.Create))
        {
            if (Directory.Exists(SavesDir))
            {
                foreach (var f in Directory.GetFiles(SavesDir, "*.*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(CoreManager.AppDir, f);
                    zip.CreateEntryFromFile(f, rel);
                }
            }
            if (Directory.Exists(StatesDir))
            {
                foreach (var f in Directory.GetFiles(StatesDir, "*.*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(CoreManager.AppDir, f);
                    zip.CreateEntryFromFile(f, rel);
                }
            }
        }

        return targetZip;
    }

    public static void RestoreFromZipBackup(string zipPath)
    {
        if (!File.Exists(zipPath)) return;

        using (var zip = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in zip.Entries)
            {
                string dest = Path.Combine(CoreManager.AppDir, entry.FullName);
                string? dir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (!string.IsNullOrEmpty(entry.Name))
                {
                    entry.ExtractToFile(dest, overwrite: true);
                }
            }
        }
    }

    public static void DeleteSave(SaveEntryItem item)
    {
        try
        {
            if (File.Exists(item.FilePath)) File.Delete(item.FilePath);
            if (!string.IsNullOrEmpty(item.ScreenshotPath) && File.Exists(item.ScreenshotPath))
                File.Delete(item.ScreenshotPath);
        }
        catch { }
    }
}
