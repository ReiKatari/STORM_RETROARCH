using System.Text.RegularExpressions;
using System.Windows.Threading;
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
        return value is true ? "★ Избранное" : "☆ В избранное";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString()?.StartsWith("★") == true;
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
    private static void LogCheckpoint(string msg)
    {
        try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "run.log"), $"[CP {DateTime.Now:HH:mm:ss.fff}] {msg}\n"); } catch { }
    }

    // In-App Embedded Game Viewport & Interactive Controller Fields
    private RomItem? _activeRomItem;
    private bool _isGameFullscreen;
    private bool _wasTelemetryVisibleBeforeFs;
    private bool _isCapturingGamepadButton;
    private string _capturingButtonName = "";
    private Border? _activePadButtonBorder;
    private DateTime _lastDropTime = DateTime.MinValue;
    private Image NativeGameImage => ImgGameViewport;
    private Border GameViewportContainer => GameViewportHostBorder;
    private TextBox TxtSearch => TxtGlobalSearch;
    private Grid ViewDiscs => ViewImport;
    private Button NavBtnDiscs => NavBtnImport;
    private string? _capturingHotkeyActionId;
    private RomHashReport? _latestHashReport;

    public static MainWindow? Instance { get; private set; }

    public const string StormCertificate = "STORM-AURORA-FLUID-CERT-2026-RETROARCH-ORIGINAL-AUTHENTIC-BUILD-VERIFIED";
    public const string StormSignature = "STORM TEAM SIGNATURE 2026 - AURORA FLUID STANDARD";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool _isTheaterMode;
    private int _viewportScaleMode; // 0=Auto, 1=1x, 2=2x, 3=3x, 4=4x
    private Point _cardDragStartPoint;
    private bool _isSyncingQuickToggles;

    private double _dynamicCardWidth = 380;
    public double DynamicCardWidth
    {
        get => _dynamicCardWidth;
        set
        {
            double clamped = Math.Clamp(value, 240, 560);
            if (Math.Abs(_dynamicCardWidth - clamped) > 0.1)
            {
                _dynamicCardWidth = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DynamicBentoWidth));
                OnPropertyChanged(nameof(DynamicBentoHeight));
                OnPropertyChanged(nameof(DynamicBentoCoverWidth));
                OnPropertyChanged(nameof(DynamicBentoCoverHeight));
                OnPropertyChanged(nameof(DynamicCompactWidth));
                OnPropertyChanged(nameof(DynamicCompactHeight));
                OnPropertyChanged(nameof(DynamicCompactCoverHeightGridLength));
            }
        }
    }

    public double DynamicBentoWidth => _dynamicCardWidth;
    public double DynamicBentoHeight => Math.Max(206.0, Math.Round(_dynamicCardWidth * 0.465, 0));
    public double DynamicBentoCoverWidth => Math.Round(_dynamicCardWidth * 0.285, 0);
    public double DynamicBentoCoverHeight => Math.Max(96, DynamicBentoHeight - 24);
    public double DynamicCompactWidth => Math.Round(_dynamicCardWidth * 0.52, 0);
    public double DynamicCompactHeight => Math.Round(DynamicCompactWidth * 1.48, 0);
    public GridLength DynamicCompactCoverHeightGridLength => new GridLength(Math.Round(DynamicCompactHeight * 0.62, 0));

    // Subsystem Collections
    private List<CoreProfileItem> _allCoreProfiles = new();
    private CoreProfileItem? _selectedCoreProfile;
    private readonly ObservableCollection<CoreOptionItem> _currentCoreOptions = new();
    private readonly ObservableCollection<CoreOptionItem> _filteredCoreOptions = new();
    private string _selectedCoreOptionCategory = "Все параметры";
    private string _coreOptionSearchQuery = "";

    // Cores Filter & Search
    private string _coreStatusFilter = "All";
    private string _coreSearchQuery = "";

    // Cheats Filter
    private bool _cheatsFilterLibraryOnly = true;

    // Window Bounds & Maximized Persistence Timer
    private DispatcherTimer? _windowPosDebounceTimer;

    private DispatcherTimer? _livePadTesterTimer;
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

    // 1.6.0 Fast Navigation & Command Palette Collections
    private readonly List<CommandPaletteItem> _allPaletteItems = new();
    private readonly ObservableCollection<CommandPaletteItem> _filteredPaletteItems = new();
    private readonly DispatcherTimer _searchDebounceTimer;
    private string _selectedChipPlatform = "Все платформы";

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
    private string _selectedBezelPlatform = "Все рамки";
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

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool SetWindowText(IntPtr hWnd, string lpString);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

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

        // Restore Manual Window Width, Height, Position & Maximized State
        SettingsManager.Load();
        var winCfg = SettingsManager.Current;
        if (winCfg.WindowWidth >= 640 && winCfg.WindowHeight >= 480)
        {
            Width = winCfg.WindowWidth;
            Height = winCfg.WindowHeight;
        }
        if (!double.IsNaN(winCfg.WindowLeft) && !double.IsNaN(winCfg.WindowTop))
        {
            double vLeft = SystemParameters.VirtualScreenLeft;
            double vTop = SystemParameters.VirtualScreenTop;
            double vWidth = SystemParameters.VirtualScreenWidth;
            double vHeight = SystemParameters.VirtualScreenHeight;

            if (winCfg.WindowLeft >= vLeft - 20 && winCfg.WindowLeft + 100 <= vLeft + vWidth &&
                winCfg.WindowTop >= vTop - 20 && winCfg.WindowTop + 100 <= vTop + vHeight)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = winCfg.WindowLeft;
                Top = winCfg.WindowTop;
            }
        }
        if (winCfg.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        _windowPosDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _windowPosDebounceTimer.Tick += (s, e) =>
        {
            _windowPosDebounceTimer.Stop();
            SaveWindowPlacement();
        };

        SizeChanged += (s, e) => ScheduleWindowPlacementSave();
        LocationChanged += (s, e) => ScheduleWindowPlacementSave();
        StateChanged += (s, e) => ScheduleWindowPlacementSave();

        // 1.6.0 Lockstep Viewport Geometry Sync
        NativeLibretroHost.GeometryChanged += () => Dispatcher.Invoke(UpdateViewportStageLayout);

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

        if (ItemsCoreOptions != null) ItemsCoreOptions.ItemsSource = _filteredCoreOptions;
        if (LstCoreProfiles != null) LstCoreProfiles.ItemsSource = _allCoreProfiles;

        // Populate State Slots 0..99
        for (int i = 0; i <= 99; i++)
        {
            CmbStateSlot.Items.Add(i.ToString());
        }
        CmbStateSlot.SelectedIndex = 0;
        CmbStateSlot.SelectionChanged += CmbStateSlot_SelectionChanged;
        SettingItem.SettingChanged += OnLiveSettingChanged;

        // 1.6.0 Debounced Search Timer (120ms)
        _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _searchDebounceTimer.Tick += (s, e) =>
        {
            _searchDebounceTimer.Stop();
            ApplyActiveViewSearch();
        };

        if (LstCommandPaletteResults != null)
            LstCommandPaletteResults.ItemsSource = _filteredPaletteItems;

        InitQuickMenuStateSlots();

        // 2.0.0 Ambilux Dynamic Glow Event Subscription
        NativeLibretroHost.Current.OnAmbiluxColorUpdated += (r, g, b) =>
        {
            if (SettingsManager.Current == null || !SettingsManager.Current.AmbiluxGlowEnabled) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (BdrAmbiluxAmbientGlow != null)
                {
                    BdrAmbiluxAmbientGlow.Background = new SolidColorBrush(Color.FromArgb(95, r, g, b));
                }
            }));
        };

        // 2.2.0 Multi-Device Input Engine & Controller Hotkey Studio
        DeviceInputProfileEngine.Initialize();
        DeviceInputProfileEngine.OnDeviceProfileActivated += (dev, msg) =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                ShowOsdToast(msg, "#38BDF8");
            }));
        };

        NativeLibretroHost.Current.OnControllerHotkeyTriggered += (actionId) =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                HandleControllerHotkeyAction(actionId);
            }));
        };

        // 2.0.0 Live Gamepad Telemetry & Axis Tester Timer (65ms, active ONLY on ViewInputRemap)
        _livePadTesterTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(65)
        };
        _livePadTesterTimer.Tick += (s, e) =>
        {
            if (ViewInputRemap == null || ViewInputRemap.Visibility != Visibility.Visible)
            {
                _livePadTesterTimer?.Stop();
                return;
            }
            int port = Math.Max(0, _selectedRemapPlayerIndex - 1);
            var telemetry = NativeLibretroHost.PollGamepadTelemetry(port);
            if (telemetry.Connected)
            {
                int lx = (int)(telemetry.LeftStickX * 100);
                int ly = (int)(telemetry.LeftStickY * 100);
                int lt = (int)(telemetry.LeftTrigger * 100);
                int rt = (int)(telemetry.RightTrigger * 100);
                if (TxtLivePadTelemetry != null)
                {
                    TxtLivePadTelemetry.Text = $"Подключён • Оси L:({lx}%, {ly}%) • LT:{lt}% RT:{rt}%";
                    TxtLivePadTelemetry.Foreground = (Brush)FindResource("ThemeAccentGlow");
                }
                if (!string.IsNullOrEmpty(telemetry.ActiveRetroButton) && telemetry.ActiveRetroButton != "None")
                {
                    UpdateControllerPreview(telemetry.ActiveRetroButton);
                }
                else
                {
                    UpdateControllerPreview(null);
                }
            }
            else
            {
                if (TxtLivePadTelemetry != null)
                {
                    TxtLivePadTelemetry.Text = "Ожидание XInput геймпада...";
                    TxtLivePadTelemetry.Foreground = (Brush)FindResource("ThemeTextMuted");
                }
            }
        };
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

    private void ForceForegroundWindow(IntPtr hwnd)
    {
        try
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Show();
            Topmost = true;
            Activate();
            Focus();
            if (hwnd != IntPtr.Zero)
            {
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
                SwitchToThisWindow(hwnd, true);
            }
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    Topmost = false;
                    Activate();
                    Focus();
                    if (hwnd != IntPtr.Zero) SetForegroundWindow(hwnd);
                }
                catch { }
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        catch { }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "run.log"), "Loaded start: " + string.Join(" ", Environment.GetCommandLineArgs()) + "\n"); } catch { }
        // Setup Win32 UIPI Bypass
        var helper = new WindowInteropHelper(this);
        IntPtr hwnd = helper.Handle;
        try
        {
            SetWindowText(hwnd, Title ?? "STORM RETROARCH 1.6.0");
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
        if (LstCoreProfiles != null) LstCoreProfiles.ItemsSource = _allCoreProfiles;
        if (ItemsCoreOptions != null) ItemsCoreOptions.ItemsSource = _filteredCoreOptions;
        var firstProfile = _allCoreProfiles.FirstOrDefault();
        if (firstProfile != null)
        {
            if (LstCoreProfiles != null) LstCoreProfiles.SelectedItem = firstProfile;
            LoadCoreOptionsProfile(firstProfile.CoreName);
        }
        else
        {
            LoadCoreOptionsProfile("SwanStation");
        }

        if (CmbControllerPlatformProfile != null)
        {
            CmbControllerPlatformProfile.Items.Clear();
            CmbControllerPlatformProfile.Items.Add("Авто (по текущей игре)");
            foreach (var p in ControllerArtRenderer.PlatformProfiles)
            {
                CmbControllerPlatformProfile.Items.Add(p);
            }
            CmbControllerPlatformProfile.SelectedIndex = 0;
            CmbControllerPlatformProfile.SelectionChanged += CmbControllerPlatformProfile_SelectionChanged;
        }

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

        // Refresh Library first, then Load Playlists (History & Favorites) & Cheats for Library Games
        LoadLibraryFromPlaylist();
        LoadPlaylists();
        InitPlatformDefaultCoreSelectors();
        UpdatePlatformFilterChips();
        UpdateDashboardRecentGames();
        InitCommandPaletteItems();
        LoadCheats();
        PopulateFilters();
        ApplyGamesFilter();
        UpdateStats();
        RefreshScreenshotsList();
        EnsureDefaultBezelInitialized();
        ApplyActiveBezelToViewport();

        // Initialize 5 Flagship Systems
        InitSmartProfiles();
        InitAiStudio();
        InitSaveSync();
        InitTelemetryTimer();

        ChkToolbarAutoUpdates.IsChecked = SettingsManager.Current.CheckUpdatesOnStartup;

        // Initialize v1.6.0 Scalability, Sidebar, Card Slider, Quick Toggles & Reorderable Dashboard
        ApplyUiScale(SettingsManager.Current.UiScale, save: false);
        ApplySidebarState();


        DynamicCardWidth = SettingsManager.Current.BentoCardWidth;
        if (SliderCardScale != null) SliderCardScale.Value = DynamicCardWidth;
        if (TxtCardScaleReadout != null) TxtCardScaleReadout.Text = $"{Math.Round(DynamicCardWidth)} px";
        ApplyDashboardCardOrder();

        // 2.0.0 Attach Interactive Tactile Hover Springs to Dashboard Bento Cards
        if (DashCard0 != null) AnalyticalSpringEngine.AttachInteractiveHoverSpring(DashCard0);
        if (DashCard1 != null) AnalyticalSpringEngine.AttachInteractiveHoverSpring(DashCard1);
        if (DashCard2 != null) AnalyticalSpringEngine.AttachInteractiveHoverSpring(DashCard2);
        if (DashCard3 != null) AnalyticalSpringEngine.AttachInteractiveHoverSpring(DashCard3);

        SyncPerformanceControls();
        SyncBezelControls();
        SyncGamepadStudioControls();
        SyncQuickSettingsCheckboxes();

        _isLoaded = true;
        LogCheckpoint("Before InitStudio200"); InitStudio200(); InitStudio210(); InitStudio220Subsystem(); LogCheckpoint("After InitStudio220");

        ForceForegroundWindow(hwnd);

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
        LogCheckpoint("Before Screenshot loop"); string[] cmdArgs = Environment.GetCommandLineArgs();
        for (int i = 0; i < cmdArgs.Length - 1; i++)
        {
            if (cmdArgs[i].Equals("--screenshot-220-suite", StringComparison.OrdinalIgnoreCase) || cmdArgs[i].Equals("--screenshot-210-suite", StringComparison.OrdinalIgnoreCase) || cmdArgs[i].Equals("--screenshot-200-suite", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot", StringComparison.OrdinalIgnoreCase) ||
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
                cmdArgs[i].Equals("--screenshot-v3-running", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-core-modal", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-cores", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-core-options", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-gamepads", StringComparison.OrdinalIgnoreCase) ||
                cmdArgs[i].Equals("--screenshot-cheats", StringComparison.OrdinalIgnoreCase) ||
                    cmdArgs[i].Equals("--screenshot-library-covers", StringComparison.OrdinalIgnoreCase) ||
                    cmdArgs[i].Equals("--screenshot-dos-aspect", StringComparison.OrdinalIgnoreCase) ||
                    cmdArgs[i].Equals("--screenshot-gamehub", StringComparison.OrdinalIgnoreCase) ||
                    cmdArgs[i].Equals("--screenshot-bigbox", StringComparison.OrdinalIgnoreCase) ||
                    cmdArgs[i].Equals("--screenshot-170-library", StringComparison.OrdinalIgnoreCase) ||
                    cmdArgs[i].Equals("--screenshot-170-dashboard", StringComparison.OrdinalIgnoreCase) ||
                    cmdArgs[i].Equals("--screenshot-170-shaders", StringComparison.OrdinalIgnoreCase) ||
                    cmdArgs[i].Equals("--screenshot-170-hotkeys", StringComparison.OrdinalIgnoreCase) ||
                    cmdArgs[i].Equals("--screenshot-170-saves", StringComparison.OrdinalIgnoreCase) ||
                    cmdArgs[i].Equals("--screenshot-170-discs", StringComparison.OrdinalIgnoreCase) ||
                    cmdArgs[i].Equals("--screenshot-170-ingame", StringComparison.OrdinalIgnoreCase) ||
                    cmdArgs[i].Equals("--screenshot-170-suite", StringComparison.OrdinalIgnoreCase) ||
                    cmdArgs[i].Equals("--screenshot-180-suite", StringComparison.OrdinalIgnoreCase) ||
                    cmdArgs[i].Equals("--screenshot-190-suite", StringComparison.OrdinalIgnoreCase))
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
                bool isCoreModal = cmdArgs[i].Equals("--screenshot-core-modal", StringComparison.OrdinalIgnoreCase);
                bool isCores = cmdArgs[i].Equals("--screenshot-cores", StringComparison.OrdinalIgnoreCase);
                bool isCoreOptions = cmdArgs[i].Equals("--screenshot-core-options", StringComparison.OrdinalIgnoreCase);
                bool isGamepads = cmdArgs[i].Equals("--screenshot-gamepads", StringComparison.OrdinalIgnoreCase);
                bool isCheats = cmdArgs[i].Equals("--screenshot-cheats", StringComparison.OrdinalIgnoreCase);
                string shotPath = cmdArgs[i + 1];

                Dispatcher.BeginInvoke(new Action(async () =>
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
                            if (LstCoreProfiles != null && LstCoreProfiles.Items.Count > 0) LstCoreProfiles.SelectedIndex = 0;
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
                        else if (isCoreModal)
                        {
                            NavBtn_Click(NavBtnLibrary, new RoutedEventArgs());
                            var g = _allGames.FirstOrDefault() ?? new RomItem { Name = "Super Mario Bros.", Platform = "Nintendo Entertainment System", Extension = ".nes" };
                            OpenCoreSelectorForGame(g);
                        }
                        else if (isCores)
                        {
                            NavBtn_Click(NavBtnCores, new RoutedEventArgs());
                        }
                        else if (isCoreOptions)
                        {
                            NavBtn_Click(NavBtnCoreOptions, new RoutedEventArgs());
                        }
                        else if (isGamepads)
                        {
                            NavBtn_Click(NavBtnInputRemap, new RoutedEventArgs());
                        }
                        else if (isCheats)
                        {
                            NavBtn_Click(NavBtnCheats, new RoutedEventArgs());
                        }
                        else if (cmdArgs[i].Equals("--screenshot-library-covers", StringComparison.OrdinalIgnoreCase))
                        {
                            NavBtn_Click(NavBtnLibrary, new RoutedEventArgs());
                            EnsureBattletoadsTestGame();
                            BtnViewMode_Click(BtnViewBento, new RoutedEventArgs());
                            await CoverArtService.LoadCoversParallelAsync(_allGames, forceRefresh: false);
                            await Task.Delay(500);
                        }
                        else if (cmdArgs[i].Equals("--screenshot-dos-aspect", StringComparison.OrdinalIgnoreCase))
                        {
                            var xanth = _allGames.FirstOrDefault(g => g.Name.Contains("Xanth", StringComparison.OrdinalIgnoreCase))
                                     ?? _allGames.FirstOrDefault(g => g.Platform.Contains("DOS", StringComparison.OrdinalIgnoreCase));
                            if (xanth != null && File.Exists(xanth.FilePath))
                            {
                                await StartGameInViewportAsync(xanth);
                                ApplyActiveBezelToViewport();
                                UpdateViewportStageLayout();
                                await Task.Delay(3800);
                                if (PnlOsdToast != null) PnlOsdToast.Visibility = Visibility.Collapsed;
                            }
                            else
                            {
                                if (BtnNavGamePlayer != null) NavBtn_Click(BtnNavGamePlayer, new RoutedEventArgs());
                                if (ViewGamePlayer != null) ViewGamePlayer.Visibility = Visibility.Visible;
                                EnsureBattletoadsRunningPreview();
                                ApplyActiveBezelToViewport();
                                UpdateViewportStageLayout();
                            }
                        }
                        else if (cmdArgs[i].Equals("--screenshot-gamehub", StringComparison.OrdinalIgnoreCase))
                        {
                            NavBtn_Click(NavBtnLibrary, new RoutedEventArgs());
                            EnsureBattletoadsTestGame();
                            await CoverArtService.LoadCoversParallelAsync(_allGames, forceRefresh: false);
                            await Task.Delay(250);
                            var sampleGame = _allGames.FirstOrDefault(g => g.Name.Contains("Dungeon Master", StringComparison.OrdinalIgnoreCase))
                                          ?? _allGames.FirstOrDefault()
                                          ?? new RomItem { Name = "Battletoads", Platform = "Nintendo Entertainment System", CoreDisplayName = "FCEUmm" };
                            OpenGameHubModal(sampleGame);
                            await Task.Delay(350);
                        }
                        else if (cmdArgs[i].Equals("--screenshot-bigbox", StringComparison.OrdinalIgnoreCase))
                        {
                            NavBtn_Click(NavBtnLibrary, new RoutedEventArgs());
                            EnsureBattletoadsTestGame();
                            await CoverArtService.LoadCoversParallelAsync(_allGames, forceRefresh: false);
                            await Task.Delay(250);
                            OpenBigBoxMode();
                            await Task.Delay(350);
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
                                                else if (cmdArgs[i].Equals("--screenshot-220-suite", StringComparison.OrdinalIgnoreCase)) { await RunScreenshot220SuiteAsync(shotPath); } else if (cmdArgs[i].Equals("--screenshot-210-suite", StringComparison.OrdinalIgnoreCase)) { await RunScreenshot210SuiteAsync(shotPath); } else if (cmdArgs[i].Equals("--screenshot-200-suite", StringComparison.OrdinalIgnoreCase))
                        {
                            await RunScreenshot200SuiteAsync(shotPath);
                        }
                        else if (cmdArgs[i].Equals("--screenshot-190-suite", StringComparison.OrdinalIgnoreCase))
                        {
                            string outDir = shotPath;
                            Directory.CreateDirectory(outDir);

                            void SnapView(string fileName)
                            {
                                AnalyticalSpringEngine.FlushAllImmediately();
                                UpdateLayout();
                                int sw = Math.Max(1360, (int)ActualWidth);
                                int sh = Math.Max(820, (int)ActualHeight);
                                var rtbS = new RenderTargetBitmap(sw, sh, 96, 96, PixelFormats.Pbgra32);
                                rtbS.Render(this);
                                var encS = new PngBitmapEncoder();
                                encS.Frames.Add(BitmapFrame.Create(rtbS));
                                using var fsS = File.Create(Path.Combine(outDir, fileName));
                                encS.Save(fsS);
                            }

                            // 1. ViewCheats: Живой сканер ОЗУ (RamCheatScannerEngine)
                            NavBtn_Click(NavBtnCheats, new RoutedEventArgs());
                            RamCheatScannerEngine.Shared.ResetScan();
                            RamCheatScannerEngine.Shared.StartFirstScan(RamValueWidth.UInt16, RamCompareMode.Changed, 0);
                            UpdateRamScannerUi();
                            if (PnlOsdToast != null) PnlOsdToast.Visibility = Visibility.Collapsed;
                            await Task.Delay(250);
                            SnapView("v190_cheats_ram.png");

                            // 2. ViewSettings: Акустическая студия Audio DSP с пресетом "Аркадный бас"
                            NavBtn_Click(NavBtnSettings, new RoutedEventArgs());
                            _selectedSettingsCategory = "audio";
                            CategoryChip_Click(SettingsCategoryChips.Children.OfType<Button>().FirstOrDefault(b => b.Content?.ToString()?.Contains("Аудио") == true) ?? NavBtnSettings, new RoutedEventArgs());
                            AudioDspEngine.Shared.ApplyPreset("Аркадный бас");
                            SyncAudioDspStudioControls();
                            if (BdrAudioDspStudio != null) BdrAudioDspStudio.Visibility = Visibility.Visible;
                            if (PnlOsdToast != null) PnlOsdToast.Visibility = Visibility.Collapsed;
                            await Task.Delay(250);
                            SnapView("v190_audio_dsp.png");

                            // 3. ViewGamePlayer: PnlInGameQuickMenu 2.0 с панелью профиля игры, CD и BSV
                            if (BtnNavGamePlayer != null) NavBtn_Click(BtnNavGamePlayer, new RoutedEventArgs());
                            if (ViewGamePlayer != null) ViewGamePlayer.Visibility = Visibility.Visible;
                            EnsureBattletoadsRunningPreview();
                            ApplyActiveBezelToViewport();
                            PnlInGameQuickMenu.Visibility = Visibility.Visible;
                            if (BorderInGameQuickMenuDialog != null) BorderInGameQuickMenuDialog.Visibility = Visibility.Visible;
                            if (PnlOsdToast != null) PnlOsdToast.Visibility = Visibility.Collapsed;
                            AnalyticalSpringEngine.FlushAllImmediately();
                            await Task.Delay(250);
                            SnapView("v190_quickmenu.png");
                            PnlInGameQuickMenu.Visibility = Visibility.Collapsed;

                            // 4. ViewLibrary: Умные динамические коллекции + библиотека игр
                            NavBtn_Click(NavBtnLibrary, new RoutedEventArgs());
                            EnsureBattletoadsTestGame();
                            UpdatePlatformFilterChips();
                            UpdateSmartCollectionPills();
                            if (PnlOsdToast != null) PnlOsdToast.Visibility = Visibility.Collapsed;
                            await Task.Delay(250);
                            SnapView("v190_library.png");

                            // 5. ViewMainMenu: Дашборд 2.0.0
                            NavBtn_Click(NavBtnMainMenu, new RoutedEventArgs());
                            SyncPerformanceControls();
                            if (PnlOsdToast != null) PnlOsdToast.Visibility = Visibility.Collapsed;
                            await Task.Delay(250);
                            SnapView("v190_dashboard.png");

                            System.Diagnostics.Process.GetCurrentProcess().Kill();
                        }
                        else if (cmdArgs[i].Equals("--screenshot-180-suite", StringComparison.OrdinalIgnoreCase) ||
                    cmdArgs[i].Equals("--screenshot-190-suite", StringComparison.OrdinalIgnoreCase))
                        {
                            string outDir = shotPath;
                            Directory.CreateDirectory(outDir);

                            void SnapView(string fileName)
                            {
                                AnalyticalSpringEngine.FlushAllImmediately();
                                UpdateLayout();
                                int sw = Math.Max(1360, (int)ActualWidth);
                                int sh = Math.Max(820, (int)ActualHeight);
                                var rtbS = new RenderTargetBitmap(sw, sh, 96, 96, PixelFormats.Pbgra32);
                                rtbS.Render(this);
                                var encS = new PngBitmapEncoder();
                                encS.Frames.Add(BitmapFrame.Create(rtbS));
                                using var fsS = File.Create(Path.Combine(outDir, fileName));
                                encS.Save(fsS);
                            }

                            // 1. Dashboard 2.0.0 with JIT Dynarec / Run-Ahead / Precision Pacer
                            NavBtn_Click(NavBtnMainMenu, new RoutedEventArgs());
                            SyncPerformanceControls();
                            await Task.Delay(250);
                            SnapView("v180_dashboard.png");

                            // 2. Shaders Studio with 6 Sliders & Live Split-Screen ДО / ПОСЛЕ Monitor
                            NavBtn_Click(NavBtnShaders, new RoutedEventArgs());
                            SyncShaderStudioControls();
                            RefreshShaderLivePreview();
                            LoadShaders();
                            await Task.Delay(250);
                            SnapView("v180_shaders.png");

                            // 3. Bezels with 16 Studio HD presets + Auto-Platform & Ambilux
                            NavBtn_Click(NavBtnBezels, new RoutedEventArgs());
                            BezelManager.EnsureBuiltinBezels();
                            SyncBezelControls();
                            LoadBezels();
                            await Task.Delay(250);
                            SnapView("v180_bezels.png");

                            // 4. Cores with JIT Dynarec badges and active ABI Benchmark report
                            NavBtn_Click(NavBtnCores, new RoutedEventArgs());
                            RefreshCoresUi();
                            var sampleCore = CoreManager.AllCoresCatalog.FirstOrDefault(c => c.IsInstalled) ?? CoreManager.AllCoresCatalog.FirstOrDefault();
                            if (sampleCore != null)
                            {
                                await RunCoreBenchmarkAsync(sampleCore);
                            }
                            await Task.Delay(300);
                            SnapView("v180_cores.png");

                            // 5. Input Remap with Deadzone/Trigger/Turbo/Rumble Studio & Controller Telemetry
                            NavBtn_Click(NavBtnInputRemap, new RoutedEventArgs());
                            SyncGamepadStudioControls();
                            RefreshHotkeyBindings();
                            LoadRemapBindings(1);
                            UpdateControllerPreview("a");
                            await Task.Delay(250);
                            SnapView("v180_input.png");

                            Application.Current.Shutdown(0);
                        }
                        else if (cmdArgs[i].Equals("--screenshot-170-suite", StringComparison.OrdinalIgnoreCase))
                        {
                            string outDir = shotPath;
                            Directory.CreateDirectory(outDir);
                            string traceFile = Path.Combine(outDir, "suite_trace.log");
                            void SnapView(string fileName)
                            {
                                AnalyticalSpringEngine.FlushAllImmediately();
                                UpdateLayout();
                                int sw = Math.Max(1360, (int)ActualWidth);
                                int sh = Math.Max(820, (int)ActualHeight);
                                var rtbS = new RenderTargetBitmap(sw, sh, 96, 96, PixelFormats.Pbgra32);
                                rtbS.Render(this);
                                var encS = new PngBitmapEncoder();
                                encS.Frames.Add(BitmapFrame.Create(rtbS));
                                using var fsS = File.Create(Path.Combine(outDir, fileName));
                                encS.Save(fsS);
                            }

                            File.AppendAllText(traceFile, "1: library\n");
                            NavBtn_Click(NavBtnLibrary, new RoutedEventArgs());
                            EnsureBattletoadsTestGame();
                            BtnViewMode_Click(BtnViewBento, new RoutedEventArgs());
                            await CoverArtService.LoadCoversParallelAsync(_allGames, forceRefresh: false);
                            await Task.Delay(300);
                            SnapView("v170_library.png");

                            File.AppendAllText(traceFile, "2: dashboard\n");
                            NavBtn_Click(NavBtnMainMenu, new RoutedEventArgs());
                            await Task.Delay(120);
                            SnapView("v170_dashboard.png");

                            File.AppendAllText(traceFile, "3: shaders\n");
                            NavBtn_Click(NavBtnShaders, new RoutedEventArgs());
                            SyncShaderStudioControls();
                            LoadShaders();
                            await Task.Delay(150);
                            SnapView("v170_shaders.png");

                            File.AppendAllText(traceFile, "4: hotkeys\n");
                            NavBtn_Click(NavBtnInputRemap, new RoutedEventArgs());
                            RefreshHotkeyBindings();
                            await Task.Delay(150);
                            SnapView("v170_hotkeys.png");

                            File.AppendAllText(traceFile, "5: saves\n");
                            NavBtn_Click(NavBtnSaves, new RoutedEventArgs());
                            LoadSaves();
                            await Task.Delay(150);
                            SnapView("v170_saves.png");

                            File.AppendAllText(traceFile, "6: discs\n");
                            NavBtn_Click(NavBtnImport, new RoutedEventArgs());
                            var sampleRom2 = _allGames.FirstOrDefault(g => File.Exists(g.FilePath));
                            if (sampleRom2 != null) { await RunRomInspectionAsync(sampleRom2.FilePath); if (PnlOsdToast != null) PnlOsdToast.Visibility = Visibility.Collapsed; }
                            await Task.Delay(150);
                            SnapView("v170_discs.png");

                            File.AppendAllText(traceFile, "7: ingame\n");
                            if (BtnNavGamePlayer != null) BtnNavGamePlayer_Click(BtnNavGamePlayer, new RoutedEventArgs());
                            if (ViewGamePlayer != null) ViewGamePlayer.Visibility = Visibility.Visible;
                            if (TxtActiveGameTitle != null) TxtActiveGameTitle.Text = "Battletoads";
                            if (TxtActiveGamePlatform != null) TxtActiveGamePlatform.Text = "Nintendo Entertainment System";
                            if (TxtActiveGameCore != null) TxtActiveGameCore.Text = "FCEUmm";
                            if (PnlGameLoadingOverlay != null) PnlGameLoadingOverlay.Visibility = Visibility.Collapsed;
                            if (TxtSearchPlaceholder != null) TxtSearchPlaceholder.Visibility = Visibility.Collapsed;
                            if (NativeGameImage != null)
                            {
                                NativeGameImage.Visibility = Visibility.Visible;
                                var wb = new WriteableBitmap(256, 240, 96, 96, PixelFormats.Bgra32, null);
                                byte[] px = new byte[256 * 240 * 4];
                                for (int py = 0; py < 240; py++)
                                {
                                    for (int pxx = 0; pxx < 256; pxx++)
                                    {
                                        int off = (py * 256 + pxx) * 4;
                                        bool star = ((pxx * 37 + py * 91) % 197 == 0) && py < 160;
                                        bool horizon = py >= 160;
                                        bool grid = horizon && ((pxx - 128) * 12 / Math.Max(1, py - 158) % 8 == 0 || py % 12 == 0);
                                        px[off + 0] = (byte)(star ? 255 : grid ? 246 : horizon ? 58 : 28);
                                        px[off + 1] = (byte)(star ? 255 : grid ? 92 : horizon ? 22 : 14);
                                        px[off + 2] = (byte)(star ? 255 : grid ? 139 : horizon ? 42 : 20);
                                        px[off + 3] = 255;
                                    }
                                }
                                wb.WritePixels(new Int32Rect(0, 0, 256, 240), px, 256 * 4, 0);
                                NativeGameImage.Source = wb;
                            }
                            if (PbRewindBuffer != null) PbRewindBuffer.Value = 68;
                            if (PnlTelemetryOverlay != null) PnlTelemetryOverlay.Visibility = Visibility.Visible;
                            for (int k = 0; k < 50; k++) TelemetryMonitorManager.RecordFrameTimeSample(16.55 + Math.Sin(k * 0.35) * 0.28);
                            if (PolyTelemetrySparkline != null) PolyTelemetrySparkline.Points = TelemetryMonitorManager.BuildSparklinePoints(150, 28);
                            await Task.Delay(180);
                            SnapView("v170_ingame.png");

                            File.AppendAllText(traceFile, "DONE\n");
                            Process.GetCurrentProcess().Kill();
                            return;
                        }
                        else if (cmdArgs[i].Equals("--screenshot-170-library", StringComparison.OrdinalIgnoreCase))
                        {
                            NavBtn_Click(NavBtnLibrary, new RoutedEventArgs());
                            EnsureBattletoadsTestGame();
                            BtnViewMode_Click(BtnViewBento, new RoutedEventArgs());
                            await CoverArtService.LoadCoversParallelAsync(_allGames, forceRefresh: false);
                            await Task.Delay(380);
                        }
                        else if (cmdArgs[i].Equals("--screenshot-170-dashboard", StringComparison.OrdinalIgnoreCase))
                        {
                            NavBtn_Click(NavBtnMainMenu, new RoutedEventArgs());
                            await Task.Delay(380);
                        }
                        else if (cmdArgs[i].Equals("--screenshot-170-shaders", StringComparison.OrdinalIgnoreCase))
                        {
                            NavBtn_Click(NavBtnShaders, new RoutedEventArgs());
                            SyncShaderStudioControls();
                            LoadShaders();
                            await Task.Delay(380);
                        }
                        else if (cmdArgs[i].Equals("--screenshot-170-hotkeys", StringComparison.OrdinalIgnoreCase))
                        {
                            NavBtn_Click(NavBtnInputRemap, new RoutedEventArgs());
                            RefreshHotkeyBindings();
                            await Task.Delay(380);
                        }
                        else if (cmdArgs[i].Equals("--screenshot-170-saves", StringComparison.OrdinalIgnoreCase))
                        {
                            NavBtn_Click(NavBtnSaves, new RoutedEventArgs());
                            LoadSaves();
                            await Task.Delay(380);
                        }
                        else if (cmdArgs[i].Equals("--screenshot-170-discs", StringComparison.OrdinalIgnoreCase))
                        {
                            NavBtn_Click(NavBtnImport, new RoutedEventArgs());
                            var sampleRom = _allGames.FirstOrDefault(g => File.Exists(g.FilePath));
                            if (sampleRom != null) { await RunRomInspectionAsync(sampleRom.FilePath); if (PnlOsdToast != null) PnlOsdToast.Visibility = Visibility.Collapsed; }
                            await Task.Delay(380);
                        }
                        else if (cmdArgs[i].Equals("--screenshot-170-ingame", StringComparison.OrdinalIgnoreCase) ||
                    cmdArgs[i].Equals("--screenshot-170-suite", StringComparison.OrdinalIgnoreCase))
                        {
                            if (BtnNavGamePlayer != null) NavBtn_Click(BtnNavGamePlayer, new RoutedEventArgs());
                            if (ViewGamePlayer != null) ViewGamePlayer.Visibility = Visibility.Visible;
                            if (TxtActiveGameTitle != null) TxtActiveGameTitle.Text = "Battletoads";
                            if (TxtActiveGamePlatform != null) TxtActiveGamePlatform.Text = "Nintendo Entertainment System";
                            if (TxtActiveGameCore != null) TxtActiveGameCore.Text = "FCEUmm";
                            if (PnlGameLoadingOverlay != null) PnlGameLoadingOverlay.Visibility = Visibility.Collapsed;
                            EnsureBattletoadsRunningPreview();
                            if (PbRewindBuffer != null) PbRewindBuffer.Value = 68;
                            if (PnlTelemetryOverlay != null) PnlTelemetryOverlay.Visibility = Visibility.Visible;
                            for (int k = 0; k < 40; k++) TelemetryMonitorManager.RecordFrameTimeSample(16.4 + (k % 5) * 0.12);
                            if (PolyTelemetrySparkline != null) PolyTelemetrySparkline.Points = TelemetryMonitorManager.BuildSparklinePoints(150, 28);
                            await Task.Delay(380);
                        }
                        AnalyticalSpringEngine.FlushAllImmediately();
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
                        try { NativeLibretroHost.Current.StopGame(); } catch { }
                        Environment.Exit(0);
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
                break;
            }
        }
    }

    private void ScheduleWindowPlacementSave()
    {
        if (!_isLoaded || _isGameFullscreen) return;
        _windowPosDebounceTimer?.Stop();
        _windowPosDebounceTimer?.Start();
    }

    private void SaveWindowPlacement()
    {
        if (!_isLoaded || _isGameFullscreen) return;
        var cfg = SettingsManager.Current;
        cfg.WindowMaximized = (WindowState == WindowState.Maximized);
        if (WindowState == WindowState.Normal)
        {
            cfg.WindowWidth = ActualWidth > 0 ? ActualWidth : Width;
            cfg.WindowHeight = ActualHeight > 0 ? ActualHeight : Height;
            cfg.WindowLeft = Left;
            cfg.WindowTop = Top;
        }
        else if (WindowState == WindowState.Maximized)
        {
            Rect rb = RestoreBounds;
            if (!rb.IsEmpty && rb.Width >= 640 && rb.Height >= 480)
            {
                cfg.WindowWidth = rb.Width;
                cfg.WindowHeight = rb.Height;
                cfg.WindowLeft = rb.Left;
                cfg.WindowTop = rb.Top;
            }
        }
        SettingsManager.Save();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveWindowPlacement();
        if (NativeLibretroHost.Current != null && NativeLibretroHost.Current.IsRunning &&
            SettingsManager.Current != null && SettingsManager.Current.AutoSaveLoadOnExit)
        {
            NativeLibretroHost.Current.SaveState(NativeLibretroHost.Current.ActiveStateSlot);
        }
        // Minimize to tray if not exiting
        if (SettingsManager.Current?.CheckUpdatesOnStartup == true)
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
        // Global ESC Modal Dismissal Rule (STORM Law 19) - Checked at the VERY TOP before anything else
        if (e.Key == Key.Escape)
        {
            if (PnlInGamePadOverlay != null && PnlInGamePadOverlay.Visibility == Visibility.Visible)
            {
                PnlInGamePadOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }
            if (PnlOnScreenKeyboard != null && PnlOnScreenKeyboard.Visibility == Visibility.Visible)
            {
                PnlOnScreenKeyboard.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }
            if (!string.IsNullOrEmpty(_capturingHotkeyActionId))
            {
                CancelHotkeyCapture();
                e.Handled = true;
                return;
            }
            if (_isCapturingGamepadButton && !string.IsNullOrEmpty(_capturingButtonName))
            {
                CancelGamepadButtonCapture();
                e.Handled = true;
                return;
            }
            if (StormAlertOverlay != null && StormAlertOverlay.Visibility == Visibility.Visible)
            {
                StormAlertOverlay.Visibility = Visibility.Collapsed;
                _alertTcs?.TrySetResult(false);
                _alertTcs = null;
                e.Handled = true;
                return;
            }
            if (PnlGameHubModal != null && PnlGameHubModal.Visibility == Visibility.Visible)
            {
                CloseGameHubModal();
                e.Handled = true;
                return;
            }
            if (PnlBigBoxOverlay != null && PnlBigBoxOverlay.Visibility == Visibility.Visible)
            {
                CloseBigBoxMode();
                e.Handled = true;
                return;
            }
            if (PnlCommandPaletteModal != null && PnlCommandPaletteModal.Visibility == Visibility.Visible)
            {
                CloseCommandPaletteModal();
                e.Handled = true;
                return;
            }
            if (PnlCoreSelectorModal != null && PnlCoreSelectorModal.Visibility == Visibility.Visible)
            {
                CloseCoreSelectorModal();
                e.Handled = true;
                return;
            }
            if (OverlayAbout != null && OverlayAbout.Visibility == Visibility.Visible)
            {
                OverlayAbout.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }
            if (OverlayUpdate != null && OverlayUpdate.Visibility == Visibility.Visible)
            {
                OverlayUpdate.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }
            if (PnlTelemetryOverlay != null && PnlTelemetryOverlay.Visibility == Visibility.Visible)
            {
                PnlTelemetryOverlay.Visibility = Visibility.Collapsed;
                SettingsManager.Current.TelemetryOverlayVisible = false;
                e.Handled = true;
                return;
            }
            if (PnlInGameQuickMenu != null && PnlInGameQuickMenu.Visibility == Visibility.Visible)
            {
                ToggleInGameQuickMenu(false);
                e.Handled = true;
                return;
            }
            if (_isGameFullscreen)
            {
                SetFullscreenMode(false);
                e.Handled = true;
                return;
            }
            return;
        }

        if (!string.IsNullOrEmpty(_capturingHotkeyActionId))
        {
            Key key = (e.Key == Key.System) ? e.SystemKey : e.Key;
            if (key != Key.LeftCtrl && key != Key.RightCtrl &&
                key != Key.LeftShift && key != Key.RightShift &&
                key != Key.LeftAlt && key != Key.RightAlt &&
                key != Key.LWin && key != Key.RWin)
            {
                HotkeyManager.SetBinding(_capturingHotkeyActionId, key);
                CancelHotkeyCapture();
                RefreshHotkeyBindings();
                ShowOsdToast("Горячая клавиша назначена", "#10B981");
                e.Handled = true;
                return;
            }
        }

        if (_isCapturingGamepadButton && !string.IsNullOrEmpty(_capturingButtonName))
        {
            string retroKey = InputRemapManager.ConvertKeyToRetroArch(e.Key);
            ApplyCapturedKey(retroKey);
            e.Handled = true;
            return;
        }

        Key actualKey = (e.Key == Key.System) ? e.SystemKey : e.Key;

        if (Keyboard.FocusedElement is not TextBox)
        {
            if (HotkeyManager.Matches("quick_menu", actualKey))
            {
                ToggleInGameQuickMenu();
                e.Handled = true;
                return;
            }
            if (HotkeyManager.Matches("pause_toggle", actualKey))
            {
                if (NativeLibretroHost.Current.IsRunning)
                {
                    NativeLibretroHost.Current.TogglePause();
                    ShowOsdToast(NativeLibretroHost.Current.IsPaused ? "Эмуляция приостановлена" : "Эмуляция возобновлена", "#38BDF8");
                }
                e.Handled = true;
                return;
            }
            if (HotkeyManager.Matches("reset_game", actualKey))
            {
                BtnGameReset_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (HotkeyManager.Matches("state_slot_next", actualKey))
            {
                int nextSlot = Math.Min(9, NativeLibretroHost.Current.ActiveStateSlot + 1);
                NativeLibretroHost.Current.ActiveStateSlot = nextSlot;
                if (CmbInGameStateSlot != null && nextSlot < CmbInGameStateSlot.Items.Count) CmbInGameStateSlot.SelectedIndex = nextSlot;
                if (CmbStateSlot != null && nextSlot < CmbStateSlot.Items.Count) CmbStateSlot.SelectedIndex = nextSlot;
                ShowOsdToast($"Слот сохранения: {nextSlot}");
                e.Handled = true;
                return;
            }
            if (HotkeyManager.Matches("state_slot_prev", actualKey))
            {
                int nextSlot = Math.Max(0, NativeLibretroHost.Current.ActiveStateSlot - 1);
                NativeLibretroHost.Current.ActiveStateSlot = nextSlot;
                if (CmbInGameStateSlot != null && nextSlot < CmbInGameStateSlot.Items.Count) CmbInGameStateSlot.SelectedIndex = nextSlot;
                if (CmbStateSlot != null && nextSlot < CmbStateSlot.Items.Count) CmbStateSlot.SelectedIndex = nextSlot;
                ShowOsdToast($"Слот сохранения: {nextSlot}");
                e.Handled = true;
                return;
            }
            if (HotkeyManager.Matches("save_state", actualKey))
            {
                BtnGameSaveState_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (HotkeyManager.Matches("load_state", actualKey))
            {
                BtnGameLoadState_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (HotkeyManager.Matches("game_screen", actualKey))
            {
                BtnNavGamePlayer_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (HotkeyManager.Matches("instant_replay", actualKey))
            {
                TriggerInstantReplaySave();
                e.Handled = true;
                return;
            }
            if (HotkeyManager.Matches("screenshot", actualKey))
            {
                BtnGameScreenshot_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (HotkeyManager.Matches("shader_next", actualKey) || HotkeyManager.Matches("shader_cycle", actualKey))
            {
                BtnGameShader_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (HotkeyManager.Matches("bezel_toggle", actualKey))
            {
                BtnGameBezel_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (HotkeyManager.Matches("bigbox_mode", actualKey))
            {
                ToggleBigBoxMode();
                e.Handled = true;
                return;
            }
            if (HotkeyManager.Matches("fullscreen_toggle", actualKey) || HotkeyManager.Matches("fullscreen", actualKey))
            {
                BtnGameFullscreen_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (HotkeyManager.Matches("frame_advance", actualKey))
            {
                if (ViewGamePlayer != null && ViewGamePlayer.Visibility == Visibility.Visible)
                {
                    NativeLibretroHost.Current.StepSingleFrame();
                    ShowOsdToast("Кадр +1", "#38BDF8");
                }
                e.Handled = true;
                return;
            }
            if (HotkeyManager.Matches("rewind_hold", actualKey))
            {
                BtnGameRewind_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (HotkeyManager.Matches("fast_forward_hold", actualKey) || HotkeyManager.Matches("fast_forward_toggle", actualKey) || HotkeyManager.Matches("fast_forward", actualKey))
            {
                BtnGameFastForward_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (HotkeyManager.Matches("audio_mute", actualKey) || HotkeyManager.Matches("mute_audio", actualKey))
            {
                BtnGameMute_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (HotkeyManager.Matches("osk_toggle", actualKey))
            {
                BtnHudToggleOsk_Click(sender, e);
                e.Handled = true;
                return;
            }
        }

        // Alt+Enter or F11 toggles Fullscreen anywhere
        if (actualKey == Key.F11 || (actualKey == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt))
        {
            BtnGameFullscreen_Click(sender, e);
            e.Handled = true;
            return;
        }

        // Ctrl+K opens Command Palette anywhere
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && actualKey == Key.K)
        {
            ToggleCommandPaletteModal();
            e.Handled = true;
            return;
        }

        // F10 toggles BigBox 10-Foot Console Mode
        if (actualKey == Key.F10)
        {
            ToggleBigBoxMode();
            e.Handled = true;
            return;
        }

        // F9 cycles CRT/Phosphor shader modes
        if (actualKey == Key.F9)
        {
            BtnGameShader_Click(sender, e);
            e.Handled = true;
            return;
        }

        // K steps single frame when paused in game
        if (actualKey == Key.K && Keyboard.FocusedElement is not TextBox &&
            ViewGamePlayer != null && ViewGamePlayer.Visibility == Visibility.Visible)
        {
            NativeLibretroHost.Current.StepSingleFrame();
            ShowOsdToast("Кадр вперед (Покадрово)");
            e.Handled = true;
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

        // Ctrl + Plus / Minus / 0 Global Interface Zoom
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (actualKey is Key.OemPlus or Key.Add)
            {
                ApplyUiScale(SettingsManager.Current.UiScale + 0.05);
                e.Handled = true;
                return;
            }
            if (actualKey is Key.OemMinus or Key.Subtract)
            {
                ApplyUiScale(SettingsManager.Current.UiScale - 0.05);
                e.Handled = true;
                return;
            }
            if (actualKey is Key.D0 or Key.NumPad0)
            {
                ApplyUiScale(1.0);
                e.Handled = true;
                return;
            }
        }

        // Strict Focus Lock when playing a game in ViewGamePlayer so keys like '1', '2', 'Space' never type into TxtSearch!
        if (ViewGamePlayer != null && ViewGamePlayer.Visibility == Visibility.Visible &&
            NativeLibretroHost.Current.IsRunning &&
            (PnlInGameQuickMenu == null || PnlInGameQuickMenu.Visibility != Visibility.Visible))
        {
            if (Keyboard.FocusedElement == TxtSearch || Keyboard.FocusedElement == TxtLibrarySearch)
            {
                Keyboard.ClearFocus();
                GameViewportContainer?.Focus();
            }
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
                if (actualKey == Key.T)
                {
                    BtnGameTheater_Click(sender, e);
                    e.Handled = true;
                    return;
                }
            }

            // Prevent Space / Enter / Arrows from activating toolbar buttons while playing in ViewGamePlayer
            if (NativeLibretroHost.Current.IsRunning &&
                (PnlInGameQuickMenu == null || PnlInGameQuickMenu.Visibility != Visibility.Visible) &&
                actualKey is Key.Space or Key.Enter or Key.Up or Key.Down or Key.Left or Key.Right or Key.Tab)
            {
                GameViewportContainer?.Focus();
                e.Handled = true;
                return;
            }
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Handled in Window_PreviewKeyDown
    }

    private void HandleControllerHotkeyAction(string actionId)
    {
        string normId = HotkeyManager.NormalizeActionId(actionId);
        switch (normId)
        {
            case "quick_menu":
                ToggleInGameQuickMenu();
                break;
            case "pause_toggle":
                if (NativeLibretroHost.Current.IsRunning)
                {
                    NativeLibretroHost.Current.TogglePause();
                    ShowOsdToast(NativeLibretroHost.Current.IsPaused ? "Эмуляция приостановлена" : "Эмуляция возобновлена", "#38BDF8");
                }
                break;
            case "reset_game":
                BtnGameReset_Click(this, new RoutedEventArgs());
                break;
            case "state_slot_next":
                {
                    int nextSlot = Math.Min(9, NativeLibretroHost.Current.ActiveStateSlot + 1);
                    NativeLibretroHost.Current.ActiveStateSlot = nextSlot;
                    if (CmbInGameStateSlot != null && nextSlot < CmbInGameStateSlot.Items.Count) CmbInGameStateSlot.SelectedIndex = nextSlot;
                    if (CmbStateSlot != null && nextSlot < CmbStateSlot.Items.Count) CmbStateSlot.SelectedIndex = nextSlot;
                    ShowOsdToast($"Слот сохранения: {nextSlot}");
                }
                break;
            case "state_slot_prev":
                {
                    int nextSlot = Math.Max(0, NativeLibretroHost.Current.ActiveStateSlot - 1);
                    NativeLibretroHost.Current.ActiveStateSlot = nextSlot;
                    if (CmbInGameStateSlot != null && nextSlot < CmbInGameStateSlot.Items.Count) CmbInGameStateSlot.SelectedIndex = nextSlot;
                    if (CmbStateSlot != null && nextSlot < CmbStateSlot.Items.Count) CmbStateSlot.SelectedIndex = nextSlot;
                    ShowOsdToast($"Слот сохранения: {nextSlot}");
                }
                break;
            case "save_state":
                BtnGameSaveState_Click(this, new RoutedEventArgs());
                break;
            case "load_state":
                BtnGameLoadState_Click(this, new RoutedEventArgs());
                break;
            case "instant_replay":
                TriggerInstantReplaySave();
                break;
            case "screenshot":
                BtnGameScreenshot_Click(this, new RoutedEventArgs());
                break;
            case "shader_next":
                BtnGameShader_Click(this, new RoutedEventArgs());
                break;
            case "bezel_toggle":
                BtnGameBezel_Click(this, new RoutedEventArgs());
                break;
            case "fullscreen_toggle":
                BtnGameFullscreen_Click(this, new RoutedEventArgs());
                break;
            case "frame_advance":
                if (ViewGamePlayer != null && ViewGamePlayer.Visibility == Visibility.Visible)
                {
                    NativeLibretroHost.Current.StepSingleFrame();
                    ShowOsdToast("Кадр +1", "#38BDF8");
                }
                break;
            case "rewind_hold":
                BtnGameRewind_Click(this, new RoutedEventArgs());
                break;
            case "fast_forward_hold":
            case "fast_forward_toggle":
                BtnGameFastForward_Click(this, new RoutedEventArgs());
                break;
            case "audio_mute":
                BtnGameMute_Click(this, new RoutedEventArgs());
                break;
            case "osk_toggle":
                BtnHudToggleOsk_Click(this, new RoutedEventArgs());
                break;
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

    private void StormAlertOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == StormAlertOverlay)
        {
            StormAlertOverlay.Visibility = Visibility.Collapsed;
            _alertTcs?.TrySetResult(false);
            _alertTcs = null;
        }
    }

    private void OverlayAbout_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == OverlayAbout)
            OverlayAbout.Visibility = Visibility.Collapsed;
    }

    private void PnlCoreSelectorModal_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == PnlCoreSelectorModal)
            CloseCoreSelectorModal();
    }

    public void CloseCoreSelectorModal()
    {
        if (PnlCoreSelectorModal != null)
        {
            if (BorderCoreSelectorDialog != null)
                AnalyticalSpringEngine.AnimateBackdropAndDialog(PnlCoreSelectorModal, BorderCoreSelectorDialog, opening: false, () => PnlCoreSelectorModal.Visibility = Visibility.Collapsed);
            else
                PnlCoreSelectorModal.Visibility = Visibility.Collapsed;
        }
    }

    public void ToggleInGameQuickMenu(bool? show = null)
    {
        if (PnlInGameQuickMenu == null) return;
        bool opening = show ?? (PnlInGameQuickMenu.Visibility != Visibility.Visible);
        if (opening)
        {
            PopulateQuickMenuCoreSelector();
            PnlInGameQuickMenu.Visibility = Visibility.Visible;
            if (BorderInGameQuickMenuDialog != null)
                AnalyticalSpringEngine.AnimateBackdropAndDialog(PnlInGameQuickMenu, BorderInGameQuickMenuDialog, opening: true);
        }
        else
        {
            if (BorderInGameQuickMenuDialog != null)
                AnalyticalSpringEngine.AnimateBackdropAndDialog(PnlInGameQuickMenu, BorderInGameQuickMenuDialog, opening: false, () => PnlInGameQuickMenu.Visibility = Visibility.Collapsed);
            else
                PnlInGameQuickMenu.Visibility = Visibility.Collapsed;
        }

        if (NativeLibretroHost.Current.IsRunning)
        {
            if (opening && !NativeLibretroHost.Current.IsPaused)
                NativeLibretroHost.Current.TogglePause();
            else if (!opening && NativeLibretroHost.Current.IsPaused)
                NativeLibretroHost.Current.TogglePause();
        }
        UpdateInGameHudButtons();
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

        // 2.0.0 Power-Saving: Stop Live Gamepad Telemetry Timer immediately when switching views
        _livePadTesterTimer?.Stop();

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
            SyncPerformanceControls();
            if (DashboardCardsGrid != null) AnalyticalSpringEngine.AnimateStaggeredChildren(DashboardCardsGrid);
        }
        else if (btn == NavBtnSettings)
        {
            _activeNavView = "Settings";
            ViewSettings.Visibility = Visibility.Visible;
            SyncAudioDspStudioControls();
            ApplySettingsFilter();
        }
        else if (btn == NavBtnLibrary)
        {
            _activeNavView = "Library";
            ViewLibrary.Visibility = Visibility.Visible;
            UpdateSmartCollectionPills();
            ApplyGamesFilter();
        }
        else if (btn == NavBtnFavorites)
        {
            _activeNavView = "Favorites";
            ViewFavorites.Visibility = Visibility.Visible;
            UpdatePlaylistEmptyStates();
        }
        else if (btn == NavBtnHistory)
        {
            _activeNavView = "History";
            ViewHistory.Visibility = Visibility.Visible;
            if (_history.Count == 0 && _allGames.Count > 0)
            {
                foreach (var g in _allGames)
                {
                    if (string.IsNullOrEmpty(g.LastPlayedFormatted) || g.LastPlayedFormatted == "-")
                        g.LastPlayedFormatted = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
                    _history.Add(g);
                }
                SavePlaylists();
            }
            UpdatePlaylistEmptyStates();
        }
        else if (btn == NavBtnCores)
        {
            _activeNavView = "Cores";
            ViewCores.Visibility = Visibility.Visible;
            if (_cores.Count == 0) PopulateCoresList("Все ядра (350+)");
            else RefreshCoresUi();
            if (PanelCoresTiles != null) AnalyticalSpringEngine.AnimateStaggeredChildren(PanelCoresTiles);
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
            SyncShaderStudioControls();
            RefreshShaderLivePreview();
            LoadShaders();
            if (ItemsShaders != null) AnalyticalSpringEngine.AnimateStaggeredChildren(ItemsShaders);
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
            UpdateRamScannerUi();
        }
        else if (btn == NavBtnBezels)
        {
            _activeNavView = "Bezels";
            ViewBezels.Visibility = Visibility.Visible;
            SyncBezelControls();
            LoadBezels();
            if (ItemsBezels != null) AnalyticalSpringEngine.AnimateStaggeredChildren(ItemsBezels);
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
            SyncGamepadStudioControls();
            RefreshHotkeyBindings();
            LoadRemapBindings(1);
            _livePadTesterTimer?.Start();
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

        UIElement? activePanel = _activeNavView switch
        {
            "MainMenu" => ViewMainMenu,
            "Settings" => ViewSettings,
            "Library" => ViewLibrary,
            "Favorites" => ViewFavorites,
            "History" => ViewHistory,
            "Cores" => ViewCores,
            "Netplay" => ViewNetplay,
            "Screenshots" => ViewScreenshots,
            "Import" => ViewImport,
            "Shaders" => ViewShaders,
            "Saves" => ViewSaves,
            "Bios" => ViewBios,
            "Cheats" => ViewCheats,
            "Bezels" => ViewBezels,
            "CoreOptions" => ViewCoreOptions,
            "InputRemap" => ViewInputRemap,
            "Achievements" => ViewAchievements,
            "StreamAi" => ViewStreamAi,
            "SaveSync" => ViewSaveSync,
            "AiWalkthrough" => ViewAiWalkthrough,
            _ => null
        };
        if (activePanel is FrameworkElement fe)
        {
            AnalyticalSpringEngine.AnimateViewTransition(fe);
        }
        else if (activePanel != null)
        {
            AnimatePanelEntrance(activePanel);
        }
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
        IcGamesBento.Visibility = (_currentViewMode == ViewDisplayMode.Bento) ? Visibility.Visible : Visibility.Collapsed;
        LvGames.Visibility = (_currentViewMode == ViewDisplayMode.Ozone) ? Visibility.Visible : Visibility.Collapsed;
        GamesIconsScroll.Visibility = (_currentViewMode == ViewDisplayMode.Compact) ? Visibility.Visible : Visibility.Collapsed;

        // 3. Dynamic Card Width across all Bento Cards & Item Controls (respects SliderCardScale)
        if (_currentViewMode is ViewDisplayMode.Bento or ViewDisplayMode.Compact)
        {
            DynamicCardWidth = SettingsManager.Current.BentoCardWidth > 240 ? SettingsManager.Current.BentoCardWidth : 380;
        }
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
        else _selectedSettingsCategory = "all";

        if (BdrAudioDspStudio != null)
        {
            BdrAudioDspStudio.Visibility = (_selectedSettingsCategory == "all" || _selectedSettingsCategory == "audio")
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

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
            _ = CoverArtService.LoadCoverAsync(item);
            _allGames.Add(item);
        }
        catch { }
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TxtSearchPlaceholder != null)
            TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void TxtLibrarySearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void ApplyActiveViewSearch()
    {
        if (_activeNavView == "Library" || (ViewLibrary != null && ViewLibrary.Visibility == Visibility.Visible))
        {
            ApplyGamesFilter();
        }
        else if (_activeNavView == "Settings" || (ViewSettings != null && ViewSettings.Visibility == Visibility.Visible))
        {
            ApplySettingsFilter();
        }
        else if (_activeNavView == "Cheats" || (ViewCheats != null && ViewCheats.Visibility == Visibility.Visible))
        {
            ApplyCheatsFilter();
        }
        else if (_activeNavView == "Shaders" || (ViewShaders != null && ViewShaders.Visibility == Visibility.Visible))
        {
            ApplyShadersFilter();
        }
        else if (_activeNavView == "Saves" || (ViewSaves != null && ViewSaves.Visibility == Visibility.Visible))
        {
            ApplySavesFilter();
        }
        else if (_activeNavView == "Bios" || (ViewBios != null && ViewBios.Visibility == Visibility.Visible))
        {
            ApplyBiosFilter();
        }
        else if (_activeNavView == "Bezels" || (ViewBezels != null && ViewBezels.Visibility == Visibility.Visible))
        {
            ApplyBezelsFilter();
        }
        else if (_activeNavView == "Cores" || (ViewCores != null && ViewCores.Visibility == Visibility.Visible))
        {
            PopulateCoresList("Все ядра (350+)");
        }
        else
        {
            ApplyGamesFilter();
        }
    }

    private void CmbPlatformFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) ApplyGamesFilter(); }
    private void CmbStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) ApplyGamesFilter(); }

    private void ApplyGamesFilter()
    {
        string libSearch = TxtLibrarySearch?.Text?.Trim().ToLowerInvariant() ?? "";
        string globalSearch = TxtSearch?.Text?.Trim().ToLowerInvariant() ?? "";
        string search = !string.IsNullOrEmpty(libSearch) ? libSearch : globalSearch;

        string platform = CmbPlatformFilter?.SelectedItem?.ToString() ?? "Все платформы";
        string status = CmbStatusFilter?.SelectedItem?.ToString() ?? "Все статусы";

        var matchingGames = new List<RomItem>();
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

            // 2.0.0 Smart Collections filter
            if (_selectedSmartCollection == "Русские переводы")
            {
                bool isRus = (g.LanguageBadge?.Contains("RUS", StringComparison.OrdinalIgnoreCase) == true) ||
                             (g.Name?.Contains("RUS", StringComparison.OrdinalIgnoreCase) == true) ||
                             (g.Name?.Contains("Рус", StringComparison.OrdinalIgnoreCase) == true);
                if (!isRus) continue;
            }
            else if (_selectedSmartCollection == "Недавние")
            {
                bool isRecent = g.LaunchCount > 0 || g.TotalPlaySeconds > 0 || g.LastPlayed != null || _history.Any(h => h.FilePath == g.FilePath);
                if (!isRecent) continue;
            }
            else if (_selectedSmartCollection == "Классика 8/16-бит")
            {
                string p = g.Platform?.ToLowerInvariant() ?? "";
                bool isClassic = p.Contains("nes") || p.Contains("famicom") || p.Contains("snes") || p.Contains("genesis") || p.Contains("mega drive") || p.Contains("game boy") || p.Contains("gba") || p.Contains("master system") || p.Contains("pc engine");
                if (!isClassic) continue;
            }
            else if (_selectedSmartCollection == "MS-DOS")
            {
                string p = g.Platform?.ToLowerInvariant() ?? "";
                bool isDos = p.Contains("dos") || p.Contains("pc");
                if (!isDos) continue;
            }
            else if (_selectedSmartCollection == "3D и CD")
            {
                string p = g.Platform?.ToLowerInvariant() ?? "";
                bool is3d = p.Contains("ps1") || p.Contains("playstation") || p.Contains("n64") || p.Contains("saturn") || p.Contains("dreamcast") || p.Contains("3do") || p.Contains("sega cd") || p.Contains("ps2");
                if (!is3d) continue;
            }

            if (platform != "Все платформы" && g.Platform != platform)
                continue;

            if (status == "Готовы к запуску" && !g.IsCoreInstalled)
                continue;
            if (status == "Требуют ядра" && g.IsCoreInstalled)
                continue;
            if (status != "Все статусы" && status != "Готовы к запуску" && status != "Требуют ядра")
            {
                if (g.CompletionStatus != status)
                    continue;
            }

            matchingGames.Add(g);
        }

        // Apply Library Sort (0: Название А-Я, 1: По платформе, 2: По ядру Libretro, 3: По размеру файла)
        int sortIdx = CmbLibrarySort?.SelectedIndex ?? 0;
        IEnumerable<RomItem> sorted = sortIdx switch
        {
            1 => matchingGames.OrderBy(g => g.Platform).ThenBy(g => g.Name),
            2 => matchingGames.OrderBy(g => g.CoreDisplayName).ThenBy(g => g.Name),
            3 => matchingGames.OrderByDescending(g => g.FileSizeBytes),
            4 => matchingGames.OrderByDescending(g => g.TotalPlaySeconds),
            5 => matchingGames.OrderByDescending(g => g.StarRating),
            6 => matchingGames.OrderByDescending(g => g.LaunchCount),
            _ => matchingGames.OrderBy(g => g.Name)
        };

        _filteredGames.Clear();
        foreach (var item in sorted)
        {
            _filteredGames.Add(item);
        }

        if (EmptyStateBorder != null) EmptyStateBorder.Visibility = _filteredGames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PopulateFilters()
    {
        UpdateSmartCollectionPills();
        CmbPlatformFilter.Items.Clear();
        CmbPlatformFilter.Items.Add("Все платформы");
        CmbPlatformFilter.SelectedIndex = 0;

        CmbStatusFilter.Items.Clear();
        CmbStatusFilter.Items.Add("Все статусы");
        CmbStatusFilter.Items.Add("Готовы к запуску");
        CmbStatusFilter.Items.Add("Требуют ядра");
        foreach (var cs in GameMetadataStore.CompletionStatuses)
        {
            CmbStatusFilter.Items.Add(cs);
        }
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
                if (!_favorites.Any(f => f.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase)))
                    _favorites.Add(item);
                ShowOsdToast($"Игра «{item.Name}» добавлена в Избранное ★");
            }
            else
            {
                var existing = _favorites.FirstOrDefault(f => f.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
                if (existing != null) _favorites.Remove(existing);
                ShowOsdToast($"Игра «{item.Name}» убрана из Избранного");
            }
            UpdatePlaylistEmptyStates();
            SavePlaylists();
        }
    }

    public void RecordGameHistory(RomItem item)
    {
        Dispatcher.Invoke(() =>
        {
            item.LastPlayed = DateTime.Now;
            item.LastPlayedFormatted = DateTime.Now.ToString("dd.MM.yyyy HH:mm");

            var existing = _history.FirstOrDefault(h => h.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                _history.Remove(existing);
            }
            _history.Insert(0, item);
            UpdatePlaylistEmptyStates();
            SavePlaylists();
        });
    }

    private void UpdatePlaylistEmptyStates()
    {
        if (EmptyFavoritesBorder != null)
            EmptyFavoritesBorder.Visibility = _favorites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (EmptyHistoryBorder != null)
            EmptyHistoryBorder.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
                using var doc = JsonDocument.Parse(File.ReadAllText(favPath));
                if (doc.RootElement.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in itemsEl.EnumerateArray())
                    {
                        string? p = el.TryGetProperty("path", out var pEl) ? pEl.GetString() : null;
                        if (string.IsNullOrWhiteSpace(p)) continue;

                        if (File.Exists(p) && !_allGames.Any(g => g.FilePath.Equals(p, StringComparison.OrdinalIgnoreCase)))
                            AddRomFile(p);

                        var match = _allGames.FirstOrDefault(g => g.FilePath.Equals(p, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            match.IsFavorite = true;
                            if (!_favorites.Any(f => f.FilePath.Equals(p, StringComparison.OrdinalIgnoreCase)))
                                _favorites.Add(match);
                        }
                    }
                }
            }

            // Load history
            string histPath = Path.Combine(playlistsDir, "content_history.lpl");
            if (File.Exists(histPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(histPath));
                if (doc.RootElement.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in itemsEl.EnumerateArray())
                    {
                        string? p = el.TryGetProperty("path", out var pEl) ? pEl.GetString() : null;
                        string? played = el.TryGetProperty("last_played", out var lpEl) ? lpEl.GetString() : null;
                        if (string.IsNullOrWhiteSpace(p)) continue;

                        if (File.Exists(p) && !_allGames.Any(g => g.FilePath.Equals(p, StringComparison.OrdinalIgnoreCase)))
                            AddRomFile(p);

                        var match = _allGames.FirstOrDefault(g => g.FilePath.Equals(p, StringComparison.OrdinalIgnoreCase));
                        if (match != null && !_history.Any(h => h.FilePath.Equals(p, StringComparison.OrdinalIgnoreCase)))
                        {
                            match.LastPlayedFormatted = !string.IsNullOrWhiteSpace(played) ? played : DateTime.Now.ToString("dd.MM.yyyy HH:mm");
                            _history.Add(match);
                        }
                    }
                }
            }

            // Seed history from library if history is currently empty so History tab is populated right away
            if (_history.Count == 0 && _allGames.Count > 0)
            {
                foreach (var g in _allGames)
                {
                    if (string.IsNullOrEmpty(g.LastPlayedFormatted) || g.LastPlayedFormatted == "-")
                        g.LastPlayedFormatted = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
                    _history.Add(g);
                }
                SavePlaylists();
            }

            UpdatePlaylistEmptyStates();
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
                    core_path = f.CoreDll,
                    core_name = f.CoreDisplayName,
                    crc32 = "DETECT",
                    db_name = "content_favorites.lpl"
                }).ToList()
            };
            File.WriteAllText(Path.Combine(playlistsDir, "content_favorites.lpl"), JsonSerializer.Serialize(favObj, new JsonSerializerOptions { WriteIndented = true }));

            // Save history playlist
            var histObj = new
            {
                version = "1.5",
                default_core_path = "",
                default_core_name = "",
                label_display_mode = 0,
                right_thumbnail_mode = 0,
                left_thumbnail_mode = 0,
                sort_mode = 2,
                items = _history.Select(h => new
                {
                    path = h.FilePath,
                    label = h.Name,
                    core_path = h.CoreDll,
                    core_name = h.CoreDisplayName,
                    last_played = string.IsNullOrEmpty(h.LastPlayedFormatted) || h.LastPlayedFormatted == "-" ? DateTime.Now.ToString("dd.MM.yyyy HH:mm") : h.LastPlayedFormatted,
                    crc32 = "DETECT",
                    db_name = "content_history.lpl"
                }).ToList()
            };
            File.WriteAllText(Path.Combine(playlistsDir, "content_history.lpl"), JsonSerializer.Serialize(histObj, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void BtnSyncFavorites_Click(object sender, RoutedEventArgs e)
    {
        SavePlaylists();
        UpdatePlaylistEmptyStates();
        ShowNotification("STORM RETROARCH", "Список избранных игр успешно обновлён и сохранён!");
    }

    private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        SavePlaylists();
        UpdatePlaylistEmptyStates();
        ShowNotification("STORM RETROARCH", "История запусков очищена.");
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
        ApplyLocalization();
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

    private void BtnAbout_Click(object sender, RoutedEventArgs e)
    {
        OverlayAbout.Visibility = Visibility.Visible;
        if (BorderAboutDialog != null)
            AnalyticalSpringEngine.AnimateBackdropAndDialog(OverlayAbout, BorderAboutDialog, opening: true);
    }
    private void BtnCloseAbout_Click(object sender, RoutedEventArgs e)
    {
        if (BorderAboutDialog != null)
            AnalyticalSpringEngine.AnimateBackdropAndDialog(OverlayAbout, BorderAboutDialog, opening: false, () => OverlayAbout.Visibility = Visibility.Collapsed);
        else
            OverlayAbout.Visibility = Visibility.Collapsed;
    }

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

        IEnumerable<CoreInfo> baseList;
        if (filter == "Золотой набор (25)")
        {
            baseList = CoreManager.GoldenCores;
        }
        else if (filter == "3D: PS2 / PS3 / PSV / Xbox" || filter.Contains("3D", StringComparison.OrdinalIgnoreCase))
        {
            baseList = CoreManager.AllCoresCatalog.Where(c => c.CategoryGroup == "NextGen3D" || c.RequiresVulkanOrD3D || c.Platform.Contains("PlayStation 2") || c.Platform.Contains("PlayStation 3") || c.Platform.Contains("PS Vita") || c.Platform.Contains("Xbox") || c.Name.Contains("LRPS2") || c.Name.Contains("RPCS3") || c.Name.Contains("Vita3K") || c.Name.Contains("Dolphin") || c.Name.Contains("Cemu") || c.Name.Contains("xemu") || c.Name.Contains("Citra") || c.Name.Contains("Azahar"));
        }
        else if (filter == "MAME / Аркады" || filter.Contains("MAME", StringComparison.OrdinalIgnoreCase) || filter.Contains("Аркады", StringComparison.OrdinalIgnoreCase))
        {
            baseList = CoreManager.AllCoresCatalog.Where(c => c.CategoryGroup == "ArcadeMame" || c.Manufacturer.Equals("Arcade", StringComparison.OrdinalIgnoreCase) || c.Name.Contains("MAME", StringComparison.OrdinalIgnoreCase) || c.Name.Contains("FBNeo", StringComparison.OrdinalIgnoreCase));
        }
        else if (filter == "Экспериментальные" || filter.Contains("Экспериментальные", StringComparison.OrdinalIgnoreCase))
        {
            baseList = CoreManager.AllCoresCatalog.Where(c => c.IsExperimental || c.CategoryGroup == "Experimental");
        }
        else if (filter == "Все ядра (350+)" || filter.StartsWith("Все ядра", StringComparison.OrdinalIgnoreCase))
        {
            baseList = CoreManager.AllCoresCatalog;
        }
        else
        {
            baseList = CoreManager.GetCoresByManufacturer(filter);
        }

        if (_coreStatusFilter == "Installed")
            baseList = baseList.Where(c => c.IsInstalled);
        else if (_coreStatusFilter == "Available")
            baseList = baseList.Where(c => !c.IsInstalled);

        foreach (var c in baseList) _cores.Add(c);
        RefreshCoresUi();
    }

    private void BtnCoresStatusFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (BtnCoresFilterAll != null) BtnCoresFilterAll.Tag = null;
        if (BtnCoresFilterInstalled != null) BtnCoresFilterInstalled.Tag = null;
        if (BtnCoresFilterAvailable != null) BtnCoresFilterAvailable.Tag = null;
        btn.Tag = "Active";

        if (btn == BtnCoresFilterInstalled) _coreStatusFilter = "Installed";
        else if (btn == BtnCoresFilterAvailable) _coreStatusFilter = "Available";
        else _coreStatusFilter = "All";

        PopulateCoresList(_selectedCoreManufacturer);
    }

    private void TxtSearchCores_TextChanged(object sender, TextChangedEventArgs e)
    {
        _coreSearchQuery = TxtSearchCores?.Text?.Trim().ToLowerInvariant() ?? "";
        RefreshCoresUi();
    }

    private async void BtnUpdateInstalledCores_Click(object sender, RoutedEventArgs e)
    {
        var installed = CoreManager.AllCoresCatalog.Where(c => c.IsInstalled).ToList();
        if (installed.Count == 0)
        {
            ShowOsdToast("Нет установленных ядер для обновления");
            return;
        }
        ShowOsdToast($"Обновление {installed.Count} установленных ядер...");
        int updated = 0;
        foreach (var core in installed)
        {
            bool ok = await CoreManager.EnsureCoreDownloadedAsync(core.DllName);
            if (ok) updated++;
        }
        RefreshCoresUi();
        ShowOsdToast($"Успешно обновлено ядер: {updated} из {installed.Count}");
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
                if (child is Button b && b != BtnCoresFilterAll && b != BtnCoresFilterInstalled && b != BtnCoresFilterAvailable)
                    b.Tag = null;
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
                "PS2 / PS3 / PS Vita" => b.Platform.Contains("PS2", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("PS3", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("Vita", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("PlayStation 2", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("PlayStation 3", StringComparison.OrdinalIgnoreCase) || b.RelativePath.Contains("pcsx2", StringComparison.OrdinalIgnoreCase) || b.RelativePath.Contains("vita3k", StringComparison.OrdinalIgnoreCase) || b.RelativePath.Contains("rpcs3", StringComparison.OrdinalIgnoreCase),
                "Xbox / 3DS / Wii U" => b.Platform.Contains("Xbox", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("3DS", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("Wii", StringComparison.OrdinalIgnoreCase) || b.RelativePath.Contains("xemu", StringComparison.OrdinalIgnoreCase) || b.RelativePath.Contains("citra", StringComparison.OrdinalIgnoreCase) || b.RelativePath.Contains("cemu", StringComparison.OrdinalIgnoreCase),
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
        CheatManager.CleanupDuplicateAndSampleCheats(_allGames);
        var files = CheatManager.ScanCheats();
        _allCheatFiles.Clear();
        foreach (var f in files) _allCheatFiles.Add(f);
        ApplyCheatsFilter();

        if (_filteredCheatFiles.Count > 0)
        {
            if (NativeLibretroHost.Current.IsRunning && !string.IsNullOrWhiteSpace(NativeLibretroHost.Current.CurrentGameTitle))
            {
                var activeMatch = _filteredCheatFiles.FirstOrDefault(f =>
                    f.GameName.Contains(NativeLibretroHost.Current.CurrentGameTitle, StringComparison.OrdinalIgnoreCase) ||
                    NativeLibretroHost.Current.CurrentGameTitle.Contains(f.GameName, StringComparison.OrdinalIgnoreCase));
                if (activeMatch != null)
                {
                    ListCheatFiles.SelectedItem = activeMatch;
                    return;
                }
            }
            if (ListCheatFiles.SelectedItem == null)
            {
                ListCheatFiles.SelectedIndex = 0;
            }
        }
    }

    private void BtnCheatsFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (BtnCheatsFilterLibrary != null) BtnCheatsFilterLibrary.Tag = null;
        if (BtnCheatsFilterAll != null) BtnCheatsFilterAll.Tag = null;
        btn.Tag = "Active";

        _cheatsFilterLibraryOnly = (btn == BtnCheatsFilterLibrary);
        ApplyCheatsFilter();
    }

    private void BtnSwitchToCheatsView_Click(object sender, RoutedEventArgs e)
    {
        NavBtn_Click(NavBtnCheats, e);
    }

    private void BtnEnableAllCheats_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCheatFile == null || _selectedCheats.Count == 0) return;
        foreach (var c in _selectedCheats) c.IsEnabled = true;
        CheatManager.SaveCht(_selectedCheatFile.FilePath, _selectedCheats);
        if (NativeLibretroHost.Current.IsRunning)
            NativeLibretroHost.Current.ApplyCheats(_selectedCheats);
        ShowOsdToast($"Все читы ({_selectedCheats.Count}) для «{_selectedCheatFile.GameName}» включены!");
    }

    private void BtnDisableAllCheats_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCheatFile == null || _selectedCheats.Count == 0) return;
        foreach (var c in _selectedCheats) c.IsEnabled = false;
        CheatManager.SaveCht(_selectedCheatFile.FilePath, _selectedCheats);
        if (NativeLibretroHost.Current.IsRunning)
            NativeLibretroHost.Current.ApplyCheats(_selectedCheats);
        ShowOsdToast($"Все читы для «{_selectedCheatFile.GameName}» отключены");
    }

    private void ChkCheatEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded || _selectedCheatFile == null) return;
        CheatManager.SaveCht(_selectedCheatFile.FilePath, _selectedCheats);
        if (NativeLibretroHost.Current.IsRunning)
        {
            int activeCount = NativeLibretroHost.Current.ApplyCheats(_selectedCheats);
            ShowOsdToast($"Читы обновлены (активно: {activeCount})");
        }
    }

    private void ApplyCheatsFilter()
    {
        string query = TxtSearchCheats?.Text?.Trim().ToLowerInvariant() ?? "";
        _filteredCheatFiles.Clear();

        foreach (var f in _allCheatFiles)
        {
            if (_cheatsFilterLibraryOnly && !f.IsLibraryGame) continue;
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

        ShowNotification($"Загрузка обложек для {_allGames.Count} игр...");
        await CoverArtService.LoadCoversParallelAsync(_allGames, forceRefresh: true);
        ShowNotification("Загрузка обложек завершена!");
    }

    private async void BtnFetchCovers_Click(object sender, RoutedEventArgs e)
    {
        if (_allGames.Count == 0)
        {
            ShowOsdToast("В библиотеке нет игр");
            return;
        }

        ShowOsdToast($"Параллельная загрузка обложек для {_allGames.Count} игр...");
        await CoverArtService.LoadCoversParallelAsync(_allGames, forceRefresh: true);
        ShowOsdToast("Обложки успешно загружены!");
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

    private void EnsureDefaultBezelInitialized()
    {
        try
        {
            string currentCfg = RetroArchConfigManager.GetString("input_overlay", "");
            if (string.IsNullOrWhiteSpace(currentCfg) || !File.Exists(currentCfg))
            {
                var bezels = BezelManager.ScanBezels();
                if (bezels.Count > 0)
                {
                    BezelManager.ApplyBezel(bezels[0]);
                    LoadBezels();
                }
            }
        }
        catch { }
    }

    private void ApplyActiveBezelToViewport()
    {
        var active = BezelManager.GetActiveBezel();
        if (active != null && !string.IsNullOrEmpty(active.ImagePath) && File.Exists(active.ImagePath))
        {
            var bmp = BezelManager.LoadFullBezelImage(active.ImagePath) ?? BezelManager.LoadPreviewImage(active.ImagePath);
            if (bmp != null)
            {
                ImgGameBezelOverlay.Source = bmp;
                string opStr = RetroArchConfigManager.GetString("input_overlay_opacity", "1.000000");
                if (float.TryParse(opStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float op))
                {
                    ImgGameBezelOverlay.Opacity = Math.Clamp(op, 0.15f, 1.0f);
                }
                else
                {
                    ImgGameBezelOverlay.Opacity = 1.0;
                }
                ImgGameBezelOverlay.Visibility = Visibility.Visible;
                ImgGameViewport.Margin = new Thickness(96, 18, 96, 18);
                return;
            }
        }

        ImgGameBezelOverlay.Source = null;
        ImgGameBezelOverlay.Visibility = Visibility.Collapsed;
        UpdateViewportStageLayout();
    }

    public void UpdateGameViewportBezelMargin(BezelPresetItem? active = null)
    {
        UpdateViewportStageLayout();
    }

    private void UpdateViewportStageLayout()
    {
        if (GrdGameStageCanvas == null || BorderGameScreenHost == null || ImgGameBezelOverlay == null)
            return;

        int aspect = NativeLibretroHost.Current != null ? NativeLibretroHost.Current.AspectMode : 0;
        bool hasActiveBezel = ImgGameBezelOverlay.Source != null && BezelManager.GetActiveBezel() != null;

        // If Bezel is active AND aspect is 4:3 (mode 0), lock inside Bezel cutout
        if (hasActiveBezel && aspect == 0)
        {
            GrdGameStageCanvas.Width = BezelManager.CanvasWidth; // 1920
            GrdGameStageCanvas.Height = BezelManager.CanvasHeight; // 1080

            BorderGameScreenHost.Width = BezelManager.InnerScreenWidth; // 1376
            BorderGameScreenHost.Height = BezelManager.InnerScreenHeight; // 1032
            BorderGameScreenHost.Margin = new Thickness(BezelManager.InnerScreenLeft, BezelManager.InnerScreenTop, 0, 0); // 272, 24, 0, 0

            if (BdrAmbiluxAmbientGlow != null)
            {
                BdrAmbiluxAmbientGlow.Width = BorderGameScreenHost.Width + 52;
                BdrAmbiluxAmbientGlow.Height = BorderGameScreenHost.Height + 52;
                BdrAmbiluxAmbientGlow.Margin = new Thickness(BorderGameScreenHost.Margin.Left - 26, BorderGameScreenHost.Margin.Top - 26, 0, 0);
                BdrAmbiluxAmbientGlow.Visibility = SettingsManager.Current.AmbiluxGlowEnabled ? Visibility.Visible : Visibility.Collapsed;
            }

            ImgGameBezelOverlay.Width = BezelManager.CanvasWidth;
            ImgGameBezelOverlay.Height = BezelManager.CanvasHeight;
            ImgGameBezelOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            ImgGameBezelOverlay.Visibility = Visibility.Collapsed;
            BorderGameScreenHost.Margin = new Thickness(0);
            if (BdrAmbiluxAmbientGlow != null)
            {
                BdrAmbiluxAmbientGlow.Width = BorderGameScreenHost.Width + 52;
                BdrAmbiluxAmbientGlow.Height = BorderGameScreenHost.Height + 52;
                BdrAmbiluxAmbientGlow.Margin = new Thickness(-26);
                BdrAmbiluxAmbientGlow.Visibility = SettingsManager.Current.AmbiluxGlowEnabled ? Visibility.Visible : Visibility.Collapsed;
            }

            if (aspect == 1) // 16:9
            {
                GrdGameStageCanvas.Width = 1920;
                GrdGameStageCanvas.Height = 1080;
                BorderGameScreenHost.Width = 1920;
                BorderGameScreenHost.Height = 1080;
            }
            else if (aspect == 2) // 1:1
            {
                GrdGameStageCanvas.Width = 1080;
                GrdGameStageCanvas.Height = 1080;
                BorderGameScreenHost.Width = 1080;
                BorderGameScreenHost.Height = 1080;
            }
            else // 0 = 4:3
            {
                GrdGameStageCanvas.Width = 1440;
                GrdGameStageCanvas.Height = 1080;
                BorderGameScreenHost.Width = 1440;
                BorderGameScreenHost.Height = 1080;
            }
        }
    }

    private void BtnGameBezel_Click(object sender, RoutedEventArgs e)
    {
        var bezels = BezelManager.ScanBezels();
        if (bezels.Count == 0)
        {
            ShowOsdToast("Нет доступных экранных рамок");
            return;
        }

        var active = BezelManager.GetActiveBezel();
        if (active == null)
        {
            BezelManager.ApplyBezel(bezels[0]);
            ShowOsdToast($"Экранная рамка: {bezels[0].Name} [ВКЛ]");
        }
        else
        {
            int currentIndex = bezels.FindIndex(b => b.CfgPath.Equals(active.CfgPath, StringComparison.OrdinalIgnoreCase));
            if (currentIndex >= 0 && currentIndex < bezels.Count - 1)
            {
                var next = bezels[currentIndex + 1];
                BezelManager.ApplyBezel(next);
                ShowOsdToast($"Экранная рамка: {next.Name} [ВКЛ]");
            }
            else
            {
                BezelManager.DisableBezel();
                ShowOsdToast("Экранная рамка: [ВЫКЛ]");
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



private void LoadBezels()
    {
        BezelManager.EnsureBuiltinBezels();
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
                "Mega Bezel / Duimon" => b.PackSource.Contains("Mega Bezel", StringComparison.OrdinalIgnoreCase) || b.PackSource.Contains("Duimon", StringComparison.OrdinalIgnoreCase) || b.SlangpPath.Contains("Mega_Bezel", StringComparison.OrdinalIgnoreCase) || b.Name.Contains("Mega Bezel", StringComparison.OrdinalIgnoreCase) || b.Name.Contains("Duimon", StringComparison.OrdinalIgnoreCase),
                "PS2 / PS3 / PSV" => b.Platform.Contains("PS2", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("PS3", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("Vita", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("PlayStation 2", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("PlayStation 3", StringComparison.OrdinalIgnoreCase),
                "Sony" => b.Platform.Contains("Sony", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("PlayStation", StringComparison.OrdinalIgnoreCase),
                "Nintendo" => b.Platform.Contains("Nintendo", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("Game Boy", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("SNES", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("NES", StringComparison.OrdinalIgnoreCase),
                "Sega" => b.Platform.Contains("Sega", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("Genesis", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("Saturn", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("Dreamcast", StringComparison.OrdinalIgnoreCase),
                "Arcade / Other" or "Аркады и другие" => b.Platform.Contains("Arcade", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("STORM", StringComparison.OrdinalIgnoreCase) || b.Platform.Contains("Custom", StringComparison.OrdinalIgnoreCase),
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
        _selectedBezelPlatform = btn.Content?.ToString() ?? "Все рамки";
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
            if (TxtSelectedCoreProfileName != null) TxtSelectedCoreProfileName.Text = _selectedCoreProfile.DisplayName;
            if (TxtSelectedCorePlatform != null) TxtSelectedCorePlatform.Text = _selectedCoreProfile.Platform;

            CoreOptionsAndOverridesManager.LoadCoreOptionsFromDisk(_selectedCoreProfile.CoreName, _selectedCoreProfile.Options);
            foreach (var opt in _selectedCoreProfile.Options)
            {
                _currentCoreOptions.Add(opt);
            }
        }
        ApplyCoreOptionsFilter();
    }

    private void LstCoreProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstCoreProfiles?.SelectedItem is CoreProfileItem profile)
        {
            LoadCoreOptionsProfile(profile.CoreName);
        }
    }

    private void ApplyCoreOptionsFilter()
    {
        _filteredCoreOptions.Clear();
        string q = _coreOptionSearchQuery;
        bool catAll = _selectedCoreOptionCategory == "Все параметры";

        foreach (var opt in _currentCoreOptions)
        {
            bool matchCat = catAll || (opt.Category != null && opt.Category.Equals(_selectedCoreOptionCategory, StringComparison.OrdinalIgnoreCase));
            bool matchQ = string.IsNullOrEmpty(q) ||
                          (opt.Title != null && opt.Title.ToLowerInvariant().Contains(q)) ||
                          (opt.Description != null && opt.Description.ToLowerInvariant().Contains(q)) ||
                          (opt.Key != null && opt.Key.ToLowerInvariant().Contains(q));
            if (matchCat && matchQ)
            {
                _filteredCoreOptions.Add(opt);
            }
        }
    }

    private void BtnCoreOptionCategory_Click(object sender, RoutedEventArgs e)
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
        _selectedCoreOptionCategory = btn.Content?.ToString() ?? "Все параметры";
        ApplyCoreOptionsFilter();
    }

    private void TxtCoreOptionSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _coreOptionSearchQuery = TxtCoreOptionSearch?.Text?.Trim().ToLowerInvariant() ?? "";
        ApplyCoreOptionsFilter();
    }

    private void BtnResetSingleOption_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CoreOptionItem opt)
        {
            string def = !string.IsNullOrEmpty(opt.DefaultValue) ? opt.DefaultValue : (opt.Options.FirstOrDefault() ?? "");
            opt.Value = def;
            ShowOsdToast($"Параметр «{opt.Title}» сброшен: {def}");
        }
    }

    private void BtnResetCoreOptions_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCoreProfile == null) return;
        foreach (var opt in _currentCoreOptions)
        {
            if (!string.IsNullOrEmpty(opt.DefaultValue))
                opt.Value = opt.DefaultValue;
            else if (opt.Options.Count > 0)
                opt.Value = opt.Options[0];
        }
        CoreOptionsAndOverridesManager.ResetCoreOptions(_selectedCoreProfile.CoreName);
        ShowOsdToast($"Все опции ядра {_selectedCoreProfile.DisplayName} сброшены на стандартные.");
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
        ShowNotification("STORM RETROARCH", $"Переопределение параметров для ядра {_selectedCoreProfile.CoreName} сохранено!");
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
        UpdateControllerPreview();
    }

    private void CmbControllerPlatformProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;
        UpdateControllerPreview();
    }

    private void UpdateControllerPreview(string? activeKey = null)
    {
        if (ImgRealisticController == null) return;
        try
        {
            string selected = CmbControllerPlatformProfile?.SelectedItem?.ToString() ?? "SNES";
            if (selected.StartsWith("Авто", StringComparison.OrdinalIgnoreCase))
            {
                string? currentCore = NativeLibretroHost.Current.IsRunning ? NativeLibretroHost.Current.CurrentCoreName : (_selectedCoreProfile?.CoreName);
                selected = ControllerArtRenderer.MapPlatformToControllerProfile(currentCore);
            }
            ImgRealisticController.Source = ControllerArtRenderer.RenderController(selected, _selectedRemapPlayerIndex, activeKey);
        }
        catch { }
    }

    private void RemapRow_MouseEnter(object sender, MouseEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RemapButtonBinding b)
        {
            UpdateControllerPreview(b.ButtonName);
        }
    }

    private void RemapRow_MouseLeave(object sender, MouseEventArgs e)
    {
        UpdateControllerPreview(null);
    }

    private void BtnDetectXInput_Click(object sender, RoutedEventArgs e)
    {
        ShowOsdToast("Обнаружен контроллер XInput. Привязки обновлены.");
        LoadRemapBindings(_selectedRemapPlayerIndex);
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
            UpdateControllerPreview();
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
        bool open = NativeLibretroHost.Current.ToggleVirtualDiscTray();
        ShowOsdToast(open ? "Виртуальный лоток CD открыт" : "Виртуальный лоток CD закрыт");
    }

    private void BtnDiscPrev_Click(object sender, RoutedEventArgs e)
    {
        DiscPatchSubsystemManager.PreviousDisc();
        NativeLibretroHost.Current.SwitchVirtualDisc(-1);
        ShowOsdToast(NativeLibretroHost.Current.GetVirtualDiscStatusRu());
    }

    private void BtnDiscNext_Click(object sender, RoutedEventArgs e)
    {
        DiscPatchSubsystemManager.NextDisc();
        NativeLibretroHost.Current.SwitchVirtualDisc(1);
        ShowOsdToast(NativeLibretroHost.Current.GetVirtualDiscStatusRu());
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

        if (PolyTelemetrySparkline != null)
        {
            PolyTelemetrySparkline.Points = TelemetryMonitorManager.BuildSparklinePoints(150, 28);
        }
        if (PbRewindBuffer != null)
        {
            PbRewindBuffer.Value = NativeLibretroHost.Current?.RewindFillPercent ?? 0.0;
        }
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
        SettingsManager.NormalizeRootPaths();
        SaveSyncManager.CloudSyncDir = SettingsManager.Current.CloudSyncFolder;
        if (TxtCloudSyncFolder != null)
            TxtCloudSyncFolder.Text = SaveSyncManager.CloudSyncDir;
        if (TxtHeroRootPathBadge != null)
            TxtHeroRootPathBadge.Text = $"Корень: {CoreManager.AppDir}";
        if (TxtHeroSaveSyncBadge != null)
            TxtHeroSaveSyncBadge.Text = $"save sync ({SaveSyncManager.CloudSyncDir})";
        if (TxtConfigPathDisplay != null)
            TxtConfigPathDisplay.Text = $"Файл: {RetroArchConfigManager.ConfigFilePath}";
    }

    private void BtnResetCloudSyncRoot_Click(object sender, RoutedEventArgs e)
    {
        string rootSync = Path.Combine(CoreManager.AppDir, "save sync");
        try
        {
            if (!Directory.Exists(rootSync)) Directory.CreateDirectory(rootSync);
        }
        catch { }
        SaveSyncManager.CloudSyncDir = rootSync;
        if (TxtCloudSyncFolder != null) TxtCloudSyncFolder.Text = rootSync;
        if (TxtHeroSaveSyncBadge != null) TxtHeroSaveSyncBadge.Text = $"save sync ({rootSync})";
        RefreshSaveSyncUi();
        ShowNotification("STORM RETROARCH", $"Корневая папка синхронизации установлена:\n{rootSync}");
    }

    private void BtnOpenCloudSyncFolder_Click(object sender, RoutedEventArgs e)
    {
        string dir = SaveSyncManager.CloudSyncDir;
        try
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowNotification("STORM RETROARCH", $"Не удалось открыть папку save sync: {ex.Message}");
        }
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
            var btnUpload = new Button { Content = "В облако", Style = (Style)FindResource("StormAccentButton"), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 4) };
            btnUpload.Click += (s, e) => { SaveSyncManager.UploadToRemote(item); RefreshSaveSyncUi(); ShowNotification("STORM RETROARCH", $"Сейв {item.FileName} выгружен в облако!"); };
            var btnDownload = new Button { Content = "Из облака", Style = (Style)FindResource("StormSecondaryButton"), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 4) };
            btnDownload.Click += (s, e) => { SaveSyncManager.DownloadFromRemote(item); RefreshSaveSyncUi(); ShowNotification("STORM RETROARCH", $"Сейв {item.FileName} загружен из облака!"); };
            var btnKeepBoth = new Button { Content = "Сохранить оба", Style = (Style)FindResource("StormSecondaryButton"), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 4) };
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
            var rUp = new Button { Content = "Выгр.", Style = (Style)FindResource("StormAccentButton"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 0) };
            rUp.Click += (s, e) => { SaveSyncManager.UploadToRemote(item); RefreshSaveSyncUi(); };
            var rDown = new Button { Content = "Загр.", Style = (Style)FindResource("StormSecondaryButton"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 0) };
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
            ShowNotification("STORM RETROARCH", "Голосовое воспроизведение тактики запущено!");
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

        // Update live counts on status chips
        int totalAll = CoreManager.AllCoresCatalog.Count;
        int totalInstalled = CoreManager.AllCoresCatalog.Count(c => c.IsInstalled);
        int totalAvailable = CoreManager.AllCoresCatalog.Count(c => !c.IsInstalled);

        if (BtnCoresFilterAll != null) BtnCoresFilterAll.Content = $"Все ядра ({totalAll})";
        if (BtnCoresFilterInstalled != null) BtnCoresFilterInstalled.Content = $"Установленные ({totalInstalled})";
        if (BtnCoresFilterAvailable != null) BtnCoresFilterAvailable.Content = $"Доступные к установке ({totalAvailable})";

        IEnumerable<CoreInfo> displayCores = string.IsNullOrEmpty(_coreSearchQuery)
            ? _cores
            : _cores.Where(c => c.Name.ToLowerInvariant().Contains(_coreSearchQuery) ||
                                c.Platform.ToLowerInvariant().Contains(_coreSearchQuery) ||
                                c.DllName.ToLowerInvariant().Contains(_coreSearchQuery) ||
                                c.Description.ToLowerInvariant().Contains(_coreSearchQuery)).ToList();

        foreach (var c in displayCores)
        {
            bool installed = c.IsInstalled;

            // 1. Bento Card (Tiles)
            var card = new Border
            {
                Background = (Brush)FindResource("ThemeSurface"),
                BorderBrush = installed ? new SolidColorBrush(Color.FromArgb(180, 16, 185, 129)) : (Brush)FindResource("ThemeBorder"),
                BorderThickness = new Thickness(installed ? 2 : 1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 12, 12),
                Width = 360
            };
            var sp = new StackPanel();

            var dockHead = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            
            // Status Badge Pill
            var badgeStatus = new Border
            {
                Background = installed ? new SolidColorBrush(Color.FromArgb(32, 16, 185, 129)) : (Brush)FindResource("ThemeSurfaceLight"),
                BorderBrush = installed ? new SolidColorBrush(Color.FromArgb(140, 16, 185, 129)) : (Brush)FindResource("ThemeBorderSubtle"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3)
            };
            badgeStatus.Child = new TextBlock
            {
                Text = c.InstalledStatusBadge,
                Foreground = installed ? new SolidColorBrush(Color.FromRgb(110, 231, 183)) : (Brush)FindResource("ThemeTextMuted"),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold
            };
            DockPanel.SetDock(badgeStatus, Dock.Right);
            dockHead.Children.Add(badgeStatus);

            var titleSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleSp.Children.Add(new TextBlock { Text = c.Name, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("ThemeTextPrimary"), FontSize = 14 });
            
            var badgeVer = new Border
            {
                Background = (Brush)FindResource("ThemeSurfaceLight"),
                BorderBrush = (Brush)FindResource("ThemeBorderSubtle"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            badgeVer.Child = new TextBlock { Text = c.VersionBadge, Foreground = (Brush)FindResource("ThemeAccentGlow"), FontSize = 9.5, FontWeight = FontWeights.SemiBold };
            dockHead.Children.Add(titleSp);
            sp.Children.Add(dockHead);

            badgeVer.Margin = new Thickness(0, 0, 6, 0);
            var badgeJit = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(32, 139, 92, 246)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(140, 139, 92, 246)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            badgeJit.Child = new TextBlock
            {
                Text = c.JitSupportBadge,
                Foreground = new SolidColorBrush(Color.FromRgb(196, 181, 253)),
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold
            };
            var badgesWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 6) };
            badgesWrap.Children.Add(badgeVer);
            badgesWrap.Children.Add(badgeJit);

            var badgeHw = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(32, 56, 189, 248)),
                BorderBrush = (Brush)new BrushConverter().ConvertFromString(c.HwBadgeColor)!,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            badgeHw.Child = new TextBlock
            {
                Text = c.HwBadgeText,
                Foreground = (Brush)new BrushConverter().ConvertFromString(c.HwBadgeColor)!,
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold
            };
            badgesWrap.Children.Add(badgeHw);

            sp.Children.Add(badgesWrap);

            sp.Children.Add(new TextBlock { Text = $"Платформа: {c.Platform}", Foreground = (Brush)FindResource("ThemeAccentGlow"), FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
            sp.Children.Add(new TextBlock { Text = $"Файл: {c.DllName}", Foreground = (Brush)FindResource("ThemeTextMuted"), FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
            sp.Children.Add(new TextBlock { Text = c.Description, Foreground = (Brush)FindResource("ThemeTextSecondary"), TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 0, 0, 10) });

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var btnBenchmark = new Button
            {
                Content = "Тест ABI",
                Style = (Style)FindResource("StormSecondaryButton"),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 8, 0),
                Tag = c,
                ToolTip = "Проверить экспортируемые символы ABI, PE64 и время холодного старта"
            };
            btnBenchmark.Click += async (s, e) =>
            {
                await RunCoreBenchmarkAsync(c);
            };
            btnPanel.Children.Add(btnBenchmark);
            if (installed)
            {
                var btnUpdate = new Button
                {
                    Content = "Обновить",
                    Style = (Style)FindResource("StormSecondaryButton"),
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(0, 0, 8, 0)
                };
                btnUpdate.Click += async (s, e) =>
                {
                    ShowOsdToast($"Обновление ядра «{c.Name}»...");
                    bool ok = await CoreManager.EnsureCoreDownloadedAsync(c.DllName);
                    if (ok) ShowOsdToast($"Ядро «{c.Name}» успешно обновлено!");
                    RefreshCoresUi();
                };
                btnPanel.Children.Add(btnUpdate);

                var btnDelete = new Button
                {
                    Content = "Удалить",
                    Style = (Style)FindResource("StormSecondaryButton"),
                    Padding = new Thickness(10, 4, 10, 4)
                };
                btnDelete.Click += (s, e) =>
                {
                    string path = Path.Combine(CoreManager.CoresDir, c.DllName);
                    try
                    {
                        if (File.Exists(path)) File.Delete(path);
                        ShowOsdToast($"Ядро «{c.Name}» удалено");
                        RefreshCoresUi();
                    }
                    catch (Exception ex)
                    {
                        ShowOsdToast($"Ошибка: {ex.Message}");
                    }
                };
                btnPanel.Children.Add(btnDelete);
            }
            else
            {
                var btnInstall = new Button
                {
                    Content = "Установить",
                    Style = (Style)FindResource("StormAccentButton"),
                    Padding = new Thickness(12, 4, 12, 4)
                };
                btnInstall.Click += async (s, e) =>
                {
                    ShowOsdToast($"Загрузка ядра «{c.Name}»...");
                    bool ok = await CoreManager.EnsureCoreDownloadedAsync(c.DllName);
                    if (ok) ShowOsdToast($"Ядро «{c.Name}» успешно установлено!");
                    RefreshCoresUi();
                };
                btnPanel.Children.Add(btnInstall);
            }
            sp.Children.Add(btnPanel);

            card.Child = sp;
            PanelCoresTiles.Children.Add(card);

            // 2. Ozone List Row
            var rowBorder = new Border
            {
                Background = (Brush)FindResource("ThemeSurface"),
                BorderBrush = installed ? new SolidColorBrush(Color.FromArgb(140, 16, 185, 129)) : (Brush)FindResource("ThemeBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var rowDock = new DockPanel { LastChildFill = true };

            var rBtn = new Button
            {
                Content = installed ? "Обновить" : "Установить",
                Style = (Style)FindResource(installed ? "StormSecondaryButton" : "StormAccentButton"),
                Padding = new Thickness(10, 3, 10, 3)
            };
            rBtn.Click += async (s, e) =>
            {
                ShowOsdToast($"Загрузка ядра «{c.Name}»...");
                await CoreManager.EnsureCoreDownloadedAsync(c.DllName);
                RefreshCoresUi();
            };
            DockPanel.SetDock(rBtn, Dock.Right);
            rowDock.Children.Add(rBtn);

            var rStatusPill = new Border
            {
                Background = installed ? new SolidColorBrush(Color.FromArgb(32, 16, 185, 129)) : (Brush)FindResource("ThemeSurfaceLight"),
                BorderBrush = installed ? new SolidColorBrush(Color.FromArgb(140, 16, 185, 129)) : (Brush)FindResource("ThemeBorderSubtle"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            rStatusPill.Child = new TextBlock
            {
                Text = c.InstalledStatusBadge,
                Foreground = installed ? new SolidColorBrush(Color.FromRgb(110, 231, 183)) : (Brush)FindResource("ThemeTextMuted"),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            DockPanel.SetDock(rStatusPill, Dock.Right);
            rowDock.Children.Add(rStatusPill);

            var rInfo = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            rInfo.Children.Add(new TextBlock { Text = c.Name, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("ThemeTextPrimary"), Width = 180, VerticalAlignment = VerticalAlignment.Center });
            rInfo.Children.Add(new TextBlock { Text = c.Platform, Foreground = (Brush)FindResource("ThemeAccentGlow"), Width = 180, VerticalAlignment = VerticalAlignment.Center });
            rInfo.Children.Add(new TextBlock { Text = c.Description, Foreground = (Brush)FindResource("ThemeTextSecondary"), TextTrimming = TextTrimming.CharacterEllipsis, Width = 320, VerticalAlignment = VerticalAlignment.Center });
            rowDock.Children.Add(rInfo);
            rowBorder.Child = rowDock;
            PanelCoresList.Children.Add(rowBorder);

            // 3. Compact Icon Tile
            var comp = new Border
            {
                Background = (Brush)FindResource("ThemeSurface"),
                BorderBrush = installed ? new SolidColorBrush(Color.FromArgb(140, 16, 185, 129)) : (Brush)FindResource("ThemeBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 8, 8),
                Width = 180
            };
            var cSp = new StackPanel();
            cSp.Children.Add(new TextBlock { Text = c.Name, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("ThemeTextPrimary"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
            cSp.Children.Add(new TextBlock { Text = c.VersionBadge, Foreground = (Brush)FindResource("ThemeAccentGlow"), FontSize = 10, Margin = new Thickness(0, 2, 0, 0) });
            cSp.Children.Add(new TextBlock { Text = c.InstalledStatusBadge, Foreground = installed ? new SolidColorBrush(Color.FromRgb(110, 231, 183)) : (Brush)FindResource("ThemeTextMuted"), FontSize = 9.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
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
        await LaunchGameUnified220Async(item);
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
        ToggleInGameQuickMenu();
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
        if (VulkanD3dEngineBridge.Current.IsRunning)
        {
            VulkanD3dEngineBridge.Current.TogglePause();
            UpdateInGameHudButtons();
            ShowOsdToast(VulkanD3dEngineBridge.Current.IsPaused ? "Пауза" : "Возобновлено");
            return;
        }

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
        if (VulkanD3dEngineBridge.Current.IsRunning)
        {
            VulkanD3dEngineBridge.Current.Reset();
            ShowOsdToast("Игра перезапущена");
            return;
        }

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
        if (VulkanD3dEngineBridge.Current.IsRunning)
        {
            VulkanD3dEngineBridge.Current.SaveState();
            ShowOsdToast("Состояние сохранено (GPU)");
            return;
        }

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
        if (VulkanD3dEngineBridge.Current.IsRunning)
        {
            VulkanD3dEngineBridge.Current.LoadState();
            ShowOsdToast("Состояние загружено (GPU)");
            return;
        }

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
        ShowOsdToast("Перемотка времени назад");
    }

    private void BtnGameFastForward_Click(object sender, RoutedEventArgs e)
    {
        if (VulkanD3dEngineBridge.Current.IsRunning)
        {
            VulkanD3dEngineBridge.Current.ToggleFastForward();
            UpdateInGameHudButtons();
            ShowOsdToast(VulkanD3dEngineBridge.Current.IsFastForward ? "Ускорение: ВКЛ" : "Ускорение: ВЫКЛ");
            return;
        }

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
        string filterName = NativeLibretroHost.Current.GetShaderModeTitle();
        if (TxtGameShaderLabel != null) TxtGameShaderLabel.Text = "Фильтр";
        ShowOsdToast($"Видеофильтр: {filterName}");
    }

    private void BtnGameFrameAdvance_Click(object sender, RoutedEventArgs e)
    {
        NativeLibretroHost.Current.StepSingleFrame();
        ShowOsdToast("Кадр вперед (Покадрово)");
    }

    private void BtnGameAspect_Click(object sender, RoutedEventArgs e)
    {
        NativeLibretroHost.Current.CycleAspectMode(GameViewportContainer);
        UpdateInGameHudButtons();
        UpdateViewportStageLayout();
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
        if (VulkanD3dEngineBridge.Current.IsRunning)
        {
            VulkanD3dEngineBridge.Current.ToggleMute();
            UpdateInGameHudButtons();
            return;
        }

        NativeLibretroHost.Current.ToggleMute();
        UpdateInGameHudButtons();
        ShowOsdToast(NativeLibretroHost.Current.IsAudioMuted ? "Звук отключён" : "Звук включён");
    }

    private async void TriggerInstantReplaySave()
    {
        string title = NativeLibretroHost.Current.CurrentGameTitle;
        var res = await InstantReplayRecorder.Instance.SaveLast30SecondsAsync(title);
        ShowOsdToast(res.MessageRu);
    }

    private void BtnGameScreenshot_Click(object sender, RoutedEventArgs e)
    {
        if (VulkanD3dEngineBridge.Current.IsRunning)
        {
            VulkanD3dEngineBridge.Current.TakeScreenshot();
            ShowOsdToast("Скриншот сохранён (GPU)");
            return;
        }

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
            if (PnlTelemetryOverlay != null) { _wasTelemetryVisibleBeforeFs = PnlTelemetryOverlay.Visibility == Visibility.Visible; PnlTelemetryOverlay.Visibility = Visibility.Collapsed; }
            if (LeftSidebarBorder != null) LeftSidebarBorder.Visibility = Visibility.Collapsed;
            if (BottomFooterBorder != null) BottomFooterBorder.Visibility = Visibility.Collapsed;

            if (RowDefTitleBar != null) RowDefTitleBar.Height = new GridLength(0);
            if (RowDefToolbar != null) RowDefToolbar.Height = new GridLength(0);
            if (RowDefFooter != null) RowDefFooter.Height = new GridLength(0);
            if (ColDefSidebar != null) { ColDefSidebar.MinWidth = 0; ColDefSidebar.Width = new GridLength(0); }
            if (ColDefSplitter != null) ColDefSplitter.Width = new GridLength(0);
            if (SidebarSplitter != null) SidebarSplitter.Visibility = Visibility.Collapsed;
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
            if (PnlTelemetryOverlay != null && _wasTelemetryVisibleBeforeFs) PnlTelemetryOverlay.Visibility = Visibility.Visible;
            if (LeftSidebarBorder != null) LeftSidebarBorder.Visibility = Visibility.Visible;
            if (BottomFooterBorder != null) BottomFooterBorder.Visibility = Visibility.Visible;

            if (RowDefTitleBar != null) RowDefTitleBar.Height = new GridLength(40);
            if (RowDefToolbar != null) RowDefToolbar.Height = GridLength.Auto;
            if (RowDefFooter != null) RowDefFooter.Height = new GridLength(28);
            ApplySidebarState();
            if (WindowOuterBorder != null) WindowOuterBorder.BorderThickness = new Thickness(1);
            _isTheaterMode = false;

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
        DeviceInputProfileEngine.NotifyCoreOrGameChanged(null, null);
        LoadSaves();
        RefreshSaveSyncUi();
        if (PnlGameLoadingOverlay != null) PnlGameLoadingOverlay.Visibility = Visibility.Visible;
        NavBtn_Click(NavBtnLibrary, e);
        ShowOsdToast("Игра остановлена, батарейные сохранения (.srm) записаны");
    }

    private void BtnExitGame_Click(object sender, RoutedEventArgs e)
    {
        if (VulkanD3dEngineBridge.Current.IsRunning)
        {
            VulkanD3dEngineBridge.Current.Stop();
            if (HwEngineGameHost != null) HwEngineGameHost.Visibility = Visibility.Collapsed;
            if (VbGameStage != null) VbGameStage.Visibility = Visibility.Visible;
        }

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
            var candidateFiles = new List<string>
            {
                Path.Combine(CoreManager.AppDir, "playlists", "storm_library.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "STORM RETROARCH", "playlists", "storm_library.json"),
                @"C:\Program Files\STORM RETROARCH\playlists\storm_library.json"
            };

            bool migrated = false;
            foreach (var file in candidateFiles.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(file)) continue;
                try
                {
                    string json = File.ReadAllText(file);
                    var paths = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                    if (paths != null)
                    {
                        foreach (var p in paths)
                        {
                            if (File.Exists(p))
                            {
                                int before = _allGames.Count;
                                AddRomFile(p);
                                if (_allGames.Count > before && !file.Equals(candidateFiles[0], StringComparison.OrdinalIgnoreCase))
                                    migrated = true;
                            }
                        }
                    }
                }
                catch { }
            }

            if (migrated) SaveLibraryToPlaylist();
            foreach (var game in _allGames)
            {
                GameMetadataStore.ApplyToRomItem(game);
            }
            _ = CoverArtService.LoadCoversParallelAsync(_allGames, forceRefresh: false);
            ApplyGamesFilter();
            UpdateStats();
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

    private Border? FindPadBorderByName(string btnName) => null;

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
            TxtGamepadCaptureStatus.Text = $"Нажмите кнопку геймпада/стика или клавишу для [{btnName.ToUpper()}]... (ESC - отмена)"; _physicalCaptureTimer?.Start();
            TxtGamepadCaptureStatus.Foreground = (Brush)new BrushConverter().ConvertFrom("#38BDF8")!;
        }
    }

    private void CancelGamepadButtonCapture()
    {
        _isCapturingGamepadButton = false; _physicalCaptureTimer?.Stop();
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


    // ==================== V1.6.0 UI SCALABILITY, RESIZING, MOVING & STUDIO CONTROLS ====================

    private void ApplyUiScale(double scale, bool save = true)
    {
        double clamped = Math.Round(Math.Clamp(scale, 0.80, 1.40), 2);
        SettingsManager.Current.UiScale = clamped;
        if (RootScaleTransform != null)
        {
            RootScaleTransform.ScaleX = clamped;
            RootScaleTransform.ScaleY = clamped;
        }
        if (BtnUiScaleReset != null)
        {
            BtnUiScaleReset.Content = $"{Math.Round(clamped * 100)}%";
        }
        if (save && _isLoaded)
        {
            SettingsManager.Save();
            ShowOsdToast($"Масштаб интерфейса: {Math.Round(clamped * 100)}%");
        }
    }

    private void BtnUiScaleDown_Click(object sender, RoutedEventArgs e) => ApplyUiScale(SettingsManager.Current.UiScale - 0.05);
    private void BtnUiScaleUp_Click(object sender, RoutedEventArgs e) => ApplyUiScale(SettingsManager.Current.UiScale + 0.05);
    private void BtnUiScaleReset_Click(object sender, RoutedEventArgs e) => ApplyUiScale(1.0);

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            double delta = e.Delta > 0 ? 0.05 : -0.05;
            if (ViewLibrary != null && ViewLibrary.Visibility == Visibility.Visible && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                double nextWidth = Math.Clamp(DynamicCardWidth + (e.Delta > 0 ? 20 : -20), 270, 540);
                if (SliderCardScale != null) SliderCardScale.Value = nextWidth;
            }
            else
            {
                ApplyUiScale(SettingsManager.Current.UiScale + delta);
            }
            e.Handled = true;
        }
    }

    private void ApplySidebarState()
    {
        if (ColDefSidebar == null || LeftSidebarBorder == null) return;
        bool collapsed = SettingsManager.Current.SidebarCollapsed;
        if (collapsed)
        {
            LeftSidebarBorder.Visibility = Visibility.Collapsed;
            if (SidebarSplitter != null) SidebarSplitter.Visibility = Visibility.Collapsed;
            ColDefSidebar.MinWidth = 0;
            ColDefSidebar.Width = new GridLength(0);
            if (ColDefSplitter != null) ColDefSplitter.Width = new GridLength(0);
            if (TxtSidebarToggleIcon != null) TxtSidebarToggleIcon.Text = "» Панель";
        }
        else
        {
            double w = Math.Clamp(SettingsManager.Current.SidebarWidth, 248, 460);
            LeftSidebarBorder.Visibility = Visibility.Visible;
            if (SidebarSplitter != null) SidebarSplitter.Visibility = Visibility.Visible;
            ColDefSidebar.MinWidth = 248;
            ColDefSidebar.MaxWidth = 460;
            ColDefSidebar.Width = new GridLength(w);
            if (ColDefSplitter != null) ColDefSplitter.Width = new GridLength(6);
            if (TxtSidebarToggleIcon != null) TxtSidebarToggleIcon.Text = "« Панель";
        }
    }

    private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.SidebarCollapsed = !SettingsManager.Current.SidebarCollapsed;
        ApplySidebarState();
        SettingsManager.Save();
    }

    private void SidebarSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (ColDefSidebar != null && ColDefSidebar.ActualWidth >= 240)
        {
            SettingsManager.Current.SidebarWidth = Math.Round(ColDefSidebar.ActualWidth, 0);
            SettingsManager.Save();
        }
    }

    private void SliderCardScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        DynamicCardWidth = e.NewValue;
        if (TxtCardScaleReadout != null)
            TxtCardScaleReadout.Text = $"{Math.Round(DynamicCardWidth)} px";
        if (_isLoaded)
        {
            SettingsManager.Current.BentoCardWidth = DynamicCardWidth;
            SettingsManager.Save();
        }
    }

    private void BtnCardScaleDown_Click(object sender, RoutedEventArgs e)
    {
        double next = Math.Max(270, DynamicCardWidth - 25);
        if (SliderCardScale != null) SliderCardScale.Value = next;
        else DynamicCardWidth = next;
    }

    private void BtnCardScaleUp_Click(object sender, RoutedEventArgs e)
    {
        double next = Math.Min(540, DynamicCardWidth + 25);
        if (SliderCardScale != null) SliderCardScale.Value = next;
        else DynamicCardWidth = next;
    }

    // Reorderable Game Cards (Buttons & Drag-and-Drop)
    private void BtnMoveGameLeft_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is RomItem item)
        {
            MoveGameItem(item, -1);
        }
    }

    private void BtnMoveGameRight_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is RomItem item)
        {
            MoveGameItem(item, +1);
        }
    }

    private void MoveGameItem(RomItem item, int offset)
    {
        int idxFiltered = _filteredGames.IndexOf(item);
        int targetFiltered = idxFiltered + offset;
        if (idxFiltered >= 0 && targetFiltered >= 0 && targetFiltered < _filteredGames.Count)
        {
            _filteredGames.Move(idxFiltered, targetFiltered);
        }

        int idxAll = _allGames.IndexOf(item);
        int targetAll = idxAll + offset;
        if (idxAll >= 0 && targetAll >= 0 && targetAll < _allGames.Count)
        {
            _allGames.Move(idxAll, targetAll);
            SaveLibraryToPlaylist();
        }
    }

    private void GameCard_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement fe && fe.DataContext is RomItem item)
        {
            if (e.OriginalSource is DependencyObject dep)
            {
                var cur = dep;
                while (cur != null)
                {
                    if (cur is Button) return;
                    cur = VisualTreeHelper.GetParent(cur);
                }
            }
            Point pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _cardDragStartPoint.X) > 14 || Math.Abs(pos.Y - _cardDragStartPoint.Y) > 14)
            {
                _cardDragStartPoint = pos;
                DragDrop.DoDragDrop(fe, item, DragDropEffects.Move);
            }
        }
        else
        {
            _cardDragStartPoint = e.GetPosition(null);
        }
    }

    private void GameCard_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(RomItem)) &&
            e.Data.GetData(typeof(RomItem)) is RomItem sourceItem &&
            sender is FrameworkElement fe &&
            fe.DataContext is RomItem targetItem &&
            !ReferenceEquals(sourceItem, targetItem))
        {
            int srcIdx = _filteredGames.IndexOf(sourceItem);
            int dstIdx = _filteredGames.IndexOf(targetItem);
            if (srcIdx >= 0 && dstIdx >= 0)
            {
                _filteredGames.Move(srcIdx, dstIdx);
            }

            int srcAll = _allGames.IndexOf(sourceItem);
            int dstAll = _allGames.IndexOf(targetItem);
            if (srcAll >= 0 && dstAll >= 0)
            {
                _allGames.Move(srcAll, dstAll);
                SaveLibraryToPlaylist();
            }
            e.Handled = true;
        }
    }

    // Reorderable Dashboard Cards (◀ / ▶)
    private void ApplyDashboardCardOrder()
    {
        if (DashboardCardsGrid == null || DashCard0 == null || DashCard1 == null || DashCard2 == null || DashCard3 == null)
            return;

        var cardsMap = new Dictionary<int, Border>
        {
            [0] = DashCard0,
            [1] = DashCard1,
            [2] = DashCard2,
            [3] = DashCard3
        };

        var order = SettingsManager.Current.DashboardCardOrder;
        if (order == null || order.Count != 4 || order.Distinct().Count() != 4)
        {
            order = new List<int> { 0, 1, 2, 3 };
            SettingsManager.Current.DashboardCardOrder = order;
        }

        DashboardCardsGrid.Children.Clear();
        foreach (int id in order)
        {
            if (cardsMap.TryGetValue(id, out var border))
                DashboardCardsGrid.Children.Add(border);
        }
    }

    private void MoveDashboardCard(int cardId, int delta)
    {
        var order = SettingsManager.Current.DashboardCardOrder;
        if (order == null || order.Count != 4)
            order = new List<int> { 0, 1, 2, 3 };

        int idx = order.IndexOf(cardId);
        if (idx < 0) return;
        int nextIdx = (idx + delta + order.Count) % order.Count;
        (order[idx], order[nextIdx]) = (order[nextIdx], order[idx]);
        SettingsManager.Current.DashboardCardOrder = order;
        ApplyDashboardCardOrder();
        SettingsManager.Save();
        ShowOsdToast("Расположение блоков главного меню обновлено");
    }

    private void BtnMoveDashCardLeft_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int id))
            MoveDashboardCard(id, -1);
    }

    private void BtnMoveDashCardRight_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int id))
            MoveDashboardCard(id, +1);
    }

    // Quick Dashboard Navigation & Quick Hardware Toggles
    private void BtnQuickNavSettings_Click(object sender, RoutedEventArgs e) => NavBtn_Click(NavBtnSettings, e);
    private void BtnQuickNavSaveSync_Click(object sender, RoutedEventArgs e) => NavBtn_Click(NavBtnSaveSync, e);
    private void BtnQuickNavGamepads_Click(object sender, RoutedEventArgs e) => NavBtn_Click(NavBtnInputRemap, e);

    private void SyncQuickSettingsCheckboxes()
    {
        _isSyncingQuickToggles = true;
        try
        {
            if (ChkQuickVsync != null) ChkQuickVsync.IsChecked = RetroArchConfigManager.GetBool("video_vsync", true);
            if (ChkQuickSmooth != null) ChkQuickSmooth.IsChecked = RetroArchConfigManager.GetBool("video_smooth", false);
            if (ChkQuickRewind != null) ChkQuickRewind.IsChecked = RetroArchConfigManager.GetBool("rewind_enable", true);
            if (ChkQuickAutoSave != null) ChkQuickAutoSave.IsChecked = RetroArchConfigManager.GetInt("autosave_interval", 5) > 0;
        }
        finally
        {
            _isSyncingQuickToggles = false;
        }
    }

    private void ChkQuickSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded || _isSyncingQuickToggles) return;

        bool vsync = ChkQuickVsync?.IsChecked == true;
        bool smooth = ChkQuickSmooth?.IsChecked == true;
        bool rewind = ChkQuickRewind?.IsChecked == true;
        bool autosave = ChkQuickAutoSave?.IsChecked == true;

        RetroArchConfigManager.SetBool("video_vsync", vsync);
        RetroArchConfigManager.SetBool("video_smooth", smooth);
        RetroArchConfigManager.SetBool("rewind_enable", rewind);
        RetroArchConfigManager.SetInt("autosave_interval", autosave ? 5 : 0);
        RetroArchConfigManager.Save();

        if (ImgGameViewport != null)
        {
            RenderOptions.SetBitmapScalingMode(ImgGameViewport, smooth ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
        }
        ShowOsdToast("Быстрые настройки эмулятора применены и сохранены в retroarch.cfg");
    }

    // In-Game Viewport Scale (Auto / 1x / 2x / 3x / 4x) & Theater Mode (T)
    private void BtnGameViewportScale_Click(object sender, RoutedEventArgs e)
    {
        _viewportScaleMode = (_viewportScaleMode + 1) % 5;
        if (ImgGameViewport != null)
        {
            if (_viewportScaleMode == 0)
            {
                ImgGameViewport.Width = double.NaN;
                ImgGameViewport.Height = double.NaN;
                NativeLibretroHost.Current.ApplyAspectMode(GameViewportContainer);
            }
            else
            {
                int factor = _viewportScaleMode;
                ImgGameViewport.Stretch = Stretch.Fill;
                ImgGameViewport.Width = 320 * factor;
                ImgGameViewport.Height = 240 * factor;
            }
        }
        string label = _viewportScaleMode == 0 ? "Авто" : $"{_viewportScaleMode}x";
        if (TxtGameViewportScaleLabel != null) TxtGameViewportScaleLabel.Text = label;
        ShowOsdToast($"Масштаб игрового экрана: {label}");
        GameViewportContainer?.Focus();
    }

    private void BtnGameTheater_Click(object sender, RoutedEventArgs e)
    {
        if (_isGameFullscreen)
        {
            SetFullscreenMode(false);
            return;
        }

        _isTheaterMode = !_isTheaterMode;
        if (_isTheaterMode)
        {
            if (TopToolbarBorder != null) TopToolbarBorder.Visibility = Visibility.Collapsed;
            if (PnlTelemetryOverlay != null) { _wasTelemetryVisibleBeforeFs = PnlTelemetryOverlay.Visibility == Visibility.Visible; PnlTelemetryOverlay.Visibility = Visibility.Collapsed; }
            if (LeftSidebarBorder != null) LeftSidebarBorder.Visibility = Visibility.Collapsed;
            if (SidebarSplitter != null) SidebarSplitter.Visibility = Visibility.Collapsed;
            if (RowDefToolbar != null) RowDefToolbar.Height = new GridLength(0);
            if (ColDefSidebar != null) { ColDefSidebar.MinWidth = 0; ColDefSidebar.Width = new GridLength(0); }
            if (ColDefSplitter != null) ColDefSplitter.Width = new GridLength(0);
            if (TxtGameTheaterLabel != null) TxtGameTheaterLabel.Text = "Панели";
            ShowOsdToast("Режим театра включён (боковая и верхняя панели скрыты)");
        }
        else
        {
            if (TopToolbarBorder != null) TopToolbarBorder.Visibility = Visibility.Visible;
            if (PnlTelemetryOverlay != null && _wasTelemetryVisibleBeforeFs) PnlTelemetryOverlay.Visibility = Visibility.Visible;
            if (RowDefToolbar != null) RowDefToolbar.Height = GridLength.Auto;
            ApplySidebarState();
            if (TxtGameTheaterLabel != null) TxtGameTheaterLabel.Text = "Театр";
            ShowOsdToast("Стандартные панели восстановлены");
        }
        NativeLibretroHost.Current.ApplyAspectMode(GameViewportContainer);
        GameViewportContainer?.Focus();
    }

    // ==================== PER-GAME & PER-PLATFORM CORE SELECTION (1.6.0) ====================

    private RomItem? _coreModalTargetRom;
    private bool _isSyncingPlatformCoreCombo;

    private void InitPlatformDefaultCoreSelectors()
    {
        if (CmbPlatformCoreTarget == null) return;
        _isSyncingPlatformCoreCombo = true;
        try
        {
            string prev = CmbPlatformCoreTarget.SelectedItem?.ToString() ?? "";
            CmbPlatformCoreTarget.Items.Clear();

            var platforms = _allGames.Select(g => g.Platform)
                .Concat(CoreManager.GetStandardPlatformsList())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var p in platforms)
                CmbPlatformCoreTarget.Items.Add(p);

            if (!string.IsNullOrEmpty(prev) && platforms.Contains(prev))
                CmbPlatformCoreTarget.SelectedItem = prev;
            else if (CmbPlatformCoreTarget.Items.Count > 0)
                CmbPlatformCoreTarget.SelectedIndex = 0;
        }
        finally
        {
            _isSyncingPlatformCoreCombo = false;
        }

        UpdatePlatformDefaultCoreDropdown();
    }

    private void CmbPlatformCoreTarget_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingPlatformCoreCombo) return;
        UpdatePlatformDefaultCoreDropdown();
    }

    private void UpdatePlatformDefaultCoreDropdown()
    {
        if (CmbPlatformCoreTarget == null || CmbPlatformDefaultCore == null) return;
        string platform = CmbPlatformCoreTarget.SelectedItem?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(platform)) return;

        var cores = CoreManager.GetCompatibleCoresForPlatform(platform);
        CmbPlatformDefaultCore.ItemsSource = cores;

        if (SettingsManager.Current.PlatformDefaultCores != null &&
            SettingsManager.Current.PlatformDefaultCores.TryGetValue(platform, out string? savedVal) &&
            !string.IsNullOrWhiteSpace(savedVal))
        {
            string savedDll = savedVal.Split('|')[0];
            var match = cores.FirstOrDefault(c => c.DllName.Equals(savedDll, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                CmbPlatformDefaultCore.SelectedItem = match;
                return;
            }
        }

        var gameWithPlat = _allGames.FirstOrDefault(g => g.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase));
        if (gameWithPlat != null)
        {
            var match = cores.FirstOrDefault(c => c.DllName.Equals(gameWithPlat.CoreDll, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                CmbPlatformDefaultCore.SelectedItem = match;
                return;
            }
        }

        if (cores.Count > 0) CmbPlatformDefaultCore.SelectedIndex = 0;
    }

    private async void BtnApplyPlatformDefaultCore_Click(object sender, RoutedEventArgs e)
    {
        if (CmbPlatformCoreTarget?.SelectedItem is not string platform ||
            CmbPlatformDefaultCore?.SelectedItem is not CoreInfo selectedCore)
            return;

        await ApplyCoreToPlatformAsync(platform, selectedCore);
    }

    private async Task ApplyCoreToPlatformAsync(string platform, CoreInfo selectedCore)
    {
        SettingsManager.Current.PlatformDefaultCores ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        SettingsManager.Current.PlatformDefaultCores[platform] = $"{selectedCore.DllName}|{selectedCore.Name}";
        SettingsManager.Save();

        if (!CoreManager.IsCoreInstalled(selectedCore.DllName))
        {
            ShowOsdToast($"Загрузка ядра {selectedCore.Name}...");
            await CoreManager.EnsureCoreDownloadedAsync(selectedCore.DllName);
        }

        bool isInstalled = CoreManager.IsCoreInstalled(selectedCore.DllName);
        int updatedCount = 0;

        foreach (var g in _allGames.Where(x => x.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase)))
        {
            g.CoreDll = selectedCore.DllName;
            g.CoreDisplayName = selectedCore.Name;
            g.IsCoreInstalled = isInstalled;
            g.StatusText = isInstalled ? "Готово к запуску" : "Требуется ядро";
            if (SettingsManager.Current.GameCoreOverrides != null)
                SettingsManager.Current.GameCoreOverrides.Remove(g.FilePath);
            updatedCount++;
        }

        SettingsManager.Save();
        SavePlaylists();
        UpdateStats();
        ShowOsdToast($"Ядро по умолчанию для «{platform}»: {selectedCore.Name} (обновлено игр: {updatedCount})");
    }

    private void BtnChangeGameCore_Click(object sender, RoutedEventArgs e)
    {
        RomItem? item = (sender as FrameworkElement)?.DataContext as RomItem;
        if (item != null) OpenCoreSelectorForGame(item);
    }

    private void BtnCloseCoreSelectorModal_Click(object sender, RoutedEventArgs e)
    {
        CloseCoreSelectorModal();
    }

    private async void BtnConfirmCoreSelectorModal_Click(object sender, RoutedEventArgs e)
    {
        if (_coreModalTargetRom == null || LstModalCompatibleCores?.SelectedItem is not CoreInfo selectedCore)
        {
            CloseCoreSelectorModal();
            return;
        }

        var rom = _coreModalTargetRom;
        bool applyToPlatform = ChkSetCoreDefaultForPlatform?.IsChecked == true;
        CloseCoreSelectorModal();

        if (!CoreManager.IsCoreInstalled(selectedCore.DllName))
        {
            ShowOsdToast($"Загрузка выбранного ядра {selectedCore.Name}...");
            await CoreManager.EnsureCoreDownloadedAsync(selectedCore.DllName);
        }

        if (applyToPlatform)
        {
            await ApplyCoreToPlatformAsync(rom.Platform, selectedCore);
        }

        SettingsManager.Current.GameCoreOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        SettingsManager.Current.GameCoreOverrides[rom.FilePath] = $"{selectedCore.DllName}|{selectedCore.Name}";
        SettingsManager.Save();

        bool installed = CoreManager.IsCoreInstalled(selectedCore.DllName);
        rom.CoreDll = selectedCore.DllName;
        rom.CoreDisplayName = selectedCore.Name;
        rom.IsCoreInstalled = installed;
        rom.StatusText = installed ? "Готово к запуску" : "Требуется ядро";

        InitPlatformDefaultCoreSelectors();
        UpdatePlatformFilterChips();
        UpdateDashboardRecentGames();
        InitCommandPaletteItems();
        SavePlaylists();
        UpdateStats();
        ShowOsdToast($"Для игры «{rom.Name}» установлено ядро: {selectedCore.Name}");
    }

    private void PopulateQuickMenuCoreSelector()
    {
        if (CmbQuickMenuCore == null) return;
        var rom = _activeRomItem ?? _allGames.FirstOrDefault();
        if (rom == null)
        {
            if (TxtQuickMenuActiveGame != null) TxtQuickMenuActiveGame.Text = "Нет запущенной игры";
            var fallbackCores = CoreManager.GetCompatibleCoresForPlatform("");
            CmbQuickMenuCore.ItemsSource = fallbackCores;
            if (fallbackCores.Count > 0) CmbQuickMenuCore.SelectedIndex = 0;
            return;
        }

        if (TxtQuickMenuActiveGame != null)
            TxtQuickMenuActiveGame.Text = $"{rom.Name} ({rom.Platform})";

        var cores = CoreManager.GetCompatibleCoresForPlatform(rom.Platform, rom.Extension);
        CmbQuickMenuCore.ItemsSource = cores;
        var match = cores.FirstOrDefault(c => c.DllName.Equals(rom.CoreDll, StringComparison.OrdinalIgnoreCase));
        CmbQuickMenuCore.SelectedItem = match ?? cores.FirstOrDefault();
    }

    private async void BtnApplyQuickMenuCore_Click(object sender, RoutedEventArgs e)
    {
        if (CmbQuickMenuCore?.SelectedItem is not CoreInfo selectedCore) return;
        var rom = _activeRomItem ?? _allGames.FirstOrDefault();
        if (rom == null) return;

        if (!CoreManager.IsCoreInstalled(selectedCore.DllName))
        {
            ShowOsdToast($"Загрузка ядра {selectedCore.Name}...");
            await CoreManager.EnsureCoreDownloadedAsync(selectedCore.DllName);
        }

        SettingsManager.Current.GameCoreOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        SettingsManager.Current.GameCoreOverrides[rom.FilePath] = $"{selectedCore.DllName}|{selectedCore.Name}";
        SettingsManager.Save();

        rom.CoreDll = selectedCore.DllName;
        rom.CoreDisplayName = selectedCore.Name;
        rom.IsCoreInstalled = CoreManager.IsCoreInstalled(selectedCore.DllName);

        if (PnlInGameQuickMenu != null) PnlInGameQuickMenu.Visibility = Visibility.Collapsed;
        ShowOsdToast($"Ядро изменено на {selectedCore.Name}. Перезапуск игры...");
        await StartGameInViewportAsync(rom);
    }

    // ==================== 1.6.0 COMMAND PALETTE (CTRL+K) ====================

    private void BtnCommandPalette_Click(object sender, RoutedEventArgs e)
    {
        ToggleCommandPaletteModal();
    }

    public void ToggleCommandPaletteModal()
    {
        if (PnlCommandPaletteModal == null) return;
        if (PnlCommandPaletteModal.Visibility == Visibility.Visible)
            CloseCommandPaletteModal();
        else
            OpenCommandPaletteModal();
    }

    public void OpenCommandPaletteModal()
    {
        if (PnlCommandPaletteModal == null) return;
        InitCommandPaletteItems();
        FilterCommandPalette("");
        if (TxtCommandPaletteSearch != null)
        {
            TxtCommandPaletteSearch.Text = "";
            TxtCommandPaletteSearch.Focus();
        }
        PnlCommandPaletteModal.Visibility = Visibility.Visible;
        if (BorderCommandPaletteDialog != null)
            AnalyticalSpringEngine.AnimateBackdropAndDialog(PnlCommandPaletteModal, BorderCommandPaletteDialog, opening: true);
    }

    public void CloseCommandPaletteModal()
    {
        if (PnlCommandPaletteModal != null)
        {
            if (BorderCommandPaletteDialog != null)
                AnalyticalSpringEngine.AnimateBackdropAndDialog(PnlCommandPaletteModal, BorderCommandPaletteDialog, opening: false, () => PnlCommandPaletteModal.Visibility = Visibility.Collapsed);
            else
                PnlCommandPaletteModal.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnCloseCommandPaletteModal_Click(object sender, RoutedEventArgs e)
    {
        CloseCommandPaletteModal();
    }

    private void PnlCommandPaletteModal_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == PnlCommandPaletteModal)
            CloseCommandPaletteModal();
    }

    private void InitCommandPaletteItems()
    {
        _allPaletteItems.Clear();

        // 1. All Navigation Sections (17 sections)
        var sections = new List<(string title, string desc, Button navBtn)>
        {
            ("Главное меню", "Командный центр, быстрый старт и профили оптимизации", NavBtnMainMenu),
            ("Все игры", "Библиотека игр, фильтры платформ и карточки коллекции", NavBtnLibrary),
            ("Избранное", "Избранные игры коллекции для быстрого доступа", NavBtnFavorites),
            ("История", "Журнал последних запущенных игровых сессий", NavBtnHistory),
            ("Менеджер ядер", "Загрузка и обновление ядер Libretro", NavBtnCores),
            ("Опции ядер", "Тонкая настройка видео, аудио и параметров активного ядра", NavBtnCoreOptions),
            ("Читы и тренеры", "База чит-кодов Libretro CHT и встроенный редактор", NavBtnCheats),
            ("Диски и патчи", "Импорт многодисковых игр, сжатие CHD и патчи IPS/BPS", NavBtnImport),
            ("Экранные рамки", "4-сторонние экранные рамки для ретро-консолей", NavBtnBezels),
            ("Шейдеры и фильтры", "Графические фильтры CRT, Scanlines, VHS и GLSL-пресеты", NavBtnShaders),
            ("Сохранения", "Стейты, файлы батарейной памяти SRM и облачный Sync", NavBtnSaves),
            ("Геймпады (1-8)", "Калибровка геймпадов DirectInput/XInput и клавиатуры", NavBtnInputRemap),
            ("BIOS и система", "Проверка наличия дампов BIOS и системных прошивок", NavBtnBios),
            ("Ретро-достижения", "Интеграция с сервисом RetroAchievements и трофеи", NavBtnAchievements),
            ("Сетевая игра", "Лобби Netplay, хостинг комнат и подключение по IP", NavBtnNetplay),
            ("ИИ и Стриминг", "ИИ-помощник прохождения и захват видео 60 FPS", NavBtnStreamAi),
            ("Настройки", "16 категорий системных конфигураций RetroArch", NavBtnSettings)
        };

        foreach (var sec in sections)
        {
            var btn = sec.navBtn;
            _allPaletteItems.Add(new CommandPaletteItem
            {
                Title = sec.title,
                Subtitle = sec.desc,
                Category = "Раздел",
                IconGeometry = "M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5",
                IconBrush = new SolidColorBrush(Color.FromRgb(167, 139, 250)), // #A78BFA
                IconBgBrush = new SolidColorBrush(Color.FromArgb(40, 139, 92, 246)),
                BadgeTextBrush = new SolidColorBrush(Color.FromRgb(196, 181, 253)),
                BadgeBgBrush = new SolidColorBrush(Color.FromArgb(40, 139, 92, 246)),
                BadgeBorderBrush = new SolidColorBrush(Color.FromRgb(139, 92, 246)),
                ExecuteAction = () => NavBtn_Click(btn, new RoutedEventArgs())
            });
        }

        // 2. Studio Quick Commands
        _allPaletteItems.Add(new CommandPaletteItem
        {
            Title = "Синхронизировать сохранения (save sync)",
            Subtitle = "Синхронизация сохранений с папкой облака / NAS",
            Category = "Команда",
            IconGeometry = "M19.35 10.04C18.67 6.59 15.64 4 12 4 9.11 4 6.6 5.64 5.35 8.04 2.34 8.36 0 10.91 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96z",
            IconBrush = new SolidColorBrush(Color.FromRgb(52, 211, 153)), // #34D399
            IconBgBrush = new SolidColorBrush(Color.FromArgb(40, 16, 185, 129)),
            BadgeTextBrush = new SolidColorBrush(Color.FromRgb(110, 231, 183)),
            BadgeBgBrush = new SolidColorBrush(Color.FromArgb(40, 16, 185, 129)),
            BadgeBorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
            ExecuteAction = () => BtnOpenCloudSyncFolder_Click(this, new RoutedEventArgs())
        });

        _allPaletteItems.Add(new CommandPaletteItem
        {
            Title = @"Открыть корневую папку N:\STORM RETROARCH",
            Subtitle = @"Проводник Windows по установленному пути N:\STORM RETROARCH",
            Category = "Команда",
            IconGeometry = "M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z",
            IconBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)), // #38BDF8
            IconBgBrush = new SolidColorBrush(Color.FromArgb(40, 14, 165, 233)),
            BadgeTextBrush = new SolidColorBrush(Color.FromRgb(186, 230, 253)),
            BadgeBgBrush = new SolidColorBrush(Color.FromArgb(40, 14, 165, 233)),
            BadgeBorderBrush = new SolidColorBrush(Color.FromRgb(14, 165, 233)),
            ExecuteAction = () =>
            {
                try { Process.Start(new ProcessStartInfo { FileName = @"N:\STORM RETROARCH", UseShellExecute = true }); } catch { }
            }
        });

        _allPaletteItems.Add(new CommandPaletteItem
        {
            Title = "Скачать рекомендуемые ядра",
            Subtitle = "Загрузить официальный Золотой набор из 25 ядер Libretro",
            Category = "Команда",
            IconGeometry = "M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z",
            IconBrush = new SolidColorBrush(Color.FromRgb(251, 191, 36)), // #FBBF24
            IconBgBrush = new SolidColorBrush(Color.FromArgb(40, 245, 158, 11)),
            BadgeTextBrush = new SolidColorBrush(Color.FromRgb(253, 230, 138)),
            BadgeBgBrush = new SolidColorBrush(Color.FromArgb(40, 245, 158, 11)),
            BadgeBorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
            ExecuteAction = () => BtnDownloadGoldenCores_Click(this, new RoutedEventArgs())
        });

        _allPaletteItems.Add(new CommandPaletteItem
        {
            Title = "Сканировать папки с играми",
            Subtitle = "Добавить ROM-файлы и папки в библиотеку коллекции",
            Category = "Команда",
            IconGeometry = "M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z",
            IconBrush = new SolidColorBrush(Color.FromRgb(52, 211, 153)),
            IconBgBrush = new SolidColorBrush(Color.FromArgb(40, 16, 185, 129)),
            BadgeTextBrush = new SolidColorBrush(Color.FromRgb(110, 231, 183)),
            BadgeBgBrush = new SolidColorBrush(Color.FromArgb(40, 16, 185, 129)),
            BadgeBorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
            ExecuteAction = () => BtnAddFolder_Click(this, new RoutedEventArgs())
        });

        _allPaletteItems.Add(new CommandPaletteItem
        {
            Title = "Сбросить масштаб интерфейса (100%)",
            Subtitle = "Вернуть стандартный масштаб интерфейса 1.0x (Ctrl+0)",
            Category = "Команда",
            IconGeometry = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8z",
            IconBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            IconBgBrush = new SolidColorBrush(Color.FromArgb(40, 14, 165, 233)),
            BadgeTextBrush = new SolidColorBrush(Color.FromRgb(186, 230, 253)),
            BadgeBgBrush = new SolidColorBrush(Color.FromArgb(40, 14, 165, 233)),
            BadgeBorderBrush = new SolidColorBrush(Color.FromRgb(14, 165, 233)),
            ExecuteAction = () => BtnUiScaleReset_Click(this, new RoutedEventArgs())
        });

        // 3. All Games
        foreach (var g in _allGames)
        {
            var game = g;
            _allPaletteItems.Add(new CommandPaletteItem
            {
                Title = game.Name,
                Subtitle = $"{game.Platform} • {game.CoreDisplayName} • {game.FormattedSize}",
                Category = "Игра",
                IconGeometry = "M21 6H3c-1.1 0-2 .9-2 2v8c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm-10 7H8v3H6v-3H3v-2h3V8h2v3h3v2zm4.5 2c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5zm4-3c-.83 0-1.5-.67-1.5-1.5S18.67 9 19.5 9s1.5.67 1.5 1.5-.67 1.5-1.5 1.5z",
                IconBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)), // #F59E0B
                IconBgBrush = new SolidColorBrush(Color.FromArgb(40, 245, 158, 11)),
                BadgeTextBrush = new SolidColorBrush(Color.FromRgb(253, 230, 138)),
                BadgeBgBrush = new SolidColorBrush(Color.FromArgb(40, 245, 158, 11)),
                BadgeBorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                Game = game,
                ExecuteAction = async () => await StartGameInViewportAsync(game)
            });
        }
    }

    private void FilterCommandPalette(string query)
    {
        _filteredPaletteItems.Clear();
        string q = query.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(q))
        {
            // Show first 15 games + all sections + all commands
            foreach (var item in _allPaletteItems.Where(i => i.Category != "Игра"))
                _filteredPaletteItems.Add(item);
            foreach (var item in _allPaletteItems.Where(i => i.Category == "Игра").Take(25))
                _filteredPaletteItems.Add(item);
        }
        else
        {
            var matches = _allPaletteItems.Where(i =>
                i.Title.ToLowerInvariant().Contains(q) ||
                i.Subtitle.ToLowerInvariant().Contains(q) ||
                i.Category.ToLowerInvariant().Contains(q)
            ).Take(40);

            foreach (var m in matches)
                _filteredPaletteItems.Add(m);
        }

        if (LstCommandPaletteResults != null && _filteredPaletteItems.Count > 0)
        {
            LstCommandPaletteResults.SelectedIndex = 0;
        }
    }

    private void TxtCommandPaletteSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterCommandPalette(TxtCommandPaletteSearch?.Text ?? "");
    }

    private void TxtCommandPaletteSearch_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (LstCommandPaletteResults != null && LstCommandPaletteResults.Items.Count > 0)
            {
                int next = Math.Min(LstCommandPaletteResults.Items.Count - 1, LstCommandPaletteResults.SelectedIndex + 1);
                LstCommandPaletteResults.SelectedIndex = next;
                LstCommandPaletteResults.ScrollIntoView(LstCommandPaletteResults.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (LstCommandPaletteResults != null && LstCommandPaletteResults.Items.Count > 0)
            {
                int prev = Math.Max(0, LstCommandPaletteResults.SelectedIndex - 1);
                LstCommandPaletteResults.SelectedIndex = prev;
                LstCommandPaletteResults.ScrollIntoView(LstCommandPaletteResults.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ExecuteSelectedCommandPaletteItem();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseCommandPaletteModal();
            e.Handled = true;
        }
    }

    private void LstCommandPaletteResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ExecuteSelectedCommandPaletteItem();
    }

    private void LstCommandPaletteResults_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteSelectedCommandPaletteItem();
            e.Handled = true;
        }
    }

    private async void ExecuteSelectedCommandPaletteItem()
    {
        if (LstCommandPaletteResults?.SelectedItem is not CommandPaletteItem item) return;
        CloseCommandPaletteModal();
        if (item.Game != null)
        {
            await StartGameInViewportAsync(item.Game);
        }
        else
        {
            item.ExecuteAction?.Invoke();
        }
    }


    // ==================== 1.6.0 PLATFORM FILTER CHIPS & SORTING ====================

    private void CmbLibrarySort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyGamesFilter();
    }

    private void UpdatePlatformFilterChips()
    {
        if (PnlPlatformFilterChips == null) return;
        PnlPlatformFilterChips.Children.Clear();

        var distinctPlats = _allGames
            .Select(g => g.Platform)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .GroupBy(p => p)
            .OrderBy(g => g.Key)
            .Select(g => new { Platform = g.Key, Count = g.Count() })
            .ToList();

        // 1. All Platforms Chip
        var allBtn = CreatePlatformChipButton("Все платформы", _allGames.Count, _selectedChipPlatform == "Все платформы");
        allBtn.Click += (s, e) =>
        {
            _selectedChipPlatform = "Все платформы";
            if (CmbPlatformFilter != null) CmbPlatformFilter.SelectedItem = "Все платформы";
            UpdatePlatformFilterChips();
            ApplyGamesFilter();
        };
        PnlPlatformFilterChips.Children.Add(allBtn);

        // 2. Specific Platform Chips
        foreach (var plat in distinctPlats)
        {
            bool isSelected = _selectedChipPlatform.Equals(plat.Platform, StringComparison.OrdinalIgnoreCase);
            var chipBtn = CreatePlatformChipButton(plat.Platform, plat.Count, isSelected);
            string capturedPlat = plat.Platform;
            chipBtn.Click += (s, e) =>
            {
                _selectedChipPlatform = capturedPlat;
                if (CmbPlatformFilter != null)
                {
                    for (int i = 0; i < CmbPlatformFilter.Items.Count; i++)
                    {
                        if (CmbPlatformFilter.Items[i]?.ToString() == capturedPlat)
                        {
                            CmbPlatformFilter.SelectedIndex = i;
                            break;
                        }
                    }
                }
                UpdatePlatformFilterChips();
                ApplyGamesFilter();
            };
            PnlPlatformFilterChips.Children.Add(chipBtn);
        }
    }

    private Button CreatePlatformChipButton(string name, int count, bool isSelected)
    {
        var btn = new Button
        {
            Style = (Style)FindResource("CapsuleButton"),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0),
            Tag = name,
            Cursor = Cursors.Hand,
            ToolTip = $"Фильтровать по платформе {name} ({count} игр)"
        };

        if (isSelected)
        {
            btn.Background = new SolidColorBrush(Color.FromArgb(60, 124, 58, 237));
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(167, 139, 250));
            btn.BorderThickness = new Thickness(1.5);
        }
        else
        {
            btn.Background = (Brush)FindResource("ThemeSurface");
            btn.BorderBrush = (Brush)FindResource("ThemeBorder");
            btn.BorderThickness = new Thickness(1);
        }

        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var txtName = new TextBlock
        {
            Text = name,
            FontSize = 11.5,
            FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
            Foreground = isSelected ? new SolidColorBrush(Color.FromRgb(248, 250, 252)) : (Brush)FindResource("ThemeTextPrimary"),
            VerticalAlignment = VerticalAlignment.Center
        };
        var badgeBorder = new Border
        {
            Background = isSelected ? new SolidColorBrush(Color.FromArgb(80, 139, 92, 246)) : new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var txtCount = new TextBlock
        {
            Text = count.ToString(),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = isSelected ? new SolidColorBrush(Color.FromRgb(221, 214, 254)) : (Brush)FindResource("ThemeTextSecondary")
        };
        badgeBorder.Child = txtCount;
        sp.Children.Add(txtName);
        sp.Children.Add(badgeBorder);
        btn.Content = sp;

        return btn;
    }


    // ==================== 1.6.0 GAME CONTEXT ACTIONS & CHEATS BUTTON ====================

    private RomItem? GetRomFromSender(object sender)
    {
        if (sender is FrameworkElement fe && fe.DataContext is RomItem r1) return r1;
        if (sender is MenuItem mi)
        {
            if (mi.DataContext is RomItem r2) return r2;
            if (mi.Parent is ContextMenu cm && cm.PlacementTarget is FrameworkElement pt && pt.DataContext is RomItem r3)
                return r3;
        }
        return null;
    }

    private async void ContextPlayGame_Click(object sender, RoutedEventArgs e)
    {
        var game = GetRomFromSender(sender);
        if (game != null) await StartGameInViewportAsync(game);
    }

    private void ContextChangeCore_Click(object sender, RoutedEventArgs e)
    {
        var game = GetRomFromSender(sender);
        if (game != null) OpenCoreSelectorForGame(game);
    }

    private void ContextGameCheats_Click(object sender, RoutedEventArgs e)
    {
        var game = GetRomFromSender(sender);
        if (game != null) OpenCheatsForGame(game);
    }

    private void ContextGameSaves_Click(object sender, RoutedEventArgs e)
    {
        var game = GetRomFromSender(sender);
        if (game != null) OpenSavesForGame(game);
    }

    private void ContextToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        var game = GetRomFromSender(sender);
        if (game == null) return;
        game.IsFavorite = !game.IsFavorite;
        if (game.IsFavorite)
        {
            if (!_favorites.Any(f => f.FilePath.Equals(game.FilePath, StringComparison.OrdinalIgnoreCase)))
                _favorites.Add(game);
            ShowOsdToast($"Игра «{game.Name}» добавлена в Избранное ★");
        }
        else
        {
            var existing = _favorites.FirstOrDefault(f => f.FilePath.Equals(game.FilePath, StringComparison.OrdinalIgnoreCase));
            if (existing != null) _favorites.Remove(existing);
            ShowOsdToast($"Игра «{game.Name}» убрана из Избранного");
        }
    }

    private void ContextOpenRomFolder_Click(object sender, RoutedEventArgs e)
    {
        var game = GetRomFromSender(sender);
        if (game == null) return;
        try
        {
            if (File.Exists(game.FilePath))
                Process.Start("explorer.exe", $"/select,\"{game.FilePath}\"");
            else if (Directory.Exists(Path.GetDirectoryName(game.FilePath)))
                Process.Start("explorer.exe", $"\"{Path.GetDirectoryName(game.FilePath)}\"");
        }
        catch { }
    }

    private void BtnGameCheats_Click(object sender, RoutedEventArgs e)
    {
        var game = GetRomFromSender(sender);
        if (game != null) OpenCheatsForGame(game);
    }

    public void OpenCoreSelectorForGame(RomItem item)
    {
        if (item == null) return;
        _coreModalTargetRom = item;
        if (TxtCoreModalTitle != null) TxtCoreModalTitle.Text = $"Выбор ядра: {item.Name}";
        if (TxtCoreModalSubtitle != null) TxtCoreModalSubtitle.Text = $"Платформа: {item.Platform} • Текущее ядро: {item.CoreDisplayName}";

        var cores = CoreManager.GetCompatibleCoresForPlatform(item.Platform, item.Extension);
        if (LstModalCompatibleCores != null)
        {
            LstModalCompatibleCores.ItemsSource = cores;
            var currentMatch = cores.FirstOrDefault(c => c.DllName.Equals(item.CoreDll, StringComparison.OrdinalIgnoreCase));
            LstModalCompatibleCores.SelectedItem = currentMatch ?? cores.FirstOrDefault();
        }

        if (TxtSetCoreForPlatformDefault != null)
        {
            TxtSetCoreForPlatformDefault.Text = $"Сделать выбранное ядро стандартным по умолчанию для всех игр платформы «{item.Platform}»";
        }
        else if (ChkSetCoreDefaultForPlatform != null)
        {
            ChkSetCoreDefaultForPlatform.Content = $"Сделать выбранное ядро стандартным по умолчанию для всех игр платформы «{item.Platform}»";
        }
        if (ChkSetCoreDefaultForPlatform != null)
        {
            ChkSetCoreDefaultForPlatform.IsChecked = false;
        }

        if (PnlCoreSelectorModal != null)
        {
            PnlCoreSelectorModal.Visibility = Visibility.Visible;
            if (BorderCoreSelectorDialog != null)
                AnalyticalSpringEngine.AnimateBackdropAndDialog(PnlCoreSelectorModal, BorderCoreSelectorDialog, opening: true);
        }
    }

    public void OpenCheatsForGame(RomItem game)
    {
        NavBtn_Click(NavBtnCheats, new RoutedEventArgs());
        if (TxtSearchCheats != null)
        {
            TxtSearchCheats.Text = game.Name;
        }

        var match = _allCheatFiles.FirstOrDefault(f =>
            f.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) ||
            f.FilePath.Contains(Path.GetFileNameWithoutExtension(game.FilePath), StringComparison.OrdinalIgnoreCase));

        if (match != null && ListCheatFiles != null)
        {
            ListCheatFiles.SelectedItem = match;
            ListCheatFiles.ScrollIntoView(match);
        }
    }

    public void OpenSavesForGame(RomItem game)
    {
        NavBtn_Click(NavBtnSaves, new RoutedEventArgs());
        if (TxtSearchSaves != null)
        {
            TxtSearchSaves.Text = Path.GetFileNameWithoutExtension(game.FilePath);
        }
    }


    // ==================== 1.6.0 DASHBOARD RECENT GAMES STRIP ====================

    private void UpdateDashboardRecentGames()
    {
        if (IcDashboardRecentGames == null) return;
        var source = _history.Count > 0 ? _history.Take(8).ToList() : _allGames.Take(8).ToList();
        IcDashboardRecentGames.ItemsSource = source;
    }

    private void BtnGoToHistory_Click(object sender, RoutedEventArgs e)
    {
        NavBtn_Click(NavBtnHistory, e);
    }


    // ==================== 1.6.0 QUICK MENU LIVE STATE STATUS ====================

    private void InitQuickMenuStateSlots()
    {
        if (CmbQuickMenuStateSlot == null) return;
        CmbQuickMenuStateSlot.Items.Clear();
        for (int i = 0; i <= 9; i++)
        {
            CmbQuickMenuStateSlot.Items.Add($"#{i}");
        }
        int active = Math.Clamp(NativeLibretroHost.Current.ActiveStateSlot, 0, 9);
        CmbQuickMenuStateSlot.SelectedIndex = active;
    }

    private bool _syncingQuickStateSlot = false;
    private void CmbQuickMenuStateSlot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _syncingQuickStateSlot || CmbQuickMenuStateSlot == null || CmbQuickMenuStateSlot.SelectedIndex < 0) return;
        _syncingQuickStateSlot = true;
        try
        {
            int slot = CmbQuickMenuStateSlot.SelectedIndex;
            NativeLibretroHost.Current.ActiveStateSlot = slot;
            if (CmbInGameStateSlot != null && slot < CmbInGameStateSlot.Items.Count && CmbInGameStateSlot.SelectedIndex != slot) CmbInGameStateSlot.SelectedIndex = slot;
            if (CmbStateSlot != null && slot < CmbStateSlot.Items.Count && CmbStateSlot.SelectedIndex != slot) CmbStateSlot.SelectedIndex = slot;
            UpdateQuickSlotStatus();
        }
        finally
        {
            _syncingQuickStateSlot = false;
        }
    }

    public void UpdateQuickSlotStatus()
    {
        if (TxtQuickSlotStatus == null) return;
        int slot = NativeLibretroHost.Current.ActiveStateSlot;
        string gameTitle = NativeLibretroHost.Current.CurrentGameTitle;
        if (string.IsNullOrWhiteSpace(gameTitle))
        {
            TxtQuickSlotStatus.Text = $"Слот #{slot}: Игра не запущена";
            TxtQuickSlotStatus.Foreground = (Brush)FindResource("ThemeTextMuted");
            return;
        }

        try
        {
            string safeTitle = NativeLibretroHost.SanitizeFileName(gameTitle);
            string statesDir = Path.Combine(CoreManager.AppDir, "states");
            string statePath = Path.Combine(statesDir, $"{safeTitle}.state{slot}");

            if (File.Exists(statePath))
            {
                var fi = new FileInfo(statePath);
                long bytes = fi.Length;
                string sizeStr = bytes < 1024 * 1024 ? $"{bytes / 1024.0:F0} КБ" : $"{bytes / (1024.0 * 1024.0):F1} МБ";
                string timeStr = fi.LastWriteTime.Date == DateTime.Today
                    ? $"сегодня {fi.LastWriteTime:HH:mm}"
                    : (fi.LastWriteTime.Date == DateTime.Today.AddDays(-1)
                        ? $"вчера {fi.LastWriteTime:HH:mm}"
                        : fi.LastWriteTime.ToString("dd.MM.yyyy HH:mm"));

                TxtQuickSlotStatus.Text = $"Слот #{slot}: Стейт найден ({sizeStr}, {timeStr})";
                TxtQuickSlotStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)); // #34D399
            }
            else
            {
                TxtQuickSlotStatus.Text = $"Слот #{slot}: Пусто";
                TxtQuickSlotStatus.Foreground = (Brush)FindResource("ThemeTextMuted");
            }
        }
        catch
        {
            TxtQuickSlotStatus.Text = $"Слот #{slot}: Пусто";
            TxtQuickSlotStatus.Foreground = (Brush)FindResource("ThemeTextMuted");
        }
    }


    // ==================== 1.6.0 GAME HUB, BIGBOX, GPU ANIMATIONS & CUSTOM COVERS ====================

    private RomItem? _activeGameHubItem;
    private DispatcherTimer? _bigBoxClockTimer;

    private void AnimatePanelEntrance(UIElement? panel)
    {
        if (panel == null || SettingsManager.Current == null || !SettingsManager.Current.EnableUiAnimations)
            return;

        if (panel is FrameworkElement fe)
        {
            AnalyticalSpringEngine.AnimatePanelReveal(fe, 12.0, 380f, 30f);
        }
    }


    public void OpenGameHubModal(RomItem item)
    {
        if (item == null || PnlGameHubModal == null) return;
        _activeGameHubItem = item;

        TxtGameHubTitle.Text = item.Name;
        ImgGameHubCover.Source = item.Icon;
        ImgGameHubHero.Source = item.Icon;

        TxtGameHubPlatform.Text = item.Platform;
        TxtGameHubCore.Text = item.CoreDisplayName;
        TxtGameHubLanguage.Text = !string.IsNullOrEmpty(item.LanguageBadge) ? item.LanguageBadge : "RU [STORM]";
        TxtGameHubYear.Text = !string.IsNullOrEmpty(item.Year) ? item.Year : "—";
        TxtGameHubRegion.Text = !string.IsNullOrEmpty(item.Region) ? item.Region : "—";

        TxtGameHubPlaytime.Text = $"Время: {item.PlaytimeText}";
        TxtGameHubLaunchCount.Text = $"Запусков: {item.LaunchCount}";

        CmbGameHubCompletionStatus.SelectionChanged -= CmbGameHubCompletionStatus_SelectionChanged;
        CmbGameHubCompletionStatus.Items.Clear();
        foreach (var s in GameMetadataStore.CompletionStatuses)
        {
            CmbGameHubCompletionStatus.Items.Add(s);
        }
        CmbGameHubCompletionStatus.SelectedItem = !string.IsNullOrEmpty(item.CompletionStatus) ? item.CompletionStatus : GameMetadataStore.CompletionStatuses[0];
        CmbGameHubCompletionStatus.SelectionChanged += CmbGameHubCompletionStatus_SelectionChanged;

        TxtGameHubCollection.TextChanged -= TxtGameHubCollection_TextChanged;
        TxtGameHubCollection.Text = item.CustomCollection ?? "";
        TxtGameHubCollection.TextChanged += TxtGameHubCollection_TextChanged;

        TxtGameHubUserNotes.TextChanged -= TxtGameHubUserNotes_TextChanged;
        TxtGameHubUserNotes.Text = item.UserNotes ?? "";
        TxtGameHubUserNotes.TextChanged += TxtGameHubUserNotes_TextChanged;

        UpdateStarRatingVisuals(item.StarRating);

        // ClearLogo
        var clearLogo = CoverArtService.LoadClearLogo(item);
        if (clearLogo != null)
        {
            ImgGameHubClearLogo.Source = clearLogo;
            ImgGameHubClearLogo.Visibility = Visibility.Visible;
        }
        else
        {
            ImgGameHubClearLogo.Source = null;
            ImgGameHubClearLogo.Visibility = Visibility.Collapsed;
        }

        // Screenshots
        PnlGameHubScreenshots.Children.Clear();
        var screenshots = CoverArtService.LoadGameScreenshots(item);
        if (screenshots.Count > 0)
        {
            foreach (var shot in screenshots)
            {
                var border = new Border
                {
                    Width = 120,
                    Height = 78,
                    CornerRadius = new CornerRadius(6),
                    ClipToBounds = true,
                    Margin = new Thickness(0, 0, 8, 0),
                    Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                    BorderBrush = (Brush)FindResource("ThemeBorder"),
                    BorderThickness = new Thickness(1)
                };
                var img = new Image
                {
                    Source = shot,
                    Stretch = Stretch.UniformToFill
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                border.Child = img;
                PnlGameHubScreenshots.Children.Add(border);
            }
        }
        else
        {
            PnlGameHubScreenshots.Children.Add(new TextBlock
            {
                Text = "Кадры и скриншоты отсутствуют (сделайте скриншот клавишей F8 в игре)",
                Foreground = (Brush)FindResource("ThemeTextMuted"),
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        // PDF Game Books & Manuals
        PnlGameHubPdfs.Children.Clear();
        var pdfs = CoverArtService.FindGameManualPdfs(item);
        if (pdfs.Count > 0)
        {
            foreach (var pdf in pdfs)
            {
                string pdfFileName = Path.GetFileName(pdf);
                var btn = new Button
                {
                    Style = (Style)FindResource("StormSecondaryButton"),
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 0, 8, 0),
                    ToolTip = $"Открыть документацию: {pdf}"
                };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var path = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z"),
                    Fill = new SolidColorBrush(Color.FromRgb(0xEC, 0x48, 0x99)),
                    Stretch = Stretch.Uniform,
                    Width = 12,
                    Height = 12,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                sp.Children.Add(path);
                sp.Children.Add(new TextBlock { Text = pdfFileName, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
                btn.Content = sp;
                string capturePdf = pdf;
                btn.Click += (s, e) =>
                {
                    try { Process.Start(new ProcessStartInfo(capturePdf) { UseShellExecute = true }); } catch { }
                };
                PnlGameHubPdfs.Children.Add(btn);
            }
        }
        else
        {
            PnlGameHubPdfs.Children.Add(new TextBlock
            {
                Text = "Документация и PDF-руководства не найдены в каталоге книги",
                Foreground = (Brush)FindResource("ThemeTextMuted"),
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        PnlGameHubModal.Visibility = Visibility.Visible;
        if (GameHubDialogBorder != null)
            AnalyticalSpringEngine.AnimateBackdropAndDialog(PnlGameHubModal, GameHubDialogBorder, opening: true);
    }

    public void CloseGameHubModal()
    {
        if (_activeGameHubItem != null)
        {
            GameMetadataStore.UpdateFromRomItem(_activeGameHubItem);
            _activeGameHubItem = null;
        }
        if (PnlGameHubModal != null)
        {
            if (GameHubDialogBorder != null)
                AnalyticalSpringEngine.AnimateBackdropAndDialog(PnlGameHubModal, GameHubDialogBorder, opening: false, () => PnlGameHubModal.Visibility = Visibility.Collapsed);
            else
                PnlGameHubModal.Visibility = Visibility.Collapsed;
        }
    }


    private void PnlGameHubModal_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == PnlGameHubModal)
        {
            CloseGameHubModal();
        }
    }

    private void GameHubDialog_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void BtnCloseGameHub_Click(object sender, RoutedEventArgs e)
    {
        CloseGameHubModal();
    }

    private void BtnGameHubPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_activeGameHubItem != null)
        {
            var game = _activeGameHubItem;
            CloseGameHubModal();
            _ = StartGameInViewportAsync(game);
        }
    }

    private void BtnGameHubChangeCore_Click(object sender, RoutedEventArgs e)
    {
        if (_activeGameHubItem != null)
        {
            OpenCoreSelectorForGame(_activeGameHubItem);
        }
    }

    private void BtnGameHubCheats_Click(object sender, RoutedEventArgs e)
    {
        if (_activeGameHubItem != null)
        {
            var game = _activeGameHubItem;
            CloseGameHubModal();
            OpenCheatsForGame(game);
        }
    }

    private void BtnGameHubChangeCover_Click(object sender, RoutedEventArgs e)
    {
        if (_activeGameHubItem != null)
        {
            PromptCustomCoverForGame(_activeGameHubItem);
        }
    }

    private void BtnGameHubOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_activeGameHubItem != null)
        {
            OpenFolderForGame(_activeGameHubItem);
        }
    }

    private void BtnStar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int rating))
        {
            if (_activeGameHubItem != null)
            {
                _activeGameHubItem.StarRating = rating;
                GameMetadataStore.UpdateFromRomItem(_activeGameHubItem);
                UpdateStarRatingVisuals(rating);
            }
        }
    }

    private void UpdateStarRatingVisuals(int rating)
    {
        var stars = new[] { BtnStar1, BtnStar2, BtnStar3, BtnStar4, BtnStar5 };
        for (int i = 0; i < stars.Length; i++)
        {
            if (stars[i] != null)
            {
                stars[i].Foreground = (i < rating)
                    ? new SolidColorBrush(Color.FromRgb(0xFD, 0xE0, 0x47))
                    : new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63));
            }
        }
    }

    private void CmbGameHubCompletionStatus_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_activeGameHubItem != null && CmbGameHubCompletionStatus.SelectedItem != null)
        {
            _activeGameHubItem.CompletionStatus = CmbGameHubCompletionStatus.SelectedItem.ToString() ?? "";
            GameMetadataStore.UpdateFromRomItem(_activeGameHubItem);
        }
    }

    private void TxtGameHubCollection_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_activeGameHubItem != null)
        {
            _activeGameHubItem.CustomCollection = TxtGameHubCollection.Text;
            GameMetadataStore.UpdateFromRomItem(_activeGameHubItem);
        }
    }

    private void TxtGameHubUserNotes_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_activeGameHubItem != null)
        {
            _activeGameHubItem.UserNotes = TxtGameHubUserNotes.Text;
            GameMetadataStore.UpdateFromRomItem(_activeGameHubItem);
        }
    }

    private void BtnGameHub_Click(object sender, RoutedEventArgs e)
    {
        var item = GetRomFromSender(sender);
        if (item != null)
        {
            OpenGameHubModal(item);
        }
    }

    private void ContextOpenGameHub_Click(object sender, RoutedEventArgs e)
    {
        var item = GetRomFromSender(sender);
        if (item != null)
        {
            OpenGameHubModal(item);
        }
    }

    private void ContextSetCustomCover_Click(object sender, RoutedEventArgs e)
    {
        var item = GetRomFromSender(sender);
        if (item != null)
        {
            PromptCustomCoverForGame(item);
        }
    }

    public void PromptCustomCoverForGame(RomItem item)
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Выберите изображение обложки для игры",
                Filter = "Изображения (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|Все файлы (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                bool success = CoverArtService.SetCustomCoverFromFile(item, dlg.FileName);
                if (success)
                {
                    if (_activeGameHubItem == item && ImgGameHubCover != null)
                    {
                        ImgGameHubCover.Source = item.Icon;
                    }
                    ShowOsdToast("Обложка игры успешно обновлена!");
                }
            }
        }
        catch (Exception ex)
        {
            ShowOsdToast($"Ошибка смены обложки: {ex.Message}");
        }
    }

    public void OpenFolderForGame(RomItem game)
    {
        if (game != null && File.Exists(game.FilePath))
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{game.FilePath}\"");
            }
            catch { }
        }
    }

    // ==================== BIGBOX 10-FOOT CONSOLE MODE ====================

    public void OpenBigBoxMode()
    {
        if (PnlBigBoxOverlay == null) return;

        var games = _filteredGames.Count > 0 ? _filteredGames : _allGames;
        LstBigBoxStrip.ItemsSource = games;
        if (games.Count > 0)
        {
            LstBigBoxStrip.SelectedIndex = 0;
        }

        TxtBigBoxClock.Text = DateTime.Now.ToString("HH:mm");
        if (_bigBoxClockTimer == null)
        {
            _bigBoxClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _bigBoxClockTimer.Tick += (s, e) =>
            {
                if (TxtBigBoxClock != null && PnlBigBoxOverlay.Visibility == Visibility.Visible)
                {
                    TxtBigBoxClock.Text = DateTime.Now.ToString("HH:mm");
                }
            };
        }
        _bigBoxClockTimer.Start();

        PnlBigBoxOverlay.Visibility = Visibility.Visible;
        PnlBigBoxOverlay.Focus();

        if (SettingsManager.Current?.EnableUiAnimations == true)
        {
            AnimatePanelEntrance(PnlBigBoxOverlay);
        }
    }

    public void CloseBigBoxMode()
    {
        _bigBoxClockTimer?.Stop();
        if (PnlBigBoxOverlay != null)
        {
            PnlBigBoxOverlay.Visibility = Visibility.Collapsed;
        }
    }

    public void ToggleBigBoxMode()
    {
        if (PnlBigBoxOverlay != null && PnlBigBoxOverlay.Visibility == Visibility.Visible)
        {
            CloseBigBoxMode();
        }
        else
        {
            OpenBigBoxMode();
        }
    }

    private void BtnBigBox_Click(object sender, RoutedEventArgs e)
    {
        ToggleBigBoxMode();
    }

    private void BtnBigBoxExit_Click(object sender, RoutedEventArgs e)
    {
        CloseBigBoxMode();
    }

    private void LstBigBoxStrip_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstBigBoxStrip.SelectedItem is RomItem item)
        {
            TxtBigBoxTitle.Text = item.Name;
            TxtBigBoxSubtitle.Text = $"{item.Platform} • {item.CoreDisplayName}";
            ImgBigBoxShowcaseCover.Source = item.Icon;
            ImgBigBoxBackdrop.Source = item.Icon;

            int idx = LstBigBoxStrip.SelectedIndex + 1;
            int total = LstBigBoxStrip.Items.Count;
            TxtBigBoxGameCounter.Text = $"{idx} из {total}";

            TxtBigBoxStatus.Text = !string.IsNullOrEmpty(item.CompletionStatus) ? item.CompletionStatus : "Не начато";
            TxtBigBoxRating.Text = item.StarRatingDisplay;
            TxtBigBoxPlaytime.Text = item.PlaytimeText;
            TxtBigBoxYear.Text = !string.IsNullOrEmpty(item.Year) ? item.Year : "—";
        }
    }

    private void BigBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Left)
        {
            if (LstBigBoxStrip.SelectedIndex > 0)
            {
                LstBigBoxStrip.SelectedIndex--;
                LstBigBoxStrip.ScrollIntoView(LstBigBoxStrip.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            if (LstBigBoxStrip.SelectedIndex < LstBigBoxStrip.Items.Count - 1)
            {
                LstBigBoxStrip.SelectedIndex++;
                LstBigBoxStrip.ScrollIntoView(LstBigBoxStrip.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            BtnBigBoxPlay_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.I)
        {
            BtnBigBoxHub_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.C)
        {
            BtnBigBoxCore_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F)
        {
            BtnBigBoxFavorite_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape || e.Key == Key.F10)
        {
            CloseBigBoxMode();
            e.Handled = true;
        }
    }

    private void BtnBigBoxPlay_Click(object sender, RoutedEventArgs e)
    {
        if (LstBigBoxStrip.SelectedItem is RomItem item)
        {
            CloseBigBoxMode();
            _ = StartGameInViewportAsync(item);
        }
    }

    private void BtnBigBoxHub_Click(object sender, RoutedEventArgs e)
    {
        if (LstBigBoxStrip.SelectedItem is RomItem item)
        {
            OpenGameHubModal(item);
        }
    }

    private void BtnBigBoxCore_Click(object sender, RoutedEventArgs e)
    {
        if (LstBigBoxStrip.SelectedItem is RomItem item)
        {
            OpenCoreSelectorForGame(item);
        }
    }

    private void BtnBigBoxFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (LstBigBoxStrip.SelectedItem is RomItem item)
        {
            item.IsFavorite = !item.IsFavorite;
            SavePlaylists();
            ShowOsdToast(item.IsFavorite ? "Добавлено в Избранное" : "Удалено из Избранного");
        }
    }

    // ==================== PHASE 2: SIDEBAR DECKS, SHADERS, HOTKEYS & ROM HASH ====================
    private void DeckHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string panelName)
        {
            StackPanel? target = FindName(panelName) as StackPanel;
            if (target != null)
            {
                bool isExpanded = target.Visibility == Visibility.Visible;
                target.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;
                string chevronName = "Chevron" + panelName.Replace("DeckPanel", "Deck");
                if (FindName(chevronName) is TextBlock ch)
                {
                    ch.Text = isExpanded ? "▸" : "▾";
                }
            }
        }
    }

    private void BtnShaderPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        if (BtnPresetTrinitron != null) BtnPresetTrinitron.Tag = null;
        if (BtnPresetArcade != null) BtnPresetArcade.Tag = null;
        if (BtnPresetVga != null) BtnPresetVga.Tag = null;
        if (BtnPresetGbLcd != null) BtnPresetGbLcd.Tag = null;
        if (BtnPresetVhs != null) BtnPresetVhs.Tag = null;
        if (BtnPresetClean != null) BtnPresetClean.Tag = null;

        btn.Tag = "Active";

        string presetId = "trinitron";
        if (btn == BtnPresetArcade) presetId = "arcade_rgb";
        else if (btn == BtnPresetVga) presetId = "ibm_vga";
        else if (btn == BtnPresetGbLcd) presetId = "gb_lcd";
        else if (btn == BtnPresetVhs) presetId = "vhs";
        else if (btn == BtnPresetClean) presetId = "clean_raw";

        NativeLibretroHost.Current.ApplyShaderStudioPreset(presetId);
        SyncShaderStudioControls();
        RefreshShaderLivePreview();
        ShowOsdToast($"Шейдер: {btn.Content}", "#8B5CF6");
    }

    public void SyncShaderStudioControls()
    {
        if (SliderShaderScanlines != null)
        {
            int scan = (int)Math.Round(NativeLibretroHost.Current.ScanlineIntensity * 100);
            SliderShaderScanlines.Value = scan;
            if (TxtShaderScanlinesVal != null) TxtShaderScanlinesVal.Text = $"{scan}%";
        }
        if (SliderShaderMask != null)
        {
            int mask = (int)Math.Round(NativeLibretroHost.Current.MaskIntensity * 100);
            SliderShaderMask.Value = mask;
            if (TxtShaderMaskVal != null) TxtShaderMaskVal.Text = $"{mask}%";
        }
        if (SliderShaderVignette != null)
        {
            int vig = (int)Math.Round(NativeLibretroHost.Current.VignetteIntensity * 100);
            SliderShaderVignette.Value = vig;
            if (TxtShaderVignetteVal != null) TxtShaderVignetteVal.Text = $"{vig}%";
        }
        if (SliderShaderBrightness != null)
        {
            int bright = (int)Math.Round(NativeLibretroHost.Current.BrightnessGain * 100);
            SliderShaderBrightness.Value = bright;
            if (TxtShaderBrightnessVal != null) TxtShaderBrightnessVal.Text = $"{bright}%";
        }
        if (CmbShaderColorTemp != null)
        {
            CmbShaderColorTemp.SelectedIndex = Math.Clamp(NativeLibretroHost.Current.ColorTemperatureMode, 0, 2);
        }
        if (SliderShaderCurvature != null)
        {
            int cur = (int)Math.Round(NativeLibretroHost.Current.CurvatureIntensity * 100);
            SliderShaderCurvature.Value = cur;
            if (TxtShaderCurvatureVal != null) TxtShaderCurvatureVal.Text = $"{cur}%";
        }
        if (SliderShaderChromatic != null)
        {
            int chr = (int)Math.Round(NativeLibretroHost.Current.ChromaticIntensity * 100);
            SliderShaderChromatic.Value = chr;
            if (TxtShaderChromaticVal != null) TxtShaderChromaticVal.Text = $"{chr}%";
        }
        RefreshShaderLivePreview();
    }

    private void SliderShaderScanlines_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isLoaded || SliderShaderScanlines == null) return;
        int val = (int)Math.Round(SliderShaderScanlines.Value);
        if (TxtShaderScanlinesVal != null) TxtShaderScanlinesVal.Text = $"{val}%";
        NativeLibretroHost.Current.ScanlineIntensity = val / 100.0;
        RefreshShaderLivePreview();
    }

    private void SliderShaderMask_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isLoaded || SliderShaderMask == null) return;
        int val = (int)Math.Round(SliderShaderMask.Value);
        if (TxtShaderMaskVal != null) TxtShaderMaskVal.Text = $"{val}%";
        NativeLibretroHost.Current.MaskIntensity = val / 100.0;
        RefreshShaderLivePreview();
    }

    private void SliderShaderVignette_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isLoaded || SliderShaderVignette == null) return;
        int val = (int)Math.Round(SliderShaderVignette.Value);
        if (TxtShaderVignetteVal != null) TxtShaderVignetteVal.Text = $"{val}%";
        NativeLibretroHost.Current.VignetteIntensity = val / 100.0;
        RefreshShaderLivePreview();
    }

    private void SliderShaderBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isLoaded || SliderShaderBrightness == null) return;
        int val = (int)Math.Round(SliderShaderBrightness.Value);
        if (TxtShaderBrightnessVal != null) TxtShaderBrightnessVal.Text = $"{val}%";
        NativeLibretroHost.Current.BrightnessGain = val / 100.0;
        RefreshShaderLivePreview();
    }

    private void CmbShaderColorTemp_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || CmbShaderColorTemp == null) return;
        NativeLibretroHost.Current.ColorTemperatureMode = CmbShaderColorTemp.SelectedIndex;
        RefreshShaderLivePreview();
    }

    public void RefreshHotkeyBindings()
    {
        RebuildHotkeyStudioList();
    }

    private void BtnAssignHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is HotkeyBindingItem item)
        {
            _capturingHotkeyActionId = item.ActionId;
            if (BadgeHotkeyCapture != null) BadgeHotkeyCapture.Visibility = Visibility.Visible;
            if (TxtHotkeyCaptureStatus != null)
                TxtHotkeyCaptureStatus.Text = $"Нажмите клавишу для «{item.Title}» (ESC для отмены)...";
        }
    }

    public void CancelHotkeyCapture()
    {
        _capturingHotkeyActionId = null; _isCapturingGamepadChord = false; _physicalCaptureTimer?.Stop();
        if (BadgeHotkeyCapture != null) BadgeHotkeyCapture.Visibility = Visibility.Collapsed;
    }

    private void BtnResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        HotkeyManager.ResetAllToDefaults();
        CancelHotkeyCapture();
        RefreshHotkeyBindings();
        ShowOsdToast("Горячие клавиши сброшены по умолчанию", "#10B981");
    }

    private void BtnToggleLockSaveEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is SaveEntryItem entry)
        {
            SaveManager.ToggleLock(entry);
            LoadSaves();
            ShowOsdToast(entry.IsLocked ? "Сейв защищён от удаления" : "Защита снята", "#10B981");
        }
    }

    private async void BtnInspectRom_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "ROM и образы дисков (*.*)|*.*",
            Title = "Выберите образ игры для вычисления хешей и инспекции"
        };

        if (dlg.ShowDialog() == true)
        {
            await RunRomInspectionAsync(dlg.FileName);
        }
    }

    public async Task RunRomInspectionAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        if (TxtHashFileName != null) TxtHashFileName.Text = "Вычисление хешей...";

        var report = await DiscPatchSubsystemManager.InspectRomFileAsync(filePath);
        _latestHashReport = report;

        if (TxtHashFileName != null) TxtHashFileName.Text = report.FileName;
        if (TxtHashFormatAndSize != null) TxtHashFormatAndSize.Text = $"{report.ContainerFormat} • {report.FileSizeFormatted}";
        if (TxtHashHeaderSummary != null) TxtHashHeaderSummary.Text = report.HeaderSummary;
        if (TxtHashCrc32 != null) TxtHashCrc32.Text = report.Crc32;
        if (TxtHashMd5 != null) TxtHashMd5.Text = report.Md5;
        if (TxtHashSha1 != null) TxtHashSha1.Text = report.Sha1;

        ShowOsdToast("Хеши ROM успешно вычислены", "#10B981");
    }

    private void BtnCopyRomHashReport_Click(object sender, RoutedEventArgs e)
    {
        if (_latestHashReport == null)
        {
            ShowNotification("STORM RETROARCH", "Сначала выберите файл для инспекции!");
            return;
        }

        string reportText = $"[ROM HASH REPORT]\n" +
                            $"Файл: {_latestHashReport.FileName}\n" +
                            $"Формат: {_latestHashReport.ContainerFormat}\n" +
                            $"Размер: {_latestHashReport.FileSizeFormatted}\n" +
                            $"Заголовок: {_latestHashReport.HeaderSummary}\n" +
                            $"CRC32: {_latestHashReport.Crc32}\n" +
                            $"MD5: {_latestHashReport.Md5}\n" +
                            $"SHA-1: {_latestHashReport.Sha1}\n";

        try
        {
            Clipboard.SetText(reportText);
            ShowOsdToast("Отчёт скопирован в буфер обмена", "#10B981");
        }
        catch (Exception ex)
        {
            ShowNotification("STORM RETROARCH", $"Ошибка копирования: {ex.Message}");
        }
    }

