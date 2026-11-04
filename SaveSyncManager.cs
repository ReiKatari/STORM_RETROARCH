using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace StormRetroArch;

public enum SyncItemStatus
{
    InSync,
    LocalNewer,
    RemoteNewer,
    Conflict,
    LocalOnly,
    RemoteOnly
}

public class SyncEntryItem : INotifyPropertyChanged
{
    public string GameName { get; set; } = "";
    public string FileName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string LocalPath { get; set; } = "";
    public string RemotePath { get; set; } = "";

    public long LocalSize { get; set; }
    public DateTime LocalModifiedUtc { get; set; }
    public string LocalSha256 { get; set; } = "";

    public long RemoteSize { get; set; }
    public DateTime RemoteModifiedUtc { get; set; }
    public string RemoteSha256 { get; set; } = "";

    public bool LocalExists => !string.IsNullOrEmpty(LocalPath) && File.Exists(LocalPath);
    public bool RemoteExists => !string.IsNullOrEmpty(RemotePath) && File.Exists(RemotePath);

    public string LocalDisplay => LocalExists 
        ? $"{LocalSize / 1024.0:F1} КБ ({LocalModifiedUtc.ToLocalTime():yyyy-MM-dd HH:mm})"
        : "Отсутствует на ПК";

    public string RemoteDisplay => RemoteExists 
        ? $"{RemoteSize / 1024.0:F1} КБ ({RemoteModifiedUtc.ToLocalTime():yyyy-MM-dd HH:mm})"
        : "Отсутствует в облаке";

