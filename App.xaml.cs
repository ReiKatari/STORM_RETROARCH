using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace StormRetroArch;

public partial class App : Application
{
    private static void LogCrash(string content)
    {
        try
        {
            string targetDir = !string.IsNullOrEmpty(CoreManager.AppDir) && Directory.Exists(CoreManager.AppDir)
                ? CoreManager.AppDir
                : (AppContext.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory);
            File.WriteAllText(Path.Combine(targetDir, "crash.log"), content);
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
        {
            LogCrash(ev.ExceptionObject?.ToString() ?? "Unknown unhandled exception");
        };
        this.DispatcherUnhandledException += (s, ev) =>
        {
            LogCrash(ev.Exception?.ToString() ?? "Unknown dispatcher exception");
        };

        try
        {
            int currentPid = Environment.ProcessId;
            foreach (var proc in Process.GetProcessesByName("STORM RETROARCH"))
            {
                if (proc.Id != currentPid)
                {
                    try { proc.Kill(true); proc.WaitForExit(1500); } catch { }
                }
            }
        }
        catch { }

        base.OnStartup(e);
        try
        {
            SettingsManager.Load();
            LocalizationManager.InitSystemLanguage();
            if (!string.IsNullOrEmpty(SettingsManager.Current.LanguageCode))
            {
                LocalizationManager.SetLanguage(SettingsManager.Current.LanguageCode);
            }
            ThemeManager.ApplyTheme(
                SettingsManager.Current.ThemeName,
                SettingsManager.Current.AccentIndex,
                SettingsManager.Current.FontIndex);
        }
        catch (Exception ex)
        {
            LogCrash(ex.ToString());
        }
    }
}
