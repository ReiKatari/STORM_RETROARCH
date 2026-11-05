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
        SyncItemStatus.InSync => "Синхронизировано",
        SyncItemStatus.LocalNewer => "На ПК новее",
        SyncItemStatus.RemoteNewer => "В облаке новее",
        SyncItemStatus.Conflict => "Конфликт версий",
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

            string defaultSync = Path.Combine(CoreManager.AppDir, "save sync");
            try
            {
                if (!Directory.Exists(defaultSync)) Directory.CreateDirectory(defaultSync);
            }
            catch { }
            return defaultSync;
        }
        set
        {
            SettingsManager.Current.CloudSyncFolder = value;
            SettingsManager.Save();
        }
    }

    static SaveSyncManager()
    {
        try
        {
            if (!Directory.Exists(LocalSavesDir)) Directory.CreateDirectory(LocalSavesDir);
            if (!Directory.Exists(LocalStatesDir)) Directory.CreateDirectory(LocalStatesDir);
            string defSync = Path.Combine(CoreManager.AppDir, "save sync");
            if (!Directory.Exists(defSync)) Directory.CreateDirectory(defSync);
        }
        catch { }
    }

    public static List<SyncEntryItem> ScanSavesAndCompare(string cloudFolder)
    {
        var result = new List<SyncEntryItem>();
        var map = new Dictionary<string, SyncEntryItem>(StringComparer.OrdinalIgnoreCase);

        // 1. Scan Local Saves
        ScanDirectory(LocalSavesDir, map, isLocal: true, prefix: "saves");
        ScanDirectory(LocalStatesDir, map, isLocal: true, prefix: "states");

        // 2. Scan Cloud Saves
        if (!string.IsNullOrEmpty(cloudFolder) && Directory.Exists(cloudFolder))
        {
            string cloudSaves = Path.Combine(cloudFolder, "saves");
            string cloudStates = Path.Combine(cloudFolder, "states");

            if (Directory.Exists(cloudSaves)) ScanDirectory(cloudSaves, map, isLocal: false, prefix: "saves");
            if (Directory.Exists(cloudStates)) ScanDirectory(cloudStates, map, isLocal: false, prefix: "states");

            // Also check root of cloud folder if flat
            ScanDirectory(cloudFolder, map, isLocal: false, prefix: "");
        }

        foreach (var item in map.Values)
        {
            DetermineStatus(item);
            result.Add(item);
        }

        return result.OrderBy(x => x.GameName).ThenBy(x => x.FileName).ToList();
    }

    private static void ScanDirectory(string dir, Dictionary<string, SyncEntryItem> map, bool isLocal, string prefix)
    {
        if (!Directory.Exists(dir)) return;

        try
        {
            var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
            foreach (var f in files)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (!IsSaveOrStateExtension(ext)) continue;

                string fileName = Path.GetFileName(f);
                string gameName = Path.GetFileNameWithoutExtension(fileName);

                // Strip .state suffixes like .state1, .state.auto
                if (gameName.Contains(".state"))
                {
                    int idx = gameName.IndexOf(".state", StringComparison.OrdinalIgnoreCase);
                    if (idx > 0) gameName = gameName.Substring(0, idx);
                }

                string key = fileName.ToLowerInvariant();
                if (!map.TryGetValue(key, out var item))
                {
                    item = new SyncEntryItem
                    {
                        GameName = gameName,
                        FileName = fileName,
                        RelativePath = string.IsNullOrEmpty(prefix) ? fileName : Path.Combine(prefix, fileName)
                    };
                    map[key] = item;
                }

                var fi = new FileInfo(f);
                if (isLocal)
                {
                    item.LocalPath = f;
                    item.LocalSize = fi.Length;
                    item.LocalModifiedUtc = fi.LastWriteTimeUtc;
                }
                else
                {
                    item.RemotePath = f;
                    item.RemoteSize = fi.Length;
                    item.RemoteModifiedUtc = fi.LastWriteTimeUtc;
                }
            }
        }
        catch { }
    }

    private static bool IsSaveOrStateExtension(string ext)
    {
        return ext is ".srm" or ".sav" or ".eep" or ".nv" or ".state" or ".auto" ||
               ext.StartsWith(".state") || ext.StartsWith(".nvram");
    }

    private static void DetermineStatus(SyncEntryItem item)
    {
        if (item.LocalExists && !item.RemoteExists)
        {
            item.Status = SyncItemStatus.LocalOnly;
            return;
        }

        if (!item.LocalExists && item.RemoteExists)
        {
            item.Status = SyncItemStatus.RemoteOnly;
            return;
        }

        if (item.LocalExists && item.RemoteExists)
        {
            // Compute fast hash comparison if sizes match
            if (item.LocalSize == item.RemoteSize)
            {
                string hashLocal = ComputeFileHash(item.LocalPath);
                string hashRemote = ComputeFileHash(item.RemotePath);

                if (hashLocal == hashRemote)
                {
                    item.Status = SyncItemStatus.InSync;
                    return;
                }
            }

            // If hashes differ, compare timestamps
            var diff = item.LocalModifiedUtc - item.RemoteModifiedUtc;
            if (Math.Abs(diff.TotalSeconds) < 2)
            {
                item.Status = SyncItemStatus.Conflict;
            }
            else if (item.LocalModifiedUtc > item.RemoteModifiedUtc)
            {
                item.Status = SyncItemStatus.LocalNewer;
            }
            else
            {
                item.Status = SyncItemStatus.RemoteNewer;
            }
        }
    }

    private static string ComputeFileHash(string path)
    {
        try
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            byte[] bytes = sha.ComputeHash(stream);
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
        catch
        {
            return Guid.NewGuid().ToString();
        }
    }

    public static void SyncItem(SyncEntryItem item, bool uploadToCloud)
    {
        try
        {
            if (uploadToCloud)
            {
                if (!item.LocalExists) return;
                string destDir = Path.GetDirectoryName(item.RemotePath) ?? CloudSyncDir;
                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                File.Copy(item.LocalPath, item.RemotePath, overwrite: true);
                File.SetLastWriteTimeUtc(item.RemotePath, item.LocalModifiedUtc);
                item.RemoteSize = item.LocalSize;
                item.RemoteModifiedUtc = item.LocalModifiedUtc;
                item.Status = SyncItemStatus.InSync;
            }
            else
            {
                if (!item.RemoteExists) return;
                string destDir = Path.GetDirectoryName(item.LocalPath) ?? LocalSavesDir;
                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                File.Copy(item.RemotePath, item.LocalPath, overwrite: true);
                File.SetLastWriteTimeUtc(item.LocalPath, item.RemoteModifiedUtc);
                item.LocalSize = item.RemoteSize;
                item.LocalModifiedUtc = item.RemoteModifiedUtc;
                item.Status = SyncItemStatus.InSync;
            }
        }
        catch { }
    }

    public static List<SyncEntryItem> ScanAndCompareAll(string? cloudFolder = null) => ScanSavesAndCompare(cloudFolder ?? CloudSyncDir);

    public static void UploadToRemote(SyncEntryItem item) => SyncItem(item, uploadToCloud: true);

    public static void DownloadFromRemote(SyncEntryItem item) => SyncItem(item, uploadToCloud: false);

    public static void KeepBoth(SyncEntryItem item)
    {
        try
        {
            if (item.RemoteExists)
            {
                string bak = item.RemotePath + ".bak";
                File.Copy(item.RemotePath, bak, overwrite: true);
            }
            if (item.LocalExists)
            {
                UploadToRemote(item);
            }
        }
        catch { }
    }

    public static void SmartSyncAll(List<SyncEntryItem> items)
    {
        foreach (var item in items)
        {
            if (item.Status == SyncItemStatus.LocalNewer || item.Status == SyncItemStatus.LocalOnly)
            {
                UploadToRemote(item);
            }
            else if (item.Status == SyncItemStatus.RemoteNewer || item.Status == SyncItemStatus.RemoteOnly)
            {
                DownloadFromRemote(item);
            }
        }
    }

}