    private SyncItemStatus _status;
    public SyncItemStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(BadgeBg));
                OnPropertyChanged(nameof(BadgeBorder));
                OnPropertyChanged(nameof(BadgeText));
            }
        }
    }

    public string StatusText => Status switch
    {
        SyncItemStatus.InSync => "✓ Синхронизировано",
        SyncItemStatus.LocalNewer => "⬆ На ПК новее",
        SyncItemStatus.RemoteNewer => "⬇ В облаке новее",
        SyncItemStatus.Conflict => "⚠️ Конфликт версий",
        SyncItemStatus.LocalOnly => "Только на ПК",
        SyncItemStatus.RemoteOnly => "Только в облаке",
        _ => "Неизвестно"
    };

    public string StatusColor => Status switch
    {
        SyncItemStatus.InSync => "#10B981",
        SyncItemStatus.LocalNewer => "#38BDF8",
        SyncItemStatus.RemoteNewer => "#A78BFA",
        SyncItemStatus.Conflict => "#EF4444",
        SyncItemStatus.LocalOnly => "#FBBF24",
        SyncItemStatus.RemoteOnly => "#F59E0B",
        _ => "#9CA3AF"
    };

    public string BadgeBg => Status switch
    {
        SyncItemStatus.InSync => "#1A10B981",
        SyncItemStatus.LocalNewer => "#1A38BDF8",
        SyncItemStatus.RemoteNewer => "#1AA78BFA",
        SyncItemStatus.Conflict => "#1AEF4444",
        SyncItemStatus.LocalOnly => "#1AFBBF24",
        SyncItemStatus.RemoteOnly => "#1AF59E0B",
        _ => "#1A9CA3AF"
    };

    public string BadgeBorder => Status switch
    {
        SyncItemStatus.InSync => "#10B981",
        SyncItemStatus.LocalNewer => "#38BDF8",
        SyncItemStatus.RemoteNewer => "#A78BFA",
        SyncItemStatus.Conflict => "#EF4444",
        SyncItemStatus.LocalOnly => "#FBBF24",
        SyncItemStatus.RemoteOnly => "#F59E0B",
        _ => "#9CA3AF"
    };

    public string BadgeText => Status switch
    {
        SyncItemStatus.InSync => "#6EE7B7",
        SyncItemStatus.LocalNewer => "#7DD3FC",
        SyncItemStatus.RemoteNewer => "#DDD6FE",
        SyncItemStatus.Conflict => "#FCA5A5",
        SyncItemStatus.LocalOnly => "#FDE047",
        SyncItemStatus.RemoteOnly => "#FDE047",
        _ => "#D1D5DB"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class SaveSyncManager
{
    public static readonly string LocalSavesDir = Path.Combine(CoreManager.AppDir, "saves");
    public static readonly string LocalStatesDir = Path.Combine(CoreManager.AppDir, "states");

    public static string CloudSyncDir
    {
        get
        {
            if (!string.IsNullOrEmpty(SettingsManager.Current.CloudSyncFolder) &&
                Directory.Exists(SettingsManager.Current.CloudSyncFolder))
            {
                return SettingsManager.Current.CloudSyncFolder;
            }

            string oneDrive = Environment.GetEnvironmentVariable("OneDrive") ?? "";
            if (!string.IsNullOrEmpty(oneDrive) && Directory.Exists(oneDrive))
            {
                return Path.Combine(oneDrive, "STORM RETROARCH Saves");
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "STORM RETROARCH Cloud");
        }
        set
        {
            SettingsManager.Current.CloudSyncFolder = value;
            SettingsManager.Save();
        }
    }

    public static List<SyncEntryItem> ScanAndCompareAll()
    {
        var result = new List<SyncEntryItem>();
        string cloudDir = CloudSyncDir;

        try
        {
            if (!Directory.Exists(LocalSavesDir)) Directory.CreateDirectory(LocalSavesDir);
            if (!Directory.Exists(LocalStatesDir)) Directory.CreateDirectory(LocalStatesDir);
            if (!Directory.Exists(cloudDir)) Directory.CreateDirectory(cloudDir);

            // Gather real local files
            var localFiles = Directory.GetFiles(LocalSavesDir, "*.*", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(LocalStatesDir, "*.*", SearchOption.AllDirectories))
                .Where(f => !f.EndsWith(".tmp") && !f.EndsWith(".bak") && !f.EndsWith(".gitkeep"))
                .ToList();

            // Gather real cloud files
            var cloudFiles = Directory.Exists(cloudDir)
                ? Directory.GetFiles(cloudDir, "*.*", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith(".tmp") && !f.EndsWith(".bak") && !f.EndsWith(".gitkeep"))
                    .ToList()
                : new List<string>();

            var allRelativeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var lf in localFiles)
            {
                string rel = Path.GetRelativePath(CoreManager.AppDir, lf).Replace('\\', '/');
                allRelativeKeys.Add(rel);
            }

            foreach (var cf in cloudFiles)
            {
                string rel = Path.GetRelativePath(cloudDir, cf).Replace('\\', '/');
                allRelativeKeys.Add(rel);
            }

            foreach (var rel in allRelativeKeys)
            {
                string localPath = Path.Combine(CoreManager.AppDir, rel.Replace('/', '\\'));
                string remotePath = Path.Combine(cloudDir, rel.Replace('/', '\\'));

                bool localExists = File.Exists(localPath);
                bool remoteExists = File.Exists(remotePath);

                var item = new SyncEntryItem
                {
                    RelativePath = rel,
                    FileName = Path.GetFileName(rel),
                    GameName = Path.GetFileNameWithoutExtension(rel),
                    LocalPath = localPath,
                    RemotePath = remotePath
                };

                if (localExists)
                {
                    var fi = new FileInfo(localPath);
                    item.LocalSize = fi.Length;
                    item.LocalModifiedUtc = fi.LastWriteTimeUtc;
                    item.LocalSha256 = ComputeSha256(localPath);
                }

                if (remoteExists)
                {
                    var fi = new FileInfo(remotePath);
                    item.RemoteSize = fi.Length;
                    item.RemoteModifiedUtc = fi.LastWriteTimeUtc;
                    item.RemoteSha256 = ComputeSha256(remotePath);
                }

                // Status classification
                if (localExists && remoteExists)
                {
                    if (item.LocalSha256 == item.RemoteSha256 && item.LocalSha256 != "")
                    {
                        item.Status = SyncItemStatus.InSync;
                    }
                    else
                    {
                        var diff = (item.LocalModifiedUtc - item.RemoteModifiedUtc).TotalSeconds;
                        if (diff > 5) item.Status = SyncItemStatus.LocalNewer;
                        else if (diff < -5) item.Status = SyncItemStatus.RemoteNewer;
                        else item.Status = SyncItemStatus.Conflict;
                    }
                }
                else if (localExists)
                {
                    item.Status = SyncItemStatus.LocalOnly;
                }
                else
                {
                    item.Status = SyncItemStatus.RemoteOnly;
                }

                result.Add(item);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SaveSyncManager Error]: {ex.Message}");
        }

        return result.OrderBy(x => x.GameName).ToList();
    }

    public static void SmartSyncAll(IEnumerable<SyncEntryItem> items)
    {
        foreach (var item in items)
        {
            switch (item.Status)
            {
                case SyncItemStatus.LocalNewer:
                case SyncItemStatus.LocalOnly:
                    UploadToRemote(item);
                    break;
                case SyncItemStatus.RemoteNewer:
                case SyncItemStatus.RemoteOnly:
                    DownloadFromRemote(item);
                    break;
                case SyncItemStatus.Conflict:
                    KeepBoth(item);
                    break;
            }
        }
    }

    public static void UploadToRemote(SyncEntryItem item)
    {
        if (!File.Exists(item.LocalPath)) return;
        try
        {
            var dir = Path.GetDirectoryName(item.RemotePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.Copy(item.LocalPath, item.RemotePath, true);

            var fi = new FileInfo(item.RemotePath);
            item.RemoteSize = fi.Length;
            item.RemoteModifiedUtc = fi.LastWriteTimeUtc;
            item.RemoteSha256 = item.LocalSha256;
            item.Status = SyncItemStatus.InSync;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Upload Error]: {ex.Message}");
        }
    }

    public static void DownloadFromRemote(SyncEntryItem item)
    {
        if (!File.Exists(item.RemotePath)) return;
        try
        {
            var dir = Path.GetDirectoryName(item.LocalPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.Copy(item.RemotePath, item.LocalPath, true);

            var fi = new FileInfo(item.LocalPath);
            item.LocalSize = fi.Length;
            item.LocalModifiedUtc = fi.LastWriteTimeUtc;
            item.LocalSha256 = item.RemoteSha256;
            item.Status = SyncItemStatus.InSync;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Download Error]: {ex.Message}");
        }
    }

    public static void KeepBoth(SyncEntryItem item)
    {
        try
        {
            if (File.Exists(item.RemotePath))
            {
                string ext = Path.GetExtension(item.LocalPath);
                string noExt = Path.ChangeExtension(item.LocalPath, null);
                string backup = $"{noExt}_Remote_{item.RemoteModifiedUtc.ToLocalTime():yyyyMMdd_HHmmss}{ext}";
                File.Copy(item.RemotePath, backup, true);
            }
            UploadToRemote(item);
        }
        catch { }
    }

    public static string CheckAdbDevices()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "adb",
                Arguments = "devices",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string outText = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);
                return outText;
            }
        }
        catch
        {
            return "ADB утилита не найдена в системном PATH.";
        }
        return "Устройства не обнаружены.";
    }

    private static string ComputeSha256(string filePath)
    {
        try
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }
}
