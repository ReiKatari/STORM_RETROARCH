using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace StormRetroArch;

public partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    // Unified Hidden Certificate & Signature (Law 4 & Law 18)
    public const string StormCertificate = "STORM-AURORA-FLUID-CERT-2026-RETROARCH-ORIGINAL-AUTHENTIC-BUILD-VERIFIED";
    public const string StormSignature = "STORM TEAM SIGNATURE 2026 - AURORA FLUID STANDARD";

    public static readonly IValueConverter BoolToVisibility = new BooleanToVisibilityConverter();

    private readonly ObservableCollection<RomItem> _allGames = new();
    private readonly ObservableCollection<RomItem> _filteredGames = new();
    private StormTrayService? _trayService;
    private UpdateInfo? _pendingUpdate;
    private bool _isLoaded;

    // Win32 UIPI Bypass & Drag-and-Drop Constants
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
        LvGames.ItemsSource = _filteredGames;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;

        // Adaptive window placement into WorkArea
        var wa = SystemParameters.WorkArea;
        if (Width > wa.Width) Width = wa.Width * 0.95;
        if (Height > wa.Height) Height = wa.Height * 0.95;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + (wa.Height - Height) / 2;

        // UIPI Bypass & Win32 Drag-Drop hook
        var helper = new WindowInteropHelper(this);
        IntPtr hwnd = helper.EnsureHandle();

        try
        {
            DragAcceptFiles(hwnd, true);
            ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero);
            ChangeWindowMessageFilterEx(hwnd, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
            ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);

            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);
        }
        catch { }

        // Setup Tray Service
        _trayService = new StormTrayService(this);
        _trayService.Initialize();

        // Populate Selectors
        PopulateSelectors();

        // Setup CheckUpdatesOnStartup checkbox
        ChkCheckUpdatesStartup.IsChecked = SettingsManager.Current.CheckUpdatesOnStartup;

        // Populate Platform & Status Filters
        PopulateFilters();

        // Load Recent ROMs if any exist
        LoadRecentRoms();

        // Auto Check Updates on Startup
        if (SettingsManager.Current.CheckUpdatesOnStartup)
        {
            _ = Task.Run(() => UpdateManager.CheckForUpdatesAsync(silent: true));
        }

        UpdateStatistics();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DROPFILES)
        {
            IntPtr hDrop = wParam;
            uint count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
            var files = new List<string>();

            for (uint i = 0; i < count; i++)
            {
                var sb = new System.Text.StringBuilder(512);
                DragQueryFile(hDrop, i, sb, 512);
                files.Add(sb.ToString());
            }

            DragFinish(hDrop);
            handled = true;

            Dispatcher.InvokeAsync(() => ProcessDroppedFiles(files));
        }
        return IntPtr.Zero;
    }

    private void PopulateSelectors()
    {
        // Languages
        CmbLanguage.ItemsSource = LocalizationManager.Languages.Select(l => l.DisplayName).ToList();
        int langIdx = Array.FindIndex(LocalizationManager.Languages, l => l.Code.Equals(LocalizationManager.CurrentLanguage, StringComparison.OrdinalIgnoreCase));
        CmbLanguage.SelectedIndex = langIdx >= 0 ? langIdx : 0;

        // Themes
        CmbTheme.ItemsSource = ThemeManager.Themes.Select(t => t.Name).ToList();
        int themeIdx = ThemeManager.Themes.FindIndex(t => t.Name.Equals(SettingsManager.Current.ThemeName, StringComparison.OrdinalIgnoreCase));
        CmbTheme.SelectedIndex = themeIdx >= 0 ? themeIdx : 0;

        // Accents
        CmbAccent.ItemsSource = ThemeManager.Accents.Select(a => a.Name).ToList();
        CmbAccent.SelectedIndex = Math.Clamp(SettingsManager.Current.AccentIndex, 0, ThemeManager.Accents.Length - 1);

        // Fonts
        CmbFont.ItemsSource = ThemeManager.Fonts.Select(f => f.Name).ToList();
        CmbFont.SelectedIndex = Math.Clamp(SettingsManager.Current.FontIndex, 0, ThemeManager.Fonts.Length - 1);

        // Video Drivers in Engine Modal
        CmbVideoDriver.ItemsSource = new[] { "vulkan (Рекомендуется)", "d3d11", "d3d12", "gl" };
        CmbVideoDriver.SelectedIndex = 0;

        // Shaders in Engine Modal
        CmbShaders.ItemsSource = new[] { "None (Без фильтров)", "CRT-Royale (Кинескоп)", "CRT-Geom", "LCD3x (Портативные)", "xBRZ (Сглаживание)" };
        CmbShaders.SelectedIndex = 0;
    }

    private void PopulateFilters()
    {
        CmbPlatformFilter.Items.Clear();
        CmbPlatformFilter.Items.Add(LocalizationManager.GetString("FilterAllPlatforms"));
        CmbPlatformFilter.SelectedIndex = 0;

        CmbStatusFilter.Items.Clear();
        CmbStatusFilter.Items.Add(LocalizationManager.GetString("FilterAllStatuses"));
        CmbStatusFilter.Items.Add(LocalizationManager.GetString("StatusReady"));
        CmbStatusFilter.Items.Add(LocalizationManager.GetString("StatusMissingCore"));
        CmbStatusFilter.SelectedIndex = 0;
    }

    private void RefreshLocalization()
    {
        Title = LocalizationManager.GetString("AppTitle");
        TxtSubtitle.Text = LocalizationManager.GetString("Subtitle");
        LblLang.Text = LocalizationManager.GetString("Language") + ":";
        LblTheme.Text = LocalizationManager.GetString("Theme") + ":";
        LblAccent.Text = LocalizationManager.GetString("Accent") + ":";
        LblFont.Text = LocalizationManager.GetString("Font") + ":";
        BtnCheckUpdates.Content = LocalizationManager.GetString("BtnCheckUpdates");
        BtnCoresManager.Content = LocalizationManager.GetString("BtnCoresManager");
        BtnEngineSettings.Content = LocalizationManager.GetString("BtnEngineSettings");
        BtnAbout.Content = LocalizationManager.GetString("BtnAbout");
        BtnFullscreenOzone.Content = LocalizationManager.GetString("BtnFullscreenOzone");
        BtnClearList.Content = LocalizationManager.GetString("BtnClearList");
        ChkCheckUpdatesStartup.Content = LocalizationManager.GetString("ChkCheckUpdatesStartup");
        TxtSearchPlaceholder.Text = LocalizationManager.GetString("SearchPlaceholder");
        TxtEmptyTitle.Text = LocalizationManager.GetString("EmptyTitle");
        TxtEmptySubtitle.Text = LocalizationManager.GetString("EmptySubtitle");

        PopulateFilters();
        ApplyFilter();
    }

    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || CmbLanguage.SelectedIndex < 0) return;
        var lang = LocalizationManager.Languages[CmbLanguage.SelectedIndex];
        LocalizationManager.SetLanguage(lang.Code);
        SettingsManager.Current.LanguageCode = lang.Code;
        SettingsManager.Save();
        RefreshLocalization();
    }

    private void CmbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || CmbTheme.SelectedIndex < 0) return;
        string theme = ThemeManager.Themes[CmbTheme.SelectedIndex].Name;
        SettingsManager.Current.ThemeName = theme;
        SettingsManager.Save();
        ThemeManager.ApplyTheme(theme, CmbAccent.SelectedIndex, CmbFont.SelectedIndex);
    }

    private void CmbAccent_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || CmbAccent.SelectedIndex < 0) return;
        SettingsManager.Current.AccentIndex = CmbAccent.SelectedIndex;
        SettingsManager.Save();
        ThemeManager.ApplyTheme(SettingsManager.Current.ThemeName, CmbAccent.SelectedIndex, CmbFont.SelectedIndex);
    }

    private void CmbFont_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || CmbFont.SelectedIndex < 0) return;
        SettingsManager.Current.FontIndex = CmbFont.SelectedIndex;
        SettingsManager.Save();
        ThemeManager.ApplyTheme(SettingsManager.Current.ThemeName, CmbAccent.SelectedIndex, CmbFont.SelectedIndex);
    }

    private void ChkCheckUpdatesStartup_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        SettingsManager.Current.CheckUpdatesOnStartup = ChkCheckUpdatesStartup.IsChecked == true;
        SettingsManager.Save();
    }

    // ==================== DRAG AND DROP HANDLING ====================
    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void RootGrid_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0)
            {
                ProcessDroppedFiles(files);
                e.Handled = true;
            }
        }
    }

    private void ProcessDroppedFiles(IEnumerable<string> paths)
    {
        int addedCount = 0;
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                addedCount += ScanDirectory(path);
            }
            else if (File.Exists(path))
            {
                if (AddSingleFile(path)) addedCount++;
            }
        }

        if (addedCount > 0)
        {
            ApplyFilter();
            UpdateStatistics();
            SaveRecentRoms();
        }
    }

    private int ScanDirectory(string dirPath)
    {
        int count = 0;
        try
        {
            string[] files = Directory.GetFiles(dirPath, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                if (AddSingleFile(file)) count++;
            }
        }
        catch { }
        return count;
    }

    private bool AddSingleFile(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();

        // Check if it's a Libretro Core DLL dropped
        if (ext == ".dll" && Path.GetFileName(filePath).EndsWith("_libretro.dll", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string dest = Path.Combine(CoreManager.CoresDir, Path.GetFileName(filePath));
                File.Copy(filePath, dest, true);
                MessageBox.Show($"Ядро {Path.GetFileName(filePath)} успешно скопировано в cores/!", "STORM RETROARCH", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshCoreStatuses();
                return false;
            }
            catch { }
        }

        if (_allGames.Any(g => g.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
            return false;

        var id = CoreManager.IdentifyRom(filePath);
        if (id.Platform == "Неизвестная платформа" && ext != ".bin" && ext != ".iso" && ext != ".zip" && ext != ".7z")
            return false;

        var fi = new FileInfo(filePath);
        bool installed = CoreManager.IsCoreInstalled(id.CoreDll);

        var item = new RomItem
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            FilePath = filePath,
            Extension = ext,
            Platform = id.Platform,
            CoreDll = id.CoreDll,
            CoreDisplayName = id.CoreName,
            FileSizeBytes = fi.Length,
            IsCoreInstalled = installed,
            StatusText = installed ? LocalizationManager.GetString("StatusReady") : LocalizationManager.GetString("StatusMissingCore")
        };

        item.GeneratePixelIcon();
        _allGames.Add(item);
        return true;
    }

    private void RefreshCoreStatuses()
    {
        foreach (var g in _allGames)
        {
            g.IsCoreInstalled = CoreManager.IsCoreInstalled(g.CoreDll);
            if (g.IsCoreInstalled)
                g.StatusText = LocalizationManager.GetString("StatusReady");
        }
        UpdateStatistics();
    }

    private void ApplyFilter()
    {
        string search = TxtSearch?.Text.Trim().ToLowerInvariant() ?? "";
        string platFilter = CmbPlatformFilter?.SelectedItem?.ToString() ?? "";
        string statusFilter = CmbStatusFilter?.SelectedItem?.ToString() ?? "";

        string allPlatStr = LocalizationManager.GetString("FilterAllPlatforms");
        string allStatusStr = LocalizationManager.GetString("FilterAllStatuses");
        string readyStr = LocalizationManager.GetString("StatusReady");
        string missingStr = LocalizationManager.GetString("StatusMissingCore");

        _filteredGames.Clear();
        foreach (var g in _allGames)
        {
            if (!string.IsNullOrEmpty(search))
            {
                if (!g.Name.ToLowerInvariant().Contains(search) &&
                    !g.Platform.ToLowerInvariant().Contains(search) &&
                    !g.CoreDisplayName.ToLowerInvariant().Contains(search))
                {
                    continue;
                }
            }

            if (!string.IsNullOrEmpty(platFilter) && platFilter != allPlatStr && g.Platform != platFilter)
                continue;

            if (!string.IsNullOrEmpty(statusFilter) && statusFilter != allStatusStr)
            {
                if (statusFilter == readyStr && !g.IsCoreInstalled) continue;
                if (statusFilter == missingStr && g.IsCoreInstalled) continue;
            }

            _filteredGames.Add(g);
        }

        EmptyStateBorder.Visibility = _filteredGames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateStatistics()
    {
        TxtStatTotalGames.Text = _allGames.Count.ToString();
        int coresReady = _allGames.Count(g => g.IsCoreInstalled);
        TxtStatCoresReady.Text = $"{coresReady} / {_allGames.Count}";

        long totalBytes = _allGames.Sum(g => g.FileSizeBytes);
        TxtStatTotalSize.Text = RomItem.FormatFileSize(totalBytes);

        // Update platform filter combo
        var platforms = _allGames.Select(g => g.Platform).Distinct().OrderBy(p => p).ToList();
        string currentPlat = CmbPlatformFilter.SelectedItem?.ToString() ?? "";
        CmbPlatformFilter.Items.Clear();
        CmbPlatformFilter.Items.Add(LocalizationManager.GetString("FilterAllPlatforms"));
        foreach (var p in platforms) CmbPlatformFilter.Items.Add(p);

        int idx = CmbPlatformFilter.Items.IndexOf(currentPlat);
        CmbPlatformFilter.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void LoadRecentRoms()
    {
        if (SettingsManager.Current.RecentRoms != null)
        {
            foreach (var r in SettingsManager.Current.RecentRoms)
            {
                if (File.Exists(r)) AddSingleFile(r);
            }
            ApplyFilter();
        }
    }

    private void SaveRecentRoms()
    {
        SettingsManager.Current.RecentRoms = _allGames.Take(50).Select(g => g.FilePath).ToList();
        SettingsManager.Save();
    }

    // ==================== TOOLBAR BUTTONS ====================
    private void BtnAddFiles_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите ROM-образы игр",
            Filter = "Игровые образы и архивы (*.*)|*.*|NES/SNES/Genesis (*.nes;*.sfc;*.md)|*.nes;*.sfc;*.md|PlayStation/ISO (*.bin;*.cue;*.iso;*.chd)|*.bin;*.cue;*.iso;*.chd|GBA/NDS/3DS (*.gba;*.nds;*.3ds)|*.gba;*.nds;*.3ds|Архивы (*.zip;*.7z;*.rar)|*.zip;*.7z;*.rar",
            Multiselect = true
        };

        if (ofd.ShowDialog() == true)
        {
            ProcessDroppedFiles(ofd.FileNames);
        }
    }

    private void BtnAddFolder_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Выберите папку с игровыми образами",
            Multiselect = false
        };
        if (ofd.ShowDialog() == true)
        {
            ProcessDroppedFiles(new[] { ofd.FolderName });
        }
    }

    private void BtnClearList_Click(object sender, RoutedEventArgs e)
    {
        if (_allGames.Count == 0) return;
        if (MessageBox.Show("Очистить список игр?", "STORM RETROARCH", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _allGames.Clear();
            _filteredGames.Clear();
            UpdateStatistics();
            SaveRecentRoms();
            EmptyStateBorder.Visibility = Visibility.Visible;
        }
    }

    private void BtnFullscreenOzone_Click(object sender, RoutedEventArgs e)
    {
        CoreManager.LaunchOzone();
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }

    private void CmbPlatformFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoaded) ApplyFilter();
    }

    private void CmbStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoaded) ApplyFilter();
    }

    // ==================== LIST ITEM ACTIONS ====================
    private async void BtnItemPlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RomItem item)
        {
            await CoreManager.LaunchGameAsync(item);
        }
    }

    private void BtnItemFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RomItem item && File.Exists(item.FilePath))
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
            }
            catch { }
        }
    }

    private void BtnItemDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RomItem item)
        {
            _allGames.Remove(item);
            _filteredGames.Remove(item);
            UpdateStatistics();
            SaveRecentRoms();
            if (_filteredGames.Count == 0) EmptyStateBorder.Visibility = Visibility.Visible;
        }
    }

    // ==================== MODALS MANAGEMENT ====================
    public void ShowUpdateModal(UpdateInfo info)
    {
        _pendingUpdate = info;
        TxtUpdateSubtitle.Text = $"Новая версия {info.VersionString} (текущая: {UpdateManager.CurrentVersionString})";
        TxtUpdateChangelog.Text = string.IsNullOrWhiteSpace(info.Changelog) ? "Обновления стабильности и оптимизация движка." : info.Changelog;
        PbUpdate.Visibility = Visibility.Collapsed;
        OverlayUpdate.Visibility = Visibility.Visible;
    }

    public void ShowAboutModal()
    {
        OverlayAbout.Visibility = Visibility.Visible;
    }

    private void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        _ = UpdateManager.CheckForUpdatesAsync(silent: false);
    }

    private void BtnCoresManager_Click(object sender, RoutedEventArgs e)
    {
        LvCores.ItemsSource = CoreManager.GoldenCores;
        OverlayCoresManager.Visibility = Visibility.Visible;
    }

    private void BtnEngineSettings_Click(object sender, RoutedEventArgs e)
    {
        ChkPreemptive.IsChecked = SettingsManager.Current.PreemptiveFrames;
        ChkVrr.IsChecked = SettingsManager.Current.VrrEnabled;
        ChkWasapi.IsChecked = SettingsManager.Current.WasapiLowLatency;
        ChkInteger.IsChecked = SettingsManager.Current.IntegerScaling;
        OverlayEngineSettings.Visibility = Visibility.Visible;
    }

    private void BtnAbout_Click(object sender, RoutedEventArgs e)
    {
        ShowAboutModal();
    }

    private async void BtnDownloadCoreItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CoreInfo core)
        {
            btn.IsEnabled = false;
            var prog = new Progress<(double percent, string status)>(p =>
            {
                TxtFooterStatus.Text = $"{core.Name}: {p.status}";
            });
            bool ok = await CoreManager.DownloadCoreAsync(core.DllName, prog);
            btn.IsEnabled = true;
            if (ok)
            {
                RefreshCoreStatuses();
                MessageBox.Show($"Ядро {core.Name} готово к работе!", "STORM RETROARCH", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private async void BtnDownloadGoldenCores_Click(object sender, RoutedEventArgs e)
    {
        OverlayCoresManager.Visibility = Visibility.Collapsed;
        TxtFooterStatus.Text = "Начата загрузка золотого набора ядер...";

        foreach (var core in CoreManager.GoldenCores.Take(12))
        {
            if (!CoreManager.IsCoreInstalled(core.DllName))
            {
                TxtFooterStatus.Text = $"Загрузка {core.Name}...";
                await CoreManager.DownloadCoreAsync(core.DllName);
            }
        }

        RefreshCoreStatuses();
        TxtFooterStatus.Text = "Золотой набор ядер успешно загружен!";
        MessageBox.Show("Золотой набор ядер успешно установлен в папку cores/!", "STORM RETROARCH", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnSaveEngineSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.PreemptiveFrames = ChkPreemptive.IsChecked == true;
        SettingsManager.Current.VrrEnabled = ChkVrr.IsChecked == true;
        SettingsManager.Current.WasapiLowLatency = ChkWasapi.IsChecked == true;
        SettingsManager.Current.IntegerScaling = ChkInteger.IsChecked == true;
        SettingsManager.Save();
        OverlayEngineSettings.Visibility = Visibility.Collapsed;
        MessageBox.Show("Настройки движка сохранены!", "STORM RETROARCH", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void BtnUpdateConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate == null) return;
        BtnUpdateConfirm.IsEnabled = false;
        PbUpdate.Visibility = Visibility.Visible;

        var prog = new Progress<(long rec, long tot, int pct)>(p =>
        {
            PbUpdate.Value = p.pct;
        });

        await UpdateManager.DownloadAndExecuteInstallerAsync(_pendingUpdate, prog);
        BtnUpdateConfirm.IsEnabled = true;
    }

    private void BtnCloseModals_Click(object sender, RoutedEventArgs e)
    {
        CloseAllModals();
    }

    private void CloseAllModals()
    {
        OverlayCoresManager.Visibility = Visibility.Collapsed;
        OverlayEngineSettings.Visibility = Visibility.Collapsed;
        OverlayUpdate.Visibility = Visibility.Collapsed;
        OverlayAbout.Visibility = Visibility.Collapsed;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseAllModals();
        }
    }

    // ==================== WINDOW CAPTION BUTTONS ====================
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                BtnMax_Click(sender, e);
            }
            else
            {
                DragMove();
            }
        }
    }

    private void BtnMin_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnMax_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        PathMax.Data = Geometry.Parse(WindowState == WindowState.Maximized ? "M 2,0 L 9,0 L 9,7 M 0,2 L 7,2 L 7,9 L 0,9 Z" : "M 0,0 L 9,0 L 9,9 L 0,9 Z");
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        // Close to tray
        Hide();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Intercept close and minimize to tray
        e.Cancel = true;
        Hide();
    }
}
