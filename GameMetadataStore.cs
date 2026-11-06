using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace StormRetroArch;

public class GameMetaRecord
{
    public long TotalPlaySeconds { get; set; }
    public int LaunchCount { get; set; }
    public string LastPlayedIso { get; set; } = "";
    public string CompletionStatus { get; set; } = "Не начато";
    public int StarRating { get; set; } = 0;
    public string UserNotes { get; set; } = "";
    public string CustomCollection { get; set; } = "";
}

public static class GameMetadataStore
{
    public static readonly string[] CompletionStatuses =
    {
        "Не начато",
        "Играю сейчас",
        "Пройдено",
        "Пройдено на 100%",
        "В планах",
        "Отложено"
    };

    private static readonly object _fileLock = new();
    private static readonly ConcurrentDictionary<string, GameMetaRecord> _store = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded = false;

    private static string MetadataFilePath => Path.Combine(CoreManager.AppDir, "playlists", "storm_game_metadata.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_fileLock)
        {
            if (_loaded) return;
            try
            {
                string path = MetadataFilePath;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, GameMetaRecord>>(json, JsonOptions);
                    if (dict != null)
                    {
                        foreach (var kv in dict)
                        {
                            _store[kv.Key] = kv.Value;
                        }
                    }
                }
            }
            catch { }
            finally
            {
                _loaded = true;
            }
        }
    }

    private static void Save()
    {
        lock (_fileLock)
        {
            try
            {
                string path = MetadataFilePath;
                string dir = Path.GetDirectoryName(path) ?? "";
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var snapshot = new Dictionary<string, GameMetaRecord>(_store, StringComparer.OrdinalIgnoreCase);
                string json = JsonSerializer.Serialize(snapshot, JsonOptions);
                File.WriteAllText(path, json);
            }
            catch { }
        }
    }

    public static string NormalizeKey(string nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath)) return "";
        string clean = Path.GetFileNameWithoutExtension(nameOrPath);
        return CheatManager.NormalizeGameKey(clean);
    }

    public static GameMetaRecord GetOrCreate(string gameNameOrPath)
    {
        EnsureLoaded();
        string key = NormalizeKey(gameNameOrPath);
        if (string.IsNullOrEmpty(key)) return new GameMetaRecord();

        return _store.GetOrAdd(key, _ => new GameMetaRecord());
    }

    public static void RecordLaunch(string gameName)
    {
        if (string.IsNullOrWhiteSpace(gameName)) return;
        EnsureLoaded();

        string key = NormalizeKey(gameName);
        if (string.IsNullOrEmpty(key)) return;

        var rec = _store.GetOrAdd(key, _ => new GameMetaRecord());
        lock (rec)
        {
            rec.LaunchCount++;
            rec.LastPlayedIso = DateTime.UtcNow.ToString("o");
            if (rec.CompletionStatus == "Не начато")
            {
                rec.CompletionStatus = "Играю сейчас";
            }
        }
        Save();
    }

    public static void RecordSession(string gameName, long elapsedSeconds)
    {
        if (string.IsNullOrWhiteSpace(gameName) || elapsedSeconds <= 0) return;
        EnsureLoaded();

        string key = NormalizeKey(gameName);
        if (string.IsNullOrEmpty(key)) return;

        var rec = _store.GetOrAdd(key, _ => new GameMetaRecord());
        lock (rec)
        {
            rec.TotalPlaySeconds += elapsedSeconds;
            rec.LastPlayedIso = DateTime.UtcNow.ToString("o");
            if (rec.CompletionStatus == "Не начато")
            {
                rec.CompletionStatus = "Играю сейчас";
            }
        }
        Save();
    }

    public static void ApplyToRomItem(RomItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Name)) return;
        var rec = GetOrCreate(item.Name);
        if (rec == null) return;

        item.TotalPlaySeconds = rec.TotalPlaySeconds;
        item.LaunchCount = rec.LaunchCount;
        item.CompletionStatus = string.IsNullOrWhiteSpace(rec.CompletionStatus) ? "Не начато" : rec.CompletionStatus;
        item.StarRating = Math.Clamp(rec.StarRating, 0, 5);
        item.UserNotes = rec.UserNotes ?? "";
        item.CustomCollection = rec.CustomCollection ?? "";

        if (!string.IsNullOrEmpty(rec.LastPlayedIso) && DateTime.TryParse(rec.LastPlayedIso, out var dt))
        {
            item.LastPlayed = dt.ToLocalTime();
        }
    }

    public static void UpdateFromRomItem(RomItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Name)) return;
        EnsureLoaded();

        string key = NormalizeKey(item.Name);
        if (string.IsNullOrEmpty(key)) return;

        var rec = _store.GetOrAdd(key, _ => new GameMetaRecord());
        lock (rec)
        {
            rec.TotalPlaySeconds = item.TotalPlaySeconds;
            rec.LaunchCount = item.LaunchCount;
            rec.CompletionStatus = item.CompletionStatus;
            rec.StarRating = item.StarRating;
            rec.UserNotes = item.UserNotes;
            rec.CustomCollection = item.CustomCollection;
            if (item.LastPlayed.HasValue)
            {
                rec.LastPlayedIso = item.LastPlayed.Value.ToUniversalTime().ToString("o");
            }
        }
        Save();
    }

    public static string FormatPlaytime(long totalSeconds)
    {
        if (totalSeconds <= 0) return "Не запускалась";
        if (totalSeconds < 60) return "< 1 мин";
        if (totalSeconds < 3600) return $"{totalSeconds / 60} мин";

        long hours = totalSeconds / 3600;
        long mins = (totalSeconds % 3600) / 60;
        return mins > 0 ? $"{hours} ч {mins} мин" : $"{hours} ч";
    }
}
