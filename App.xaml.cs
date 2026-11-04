using System;
using System.Windows;

namespace StormRetroArch;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
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
            System.Diagnostics.Debug.WriteLine($"[App.OnStartup Error]: {ex.Message}");
        }
    }
}
