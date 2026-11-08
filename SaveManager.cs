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
    public string FullPath => FilePath;
    public string SaveType { get; set; } = "Сохранение (.srm)";
    public int Slot { get; set; } = -1;
    public long FileSizeBytes { get; set; }
    public string FormattedSize { get; set; } = "";
    public DateTime ModifiedDate { get; set; }
    public string FormattedDate { get; set; } = "";
    public string ScreenshotPath { get; set; } = "";

    private ImageSource? _screenshot;
    public ImageSource? Screenshot
    {
        get => _screenshot;
        set
        {
            _screenshot = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PreviewImage));
            OnPropertyChanged(nameof(HasScreenshot));
            OnPropertyChanged(nameof(HasPreviewImage));
        }
    }

    public ImageSource? PreviewImage
    {
        get => _screenshot;
        set => Screenshot = value;
    }

    public bool HasScreenshot => !string.IsNullOrEmpty(ScreenshotPath) && File.Exists(ScreenshotPath);
    public bool HasPreviewImage => _screenshot != null;

    private bool _isLocked;
    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (_isLocked != value)
            {
                _isLocked = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LockBadgeText));
            }
        }
    }

    public string LockBadgeText => IsLocked ? "Защищён" : "Открыт";
    public string SlotBadge { get; set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Псевдоним и расширение записи файла сохранения для 100% совместимости API.
/// </summary>
public class SaveFileEntry : SaveEntryItem
{
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
                        string srmShot = f + ".png";
                        ImageSource? srmImg = null;
                        if (File.Exists(srmShot))
                        {
                            try
                            {
                                var bmp = new BitmapImage();
                                bmp.BeginInit();
                                bmp.UriSource = new Uri(srmShot);
                                bmp.DecodePixelWidth = 180;
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.EndInit();
                                bmp.Freeze();
                                srmImg = bmp;
                            }
                            catch { }
                        }
                        srmImg ??= GetGamingPlaceholderImage();

                        list.Add(new SaveEntryItem
                        {
                            GameName = Path.GetFileNameWithoutExtension(f),
                            FileName = fi.Name,
                            FilePath = f,
                            SaveType = "Батареечное сохранение (" + ext + ")",
                            Slot = -1,
                            SlotBadge = "SRAM",
                            IsLocked = File.Exists(f + ".lock"),
                            FileSizeBytes = fi.Length,
                            FormattedSize = RomItem.FormatFileSize(fi.Length),
                            ModifiedDate = fi.LastWriteTime,
                            FormattedDate = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                            ScreenshotPath = srmShot,
                            Screenshot = srmImg
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

                        img ??= GetGamingPlaceholderImage();
                        string badge = slot >= 0 ? $"Слот {slot}" : (name.Contains(".auto", StringComparison.OrdinalIgnoreCase) ? "Автосейв" : "Стейт");

                        list.Add(new SaveEntryItem
                        {
                            GameName = baseGame,
                            FileName = fi.Name,
                            FilePath = f,
                            SaveType = typeStr,
                            Slot = slot,
                            SlotBadge = badge,
                            IsLocked = File.Exists(f + ".lock"),
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

        private static ImageSource? _neonPlaceholder;
    private static readonly object PlaceholderLock = new();

    public static ImageSource GetGamingPlaceholderImage()
    {
        if (_neonPlaceholder != null) return _neonPlaceholder;
        lock (PlaceholderLock)
        {
            if (_neonPlaceholder != null) return _neonPlaceholder;
            int w = 160;
            int h = 120;
            int stride = w * 4;
            byte[] pixels = new byte[h * stride];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = (y * stride) + (x * 4);
                    bool isBorder = (x == 0 || x == w - 1 || y == 0 || y == h - 1);
                    bool isInnerGrid = (x % 20 == 0 || y % 20 == 0);

                    if (isBorder)
                    {
                        pixels[idx + 0] = 0xC8; // B
                        pixels[idx + 1] = 0x64; // G
                        pixels[idx + 2] = 0x7C; // R (#7C64C8 neon violet)
                        pixels[idx + 3] = 0xFF;
                    }
                    else if (isInnerGrid)
                    {
                        pixels[idx + 0] = 0x2A;
                        pixels[idx + 1] = 0x1A;
                        pixels[idx + 2] = 0x20;
                        pixels[idx + 3] = 0xFF;
                    }
                    else
                    {
                        pixels[idx + 0] = (byte)(0x18 + (y * 12 / h));
                        pixels[idx + 1] = 0x0E;
                        pixels[idx + 2] = (byte)(0x12 + (x * 10 / w));
                        pixels[idx + 3] = 0xFF;
                    }
                }
            }

            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            bmp.Freeze();
            _neonPlaceholder = bmp;
            return _neonPlaceholder;
        }
    }

    public static bool ToggleLock(SaveEntryItem entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.FilePath)) return false;
        string lockPath = entry.FilePath + ".lock";
        try
        {
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
                entry.IsLocked = false;
            }
            else
            {
                File.WriteAllText(lockPath, DateTime.UtcNow.ToString("O"));
                entry.IsLocked = true;
            }
            return entry.IsLocked;
        }
        catch
        {
            return entry.IsLocked;
        }
    }

    public static bool ToggleLock(SaveFileEntry entry) => ToggleLock((SaveEntryItem)entry);

    public static bool DeleteSaveFile(SaveEntryItem entry)
    {
        if (entry.IsLocked) return false;
        DeleteSave(entry);
        return true;
    }

    public static bool DeleteSaveFile(SaveFileEntry entry) => DeleteSaveFile((SaveEntryItem)entry);

    public static void DeleteSave(SaveEntryItem item)
    {
        if (item.IsLocked) return;
        try
        {
            if (File.Exists(item.FilePath)) File.Delete(item.FilePath);
            if (!string.IsNullOrEmpty(item.ScreenshotPath) && File.Exists(item.ScreenshotPath))
                File.Delete(item.ScreenshotPath);
            string lockPath = item.FilePath + ".lock";
            if (File.Exists(lockPath)) File.Delete(lockPath);
        }
        catch { }
    }
}
