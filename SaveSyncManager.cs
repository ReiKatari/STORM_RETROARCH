using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace StormRetroArch;

public enum SyncItemStatus
{
    Synchronized,
    LocalNewer,
    RemoteNewer,
    Conflict,
    LocalOnly,
    RemoteOnly
}

public class SyncEntryItem : INotifyPropertyChanged
{
    private SyncItemStatus _status = SyncItemStatus.Synchronized;
    private string _statusText = "✓ Синхронизировано";

    public string GameName { get; set; } = "";
    public string FileName { get; set; } = "";
    public string RelativePath { get; set; } = ""; // e.g. saves/Castlevania.srm
    public string LocalPath { get; set; } = "";
    public string RemotePath { get; set; } = "";

    public long LocalSize { get; set; }
    public DateTime LocalModifiedUtc { get; set; }
    public string LocalSha256 { get; set; } = "";

    public long RemoteSize { get; set; }
    public DateTime RemoteModifiedUtc { get; set; }
    public string RemoteSha256 { get; set; } = "";

    public SyncItemStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusColor));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } }
    }

    public string StatusColor => Status switch
    {
        SyncItemStatus.Synchronized => "#10B981", // Green
        SyncItemStatus.LocalNewer => "#38BDF8",    // Cyan
        SyncItemStatus.RemoteNewer => "#A78BFA",   // Violet
        SyncItemStatus.Conflict => "#F59E0B",      // Amber
        SyncItemStatus.LocalOnly => "#60A5FA",     // Blue
        SyncItemStatus.RemoteOnly => "#C084FC",    // Purple
        _ => "#94A3B8"
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

            // Default to OneDrive or Documents\STORM RETROARCH Cloud
            string oneDrive = Environment.GetEnvironmentVariable("OneDrive") ?? "";
            if (!string.IsNullOrEmpty(oneDrive) && Directory.Exists(oneDrive))
            {
                string p = Path.Combine(oneDrive, "STORM RETROARCH Cloud");
                if (!Directory.Exists(p)) Directory.CreateDirectory(p);
                return p;
            }

            string userDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string cloud = Path.Combine(userDocs, "STORM RETROARCH Cloud");
            if (!Directory.Exists(cloud)) Directory.CreateDirectory(cloud);
            return cloud;
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

            string cloudSavesDir = Path.Combine(cloudDir, "saves");
            string cloudStatesDir = Path.Combine(cloudDir, "states");
            if (!Directory.Exists(cloudSavesDir)) Directory.CreateDirectory(cloudSavesDir);
            if (!Directory.Exists(cloudStatesDir)) Directory.CreateDirectory(cloudStatesDir);

            // Gather local files
            var localFiles = Directory.GetFiles(LocalSavesDir, "*.*", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(LocalStatesDir, "*.*", SearchOption.AllDirectories))
                .Where(f => !f.EndsWith(".tmp") && !f.EndsWith(".bak"))
                .ToList();

            // Gather cloud files
            var cloudFiles = Directory.GetFiles(cloudDir, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".tmp") && !f.EndsWith(".bak"))
                .ToList();

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

                if (localExists && !remoteExists)
                {
                    var fi = new FileInfo(localPath);
                    item.LocalSize = fi.Length;
                    item.LocalModifiedUtc = fi.LastWriteTimeUtc;
                    item.LocalSha256 = ComputeSha256(localPath);
                    item.Status = SyncItemStatus.LocalOnly;
                    item.StatusText = "⬆️ Только на ПК (Готов к отправке)";
                }
                else if (!localExists && remoteExists)
                {
                    var fi = new FileInfo(remotePath);
                    item.RemoteSize = fi.Length;
                    item.RemoteModifiedUtc = fi.LastWriteTimeUtc;
                    item.RemoteSha256 = ComputeSha256(remotePath);
                    item.Status = SyncItemStatus.RemoteOnly;
                    item.StatusText = "⬇️ Только в Облаке/Android (Готов к загрузке)";
                }
                else if (localExists && remoteExists)
                {
                    var fiLocal = new FileInfo(localPath);
                    var fiRemote = new FileInfo(remotePath);

                    item.LocalSize = fiLocal.Length;
                    item.LocalModifiedUtc = fiLocal.LastWriteTimeUtc;
                    item.LocalSha256 = ComputeSha256(localPath);

                    item.RemoteSize = fiRemote.Length;
                    item.RemoteModifiedUtc = fiRemote.LastWriteTimeUtc;
                    item.RemoteSha256 = ComputeSha256(remotePath);

                    if (item.LocalSha256.Equals(item.RemoteSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        item.Status = SyncItemStatus.Synchronized;
                        item.StatusText = "✓ Синхронизировано (Хэши идентичны)";
                    }
                    else
                    {
                        var timeDiff = item.LocalModifiedUtc - item.RemoteModifiedUtc;
                        if (Math.Abs(timeDiff.TotalSeconds) < 2)
                        {
                            item.Status = SyncItemStatus.Conflict;
                            item.StatusText = "⚠️ Конфликт версий (Разное содержимое)";
                        }
                        else if (item.LocalModifiedUtc > item.RemoteModifiedUtc)
                        {
                            item.Status = SyncItemStatus.LocalNewer;
                            item.StatusText = $"⬆️ ПК новее на {FormatTimeSpan(timeDiff)}";
                        }
                        else
                        {
                            item.Status = SyncItemStatus.RemoteNewer;
                            item.StatusText = $"⬇️ Облако/Android новее на {FormatTimeSpan(-timeDiff)}";
                        }
                    }
                }

                result.Add(item);
            }
        }
        catch { }

        return result.OrderBy(r => r.Status == SyncItemStatus.Conflict ? 0 : 1)
                     .ThenBy(r => r.GameName)
                     .ToList();
    }

    public static void SmartSyncAll(IEnumerable<SyncEntryItem> items)
    {
        foreach (var item in items)
        {
            switch (item.Status)
            {
                case SyncItemStatus.LocalOnly:
                case SyncItemStatus.LocalNewer:
                    UploadToRemote(item);
                    break;
                case SyncItemStatus.RemoteOnly:
                case SyncItemStatus.RemoteNewer:
                    DownloadFromRemote(item);
                    break;
                case SyncItemStatus.Conflict:
                    // Keep both by backing up local version first, then downloading remote
                    KeepBoth(item);
                    break;
            }
        }
    }

    public static void UploadToRemote(SyncEntryItem item)
    {
        try
        {
            if (!File.Exists(item.LocalPath)) return;
            string? dir = Path.GetDirectoryName(item.RemotePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.Copy(item.LocalPath, item.RemotePath, true);
            item.Status = SyncItemStatus.Synchronized;
            item.StatusText = "✓ Синхронизировано";
        }
        catch { }
    }

    public static void DownloadFromRemote(SyncEntryItem item)
    {
        try
        {
            if (!File.Exists(item.RemotePath)) return;
            string? dir = Path.GetDirectoryName(item.LocalPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.Copy(item.RemotePath, item.LocalPath, true);
            item.Status = SyncItemStatus.Synchronized;
            item.StatusText = "✓ Синхронизировано";
        }
        catch { }
    }

    public static void KeepBoth(SyncEntryItem item)
    {
        try
        {
            if (File.Exists(item.LocalPath))
            {
                string ext = Path.GetExtension(item.LocalPath);
                string backup = Path.ChangeExtension(item.LocalPath, $".backup_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
                File.Copy(item.LocalPath, backup, true);
            }
            DownloadFromRemote(item);
        }
        catch { }
    }

    private static string ComputeSha256(string filePath)
    {
        try
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(filePath);
            byte[] hash = sha.ComputeHash(fs);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays} дн.";
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours} ч.";
        if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes} мин.";
        return $"{(int)ts.TotalSeconds} сек.";
    }
}
