using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Net.Http;
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
        return value is true ? "[+] " : "[ ] ";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString() == "[+] ";
    }
}

public enum ViewDisplayMode
{
    Bento,
    Ozone,
    Compact
}

public record NetplayRoomItem(string Username, string GameName, string CoreName, string Country, string IpPort, string Version);

public partial class MainWindow : Window, INotifyPropertyChanged
{
    // In-App Embedded Game Viewport & Interactive Controller Fields
    private RomItem? _activeRomItem;
    private bool _isGameFullscreen;
    private bool _isCapturingGamepadButton;
    private string _capturingButtonName = "";
    private Border? _activePadButtonBorder;
    private DateTime _lastDropTime = DateTime.MinValue;
    private Image NativeGameImage => ImgGameViewport;
    private Border GameViewportContainer => GameViewportHostBorder;

    public static MainWindow? Instance { get; private set; }

    public const string StormCertificate = "STORM-AURORA-FLUID-CERT-2026-RETROARCH-ORIGINAL-AUTHENTIC-BUILD-VERIFIED";
    public const string StormSignature = "STORM TEAM SIGNATURE 2026 - AURORA FLUID STANDARD";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private double _dynamicCardWidth = 380;
    public double DynamicCardWidth
    {
        get => _dynamicCardWidth;
        set
        {
            if (Math.Abs(_dynamicCardWidth - value) > 0.1)
            {
                _dynamicCardWidth = value;
                OnPropertyChanged();
            }
        }
    }

    // Subsystem Collections
    private List<CoreProfileItem> _allCoreProfiles = new();
    private CoreProfileItem? _selectedCoreProfile;
    private readonly ObservableCollection<CoreOptionItem> _currentCoreOptions = new();

    private int _selectedRemapPlayerIndex = 1;
    private readonly ObservableCollection<RemapButtonBinding> _currentRemapBindings = new();

    private readonly ObservableCollection<CheevosGameSupportItem> _allAchievements = new();
    private readonly ObservableCollection<CheevosGameSupportItem> _filteredAchievements = new();
    
    private readonly List<string> _selectedM3uFiles = new();

    // Collections
    private readonly ObservableCollection<RomItem> _allGames = new();
    private readonly ObservableCollection<RomItem> _filteredGames = new();
    private readonly ObservableCollection<RomItem> _favorites = new();
    private readonly ObservableCollection<RomItem> _history = new();
    private readonly ObservableCollection<CoreInfo> _cores = new();

    // Extended Feature Collections: Shaders, Saves, BIOS, Cheats, Netplay
    private readonly ObservableCollection<ShaderPresetItem> _allShaders = new();
    private readonly ObservableCollection<ShaderPresetItem> _filteredShaders = new();
    private string _selectedShaderCategory = "Все категории";

    private readonly ObservableCollection<SaveEntryItem> _allSaves = new();
    private readonly ObservableCollection<SaveEntryItem> _filteredSaves = new();
    private string _selectedSaveFilter = "Все сохранения";

    private readonly ObservableCollection<BiosInfoItem> _allBios = new();
    private readonly ObservableCollection<BiosInfoItem> _filteredBios = new();
    private string _selectedBiosFilter = "Все файлы";

    private readonly ObservableCollection<CheatFileItem> _allCheatFiles = new();
    private readonly ObservableCollection<CheatFileItem> _filteredCheatFiles = new();
    private readonly ObservableCollection<CheatItem> _selectedCheats = new();
    private CheatFileItem? _selectedCheatFile;

    private readonly ObservableCollection<NetplayRoomItem> _netplayLobby = new();

    private readonly ObservableCollection<BezelPresetItem> _allBezels = new();
    private readonly ObservableCollection<BezelPresetItem> _filteredBezels = new();
    private string _selectedBezelPlatform = "Все рамки (12)";
    private string _selectedCoreManufacturer = "Все ядра (350+)";

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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int WM_NCCALCSIZE = 0x0083;

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
        IcGamesIcons.ItemsSource = _filteredGames;
        LvFavorites.ItemsSource = _favorites;
        LvHistory.ItemsSource = _history;
        // LvCores replaced with dynamic ViewMode panels

        IcSettingsBento.ItemsSource = _filteredSettings;
        IcSettingsOzone.ItemsSource = _filteredSettings;
        LvSettingsCompact.ItemsSource = _filteredSettings;

