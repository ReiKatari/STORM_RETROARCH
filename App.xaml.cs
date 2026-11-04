using System;
using System.IO;
using System.Windows;

namespace StormRetroArch;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
        {
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), ev.ExceptionObject.ToString()); } catch { }
        };

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
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), ex.ToString()); } catch { }
        }
    }
}
