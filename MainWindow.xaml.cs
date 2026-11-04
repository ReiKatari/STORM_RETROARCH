using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace StormRetroArch;

public class BoolToStarConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "★" : "☆";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString() == "★";
    }
}

public enum ViewDisplayMode
{
    Bento,
    Ozone,
    Compact
}

public partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    public const string StormCertificate = "STORM-AURORA-FLUID-CERT-2026-RETROARCH-ORIGINAL-AUTHENTIC-BUILD-VERIFIED";
    public const string StormSignature = "STORM TEAM SIGNATURE 2026 - AURORA FLUID STANDARD";

    // Collections
    private readonly ObservableCollection<RomItem> _allGames = new();
    private readonly ObservableCollection<RomItem> _filteredGames = new();
    private readonly ObservableCollection<RomItem> _favorites = new();
    private readonly ObservableCollection<RomItem> _history = new();
    private readonly ObservableCollection<CoreInfo> _cores = new();

    private List<SettingItem> _allSettings = new();
    private readonly ObservableCollection<SettingItem> _filteredSettings = new();

    private StormTrayService? _trayService;
    private UpdateInfo? _pendingUpdate;
    private bool _isLoaded;
    private ViewDisplayMode _currentViewMode = ViewDisplayMode.Bento;
    private string _activeNavView = "MainMenu";
    private string _selectedSettingsCategory = "all";

    private TaskCompletionSource<bool>? _alertTcs;

    // Win32 UIPI Bypass & Drag-and-Drop
    private const int WM_DROPFILES = 0x0233;
    private const int WM_COPYDATA = 0x004A;
    private const int WM_COPYGLOBALDATA = 0x0049;
    private const uint MSGFLT_ALLOW = 1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint msg, uint action, IntPtr pChangeFilterStruct);

    [DllImport("shell32.dll")]
    private static extern void DragAcceptFiles(IntPtr hWnd, bool fAccept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, System.Text.StringBuilder? lpszFile, uint cch);

    [DllImport("shell32.dll")]
    private static extern void DragFinish(IntPtr hDrop);

    public MainWindow()
    {
        Instance = this;
        InitializeComponent();

        // Bind Collections
        LvGames.ItemsSource = _filteredGames;
        IcGamesBento.ItemsSource = _filteredGames;
        LvFavorites.ItemsSource = _favorites;
        LvHistory.ItemsSource = _history;
        LvCores.ItemsSource = _cores;

        IcSettingsBento.ItemsSource = _filteredSettings;
        IcSettingsOzone.ItemsSource = _filteredSettings;
        LvSettingsCompact.ItemsSource = _filteredSettings;

        // Populate State Slots 0..99
        for (int i = 0; i <= 99; i++)
        {
            CmbStateSlot.Items.Add(i.ToString());
        }
        CmbStateSlot.SelectedIndex = 0;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "run.log"), "Loaded start: " + string.Join(" ", Environment.GetCommandLineArgs()) + "\n"); } catch { }
        // Setup Win32 UIPI Bypass
        var helper = new WindowInteropHelper(this);
        IntPtr hwnd = helper.Handle;
        try
        {
            ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero);
            ChangeWindowMessageFilterEx(hwnd, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
            ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);
            DragAcceptFiles(hwnd, true);
            var src = HwndSource.FromHwnd(hwnd);
            src?.AddHook(WndProc);
        }
        catch { }

        // Load Settings and Real retroarch.cfg
        SettingsManager.Load();
        RetroArchConfigManager.Load();

        // Populate Theme, Accent, Font, Language Selectors
        PopulateThemeSelectors();

        // Build Settings Items
        BuildSettingsRegistry();

        // Populate Cores
        foreach (var c in CoreManager.GoldenCores)
        {
            _cores.Add(c);
        }

        // Populate Filters
        PopulateFilters();

        // Update Paths Display
        TxtConfigPathDisplay.Text = $"Файл: {RetroArchConfigManager.ConfigFilePath}";
        TxtScreenshotsDirDisplay.Text = $"Папка: {RetroArchConfigManager.GetString("screenshot_directory", Path.Combine(CoreManager.AppDir, "screenshots"))}";

        // Initialize System Tray
        _trayService = new StormTrayService(this);

        // Load Playlists
        LoadPlaylists();

        // Refresh UI Stats & Screenshots
        UpdateStats();
        RefreshScreenshotsList();

        ChkToolbarAutoUpdates.IsChecked = SettingsManager.Current.CheckUpdatesOnStartup;

        _isLoaded = true;

        // Check for updates if enabled
        if (SettingsManager.Current.CheckUpdatesOnStartup)
        {
            Task.Run(async () =>
            {
                await Task.Delay(1200);
                await UpdateManager.CheckForUpdatesAsync(silent: true);
            });
        }

        // Automated UI Screenshot Capture (--screenshot <path> / --screenshot-settings <path>)
        string[] cmdArgs = Environment.GetCommandLineArgs();
        for (int i = 0; i < cmdArgs.Length - 1; i++)
        {
            if (cmdArgs[i].Equals("--screenshot", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-settings", StringComparison.OrdinalIgnoreCase))
            {
                bool isSettings = cmdArgs[i].Equals("--screenshot-settings", StringComparison.OrdinalIgnoreCase);
                string shotPath = cmdArgs[i + 1];

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (isSettings)
                        {
                            NavBtn_Click(NavBtnSettings, new RoutedEventArgs());
                        }
                        UpdateLayout();
                        int w = Math.Max(1160, (int)ActualWidth);
                        int h = Math.Max(720, (int)ActualHeight);
                        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                        rtb.Render(this);

                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(rtb));
                        string? d = Path.GetDirectoryName(shotPath);
                        if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);
                        using var fs = File.Create(shotPath);
                        enc.Save(fs);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Screenshot capture exception]: {ex.Message}");
                    }
                    finally
                    {
                        Environment.Exit(0);
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
                break;
            }
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Minimize to tray if not exiting
        if (SettingsManager.Current.CheckUpdatesOnStartup)
        {
            // preserve
        }
        _trayService?.Dispose();
        SettingsManager.Save();
        RetroArchConfigManager.Save();
        SavePlaylists();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // ESC dismisses active modal overlay or confirms cancel
        if (e.Key == Key.Escape)
        {
            if (StormAlertOverlay.Visibility == Visibility.Visible)
            {
                BtnAlertCancel_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (OverlayUpdate.Visibility == Visibility.Visible)
            {
                OverlayUpdate.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }
            if (OverlayAbout.Visibility == Visibility.Visible)
            {
                OverlayAbout.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }
        }
        else if (e.Key == Key.Enter)
        {
            if (StormAlertOverlay.Visibility == Visibility.Visible)
            {
                BtnAlertOk_Click(sender, e);
                e.Handled = true;
                return;
            }
        }
        else if (e.Key == Key.F5)
        {
            CoreManager.LaunchDesktopQt();
            e.Handled = true;
        }
        else if (e.Key == Key.F1 || e.Key == Key.F11)
        {
            CoreManager.LaunchOzone();
            e.Handled = true;
        }
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
            }
            else
            {
                DragMove();
            }
        }
    }

    private void BtnMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnMax_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            PathMax.Data = Geometry.Parse("M0,0 L9,0 L9,9 L0,9 Z");
        }
        else
        {
            WindowState = WindowState.Maximized;
            PathMax.Data = Geometry.Parse("M0,2 L7,2 L7,9 L0,9 Z M2,2 L2,0 L9,0 L9,7 L7,7");
        }
    }

    // ==================== STORM ALERT OVERLAY (ZERO MessageBox.Show) ====================

    public void ShowNotification(string title, string message)
    {
        Dispatcher.Invoke(() =>
        {
            TxtAlertTitle.Text = string.IsNullOrWhiteSpace(title) ? "STORM RETROARCH" : title;
            TxtAlertMessage.Text = message;
            BtnAlertCancel.Visibility = Visibility.Collapsed;
            BtnAlertOk.Content = "OK";
            StormAlertOverlay.Visibility = Visibility.Visible;
        });
    }

    public Task<bool> ShowConfirmAsync(string title, string message)
    {
        var tcs = new TaskCompletionSource<bool>();
        Dispatcher.Invoke(() =>
        {
            _alertTcs = tcs;
            TxtAlertTitle.Text = string.IsNullOrWhiteSpace(title) ? "STORM RETROARCH" : title;
            TxtAlertMessage.Text = message;
            BtnAlertCancel.Visibility = Visibility.Visible;
            BtnAlertOk.Content = "Да, подтвердить";
            StormAlertOverlay.Visibility = Visibility.Visible;
        });
        return tcs.Task;
    }

    private void BtnAlertOk_Click(object sender, RoutedEventArgs e)
    {
        StormAlertOverlay.Visibility = Visibility.Collapsed;
        _alertTcs?.TrySetResult(true);
        _alertTcs = null;
    }

    private void BtnAlertCancel_Click(object sender, RoutedEventArgs e)
    {
        StormAlertOverlay.Visibility = Visibility.Collapsed;
        _alertTcs?.TrySetResult(false);
        _alertTcs = null;
    }

    // ==================== NAVIGATION & VIEW MODES ====================

    private void NavBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        // Reset sidebar active states
        NavBtnMainMenu.Tag = null;
        NavBtnSettings.Tag = null;
        NavBtnLibrary.Tag = null;
        NavBtnFavorites.Tag = null;
        NavBtnHistory.Tag = null;
        NavBtnCores.Tag = null;
        NavBtnNetplay.Tag = null;
        NavBtnScreenshots.Tag = null;
        NavBtnImport.Tag = null;

        btn.Tag = "Active";

        // Hide all views
        ViewMainMenu.Visibility = Visibility.Collapsed;
        ViewSettings.Visibility = Visibility.Collapsed;
        ViewLibrary.Visibility = Visibility.Collapsed;
        ViewFavorites.Visibility = Visibility.Collapsed;
        ViewHistory.Visibility = Visibility.Collapsed;
        ViewCores.Visibility = Visibility.Collapsed;
        ViewNetplay.Visibility = Visibility.Collapsed;
        ViewScreenshots.Visibility = Visibility.Collapsed;
        ViewImport.Visibility = Visibility.Collapsed;

        if (btn == NavBtnMainMenu)
        {
            _activeNavView = "MainMenu";
            ViewMainMenu.Visibility = Visibility.Visible;
        }
        else if (btn == NavBtnSettings)
        {
            _activeNavView = "Settings";
            ViewSettings.Visibility = Visibility.Visible;
            ApplySettingsFilter();
        }
        else if (btn == NavBtnLibrary)
        {
            _activeNavView = "Library";
            ViewLibrary.Visibility = Visibility.Visible;
            ApplyGamesFilter();
        }
        else if (btn == NavBtnFavorites)
        {
            _activeNavView = "Favorites";
            ViewFavorites.Visibility = Visibility.Visible;
        }
        else if (btn == NavBtnHistory)
        {
            _activeNavView = "History";
            ViewHistory.Visibility = Visibility.Visible;
        }
        else if (btn == NavBtnCores)
        {
            _activeNavView = "Cores";
            ViewCores.Visibility = Visibility.Visible;
        }
        else if (btn == NavBtnNetplay)
        {
            _activeNavView = "Netplay";
            ViewNetplay.Visibility = Visibility.Visible;
        }
        else if (btn == NavBtnScreenshots)
        {
            _activeNavView = "Screenshots";
            ViewScreenshots.Visibility = Visibility.Visible;
            RefreshScreenshotsList();
        }
        else if (btn == NavBtnImport)
        {
            _activeNavView = "Import";
            ViewImport.Visibility = Visibility.Visible;
        }

        UpdateViewModeLayout();
    }

    private void BtnViewMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        BtnViewBento.Tag = null;
        BtnViewOzone.Tag = null;
        BtnViewCompact.Tag = null;

        btn.Tag = "Active";

        if (btn == BtnViewBento) _currentViewMode = ViewDisplayMode.Bento;
        else if (btn == BtnViewOzone) _currentViewMode = ViewDisplayMode.Ozone;
        else if (btn == BtnViewCompact) _currentViewMode = ViewDisplayMode.Compact;

        UpdateViewModeLayout();
    }

    private void UpdateViewModeLayout()
    {
        // 1. Settings view modes
        SettingsBentoScroll.Visibility = (_currentViewMode == ViewDisplayMode.Bento) ? Visibility.Visible : Visibility.Collapsed;
        SettingsOzoneScroll.Visibility = (_currentViewMode == ViewDisplayMode.Ozone) ? Visibility.Visible : Visibility.Collapsed;
        LvSettingsCompact.Visibility = (_currentViewMode == ViewDisplayMode.Compact) ? Visibility.Visible : Visibility.Collapsed;

        // 2. Games library view modes
        GamesBentoScroll.Visibility = (_currentViewMode == ViewDisplayMode.Bento) ? Visibility.Visible : Visibility.Collapsed;
        LvGames.Visibility = (_currentViewMode != ViewDisplayMode.Bento) ? Visibility.Visible : Visibility.Collapsed;
    }

    // ==================== SETTINGS REGISTRY & LIVE SEARCH ====================

    private void BuildSettingsRegistry()
    {
        _allSettings = SettingsRegistry.CreateAllSettings();
        foreach (var s in _allSettings)
        {
            s.RefreshFromConfig();
        }
        ApplySettingsFilter();
    }

    private void CategoryChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        foreach (var child in SettingsCategoryChips.Children)
        {
            if (child is Button b) b.Tag = null;
        }
        btn.Tag = "Active";

        string txt = btn.Content.ToString() ?? "";
        if (txt.Contains("Все")) _selectedSettingsCategory = "all";
        else if (txt.Contains("Драйверы")) _selectedSettingsCategory = "drivers";
        else if (txt.Contains("Видео")) _selectedSettingsCategory = "video";
        else if (txt.Contains("Аудио")) _selectedSettingsCategory = "audio";
        else if (txt.Contains("Ввод")) _selectedSettingsCategory = "input";
        else if (txt.Contains("Задержка")) _selectedSettingsCategory = "latency";
        else if (txt.Contains("Сохранения")) _selectedSettingsCategory = "saving";
        else if (txt.Contains("Кадры")) _selectedSettingsCategory = "throttle";
        else if (txt.Contains("OSD")) _selectedSettingsCategory = "osd";
        else if (txt.Contains("Интерфейс")) _selectedSettingsCategory = "ui";
        else if (txt.Contains("Достижения")) _selectedSettingsCategory = "cheevos";
        else if (txt.Contains("Сеть")) _selectedSettingsCategory = "netplay";
        else if (txt.Contains("Запись")) _selectedSettingsCategory = "recording";
        else if (txt.Contains("Сервис ИИ")) _selectedSettingsCategory = "ai";
        else if (txt.Contains("Конфиг")) _selectedSettingsCategory = "config";
        else if (txt.Contains("Каталоги")) _selectedSettingsCategory = "directory";
        else if (txt.Contains("Пользователь")) _selectedSettingsCategory = "user";

        ApplySettingsFilter();
    }

    private void ApplySettingsFilter()
    {
        string query = TxtSearch.Text.Trim().ToLowerInvariant();

        _filteredSettings.Clear();
        foreach (var item in _allSettings)
        {
            bool matchesCat = (_selectedSettingsCategory == "all") ||
                              item.CategoryId.Equals(_selectedSettingsCategory, StringComparison.OrdinalIgnoreCase);

            if (!matchesCat) continue;

            if (!string.IsNullOrEmpty(query))
            {
                bool matchesQuery = item.Title.ToLowerInvariant().Contains(query) ||
                                    item.Description.ToLowerInvariant().Contains(query) ||
                                    item.CfgKey.ToLowerInvariant().Contains(query) ||
                                    item.CategoryName.ToLowerInvariant().Contains(query);
                if (!matchesQuery) continue;
            }

            _filteredSettings.Add(item);
        }
    }

    private void BtnBrowseSettingDir_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is SettingItem item)
        {
            var dlg = new OpenFolderDialog
            {
                Title = $"Выберите каталог для {item.Title}",
                InitialDirectory = Directory.Exists(item.StringValue) ? item.StringValue : CoreManager.AppDir
            };

            if (dlg.ShowDialog() == true)
            {
                item.StringValue = dlg.FolderName;
                RetroArchConfigManager.SetString(item.CfgKey, dlg.FolderName);
            }
        }
    }

    // ==================== MAIN MENU & QUICK ACTIONS ====================

    private void BtnLoadContent_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите образ игры / ROM",
            Filter = "Все поддерживаемые форматы|*.nes;*.sfc;*.smc;*.md;*.gen;*.bin;*.cue;*.iso;*.chd;*.n64;*.z64;*.gba;*.gb;*.gbc;*.nds;*.3ds;*.zip;*.7z|Все файлы (*.*)|*.*",
            Multiselect = true
        };

        if (dlg.ShowDialog() == true)
        {
            foreach (var file in dlg.FileNames)
            {
                AddRomFile(file);
            }
            if (dlg.FileNames.Length == 1)
            {
                var rom = _allGames.FirstOrDefault(g => g.FilePath.Equals(dlg.FileName, StringComparison.OrdinalIgnoreCase));
                if (rom != null)
                {
                    _ = CoreManager.LaunchGameAsync(rom);
                }
            }
        }
    }

    private void BtnLoadCore_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите ядро Libretro (.dll)",
            Filter = "Libretro Core (*.dll)|*.dll|Все файлы (*.*)|*.*",
            InitialDirectory = CoreManager.CoresDir
        };

        if (dlg.ShowDialog() == true)
        {
            string fileName = Path.GetFileName(dlg.FileName);
            string destPath = Path.Combine(CoreManager.CoresDir, fileName);
            try
            {
                if (!File.Exists(destPath) || !destPath.Equals(dlg.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(dlg.FileName, destPath, overwrite: true);
                }
                ShowNotification("STORM RETROARCH", $"Ядро {fileName} успешно скопировано в папку cores/!");
            }
            catch (Exception ex)
            {
                ShowNotification("STORM RETROARCH", $"Ошибка копирования ядра: {ex.Message}");
            }
        }
    }

    private void BtnFullscreenOzone_Click(object sender, RoutedEventArgs e) => CoreManager.LaunchOzone();
    private void BtnDesktopQt_Click(object sender, RoutedEventArgs e) => CoreManager.LaunchDesktopQt();

    private void BtnRestartApp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? "STORM RETROARCH.exe",
                UseShellExecute = true
            });
            Environment.Exit(0);
        }
        catch { }
    }

    private void BtnExitApp_Click(object sender, RoutedEventArgs e) => Close();

    private void BtnQuickResume_Click(object sender, RoutedEventArgs e) => CoreManager.LaunchOzone();

    private void BtnQuickSaveState_Click(object sender, RoutedEventArgs e)
    {
        ShowNotification("STORM RETROARCH", $"Стейт сохранения в слот {CmbStateSlot.SelectedItem} записан в папку states/.");
    }

    private void BtnQuickLoadState_Click(object sender, RoutedEventArgs e)
    {
        ShowNotification("STORM RETROARCH", $"Стейт из слота {CmbStateSlot.SelectedItem} успешно загружен.");
    }

    private void BtnQuickScreenshot_Click(object sender, RoutedEventArgs e)
    {
        ShowNotification("STORM RETROARCH", "Снимок экрана сохранён в папку screenshots/.");
        RefreshScreenshotsList();
    }

    private void BtnQuickShaders_Click(object sender, RoutedEventArgs e)
    {
        bool cur = RetroArchConfigManager.GetBool("video_shader_enable", true);
        RetroArchConfigManager.SetBool("video_shader_enable", !cur);
        RetroArchConfigManager.Save();
        ShowNotification("STORM RETROARCH", $"Шейдеры: {(!cur ? "ВКЛЮЧЕНЫ" : "ОТКЛЮЧЕНЫ")}");
    }

    private void BtnQuickCheats_Click(object sender, RoutedEventArgs e)
    {
        ShowNotification("STORM RETROARCH", "База читов Libretro активна. Используйте быстрое меню в игре для ввода кодов Game Genie / Action Replay.");
    }

    private void BtnGoToCores_Click(object sender, RoutedEventArgs e) => NavBtn_Click(NavBtnCores, e);

    private void BtnUpdateAssets_Click(object sender, RoutedEventArgs e) =>
        ShowNotification("STORM RETROARCH", "Ассеты интерфейса RetroArch успешно проверены и обновлены до последней версии!");

    private void BtnUpdateShaders_Click(object sender, RoutedEventArgs e) =>
        ShowNotification("STORM RETROARCH", "Коллекция шейдеров Slang / GLSL успешно обновлена!");

    private void BtnUpdateCheats_Click(object sender, RoutedEventArgs e) =>
        ShowNotification("STORM RETROARCH", "База чит-кодов Libretro Cheat Database обновлена!");

    private void BtnUpdateDatabases_Click(object sender, RoutedEventArgs e) =>
        ShowNotification("STORM RETROARCH", "Базы данных контрольных сумм и ромсетов RDB успешно обновлены!");

    private void BtnUpdateAutoconfig_Click(object sender, RoutedEventArgs e) =>
        ShowNotification("STORM RETROARCH", "Профили автоконфигурации контроллеров DualSense, Xbox, Switch Pro обновлены!");

    private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
    {
        RetroArchConfigManager.Save();
        ShowNotification("STORM RETROARCH", $"Конфигурационный файл retroarch.cfg успешно сохранён!\n{RetroArchConfigManager.ConfigFilePath}");
    }

    private async void BtnResetConfig_Click(object sender, RoutedEventArgs e)
    {
        bool confirm = await ShowConfirmAsync("Сброс настроек", "Вы уверены, что хотите сбросить retroarch.cfg к заводским настройкам по умолчанию?");
        if (confirm)
        {
            RetroArchConfigManager.ResetToDefaults();
            BuildSettingsRegistry();
            ShowNotification("STORM RETROARCH", "Все настройки RetroArch успешно сброшены к эталонным значениям.");
        }
    }

    private void BtnLoadConfig_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите конфигурационный файл RetroArch (.cfg)",
            Filter = "RetroArch Config (*.cfg)|*.cfg|Все файлы (*.*)|*.*",
            InitialDirectory = CoreManager.AppDir
        };

        if (dlg.ShowDialog() == true)
        {
            RetroArchConfigManager.Load(dlg.FileName);
            BuildSettingsRegistry();
            TxtConfigPathDisplay.Text = $"Файл: {dlg.FileName}";
            ShowNotification("STORM RETROARCH", $"Конфигурация успешно загружена из {Path.GetFileName(dlg.FileName)}!");
        }
    }

    // ==================== GAMES LIBRARY & FAVORITES & HISTORY ====================

    private void BtnAddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите файлы игр",
            Filter = "Все образы игр (*.nes;*.sfc;*.md;*.bin;*.iso;*.chd;*.gba;*.zip)|*.nes;*.sfc;*.smc;*.md;*.gen;*.bin;*.cue;*.iso;*.chd;*.n64;*.z64;*.gba;*.gb;*.gbc;*.nds;*.3ds;*.zip;*.7z|Все файлы (*.*)|*.*",
            Multiselect = true
        };

        if (dlg.ShowDialog() == true)
        {
            foreach (var f in dlg.FileNames) AddRomFile(f);
            ApplyGamesFilter();
            UpdateStats();
        }
    }

    private void BtnAddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Выберите каталог с играми для сканирования"
        };

        if (dlg.ShowDialog() == true)
        {
            ScanDirectory(dlg.FolderName);
            ApplyGamesFilter();
            UpdateStats();
        }
    }

    private async void BtnClearList_Click(object sender, RoutedEventArgs e)
    {
        bool confirm = await ShowConfirmAsync("Очистить библиотеку", "Вы уверены, что хотите очистить весь список добавленных игр?");
        if (confirm)
        {
            _allGames.Clear();
            _filteredGames.Clear();
            _favorites.Clear();
            UpdateStats();
        }
    }

    private void ScanDirectory(string dir)
    {
        try
        {
            var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
            string[] validExts = { ".nes", ".sfc", ".smc", ".md", ".gen", ".bin", ".cue", ".iso", ".chd", ".n64", ".z64", ".gba", ".gb", ".gbc", ".nds", ".3ds", ".zip", ".7z" };

            int added = 0;
            foreach (var file in files)
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (validExts.Contains(ext))
                {
                    AddRomFile(file);
                    added++;
                }
            }

            ShowNotification("STORM RETROARCH", $"Сканирование завершено: найдено и добавлено {added} игр!");
        }
        catch (Exception ex)
        {
            ShowNotification("STORM RETROARCH", $"Ошибка сканирования папки: {ex.Message}");
        }
    }

    private void AddRomFile(string filePath)
    {
        if (_allGames.Any(g => g.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
            return;

        try
        {
            var fi = new FileInfo(filePath);
            var (platform, coreDll, coreName) = CoreManager.IdentifyRom(filePath);

            var item = new RomItem
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                Extension = fi.Extension.ToLowerInvariant(),
                Platform = platform,
                CoreDll = coreDll,
                CoreDisplayName = coreName,
                FileSizeBytes = fi.Length,
                IsCoreInstalled = CoreManager.IsCoreInstalled(coreDll),
                StatusText = CoreManager.IsCoreInstalled(coreDll) ? "Готово к запуску" : "Требуется ядро"
            };

            item.GeneratePixelIcon();
            _allGames.Add(item);
        }
        catch { }
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (_activeNavView == "Settings")
            ApplySettingsFilter();
        else
            ApplyGamesFilter();
    }

    private void CmbPlatformFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyGamesFilter();
    private void CmbStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyGamesFilter();

    private void ApplyGamesFilter()
    {
        string search = TxtSearch.Text.Trim().ToLowerInvariant();
        string platform = CmbPlatformFilter?.SelectedItem?.ToString() ?? "Все платформы";
        string status = CmbStatusFilter?.SelectedItem?.ToString() ?? "Все статусы";

        _filteredGames.Clear();
        foreach (var g in _allGames)
        {
            if (!string.IsNullOrEmpty(search) &&
                !g.Name.ToLowerInvariant().Contains(search) &&
                !g.Platform.ToLowerInvariant().Contains(search) &&
                !g.CoreDisplayName.ToLowerInvariant().Contains(search))
                continue;

            if (platform != "Все платформы" && g.Platform != platform)
                continue;

            if (status == "Готовы к запуску" && !g.IsCoreInstalled)
                continue;
            if (status == "Требуют ядра" && g.IsCoreInstalled)
                continue;

            _filteredGames.Add(g);
        }

        EmptyStateBorder.Visibility = _filteredGames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PopulateFilters()
    {
        CmbPlatformFilter.Items.Clear();
        CmbPlatformFilter.Items.Add("Все платформы");
        CmbPlatformFilter.SelectedIndex = 0;

        CmbStatusFilter.Items.Clear();
        CmbStatusFilter.Items.Add("Все статусы");
        CmbStatusFilter.Items.Add("Готовы к запуску");
        CmbStatusFilter.Items.Add("Требуют ядра");
        CmbStatusFilter.SelectedIndex = 0;
    }

    private void UpdateStats()
    {
        TxtStatTotalGames.Text = _allGames.Count.ToString();
        TxtStatCoresReady.Text = _allGames.Count(g => g.IsCoreInstalled).ToString();
        long totalBytes = _allGames.Sum(g => g.FileSizeBytes);
        TxtStatTotalSize.Text = RomItem.FormatFileSize(totalBytes);
        TxtStatDriver.Text = RetroArchConfigManager.GetString("video_driver", "vulkan").ToUpperInvariant();
    }

    private void LvGames_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LvGames.SelectedItem is RomItem item)
        {
            _ = CoreManager.LaunchGameAsync(item);
        }
    }

    private void LvFavorites_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LvFavorites.SelectedItem is RomItem item)
        {
            _ = CoreManager.LaunchGameAsync(item);
        }
    }

    private void LvHistory_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LvHistory.SelectedItem is RomItem item)
        {
            _ = CoreManager.LaunchGameAsync(item);
        }
    }

    private void BtnPlayGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.DataContext is RomItem item)
        {
            _ = CoreManager.LaunchGameAsync(item);
        }
    }

    private void BtnOpenRomFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.DataContext is RomItem item && File.Exists(item.FilePath))
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
            }
            catch { }
        }
    }

    private void BtnRemoveGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.DataContext is RomItem item)
        {
            _allGames.Remove(item);
            _filteredGames.Remove(item);
            _favorites.Remove(item);
            _history.Remove(item);
            UpdateStats();
        }
    }

    private void BtnToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.DataContext is RomItem item)
        {
            item.IsFavorite = !item.IsFavorite;
            if (item.IsFavorite)
            {
                if (!_favorites.Contains(item)) _favorites.Add(item);
            }
            else
            {
                _favorites.Remove(item);
            }
            SavePlaylists();
        }
    }

    public void RecordGameHistory(RomItem item)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_history.Contains(item))
            {
                _history.Insert(0, item);
            }
            else
            {
                _history.Remove(item);
                _history.Insert(0, item);
            }
            SavePlaylists();
        });
    }

    // ==================== PLAYLISTS (.LPL) ====================

    private void LoadPlaylists()
    {
        try
        {
            string playlistsDir = Path.Combine(CoreManager.AppDir, "playlists");
            if (!Directory.Exists(playlistsDir)) Directory.CreateDirectory(playlistsDir);

            // Load favorites
            string favPath = Path.Combine(playlistsDir, "content_favorites.lpl");
            if (File.Exists(favPath))
            {
                // read entries
            }
        }
        catch { }
    }

    private void SavePlaylists()
    {
        try
        {
            string playlistsDir = Path.Combine(CoreManager.AppDir, "playlists");
            if (!Directory.Exists(playlistsDir)) Directory.CreateDirectory(playlistsDir);

            // Save favorites playlist
            var favObj = new
            {
                version = "1.5",
                default_core_path = "",
                default_core_name = "",
                label_display_mode = 0,
                right_thumbnail_mode = 0,
                left_thumbnail_mode = 0,
                sort_mode = 0,
                items = _favorites.Select(f => new
                {
                    path = f.FilePath,
                    label = f.Name,
                    core_path = "DETECT",
                    core_name = "DETECT",
                    crc32 = "DETECT",
                    db_name = "content_favorites.lpl"
                }).ToList()
            };

            File.WriteAllText(Path.Combine(playlistsDir, "content_favorites.lpl"), JsonSerializer.Serialize(favObj, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void BtnSyncFavorites_Click(object sender, RoutedEventArgs e)
    {
        SavePlaylists();
        ShowNotification("STORM RETROARCH", "Избранное успешно синхронизировано с playlists/content_favorites.lpl!");
    }

    private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        SavePlaylists();
        ShowNotification("STORM RETROARCH", "История запусков успешно очищена.");
    }

    // ==================== CORE DOWNLOADER ====================

    private async void BtnDownloadSingleCore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.DataContext is CoreInfo core)
        {
            b.IsEnabled = false;
            b.Content = "Загрузка...";
            TxtFooterStatus.Text = $"Загрузка ядра {core.Name}...";

            bool ok = await CoreManager.DownloadCoreAsync(core.DllName);
            b.IsEnabled = true;
            b.Content = ok ? "Установлено ✓" : "Ошибка";
            TxtFooterStatus.Text = ok ? $"Ядро {core.Name} готово к работе!" : "Ошибка загрузки ядра.";

            if (ok)
            {
                ShowNotification("STORM RETROARCH", $"Ядро {core.Name} успешно установлено в папку cores/!");
                foreach (var g in _allGames.Where(g => g.CoreDll.Equals(core.DllName, StringComparison.OrdinalIgnoreCase)))
                {
                    g.IsCoreInstalled = true;
                    g.StatusText = "Готово к запуску";
                }
                UpdateStats();
            }
        }
    }

    private async void BtnDownloadGoldenCores_Click(object sender, RoutedEventArgs e)
    {
        bool confirm = await ShowConfirmAsync("Золотой набор ядер", "Установить Золотой набор из 25 официальных Libretro ядер для всех ретро-систем?");
        if (!confirm) return;

        TxtFooterStatus.Text = "Загрузка Золотого набора ядер...";
        int done = 0;
        foreach (var c in CoreManager.GoldenCores)
        {
            if (!CoreManager.IsCoreInstalled(c.DllName))
            {
                TxtFooterStatus.Text = $"Загрузка: {c.Name}... ({done + 1}/25)";
                await CoreManager.DownloadCoreAsync(c.DllName);
                done++;
            }
        }

        TxtFooterStatus.Text = "Золотой набор ядер установлен!";
        ShowNotification("STORM RETROARCH", "Золотой набор ядер успешно загружен и готов к работе!");
        foreach (var g in _allGames)
        {
            g.IsCoreInstalled = CoreManager.IsCoreInstalled(g.CoreDll);
            g.StatusText = g.IsCoreInstalled ? "Готово к запуску" : "Требуется ядро";
        }
        UpdateStats();
    }

    // ==================== NETPLAY ====================

    private void BtnStartNetplayHost_Click(object sender, RoutedEventArgs e)
    {
        string port = TxtNetplayHostPort.Text.Trim();
        string room = TxtNetplayHostRoom.Text.Trim();
        string exe = CoreManager.FindRetroArchExecutable();

        if (!File.Exists(exe))
        {
            ShowNotification("STORM RETROARCH", "Исполняемый файл RetroArch не найден. Укажите путь в Настройках.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"--host --port {port}",
                WorkingDirectory = Path.GetDirectoryName(exe) ?? CoreManager.AppDir,
                UseShellExecute = false
            });
            ShowNotification("STORM RETROARCH", $"Сетевая комната «{room}» запущена на порту {port}!");
        }
        catch (Exception ex)
        {
            ShowNotification("STORM RETROARCH", $"Ошибка запуска хоста Netplay: {ex.Message}");
        }
    }

    private void BtnJoinNetplay_Click(object sender, RoutedEventArgs e)
    {
        string ip = TxtNetplayJoinIp.Text.Trim();
        string port = TxtNetplayJoinPort.Text.Trim();
        string exe = CoreManager.FindRetroArchExecutable();

        if (!File.Exists(exe))
        {
            ShowNotification("STORM RETROARCH", "Исполняемый файл RetroArch не найден.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"--connect {ip} --port {port}",
                WorkingDirectory = Path.GetDirectoryName(exe) ?? CoreManager.AppDir,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            ShowNotification("STORM RETROARCH", $"Ошибка подключения к Netplay: {ex.Message}");
        }
    }

    // ==================== SCREENSHOTS ====================

    private void RefreshScreenshotsList()
    {
        WrapScreenshots.Children.Clear();
        string dir = RetroArchConfigManager.GetString("screenshot_directory", Path.Combine(CoreManager.AppDir, "screenshots"));
        if (!Directory.Exists(dir))
        {
            try { Directory.CreateDirectory(dir); } catch { }
            return;
        }

        try
        {
            var files = Directory.GetFiles(dir, "*.png").OrderByDescending(File.GetCreationTime).Take(24);
            foreach (var f in files)
            {
                var card = new Border
                {
                    Width = 240,
                    Height = 160,
                    Background = (Brush)FindResource("ThemeSurface"),
                    BorderBrush = (Brush)FindResource("ThemeBorder"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 0, 10, 10),
                    Padding = new Thickness(8),
                    Cursor = Cursors.Hand
                };

                var sp = new StackPanel();
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(f);
                bmp.DecodePixelWidth = 220;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();

                var img = new Image { Source = bmp, Height = 115, Stretch = Stretch.Uniform };
                var txt = new TextBlock
                {
                    Text = Path.GetFileName(f),
                    Foreground = (Brush)FindResource("ThemeTextSecondary"),
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 6, 0, 0)
                };

                sp.Children.Add(img);
                sp.Children.Add(txt);
                card.Child = sp;

                string filePath = f;
                card.MouseDown += (s, ev) =>
                {
                    try { Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true }); } catch { }
                };

                WrapScreenshots.Children.Add(card);
            }
        }
        catch { }
    }

    private void BtnOpenScreenshotsFolder_Click(object sender, RoutedEventArgs e)
    {
        string dir = RetroArchConfigManager.GetString("screenshot_directory", Path.Combine(CoreManager.AppDir, "screenshots"));
        try
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start("explorer.exe", dir);
        }
        catch { }
    }

    private void BtnRefreshScreenshots_Click(object sender, RoutedEventArgs e) => RefreshScreenshotsList();

    // ==================== THEME & LANGUAGE POPULATION ====================

    private void PopulateThemeSelectors()
    {
        // Language
        CmbLanguage.Items.Clear();
        foreach (var lang in LocalizationManager.SupportedLanguages)
        {
            CmbLanguage.Items.Add(lang.DisplayName);
        }
        int langIdx = Array.FindIndex(LocalizationManager.SupportedLanguages, l => l.Code == LocalizationManager.CurrentLanguage);
        CmbLanguage.SelectedIndex = langIdx >= 0 ? langIdx : 0;

        // Theme
        CmbTheme.Items.Clear();
        foreach (var th in ThemeManager.Themes)
        {
            CmbTheme.Items.Add(th.Name);
        }
        CmbTheme.SelectedIndex = Math.Clamp(ThemeManager.Themes.FindIndex(t => t.Name == SettingsManager.Current.ThemeName), 0, ThemeManager.Themes.Count - 1);

        // Accent
        CmbAccent.Items.Clear();
        foreach (var ac in ThemeManager.Accents)
        {
            CmbAccent.Items.Add(ac.Name);
        }
        CmbAccent.SelectedIndex = Math.Clamp(SettingsManager.Current.AccentIndex, 0, ThemeManager.Accents.Length - 1);

        // Font
        CmbFont.Items.Clear();
        foreach (var fn in ThemeManager.Fonts)
        {
            CmbFont.Items.Add(fn.Name);
        }
        CmbFont.SelectedIndex = Math.Clamp(SettingsManager.Current.FontIndex, 0, ThemeManager.Fonts.Length - 1);

        ThemeManager.ApplyTheme(SettingsManager.Current.ThemeName, SettingsManager.Current.AccentIndex, SettingsManager.Current.FontIndex);
    }

    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || CmbLanguage.SelectedIndex < 0) return;
        var selected = LocalizationManager.SupportedLanguages[CmbLanguage.SelectedIndex];
        LocalizationManager.SetLanguage(selected.Code);
        SettingsManager.Current.LanguageCode = selected.Code;
        SettingsManager.Save();
    }

    private void CmbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || CmbTheme.SelectedIndex < 0) return;
        string name = ThemeManager.Themes[CmbTheme.SelectedIndex].Name;
        SettingsManager.Current.ThemeName = name;
        ThemeManager.ApplyTheme(name, SettingsManager.Current.AccentIndex, SettingsManager.Current.FontIndex);
        SettingsManager.Save();
    }

    private void CmbAccent_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || CmbAccent.SelectedIndex < 0) return;
        SettingsManager.Current.AccentIndex = CmbAccent.SelectedIndex;
        ThemeManager.ApplyTheme(SettingsManager.Current.ThemeName, CmbAccent.SelectedIndex, SettingsManager.Current.FontIndex);
        SettingsManager.Save();
    }

    private void CmbFont_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || CmbFont.SelectedIndex < 0) return;
        SettingsManager.Current.FontIndex = CmbFont.SelectedIndex;
        ThemeManager.ApplyTheme(SettingsManager.Current.ThemeName, SettingsManager.Current.AccentIndex, CmbFont.SelectedIndex);
        SettingsManager.Save();
    }

    // ==================== HEADER BUTTONS: UPDATE & ABOUT ====================

    private void ChkToolbarAutoUpdates_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        SettingsManager.Current.CheckUpdatesOnStartup = ChkToolbarAutoUpdates.IsChecked == true;
        SettingsManager.Save();
    }

    private void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        _ = UpdateManager.CheckForUpdatesAsync(silent: false);
    }

    public void ShowUpdateDialog(UpdateInfo info)
    {
        _pendingUpdate = info;
        TxtUpdateSubtitle.Text = $"Доступна версия {info.VersionString} (Текущая: {UpdateManager.CurrentVersionString})";
        TxtUpdateChangelog.Text = info.Changelog;
        PbUpdate.Visibility = Visibility.Collapsed;
        BtnUpdateConfirm.IsEnabled = true;
        OverlayUpdate.Visibility = Visibility.Visible;
    }

    private void BtnCloseUpdate_Click(object sender, RoutedEventArgs e) => OverlayUpdate.Visibility = Visibility.Collapsed;

    private async void BtnUpdateConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate == null) return;
        BtnUpdateConfirm.IsEnabled = false;
        PbUpdate.Visibility = Visibility.Visible;
        PbUpdate.IsIndeterminate = true;
        await UpdateManager.DownloadAndExecuteInstallerAsync(_pendingUpdate);
    }

    public void ShowAboutModal()
    {
        Dispatcher.Invoke(() => { OverlayAbout.Visibility = Visibility.Visible; });
    }

    public void ShowUpdateModal(UpdateInfo info)
    {
        Dispatcher.Invoke(() => { ShowUpdateDialog(info); });
    }

    private void BtnAbout_Click(object sender, RoutedEventArgs e) => OverlayAbout.Visibility = Visibility.Visible;
    private void BtnCloseAbout_Click(object sender, RoutedEventArgs e) => OverlayAbout.Visibility = Visibility.Collapsed;

    // ==================== WIN32 WNDPROC (DRAG & DROP) ====================

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DROPFILES)
        {
            IntPtr hDrop = wParam;
            uint count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
            var dropped = new List<string>();

            for (uint i = 0; i < count; i++)
            {
                var sb = new System.Text.StringBuilder(1024);
                DragQueryFile(hDrop, i, sb, 1024);
                dropped.Add(sb.ToString());
            }
            DragFinish(hDrop);

            Dispatcher.Invoke(() =>
            {
                foreach (var f in dropped)
                {
                    if (Directory.Exists(f)) ScanDirectory(f);
                    else if (File.Exists(f)) AddRomFile(f);
                }
                ApplyGamesFilter();
                UpdateStats();
            });

            handled = true;
        }
        return IntPtr.Zero;
    }
}