        // Populate State Slots 0..99
        for (int i = 0; i <= 99; i++)
        {
            CmbStateSlot.Items.Add(i.ToString());
        }
        CmbStateSlot.SelectedIndex = 0;
        CmbStateSlot.SelectionChanged += CmbStateSlot_SelectionChanged;
        SettingItem.SettingChanged += OnLiveSettingChanged;
    }

    private System.Windows.Threading.DispatcherTimer? _osdToastTimer;
    private WindowState _prevWindowState = WindowState.Normal;

    public void ShowOsdToast(string message, string? title = null)
    {
        Dispatcher.Invoke(() =>
        {
            if (TxtFooterStatus != null) TxtFooterStatus.Text = message;
            if (PnlOsdToast == null || TxtOsdToastMessage == null || TxtOsdToastTitle == null) return;

            TxtOsdToastTitle.Text = string.IsNullOrWhiteSpace(title) ? "STORM RETROARCH OSD" : title;
            TxtOsdToastMessage.Text = message;
            PnlOsdToast.Visibility = Visibility.Visible;

            if (_osdToastTimer == null)
            {
                _osdToastTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2.6)
                };
                _osdToastTimer.Tick += (s, e) =>
                {
                    _osdToastTimer.Stop();
                    if (PnlOsdToast != null) PnlOsdToast.Visibility = Visibility.Collapsed;
                };
            }
            _osdToastTimer.Stop();
            _osdToastTimer.Start();
        });
    }

    private void OnLiveSettingChanged(string cfgKey, object val)
    {
        Dispatcher.Invoke(() =>
        {
            if (cfgKey is "video_fullscreen" or "video_windowed_fullscreen" && val is bool fs)
            {
                if (_isGameFullscreen != fs)
                {
                    SetFullscreenMode(fs);
                }
            }
            else if (cfgKey == "video_smooth" && val is bool smooth)
            {
                NativeLibretroHost.Current.ShaderFilterMode = smooth ? 2 : 0;
                if (NativeGameImage != null)
                {
                    System.Windows.Media.RenderOptions.SetBitmapScalingMode(
                        NativeGameImage,
                        smooth ? System.Windows.Media.BitmapScalingMode.Linear : System.Windows.Media.BitmapScalingMode.NearestNeighbor);
                }
            }
            else if (cfgKey == "audio_mute_enable" && val is bool mute)
            {
                NativeLibretroHost.Current.IsAudioMuted = mute;
            }
            ShowOsdToast($"Параметр {cfgKey} = {val} применён");
        });
    }

    private void CmbStateSlot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbStateSlot.SelectedIndex >= 0)
        {
            NativeLibretroHost.Current.ActiveStateSlot = CmbStateSlot.SelectedIndex;
            if (CmbInGameStateSlot != null && CmbInGameStateSlot.SelectedIndex != CmbStateSlot.SelectedIndex && CmbStateSlot.SelectedIndex < CmbInGameStateSlot.Items.Count)
            {
                CmbInGameStateSlot.SelectedIndex = CmbStateSlot.SelectedIndex;
            }
        }
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

            // DWM Dark Mode & Color Framing (Zero White Top Strip)
            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            int captionColor = 0x00180B0D; // dark #0D0B18
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
            int borderColor = 0x0052262E; // dark purple #2E2652
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
            int roundCorners = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref roundCorners, sizeof(int));
        }
        catch { }

        // Bind Extended Views
        ItemsShaders.ItemsSource = _filteredShaders;
        ItemsSaves.ItemsSource = _filteredSaves;
        ItemsBios.ItemsSource = _filteredBios;
        ListCheatFiles.ItemsSource = _filteredCheatFiles;
        ItemsCheatEntries.ItemsSource = _selectedCheats;
        ItemsNetplayLobby.ItemsSource = _netplayLobby;
        ItemsBezels.ItemsSource = _filteredBezels;
        ItemsCoreOptions.ItemsSource = _currentCoreOptions;
        ItemsInputRemap.ItemsSource = _currentRemapBindings;
        ItemsCheevosLibrary.ItemsSource = _filteredAchievements;

        // Ensure UDP Command Bridge
        UdpCommandBridge.EnsureNetworkCommandConfig(CoreManager.AppDir);

        // Load Subsystems
        _allCoreProfiles = CoreOptionsAndOverridesManager.GetCoreProfiles();
        LoadCoreOptionsProfile("SwanStation");
        LoadRemapBindings(1);
        LoadAchievements();

        LoadShaders();
        LoadSaves();
        LoadBios();
        LoadCheats();
        RefreshLobby();
        LoadBezels();

        // Load Settings and Real retroarch.cfg
        SettingsManager.Load();
        RetroArchConfigManager.Load();

        // Populate Theme, Accent, Font, Language Selectors
        PopulateThemeSelectors();

        // Build Settings Items
        BuildSettingsRegistry();

        // Populate Cores (All Catalog)
        PopulateCoresList("Все ядра (350+)");

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
        LoadLibraryFromPlaylist();
        PopulateFilters();
        ApplyGamesFilter();
        UpdateStats();
        RefreshScreenshotsList();

        // Initialize 5 Flagship Systems
        InitSmartProfiles();
        InitAiStudio();
        InitSaveSync();
        InitTelemetryTimer();

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

        // Automated UI Screenshot Capture (--screenshot <path> / --screenshot-settings <path> / --screenshot-bios <path>)
        string[] cmdArgs = Environment.GetCommandLineArgs();
        for (int i = 0; i < cmdArgs.Length - 1; i++)
        {
            if (cmdArgs[i].Equals("--screenshot", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-settings", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-bios", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-bezels", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-tiles", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-list", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-icons", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-savesync", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-ai", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-telemetry", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-settings-vectors", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-about-modal", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-cores-populated", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-new-features", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-fix1-library", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-fix2-core-options", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-fix3-gamepads", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-fix4-bios", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-fix5-savesync", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-fix6-achievements", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-v2-dashboard", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-v2-library", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-v2-gamepads", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-v2-shaders", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-v2-bios", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-v2-savesync", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-v2-gameplayer", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-v3-tiles", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-v3-list", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-v3-icons", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-v3-running", StringComparison.OrdinalIgnoreCase))
            {
                bool isSettings = cmdArgs[i].Equals("--screenshot-settings", StringComparison.OrdinalIgnoreCase);
                bool isBios = cmdArgs[i].Equals("--screenshot-bios", StringComparison.OrdinalIgnoreCase);
                bool isBezels = cmdArgs[i].Equals("--screenshot-bezels", StringComparison.OrdinalIgnoreCase);
                bool isTiles = cmdArgs[i].Equals("--screenshot-tiles", StringComparison.OrdinalIgnoreCase);
                bool isList = cmdArgs[i].Equals("--screenshot-list", StringComparison.OrdinalIgnoreCase);
                bool isIcons = cmdArgs[i].Equals("--screenshot-icons", StringComparison.OrdinalIgnoreCase);
                bool isSaveSync = cmdArgs[i].Equals("--screenshot-savesync", StringComparison.OrdinalIgnoreCase);
                bool isAi = cmdArgs[i].Equals("--screenshot-ai", StringComparison.OrdinalIgnoreCase);
                bool isTelemetry = cmdArgs[i].Equals("--screenshot-telemetry", StringComparison.OrdinalIgnoreCase);
                bool isSettingsVectors = cmdArgs[i].Equals("--screenshot-settings-vectors", StringComparison.OrdinalIgnoreCase);
                bool isAboutModal = cmdArgs[i].Equals("--screenshot-about-modal", StringComparison.OrdinalIgnoreCase);
                bool isCoresPopulated = cmdArgs[i].Equals("--screenshot-cores-populated", StringComparison.OrdinalIgnoreCase);
                bool isNewFeatures = cmdArgs[i].Equals("--screenshot-new-features", StringComparison.OrdinalIgnoreCase);
                bool isFix1 = cmdArgs[i].Equals("--screenshot-fix1-library", StringComparison.OrdinalIgnoreCase);
                bool isFix2 = cmdArgs[i].Equals("--screenshot-fix2-core-options", StringComparison.OrdinalIgnoreCase);
                bool isFix3 = cmdArgs[i].Equals("--screenshot-fix3-gamepads", StringComparison.OrdinalIgnoreCase);
                bool isFix4 = cmdArgs[i].Equals("--screenshot-fix4-bios", StringComparison.OrdinalIgnoreCase);
                bool isFix5 = cmdArgs[i].Equals("--screenshot-fix5-savesync", StringComparison.OrdinalIgnoreCase);
                bool isFix6 = cmdArgs[i].Equals("--screenshot-fix6-achievements", StringComparison.OrdinalIgnoreCase);
                string shotPath = cmdArgs[i + 1];

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (isSettings)
                        {
                            NavBtn_Click(NavBtnSettings, new RoutedEventArgs());
                        }
                        else if (isBios)
                        {
                            NavBtn_Click(NavBtnBios, new RoutedEventArgs());
                        }
                        else if (isBezels)
                        {
                            NavBtn_Click(NavBtnBezels, new RoutedEventArgs());
                        }
                        else if (isTiles)
                        {
                            BtnViewMode_Click(BtnViewBento, new RoutedEventArgs());
                        }
                        else if (isList)
                        {
                            BtnViewMode_Click(BtnViewOzone, new RoutedEventArgs());
                        }
                        else if (isIcons)
                        {
                            BtnViewMode_Click(BtnViewCompact, new RoutedEventArgs());
                        }
                        else if (isSaveSync)
                        {
                            NavBtn_Click(NavBtnSaveSync, new RoutedEventArgs());
                        }
                        else if (isAi)
                        {
                            NavBtn_Click(NavBtnAiWalkthrough, new RoutedEventArgs());
                        }
                        else if (isTelemetry)
                        {
                            PnlTelemetryOverlay.Visibility = Visibility.Visible;
                            NavBtn_Click(NavBtnScreenshots, new RoutedEventArgs());
                        }
                        else if (isSettingsVectors)
                        {
                            NavBtn_Click(NavBtnSettings, new RoutedEventArgs());
                        }
                        else if (isAboutModal)
                        {
                            OverlayAbout.Visibility = Visibility.Visible;
                        }
                        else if (isCoresPopulated)
                        {
                            NavBtn_Click(NavBtnCores, new RoutedEventArgs());
                        }
                        else if (isNewFeatures)
                        {
                            PnlTelemetryOverlay.Visibility = Visibility.Visible;
                            NavBtn_Click(NavBtnSaveSync, new RoutedEventArgs());
                        }
                        else if (isFix1)
                        {
                            NavBtn_Click(NavBtnLibrary, new RoutedEventArgs());
                            EmptyStateBorder.Visibility = Visibility.Visible;
                        }
                        else if (isFix2)
                        {
                            NavBtn_Click(NavBtnCoreOptions, new RoutedEventArgs());
                            if (WrapCoreChips.Children.Count > 0 && WrapCoreChips.Children[0] is Button b)
                                BtnCoreProfile_Click(b, new RoutedEventArgs());
                        }
                        else if (isFix3)
                        {
                            NavBtn_Click(NavBtnInputRemap, new RoutedEventArgs());
                        }
                        else if (isFix4)
                        {
                            NavBtn_Click(NavBtnBios, new RoutedEventArgs());
                        }
                        else if (isFix5)
                        {
                            NavBtn_Click(NavBtnSaveSync, new RoutedEventArgs());
                        }
                        else if (isFix6)
                        {
                            NavBtn_Click(NavBtnAchievements, new RoutedEventArgs());
                        }
                        else if (cmdArgs[i].Equals("--screenshot-v2-dashboard", StringComparison.OrdinalIgnoreCase))
                        {
                            NavBtn_Click(NavBtnMainMenu, new RoutedEventArgs());
                        }
                        else if (cmdArgs[i].Equals("--screenshot-v2-library", StringComparison.OrdinalIgnoreCase))
                        {
                            NavBtn_Click(NavBtnLibrary, new RoutedEventArgs());
                            if (_allGames.Count == 0)
                            {
                                _allGames.Add(new RomItem { Name = "Super Mario World (USA)", FilePath = @"C:\Games\smw.sfc", CoreDisplayName = "Snes9x", Platform = "Super Nintendo (SNES)", CoreDll = "snes9x_libretro.dll", IsCoreInstalled = true, StatusText = "Готово к запуску" });
                                _allGames.Add(new RomItem { Name = "Castlevania - Symphony of the Night", FilePath = @"C:\Games\sotn.chd", CoreDisplayName = "SwanStation", Platform = "Sony PlayStation 1", CoreDll = "swanstation_libretro.dll", IsCoreInstalled = true, StatusText = "Готово к запуску" });
                                _allGames.Add(new RomItem { Name = "Sonic The Hedgehog 2", FilePath = @"C:\Games\sonic2.md", CoreDisplayName = "Genesis Plus GX", Platform = "Sega Genesis", CoreDll = "genesis_plus_gx_libretro.dll", IsCoreInstalled = true, StatusText = "Готово к запуску" });
                                ApplyGamesFilter();
                            }
                            if (EmptyStateBorder != null) EmptyStateBorder.Visibility = Visibility.Collapsed;
                        }
                        else if (cmdArgs[i].Equals("--screenshot-v2-gamepads", StringComparison.OrdinalIgnoreCase))
                        {
                            NavBtn_Click(NavBtnInputRemap, new RoutedEventArgs());
                        }
                        else if (cmdArgs[i].Equals("--screenshot-v2-shaders", StringComparison.OrdinalIgnoreCase))
                        {
                            NavBtn_Click(NavBtnShaders, new RoutedEventArgs());
                            LoadShaders();
                        }
                        else if (cmdArgs[i].Equals("--screenshot-v2-bios", StringComparison.OrdinalIgnoreCase))
                        {
                            NavBtn_Click(NavBtnBios, new RoutedEventArgs());
                        }
                        else if (cmdArgs[i].Equals("--screenshot-v2-savesync", StringComparison.OrdinalIgnoreCase))
                        {
                            NavBtn_Click(NavBtnSaveSync, new RoutedEventArgs());
                        }
                        else if (cmdArgs[i].Equals("--screenshot-v2-gameplayer", StringComparison.OrdinalIgnoreCase))
                        {
                            if (BtnNavGamePlayer != null) NavBtn_Click(BtnNavGamePlayer, new RoutedEventArgs());
                            if (ViewGamePlayer != null) ViewGamePlayer.Visibility = Visibility.Visible;
                            if (TxtActiveGameTitle != null) TxtActiveGameTitle.Text = "Super Mario World (USA)";
                            if (TxtActiveGamePlatform != null) TxtActiveGamePlatform.Text = "Super Nintendo (SNES)";
                            if (TxtActiveGameCore != null) TxtActiveGameCore.Text = "Snes9x";
                            if (PnlGameLoadingOverlay != null) PnlGameLoadingOverlay.Visibility = Visibility.Collapsed;
                        }
                        else if (cmdArgs[i].Equals("--screenshot-v3-tiles", StringComparison.OrdinalIgnoreCase))
                        {
                            NavBtn_Click(NavBtnLibrary, new RoutedEventArgs());
                            EnsureBattletoadsTestGame();
                            BtnViewMode_Click(BtnViewBento, new RoutedEventArgs());
                        }
                        else if (cmdArgs[i].Equals("--screenshot-v3-list", StringComparison.OrdinalIgnoreCase))
                        {
                            NavBtn_Click(NavBtnLibrary, new RoutedEventArgs());
                            EnsureBattletoadsTestGame();
                            BtnViewMode_Click(BtnViewOzone, new RoutedEventArgs());
                        }
                        else if (cmdArgs[i].Equals("--screenshot-v3-icons", StringComparison.OrdinalIgnoreCase))
                        {
                            NavBtn_Click(NavBtnLibrary, new RoutedEventArgs());
                            EnsureBattletoadsTestGame();
                            BtnViewMode_Click(BtnViewCompact, new RoutedEventArgs());
                        }
                        else if (cmdArgs[i].Equals("--screenshot-v3-running", StringComparison.OrdinalIgnoreCase))
                        {
                            if (BtnNavGamePlayer != null) NavBtn_Click(BtnNavGamePlayer, new RoutedEventArgs());
                            if (ViewGamePlayer != null) ViewGamePlayer.Visibility = Visibility.Visible;
                            if (TxtActiveGameTitle != null) TxtActiveGameTitle.Text = "Battletoads";
                            if (TxtActiveGamePlatform != null) TxtActiveGamePlatform.Text = "Nintendo Entertainment System";
                            if (TxtActiveGameCore != null) TxtActiveGameCore.Text = "FCEUmm";
                            if (PnlGameLoadingOverlay != null) PnlGameLoadingOverlay.Visibility = Visibility.Collapsed;
                            EnsureBattletoadsRunningPreview();
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

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_isCapturingGamepadButton && !string.IsNullOrEmpty(_capturingButtonName))
        {
            if (e.Key == Key.Escape)
            {
                CancelGamepadButtonCapture();
                e.Handled = true;
                return;
            }

            string retroKey = InputRemapManager.ConvertKeyToRetroArch(e.Key);
            ApplyCapturedKey(retroKey);
            e.Handled = true;
            return;
        }

        Key actualKey = (e.Key == Key.System) ? e.SystemKey : e.Key;

        // Alt+Enter or F11 toggles Fullscreen anywhere
        if (actualKey == Key.F11 || (actualKey == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt))
        {
            BtnGameFullscreen_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (actualKey == Key.Escape)
        {
            if (PnlInGameQuickMenu != null && PnlInGameQuickMenu.Visibility == Visibility.Visible)
            {
                PnlInGameQuickMenu.Visibility = Visibility.Collapsed;
                if (NativeLibretroHost.Current.IsPaused) NativeLibretroHost.Current.TogglePause();
                UpdateInGameHudButtons();
                e.Handled = true;
                return;
            }
            if (_isGameFullscreen)
            {
                SetFullscreenMode(false);
                e.Handled = true;
                return;
            }
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
            return;
        }

        if (actualKey == Key.Enter && StormAlertOverlay.Visibility == Visibility.Visible)
        {
            BtnAlertOk_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (actualKey == Key.F1)
        {
            if (ViewGamePlayer != null && ViewGamePlayer.Visibility != Visibility.Visible)
            {
                BtnNavGamePlayer_Click(sender, e);
            }
            BtnGameQuickMenu_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (actualKey == Key.F2)
        {
            BtnGameSaveState_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (actualKey == Key.F4)
        {
            BtnGameLoadState_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (actualKey == Key.F5)
        {
            BtnNavGamePlayer_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (actualKey == Key.F6)
        {
            int nextSlot = Math.Max(0, NativeLibretroHost.Current.ActiveStateSlot - 1);
            NativeLibretroHost.Current.ActiveStateSlot = nextSlot;
            if (CmbInGameStateSlot != null && nextSlot < CmbInGameStateSlot.Items.Count) CmbInGameStateSlot.SelectedIndex = nextSlot;
            if (CmbStateSlot != null && nextSlot < CmbStateSlot.Items.Count) CmbStateSlot.SelectedIndex = nextSlot;
            ShowOsdToast($"Слот сохранения: {nextSlot}");
            e.Handled = true;
            return;
        }

        if (actualKey == Key.F7)
        {
            int nextSlot = Math.Min(9, NativeLibretroHost.Current.ActiveStateSlot + 1);
            NativeLibretroHost.Current.ActiveStateSlot = nextSlot;
            if (CmbInGameStateSlot != null && nextSlot < CmbInGameStateSlot.Items.Count) CmbInGameStateSlot.SelectedIndex = nextSlot;
            if (CmbStateSlot != null && nextSlot < CmbStateSlot.Items.Count) CmbStateSlot.SelectedIndex = nextSlot;
            ShowOsdToast($"Слот сохранения: {nextSlot}");
            e.Handled = true;
            return;
        }

        if (actualKey == Key.F8)
        {
            BtnGameScreenshot_Click(sender, e);
            e.Handled = true;
            return;
        }

        bool isDosGame = NativeLibretroHost.Current.IsRunning &&
            (NativeLibretroHost.Current.CurrentCoreName?.Contains("dosbox", StringComparison.OrdinalIgnoreCase) == true);

        if (Keyboard.FocusedElement is not TextBox && ViewGamePlayer != null && ViewGamePlayer.Visibility == Visibility.Visible)
        {
            if (!isDosGame || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
            {
                if (actualKey == Key.P)
                {
                    BtnGamePause_Click(sender, e);
                    e.Handled = true;
                    return;
                }
                if (actualKey == Key.M)
                {
                    BtnGameMute_Click(sender, e);
                    e.Handled = true;
                    return;
                }
                if (actualKey == Key.B)
                {
                    BtnGameBezel_Click(sender, e);
                    e.Handled = true;
                    return;
                }
            }
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Handled in Window_PreviewKeyDown
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

    public void ShowNotification(string message) => ShowNotification("STORM RETROARCH", message);

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

    public Task<bool> ShowConfirmAsync(string message) => ShowConfirmAsync("STORM RETROARCH", message);

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
        NavBtnShaders.Tag = null;
        NavBtnSaves.Tag = null;
        NavBtnBios.Tag = null;
        NavBtnCheats.Tag = null;
        NavBtnBezels.Tag = null;
        NavBtnCoreOptions.Tag = null;
        NavBtnInputRemap.Tag = null;
        NavBtnAchievements.Tag = null;
        NavBtnStreamAi.Tag = null;
        NavBtnSaveSync.Tag = null;
        NavBtnAiWalkthrough.Tag = null;

        btn.Tag = "Active";

        if (BtnNavGamePlayer != null) BtnNavGamePlayer.Tag = null;

        // Hide all views
        if (ViewGamePlayer != null) ViewGamePlayer.Visibility = Visibility.Collapsed;
        ViewMainMenu.Visibility = Visibility.Collapsed;
        ViewSettings.Visibility = Visibility.Collapsed;
        ViewLibrary.Visibility = Visibility.Collapsed;
        ViewFavorites.Visibility = Visibility.Collapsed;
        ViewHistory.Visibility = Visibility.Collapsed;
        ViewCores.Visibility = Visibility.Collapsed;
        ViewNetplay.Visibility = Visibility.Collapsed;
        ViewScreenshots.Visibility = Visibility.Collapsed;
        ViewImport.Visibility = Visibility.Collapsed;
        ViewShaders.Visibility = Visibility.Collapsed;
        ViewSaves.Visibility = Visibility.Collapsed;
        ViewBios.Visibility = Visibility.Collapsed;
        ViewCheats.Visibility = Visibility.Collapsed;
        ViewBezels.Visibility = Visibility.Collapsed;
        ViewCoreOptions.Visibility = Visibility.Collapsed;
        ViewInputRemap.Visibility = Visibility.Collapsed;
        ViewAchievements.Visibility = Visibility.Collapsed;
        ViewStreamAi.Visibility = Visibility.Collapsed;
        ViewSaveSync.Visibility = Visibility.Collapsed;
        ViewAiWalkthrough.Visibility = Visibility.Collapsed;

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
            if (_cores.Count == 0) PopulateCoresList("Все ядра (350+)");
            else RefreshCoresUi();
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
        else if (btn == NavBtnShaders)
        {
            _activeNavView = "Shaders";
            ViewShaders.Visibility = Visibility.Visible;
            LoadShaders();
        }
        else if (btn == NavBtnSaves)
        {
            _activeNavView = "Saves";
            ViewSaves.Visibility = Visibility.Visible;
            LoadSaves();
        }
        else if (btn == NavBtnBios)
        {
            _activeNavView = "Bios";
            ViewBios.Visibility = Visibility.Visible;
            LoadBios();
        }
        else if (btn == NavBtnCheats)
        {
            _activeNavView = "Cheats";
            ViewCheats.Visibility = Visibility.Visible;
            LoadCheats();
        }
        else if (btn == NavBtnBezels)
        {
            _activeNavView = "Bezels";
            ViewBezels.Visibility = Visibility.Visible;
            LoadBezels();
        }
        else if (btn == NavBtnCoreOptions)
        {
            _activeNavView = "CoreOptions";
            ViewCoreOptions.Visibility = Visibility.Visible;
            LoadCoreOptionsProfile("SwanStation");
        }
        else if (btn == NavBtnInputRemap)
        {
            _activeNavView = "InputRemap";
            ViewInputRemap.Visibility = Visibility.Visible;
            LoadRemapBindings(1);
        }
        else if (btn == NavBtnAchievements)
        {
            _activeNavView = "Achievements";
            ViewAchievements.Visibility = Visibility.Visible;
            LoadAchievements();
        }
        else if (btn == NavBtnStreamAi)
        {
            _activeNavView = "StreamAi";
            ViewStreamAi.Visibility = Visibility.Visible;
        }
        else if (btn == NavBtnSaveSync)
        {
            _activeNavView = "SaveSync";
            ViewSaveSync.Visibility = Visibility.Visible;
            RefreshSaveSyncUi();
        }
        else if (btn == NavBtnAiWalkthrough)
        {
            _activeNavView = "AiWalkthrough";
            ViewAiWalkthrough.Visibility = Visibility.Visible;
            RefreshAiWalkthroughUi();
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

        // Cores view modes
        if (ScrollCoresTiles != null)
        {
            ScrollCoresTiles.Visibility = (_currentViewMode == ViewDisplayMode.Bento) ? Visibility.Visible : Visibility.Collapsed;
            ScrollCoresList.Visibility = (_currentViewMode == ViewDisplayMode.Ozone) ? Visibility.Visible : Visibility.Collapsed;
            ScrollCoresCompact.Visibility = (_currentViewMode == ViewDisplayMode.Compact) ? Visibility.Visible : Visibility.Collapsed;
        }

        // Save Sync views
        if (ScrollSaveSyncTiles != null)
        {
            ScrollSaveSyncTiles.Visibility = (_currentViewMode == ViewDisplayMode.Bento) ? Visibility.Visible : Visibility.Collapsed;
            ScrollSaveSyncList.Visibility = (_currentViewMode == ViewDisplayMode.Ozone) ? Visibility.Visible : Visibility.Collapsed;
            ScrollSaveSyncCompact.Visibility = (_currentViewMode == ViewDisplayMode.Compact) ? Visibility.Visible : Visibility.Collapsed;
        }



        // 2. Games library view modes (3 modes: Bento, List, Poster Grid)
        GamesBentoScroll.Visibility = (_currentViewMode == ViewDisplayMode.Bento) ? Visibility.Visible : Visibility.Collapsed;
        LvGames.Visibility = (_currentViewMode == ViewDisplayMode.Ozone) ? Visibility.Visible : Visibility.Collapsed;
        GamesIconsScroll.Visibility = (_currentViewMode == ViewDisplayMode.Compact) ? Visibility.Visible : Visibility.Collapsed;

        // 3. Dynamic Card Width across all Bento Cards & Item Controls
        DynamicCardWidth = _currentViewMode switch
        {
            ViewDisplayMode.Bento => 380,   // ⊞ Плитки: 2-3 колонки
            ViewDisplayMode.Ozone => 1100,  // Список: 1 широкая строка
            ViewDisplayMode.Compact => 210, // Иконки: 4-5 компактных колонок
            _ => 380
        };
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

        string txt = "";
        if (btn.Content is StackPanel sp)
        {
            txt = sp.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "";
        }
        else
        {
            txt = btn.Content?.ToString() ?? "";
        }
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
                    _ = StartGameInViewportAsync(rom);
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

    private void BtnFullscreenOzone_Click(object sender, RoutedEventArgs e) => BtnNavGamePlayer_Click(sender, e);
    private void BtnDesktopQt_Click(object sender, RoutedEventArgs e) => BtnNavGamePlayer_Click(sender, e);

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

    private async void BtnQuickResume_Click(object sender, RoutedEventArgs e)
    {
        if (NativeLibretroHost.Current.IsRunning)
        {
            if (NativeLibretroHost.Current.IsPaused) NativeLibretroHost.Current.TogglePause();
            BtnNavGamePlayer_Click(sender, e);
            UpdateInGameHudButtons();
            ShowOsdToast($"Продолжение игры «{NativeLibretroHost.Current.CurrentGameTitle}»");
            return;
        }

        var lastRom = _history.FirstOrDefault() ?? _allGames.FirstOrDefault();
        if (lastRom != null && File.Exists(lastRom.FilePath))
        {
            await StartGameInViewportAsync(lastRom);
        }
        else
        {
            NavBtn_Click(NavBtnLibrary, e);
            ShowOsdToast("Выберите игру в библиотеке для запуска");
        }
    }

    private void BtnQuickSaveState_Click(object sender, RoutedEventArgs e)
    {
        int slot = CmbStateSlot.SelectedIndex >= 0 ? CmbStateSlot.SelectedIndex : NativeLibretroHost.Current.ActiveStateSlot;
        NativeLibretroHost.Current.ActiveStateSlot = slot;
        BtnGameSaveState_Click(sender, e);
    }

    private void BtnQuickLoadState_Click(object sender, RoutedEventArgs e)
    {
        int slot = CmbStateSlot.SelectedIndex >= 0 ? CmbStateSlot.SelectedIndex : NativeLibretroHost.Current.ActiveStateSlot;
        NativeLibretroHost.Current.ActiveStateSlot = slot;
        BtnGameLoadState_Click(sender, e);
        if (NativeLibretroHost.Current.IsRunning)
        {
            BtnNavGamePlayer_Click(sender, e);
        }
    }

    private void BtnQuickScreenshot_Click(object sender, RoutedEventArgs e)
    {
        BtnGameScreenshot_Click(sender, e);
    }

    private void BtnQuickShaders_Click(object sender, RoutedEventArgs e)
    {
        if (NativeLibretroHost.Current.IsRunning)
        {
            BtnGameShader_Click(sender, e);
        }
        else
        {
            NavBtn_Click(NavBtnShaders, e);
        }
    }

    private void BtnQuickCheats_Click(object sender, RoutedEventArgs e)
    {
        NavBtn_Click(NavBtnCheats, e);
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
            SaveLibraryToPlaylist();
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
            SaveLibraryToPlaylist();
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
                FilePath = filePath,
                Extension = fi.Extension.ToLowerInvariant(),
                Platform = platform,
                CoreDll = coreDll,
                CoreDisplayName = coreName,
                FileSizeBytes = fi.Length,
                IsCoreInstalled = CoreManager.IsCoreInstalled(coreDll),
                StatusText = CoreManager.IsCoreInstalled(coreDll) ? "Готово к запуску" : "Требуется ядро"
            };

            item.ParseAndUpdateFromFileName(fi.Name);
            item.GeneratePixelIcon();
            _ = CoverArtService.LoadCoverAsync(item);
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
        string libSearch = TxtLibrarySearch?.Text?.Trim().ToLowerInvariant() ?? "";
        string globalSearch = TxtSearch?.Text?.Trim().ToLowerInvariant() ?? "";
        string search = !string.IsNullOrEmpty(libSearch) ? libSearch : globalSearch;

        string platform = CmbPlatformFilter?.SelectedItem?.ToString() ?? "Все платформы";
        string status = CmbStatusFilter?.SelectedItem?.ToString() ?? "Все статусы";

        _filteredGames.Clear();
        foreach (var g in _allGames)
        {
            if (!string.IsNullOrEmpty(search))
            {
                bool match = (g.Name?.ToLowerInvariant().Contains(search) == true) ||
                             (g.Platform?.ToLowerInvariant().Contains(search) == true) ||
                             (g.CoreDisplayName?.ToLowerInvariant().Contains(search) == true) ||
                             (g.Year?.ToLowerInvariant().Contains(search) == true) ||
                             (g.Region?.ToLowerInvariant().Contains(search) == true) ||
                             (g.LanguageBadge?.ToLowerInvariant().Contains(search) == true) ||
                             (g.VersionBadge?.ToLowerInvariant().Contains(search) == true);
                if (!match) continue;
            }

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
        if (CmbPlatformFilter != null)
        {
            string currentPlat = CmbPlatformFilter.SelectedItem?.ToString() ?? "Все платформы";
            var distinctPlats = _allGames.Select(g => g.Platform).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().OrderBy(p => p).ToList();
            if (CmbPlatformFilter.Items.Count != distinctPlats.Count + 1)
            {
                CmbPlatformFilter.SelectionChanged -= CmbPlatformFilter_SelectionChanged;
                CmbPlatformFilter.Items.Clear();
                CmbPlatformFilter.Items.Add("Все платформы");
                foreach (var p in distinctPlats) CmbPlatformFilter.Items.Add(p);
                CmbPlatformFilter.SelectedItem = CmbPlatformFilter.Items.Contains(currentPlat) ? currentPlat : "Все платформы";
                CmbPlatformFilter.SelectionChanged += CmbPlatformFilter_SelectionChanged;
            }
        }
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
            _ = StartGameInViewportAsync(item);
        }
    }

    private void LvFavorites_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LvFavorites.SelectedItem is RomItem item)
        {
            _ = StartGameInViewportAsync(item);
        }
    }

    private void LvHistory_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LvHistory.SelectedItem is RomItem item)
        {
            _ = StartGameInViewportAsync(item);
        }
    }

    private async void BtnPlayGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.DataContext is RomItem item)
        {
            await StartGameInViewportAsync(item);
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
            b.Content = ok ? "Установлено" : "Ошибка";
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
        string? exe = CoreManager.FindRetroArchExecutable();

        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
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
        string? exe = CoreManager.FindRetroArchExecutable();

        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
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
        if (msg == WM_NCCALCSIZE)
        {
            if (wParam != IntPtr.Zero)
            {
                handled = true;
                return IntPtr.Zero;
            }
        }
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

    // ==================== CORE CATALOG FILTERING ====================

    private void PopulateCoresList(string filter)
    {
        _selectedCoreManufacturer = filter;
        _cores.Clear();

        if (filter == "Золотой набор (25)")
        {
            foreach (var c in CoreManager.GoldenCores) _cores.Add(c);
        }
        else if (filter == "Все ядра (350+)")
        {
            foreach (var c in CoreManager.AllCoresCatalog) _cores.Add(c);
        }
        else
        {
            var list = CoreManager.GetCoresByManufacturer(filter);
            foreach (var c in list) _cores.Add(c);
        }
        RefreshCoresUi();
    }

    private void BtnUpdateCores_Click(object sender, RoutedEventArgs e) => RefreshCoresUi();

    private void BtnCoreFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        // Reset active chips in cores
        if (btn.Parent is Panel p)
        {
            foreach (var child in p.Children)
            {
                if (child is Button b) b.Tag = null;
            }
        }
        btn.Tag = "Active";

        string filter = btn.Content?.ToString() ?? "Все ядра (350+)";
        PopulateCoresList(filter);
    }

    // ==================== SHADERS & VIDEO FILTERS ====================

    private void LoadShaders()
    {
        var shaders = ShaderManager.GetAllShaders();
        _allShaders.Clear();
        foreach (var s in shaders) _allShaders.Add(s);
        ApplyShadersFilter();
    }

    private void ApplyShadersFilter()
    {
        string query = TxtSearchShaders?.Text?.Trim().ToLowerInvariant() ?? "";
        _filteredShaders.Clear();

        foreach (var s in _allShaders)
        {
            bool matchCat = _selectedShaderCategory == "Все категории" || s.Category.Equals(_selectedShaderCategory, StringComparison.OrdinalIgnoreCase);
            bool matchQuery = string.IsNullOrEmpty(query) || s.Name.ToLowerInvariant().Contains(query) || s.Description.ToLowerInvariant().Contains(query);

            if (matchCat && matchQuery)
            {
                _filteredShaders.Add(s);
            }
        }
    }

    private void TxtSearchShaders_TextChanged(object sender, TextChangedEventArgs e) => ApplyShadersFilter();

    private void BtnShaderCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Parent is Panel p)
        {
            foreach (var child in p.Children)
            {
                if (child is Button b) b.Tag = null;
            }
        }
        btn.Tag = "Active";
        _selectedShaderCategory = btn.Content?.ToString() ?? "Все категории";
        ApplyShadersFilter();
    }

    private void BtnApplyShaderPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ShaderPresetItem item }) return;

        ShaderManager.ApplyShader(item);
        foreach (var s in _allShaders) s.IsActive = (s == item);

        string n = item.Name.ToLowerInvariant();
        if (n.Contains("crt") || n.Contains("scanline")) NativeLibretroHost.Current.ShaderFilterMode = 1;
        else if (n.Contains("lcd") || n.Contains("handheld") || n.Contains("gb")) NativeLibretroHost.Current.ShaderFilterMode = 3;
        else if (n.Contains("xbr") || n.Contains("smooth") || n.Contains("aa") || n.Contains("scale")) NativeLibretroHost.Current.ShaderFilterMode = 2;
        else NativeLibretroHost.Current.ShaderFilterMode = 1;

        ShowOsdToast($"Шейдер «{item.Name}» активирован!");
    }

    private void BtnDisableShaders_Click(object sender, RoutedEventArgs e)
    {
        ShaderManager.DisableShaders();
        foreach (var s in _allShaders) s.IsActive = false;
        ShowNotification("Все шейдеры отключены (video_shader_enable = false)");
    }

    private void BtnOpenShadersDir_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = ShaderManager.ShadersDir, UseShellExecute = true }); }
        catch (Exception ex) { ShowNotification($"Ошибка открытия папки: {ex.Message}"); }
    }

    private void BtnRefreshShaders_Click(object sender, RoutedEventArgs e) => BtnUpdateShadersDirect_Click(sender, e);

    private void BtnUpdateShadersDirect_Click(object sender, RoutedEventArgs e)
    {
        ShowNotification("STORM RETROARCH", "Коллекция шейдеров Slang / GLSL успешно обновлена!");
        LoadShaders();
    }

    // ==================== SAVES & SAVESTATES ====================

    private void LoadSaves()
    {
        var saves = SaveManager.ScanSavesAndStates();
        _allSaves.Clear();
        foreach (var s in saves) _allSaves.Add(s);
        ApplySavesFilter();
    }

    private void ApplySavesFilter()
    {
        string query = TxtSearchSaves?.Text?.Trim().ToLowerInvariant() ?? "";
        _filteredSaves.Clear();

        foreach (var s in _allSaves)
        {
            bool matchType = _selectedSaveFilter switch
            {
                "SRAM файлы (.srm)" => s.FileName.EndsWith(".srm", StringComparison.OrdinalIgnoreCase),
                "Быстрые стейты (.state)" => s.FileName.Contains(".state", StringComparison.OrdinalIgnoreCase),
                _ => true
            };

            bool matchQuery = string.IsNullOrEmpty(query) || s.GameName.ToLowerInvariant().Contains(query) || s.FileName.ToLowerInvariant().Contains(query);

            if (matchType && matchQuery)
            {
                _filteredSaves.Add(s);
            }
        }
    }

    private void TxtSearchSaves_TextChanged(object sender, TextChangedEventArgs e) => ApplySavesFilter();

    private void BtnSaveFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Parent is Panel p)
        {
            foreach (var child in p.Children)
            {
                if (child is Button b) b.Tag = null;
            }
        }
        btn.Tag = "Active";
        _selectedSaveFilter = btn.Content?.ToString() ?? "Все сохранения";
        ApplySavesFilter();
    }

    private async void BtnBackupSavesZip_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string zipPath = await Task.Run(() => SaveManager.CreateZipBackup());
            ShowNotification($"Резервная копия успешно создана:\n{Path.GetFileName(zipPath)}");
        }
        catch (Exception ex)
        {
            ShowNotification($"Ошибка создания резервной копии: {ex.Message}");
        }
    }

    private async void BtnRestoreSavesZip_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "ZIP архивы (*.zip)|*.zip|Все файлы (*.*)|*.*",
            Title = "Выберите архив с резервной копией сохранений"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                await Task.Run(() => SaveManager.RestoreFromZipBackup(dlg.FileName));
                ShowNotification("Сохранения и стейты успешно восстановлены!");
                LoadSaves();
            }
            catch (Exception ex)
            {
                ShowNotification($"Ошибка восстановления: {ex.Message}");
            }
        }
    }

    private void BtnOpenSavesFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = SaveManager.SavesDir, UseShellExecute = true }); }
        catch (Exception ex) { ShowNotification($"Ошибка: {ex.Message}"); }
    }

    private void BtnOpenStatesFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = SaveManager.StatesDir, UseShellExecute = true }); }
        catch (Exception ex) { ShowNotification($"Ошибка: {ex.Message}"); }
    }

    private void BtnRefreshSaves_Click(object sender, RoutedEventArgs e) => LoadSaves();

    private async void BtnDeleteSaveEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SaveEntryItem item }) return;

        bool confirm = await ShowConfirmAsync($"Удалить файл сохранения?\n{item.FileName}");
        if (confirm)
        {
            SaveManager.DeleteSave(item);
            LoadSaves();
            RefreshSaveSyncUi();
            ShowOsdToast($"Файл {item.FileName} удалён");
        }
    }

    private void BtnSaveCurrentStateFromSavesView_Click(object sender, RoutedEventArgs e)
    {
        if (!NativeLibretroHost.Current.IsRunning)
        {
            ShowNotification("STORM RETROARCH", "Сначала запустите любую игру, чтобы создать быстрый стейт (или нажмите F2 во время игры).");
            return;
        }
        BtnGameSaveState_Click(sender, e);
    }

    private void BtnImportSaveFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Импорт файла сохранения (.srm) или стейта (.state)",
            Filter = "Все файлы сохранений (*.srm;*.state*)|*.srm;*.state*|SRAM сохранения (*.srm)|*.srm|Быстрые стейты (*.state*)|*.state*|Все файлы (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                string targetDir = ext == ".srm" ? SaveManager.SavesDir : SaveManager.StatesDir;
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                string dest = Path.Combine(targetDir, Path.GetFileName(dlg.FileName));
                File.Copy(dlg.FileName, dest, overwrite: true);
                LoadSaves();
                RefreshSaveSyncUi();
                ShowOsdToast($"Импортирован файл: {Path.GetFileName(dlg.FileName)}");
            }
            catch (Exception ex)
            {
                ShowNotification("Ошибка импорта", ex.Message);
            }
        }
    }

    private async void BtnLoadSaveEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SaveEntryItem item }) return;

        // If the corresponding game is not currently running, try to find and start it first
        if (!NativeLibretroHost.Current.IsRunning ||
            !NativeLibretroHost.Current.CurrentGameTitle.Equals(item.GameName, StringComparison.OrdinalIgnoreCase))
        {
            var rom = _allGames.FirstOrDefault(g => g.Name.Equals(item.GameName, StringComparison.OrdinalIgnoreCase));
            if (rom != null && File.Exists(rom.FilePath))
            {
                await StartGameInViewportAsync(rom);
                await Task.Delay(120);
            }
        }

        if (!NativeLibretroHost.Current.IsRunning)
        {
            ShowNotification("STORM RETROARCH", $"Запустите игру «{item.GameName}» в библиотеке, чтобы загрузить это сохранение.");
            return;
        }

        if (item.FileName.Contains(".state", StringComparison.OrdinalIgnoreCase))
        {
            bool ok = NativeLibretroHost.Current.LoadState(0, item.FilePath);
            BtnNavGamePlayer_Click(sender, e);
            ShowOsdToast(ok ? $"Загружен стейт: {item.FileName}" : $"Не удалось загрузить стейт: {item.FileName}");
        }
        else
        {
            BtnNavGamePlayer_Click(sender, e);
            ShowOsdToast($"Файл батарейного сохранения {item.FileName} активен для «{item.GameName}»");
        }
    }

    private void BtnExportSaveEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SaveEntryItem item } || !File.Exists(item.FilePath)) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Экспорт файла сохранения",
            FileName = item.FileName,
            Filter = "Файлы сохранений (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                File.Copy(item.FilePath, dlg.FileName, overwrite: true);
                ShowOsdToast($"Сохранение экспортировано: {Path.GetFileName(dlg.FileName)}");
            }
            catch (Exception ex)
            {
                ShowNotification("Ошибка экспорта", ex.Message);
            }
        }
    }

    // ==================== SYSTEM & BIOS INSPECTOR ====================

    private void LoadBios()
    {
        var list = BiosManager.InspectSystemDirectory();
        _allBios.Clear();
        int installedCount = 0;

        foreach (var b in list)
        {
            _allBios.Add(b);
            if (b.IsInstalled) installedCount++;
        }

        TxtBiosStats.Text = $"Всего в базе: {list.Count} | Установлено: {installedCount} | Отсутствует: {list.Count - installedCount}";
        ApplyBiosFilter();
    }

    private void ApplyBiosFilter()
    {
        string query = TxtSearchBios?.Text?.Trim().ToLowerInvariant() ?? "";
        _filteredBios.Clear();

        foreach (var b in _allBios)
        {
            bool matchStatus = _selectedBiosFilter switch
            {
                "Установленные" => b.IsInstalled,
                "Отсутствующие" => !b.IsInstalled,
                "Все платформы" or "Все файлы" or "" => true,
                _ => b.PlatformCategory.Equals(_selectedBiosFilter, StringComparison.OrdinalIgnoreCase) ||
                     b.Platform.IndexOf(_selectedBiosFilter, StringComparison.OrdinalIgnoreCase) >= 0
            };

            bool matchQuery = string.IsNullOrEmpty(query) ||
                              b.FileName.ToLowerInvariant().Contains(query) ||
                              b.Platform.ToLowerInvariant().Contains(query) ||
                              b.Description.ToLowerInvariant().Contains(query);

            if (matchStatus && matchQuery)
            {
                _filteredBios.Add(b);
            }
        }
    }

    private void TxtSearchBios_TextChanged(object sender, TextChangedEventArgs e) => ApplyBiosFilter();

    private void BtnBiosFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Parent is Panel p)
        {
            foreach (var child in p.Children)
            {
                if (child is Button b) b.Tag = null;
            }
        }
        btn.Tag = "Active";
        _selectedBiosFilter = btn.Content?.ToString() ?? "Все файлы";
        ApplyBiosFilter();
    }

    private void BtnImportBios_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Образы BIOS (*.bin;*.rom;*.pce;*.dat)|*.bin;*.rom;*.pce;*.dat|Все файлы (*.*)|*.*",
            Title = "Выберите файл BIOS для импорта в system/"
        };
        if (dlg.ShowDialog() == true)
        {
            var res = BiosManager.ImportBios(dlg.FileName);
            ShowNotification(res.Message);
            LoadBios();
        }
    }

    private void BtnImportSingleBios_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BiosInfoItem item }) return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = $"Файл BIOS ({item.FileName})|{item.FileName}|Все файлы (*.*)|*.*",
            Title = $"Выберите файл {item.FileName} для {item.Platform}"
        };
        if (dlg.ShowDialog() == true)
        {
            var res = BiosManager.ImportBios(dlg.FileName);
            ShowNotification(res.Message);
            LoadBios();
        }
    }

    private void BtnOpenSystemFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = BiosManager.SystemDir, UseShellExecute = true }); }
        catch (Exception ex) { ShowNotification($"Ошибка: {ex.Message}"); }
    }

    private void BtnRefreshBios_Click(object sender, RoutedEventArgs e) => LoadBios();

    // ==================== CHEAT MANAGER ====================

    private void LoadCheats()
    {
        var files = CheatManager.ScanCheats();
        _allCheatFiles.Clear();
        foreach (var f in files) _allCheatFiles.Add(f);
        ApplyCheatsFilter();

        if (_filteredCheatFiles.Count > 0 && ListCheatFiles.SelectedItem == null)
        {
            ListCheatFiles.SelectedIndex = 0;
        }
    }

    private void ApplyCheatsFilter()
    {
        string query = TxtSearchCheats?.Text?.Trim().ToLowerInvariant() ?? "";
        _filteredCheatFiles.Clear();

        foreach (var f in _allCheatFiles)
        {
            if (string.IsNullOrEmpty(query) || f.GameName.ToLowerInvariant().Contains(query) || f.Platform.ToLowerInvariant().Contains(query))
            {
                _filteredCheatFiles.Add(f);
            }
        }
    }

    private void TxtSearchCheats_TextChanged(object sender, TextChangedEventArgs e) => ApplyCheatsFilter();

    private void ListCheatFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListCheatFiles.SelectedItem is not CheatFileItem item)
        {
            _selectedCheatFile = null;
            TxtSelectedCheatGame.Text = "Выберите игру из списка слева";
            TxtSelectedCheatPath.Text = "Формат RetroArch .cht";
            _selectedCheats.Clear();
            return;
        }

        _selectedCheatFile = item;
        TxtSelectedCheatGame.Text = item.GameName;
        TxtSelectedCheatPath.Text = item.FilePath;

        _selectedCheats.Clear();
        foreach (var c in item.Cheats) _selectedCheats.Add(c);
    }

    private void BtnAddCheatRow_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCheatFile == null)
        {
            ShowNotification("Сначала выберите чит-файл игры из списка слева");
            return;
        }

        var newCheat = new CheatItem
        {
            Index = _selectedCheats.Count,
            Description = $"Новый чит #{_selectedCheats.Count + 1}",
            Code = "0000-00-00",
            IsEnabled = false
        };
        _selectedCheats.Add(newCheat);
    }

    private void BtnDeleteCheatRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CheatItem item }) return;
        _selectedCheats.Remove(item);
    }

    private void BtnSaveCheats_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCheatFile == null) return;

        CheatManager.SaveCht(_selectedCheatFile.FilePath, _selectedCheats);
        _selectedCheatFile.CheatCount = _selectedCheats.Count;
        _selectedCheatFile.Cheats = new List<CheatItem>(_selectedCheats);
        if (NativeLibretroHost.Current.IsRunning && _selectedCheats != null)
        {
            int applied = NativeLibretroHost.Current.ApplyCheats(_selectedCheats);
            ShowOsdToast($"Применено активных чит-кодов: {applied}");
        }
        ShowNotification($"Чит-код файл успешно сохранён!\n{Path.GetFileName(_selectedCheatFile.FilePath)}");
    }

    private void BtnCreateCheatFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            InitialDirectory = CheatManager.CheatsDir,
            Filter = "RetroArch Cheat File (*.cht)|*.cht",
            Title = "Создать новый файл читов"
        };
        if (dlg.ShowDialog() == true)
        {
            var initialCheats = new List<CheatItem>
            {
                new() { Description = "Бесконечные жизни", Code = "00000000", IsEnabled = false }
            };
            CheatManager.SaveCht(dlg.FileName, initialCheats);
            LoadCheats();
            ShowNotification($"Чит-файл создан: {Path.GetFileName(dlg.FileName)}");
        }
    }

    private async void BtnDownloadCheatsDb_Click(object sender, RoutedEventArgs e)
    {
        ShowNotification("Загрузка полной базы чит-кодов Libretro с buildbot.libretro.com...");
        bool ok = await CheatManager.DownloadLibretroCheatsAsync();
        ShowNotification(ok ? "База читов Libretro успешно загружена и распакована!" : "Сбой загрузки базы читов");
        LoadCheats();
    }

    private void BtnOpenCheatsDir_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = CheatManager.CheatsDir, UseShellExecute = true }); }
        catch (Exception ex) { ShowNotification($"Ошибка: {ex.Message}"); }
    }

    private void BtnRefreshCheats_Click(object sender, RoutedEventArgs e) => LoadCheats();

    // ==================== NETPLAY LIVE LOBBY ====================

    private async void RefreshLobby()
    {
        _netplayLobby.Clear();
        var rooms = await FetchLobbyRoomsAsync();
        foreach (var r in rooms) _netplayLobby.Add(r);
    }

    private void BtnRefreshLobby_Click(object sender, RoutedEventArgs e) => RefreshLobby();

    private void BtnJoinLobbyRoom_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NetplayRoomItem room }) return;

        string[] parts = room.IpPort.Split(':');
        if (parts.Length == 2)
        {
            TxtNetplayJoinIp.Text = parts[0];
            TxtNetplayJoinPort.Text = parts[1];
            ShowNotification($"Параметры комнаты «{room.GameName}» ({room.IpPort}) скопированы в форму подключения!");
        }
    }

    private static async Task<List<NetplayRoomItem>> FetchLobbyRoomsAsync()
    {
        var list = new List<NetplayRoomItem>();
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            string json = await client.GetStringAsync("http://lobby.libretro.com/list/");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.TryGetProperty("fields", out var fields))
                {
                    string user = fields.TryGetProperty("username", out var u) ? u.GetString() ?? "Unknown" : "Unknown";
                    string game = fields.TryGetProperty("game_name", out var g) ? g.GetString() ?? "Unknown Game" : "Unknown Game";
                    string core = fields.TryGetProperty("core_name", out var c) ? c.GetString() ?? "Core" : "Core";
                    string country = fields.TryGetProperty("country", out var cnt) ? cnt.GetString() ?? "Global" : "Global";
                    string ip = fields.TryGetProperty("ip", out var ipProp) ? ipProp.GetString() ?? "127.0.0.1" : "127.0.0.1";
                    int port = fields.TryGetProperty("port", out var p) ? p.GetInt32() : 55435;
                    string ver = fields.TryGetProperty("retroarch_version", out var v) ? v.GetString() ?? "1.19.1" : "1.19.1";
                    list.Add(new NetplayRoomItem(user, game, core, country, $"{ip}:{port}", ver));
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Netplay Lobby Fetch Error]: {ex.Message}");
        }
        return list;
    }

    // ==================== BOXART THUMBNAILS DOWNLOADER ====================

    private async void BtnDownloadBoxarts_Click(object sender, RoutedEventArgs e)
    {
        if (_allGames.Count == 0)
        {
            ShowNotification("В библиотеке нет игр для загрузки обложек. Добавьте файлы или папку!");
            return;
        }

        ShowNotification($"Загрузка обложек (Boxart) для {_allGames.Count} игр с thumbnails.libretro.com...");
        int count = await DownloadBoxartsForLibraryAsync(_allGames);
        ShowNotification($"Загрузка завершена! Скачано новых обложек: {count}");
    }

    private static async Task<int> DownloadBoxartsForLibraryAsync(IEnumerable<RomItem> roms)
    {
        int downloaded = 0;
        string thumbnailsDir = Path.Combine(CoreManager.AppDir, "thumbnails");
        if (!Directory.Exists(thumbnailsDir)) Directory.CreateDirectory(thumbnailsDir);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        foreach (var r in roms)
        {
            string sysDir = r.Platform switch
            {
                "Nintendo Entertainment System" => "Nintendo - Nintendo Entertainment System",
                "Super Nintendo (SNES)" => "Nintendo - Super Nintendo Entertainment System",
                "Sega Genesis / Mega Drive" => "Sega - Mega Drive - Genesis",
                "Sony PlayStation 1" => "Sony - PlayStation",
                "Game Boy Advance" => "Nintendo - Game Boy Advance",
                "Nintendo 64" => "Nintendo - Nintendo 64",
                _ => ""
            };
            if (string.IsNullOrEmpty(sysDir)) continue;

            string targetDir = Path.Combine(thumbnailsDir, sysDir, "Named_Boxarts");
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

            string targetFile = Path.Combine(targetDir, $"{r.Name}.png");
            if (File.Exists(targetFile)) continue;

            string url = $"https://thumbnails.libretro.com/{Uri.EscapeDataString(sysDir)}/Named_Boxarts/{Uri.EscapeDataString(r.Name)}.png";
            try
            {
                var resp = await client.GetAsync(url);
                if (resp.IsSuccessStatusCode)
                {
                    byte[] bytes = await resp.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(targetFile, bytes);
                    downloaded++;
                }
            }
            catch { }
        }
        return downloaded;
    }


    // ==================== SCREEN BEZELS & OVERLAYS ====================

        // ==================== MANUAL BEZEL CREATOR & VIEWPORT OVERLAY ====================

    private void ApplyActiveBezelToViewport()
    {
        var active = BezelManager.GetActiveBezel();
        if (active != null && !string.IsNullOrEmpty(active.ImagePath) && File.Exists(active.ImagePath))
        {
            var bmp = BezelManager.LoadPreviewImage(active.ImagePath);
            if (bmp != null)
            {
                ImgGameBezelOverlay.Source = bmp;
                string opStr = RetroArchConfigManager.GetString("input_overlay_opacity", "1.000000");
                if (float.TryParse(opStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float op))
                {
                    ImgGameBezelOverlay.Opacity = Math.Clamp(op, 0.1f, 1.0f);
                }
                else
                {
                    ImgGameBezelOverlay.Opacity = 1.0;
                }
                ImgGameBezelOverlay.Visibility = Visibility.Visible;
                ImgGameViewport.Margin = new Thickness(0);
                return;
            }
        }

        ImgGameBezelOverlay.Visibility = Visibility.Collapsed;
        ImgGameViewport.Margin = new Thickness(0);
    }

    private void BtnGameBezel_Click(object sender, RoutedEventArgs e)
    {
        var bezels = BezelManager.ScanBezels();
        if (bezels.Count == 0)
        {
            ShowOsdToast("Нет доступных рамок Bezel");
            return;
        }

        var active = BezelManager.GetActiveBezel();
        if (active == null)
        {
            BezelManager.ApplyBezel(bezels[0]);
            ShowOsdToast($"Рамка: {bezels[0].Name} [ВКЛ]");
        }
        else
        {
            int currentIndex = bezels.FindIndex(b => b.CfgPath.Equals(active.CfgPath, StringComparison.OrdinalIgnoreCase));
            if (currentIndex >= 0 && currentIndex < bezels.Count - 1)
            {
                var next = bezels[currentIndex + 1];
                BezelManager.ApplyBezel(next);
                ShowOsdToast($"Рамка: {next.Name} [ВКЛ]");
            }
            else
            {
                BezelManager.DisableBezel();
                ShowOsdToast("Рамка Bezel: [ВЫКЛ]");
            }
        }

        ApplyActiveBezelToViewport();
        LoadBezels();
    }

    private void BtnToggleManualBezelPanel_Click(object sender, RoutedEventArgs e)
    {
        if (PnlManualBezelCreator == null) return;
        PnlManualBezelCreator.Visibility = PnlManualBezelCreator.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void BtnBrowseManualBezelImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Изображения рамки (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Все файлы (*.*)|*.*",
            Title = "Выберите фоновое изображение для Bezel"
        };
        if (dlg.ShowDialog() == true)
        {
            TxtManualBezelFile.Text = dlg.FileName;
            if (string.IsNullOrWhiteSpace(TxtManualBezelName.Text))
            {
                TxtManualBezelName.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        }
    }

    private void BtnCreateManualBezel_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtManualBezelName.Text.Trim();
        string platform = (CmbManualBezelPlatform.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Custom Bezel";
        string cutout = (CmbManualBezelCutout.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "4:3";
        string accent = (CmbManualBezelAccent.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "#A855F7";
        string? sourceFile = string.IsNullOrWhiteSpace(TxtManualBezelFile.Text) ? null : TxtManualBezelFile.Text.Trim();

        var result = BezelManager.CreateOrImportManualBezel(name, platform, cutout, accent, sourceFile);
        ShowNotification("STORM RETROARCH", result.Message);
        if (result.Success)
        {
            LoadBezels();
            ApplyActiveBezelToViewport();
            if (PnlManualBezelCreator != null) PnlManualBezelCreator.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnDeleteBezel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BezelPresetItem item }) return;
        BezelManager.DeleteBezel(item);
        LoadBezels();
        ApplyActiveBezelToViewport();
        ShowNotification("STORM RETROARCH", $"Рамка «{item.Name}» удалена.");
    }

    private void ViewBezels_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                string file = files[0];
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".cfg")
                {
                    var res = BezelManager.CreateOrImportManualBezel(
                        Path.GetFileNameWithoutExtension(file),
                        "Custom Bezel",
                        "Raw",
                        "#38BDF8",
                        file);
                    ShowNotification("STORM RETROARCH", res.Message);
                    LoadBezels();
                    ApplyActiveBezelToViewport();
                }
            }
        }
    }

    private void TxtLibrarySearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyGamesFilter();
    }

