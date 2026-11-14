using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace StormRetroArch;

/// <summary>
/// Structured media directory resolver for STORM RetroArch.
/// Resolves screenshot and video recording paths organized by canonical platform category, platform, and game title.
/// </summary>
public static class MediaPathResolver
{
    private static readonly string[] StormCategories = new[]
    {
        "CONSOLES",
        "HANDHELDS",
        "PERSONAL COMPUTERS",
        "ARCADES",
        "GAME ENGINES",
        "MOBILES",
        "VR AR HEADSETS",
        "CALCULATORS",
        "MEDIA CENTERS",
        "SMART TVS",
        "TV BOXES",
        "WEARABLES",
        "WEB BROWSERS"
    };

    /// <summary>
    /// Resolves canonical category from romFilePath directory structure or deterministic platform mapping.
    /// </summary>
    public static string ResolvePlatformType(string? platform, string? romFilePath)
    {
        // 1. Inspect romFilePath segments for canonical STORM category names first
        if (!string.IsNullOrWhiteSpace(romFilePath))
        {
            try
            {
                string norm = romFilePath.Replace('/', '\\');
                string[] parts = norm.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string part in parts)
                {
                    string pUpper = part.Trim().ToUpperInvariant();
                    foreach (string cat in StormCategories)
                    {
                        if (pUpper.Equals(cat, StringComparison.OrdinalIgnoreCase))
                        {
                            return cat;
                        }
                    }
                }
            }
            catch { }
        }

        // 2. Deterministic platform mapping fallback
        if (string.IsNullOrWhiteSpace(platform))
        {
            return "CONSOLES";
        }

        string p = platform.Trim().ToLowerInvariant();

        // Handhelds
        if (p.Contains("game boy") || p.Contains("gb") || p.Contains("gba") || p.Contains("ds") ||
            p.Contains("3ds") || p.Contains("psp") || p.Contains("vita") || p.Contains("handheld") ||
            p.Contains("pocket") || p.Contains("wonderswan") || p.Contains("game gear") ||
            p.Contains("lynx") || p.Contains("supervision") || p.Contains("pokemon mini") || p.Contains("pokémon mini"))
        {
            return "HANDHELDS";
        }

        // Arcades
        if (p.Contains("arcade") || p.Contains("mame") || p.Contains("fbneo") || p.Contains("neo geo") ||
            p.Contains("cps") || p.Contains("naomi") || p.Contains("atomiswave") || p.Contains("finalburn"))
        {
            return "ARCADES";
        }

        // Personal Computers
        if (p.Contains("dos") || p.Contains("pc-98") || p.Contains("pc-88") || p.Contains("x68000") ||
            p.Contains("fm towns") || p.Contains("c64") || p.Contains("commodore") || p.Contains("amiga") ||
            p.Contains("atari st") || p.Contains("zx spectrum") || p.Contains("msx") || p.Contains("amstrad") ||
            p.Contains("apple") || p.Contains("mac") || p.Contains("computer") || p.Contains("windows") ||
            p.Contains("pc engine") || p.Contains("turbografx"))
        {
            if (p.Contains("pc engine") || p.Contains("turbografx"))
            {
                return "CONSOLES";
            }
            return "PERSONAL COMPUTERS";
        }

        // Game Engines
        if (p.Contains("scumm") || p.Contains("engine") || p.Contains("pico") || p.Contains("tic-80") ||
            p.Contains("doom") || p.Contains("quake") || p.Contains("rpg maker") || p.Contains("chip-8") ||
            p.Contains("wolfenstein"))
        {
            return "GAME ENGINES";
        }

        // Mobiles
        if (p.Contains("android") || p.Contains("ios") || p.Contains("j2me") || p.Contains("symbian") || p.Contains("n-gage"))
        {
            return "MOBILES";
        }

        // VR AR Headsets
        if (p.Contains("virtual boy") || p.Contains("vr") || p.Contains("quest"))
        {
            return "VR AR HEADSETS";
        }

        // Calculators
        if (p.Contains("calculator") || p.Contains("ti-8") || p.Contains("mk-52"))
        {
            return "CALCULATORS";
        }

        // Media Centers
        if (p.Contains("media center") || p.Contains("kodi"))
        {
            return "MEDIA CENTERS";
        }

        // Default to Consoles
        return "CONSOLES";
    }

    public static string GetScreenshotsRoot()
    {
        string root = Path.Combine(SettingsManager.AppRootDir, "screenshots");
        try
        {
            if (!Directory.Exists(root)) Directory.CreateDirectory(root);
        }
        catch { }
        return root;
    }

    public static string GetVideosRoot()
    {
        string root = Path.Combine(SettingsManager.AppRootDir, "videos");
        try
        {
            if (!Directory.Exists(root)) Directory.CreateDirectory(root);
        }
        catch { }
        return root;
    }

    public static string CleanGameTitleForFolder(string? gameTitle)
    {
        if (string.IsNullOrWhiteSpace(gameTitle))
        {
            return "Gameplay";
        }

        string clean = gameTitle.Trim();
        clean = Regex.Replace(clean, @"\s*\([^)]*\)", "");
        clean = Regex.Replace(clean, @"\s*\[[^\]]*\]", "");
        clean = clean.Trim();

        if (string.IsNullOrWhiteSpace(clean))
        {
            clean = gameTitle.Trim();
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();
        clean = string.Join("_", clean.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "Gameplay" : clean;
    }

    private static string SanitizeFolderName(string? name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return fallback;
        }
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string clean = string.Join("_", name.Trim().Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
    }

    public static string GetScreenshotSaveDir(string? platform, string? gameTitle, string? romPath)
    {
        string platformType = ResolvePlatformType(platform, romPath);
        string safePlatform = SanitizeFolderName(platform, "General");
        string safeGameTitle = CleanGameTitleForFolder(gameTitle);

        string dir = Path.Combine(GetScreenshotsRoot(), platformType, safePlatform, safeGameTitle);
        try
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
        catch { }
        return dir;
    }

    public static string GetVideoSaveDir(string? platform, string? gameTitle, string? romPath)
    {
        string platformType = ResolvePlatformType(platform, romPath);
        string safePlatform = SanitizeFolderName(platform, "General");
        string safeGameTitle = CleanGameTitleForFolder(gameTitle);

        string dir = Path.Combine(GetVideosRoot(), platformType, safePlatform, safeGameTitle);
        try
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
        catch { }
        return dir;
    }

    public static List<string> ListMediaEntries(string rootDir, string[] extensions)
    {
        var list = new List<string>();
        if (!Directory.Exists(rootDir)) return list;
        try
        {
            var exts = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
            var opt = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            foreach (var file in Directory.EnumerateFiles(rootDir, "*.*", opt))
            {
                if (exts.Contains(Path.GetExtension(file)))
                {
                    list.Add(file);
                }
            }
        }
        catch { }
        return list;
    }

    public static void MigrateLegacyMediaIfNeeded()
    {
        try
        {
            string legacyRecordings = Path.Combine(SettingsManager.AppRootDir, "recordings");
            if (Directory.Exists(legacyRecordings))
            {
                string videosRoot = GetVideosRoot();
                var files = Directory.GetFiles(legacyRecordings);
                foreach (var f in files)
                {
                    try
                    {
                        string dest = Path.Combine(videosRoot, Path.GetFileName(f));
                        if (!File.Exists(dest)) File.Move(f, dest);
                    }
                    catch { }
                }
            }
        }
        catch { }
    }
}