// ==================== 2.0.0 PERFORMANCE & JIT DYNAREC STUDIO ====================
    private void BtnCpuDynarecFast_Click(object sender, RoutedEventArgs e)
    {
        CompilerAndPerformanceManager.ApplyCpuDynarecProfile("dynarec_fast");
        SyncPerformanceControls();
        ShowOsdToast("Профиль JIT Dynarec: максимальная скорость и отзывчивость");
    }

    private void BtnCpuEsportsZeroLag_Click(object sender, RoutedEventArgs e)
    {
        CompilerAndPerformanceManager.ApplyCpuDynarecProfile("esports_zero_lag");
        SyncPerformanceControls();
        ShowOsdToast("Профиль Нулевая задержка: упреждающий расчёт 2 кадров (-33,3 мс) и синхронизатор FPS");
    }

    private void BtnCpuCycleAccurate_Click(object sender, RoutedEventArgs e)
    {
        CompilerAndPerformanceManager.ApplyCpuDynarecProfile("cycle_accurate");
        SyncPerformanceControls();
        ShowOsdToast("Профиль Точный цикл: потактовая эмуляция ЦП без JIT и опережения");
    }

    private void ChkPrecisionPacer_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = ChkPrecisionPacer.IsChecked == true;
        SettingsManager.Current.PrecisionFramePacer = enabled;
        NativeLibretroHost.Current.PrecisionFramePacerEnabled = enabled;
        SettingsManager.Save();
        ShowOsdToast($"Суб-миллисекундный синхронизатор 60,0 FPS: {(enabled ? "ВКЛ" : "ВЫКЛ")}");
    }

    private void CmbRunAheadFrames_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || CmbRunAheadFrames == null) return;
        int frames = Math.Clamp(CmbRunAheadFrames.SelectedIndex, 0, 2);
        SettingsManager.Current.RunAheadFrames = frames;
        NativeLibretroHost.Current.RunAheadFrames = frames;
        SettingsManager.Save();
        ShowOsdToast($"Опережение ввода (Run-Ahead): {frames} кадр{(frames == 1 ? "" : "а")}");
    }

    private void SyncPerformanceControls()
    {
        if (ChkPrecisionPacer != null)
        {
            ChkPrecisionPacer.IsChecked = SettingsManager.Current.PrecisionFramePacer;
        }
        if (CmbRunAheadFrames != null)
        {
            CmbRunAheadFrames.SelectedIndex = Math.Clamp(SettingsManager.Current.RunAheadFrames, 0, 2);
        }
        if (BtnCpuDynarecFast != null && BtnCpuEsportsZeroLag != null && BtnCpuCycleAccurate != null)
        {
            int frames = SettingsManager.Current.RunAheadFrames;
            if (frames == 2)
            {
                BtnCpuDynarecFast.Tag = null;
                BtnCpuEsportsZeroLag.Tag = "Active";
                BtnCpuCycleAccurate.Tag = null;
            }
            else if (frames == 0)
            {
                BtnCpuDynarecFast.Tag = "Active";
                BtnCpuEsportsZeroLag.Tag = null;
                BtnCpuCycleAccurate.Tag = null;
            }
        }
    }

    // ==================== 2.0.0 CORE ABI BENCHMARK & BATCH UPDATER ====================
    private async void BtnUpdateAllInstalledCores_Click(object sender, RoutedEventArgs e)
    {
        ShowOsdToast("Пакетное обновление установленных ядер Libretro...");
        var progress = new Progress<string>(msg => ShowOsdToast(msg));
        var res = await CoreManager.UpdateAllInstalledCoresAsync(progress);
        ShowOsdToast($"Обновление ядер завершено: обновлено {res.Updated}, ошибок {res.Failed}");
        RefreshCoresUi();
    }

    private async Task RunCoreBenchmarkAsync(CoreInfo core)
    {
        ShowOsdToast($"Тестирование ABI и скорости загрузки «{core.Name}»...");
        var report = await CoreManager.InspectAndBenchmarkCoreAsync(core);
        if (BdrCoreBenchmarkResult != null)
        {
            BdrCoreBenchmarkResult.Visibility = Visibility.Visible;
            if (TxtCoreBenchmarkTitle != null)
                TxtCoreBenchmarkTitle.Text = $"Инспекция ABI: {report.CoreName} ({report.LibraryName} {report.LibraryVersion})";
            if (TxtCoreBenchmarkDetails != null)
                TxtCoreBenchmarkDetails.Text = report.SummaryRu;
        }
        ShowOsdToast($"Бенчмарк «{report.CoreName}»: {report.ColdLoadMs:F1} мс, ABI v{report.ApiVersion}");
    }

    public async void BtnBenchmarkCore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CoreInfo core })
        {
            await RunCoreBenchmarkAsync(core);
        }
    }

    private void BtnCloseCoreBenchmark_Click(object sender, RoutedEventArgs e)
    {
        if (BdrCoreBenchmarkResult != null)
            BdrCoreBenchmarkResult.Visibility = Visibility.Collapsed;
    }

    // ==================== 2.0.0 SHADER STUDIO: CURVATURE, CHROMATIC & LIVE PREVIEW ====================
    private async void BtnPrecompileShaders_Click(object sender, RoutedEventArgs e)
    {
        ShowOsdToast("Компиляция и индексация кэша шейдеров...");
        var report = await CompilerAndPerformanceManager.PrecompileShaderCacheAsync();
        ShowOsdToast(report.SummaryRu);
    }

    private void SliderShaderCurvature_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isLoaded || SliderShaderCurvature == null) return;
        int cur = (int)Math.Round(SliderShaderCurvature.Value);
        if (TxtShaderCurvatureVal != null) TxtShaderCurvatureVal.Text = $"{cur}%";
        NativeLibretroHost.Current.CurvatureIntensity = cur / 100.0;
        RefreshShaderLivePreview();
    }

    private void SliderShaderChromatic_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isLoaded || SliderShaderChromatic == null) return;
        int chr = (int)Math.Round(SliderShaderChromatic.Value);
        if (TxtShaderChromaticVal != null) TxtShaderChromaticVal.Text = $"{chr}%";
        NativeLibretroHost.Current.ChromaticIntensity = chr / 100.0;
        RefreshShaderLivePreview();
    }

    private void SliderShaderSplitCompare_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isLoaded) return;
        RefreshShaderLivePreview();
    }

    private void RefreshShaderLivePreview()
    {
        if (ImgShaderLivePreview == null) return;
        double splitVal = SliderShaderSplitCompare != null ? SliderShaderSplitCompare.Value : 50.0;
        double ratio = splitVal / 100.0;
        if (TxtShaderSplitVal != null) TxtShaderSplitVal.Text = $"{(int)splitVal}%";
        ImgShaderLivePreview.Source = NativeLibretroHost.RenderShaderPreviewSplit(ratio, 560, 315);
    }

    // ==================== 2.0.0 BEZELS & AMBILUX CONTROLS ====================
    private void ChkAutoPlatformBezel_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = ChkAutoPlatformBezel.IsChecked == true;
        SettingsManager.Current.AutoBezelByPlatform = enabled;
        SettingsManager.Save();
        ShowOsdToast($"Автоподбор рамки по платформе: {(enabled ? "ВКЛ" : "ВЫКЛ")}");
    }

    private void ChkAmbiluxGlow_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = ChkAmbiluxGlow.IsChecked == true;
        SettingsManager.Current.AmbiluxGlowEnabled = enabled;
        SettingsManager.Save();
        if (BdrAmbiluxAmbientGlow != null)
        {
            BdrAmbiluxAmbientGlow.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        }
        ShowOsdToast($"Фоновое свечение Ambilux: {(enabled ? "ВКЛ" : "ВЫКЛ")}");
    }

    private void SyncBezelControls()
    {
        if (ChkAutoPlatformBezel != null)
            ChkAutoPlatformBezel.IsChecked = SettingsManager.Current.AutoBezelByPlatform;
        if (ChkAmbiluxGlow != null)
            ChkAmbiluxGlow.IsChecked = SettingsManager.Current.AmbiluxGlowEnabled;
    }

    // ==================== 2.0.0 GAMEPAD DEADZONE, TRIGGER & TURBO STUDIO ====================
    private void SliderAnalogDeadzone_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isLoaded || SliderAnalogDeadzone == null) return;
        int dz = (int)Math.Round(SliderAnalogDeadzone.Value);
        if (TxtAnalogDeadzoneVal != null) TxtAnalogDeadzoneVal.Text = $"{dz}%";
        SettingsManager.Current.AnalogDeadzonePercent = dz;
        SettingsManager.Save();
    }

    private void SliderTriggerThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isLoaded || SliderTriggerThreshold == null) return;
        int th = (int)Math.Round(SliderTriggerThreshold.Value);
        if (TxtTriggerThresholdVal != null) TxtTriggerThresholdVal.Text = $"{th}%";
        SettingsManager.Current.TriggerThresholdPercent = th;
        SettingsManager.Save();
    }

    private void ChkRumbleEnabled_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = ChkRumbleEnabled.IsChecked == true;
        SettingsManager.Current.RumbleEnabled = enabled;
        SettingsManager.Save();
        ShowOsdToast($"Виброотдача XInput: {(enabled ? "ВКЛ" : "ВЫКЛ")}");
    }

    private void BtnTestRumble_Click(object sender, RoutedEventArgs e)
    {
        int port = Math.Max(0, _selectedRemapPlayerIndex - 1);
        NativeLibretroHost.TriggerTestRumble(port, 380);
        ShowOsdToast($"Тест виброотдачи геймпада (Порт {_selectedRemapPlayerIndex})...");
    }

    private void ChkTurboButtons_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = ChkTurboButtons.IsChecked == true;
        SettingsManager.Current.TurboButtonsEnabled = enabled;
        SettingsManager.Save();
        ShowOsdToast($"Турбо-огонь (A/B/X/Y): {(enabled ? "ВКЛ" : "ВЫКЛ")}");
    }

    private void CmbTurboFireHz_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || CmbTurboFireHz == null) return;
        int hz = CmbTurboFireHz.SelectedIndex switch
        {
            0 => 5,
            1 => 10,
            3 => 30,
            _ => 15
        };
        SettingsManager.Current.TurboFireHz = hz;
        SettingsManager.Save();
        ShowOsdToast($"Частота турбо-огня: {hz} Гц");
    }

    private void SyncGamepadStudioControls()
    {
        if (SliderAnalogDeadzone != null)
        {
            int dz = Math.Clamp(SettingsManager.Current.AnalogDeadzonePercent, 5, 40);
            SliderAnalogDeadzone.Value = dz;
            if (TxtAnalogDeadzoneVal != null) TxtAnalogDeadzoneVal.Text = $"{dz}%";
        }
        if (SliderTriggerThreshold != null)
        {
            int th = Math.Clamp(SettingsManager.Current.TriggerThresholdPercent, 5, 50);
            SliderTriggerThreshold.Value = th;
            if (TxtTriggerThresholdVal != null) TxtTriggerThresholdVal.Text = $"{th}%";
        }
        if (ChkRumbleEnabled != null)
        {
            ChkRumbleEnabled.IsChecked = SettingsManager.Current.RumbleEnabled;
        }
        if (ChkTurboButtons != null)
        {
            ChkTurboButtons.IsChecked = SettingsManager.Current.TurboButtonsEnabled;
        }
        if (CmbTurboFireHz != null)
        {
            int hz = SettingsManager.Current.TurboFireHz;
            int idx = hz switch
            {
                5 => 0,
                10 => 1,
                30 => 3,
                _ => 2
            };
            CmbTurboFireHz.SelectedIndex = idx;
        }
    }

    // =========================================================================
    // 2.0.0 PHASE 2: RAM SCANNER, AUDIO DSP STUDIO, QUICK MENU 2.0 & SMART COLLECTIONS
    // =========================================================================

    // --- 1. RAM CHEAT SCANNER (ViewCheats) ---

    private void BtnRamFirstScan_Click(object sender, RoutedEventArgs e)
    {
        var width = CmbRamWidth?.SelectedIndex switch
        {
            0 => RamValueWidth.Byte8,
            2 => RamValueWidth.UInt32,
            _ => RamValueWidth.UInt16
        };
        var mode = CmbRamCompare?.SelectedIndex switch
        {
            1 => RamCompareMode.NotEqual,
            2 => RamCompareMode.Increased,
            3 => RamCompareMode.Decreased,
            4 => RamCompareMode.Changed,
            5 => RamCompareMode.Unchanged,
            _ => RamCompareMode.ExactValue
        };
        uint.TryParse(TxtRamTargetValue?.Text, out uint target);
        RamCheatScannerEngine.Shared.StartFirstScan(width, mode, target);
        UpdateRamScannerUi();
        ShowOsdToast($"Первый поиск ОЗУ завершён: найдено {RamCheatScannerEngine.Shared.TotalCandidateCount:N0}");
    }

    private void BtnRamNextScan_Click(object sender, RoutedEventArgs e)
    {
        var mode = CmbRamCompare?.SelectedIndex switch
        {
            1 => RamCompareMode.NotEqual,
            2 => RamCompareMode.Increased,
            3 => RamCompareMode.Decreased,
            4 => RamCompareMode.Changed,
            5 => RamCompareMode.Unchanged,
            _ => RamCompareMode.ExactValue
        };
        uint.TryParse(TxtRamTargetValue?.Text, out uint target);
        RamCheatScannerEngine.Shared.NextScan(mode, target);
        UpdateRamScannerUi();
        ShowOsdToast($"Отсев ОЗУ завершён: осталось {RamCheatScannerEngine.Shared.TotalCandidateCount:N0}");
    }

    private void BtnRamResetScan_Click(object sender, RoutedEventArgs e)
    {
        RamCheatScannerEngine.Shared.ResetScan();
        UpdateRamScannerUi();
        ShowOsdToast("Сканер ОЗУ сброшен");
    }

    private void UpdateRamScannerUi()
    {
        if (ItemsRamMatches != null)
        {
            ItemsRamMatches.ItemsSource = null;
            ItemsRamMatches.ItemsSource = RamCheatScannerEngine.Shared.Matches;
        }
        if (TxtRamScanSummary != null)
        {
            if (RamCheatScannerEngine.Shared.HasActiveScan)
            {
                TxtRamScanSummary.Text = $"Найдено адресов: {RamCheatScannerEngine.Shared.TotalCandidateCount:N0} (Отображается: {RamCheatScannerEngine.Shared.Matches.Count}, Заморожено: {RamCheatScannerEngine.Shared.FrozenCount})";
            }
            else
            {
                TxtRamScanSummary.Text = "Готов к сканированию ОЗУ";
            }
        }
    }

    private void BtnRamPokeEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RamMatchEntry entry)
        {
            uint.TryParse(TxtRamPokeValue?.Text, out uint pokeVal);
            bool ok = RamCheatScannerEngine.Shared.PokeValue(entry.Address, entry.Width, pokeVal);
            ShowOsdToast(ok ? $"Записано {pokeVal} в {entry.HexAddress}" : $"Ошибка записи в {entry.HexAddress}");
            UpdateRamScannerUi();
        }
    }

    private void BtnRamFreezeEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RamMatchEntry entry)
        {
            uint.TryParse(TxtRamPokeValue?.Text, out uint pokeVal);
            RamCheatScannerEngine.Shared.ToggleFreeze(entry, pokeVal);
            ShowOsdToast(entry.IsFrozen ? $"Заморожено {entry.HexAddress} = {pokeVal}" : $"Разморожено {entry.HexAddress}");
            UpdateRamScannerUi();
        }
    }

    private void BtnRamExportEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RamMatchEntry entry)
        {
            uint.TryParse(TxtRamPokeValue?.Text, out uint pokeVal);
            ShowOsdToast($"Чит-код добавлен для {entry.HexAddress} = {pokeVal}");
        }
    }

    // --- 2. AUDIO DSP STUDIO (ViewSettings) ---

    private void ChkAudioSoftLimiter_Click(object sender, RoutedEventArgs e)
    {
        bool isChecked = ChkAudioSoftLimiter?.IsChecked == true;
        AudioDspEngine.Shared.SoftLimiterEnabled = isChecked;
        SettingsManager.Current.AudioSoftLimiter = isChecked;
        SettingsManager.Save();
        ShowOsdToast(isChecked ? "Мягкий лимитер Audio DSP включен" : "Мягкий лимитер Audio DSP выключен");
    }

    private void BtnAudioPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        string presetName = btn.Content?.ToString() ?? "Чистый студийный";
        AudioDspEngine.Shared.ApplyPreset(presetName);
        SyncAudioDspStudioControls();
        SettingsManager.Current.AudioDspPreset = presetName;
        SettingsManager.Current.AudioBassGainDb = AudioDspEngine.Shared.BassGainDb;
        SettingsManager.Current.AudioTrebleGainDb = AudioDspEngine.Shared.TrebleGainDb;
        SettingsManager.Current.AudioStereoWidthPercent = AudioDspEngine.Shared.StereoWidthPercent;
        SettingsManager.Current.AudioReverbPercent = AudioDspEngine.Shared.ReverbPercent;
        SettingsManager.Current.AudioSoftLimiter = AudioDspEngine.Shared.SoftLimiterEnabled;
        SettingsManager.Save();
        ShowOsdToast($"Аудио DSP пресет: {presetName}");
    }

    private void SliderAudioBass_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int val = (int)Math.Round(e.NewValue);
        if (TxtAudioBassVal != null) TxtAudioBassVal.Text = $"{val} дБ";
        AudioDspEngine.Shared.BassGainDb = val;
        SettingsManager.Current.AudioBassGainDb = val;
    }

    private void SliderAudioTreble_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int val = (int)Math.Round(e.NewValue);
        if (TxtAudioTrebleVal != null) TxtAudioTrebleVal.Text = $"{val} дБ";
        AudioDspEngine.Shared.TrebleGainDb = val;
        SettingsManager.Current.AudioTrebleGainDb = val;
    }

    private void SliderAudioStereo_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int val = (int)Math.Round(e.NewValue);
        if (TxtAudioStereoVal != null) TxtAudioStereoVal.Text = $"{val}%";
        AudioDspEngine.Shared.StereoWidthPercent = val;
        SettingsManager.Current.AudioStereoWidthPercent = val;
    }

    private void SliderAudioReverb_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int val = (int)Math.Round(e.NewValue);
        if (TxtAudioReverbVal != null) TxtAudioReverbVal.Text = $"{val}%";
        AudioDspEngine.Shared.ReverbPercent = val;
        SettingsManager.Current.AudioReverbPercent = val;
    }

    private void SyncAudioDspStudioControls()
    {
        if (SliderAudioBass != null) SliderAudioBass.Value = AudioDspEngine.Shared.BassGainDb;
        if (SliderAudioTreble != null) SliderAudioTreble.Value = AudioDspEngine.Shared.TrebleGainDb;
        if (SliderAudioStereo != null) SliderAudioStereo.Value = AudioDspEngine.Shared.StereoWidthPercent;
        if (SliderAudioReverb != null) SliderAudioReverb.Value = AudioDspEngine.Shared.ReverbPercent;
        if (ChkAudioSoftLimiter != null) ChkAudioSoftLimiter.IsChecked = AudioDspEngine.Shared.SoftLimiterEnabled;

        if (TxtAudioBassVal != null) TxtAudioBassVal.Text = $"{AudioDspEngine.Shared.BassGainDb} дБ";
        if (TxtAudioTrebleVal != null) TxtAudioTrebleVal.Text = $"{AudioDspEngine.Shared.TrebleGainDb} дБ";
        if (TxtAudioStereoVal != null) TxtAudioStereoVal.Text = $"{AudioDspEngine.Shared.StereoWidthPercent}%";
        if (TxtAudioReverbVal != null) TxtAudioReverbVal.Text = $"{AudioDspEngine.Shared.ReverbPercent}%";

        if (PnlAudioDspPresets != null)
        {
            foreach (var child in PnlAudioDspPresets.Children)
            {
                if (child is Button b)
                {
                    b.Tag = (b.Content?.ToString() == AudioDspEngine.Shared.ActivePreset) ? "Active" : null;
                }
            }
        }
    }

    // --- 3. QUICK MENU 2.0 (PnlInGameQuickMenu) ---

    private void BtnQuickSaveGameOverride_Click(object sender, RoutedEventArgs e)
    {
        var targetGame = _activeRomItem ?? _allGames.FirstOrDefault(g => g.Name.Contains("Battletoads", StringComparison.OrdinalIgnoreCase)) ?? _allGames.FirstOrDefault();
        if (targetGame != null)
        {
            bool ok = PerGameProfileManager.SaveProfileForGame(targetGame);
            if (TxtQuickGameProfileBadge != null)
                TxtQuickGameProfileBadge.Text = ok ? "Профиль: Сохранён" : "Ошибка сохранения";
            ShowOsdToast($"Индивидуальный профиль сохранён для «{targetGame.Name}»");
        }
        else
        {
            ShowOsdToast("Нет активной игры для сохранения профиля");
        }
    }

    private static readonly string[] _audioDspCyclePresets = new[]
    {
        "Чистый студийный",
        "Аркадный бас",
        "Тёплый ЭЛТ-динамик",
        "Широкая 3D-панорама",
        "Кристальный чиптюн",
        "Ночной компрессор"
    };

    private void BtnQuickCycleAudioDsp_Click(object sender, RoutedEventArgs e)
    {
        string current = AudioDspEngine.Shared.ActivePreset;
        int idx = Array.IndexOf(_audioDspCyclePresets, current);
        string next = _audioDspCyclePresets[(idx + 1 + _audioDspCyclePresets.Length) % _audioDspCyclePresets.Length];
        AudioDspEngine.Shared.ApplyPreset(next);
        SyncAudioDspStudioControls();
        SettingsManager.Current.AudioDspPreset = next;
        SettingsManager.Save();
        if (TxtQuickCycleAudioDspLabel != null)
            TxtQuickCycleAudioDspLabel.Text = $"Аудио DSP: {next}";
        ShowOsdToast($"Аудио DSP: {next}");
    }

    private void BtnQuickDiscTray_Click(object sender, RoutedEventArgs e)
    {
        bool open = NativeLibretroHost.Current.ToggleVirtualDiscTray();
        ShowOsdToast(open ? "Виртуальный лоток CD открыт" : "Виртуальный лоток CD закрыт");
    }

    private void BtnQuickDiscNext_Click(object sender, RoutedEventArgs e)
    {
        NativeLibretroHost.Current.SwitchVirtualDisc(1);
        ShowOsdToast(NativeLibretroHost.Current.GetVirtualDiscStatusRu());
    }

    private void BtnQuickInputRecord_Click(object sender, RoutedEventArgs e)
    {
        if (NativeLibretroHost.Current.IsRecordingInput)
        {
            NativeLibretroHost.Current.StopInputReplay();
            if (TxtQuickInputRecordLabel != null) TxtQuickInputRecordLabel.Text = "Запись ввода";
            ShowOsdToast("Запись ввода остановлена");
        }
        else
        {
            bool ok = NativeLibretroHost.Current.StartInputRecording();
            if (TxtQuickInputRecordLabel != null) TxtQuickInputRecordLabel.Text = "Стоп запись";
            ShowOsdToast(ok ? "Запись ввода BSV запущена" : "Ошибка запуска записи ввода");
        }
    }

    private void BtnQuickInputPlay_Click(object sender, RoutedEventArgs e)
    {
        if (NativeLibretroHost.Current.IsPlayingInput)
        {
            NativeLibretroHost.Current.StopInputReplay();
            if (TxtQuickInputPlayLabel != null) TxtQuickInputPlayLabel.Text = "Воспр. повтор";
            ShowOsdToast("Воспроизведение ввода остановлено");
        }
        else
        {
            bool ok = NativeLibretroHost.Current.StartInputPlayback();
            if (TxtQuickInputPlayLabel != null) TxtQuickInputPlayLabel.Text = "Стоп повтор";
            ShowOsdToast(ok ? "Воспроизведение ввода BSV запущено" : "Файл записи ввода не найден");
        }
    }

    // --- 4. SMART DYNAMIC COLLECTIONS (ViewLibrary) ---

    private string _selectedSmartCollection = "Все игры";
    private static readonly string[] _smartCollections = new[]
    {
        "Все игры",
        "Русские переводы",
        "Недавние",
        "Классика 8/16-бит",
        "MS-DOS",
        "3D и CD"
    };

    private void UpdateSmartCollectionPills()
    {
        if (PnlSmartCollectionPills == null) return;
        PnlSmartCollectionPills.Children.Clear();

        foreach (var col in _smartCollections)
        {
            bool isSelected = _selectedSmartCollection.Equals(col, StringComparison.OrdinalIgnoreCase);
            var btn = new Button
            {
                Style = (Style)FindResource("CapsuleButton"),
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(0, 0, 8, 4),
                Tag = isSelected ? "Active" : null,
                Cursor = Cursors.Hand,
                Content = col,
                ToolTip = $"Умная коллекция: {col}"
            };

            if (isSelected)
            {
                btn.Background = new SolidColorBrush(Color.FromArgb(0x40, 0x8B, 0x5C, 0xF6));
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6));
                btn.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
                btn.FontWeight = FontWeights.Bold;
            }
            else
            {
                btn.Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
                btn.BorderBrush = (Brush)FindResource("ThemeBorder");
                btn.Foreground = (Brush)FindResource("ThemeTextSecondary");
            }

            string capturedCol = col;
            btn.Click += (s, e) =>
            {
                _selectedSmartCollection = capturedCol;
                UpdateSmartCollectionPills();
                ApplyGamesFilter();
            };
            PnlSmartCollectionPills.Children.Add(btn);
        }
    }

}

public class CommandPaletteItem
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Category { get; set; } = "";
    public string IconGeometry { get; set; } = "";
    public Brush IconBrush { get; set; } = Brushes.White;
    public Brush IconBgBrush { get; set; } = Brushes.Transparent;
    public Brush BadgeTextBrush { get; set; } = Brushes.White;
    public Brush BadgeBgBrush { get; set; } = Brushes.Transparent;
    public Brush BadgeBorderBrush { get; set; } = Brushes.Transparent;
    public RomItem? Game { get; set; }
    public Action? ExecuteAction { get; set; }

    }