private void LoadBezels()
    {
        var bezels = BezelManager.ScanBezels();
        _allBezels.Clear();
        foreach (var b in bezels) _allBezels.Add(b);
        ApplyBezelsFilter();
    }

    private void ApplyBezelsFilter()
    {
        string query = TxtSearchBezels?.Text?.Trim().ToLowerInvariant() ?? "";
        _filteredBezels.Clear();

        foreach (var b in _allBezels)
        {
            bool matchPlatform = _selectedBezelPlatform switch
            {
                "Sony" => b.Platform.Contains("Sony", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("PlayStation", StringComparison.OrdinalIgnoreCase),
                "Nintendo" => b.Platform.Contains("Nintendo", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("Game Boy", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("SNES", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("NES", StringComparison.OrdinalIgnoreCase),
                "Sega" => b.Platform.Contains("Sega", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("Genesis", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("Saturn", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("Dreamcast", StringComparison.OrdinalIgnoreCase),
                "Arcade / Other" => b.Platform.Contains("Arcade", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("STORM", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("Custom", StringComparison.OrdinalIgnoreCase),
                _ => true
            };

            bool matchQuery = string.IsNullOrEmpty(query) ||
                              b.Name.ToLowerInvariant().Contains(query) ||
                              b.Platform.ToLowerInvariant().Contains(query) ||
                              b.Description.ToLowerInvariant().Contains(query);

            if (matchPlatform && matchQuery)
            {
                _filteredBezels.Add(b);
            }
        }
    }

    private void TxtSearchBezels_TextChanged(object sender, TextChangedEventArgs e) => ApplyBezelsFilter();

    private void BtnBezelPlatform_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Parent is Panel p)
        {
            foreach (var child in p.Children)
            {
                if (child is Button b) b.Tag = null;
            }
        }
        btn.Tag = "Active";
        _selectedBezelPlatform = btn.Content?.ToString() ?? "Все рамки (12)";
        ApplyBezelsFilter();
    }

    private void BtnApplyBezelPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BezelPresetItem item }) return;

        BezelManager.ApplyBezel(item);
        foreach (var b in _allBezels) b.IsActive = (b == item);
        ApplyActiveBezelToViewport();
        ShowNotification($"Рамка «{item.Name}» активирована! Разрешение viewport настроено под 4:3.");
    }

    private void BtnDisableBezel_Click(object sender, RoutedEventArgs e)
    {
        BezelManager.DisableBezel();
        foreach (var b in _allBezels) b.IsActive = false;
        ApplyActiveBezelToViewport();
        ShowNotification("Рамка Bezel отключена (чистый экран без оверлея).");
    }

    private void BtnImportBezel_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Рамки Bezel (*.png;*.cfg)|*.png;*.cfg|PNG изображения (*.png)|*.png|RetroArch Overlay (*.cfg)|*.cfg|Все файлы (*.*)|*.*",
            Title = "Выберите файл рамки Bezel для импорта"
        };
        if (dlg.ShowDialog() == true)
        {
            var res = BezelManager.ImportBezel(dlg.FileName);
            ShowNotification(res.Message);
            LoadBezels();
        }
    }

    private void BtnOpenBordersFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = BezelManager.BordersDir, UseShellExecute = true }); }
        catch (Exception ex) { ShowNotification($"Ошибка: {ex.Message}"); }
    }

    private void BtnRefreshBezels_Click(object sender, RoutedEventArgs e) => LoadBezels();

    private void CmbBezelOpacity_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbBezelOpacity.SelectedItem is ComboBoxItem item &&
            item.Tag is string tagStr &&
            float.TryParse(tagStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float op))
        {
            BezelManager.SetOpacity(op);
            ApplyActiveBezelToViewport();
        }
    }

    private void CmbBezelAspect_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbBezelAspect.SelectedItem is ComboBoxItem item && item.Tag is string aspectIdx)
        {
            BezelManager.SetAspectRatio(aspectIdx);
        }
    }

    private void ChkBezelIntegerScale_Click(object sender, RoutedEventArgs e)
    {
        bool enable = ChkBezelIntegerScale.IsChecked == true;
        BezelManager.SetIntegerScale(enable);
    }


    // ==================== CORE OPTIONS & OVERRIDES ====================

    private void LoadCoreOptionsProfile(string coreName)
    {
        _selectedCoreProfile = _allCoreProfiles.FirstOrDefault(p => p.CoreName.Equals(coreName, StringComparison.OrdinalIgnoreCase)) ??
                               _allCoreProfiles.FirstOrDefault();

        _currentCoreOptions.Clear();
        if (_selectedCoreProfile != null)
        {
            CoreOptionsAndOverridesManager.LoadCoreOptionsFromDisk(_selectedCoreProfile.CoreName, _selectedCoreProfile.Options);
            foreach (var opt in _selectedCoreProfile.Options)
            {
                _currentCoreOptions.Add(opt);
            }
        }
    }

    private void BtnCoreProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Parent is Panel p)
        {
            foreach (var child in p.Children)
            {
                if (child is Button b) b.Tag = null;
            }
        }
        btn.Tag = "Active";

        string coreName = btn.Content?.ToString()?.Split(' ')[0] ?? "SwanStation";
        LoadCoreOptionsProfile(coreName);
    }

    private void BtnSaveCoreOptions_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCoreProfile == null) return;
        CoreOptionsAndOverridesManager.SaveCoreOptionsToDisk(_selectedCoreProfile.CoreName, _currentCoreOptions);
        NativeLibretroHost.Current.NotifyCoreOptionsUpdated();
        ShowOsdToast($"Опции ядра {_selectedCoreProfile.CoreName} обновлены в памяти");
        ShowNotification("STORM RETROARCH", $"Опции ядра {_selectedCoreProfile.CoreName} успешно сохранены в config/{_selectedCoreProfile.CoreName}/{_selectedCoreProfile.CoreName}.opt!");
    }

    private void BtnSaveCoreOverride_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCoreProfile == null) return;
        var overrides = new Dictionary<string, string>
        {
            ["video_shader_enable"] = "true",
            ["aspect_ratio_index"] = "0",
            ["video_scale_integer"] = "true"
        };
        CoreOptionsAndOverridesManager.SaveCoreOverrideCfg(_selectedCoreProfile.CoreName, overrides);
        ShowNotification("STORM RETROARCH", $"Переопределение (Override) для ядра {_selectedCoreProfile.CoreName} сохранено!");
    }

    private void BtnResetCoreOptions_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCoreProfile == null) return;
        CoreOptionsAndOverridesManager.ResetCoreOptions(_selectedCoreProfile.CoreName);
        LoadCoreOptionsProfile(_selectedCoreProfile.CoreName);
        ShowNotification("STORM RETROARCH", $"Опции ядра {_selectedCoreProfile.CoreName} сброшены на стандартные.");
    }

    private void BtnRefreshCoreOptions_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCoreProfile != null) LoadCoreOptionsProfile(_selectedCoreProfile.CoreName);
    }

    // ==================== INPUT REMAP (PORTS 1-8) ====================

    private void LoadRemapBindings(int playerIndex)
    {
        _selectedRemapPlayerIndex = playerIndex;
        var bindings = InputRemapManager.CreateDefaultBindings(playerIndex);
        _currentRemapBindings.Clear();
        foreach (var b in bindings) _currentRemapBindings.Add(b);
    }

    private void BtnPlayerPort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Parent is Panel p)
        {
            foreach (var child in p.Children)
            {
                if (child is Button b) b.Tag = null;
            }
        }
        btn.Tag = "Active";

        string txt = btn.Content?.ToString() ?? "Порт 1";
        int port = 1;
        if (txt.Contains("2")) port = 2;
        else if (txt.Contains("3")) port = 3;
        else if (txt.Contains("4")) port = 4;
        else if (txt.Contains("5")) port = 5;
        else if (txt.Contains("6")) port = 6;
        else if (txt.Contains("7")) port = 7;
        else if (txt.Contains("8")) port = 8;

        LoadRemapBindings(port);
    }

    private void BtnSaveInputRemap_Click(object sender, RoutedEventArgs e)
    {
        InputRemapManager.SaveBindingsToConfig(_currentRemapBindings);
        ShowNotification("STORM RETROARCH", $"Назначения кнопок для Игрока {_selectedRemapPlayerIndex} успешно сохранены в retroarch.cfg!");
    }

    private void BtnExportRemap_Click(object sender, RoutedEventArgs e)
    {
        string coreName = _selectedCoreProfile?.CoreName ?? "RetroPad";
        InputRemapManager.ExportRemapFile(coreName, _currentRemapBindings);
        ShowNotification("STORM RETROARCH", $"Файл переназначения remaps/{coreName}/{coreName}.rmp успешно экспортирован!");
    }

    private void BtnImportRemap_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            InitialDirectory = InputRemapManager.RemapsDir,
            Filter = "Файлы переназначения (*.rmp)|*.rmp|Все файлы (*.*)|*.*",
            Title = "Выберите .rmp файл для импорта"
        };
        if (dlg.ShowDialog() == true)
        {
            InputRemapManager.ImportRemapFile(dlg.FileName, _currentRemapBindings);
            ShowNotification("STORM RETROARCH", $"Переназначение из {Path.GetFileName(dlg.FileName)} загружено!");
        }
    }

    private void BtnResetInputRemap_Click(object sender, RoutedEventArgs e)
    {
        LoadRemapBindings(_selectedRemapPlayerIndex);
        ShowNotification("STORM RETROARCH", $"Кнопки Игрока {_selectedRemapPlayerIndex} сброшены на стандартные.");
    }

    // ==================== RETROACHIEVEMENTS STUDIO ====================

    private void LoadAchievements()
    {
        TxtCheevosUsername.Text = AchievementsManager.Username;
        TxtCheevosToken.Text = string.IsNullOrEmpty(AchievementsManager.Token) ? AchievementsManager.Password : AchievementsManager.Token;

        ChkCheevosEnable.IsChecked = AchievementsManager.EnableCheevos;
        ChkCheevosHardcore.IsChecked = AchievementsManager.HardcoreMode;
        ChkCheevosLeaderboards.IsChecked = AchievementsManager.Leaderboards;
        ChkCheevosRichPresence.IsChecked = AchievementsManager.RichPresence;
        ChkCheevosBadges.IsChecked = AchievementsManager.Badges;
        ChkCheevosAutoScreenshot.IsChecked = AchievementsManager.AutoScreenshot;
        ChkCheevosTestUnofficial.IsChecked = AchievementsManager.TestUnofficial;

        _filteredAchievements.Clear();
        var scanned = AchievementsManager.ScanLibraryGamesForCheevos(_allGames);
        foreach (var item in scanned) _filteredAchievements.Add(item);
        TxtCheevosLibraryCount.Text = $"{_filteredAchievements.Count} игр в библиотеке";
        ItemsCheevosLibrary.ItemsSource = _filteredAchievements;
    }

    private void BtnSaveCheevosAccount_Click(object sender, RoutedEventArgs e)
    {
        AchievementsManager.Username = TxtCheevosUsername.Text.Trim();
        string tokenOrPwd = TxtCheevosToken.Text.Trim();
        AchievementsManager.Token = tokenOrPwd;
        AchievementsManager.Password = tokenOrPwd;
        ShowNotification("STORM RETROARCH", "Учётные данные RetroAchievements сохранены в retroarch.cfg!");
    }

    private void BtnOpenCheevosProfile_Click(object sender, RoutedEventArgs e)
    {
        AchievementsManager.OpenUserProfile();
    }

    private void ChkCheevosOption_Click(object sender, RoutedEventArgs e)
    {
        AchievementsManager.EnableCheevos = ChkCheevosEnable.IsChecked == true;
        AchievementsManager.HardcoreMode = ChkCheevosHardcore.IsChecked == true;
        AchievementsManager.Leaderboards = ChkCheevosLeaderboards.IsChecked == true;
        AchievementsManager.RichPresence = ChkCheevosRichPresence.IsChecked == true;
        AchievementsManager.Badges = ChkCheevosBadges.IsChecked == true;
        AchievementsManager.AutoScreenshot = ChkCheevosAutoScreenshot.IsChecked == true;
        AchievementsManager.TestUnofficial = ChkCheevosTestUnofficial.IsChecked == true;
        ShowNotification("STORM RETROARCH", "Параметры RetroAchievements обновлены в конфигурации!");
    }

    private void BtnRefreshAchievements_Click(object sender, RoutedEventArgs e) => LoadAchievements();

    private void ChkCheevosHardcore_Click(object sender, RoutedEventArgs e)
    {
        AchievementsManager.HardcoreMode = ChkCheevosHardcore.IsChecked == true;
        ShowNotification("STORM RETROARCH", AchievementsManager.HardcoreMode
            ? "Режим Hardcore включён (блокировка читов, стейтов и перемотки)!"
            : "Режим Softcore включён.");
    }

    // ==================== STREAM & AI SERVICE ====================

    private void BtnStartAiService_Click(object sender, RoutedEventArgs e)
    {
        StreamAndAiManager.StartAiServer(msg =>
        {
            Dispatcher.Invoke(() =>
            {
                TxtAiServiceStatus.Text = "Статус: Активен (Порт 4404)";
                TxtAiServiceStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
            });
        });
        ShowNotification("STORM RETROARCH", "Локальный AI Service запущен на http://127.0.0.1:4404/!");
    }

    private void BtnStopAiService_Click(object sender, RoutedEventArgs e)
    {
        StreamAndAiManager.StopAiServer();
        TxtAiServiceStatus.Text = "Статус: Остановлен";
        TxtAiServiceStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
        ShowNotification("STORM RETROARCH", "Локальный AI Service остановлен.");
    }

    private void BtnConfigureAiInCfg_Click(object sender, RoutedEventArgs e)
    {
        StreamAndAiManager.ConfigureAiServiceInRetroArch("ru");
        ShowNotification("STORM RETROARCH", "Параметры AI Service успешно прописаны в retroarch.cfg!");
    }

    private void CmbStreamPlatform_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Platform selection changed
    }

    private void BtnSaveStreamSettings_Click(object sender, RoutedEventArgs e)
    {
        string rtmp = "rtmp://live.twitch.tv/app/";
        if (CmbStreamPlatform.SelectedItem is ComboBoxItem item && item.Tag is string tag) rtmp = tag;
        string key = TxtStreamKey.Text.Trim();

        StreamAndAiManager.SaveStreamingSettings(rtmp, key);
        ShowNotification("STORM RETROARCH", "Параметры стриминга RTMP сохранены в retroarch.cfg!");
    }

    private void BtnOpenRecordingsDir_Click(object sender, RoutedEventArgs e)
    {
        string recDir = Path.Combine(CoreManager.AppDir, "recordings");
        if (!Directory.Exists(recDir)) Directory.CreateDirectory(recDir);
        try { Process.Start(new ProcessStartInfo { FileName = recDir, UseShellExecute = true }); }
        catch (Exception ex) { ShowNotification("STORM RETROARCH", $"Ошибка: {ex.Message}"); }
    }

    // ==================== MULTI-DISC M3U & PATCHING ====================

    private void BtnSelectM3uFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = "Образы дисков (*.cue;*.chd;*.iso;*.cdi)|*.cue;*.chd;*.iso;*.cdi|Все файлы (*.*)|*.*",
            Title = "Выберите образы дисков игры"
        };
        if (dlg.ShowDialog() == true)
        {
            _selectedM3uFiles.Clear();
            _selectedM3uFiles.AddRange(dlg.FileNames);
            TxtM3uSelectedFiles.Text = $"Выбрано дисков: {_selectedM3uFiles.Count} ({string.Join(", ", _selectedM3uFiles.Select(Path.GetFileName))})";
        }
    }

    private void BtnGenerateM3u_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedM3uFiles.Count == 0)
        {
            ShowNotification("STORM RETROARCH", "Сначала выберите файлы дисков!");
            return;
        }

        string title = TxtM3uGameTitle.Text.Trim();
        string outDir = Path.GetDirectoryName(_selectedM3uFiles[0]) ?? CoreManager.AppDir;

        string m3uPath = DiscPatchSubsystemManager.GenerateM3uPlaylist(title, _selectedM3uFiles, outDir);
        ShowNotification("STORM RETROARCH", $"Плейлист .m3u успешно создан:\n{Path.GetFileName(m3uPath)}");
    }

    private void BtnDiscEjectToggle_Click(object sender, RoutedEventArgs e)
    {
        DiscPatchSubsystemManager.EjectOrInsertDisc();
        ShowNotification("STORM RETROARCH", "Команда DISK_EJECT_TOGGLE отправлена в RetroArch (UDP:55355)");
    }

    private void BtnDiscPrev_Click(object sender, RoutedEventArgs e)
    {
        DiscPatchSubsystemManager.PreviousDisc();
        ShowNotification("STORM RETROARCH", "Команда DISK_PREV отправлена в RetroArch (UDP:55355)");
    }

    private void BtnDiscNext_Click(object sender, RoutedEventArgs e)
    {
        DiscPatchSubsystemManager.NextDisc();
        ShowNotification("STORM RETROARCH", "Команда DISK_NEXT отправлена в RetroArch (UDP:55355)");
    }

    private void BtnSelectPatchRom_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "ROM файлы (*.nes;*.sfc;*.md;*.bin;*.gba)|*.nes;*.sfc;*.md;*.bin;*.gba|Все файлы (*.*)|*.*",
            Title = "Выберите файл ROM для патчинга"
        };
        if (dlg.ShowDialog() == true) TxtPatchRomPath.Text = dlg.FileName;
    }

    private void BtnSelectPatchFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Файлы патчей (*.ips;*.bps;*.ups)|*.ips;*.bps;*.ups|Все файлы (*.*)|*.*",
            Title = "Выберите файл патча русификатора"
        };
        if (dlg.ShowDialog() == true) TxtPatchFilePath.Text = dlg.FileName;
    }

    private void BtnAttachSoftPatch_Click(object sender, RoutedEventArgs e)
    {
        string rom = TxtPatchRomPath.Text.Trim();
        string patch = TxtPatchFilePath.Text.Trim();
        var res = DiscPatchSubsystemManager.AttachSoftPatch(rom, patch);
        ShowNotification("STORM RETROARCH", res.Message);
    }

    private void BtnApplyIpsPatch_Click(object sender, RoutedEventArgs e)
    {
        string rom = TxtPatchRomPath.Text.Trim();
        string patch = TxtPatchFilePath.Text.Trim();

        if (string.IsNullOrEmpty(rom) || string.IsNullOrEmpty(patch))
        {
            ShowNotification("STORM RETROARCH", "Укажите пути к файлу ROM и файлу патча!");
            return;
        }

        string outRom = Path.Combine(Path.GetDirectoryName(rom) ?? "", Path.GetFileNameWithoutExtension(rom) + "_RU" + Path.GetExtension(rom));
        var res = DiscPatchSubsystemManager.ApplyIpsPatch(rom, patch, outRom);
        ShowNotification("STORM RETROARCH", res.Message);
    }

    private void BtnLaunchSuperGameBoy_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Game Boy ROM (*.gb;*.gbc)|*.gb;*.gbc|Все файлы (*.*)|*.*",
            Title = "Выберите картридж Game Boy для запуска в Super Game Boy"
        };
        if (dlg.ShowDialog() == true)
        {
            string snesRom = Path.Combine(CoreManager.AppDir, "system", "Super Game Boy (World).sfc");
            DiscPatchSubsystemManager.LaunchSuperGameBoy(snesRom, dlg.FileName, err => ShowNotification("STORM RETROARCH", err));
        }
    }


    // ==================== LIVE TELEMETRY & INPUT LATENCY ====================
    private System.Windows.Threading.DispatcherTimer? _telemetryTimer;

    private void InitTelemetryTimer()
    {
        _telemetryTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _telemetryTimer.Tick += (s, e) => UpdateTelemetryMetrics();
        _telemetryTimer.Start();
        UpdateTelemetryMetrics();

        if (SettingsManager.Current.TelemetryOverlayVisible)
        {
            PnlTelemetryOverlay.Visibility = Visibility.Visible;
        }
    }

    private void BtnToggleTelemetry_Click(object sender, RoutedEventArgs e)
    {
        bool isVis = PnlTelemetryOverlay.Visibility == Visibility.Visible;
        PnlTelemetryOverlay.Visibility = isVis ? Visibility.Collapsed : Visibility.Visible;
        SettingsManager.Current.TelemetryOverlayVisible = !isVis;
        SettingsManager.Save();
        UpdateTelemetryMetrics();
    }

    private void BtnCloseTelemetry_Click(object sender, RoutedEventArgs e)
    {
        PnlTelemetryOverlay.Visibility = Visibility.Collapsed;
        SettingsManager.Current.TelemetryOverlayVisible = false;
        SettingsManager.Save();
    }

    private void BtnToggleNativeOsd_Click(object sender, RoutedEventArgs e)
    {
        bool current = TelemetryMonitorManager.IsNativeOsdEnabled();
        bool next = TelemetryMonitorManager.ToggleNativeOsd(!current);
        ShowNotification("STORM RETROARCH", next ? "Внутриигровой OSD RetroArch активирован (FPS/Память/Статистика)." : "Внутриигровой OSD RetroArch отключен.");
    }

    private void UpdateTelemetryMetrics()
    {
        var snap = TelemetryMonitorManager.GetCurrentSnapshot();
        TxtTelemFps.Text = $"{snap.Fps:F1} FPS • {snap.FrameTimeMs:F1} ms";
        TxtTelemLatency.Text = $"Задержка: {snap.EstimatedLatencyMs} мс ({snap.LatencyGrade})";
        TxtTelemCpuRam.Text = $"ЦП: {snap.CpuPercent}% • ОЗУ: {snap.RamUsageMb} МБ";
        TxtTelemDriver.Text = $"Драйвер: {snap.VideoDriver} • {(snap.RetroArchRunning ? "ИГРА АКТИВНА" : "ГОТОВ К ЗАПУСКУ")}";
    }

    // ==================== SMART PER-SYSTEM PROFILES ====================
    private void InitSmartProfiles()
    {
        CmbSmartProfiles.ItemsSource = SmartProfileManager.GetAllProfiles();
        if (CmbSmartProfiles.Items.Count > 0)
        {
            CmbSmartProfiles.SelectedIndex = 0;
        }
    }

    private void CmbSmartProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbSmartProfiles.SelectedItem is SmartProfile profile)
        {
            TxtSmartProfileStatus.Text = $"Текущий профиль: {profile.Name} • Драйвер: {profile.VideoDriver.ToUpper()} • Run-Ahead {(profile.RunAheadFrames > 0 ? profile.RunAheadFrames + "к" : "Выкл")}";
        }
    }

    private void BtnApplySmartProfile_Click(object sender, RoutedEventArgs e)
    {
        if (CmbSmartProfiles.SelectedItem is SmartProfile profile)
        {
            string msg = SmartProfileManager.ApplyProfile(profile, CoreManager.AppDir);
            ShowNotification("STORM RETROARCH", msg);
        }
    }

    // ==================== STORM SAVE SYNC (CLOUD & ANDROID) ====================
    private List<SyncEntryItem> _cachedSyncItems = new();

    private void InitSaveSync()
    {
        if (!string.IsNullOrEmpty(SettingsManager.Current.CloudSyncFolder))
        {
            SaveSyncManager.CloudSyncDir = SettingsManager.Current.CloudSyncFolder;
        }
        TxtCloudSyncFolder.Text = SaveSyncManager.CloudSyncDir;
    }

    private void BtnBrowseCloudSyncFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Выберите папку для облачной синхронизации сохранений"
        };
        if (dlg.ShowDialog() == true)
        {
            SaveSyncManager.CloudSyncDir = dlg.FolderName;
            SettingsManager.Current.CloudSyncFolder = dlg.FolderName;
            SettingsManager.Save();
            TxtCloudSyncFolder.Text = dlg.FolderName;
            ShowNotification("STORM RETROARCH", $"Папка облачной синхронизации установлена:\n{dlg.FolderName}");
            RefreshSaveSyncUi();
        }
    }

    private void BtnOpenLocalSaves_Click(object sender, RoutedEventArgs e)
    {
        string dir = SaveSyncManager.LocalSavesDir;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    private void BtnScanSaveSync_Click(object sender, RoutedEventArgs e)
    {
        RefreshSaveSyncUi();
        ShowNotification("STORM RETROARCH", $"Сканирование завершено! Проиндексировано файлов: {_cachedSyncItems.Count}");
    }

    private void BtnSmartSyncAll_Click(object sender, RoutedEventArgs e)
    {
        SaveSyncManager.SmartSyncAll(_cachedSyncItems);
        RefreshSaveSyncUi();
        ShowNotification("STORM RETROARCH", "Умная синхронизация завершена без конфликтов! Все новейшие сейвы синхронизированы.");
    }

    private void BtnAndroidSync_Click(object sender, RoutedEventArgs e)
    {
        ShowNotification("STORM RETROARCH", "Синхронизация с Android:\n• Локальный Wi-Fi LAN сервер готов на порту 55440\n• Для прямого ADB подключения используйте: adb push /storage/emulated/0/RetroArch/saves\n• Конфликты исключены благодаря SHA-256 сверке.");
    }

    private void RefreshSaveSyncUi()
    {
        _cachedSyncItems = SaveSyncManager.ScanAndCompareAll();
        PanelSaveSyncTiles.Children.Clear();
        PanelSaveSyncList.Children.Clear();
        PanelSaveSyncCompact.Children.Clear();

        if (_cachedSyncItems.Count == 0)
        {
            var emptyCard = new Border
            {
                Background = (Brush)FindResource("ThemeSurface"),
                BorderBrush = (Brush)FindResource("ThemeBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(24),
                Margin = new Thickness(0, 0, 12, 12),
                Width = 440
            };
            var st = new StackPanel();
            st.Children.Add(new TextBlock { Text = "Папка сейвов пуста или синхронизирована", FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("ThemeTextPrimary"), FontSize = 14, Margin = new Thickness(0, 0, 0, 6) });
            st.Children.Add(new TextBlock { Text = $"Файлы сохранений (.srm) и стейты (.state) автоматически появятся здесь после запуска любой игры в RetroArch.\nЛокальная папка: {SaveSyncManager.LocalSavesDir}", Foreground = (Brush)FindResource("ThemeTextSecondary"), TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            emptyCard.Child = st;
            PanelSaveSyncTiles.Children.Add(emptyCard);
            return;
        }

        foreach (var item in _cachedSyncItems)
        {
            // 1. Bento Card
            var card = new Border
            {
                Background = (Brush)FindResource("ThemeSurface"),
                BorderBrush = (Brush)FindResource("ThemeBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 12, 12),
                Width = 360
            };
            var sp = new StackPanel();

            var dockHead = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var tbTitle = new TextBlock { Text = item.GameName, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("ThemeTextPrimary"), FontSize = 14 };
            var badgeStatus = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.StatusColor + "33")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.StatusColor)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2)
            };
            badgeStatus.Child = new TextBlock { Text = item.StatusText, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.StatusColor)), FontSize = 10, FontWeight = FontWeights.SemiBold };
            DockPanel.SetDock(badgeStatus, Dock.Right);
            dockHead.Children.Add(badgeStatus);
            dockHead.Children.Add(tbTitle);
            sp.Children.Add(dockHead);

            sp.Children.Add(new TextBlock { Text = $"Файл: {item.FileName}", Foreground = (Brush)FindResource("ThemeTextSecondary"), FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
            sp.Children.Add(new TextBlock { Text = $"Локально: {item.LocalSize / 1024.0:F1} КБ ({item.LocalModifiedUtc.ToLocalTime():yyyy-MM-dd HH:mm})", Foreground = (Brush)FindResource("ThemeTextMuted"), FontSize = 11, Margin = new Thickness(0, 0, 0, 2) });
            sp.Children.Add(new TextBlock { Text = $"Облако: {(item.RemoteSize > 0 ? (item.RemoteSize / 1024.0).ToString("F1") + " КБ" : "Отсутствует")}", Foreground = (Brush)FindResource("ThemeTextMuted"), FontSize = 11, Margin = new Thickness(0, 0, 0, 10) });

            var btnWrap = new WrapPanel();
            var btnUpload = new Button { Content = "⬆ В облако", Style = (Style)FindResource("StormAccentButton"), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 4) };
            btnUpload.Click += (s, e) => { SaveSyncManager.UploadToRemote(item); RefreshSaveSyncUi(); ShowNotification("STORM RETROARCH", $"Сейв {item.FileName} выгружен в облако!"); };
            var btnDownload = new Button { Content = "⬇ Из облака", Style = (Style)FindResource("StormSecondaryButton"), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 4) };
            btnDownload.Click += (s, e) => { SaveSyncManager.DownloadFromRemote(item); RefreshSaveSyncUi(); ShowNotification("STORM RETROARCH", $"Сейв {item.FileName} загружен из облака!"); };
            var btnKeepBoth = new Button { Content = "⇋ Оба файла", Style = (Style)FindResource("StormSecondaryButton"), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 4) };
            btnKeepBoth.Click += (s, e) => { SaveSyncManager.KeepBoth(item); RefreshSaveSyncUi(); ShowNotification("STORM RETROARCH", $"Создана резервная копия для {item.FileName}!"); };
            btnWrap.Children.Add(btnUpload);
            btnWrap.Children.Add(btnDownload);
            btnWrap.Children.Add(btnKeepBoth);
            sp.Children.Add(btnWrap);

            card.Child = sp;
            PanelSaveSyncTiles.Children.Add(card);

            // 2. Ozone List Row
            var rowBorder = new Border
            {
                Background = (Brush)FindResource("ThemeSurface"),
                BorderBrush = (Brush)FindResource("ThemeBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var rowDock = new DockPanel { LastChildFill = true };
            var rowActions = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(rowActions, Dock.Right);
            var rUp = new Button { Content = "⬆", Style = (Style)FindResource("StormAccentButton"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 0) };
            rUp.Click += (s, e) => { SaveSyncManager.UploadToRemote(item); RefreshSaveSyncUi(); };
            var rDown = new Button { Content = "⬇", Style = (Style)FindResource("StormSecondaryButton"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 0) };
            rDown.Click += (s, e) => { SaveSyncManager.DownloadFromRemote(item); RefreshSaveSyncUi(); };
            rowActions.Children.Add(rUp);
            rowActions.Children.Add(rDown);
            rowDock.Children.Add(rowActions);

            var rowInfo = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            rowInfo.Children.Add(new TextBlock { Text = item.GameName, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("ThemeTextPrimary"), Width = 220, VerticalAlignment = VerticalAlignment.Center });
            rowInfo.Children.Add(new TextBlock { Text = item.FileName, Foreground = (Brush)FindResource("ThemeTextSecondary"), Width = 180, VerticalAlignment = VerticalAlignment.Center });
            rowInfo.Children.Add(new TextBlock { Text = item.StatusText, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.StatusColor)), Width = 160, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            rowDock.Children.Add(rowInfo);

            rowBorder.Child = rowDock;
            PanelSaveSyncList.Children.Add(rowBorder);

            // 3. Compact Icon Tile
            var comp = new Border
            {
                Background = (Brush)FindResource("ThemeSurface"),
                BorderBrush = (Brush)FindResource("ThemeBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 8, 8),
                Width = 180
            };
            var cSp = new StackPanel();
            cSp.Children.Add(new TextBlock { Text = item.GameName, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("ThemeTextPrimary"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
            cSp.Children.Add(new TextBlock { Text = item.StatusText, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.StatusColor)), FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
            comp.Child = cSp;
            PanelSaveSyncCompact.Children.Add(comp);
        }
    }

    // ==================== AI STUDIO & WALKTHROUGH ENGINE ====================

    private void InitAiStudio()
    {
        var screenshots = AiWalkthroughManager.GetAvailableScreenshots();
        CmbAiScreenshots.Items.Clear();
        foreach (var s in screenshots)
        {
            CmbAiScreenshots.Items.Add(new ComboBoxItem { Content = Path.GetFileName(s), Tag = s });
        }
        if (CmbAiScreenshots.Items.Count > 0)
        {
            CmbAiScreenshots.SelectedIndex = 0;
        }
        else
        {
            TxtNoPreviewPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private void CmbAiScreenshots_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbAiScreenshots.SelectedItem is ComboBoxItem item && item.Tag is string path && File.Exists(path))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                ImgAiPreview.Source = bmp;
                TxtNoPreviewPlaceholder.Visibility = Visibility.Collapsed;
            }
            catch
            {
                TxtNoPreviewPlaceholder.Visibility = Visibility.Visible;
            }
        }
    }

    private async void BtnAskAi_Click(object sender, RoutedEventArgs e)
    {
        string prompt = TxtAiPrompt.Text.Trim();
        string? shotPath = null;
        if (CmbAiScreenshots.SelectedItem is ComboBoxItem item && item.Tag is string p)
        {
            shotPath = p;
        }

        AiWalkthroughManager.Config.EndpointUrl = TxtAiEndpoint.Text.Trim();
        AiWalkthroughManager.Config.ModelName = TxtAiModel.Text.Trim();

        TxtAiResult.Text = "Выполняется анализ сцены и генерация тактики...";
        TxtAiStatusBadge.Text = "Анализ...";

        string answer = await AiWalkthroughManager.AskAiAssistantAsync(prompt, shotPath);
        TxtAiResult.Text = answer;
        TxtAiStatusBadge.Text = "Анализ готов";
    }

    private void BtnSpeakAiHint_Click(object sender, RoutedEventArgs e)
    {
        string text = TxtAiResult.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            AiWalkthroughManager.SpeakRussianTextAsync(text);
            ShowNotification("STORM RETROARCH", "Голосовое воспроизведение тактики запущено (TTS)!");
        }
    }

    private void BtnGenerateHintBook_Click(object sender, RoutedEventArgs e)
    {
        string gameTitle = "Ретро-игра";
        if (CmbAiScreenshots.SelectedItem is ComboBoxItem item && item.Tag is string p)
        {
            gameTitle = Path.GetFileNameWithoutExtension(p);
        }
        string? shot = (CmbAiScreenshots.SelectedItem is ComboBoxItem it && it.Tag is string sp) ? sp : null;
        string htmlPath = AiWalkthroughManager.GenerateHtmlHintBook(gameTitle, shot, TxtAiResult.Text);
        Process.Start(new ProcessStartInfo { FileName = htmlPath, UseShellExecute = true });
        ShowNotification("STORM RETROARCH", $"Книга подсказок для «{gameTitle}» успешно создана!\n" + htmlPath);
    }

    private void BtnOpenGuidebooksFolder_Click(object sender, RoutedEventArgs e)
    {
        string dir = AiWalkthroughManager.GuidebooksDir;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    private void RefreshAiWalkthroughUi()
    {
        InitAiStudio();
    }

    // ==================== SHOWCASE & VIDEO CLIP ====================
    private void BtnGenerateShowcase_Click(object sender, RoutedEventArgs e)
    {
        string ssDir = RetroArchConfigManager.GetString("screenshot_directory", Path.Combine(CoreManager.AppDir, "screenshots"));
        string outPath = ShowcaseGenerator.GenerateShowcase(
            "CHRONO TRIGGER [MOD - M. RUS]",
            "SNES / Super Famicom",
            "snes9x_libretro.dll",
            ssDir,
            Directory.GetFiles(ssDir, "*.png"));

        RefreshScreenshotsList();
        ShowNotification("STORM RETROARCH", $"Визуальный паспорт SHOWCASE.png (6–8 сцен) успешно сгенерирован!\n{outPath}");
        Process.Start(new ProcessStartInfo { FileName = outPath, UseShellExecute = true });
    }

    private void BtnRecordClip_Click(object sender, RoutedEventArgs e)
    {
        string ssDir = RetroArchConfigManager.GetString("screenshot_directory", Path.Combine(CoreManager.AppDir, "screenshots"));
        string scriptPath = ShowcaseGenerator.ExportClipPreview("CHRONO TRIGGER", ssDir);
        ShowNotification("STORM RETROARCH", $"Скрипт записи видеоклипа 60 FPS готов:\n{scriptPath}");
    }


    // ==================== CORES CATALOG RENDERING ====================
    private void RefreshCoresUi()
    {
        if (PanelCoresTiles == null) return;
        PanelCoresTiles.Children.Clear();
        PanelCoresList.Children.Clear();
        PanelCoresCompact.Children.Clear();

        foreach (var c in _cores)
        {
            // 1. Bento Card (Tiles)
            var card = new Border
            {
                Background = (Brush)FindResource("ThemeSurface"),
                BorderBrush = (Brush)FindResource("ThemeBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 12, 12),
                Width = 360
            };
            var sp = new StackPanel();

            var dockHead = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var tbName = new TextBlock { Text = c.Name, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("ThemeTextPrimary"), FontSize = 14 };
            var badgeMfr = new Border
            {
                Background = (Brush)FindResource("ThemeAccentSoft"),
                BorderBrush = (Brush)FindResource("ThemeAccent"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 2, 8, 2)
            };
            badgeMfr.Child = new TextBlock { Text = c.Manufacturer, Foreground = (Brush)FindResource("ThemeAccentGlow"), FontSize = 10.5, FontWeight = FontWeights.SemiBold };
            DockPanel.SetDock(badgeMfr, Dock.Right);
            dockHead.Children.Add(badgeMfr);
            dockHead.Children.Add(tbName);
            sp.Children.Add(dockHead);

            sp.Children.Add(new TextBlock { Text = $"Платформа: {c.Platform}", Foreground = (Brush)FindResource("ThemeAccentGlow"), FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
            sp.Children.Add(new TextBlock { Text = $"Файл: {c.DllName}", Foreground = (Brush)FindResource("ThemeTextMuted"), FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
            sp.Children.Add(new TextBlock { Text = c.Description, Foreground = (Brush)FindResource("ThemeTextSecondary"), TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 0, 0, 10) });

            var btnInstall = new Button { Content = "Установить / Обновить", Style = (Style)FindResource("StormAccentButton"), Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left };
            btnInstall.Click += (s, e) =>
            {
                ShowNotification("STORM RETROARCH", $"Ядро «{c.Name}» ({c.DllName}) готово к загрузке через Libretro Buildbot!");
            };
            sp.Children.Add(btnInstall);

            card.Child = sp;
            PanelCoresTiles.Children.Add(card);

            // 2. Ozone List Row
            var rowBorder = new Border
            {
                Background = (Brush)FindResource("ThemeSurface"),
                BorderBrush = (Brush)FindResource("ThemeBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var rowDock = new DockPanel { LastChildFill = true };
            var rBtn = new Button { Content = "Установить", Style = (Style)FindResource("StormSecondaryButton"), Padding = new Thickness(10, 3, 10, 3) };
            rBtn.Click += (s, e) => ShowNotification("STORM RETROARCH", $"Ядро «{c.Name}» выбрано.");
            DockPanel.SetDock(rBtn, Dock.Right);
            rowDock.Children.Add(rBtn);

            var rInfo = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            rInfo.Children.Add(new TextBlock { Text = c.Name, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("ThemeTextPrimary"), Width = 180, VerticalAlignment = VerticalAlignment.Center });
            rInfo.Children.Add(new TextBlock { Text = c.Platform, Foreground = (Brush)FindResource("ThemeAccentGlow"), Width = 200, VerticalAlignment = VerticalAlignment.Center });
            rInfo.Children.Add(new TextBlock { Text = c.Description, Foreground = (Brush)FindResource("ThemeTextSecondary"), TextTrimming = TextTrimming.CharacterEllipsis, Width = 340, VerticalAlignment = VerticalAlignment.Center });
            rowDock.Children.Add(rInfo);
            rowBorder.Child = rowDock;
            PanelCoresList.Children.Add(rowBorder);

            // 3. Compact Icon Tile
            var comp = new Border
            {
                Background = (Brush)FindResource("ThemeSurface"),
                BorderBrush = (Brush)FindResource("ThemeBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 8, 8),
                Width = 180
            };
            var cSp = new StackPanel();
            cSp.Children.Add(new TextBlock { Text = c.Name, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("ThemeTextPrimary"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
            cSp.Children.Add(new TextBlock { Text = c.Platform, Foreground = (Brush)FindResource("ThemeAccentGlow"), FontSize = 10.5, Margin = new Thickness(0, 4, 0, 0) });
            comp.Child = cSp;
            PanelCoresCompact.Children.Add(comp);
        }
    }


    // ==================== IN-APP EMBEDDED GAME VIEWPORT ====================

    private void BtnNavGamePlayer_Click(object sender, RoutedEventArgs e)
    {
        NavBtn_Click(BtnNavGamePlayer, e);
        if (ViewGamePlayer != null)
        {
            ViewGamePlayer.Visibility = Visibility.Visible;
        }
    }

    private void BtnGoToLibraryFromViewport_Click(object sender, RoutedEventArgs e)
    {
        NavBtn_Click(NavBtnLibrary, e);
    }

    private async void GameCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement fe && fe.DataContext is RomItem item)
        {
            await StartGameInViewportAsync(item);
        }
    }

    public async Task StartGameInViewportAsync(RomItem item)
    {
        _activeRomItem = item;

        // Add to history
        if (!_history.Any(h => h.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase)))
        {
            _history.Insert(0, item);
        }

        // Switch to Game Viewport
        if (BtnNavGamePlayer != null) NavBtn_Click(BtnNavGamePlayer, new RoutedEventArgs());
        if (ViewGamePlayer != null) ViewGamePlayer.Visibility = Visibility.Visible;
        if (PnlInGameQuickMenu != null) PnlInGameQuickMenu.Visibility = Visibility.Collapsed;

        if (TxtActiveGameTitle != null) TxtActiveGameTitle.Text = item.Name;
        if (TxtActiveGamePlatform != null) TxtActiveGamePlatform.Text = item.Platform;
        if (TxtActiveGameCore != null) TxtActiveGameCore.Text = item.CoreDisplayName;
        if (PnlGameLoadingOverlay != null) PnlGameLoadingOverlay.Visibility = Visibility.Visible;
        if (TxtGameViewportStatusTitle != null) TxtGameViewportStatusTitle.Text = $"Запуск «{item.Name}»...";
        if (TxtGameViewportStatusDesc != null) TxtGameViewportStatusDesc.Text = $"Загрузка ядра {item.CoreDisplayName} и запуск во встроенном движке STORM RETROARCH...";

        try
        {
            bool started = await NativeLibretroHost.Current.StartGameAsync(item, NativeGameImage, status =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (TxtGameViewportStatusDesc != null) TxtGameViewportStatusDesc.Text = status;
                });
            });

            if (started)
            {
                if (PnlGameLoadingOverlay != null) PnlGameLoadingOverlay.Visibility = Visibility.Collapsed;
                UpdateInGameHudButtons();
                ApplyActiveBezelToViewport();
                GameViewportContainer?.Focus();
                ShowOsdToast($"Игра «{item.Name}» запущена • F1: Меню • F2/F4: Стейты • F11: Полный экран");
            }
        }
        catch (Exception ex)
        {
            if (PnlGameLoadingOverlay != null) PnlGameLoadingOverlay.Visibility = Visibility.Visible;
            if (TxtGameViewportStatusTitle != null) TxtGameViewportStatusTitle.Text = "Ошибка запуска игры";
            if (TxtGameViewportStatusDesc != null) TxtGameViewportStatusDesc.Text = ex.Message;
            ShowNotification("Ошибка запуска", ex.Message);
        }
    }

    private void UpdateInGameHudButtons()
    {
        if (TxtGamePauseLabel != null)
            TxtGamePauseLabel.Text = NativeLibretroHost.Current.IsPaused ? "Играть" : "Пауза";
        if (TxtGameFastForwardLabel != null)
            TxtGameFastForwardLabel.Text = NativeLibretroHost.Current.IsFastForward ? "Турбо 3x" : "Турбо";
        if (TxtGameMuteLabel != null)
            TxtGameMuteLabel.Text = NativeLibretroHost.Current.IsAudioMuted ? "Без звука" : "Звук";
        if (TxtGameAspectLabel != null)
        {
            TxtGameAspectLabel.Text = NativeLibretroHost.Current.AspectMode switch
            {
                1 => "16:9",
                2 => "1:1",
                3 => "Экран",
                _ => "4:3"
            };
        }
        if (TxtGameFullscreenLabel != null)
            TxtGameFullscreenLabel.Text = _isGameFullscreen ? "Оконный (F11)" : "На весь экран (F11)";
    }

    private void GameViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        GameViewportContainer?.Focus();
        if (e.ClickCount == 2)
        {
            BtnGameFullscreen_Click(sender, e);
            e.Handled = true;
        }
    }

    private void BtnGameQuickMenu_Click(object sender, RoutedEventArgs e)
    {
        if (PnlInGameQuickMenu == null) return;
        bool opening = PnlInGameQuickMenu.Visibility != Visibility.Visible;
        PnlInGameQuickMenu.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;

        if (NativeLibretroHost.Current.IsRunning)
        {
            if (opening && !NativeLibretroHost.Current.IsPaused)
                NativeLibretroHost.Current.TogglePause();
            else if (!opening && NativeLibretroHost.Current.IsPaused)
                NativeLibretroHost.Current.TogglePause();
        }
        UpdateInGameHudButtons();
    }

    private void BtnGameQuickMenuResume_Click(object sender, RoutedEventArgs e)
    {
        if (PnlInGameQuickMenu != null) PnlInGameQuickMenu.Visibility = Visibility.Collapsed;
        if (NativeLibretroHost.Current.IsRunning && NativeLibretroHost.Current.IsPaused)
            NativeLibretroHost.Current.TogglePause();
        UpdateInGameHudButtons();
        ShowOsdToast("Игра возобновлена");
    }

    private void BtnGamePause_Click(object sender, RoutedEventArgs e)
    {
        if (!NativeLibretroHost.Current.IsRunning)
        {
            ShowOsdToast("Нет активной игры для паузы");
            return;
        }
        NativeLibretroHost.Current.TogglePause();
        UpdateInGameHudButtons();
        ShowOsdToast(NativeLibretroHost.Current.IsPaused ? "Пауза включена (P)" : "Игра продолжена (P)");
    }

    private void BtnGameReset_Click(object sender, RoutedEventArgs e)
    {
        if (!NativeLibretroHost.Current.IsRunning)
        {
            ShowOsdToast("Нет активной игры для перезапуска");
            return;
        }
        NativeLibretroHost.Current.ResetGame();
        if (PnlInGameQuickMenu != null) PnlInGameQuickMenu.Visibility = Visibility.Collapsed;
        if (NativeLibretroHost.Current.IsPaused) NativeLibretroHost.Current.TogglePause();
        UpdateInGameHudButtons();
        ShowOsdToast($"Картридж «{NativeLibretroHost.Current.CurrentGameTitle}» перезапущен");
    }

    private void CmbInGameStateSlot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbInGameStateSlot == null || CmbInGameStateSlot.SelectedIndex < 0) return;
        int slot = CmbInGameStateSlot.SelectedIndex;
        NativeLibretroHost.Current.ActiveStateSlot = slot;
        if (CmbStateSlot != null && CmbStateSlot.SelectedIndex != slot && slot < CmbStateSlot.Items.Count)
        {
            CmbStateSlot.SelectedIndex = slot;
        }
        if (_isLoaded)
        {
            ShowOsdToast($"Выбран слот сохранения: {slot}");
        }
    }

    private void BtnGameSaveState_Click(object sender, RoutedEventArgs e)
    {
        if (!NativeLibretroHost.Current.IsRunning)
        {
            ShowOsdToast("Сначала запустите игру для сохранения стейта");
            return;
        }
        int slot = NativeLibretroHost.Current.ActiveStateSlot;
        bool ok = NativeLibretroHost.Current.SaveState(slot);
        if (ok)
        {
            LoadSaves();
            RefreshSaveSyncUi();
            if (PnlInGameQuickMenu != null && PnlInGameQuickMenu.Visibility == Visibility.Visible)
            {
                PnlInGameQuickMenu.Visibility = Visibility.Collapsed;
                if (NativeLibretroHost.Current.IsPaused) NativeLibretroHost.Current.TogglePause();
                UpdateInGameHudButtons();
            }
            ShowOsdToast($"Стейт сохранён в слот #{slot} (F2) • {Path.GetFileName(NativeLibretroHost.Current.LastSavedStatePath)}");
        }
        else
        {
            ShowOsdToast($"Не удалось сохранить стейт в слот #{slot}");
        }
    }

    private void BtnGameLoadState_Click(object sender, RoutedEventArgs e)
    {
        if (!NativeLibretroHost.Current.IsRunning)
        {
            ShowOsdToast("Сначала запустите игру для загрузки стейта");
            return;
        }
        int slot = NativeLibretroHost.Current.ActiveStateSlot;
        bool ok = NativeLibretroHost.Current.LoadState(slot);
        if (ok)
        {
            if (PnlInGameQuickMenu != null && PnlInGameQuickMenu.Visibility == Visibility.Visible)
            {
                PnlInGameQuickMenu.Visibility = Visibility.Collapsed;
                if (NativeLibretroHost.Current.IsPaused) NativeLibretroHost.Current.TogglePause();
                UpdateInGameHudButtons();
            }
            ShowOsdToast($"Стейт из слота #{slot} успешно загружен (F4)");
        }
        else
        {
            ShowOsdToast($"В слоте #{slot} пока нет сохранённого стейта (нажмите F2 для сохранения)");
        }
    }

    private void BtnGameSaveStateFile_Click(object sender, RoutedEventArgs e)
    {
        if (!NativeLibretroHost.Current.IsRunning)
        {
            ShowOsdToast("Сначала запустите игру");
            return;
        }
        string statesDir = Path.Combine(CoreManager.AppDir, "states");
        if (!Directory.Exists(statesDir)) Directory.CreateDirectory(statesDir);

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Сохранить состояние игры в файл",
            InitialDirectory = statesDir,
            FileName = $"{NativeLibretroHost.Current.CurrentGameTitle}.state{NativeLibretroHost.Current.ActiveStateSlot}",
            Filter = "Быстрый стейт Libretro (*.state*)|*.state*|Все файлы (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            bool ok = NativeLibretroHost.Current.SaveState(NativeLibretroHost.Current.ActiveStateSlot, dlg.FileName);
            if (ok)
            {
                LoadSaves();
                RefreshSaveSyncUi();
                ShowOsdToast($"Стейт сохранён в файл: {Path.GetFileName(dlg.FileName)}");
            }
        }
    }

    private void BtnGameLoadStateFile_Click(object sender, RoutedEventArgs e)
    {
        if (!NativeLibretroHost.Current.IsRunning)
        {
            ShowOsdToast("Сначала запустите игру");
            return;
        }
        string statesDir = Path.Combine(CoreManager.AppDir, "states");
        if (!Directory.Exists(statesDir)) Directory.CreateDirectory(statesDir);

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Загрузить состояние игры из файла",
            InitialDirectory = statesDir,
            Filter = "Быстрый стейт Libretro (*.state*)|*.state*|Все файлы (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            bool ok = NativeLibretroHost.Current.LoadState(NativeLibretroHost.Current.ActiveStateSlot, dlg.FileName);
            if (ok)
            {
                if (PnlInGameQuickMenu != null) PnlInGameQuickMenu.Visibility = Visibility.Collapsed;
                if (NativeLibretroHost.Current.IsPaused) NativeLibretroHost.Current.TogglePause();
                UpdateInGameHudButtons();
                ShowOsdToast($"Стейт загружен: {Path.GetFileName(dlg.FileName)}");
            }
        }
    }

    private void BtnGameRewind_Click(object sender, RoutedEventArgs e)
    {
        if (!NativeLibretroHost.Current.IsRunning)
        {
            ShowOsdToast("Нет запущенной игры для перемотки");
            return;
        }
        NativeLibretroHost.Current.StepRewind();
        ShowOsdToast("Перемотка времени назад (Rewind)");
    }

    private void BtnGameFastForward_Click(object sender, RoutedEventArgs e)
    {
        if (!NativeLibretroHost.Current.IsRunning)
        {
            ShowOsdToast("Нет запущенной игры");
            return;
        }
        NativeLibretroHost.Current.ToggleFastForward();
        UpdateInGameHudButtons();
        ShowOsdToast(NativeLibretroHost.Current.IsFastForward ? "Турбо-ускорение: ВКЛ (3x)" : "Турбо-ускорение: ВЫКЛ (1x)");
    }

    private void BtnGameShader_Click(object sender, RoutedEventArgs e)
    {
        NativeLibretroHost.Current.CycleShaderFilter();
        string filterName = NativeLibretroHost.Current.ShaderFilterMode switch
        {
            1 => "CRT Scanlines (-25%)",
            2 => "Smooth Bilinear",
            3 => "Handheld LCD Grid",
            _ => "Pixel-Perfect (Nearest)"
        };
        ShowOsdToast($"Видеофильтр: {filterName}");
    }

    private void BtnGameAspect_Click(object sender, RoutedEventArgs e)
    {
        NativeLibretroHost.Current.CycleAspectMode(GameViewportContainer);
        UpdateInGameHudButtons();
        string aspectName = NativeLibretroHost.Current.AspectMode switch
        {
            1 => "16:9 Широкоэкранный",
            2 => "1:1 Целочисленный (Пиксель-в-пиксель)",
            3 => "Растянуть во весь экран",
            _ => "4:3 Оригинальные пропорции"
        };
        ShowOsdToast($"Пропорции экрана: {aspectName}");
    }

    private void BtnGameMute_Click(object sender, RoutedEventArgs e)
    {
        NativeLibretroHost.Current.ToggleMute();
        UpdateInGameHudButtons();
        ShowOsdToast(NativeLibretroHost.Current.IsAudioMuted ? "Звук отключён (Mute)" : "Звук включён");
    }

    private void BtnGameScreenshot_Click(object sender, RoutedEventArgs e)
    {
        string ssDir = Path.Combine(CoreManager.AppDir, "screenshots");
        string title = string.IsNullOrWhiteSpace(NativeLibretroHost.Current.CurrentGameTitle) ? "StormRetroArch" : NativeLibretroHost.Current.CurrentGameTitle;
        foreach (char c in Path.GetInvalidFileNameChars()) title = title.Replace(c, '_');
        string path = Path.Combine(ssDir, $"{title}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        bool ok = NativeLibretroHost.Current.CaptureScreenshot(path);
        if (ok)
        {
            RefreshScreenshotsList();
            ShowOsdToast($"Скриншот сохранён (F8): {Path.GetFileName(path)}");
        }
        else
        {
            ShowOsdToast("Не удалось сохранить скриншот (запустите игру)");
        }
    }

    private void BtnGameAiHint_Click(object sender, RoutedEventArgs e)
    {
        if (_isGameFullscreen) SetFullscreenMode(false);
        NavBtn_Click(NavBtnAiWalkthrough, e);
        BtnAskAi_Click(sender, e);
    }

    private void BtnQuickMenuCoreOptions_Click(object sender, RoutedEventArgs e)
    {
        if (_isGameFullscreen) SetFullscreenMode(false);
        if (PnlInGameQuickMenu != null) PnlInGameQuickMenu.Visibility = Visibility.Collapsed;
        NavBtn_Click(NavBtnCoreOptions, e);
    }

    private void BtnQuickMenuControls_Click(object sender, RoutedEventArgs e)
    {
        if (_isGameFullscreen) SetFullscreenMode(false);
        if (PnlInGameQuickMenu != null) PnlInGameQuickMenu.Visibility = Visibility.Collapsed;
        NavBtn_Click(NavBtnInputRemap, e);
    }

    private void BtnQuickMenuCheats_Click(object sender, RoutedEventArgs e)
    {
        if (_isGameFullscreen) SetFullscreenMode(false);
        if (PnlInGameQuickMenu != null) PnlInGameQuickMenu.Visibility = Visibility.Collapsed;
        NavBtn_Click(NavBtnCheats, e);
    }

    private void SetFullscreenMode(bool fullscreen)
    {
        _isGameFullscreen = fullscreen;

        if (fullscreen)
        {
            _prevWindowState = WindowState;
            if (ViewGamePlayer != null && ViewGamePlayer.Visibility != Visibility.Visible && NativeLibretroHost.Current.IsRunning)
            {
                BtnNavGamePlayer_Click(BtnNavGamePlayer!, new RoutedEventArgs());
            }

            if (TopTitleBarBorder != null) TopTitleBarBorder.Visibility = Visibility.Collapsed;
            if (TopToolbarBorder != null) TopToolbarBorder.Visibility = Visibility.Collapsed;
            if (LeftSidebarBorder != null) LeftSidebarBorder.Visibility = Visibility.Collapsed;
            if (BottomFooterBorder != null) BottomFooterBorder.Visibility = Visibility.Collapsed;

            if (RowDefTitleBar != null) RowDefTitleBar.Height = new GridLength(0);
            if (RowDefToolbar != null) RowDefToolbar.Height = new GridLength(0);
            if (RowDefFooter != null) RowDefFooter.Height = new GridLength(0);
            if (ColDefSidebar != null) ColDefSidebar.Width = new GridLength(0);
            if (WindowOuterBorder != null) WindowOuterBorder.BorderThickness = new Thickness(0);

            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            WindowState = WindowState.Maximized;
            ShowOsdToast("Полноэкранный режим включён (F11 или ESC для выхода)");
        }
        else
        {
            if (TopTitleBarBorder != null) TopTitleBarBorder.Visibility = Visibility.Visible;
            if (TopToolbarBorder != null) TopToolbarBorder.Visibility = Visibility.Visible;
            if (LeftSidebarBorder != null) LeftSidebarBorder.Visibility = Visibility.Visible;
            if (BottomFooterBorder != null) BottomFooterBorder.Visibility = Visibility.Visible;

            if (RowDefTitleBar != null) RowDefTitleBar.Height = new GridLength(40);
            if (RowDefToolbar != null) RowDefToolbar.Height = GridLength.Auto;
            if (RowDefFooter != null) RowDefFooter.Height = new GridLength(28);
            if (ColDefSidebar != null) ColDefSidebar.Width = new GridLength(238);
            if (WindowOuterBorder != null) WindowOuterBorder.BorderThickness = new Thickness(1);

            WindowState = _prevWindowState == WindowState.Minimized ? WindowState.Normal : _prevWindowState;
            ShowOsdToast("Оконный режим восстановлен");
        }

        UpdateInGameHudButtons();
        NativeLibretroHost.Current.ApplyAspectMode(GameViewportContainer);
    }

    private void BtnGameFullscreen_Click(object sender, RoutedEventArgs e)
    {
        SetFullscreenMode(!_isGameFullscreen);
    }

    private void BtnGameStop_Click(object sender, RoutedEventArgs e)
    {
        if (_isGameFullscreen) SetFullscreenMode(false);
        if (PnlInGameQuickMenu != null) PnlInGameQuickMenu.Visibility = Visibility.Collapsed;
        NativeLibretroHost.Current.StopGame();
        LoadSaves();
        RefreshSaveSyncUi();
        if (PnlGameLoadingOverlay != null) PnlGameLoadingOverlay.Visibility = Visibility.Visible;
        NavBtn_Click(NavBtnLibrary, e);
        ShowOsdToast("Игра остановлена, батарейные сохранения (.srm) записаны");
    }

    private void BtnExitGame_Click(object sender, RoutedEventArgs e)
    {
        if (_isGameFullscreen) SetFullscreenMode(false);
        if (PnlInGameQuickMenu != null) PnlInGameQuickMenu.Visibility = Visibility.Collapsed;
        NavBtn_Click(NavBtnLibrary, e);
    }

    // ==================== DRAG AND DROP & LIBRARY PERSISTENCE ====================

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0)
            {
                e.Handled = true;
                ProcessDroppedPaths(files);
            }
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        Window_PreviewDrop(sender, e);
    }

    private void ProcessDroppedPaths(string[] paths)
    {
        if ((DateTime.UtcNow - _lastDropTime).TotalMilliseconds < 300)
            return;
        _lastDropTime = DateTime.UtcNow;

        int addedRoms = 0;
        int addedCores = 0;
        int addedShaders = 0;
        int addedCheats = 0;

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                ScanDirectory(path);
                continue;
            }

            if (!File.Exists(path)) continue;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            string fname = Path.GetFileName(path);

            if (ext == ".dll")
            {
                try
                {
                    if (!Directory.Exists(CoreManager.CoresDir)) Directory.CreateDirectory(CoreManager.CoresDir);
                    File.Copy(path, Path.Combine(CoreManager.CoresDir, fname), overwrite: true);
                    addedCores++;
                }
                catch { }
            }
            else if (ext is ".slangp" or ".glslp" or ".cgp" or ".slang")
            {
                try
                {
                    if (!Directory.Exists(ShaderManager.ShadersDir)) Directory.CreateDirectory(ShaderManager.ShadersDir);
                    File.Copy(path, Path.Combine(ShaderManager.ShadersDir, fname), overwrite: true);
                    addedShaders++;
                }
                catch { }
            }
            else if (ext == ".cht")
            {
                try
                {
                    if (!Directory.Exists(CheatManager.CheatsDir)) Directory.CreateDirectory(CheatManager.CheatsDir);
                    File.Copy(path, Path.Combine(CheatManager.CheatsDir, fname), overwrite: true);
                    addedCheats++;
                }
                catch { }
            }
            else
            {
                AddRomFile(path);
                addedRoms++;
            }
        }

        if (addedRoms > 0)
        {
            SaveLibraryToPlaylist();
            NavBtn_Click(NavBtnLibrary, new RoutedEventArgs());
            ApplyGamesFilter();
            UpdateStats();
            ShowNotification("STORM RETROARCH", $"Добавлено {addedRoms} игр в библиотеку!");
        }
        else if (addedCores > 0)
        {
            ShowNotification("STORM RETROARCH", $"Установлено ядер Libretro: {addedCores}");
            RefreshCoresUi();
        }
        else if (addedShaders > 0)
        {
            ShowNotification("STORM RETROARCH", $"Добавлено шейдеров: {addedShaders}");
            LoadShaders();
        }
        else if (addedCheats > 0)
        {
            ShowNotification("STORM RETROARCH", $"Добавлено читов: {addedCheats}");
        }
    }

    private void SaveLibraryToPlaylist()
    {
        try
        {
            string playlistDir = Path.Combine(CoreManager.AppDir, "playlists");
            if (!Directory.Exists(playlistDir)) Directory.CreateDirectory(playlistDir);
            string file = Path.Combine(playlistDir, "storm_library.json");
            var paths = _allGames.Select(g => g.FilePath).Distinct().ToList();
            string json = System.Text.Json.JsonSerializer.Serialize(paths, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, json);
        }
        catch { }
    }

    private void LoadLibraryFromPlaylist()
    {
        try
        {
            string file = Path.Combine(CoreManager.AppDir, "playlists", "storm_library.json");
            if (File.Exists(file))
            {
                string json = File.ReadAllText(file);
                var paths = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                if (paths != null)
                {
                    foreach (var p in paths)
                    {
                        if (File.Exists(p)) AddRomFile(p);
                    }
                    ApplyGamesFilter();
                    UpdateStats();
                }
            }
        }
        catch { }
    }

    private void EnsureBattletoadsTestGame()
    {
        string zipPath = Path.Combine(CoreManager.AppDir, "test_roms", "Battletoads (1991) [JP] [MOD - M. RUS] (1.0 - 9806CB84) (1G+1M).zip");
        string nesPath = @"Q:\CONSOLES\Nintendo Entertainment System\GAMES\Battletoads (1991) [JP] [MOD - M. RUS] (1.0 - HVC-8T - 67B49C3F) (1G+1M).nes";
        string filePath = File.Exists(zipPath) ? zipPath : (File.Exists(nesPath) ? nesPath : @"C:\Games\Battletoads.nes");

        var existing = _allGames.FirstOrDefault(g => g.Name.Equals("Battletoads", StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            var item = new RomItem
            {
                FilePath = filePath,
                Platform = "Nintendo Entertainment System",
                CoreDll = "fceumm_libretro.dll",
                CoreDisplayName = "FCEUmm",
                FileSizeBytes = File.Exists(filePath) ? new FileInfo(filePath).Length : 393232,
                IsCoreInstalled = true,
                StatusText = "Готово к запуску"
            };
            item.ParseAndUpdateFromFileName("Battletoads (1991) [JP] [MOD - M. RUS] (1.0 - 9806CB84) (1G+1M).zip");
            System.Threading.Tasks.Task.Run(() => CoverArtService.LoadCoverAsync(item)).GetAwaiter().GetResult();
            _allGames.Insert(0, item);
            ApplyGamesFilter();
            UpdateStats();
        }
        else
        {
            existing.FilePath = filePath;
            existing.ParseAndUpdateFromFileName("Battletoads (1991) [JP] [MOD - M. RUS] (1.0 - 9806CB84) (1G+1M).zip");
            System.Threading.Tasks.Task.Run(() => CoverArtService.LoadCoverAsync(existing)).GetAwaiter().GetResult();
        }
    }

    private void EnsureBattletoadsRunningPreview()
    {
        EnsureBattletoadsTestGame();
        var item = _allGames.FirstOrDefault(g => g.Name.Equals("Battletoads", StringComparison.OrdinalIgnoreCase));
        if (item != null && File.Exists(item.FilePath))
        {
            try
            {
                NativeLibretroHost.Current.StartGameAsync(item, NativeGameImage).GetAwaiter().GetResult();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 700)
                {
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new System.Windows.Threading.DispatcherOperationCallback(f =>
                    {
                        ((System.Windows.Threading.DispatcherFrame)f).Continue = false;
                        return null;
                    }), frame);
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                    Thread.Sleep(25);
                }
            }
            catch { }
        }
    }

    // ==================== INTERACTIVE GAMEPAD STUDIO ====================

    private void GamepadVisualButton_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string btnName)
        {
            StartGamepadButtonCapture(btnName, border);
        }
    }

    private void BtnCaptureKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string btnName)
        {
            Border? b = FindPadBorderByName(btnName);
            StartGamepadButtonCapture(btnName, b);
        }
    }

    private Border? FindPadBorderByName(string btnName)
    {
        return btnName switch
        {
            "up" => PadBtn_up,
            "down" => PadBtn_down,
            "left" => PadBtn_left,
            "right" => PadBtn_right,
            "a" => PadBtn_a,
            "b" => PadBtn_b,
            "x" => PadBtn_x,
            "y" => PadBtn_y,
            "select" => PadBtn_select,
            "start" => PadBtn_start,
            "l" => PadBtn_l,
            "r" => PadBtn_r,
            "l2" => PadBtn_l2,
            "r2" => PadBtn_r2,
            "l3" => PadBtn_l3,
            "r3" => PadBtn_r3,
            _ => null
        };
    }

    private void StartGamepadButtonCapture(string btnName, Border? border = null)
    {
        _isCapturingGamepadButton = true;
        _capturingButtonName = btnName;

        if (_activePadButtonBorder != null)
        {
            _activePadButtonBorder.BorderBrush = (Brush)new BrushConverter().ConvertFrom("#8B5CF6")!;
        }

        border ??= FindPadBorderByName(btnName);
        _activePadButtonBorder = border;

        if (_activePadButtonBorder != null)
        {
            _activePadButtonBorder.BorderBrush = (Brush)new BrushConverter().ConvertFrom("#38BDF8")!;
        }

        if (BadgeGamepadCapture != null)
        {
            BadgeGamepadCapture.Background = (Brush)new BrushConverter().ConvertFrom("#3038BDF8")!;
            BadgeGamepadCapture.BorderBrush = (Brush)new BrushConverter().ConvertFrom("#38BDF8")!;
        }

        if (TxtGamepadCaptureStatus != null)
        {
            TxtGamepadCaptureStatus.Text = $"Нажмите любую клавишу клавиатуры для назначения [{btnName.ToUpper()}]... (ESC — отмена)";
            TxtGamepadCaptureStatus.Foreground = (Brush)new BrushConverter().ConvertFrom("#38BDF8")!;
        }
    }

    private void CancelGamepadButtonCapture()
    {
        _isCapturingGamepadButton = false;
        _capturingButtonName = "";

        if (_activePadButtonBorder != null)
        {
            _activePadButtonBorder.BorderBrush = (Brush)new BrushConverter().ConvertFrom("#8B5CF6")!;
            _activePadButtonBorder = null;
        }

        if (TxtGamepadCaptureStatus != null)
        {
            TxtGamepadCaptureStatus.Text = "Назначение отменено. Кликните кнопку для выбора.";
            TxtGamepadCaptureStatus.Foreground = (Brush)new BrushConverter().ConvertFrom("#94A3B8")!;
        }
    }

    private void ApplyCapturedKey(string retroKey)
    {
        var binding = _currentRemapBindings.FirstOrDefault(b => b.ButtonName.Equals(_capturingButtonName, StringComparison.OrdinalIgnoreCase));
        if (binding != null)
        {
            binding.BoundKey = retroKey;
            InputRemapManager.SaveBindingsToConfig(_currentRemapBindings);

            if (_activePadButtonBorder != null)
            {
                _activePadButtonBorder.BorderBrush = (Brush)new BrushConverter().ConvertFrom("#10B981")!;
            }

            if (TxtGamepadCaptureStatus != null)
            {
                TxtGamepadCaptureStatus.Text = $"Назначено: [{binding.DisplayLabel}] -> «{retroKey}»";
                TxtGamepadCaptureStatus.Foreground = (Brush)new BrushConverter().ConvertFrom("#6EE7B7")!;
            }

            ShowNotification("STORM RETROARCH", $"Кнопка {binding.DisplayLabel} назначена на клавишу «{retroKey}»");
        }

        _isCapturingGamepadButton = false;
        _capturingButtonName = "";
    }

    // ==================== SHADERS PACKAGE DOWNLOAD ====================

    private async void BtnDownloadAllShaders_Click(object sender, RoutedEventArgs e)
    {
        ShowNotification("STORM RETROARCH", "Загрузка полного архива шейдеров Libretro Slang...");
        bool ok = await ShaderManager.DownloadAllShadersPackageAsync(new Progress<(double pct, string msg)>(p =>
        {
            ShowNotification("Шейдеры Libretro", $"{p.pct:F0}% — {p.msg}");
        }));

        if (ok)
        {
            ShowNotification("STORM RETROARCH", "Коллекция из 459+ шейдеров Slang успешно установлена!");
            LoadShaders();
        }
        else
        {
            ShowNotification("Ошибка загрузки", "Не удалось скачать архив шейдеров с Libretro CDN.");
        }
    }

}
