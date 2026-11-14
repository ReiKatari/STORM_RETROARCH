using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace StormRetroArch;

/// <summary>
/// Phase 2 Modular UI, Unified Multi-Device & Hotkey Studio, Ergonomic HUD,
/// Controller Overlay, Frozen Brush Cache and Startup Optimizations for STORM RETROARCH 2.7.5.
/// </summary>
public partial class MainWindow
{
    // ==================== 1. FROZEN BRUSH CACHE (0.0 ms GC-Free) ====================
    private static readonly ConcurrentDictionary<string, SolidColorBrush> _frozenBrushHexCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<uint, SolidColorBrush> _frozenBrushColorCache = new();

    public static SolidColorBrush GetFrozenBrush(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Brushes.Transparent;
        return _frozenBrushHexCache.GetOrAdd(hex, h =>
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(h);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch
            {
                return Brushes.Transparent;
            }
        });
    }

    public static SolidColorBrush GetFrozenBrush(Color color)
    {
        uint key = ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | (uint)color.B;
        return _frozenBrushColorCache.GetOrAdd(key, _ =>
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        });
    }

    // ==================== 2. PHASE 2.7.5 STATE & FIELDS ====================
    private readonly DispatcherTimer _hotplugToastTimer = new();
    private readonly DispatcherTimer _physicalCaptureTimer = new();

    private DeviceInputProfile? _selectedDeviceProfile;
    private string _activeCoreScope = "Global";
    private string _lastRenderedArtSignature = "";
    private string _lastOverlayArtSignature = "";

    private bool _isCapturingGamepadChord;
    private bool _isCapturingPhysicalPadInput;
    private bool _suppressInputUiSync;
    private List<string> _wizardCaptureQueue = new();
    private int _wizardCaptureIndex = -1;
    private bool _wizardCaptureForKeyboard;

    // ==================== 3. INITIALIZATION ====================
    public void InitStudio210()
    {
        LogCheckpoint("InitStudio210 start");

        // 1. Configure Hot-Plug Toast Timer
        _hotplugToastTimer.Interval = TimeSpan.FromSeconds(4.0);
        _hotplugToastTimer.Tick += (s, e) =>
        {
            _hotplugToastTimer.Stop();
            if (BdrDeviceHotplugToast != null) BdrDeviceHotplugToast.Visibility = Visibility.Collapsed;
        };

        // 2. Configure Dual Physical+Keyboard Capture Timer (30 ms)
        _physicalCaptureTimer.Interval = TimeSpan.FromMilliseconds(30);
        _physicalCaptureTimer.Tick += PhysicalCaptureTimer_Tick;

        // 3. Subscribe to Device Engine & Theme Events
        DeviceInputProfileEngine.OnDeviceProfileActivated += (dev, msg) =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string devName = dev?.DisplayName ?? "Игровой контроллер";
                if (TxtTopActiveDeviceName != null) TxtTopActiveDeviceName.Text = devName;
                if (TxtHudActiveDeviceBadge != null) TxtHudActiveDeviceBadge.Text = devName;
                if (TxtOverlayDeviceName != null) TxtOverlayDeviceName.Text = devName;

                ShowDeviceHotplugToast(devName, msg ?? "Профиль устройства активирован");
            }));
        };

        DeviceInputProfileEngine.OnDevicesChanged += () =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                PopulateDeviceProfilesDropdown();
                PopulatePortDeviceAssignDropdown();
                if (ViewInputRemap?.Visibility == Visibility.Visible)
                {
                    LoadRemapBindings(_selectedRemapPlayerIndex);
                }
            }));
        };

        ThemeManager.ThemeChanged += () =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ViewInputRemap?.Visibility == Visibility.Visible)
                {
                    RebuildHotkeyStudioList();
                }
            }));
        };

        // 4. Populate Dropdowns
        PopulateDeviceProfilesDropdown();
        PopulateControllerStylesDropdown();
        PopulateCoreScopeDropdown();
        PopulateGamepadChordModifierDropdown();
        PopulatePortDeviceAssignDropdown();

        // 5. Build Hotkeys Studio UI
        RebuildHotkeyStudioList();

        // 6. Set Initial Active Device Pill
        if (TxtTopActiveDeviceName != null)
        {
            var port0 = DeviceInputProfileEngine.GetPort(0);
            TxtTopActiveDeviceName.Text = port0.IsConnected && !port0.IsKeyboardVirtual
                ? port0.DisplayName
                : (_selectedDeviceProfile?.DisplayName ?? "Клавиатура ПК");
        }

        LogCheckpoint("InitStudio210 complete");
    }

    // ==================== 4. DEVICE PROFILES & SELECTION ====================
    public void PopulateDeviceProfilesDropdown()
    {
        if (CmbActiveDeviceProfile == null) return;

        string prevSelected = _selectedDeviceProfile?.DeviceId ?? "";
        _suppressInputUiSync = true;
        try
        {
            CmbActiveDeviceProfile.Items.Clear();
            if (CmbOverlayQuickDevice != null) CmbOverlayQuickDevice.Items.Clear();
            if (CmbQuickMenuDeviceProfile != null) CmbQuickMenuDeviceProfile.Items.Clear();

            var profiles = DeviceInputProfileEngine.GetAllStoredProfiles();
            if (profiles.Count == 0)
            {
                DeviceInputProfileEngine.Initialize();
                profiles = DeviceInputProfileEngine.GetAllStoredProfiles();
            }

            int matchIdx = 0;
            for (int i = 0; i < profiles.Count; i++)
            {
                var p = profiles[i];
                CmbActiveDeviceProfile.Items.Add(p.DisplayName);
                if (CmbOverlayQuickDevice != null) CmbOverlayQuickDevice.Items.Add(p.DisplayName);
                if (CmbQuickMenuDeviceProfile != null) CmbQuickMenuDeviceProfile.Items.Add(p.DisplayName);
                if (!string.IsNullOrEmpty(prevSelected) && p.DeviceId.Equals(prevSelected, StringComparison.OrdinalIgnoreCase))
                {
                    matchIdx = i;
                }
            }

            if (CmbActiveDeviceProfile.Items.Count > 0)
            {
                // Prefer the primary port connected device on first init if no prior selection
                if (string.IsNullOrEmpty(prevSelected))
                {
                    var p0 = DeviceInputProfileEngine.GetPort(Math.Max(0, _selectedRemapPlayerIndex - 1));
                    if (p0.Profile != null)
                    {
                        int p0Idx = profiles.FindIndex(x => x.DeviceId.Equals(p0.Profile.DeviceId, StringComparison.OrdinalIgnoreCase));
                        if (p0Idx >= 0) matchIdx = p0Idx;
                    }
                }
                CmbActiveDeviceProfile.SelectedIndex = matchIdx;
                _selectedDeviceProfile = profiles[matchIdx];
                if (CmbOverlayQuickDevice != null && matchIdx < CmbOverlayQuickDevice.Items.Count) CmbOverlayQuickDevice.SelectedIndex = matchIdx;
                if (CmbQuickMenuDeviceProfile != null && matchIdx < CmbQuickMenuDeviceProfile.Items.Count) CmbQuickMenuDeviceProfile.SelectedIndex = matchIdx;
            }
        }
        finally
        {
            _suppressInputUiSync = false;
        }
        ApplyActiveDeviceProfileToUI();
    }

    private void PopulateControllerStylesDropdown()
    {
        if (CmbControllerStyle == null) return;
        _suppressInputUiSync = true;
        try
        {
            CmbControllerStyle.Items.Clear();
            foreach (var p in ControllerArtRenderer.PlatformProfiles)
            {
                CmbControllerStyle.Items.Add(p);
            }
            if (CmbControllerStyle.Items.Count > 0) CmbControllerStyle.SelectedIndex = 0;
        }
        finally
        {
            _suppressInputUiSync = false;
        }
    }

    private void PopulateCoreScopeDropdown()
    {
        if (CmbDeviceCoreScope == null) return;
        _suppressInputUiSync = true;
        try
        {
            CmbDeviceCoreScope.Items.Clear();
            CmbDeviceCoreScope.Items.Add("Глобально (все ядра)");
            CmbDeviceCoreScope.Items.Add("Ядро: FCEUmm (NES)");
            CmbDeviceCoreScope.Items.Add("Ядро: FBNeo (Аркады)");
            CmbDeviceCoreScope.Items.Add("Ядро: DOSBox-Pure (MS-DOS)");
            CmbDeviceCoreScope.Items.Add("Ядро: Mupen64Plus-Next (N64)");
            CmbDeviceCoreScope.Items.Add("Ядро: PCSX-ReARMed (PS1)");
            CmbDeviceCoreScope.Items.Add("Ядро: Genesis Plus GX (Sega)");
            CmbDeviceCoreScope.Items.Add("Ядро: Snes9x (SNES)");
            CmbDeviceCoreScope.SelectedIndex = 0;
        }
        finally
        {
            _suppressInputUiSync = false;
        }
    }

    private void PopulateGamepadChordModifierDropdown()
    {
        if (CmbGamepadChordModifier == null) return;
        _suppressInputUiSync = true;
        try
        {
            CmbGamepadChordModifier.Items.Clear();
            CmbGamepadChordModifier.Items.Add("Select / Back (Стандарт)");
            CmbGamepadChordModifier.Items.Add("Guide / Home");
            CmbGamepadChordModifier.Items.Add("L3 (Левый стик)");
            CmbGamepadChordModifier.Items.Add("Одиночная кнопка (без аккорда)");
            string mod = SettingsManager.Current?.GamepadChordModifier ?? "SelectOrBack";
            CmbGamepadChordModifier.SelectedIndex = mod switch
            {
                "GuideOrHome" => 1,
                "L3" => 2,
                "SingleButton" => 3,
                _ => 0
            };
        }
        finally
        {
            _suppressInputUiSync = false;
        }
    }

    public void SelectDeviceProfileByName(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName)) return;

        var all = DeviceInputProfileEngine.GetAllStoredProfiles();
        var match = all.FirstOrDefault(p => p.DisplayName.Equals(profileName, StringComparison.OrdinalIgnoreCase) ||
                                            p.DisplayName.Contains(profileName, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            _selectedDeviceProfile = match;
            if (CmbActiveDeviceProfile != null)
            {
                _suppressInputUiSync = true;
                try
                {
                    for (int i = 0; i < CmbActiveDeviceProfile.Items.Count; i++)
                    {
                        if (CmbActiveDeviceProfile.Items[i]?.ToString()?.Contains(profileName, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            CmbActiveDeviceProfile.SelectedIndex = i;
                            break;
                        }
                    }
                }
                finally
                {
                    _suppressInputUiSync = false;
                }
            }
            ApplyActiveDeviceProfileToUI();
        }
    }

    private void ApplyActiveDeviceProfileToUI()
    {
        if (_selectedDeviceProfile == null) return;

        _suppressInputUiSync = true;
        try
        {
            var connectedMatches = DeviceInputProfileEngine.GetConnectedDevices()
                .Where(d => d.Profile?.DeviceId.Equals(_selectedDeviceProfile.DeviceId, StringComparison.OrdinalIgnoreCase) == true
                         || (d.VendorId == _selectedDeviceProfile.VendorId && d.ProductId == _selectedDeviceProfile.ProductId && _selectedDeviceProfile.VendorId != 0))
                .ToList();
            var liveDev = connectedMatches.FirstOrDefault();

            // 1. Update Conn Badge & dot
            if (TxtDeviceConnBadge != null)
            {
                TxtDeviceConnBadge.Text = liveDev != null
                    ? $"Подключён • {(liveDev.IsXInput ? "XInput" : "WinMM/HID")} • Порт {liveDev.PortIndex + 1}"
                    : (_selectedDeviceProfile.AutoLoadOnConnect ? "Автопрофиль (оффлайн)" : "Сохранённый профиль");
            }
            if (DotDeviceStatus != null)
            {
                DotDeviceStatus.Background = GetFrozenBrush(liveDev != null ? "#10B981" : "#F59E0B");
            }

            if (ChkDeviceAutoLoad != null)
            {
                ChkDeviceAutoLoad.IsChecked = _selectedDeviceProfile.AutoLoadOnConnect;
            }

            // 2. Sync Style Dropdown
            if (CmbControllerStyle != null)
            {
                string styleTarget = ControllerArtRenderer.FormFactorToProfileName(_selectedDeviceProfile.FormFactor);
                int targetIdx = -1;
                for (int i = 0; i < CmbControllerStyle.Items.Count; i++)
                {
                    if (string.Equals(CmbControllerStyle.Items[i]?.ToString(), styleTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        targetIdx = i;
                        break;
                    }
                }
                if (targetIdx < 0) targetIdx = Math.Clamp((int)_selectedDeviceProfile.FormFactor, 0, Math.Max(0, CmbControllerStyle.Items.Count - 1));
                if (targetIdx >= 0 && targetIdx < CmbControllerStyle.Items.Count)
                {
                    CmbControllerStyle.SelectedIndex = targetIdx;
                }
            }

            // 3. Update Hardware Info Readout
            if (TxtDeviceHardwareInfo != null)
            {
                string extra = liveDev != null && !string.IsNullOrWhiteSpace(liveDev.AutoconfigSource)
                    ? $" • {liveDev.AutoconfigSource}"
                    : "";
                string caps = liveDev != null && liveDev.AxisCount > 0
                    ? $" • Осей: {liveDev.AxisCount}, Кнопок: {liveDev.ButtonCount}"
                    : "";
                TxtDeviceHardwareInfo.Text = $"VID: 0x{_selectedDeviceProfile.VendorId:X4} • PID: 0x{_selectedDeviceProfile.ProductId:X4} • {_selectedDeviceProfile.FormFactor}{caps}{extra}";
            }

            // 4. Update Sliders
            var layer = _activeCoreScope == "Global"
                ? _selectedDeviceProfile.GlobalLayer
                : (_selectedDeviceProfile.CoreLayers.TryGetValue(_activeCoreScope, out var cl) ? cl : _selectedDeviceProfile.GlobalLayer);

            if (SliderAnalogDeadzone != null) SliderAnalogDeadzone.Value = Math.Clamp(layer.AnalogDeadzonePercent, 5, 40);
            if (TxtAnalogDeadzoneVal != null) TxtAnalogDeadzoneVal.Text = $"{layer.AnalogDeadzonePercent}%";

            if (SliderSteeringDeadzone != null) SliderSteeringDeadzone.Value = Math.Clamp(layer.SteeringDeadzonePercent, 0, 25);
            if (TxtSteeringDeadzoneVal != null) TxtSteeringDeadzoneVal.Text = $"{layer.SteeringDeadzonePercent}%";

            if (SliderTriggerThreshold != null) SliderTriggerThreshold.Value = Math.Clamp(layer.TriggerThresholdPercent, 5, 50);
            if (TxtTriggerThresholdVal != null) TxtTriggerThresholdVal.Text = $"{layer.TriggerThresholdPercent}%";

            // 5. Update Realistic Controller Image Preview (Change-Only)
            UpdateControllerPreviewChangeOnly(_selectedDeviceProfile.FormFactor.ToString(), _selectedRemapPlayerIndex, null);

            // 6. Update Top Pill & In-Game Badges
            if (TxtTopActiveDeviceName != null) TxtTopActiveDeviceName.Text = _selectedDeviceProfile.DisplayName;
            if (TxtHudActiveDeviceBadge != null) TxtHudActiveDeviceBadge.Text = _selectedDeviceProfile.DisplayName;
            if (TxtOverlayDeviceName != null) TxtOverlayDeviceName.Text = _selectedDeviceProfile.DisplayName;
        }
        finally
        {
            _suppressInputUiSync = false;
        }
    }

    // ==================== 5. CHANGE-ONLY CONTROLLER ART REDRAW ====================
    public void UpdateControllerPreviewChangeOnly(string style, int playerIndex, string? highlightKey = null, double lx = 0, double ly = 0, double rx = 0, double ry = 0)
    {
        if (ImgRealisticController == null) return;

        string sig = $"{style}_{playerIndex}_{highlightKey ?? "none"}_{Math.Round(lx, 1)}_{Math.Round(ly, 1)}_{Math.Round(rx, 1)}_{Math.Round(ry, 1)}";
        if (sig == _lastRenderedArtSignature) return;

        _lastRenderedArtSignature = sig;
        try
        {
            var formFactor = ControllerArtRenderer.ProfileNameToFormFactor(style);
            ImgRealisticController.Source = ControllerArtRenderer.RenderController(formFactor, playerIndex, highlightKey, null, lx, ly, rx, ry);
        }
        catch { }
    }

    public void UpdateOverlayArtChangeOnly(string style, int playerIndex, string? highlightKey = null, double lx = 0, double ly = 0, double rx = 0, double ry = 0)
    {
        if (ImgInGameOverlayPad == null) return;

        string sig = $"{style}_{playerIndex}_{highlightKey ?? "none"}_{Math.Round(lx, 1)}_{Math.Round(ly, 1)}";
        if (sig == _lastOverlayArtSignature) return;

        _lastOverlayArtSignature = sig;
        try
        {
            var formFactor = ControllerArtRenderer.ProfileNameToFormFactor(style);
            ImgInGameOverlayPad.Source = ControllerArtRenderer.RenderController(formFactor, playerIndex, highlightKey, null, lx, ly, rx, ry);
        }
        catch { }
    }

    // ==================== 6. SUB-TAB SWITCHING (ViewInputRemap) ====================
    private void BtnSubTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        bool isDeviceMap = btn == BtnSubTabDeviceMap;

        if (BtnSubTabDeviceMap != null) BtnSubTabDeviceMap.Tag = isDeviceMap ? "Active" : null;
        if (BtnSubTabHotkeysStudio != null) BtnSubTabHotkeysStudio.Tag = isDeviceMap ? null : "Active";

        if (PnlSubViewControllerMap != null)
        {
            PnlSubViewControllerMap.Visibility = isDeviceMap ? Visibility.Visible : Visibility.Collapsed;
        }
        if (PnlSubViewHotkeysStudio != null)
        {
            PnlSubViewHotkeysStudio.Visibility = isDeviceMap ? Visibility.Collapsed : Visibility.Visible;
            if (!isDeviceMap)
            {
                RebuildHotkeyStudioList();
            }
        }
    }

    // ==================== 7. HOTKEYS STUDIO (18 Categorized Actions) ====================
    public void RebuildHotkeyStudioList()
    {
        if (PnlHotkeyCategoriesHost == null) return;

        PnlHotkeyCategoriesHost.Children.Clear();
        var allBindings = HotkeyManager.GetAllBindings();
        var conflicts = HotkeyManager.FindConflicts();
        bool isLightTheme = ThemeManager.CurrentPalette.IsLight;

        // Update Conflict Status Badge
        if (TxtHotkeyConflictStatus != null && BdrHotkeyConflictStatus != null)
        {
            if (conflicts.Count == 0)
            {
                TxtHotkeyConflictStatus.Text = $"Конфликтов нет ({allBindings.Count} действий)";
                TxtHotkeyConflictStatus.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextPrimary");
                BdrHotkeyConflictStatus.Background = GetFrozenBrush(isLightTheme ? "#1810B981" : "#1A10B981");
                BdrHotkeyConflictStatus.BorderBrush = GetFrozenBrush("#10B981");
            }
            else
            {
                TxtHotkeyConflictStatus.Text = $"Конфликты: {conflicts.Count} пересечений";
                TxtHotkeyConflictStatus.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextPrimary");
                BdrHotkeyConflictStatus.Background = GetFrozenBrush("#2AF59E0B");
                BdrHotkeyConflictStatus.BorderBrush = GetFrozenBrush("#F59E0B");
            }
        }

        // Group into 4 Categories
        var groups = allBindings.GroupBy(b => b.Category).OrderBy(g => GetCategorySortIndex(g.Key));

        foreach (var group in groups)
        {
            var cardBorder = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 10)
            };
            cardBorder.SetResourceReference(Border.BackgroundProperty, "ThemeSurface");
            cardBorder.SetResourceReference(Border.BorderBrushProperty, "ThemeBorder");

            var stack = new StackPanel();

            // Category Header
            var headerDock = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var catTitle = new TextBlock
            {
                Text = group.Key.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            catTitle.SetResourceReference(TextBlock.ForegroundProperty, isLightTheme ? "ThemeAccent" : "ThemeAccentGlow");
            headerDock.Children.Add(catTitle);
            stack.Children.Add(headerDock);

            // Action Rows
            foreach (var item in group)
            {
                var rowBorder = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(7),
                    Margin = new Thickness(0, 0, 0, 6),
                    Padding = new Thickness(10, 7, 10, 7)
                };
                rowBorder.SetResourceReference(Border.BackgroundProperty, "ThemeBgInput");
                rowBorder.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderSubtle");

                var rowDock = new DockPanel { LastChildFill = true };

                // Right controls: [Key Pill + Btn] + [Pad Chord Pill + Btn + Unbind] + [Reset Btn]
                var rightWrap = new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(rightWrap, Dock.Right);

                // Keyboard Pill
                var keyPill = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 2, 4, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                keyPill.SetResourceReference(Border.BackgroundProperty, "ThemeAccentSoft");
                keyPill.SetResourceReference(Border.BorderBrushProperty, "ThemeAccent");
                var keyText = new TextBlock
                {
                    Text = item.KeyBadgeText,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold
                };
                keyText.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextPrimary");
                keyPill.Child = keyText;
                rightWrap.Children.Add(keyPill);

                var btnAssignKey = new Button
                {
                    Content = "Клавиша",
                    Style = (Style)FindResource("StormSecondaryButton"),
                    Padding = new Thickness(8, 3, 8, 3),
                    FontSize = 10.5,
                    Margin = new Thickness(0, 2, 10, 2),
                    Tag = item
                };
                btnAssignKey.Click += BtnAssignHotkeyKey_Click;
                rightWrap.Children.Add(btnAssignKey);

                // Gamepad Chord Pill
                bool hasPad = !string.IsNullOrWhiteSpace(item.GamepadChord) && !item.GamepadChord.Equals("None", StringComparison.OrdinalIgnoreCase);
                var padPill = new Border
                {
                    Background = GetFrozenBrush(hasPad ? (isLightTheme ? "#1810B981" : "#1A10B981") : "#1464748B"),
                    BorderBrush = GetFrozenBrush(hasPad ? "#10B981" : "#64748B"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 2, 4, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var padText = new TextBlock
                {
                    Text = item.GamepadBadgeText,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold
                };
                padText.SetResourceReference(TextBlock.ForegroundProperty, hasPad ? "ThemeTextPrimary" : "ThemeTextMuted");
                padPill.Child = padText;
                rightWrap.Children.Add(padPill);

                var btnAssignPad = new Button
                {
                    Content = "Геймпад",
                    Style = (Style)FindResource("StormSecondaryButton"),
                    Padding = new Thickness(8, 3, 8, 3),
                    FontSize = 10.5,
                    Margin = new Thickness(0, 2, 4, 2),
                    Tag = item
                };
                btnAssignPad.Click += BtnAssignHotkeyPad_Click;
                rightWrap.Children.Add(btnAssignPad);

                if (hasPad)
                {
                    var btnClearPad = new Button
                    {
                        Content = "Снять",
                        Style = (Style)FindResource("StormGhostButton"),
                        Padding = new Thickness(6, 3, 6, 3),
                        FontSize = 10,
                        Margin = new Thickness(0, 2, 6, 2),
                        Tag = item
                    };
                    btnClearPad.Click += BtnClearHotkeyPad_Click;
                    rightWrap.Children.Add(btnClearPad);
                }

                // Reset Button
                var btnReset = new Button
                {
                    Content = "Сброс",
                    Style = (Style)FindResource("StormGhostButton"),
                    Padding = new Thickness(6, 3, 6, 3),
                    FontSize = 10,
                    Margin = new Thickness(0, 2, 0, 2),
                    Tag = item
                };
                btnReset.Click += BtnResetHotkeyItem_Click;
                rightWrap.Children.Add(btnReset);

                rowDock.Children.Add(rightWrap);

                // Left: Title + Subtitle
                var leftStack = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                var titleText = new TextBlock
                {
                    Text = item.Title,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap
                };
                titleText.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextPrimary");
                var descText = new TextBlock
                {
                    Text = item.Description,
                    FontSize = 10.5,
                    Margin = new Thickness(0, 1, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                };
                descText.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextSecondary");
                leftStack.Children.Add(titleText);
                leftStack.Children.Add(descText);

                rowDock.Children.Add(leftStack);

                rowBorder.Child = rowDock;
                stack.Children.Add(rowBorder);
            }

            cardBorder.Child = stack;
            PnlHotkeyCategoriesHost.Children.Add(cardBorder);
        }
    }

    private static int GetCategorySortIndex(string cat)
    {
        if (cat.Contains("Сессия", StringComparison.OrdinalIgnoreCase)) return 0;
        if (cat.Contains("Сохранен", StringComparison.OrdinalIgnoreCase) || cat.Contains("время", StringComparison.OrdinalIgnoreCase)) return 1;
        if (cat.Contains("Экран", StringComparison.OrdinalIgnoreCase) || cat.Contains("звук", StringComparison.OrdinalIgnoreCase)) return 2;
        return 3;
    }

    // ==================== 8. DUAL PHYSICAL + KEYBOARD CAPTURE ====================
    private string GetChordPrefixForSettings()
    {
        string mod = SettingsManager.Current?.GamepadChordModifier ?? "SelectOrBack";
        return mod switch
        {
            "GuideOrHome" => "Guide + ",
            "L3" => "L3 + ",
            "SingleButton" => "",
            _ => "Select + "
        };
    }

    private void CmbGamepadChordModifier_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressInputUiSync || CmbGamepadChordModifier == null) return;
        string mode = CmbGamepadChordModifier.SelectedIndex switch
        {
            1 => "GuideOrHome",
            2 => "L3",
            3 => "SingleButton",
            _ => "SelectOrBack"
        };
        SettingsManager.Current.GamepadChordModifier = mode;
        SettingsManager.Save();

        // Refresh existing chord displays to reflect new modifier label
        string newPrefix = GetChordPrefixForSettings();
        foreach (var item in HotkeyManager.GetAllBindings())
        {
            if (string.IsNullOrWhiteSpace(item.GamepadChord) || item.GamepadChord.Equals("None", StringComparison.OrdinalIgnoreCase))
                continue;
            int plusIdx = item.GamepadChord.LastIndexOf('+');
            string triggerName = plusIdx >= 0 ? item.GamepadChord[(plusIdx + 1)..].Trim() : item.GamepadChord.Trim();
            string updatedChord = string.IsNullOrEmpty(newPrefix) ? triggerName : $"{newPrefix}{triggerName}";
            HotkeyManager.SetGamepadBinding(item.ActionId, null, updatedChord);
        }

        DeviceInputProfileEngine.ApplyCompiledMapsToNativeHost();
        RebuildHotkeyStudioList();
        ShowDeviceHotplugToast("Модификатор аккорда", $"Установлен режим: {CmbGamepadChordModifier.SelectedItem}");
    }

    private void BtnAssignHotkeyKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is HotkeyBindingItem item)
        {
            _isCapturingGamepadButton = false;
            _isCapturingPhysicalPadInput = false;
            _physicalCaptureTimer.Stop();

            _capturingHotkeyActionId = item.ActionId;
            _isCapturingGamepadChord = false;

            if (BadgeHotkeyCapture != null) BadgeHotkeyCapture.Visibility = Visibility.Visible;
            if (TxtHotkeyCaptureStatus != null)
            {
                TxtHotkeyCaptureStatus.Text = $"Нажмите клавишу или комбинацию (Ctrl/Alt/Shift + клавиша) для «{item.Title}» (ESC - отмена)...";
            }
        }
    }

    private void BtnAssignHotkeyPad_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is HotkeyBindingItem item)
        {
            _isCapturingGamepadButton = false;
            _isCapturingPhysicalPadInput = false;
            _capturingHotkeyActionId = item.ActionId;
            _isCapturingGamepadChord = true;

            if (BadgeHotkeyCapture != null) BadgeHotkeyCapture.Visibility = Visibility.Visible;
            if (TxtHotkeyCaptureStatus != null)
            {
                TxtHotkeyCaptureStatus.Text = $"Нажмите кнопку, курок или направление на контроллере/руле для «{item.Title}» (ESC - отмена)...";
            }

            DeviceInputProfileEngine.BeginInputCaptureSnapshot();
            _physicalCaptureTimer.Start();
        }
    }

    private void BtnClearHotkeyPad_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is HotkeyBindingItem item)
        {
            HotkeyManager.ClearGamepadBinding(item.ActionId);
            DeviceInputProfileEngine.ApplyCompiledMapsToNativeHost();
            RebuildHotkeyStudioList();
            ShowDeviceHotplugToast("Горячие клавиши", $"Привязка геймпада для «{item.Title}» снята");
        }
    }

    private void BtnResetHotkeyItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is HotkeyBindingItem item)
        {
            HotkeyManager.ResetActionToDefault(item.ActionId);
            DeviceInputProfileEngine.ApplyCompiledMapsToNativeHost();
            RebuildHotkeyStudioList();
        }
    }

    private (DeviceInputProfile Profile, BindingLayer Layer) GetPortEditingProfileAndLayer(int portIndex)
    {
        var portDev = DeviceInputProfileEngine.GetPort(portIndex);
        DeviceInputProfile? profile = portDev.Profile;
        if (profile == null)
        {
            if (_selectedDeviceProfile != null)
            {
                profile = _selectedDeviceProfile;
            }
            else
            {
                profile = DeviceInputProfileEngine.GetOrCreateProfile($"PORT_{portIndex + 1}_GENERIC", $"Игрок {portIndex + 1} (Универсальный)", 0, 0);
            }
        }
        var layer = GetSelectedEditingLayer(profile);
        return (profile, layer);
    }

    private void PhysicalCaptureTimer_Tick(object? sender, EventArgs e)
    {
        int? preferredPort = (!string.IsNullOrEmpty(_capturingHotkeyActionId) && _isCapturingGamepadChord)
            ? null
            : Math.Max(0, _selectedRemapPlayerIndex - 1);

        var input = DeviceInputProfileEngine.PollAnyPhysicalInputForCapture(preferredPort);
        if (input == null) return;

        _physicalCaptureTimer.Stop();

        if (!string.IsNullOrEmpty(_capturingHotkeyActionId) && _isCapturingGamepadChord)
        {
            string prefix = GetChordPrefixForSettings();
            string chord = string.IsNullOrEmpty(prefix) ? input.DisplayLabel : $"{prefix}{input.DisplayLabel}";
            HotkeyManager.SetGamepadBinding(_capturingHotkeyActionId, input, chord);
            DeviceInputProfileEngine.ApplyCompiledMapsToNativeHost();
            _capturingHotkeyActionId = null;
            _isCapturingGamepadChord = false;

            if (BadgeHotkeyCapture != null) BadgeHotkeyCapture.Visibility = Visibility.Collapsed;
            RebuildHotkeyStudioList();
            ShowDeviceHotplugToast("Горячие клавиши", $"Привязано к контроллеру: {chord}");
        }
        else if (_isCapturingGamepadButton && _isCapturingPhysicalPadInput && !string.IsNullOrEmpty(_capturingButtonName))
        {
            int portIdx = Math.Max(0, _selectedRemapPlayerIndex - 1);
            int retroId = ButtonNameToRetroId(_capturingButtonName);

            var binding = _allPortRemapBindings.FirstOrDefault(b => b.ButtonName.Equals(_capturingButtonName, StringComparison.OrdinalIgnoreCase))
                       ?? _currentRemapBindings.FirstOrDefault(b => b.ButtonName.Equals(_capturingButtonName, StringComparison.OrdinalIgnoreCase));
            if (binding != null)
            {
                binding.BoundBtn = input.DisplayLabel;
            }

            if (retroId >= 0)
            {
                var (profile, layer) = GetPortEditingProfileAndLayer(portIdx);
                if (retroId >= 100)
                {
                    layer.AnalogBindings[retroId] = input;
                }
                else
                {
                    layer.JoypadBindings[retroId] = input;
                }
                DeviceInputProfileEngine.SaveProfile(profile);
                DeviceInputProfileEngine.ApplyCompiledMapsToNativeHost();
            }

            if (TxtGamepadCaptureStatus != null)
            {
                TxtGamepadCaptureStatus.Text = $"Порт {portIdx + 1}: [{_capturingButtonName.ToUpperInvariant()}] -> {input.DisplayLabel}";
            }
            ShowDeviceHotplugToast($"Порт {portIdx + 1}", $"{_capturingButtonName.ToUpperInvariant()} -> {input.DisplayLabel}");
            _isCapturingGamepadButton = false;
            _isCapturingPhysicalPadInput = false;
            _capturingButtonName = "";

            AdvanceBindWizardIfActive();
        }
    }

    // ==================== 9. UI EVENT HANDLERS ====================
    private void BtnTopActiveDevice_Click(object sender, RoutedEventArgs e)
    {
        NavBtn_Click(NavBtnInputRemap, e);
    }

    private void CmbActiveDeviceProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressInputUiSync || CmbActiveDeviceProfile?.SelectedItem == null) return;
        string selName = CmbActiveDeviceProfile.SelectedItem.ToString() ?? "";
        SelectDeviceProfileByName(selName);
    }

    private void CmbControllerStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressInputUiSync || CmbControllerStyle?.SelectedItem == null) return;
        string styleName = CmbControllerStyle.SelectedItem.ToString() ?? "";
        if (_selectedDeviceProfile != null)
        {
            _selectedDeviceProfile.FormFactor = ControllerArtRenderer.ProfileNameToFormFactor(styleName);
        }
        UpdateControllerPreviewChangeOnly(styleName, _selectedRemapPlayerIndex, null);
    }

    private void CmbDeviceCoreScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressInputUiSync || CmbDeviceCoreScope?.SelectedItem == null) return;
        string scopeStr = CmbDeviceCoreScope.SelectedItem.ToString() ?? "Global";

        if (scopeStr.StartsWith("Глобально", StringComparison.OrdinalIgnoreCase))
        {
            _activeCoreScope = "Global";
        }
        else
        {
            // Extract core name e.g. "FBNeo" from "Ядро: FBNeo (Аркады)"
            int colon = scopeStr.IndexOf(':');
            int paren = scopeStr.IndexOf('(');
            if (colon >= 0 && paren > colon)
            {
                _activeCoreScope = scopeStr[(colon + 1)..paren].Trim().ToLowerInvariant();
            }
            else
            {
                _activeCoreScope = scopeStr.Trim().ToLowerInvariant();
            }
        }

        if (TxtDeviceCoreBadge != null)
        {
            TxtDeviceCoreBadge.Text = _activeCoreScope == "Global" ? "Слой: Глобально" : $"Слой: {_activeCoreScope}";
        }
        if (TxtOverlayCoreBadge != null)
        {
            TxtOverlayCoreBadge.Text = _activeCoreScope == "Global" ? "Слой: Глобально" : $"Слой: {_activeCoreScope}";
        }

        ApplyActiveDeviceProfileToUI();
        LoadRemapBindings(_selectedRemapPlayerIndex);
    }

    private void ChkDeviceAutoLoad_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDeviceProfile == null || ChkDeviceAutoLoad == null) return;
        _selectedDeviceProfile.AutoLoadOnConnect = ChkDeviceAutoLoad.IsChecked == true;
        DeviceInputProfileEngine.SaveProfile(_selectedDeviceProfile);
        ApplyActiveDeviceProfileToUI();
        ShowOsdToast(_selectedDeviceProfile.AutoLoadOnConnect ? "Автопрофиль включён" : "Автопрофиль выключен");
    }

    private void BtnSaveDeviceLayer_Click(object sender, RoutedEventArgs e)
    {
        int portIdx = Math.Max(0, _selectedRemapPlayerIndex - 1);
        var (profile, layer) = GetPortEditingProfileAndLayer(portIdx);
        SaveRemapListIntoPortAndProfile(portIdx, profile, layer);
        DeviceInputProfileEngine.SaveProfile(profile);
        if (_selectedDeviceProfile != null && !ReferenceEquals(_selectedDeviceProfile, profile))
        {
            DeviceInputProfileEngine.SaveProfile(_selectedDeviceProfile);
        }
        DeviceInputProfileEngine.ApplyCompiledMapsToNativeHost();
        ShowOsdToast($"Профиль «{profile.DisplayName}» и назначения Порта {_selectedRemapPlayerIndex} сохранены!");
    }

    private void BtnCloneDeviceProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDeviceProfile == null) return;
        string newName = $"{_selectedDeviceProfile.DisplayName} (Копия)";
        string newId = $"{_selectedDeviceProfile.DeviceId}_COPY_{DateTime.UtcNow.Ticks % 10000}";
        var clone = DeviceInputProfileEngine.CreateDefaultProfileForFormFactor(newId, newName, _selectedDeviceProfile.FormFactor, _selectedDeviceProfile.VendorId, _selectedDeviceProfile.ProductId);
        clone.GlobalLayer = _selectedDeviceProfile.GlobalLayer.Clone();
        DeviceInputProfileEngine.SaveProfile(clone);
        PopulateDeviceProfilesDropdown();
        SelectDeviceProfileByName(newName);
        ShowOsdToast($"Создан новый профиль «{newName}»!");
    }

    private void BtnRefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        DeviceInputProfileEngine.PollDevicesAndHotplug();
        PopulateDeviceProfilesDropdown();
        PopulatePortDeviceAssignDropdown();
        LoadRemapBindings(_selectedRemapPlayerIndex);
        ShowOsdToast("Порты устройств ввода (XInput, WinMM/HID, рули, HOTAS) обновлены");
    }

    private void BtnResetAllHotkeys_Click(object sender, RoutedEventArgs e)
    {
        HotkeyManager.ResetAllToDefaults();
        DeviceInputProfileEngine.ApplyCompiledMapsToNativeHost();
        RebuildHotkeyStudioList();
        ShowOsdToast("Все горячие клавиши сброшены на стандартные!");
    }

    // ==================== 10. IN-GAME HUD & OVERLAY HANDLERS ====================
    private void BtnCollapseInGameHud_Click(object sender, RoutedEventArgs e)
    {
        if (InGameHudBorder != null) InGameHudBorder.Visibility = Visibility.Collapsed;
        if (PnlCollapsedHudRestore != null) PnlCollapsedHudRestore.Visibility = Visibility.Visible;
    }

    private void BtnRestoreInGameHud_Click(object sender, RoutedEventArgs e)
    {
        if (InGameHudBorder != null) InGameHudBorder.Visibility = Visibility.Visible;
        if (PnlCollapsedHudRestore != null) PnlCollapsedHudRestore.Visibility = Visibility.Collapsed;
    }

    private void BtnHudTogglePadOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (PnlInGamePadOverlay == null) return;

        bool show = PnlInGamePadOverlay.Visibility != Visibility.Visible;
        PnlInGamePadOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        if (show)
        {
            string style = _selectedDeviceProfile?.FormFactor.ToString() ?? "XboxGamepad";
            UpdateOverlayArtChangeOnly(style, _selectedRemapPlayerIndex, null);
            _livePadTesterTimer?.Start();
        }
    }

    private void BtnCloseInGamePadOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (PnlInGamePadOverlay != null) PnlInGamePadOverlay.Visibility = Visibility.Collapsed;
    }

    private void CmbOverlayQuickDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbOverlayQuickDevice?.SelectedItem == null) return;
        string sel = CmbOverlayQuickDevice.SelectedItem.ToString() ?? "";
        SelectDeviceProfileByName(sel);
    }

    private void BtnOverlayVirtualInput_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        string tag = btn.Tag?.ToString() ?? "";
        string style = _selectedDeviceProfile?.FormFactor.ToString() ?? "XboxGamepad";
        UpdateOverlayArtChangeOnly(style, _selectedRemapPlayerIndex, tag);
    }

    private void CmbQuickMenuDeviceProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbQuickMenuDeviceProfile?.SelectedItem == null) return;
        string sel = CmbQuickMenuDeviceProfile.SelectedItem.ToString() ?? "";
        SelectDeviceProfileByName(sel);
    }

    private void BtnQuickMenuConfigureInput_Click(object sender, RoutedEventArgs e)
    {
        if (PnlInGameQuickMenu != null) PnlInGameQuickMenu.Visibility = Visibility.Collapsed;
        NavBtn_Click(NavBtnInputRemap, e);
    }

    public void ShowDeviceHotplugToast(string title, string message)
    {
        if (BdrDeviceHotplugToast == null) return;

        if (TxtDeviceHotplugTitle != null) TxtDeviceHotplugTitle.Text = title;
        if (TxtDeviceHotplugMessage != null) TxtDeviceHotplugMessage.Text = message;

        BdrDeviceHotplugToast.Visibility = Visibility.Visible;
        _hotplugToastTimer.Stop();
        _hotplugToastTimer.Start();
    }

    // ==================== 11. HEADLESS 6-SCREENSHOT SUITE (--screenshot-210-suite) ====================
    public async Task RunScreenshot210SuiteAsync(string outDir)
    {
        string logPath = System.IO.Path.Combine(outDir, "run210.log");
        void Log(string msg) { try { File.AppendAllText(logPath, msg + "\n"); } catch { } }

        try
        {
            Log("RunScreenshot210SuiteAsync started into " + outDir);
            Directory.CreateDirectory(outDir);

            Width = 1540;
            Height = 940;
            UpdateLayout();

            void Snap(string fileName)
            {
                AnalyticalSpringEngine.FlushAllImmediately();
                UpdateLayout();
                int sw = 1540;
                int sh = 940;
                var rtb = new RenderTargetBitmap(sw, sh, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(this);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                string targetFile = System.IO.Path.Combine(outDir, fileName);
                using var fs = File.Create(targetFile);
                enc.Save(fs);
                Log("Saved " + targetFile + " (" + new FileInfo(targetFile).Length + " bytes)");
            }

            // 1. v210_dashboard.png - Dashboard showing rebalanced 4-deck sidebar, top pill, clean cards
            NavBtn_Click(NavBtnMainMenu, new RoutedEventArgs());
            if (TxtTopActiveDeviceName != null) TxtTopActiveDeviceName.Text = "Xbox Wireless";
            if (PnlOsdToast != null) PnlOsdToast.Visibility = Visibility.Collapsed;
            if (BdrDeviceHotplugToast != null) BdrDeviceHotplugToast.Visibility = Visibility.Collapsed;
            await Task.Delay(250);
            Snap("v210_dashboard.png");

            // 2. v210_devices_pad.png - ViewInputRemap Sub-Tab 1 showing Sony DualSense with core scope & remap cards
            NavBtn_Click(NavBtnInputRemap, new RoutedEventArgs());
            BtnSubTab_Click(BtnSubTabDeviceMap, new RoutedEventArgs());
            SelectDeviceProfileByName("Sony DualSense Wireless Controller");
            if (CmbDeviceCoreScope != null) CmbDeviceCoreScope.SelectedIndex = 0;
            UpdateControllerPreviewChangeOnly(InputDeviceFormFactor.PlayStationPad.ToString(), 1, "cross");
            if (PnlOsdToast != null) PnlOsdToast.Visibility = Visibility.Collapsed;
            if (BdrDeviceHotplugToast != null) BdrDeviceHotplugToast.Visibility = Visibility.Collapsed;
            await Task.Delay(250);
            Snap("v210_devices_pad.png");

            // 3. v210_devices_arcadestick.png - ViewInputRemap Sub-Tab 1 switched to Hori Fighting Stick Alpha + FBNeo
            SelectDeviceProfileByName("Hori Fighting Stick Alpha (Vewlix 8-Btn)");
            if (CmbDeviceCoreScope != null)
            {
                for (int i = 0; i < CmbDeviceCoreScope.Items.Count; i++)
                {
                    if (CmbDeviceCoreScope.Items[i]?.ToString()?.Contains("FBNeo", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        CmbDeviceCoreScope.SelectedIndex = i;
                        break;
                    }
                }
            }
            if (TxtDeviceConnBadge != null) TxtDeviceConnBadge.Text = "Активен • Автопрофиль";
            if (TxtDeviceCoreBadge != null) TxtDeviceCoreBadge.Text = "Слой: Ядро FBNeo (Аркады)";
            UpdateControllerPreviewChangeOnly(InputDeviceFormFactor.ArcadeFightStick.ToString(), 1, "b");
            await Task.Delay(250);
            Snap("v210_devices_arcadestick.png");

            // 4. v210_devices_flightstick.png - ViewInputRemap Sub-Tab 1 switched to Thrustmaster T.16000M FCS + DOSBox
            SelectDeviceProfileByName("Thrustmaster T.16000M FCS HOTAS");
            if (CmbDeviceCoreScope != null)
            {
                for (int i = 0; i < CmbDeviceCoreScope.Items.Count; i++)
                {
                    if (CmbDeviceCoreScope.Items[i]?.ToString()?.Contains("DOSBox", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        CmbDeviceCoreScope.SelectedIndex = i;
                        break;
                    }
                }
            }
            if (TxtDeviceConnBadge != null) TxtDeviceConnBadge.Text = "Активен • Автопрофиль";
            if (TxtDeviceCoreBadge != null) TxtDeviceCoreBadge.Text = "Слой: Ядро DOSBox-Pure (MS-DOS)";
            UpdateControllerPreviewChangeOnly(InputDeviceFormFactor.FlightJoystickHotas.ToString(), 1, "a");
            await Task.Delay(250);
            Snap("v210_devices_flightstick.png");

            // 5. v210_hotkeys_studio.png - ViewInputRemap Sub-Tab 2 showing the 18-action Hotkey Studio + conflicts badge
            BtnSubTab_Click(BtnSubTabHotkeysStudio, new RoutedEventArgs());
            RebuildHotkeyStudioList();
            await Task.Delay(250);
            Snap("v210_hotkeys_studio.png");

            // 6. v210_ingame_hud_overlay.png - ViewGamePlayer with 5-cluster HUD, hot-plug toast, live overlay
            if (BtnNavGamePlayer != null) NavBtn_Click(BtnNavGamePlayer, new RoutedEventArgs());
            if (ViewGamePlayer != null) ViewGamePlayer.Visibility = Visibility.Visible;
            EnsureBattletoadsRunningPreview();
            ApplyActiveBezelToViewport();

            if (TxtActiveGameTitle != null) TxtActiveGameTitle.Text = "Battletoads";
            if (TxtActiveGamePlatform != null) TxtActiveGamePlatform.Text = "Arcade";
            if (TxtActiveGameCore != null) TxtActiveGameCore.Text = "FBNeo";
            if (TxtHudActiveDeviceBadge != null) TxtHudActiveDeviceBadge.Text = "Hori FightStick • FBNeo";

            // Show Device Hot-Plug Toast
            ShowDeviceHotplugToast("Устройство активно: Hori Fighting Stick Alpha", "Автоматически применён профиль для ядра FBNeo");

            // Show Live Pad Overlay
            if (PnlInGamePadOverlay != null)
            {
                PnlInGamePadOverlay.Visibility = Visibility.Visible;
                if (TxtOverlayDeviceName != null) TxtOverlayDeviceName.Text = "Hori Fighting Stick Alpha"; if (CmbOverlayQuickDevice != null && CmbOverlayQuickDevice.Items.Count > 2) CmbOverlayQuickDevice.SelectedIndex = 2;
                if (TxtOverlayCoreBadge != null) TxtOverlayCoreBadge.Text = "Слой: FBNeo";
                UpdateOverlayArtChangeOnly(InputDeviceFormFactor.ArcadeFightStick.ToString(), 1, "b");
            }

            await Task.Delay(250);
            Snap("v210_ingame_hud_overlay.png");

            Log("RunScreenshot210SuiteAsync finished successfully!");
        }
        catch (Exception ex)
        {
            Log("RunScreenshot210SuiteAsync exception: " + ex);
        }
    }

    // Compatibility alias for legacy code-behind references
    private ComboBox? CmbControllerPlatformProfile => CmbControllerStyle;

    private BindingLayer GetSelectedEditingLayer(DeviceInputProfile profile)
    {
        string scope = CmbDeviceCoreScope?.SelectedItem?.ToString() ?? "";
        if (scope.StartsWith("Ядро:", StringComparison.OrdinalIgnoreCase))
        {
            string corePart = scope.Substring(5).Trim();
            int paren = corePart.IndexOf('(');
            if (paren > 0) corePart = corePart.Substring(0, paren).Trim();
            string key = DeviceInputProfileEngine.GetSafeDeviceId(corePart).ToLowerInvariant();
            if (!profile.CoreLayers.TryGetValue(key, out var cl))
            {
                cl = profile.GlobalLayer.Clone();
                profile.CoreLayers[key] = cl;
            }
            return cl;
        }
        return profile.GlobalLayer;
    }

    private static int ButtonNameToRetroId(string name) => name.ToLowerInvariant() switch
    {
        "b" => 0, "y" => 1, "select" => 2, "start" => 3,
        "up" => 4, "down" => 5, "left" => 6, "right" => 7,
        "a" => 8, "x" => 9, "l" or "l1" or "lb" => 10, "r" or "r1" or "rb" => 11,
        "l2" or "lt" => 12, "r2" or "rt" => 13, "l3" => 14, "r3" => 15,
        "l_x_minus" => 100, "l_x_plus" => 101,
        "l_y_minus" => 102, "l_y_plus" => 103,
        "r_x_minus" => 104, "r_x_plus" => 105,
        "r_y_minus" => 106, "r_y_plus" => 107,
        _ => -1
    };
}
