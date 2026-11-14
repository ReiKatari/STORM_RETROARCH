using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace StormRetroArch;

/// <summary>
/// STORM RETROARCH 2.7.2 Studio Integrator:
/// - 2-row studio in-game HUD toolbar with direct ComboBox dropdown controls
/// - Real-time LiveVisualEffectsEngine shader and bezel overlays
/// - Dynamic font, theme, accent, and language synchronization with 140ms ease-out transitions
/// - RetroAchievements 2.7.2 profile banner and visual card deck (unlocked full-color vs locked grayscale)
/// - Rollback Netplay relay ping, room code generation (STORM-XXXX-XXXX) and LAN discovery
/// - AI Vision Walkthrough with screenshot analysis and fast speech synthesis
/// - Streaming telemetry and FFmpeg process bridge
/// - System BIOS readiness bar and core category filtering
/// </summary>
public partial class MainWindow
{
    // ==================== 1. HUD & STUDIO 2.7.2 STATE ====================
    private bool _isStudio270Initialized = false;
    private DispatcherTimer? _streamingTelemetryTimer;
    private CancellationTokenSource? _aiAskCts;
    private CancellationTokenSource? _netplayPingCts;
    private readonly ObservableCollection<AchievementCardItem> _studioCheevosCards = new();
    private string _activeCoreFilterStatus = "ALL";

    // Compatibility TextBlock labels for older HUD references in MainWindow.xaml.cs
    private TextBlock? TxtGameMuteLabel => null;
    private TextBlock? TxtGameAspectLabel => null;
    private TextBlock? TxtGameShaderLabel => null;
    private TextBlock? TxtGameViewportScaleLabel => null;

    // ==================== 2. INITIALIZATION ====================
    public void InitializeStudio270()
    {
        if (_isStudio270Initialized) return;
        _isStudio270Initialized = true;

        // 1. Initialize In-Game HUD Dropdowns
        InitHudDropdowns();

        // 2. Subscribe to Dynamic Theme / Font / Accent Changes
        ThemeManager.ThemeChanged += OnThemeOrFontChangedStudio270;

        // 3. Setup Achievements Card ItemsSource
        if (ItemsCheevosCards != null)
        {
            ItemsCheevosCards.ItemsSource = _studioCheevosCards;
        }

        // 4. Update Window Title to 2.7.2
        Title = "STORM RETROARCH 2.7.2";
    }

    private void InitHudDropdowns()
    {
        // --- 1. Shader Preset Dropdown ---
        if (CmbHudShaderPreset != null)
        {
            CmbHudShaderPreset.Items.Clear();
            CmbHudShaderPreset.Items.Add(new ComboBoxItem { Content = "Без фильтра", Tag = "none" });
            CmbHudShaderPreset.Items.Add(new ComboBoxItem { Content = "CRT Royale", Tag = "crt-royale" });
            CmbHudShaderPreset.Items.Add(new ComboBoxItem { Content = "CRT Geom", Tag = "crt-geom" });
            CmbHudShaderPreset.Items.Add(new ComboBoxItem { Content = "CRT Lottes", Tag = "crt-lottes" });
            CmbHudShaderPreset.Items.Add(new ComboBoxItem { Content = "LCD Grid", Tag = "lcd-grid" });
            CmbHudShaderPreset.Items.Add(new ComboBoxItem { Content = "xBRZ 4x", Tag = "xbrz-4x" });
            CmbHudShaderPreset.Items.Add(new ComboBoxItem { Content = "VHS / NTSC", Tag = "vhs-ntsc" });
            CmbHudShaderPreset.Items.Add(new ComboBoxItem { Content = "Scanlines Subtle", Tag = "scanlines-subtle" });
            CmbHudShaderPreset.SelectedIndex = 0;
            CmbHudShaderPreset.SelectionChanged += CmbHudShaderPreset_SelectionChanged;
        }

        // --- 2. Bezel Preset Dropdown ---
        if (CmbHudBezelPreset != null)
        {
            CmbHudBezelPreset.Items.Clear();
            CmbHudBezelPreset.Items.Add(new ComboBoxItem { Content = "Без рамки", Tag = "none" });
            CmbHudBezelPreset.Items.Add(new ComboBoxItem { Content = "Arcade Neon", Tag = "arcade-neon" });
            CmbHudBezelPreset.Items.Add(new ComboBoxItem { Content = "Sony CRT", Tag = "crt-sony" });
            CmbHudBezelPreset.Items.Add(new ComboBoxItem { Content = "Handheld GBA", Tag = "handheld-gba" });
            CmbHudBezelPreset.Items.Add(new ComboBoxItem { Content = "SNES Classic", Tag = "snes-classic" });
            CmbHudBezelPreset.Items.Add(new ComboBoxItem { Content = "Mega Drive", Tag = "megadrive-crt" });
            CmbHudBezelPreset.Items.Add(new ComboBoxItem { Content = "STORM Studio", Tag = "storm-studio" });

            try
            {
                var discovered = BezelManager.ScanBezels();
                foreach (var b in discovered.Take(12))
                {
                    if (!string.IsNullOrWhiteSpace(b.Name) &&
                        !CmbHudBezelPreset.Items.OfType<ComboBoxItem>().Any(x => x.Content?.ToString() == b.Name))
                    {
                        CmbHudBezelPreset.Items.Add(new ComboBoxItem { Content = b.Name, Tag = b.ImagePath });
                    }
                }
            }
            catch { }

            CmbHudBezelPreset.SelectedIndex = 0;
            CmbHudBezelPreset.SelectionChanged += CmbHudBezelPreset_SelectionChanged;
        }

        // --- 3. Internal Resolution Dropdown ---
        if (CmbHudInternalRes != null)
        {
            CmbHudInternalRes.Items.Clear();
            CmbHudInternalRes.Items.Add(new ComboBoxItem { Content = "1x (Нативное)", Tag = "1" });
            CmbHudInternalRes.Items.Add(new ComboBoxItem { Content = "2x", Tag = "2" });
            CmbHudInternalRes.Items.Add(new ComboBoxItem { Content = "3x", Tag = "3" });
            CmbHudInternalRes.Items.Add(new ComboBoxItem { Content = "4x", Tag = "4" });
            CmbHudInternalRes.Items.Add(new ComboBoxItem { Content = "6x", Tag = "6" });
            CmbHudInternalRes.Items.Add(new ComboBoxItem { Content = "8x", Tag = "8" });
            CmbHudInternalRes.SelectedIndex = 0;
            CmbHudInternalRes.SelectionChanged += CmbHudInternalRes_SelectionChanged;
        }

        // --- 4. Aspect Ratio Dropdown ---
        if (CmbHudAspectRatio != null)
        {
            CmbHudAspectRatio.Items.Clear();
            CmbHudAspectRatio.Items.Add(new ComboBoxItem { Content = "4:3", Tag = "0" });
            CmbHudAspectRatio.Items.Add(new ComboBoxItem { Content = "16:9", Tag = "1" });
            CmbHudAspectRatio.Items.Add(new ComboBoxItem { Content = "1:1", Tag = "2" });
            CmbHudAspectRatio.Items.Add(new ComboBoxItem { Content = "Пиксель 1:1", Tag = "3" });
            CmbHudAspectRatio.Items.Add(new ComboBoxItem { Content = "На весь экран", Tag = "4" });
            CmbHudAspectRatio.SelectedIndex = 0;
            CmbHudAspectRatio.SelectionChanged += CmbHudAspectRatio_SelectionChanged;
        }

        // --- 5. Viewport Scale Dropdown ---
        if (CmbHudViewportScale != null)
        {
            CmbHudViewportScale.Items.Clear();
            CmbHudViewportScale.Items.Add(new ComboBoxItem { Content = "Авто", Tag = "0" });
            CmbHudViewportScale.Items.Add(new ComboBoxItem { Content = "Целые числа", Tag = "1" });
            CmbHudViewportScale.Items.Add(new ComboBoxItem { Content = "100%", Tag = "2" });
            CmbHudViewportScale.Items.Add(new ComboBoxItem { Content = "Увел. (Crop)", Tag = "3" });
            CmbHudViewportScale.SelectedIndex = 0;
            CmbHudViewportScale.SelectionChanged += CmbHudViewportScale_SelectionChanged;
        }

        // --- 6. GPU Backend Dropdown ---
        if (CmbHudGpuBackend != null)
        {
            CmbHudGpuBackend.Items.Clear();
            CmbHudGpuBackend.Items.Add(new ComboBoxItem { Content = "Авто", Tag = "Auto" });
            CmbHudGpuBackend.Items.Add(new ComboBoxItem { Content = "Vulkan", Tag = "Vulkan" });
            CmbHudGpuBackend.Items.Add(new ComboBoxItem { Content = "D3D11", Tag = "D3D11" });
            CmbHudGpuBackend.Items.Add(new ComboBoxItem { Content = "OpenGL", Tag = "OpenGL" });
            CmbHudGpuBackend.SelectedIndex = 0;
            CmbHudGpuBackend.SelectionChanged += CmbHudGpuBackend_SelectionChanged;
        }

        // --- 7. Audio Profile Dropdown ---
        if (CmbHudAudioProfile != null)
        {
            CmbHudAudioProfile.Items.Clear();
            CmbHudAudioProfile.Items.Add(new ComboBoxItem { Content = "Обычный", Tag = "normal" });
            CmbHudAudioProfile.Items.Add(new ComboBoxItem { Content = "Тихо (50%)", Tag = "quiet" });
            CmbHudAudioProfile.Items.Add(new ComboBoxItem { Content = "Выкл (Mute)", Tag = "mute" });
            CmbHudAudioProfile.SelectedIndex = 0;
            CmbHudAudioProfile.SelectionChanged += CmbHudAudioProfile_SelectionChanged;
        }

        // --- 8. Save Slot Dropdown ---
        if (CmbHudSaveSlot != null)
        {
            CmbHudSaveSlot.Items.Clear();
            for (int i = 0; i <= 99; i++)
            {
                CmbHudSaveSlot.Items.Add(i.ToString());
            }
            CmbHudSaveSlot.SelectedIndex = 0;
            CmbHudSaveSlot.SelectionChanged += (s, e) =>
            {
                if (CmbHudSaveSlot.SelectedIndex >= 0)
                {
                    if (CmbStateSlot != null) CmbStateSlot.SelectedIndex = CmbHudSaveSlot.SelectedIndex;
                    if (CmbInGameStateSlot != null && CmbInGameStateSlot.SelectedIndex != CmbHudSaveSlot.SelectedIndex)
                    {
                        CmbInGameStateSlot.SelectedIndex = CmbHudSaveSlot.SelectedIndex;
                    }
                }
            };
        }
    }

    // ==================== 3. HUD EVENT HANDLERS ====================
    private void CmbHudShaderPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbHudShaderPreset?.SelectedItem is not ComboBoxItem item) return;
        string tag = item.Tag?.ToString() ?? "none";
        ApplyShaderPresetStudio270(tag, item.Content?.ToString() ?? tag);
    }

    public void ApplyShaderPresetStudio270(string presetId, string displayName)
    {
        string canon = LiveVisualEffectsEngine.NormalizeShaderId(presetId);
        if (GrdLiveShaderOverlay != null)
        {
            LiveVisualEffectsEngine.ApplyLiveShaderOverlay(GrdLiveShaderOverlay, canon);
        }

        // Real-time synchronization with libretro host filter
        if (canon == "none")
        {
            NativeLibretroHost.Current.ApplyShaderStudioPreset("none");
            ShowOsdToast("Видеофильтр: Без фильтра");
        }
        else
        {
            NativeLibretroHost.Current.ApplyShaderStudioPreset(canon);
            ShowOsdToast($"Видеофильтр: {displayName}");
        }

        if (CmbHudShaderPreset != null)
        {
            for (int i = 0; i < CmbHudShaderPreset.Items.Count; i++)
            {
                if (CmbHudShaderPreset.Items[i] is ComboBoxItem cbi &&
                    (cbi.Tag?.ToString()?.Equals(canon, StringComparison.OrdinalIgnoreCase) == true ||
                     cbi.Tag?.ToString()?.Equals(presetId, StringComparison.OrdinalIgnoreCase) == true))
                {
                    if (CmbHudShaderPreset.SelectedIndex != i)
                    {
                        CmbHudShaderPreset.SelectedIndex = i;
                    }
                    break;
                }
            }
        }
    }

    private void CmbHudBezelPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbHudBezelPreset?.SelectedItem is not ComboBoxItem item) return;
        string tag = item.Tag?.ToString() ?? "none";
        ApplyBezelPresetStudio270(tag, item.Content?.ToString() ?? tag);
    }

    public void ApplyBezelPresetStudio270(string presetOrPath, string displayName)
    {
        string canon = LiveVisualEffectsEngine.NormalizeBezelId(presetOrPath);
        if (canon == "none")
        {
            if (ImgGameBezelOverlay != null)
            {
                ImgGameBezelOverlay.Source = null;
                ImgGameBezelOverlay.Visibility = Visibility.Collapsed;
            }
            if (ImgGameViewport != null)
            {
                ImgGameViewport.Margin = new Thickness(0);
            }
            BezelManager.DisableBezel();
            ShowOsdToast("Экранная рамка: Без рамки");
        }
        else
        {
            var bmp = LiveVisualEffectsEngine.RenderBezelFrameBitmap(canon, 1920, 1080, false);
            if (ImgGameBezelOverlay != null)
            {
                ImgGameBezelOverlay.Source = bmp;
                ImgGameBezelOverlay.Opacity = 1.0;
                ImgGameBezelOverlay.Visibility = Visibility.Visible;
            }
            if (ImgGameViewport != null)
            {
                ImgGameViewport.Margin = new Thickness(96, 18, 96, 18);
            }
            ShowOsdToast($"Экранная рамка: {displayName}");
        }

        if (CmbHudBezelPreset != null)
        {
            for (int i = 0; i < CmbHudBezelPreset.Items.Count; i++)
            {
                if (CmbHudBezelPreset.Items[i] is ComboBoxItem cbi &&
                    (cbi.Tag?.ToString()?.Equals(canon, StringComparison.OrdinalIgnoreCase) == true ||
                     cbi.Tag?.ToString()?.Equals(presetOrPath, StringComparison.OrdinalIgnoreCase) == true))
                {
                    if (CmbHudBezelPreset.SelectedIndex != i)
                    {
                        CmbHudBezelPreset.SelectedIndex = i;
                    }
                    break;
                }
            }
        }
    }

    private void CmbHudInternalRes_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbHudInternalRes?.SelectedItem is not ComboBoxItem item) return;
        ShowOsdToast($"Внутреннее разрешение: {item.Content}");
    }

    private void CmbHudAspectRatio_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbHudAspectRatio?.SelectedItem is not ComboBoxItem item) return;
        int mode = int.TryParse(item.Tag?.ToString(), out int m) ? m : 0;
        NativeLibretroHost.Current.AspectMode = mode;
        NativeLibretroHost.Current.ApplyAspectMode(GameViewportContainer);
        UpdateViewportStageLayout();
        ShowOsdToast($"Формат экрана: {item.Content}");
    }

    private void CmbHudViewportScale_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbHudViewportScale?.SelectedItem is not ComboBoxItem item) return;
        int mode = int.TryParse(item.Tag?.ToString(), out int m) ? m : 0;
        _viewportScaleMode = mode;
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
                double factor = _viewportScaleMode switch
                {
                    1 => 1.0,
                    2 => 2.0,
                    3 => 3.0,
                    _ => 1.0
                };
                ImgGameViewport.Stretch = Stretch.Fill;
                ImgGameViewport.Width = 320 * factor;
                ImgGameViewport.Height = 240 * factor;
            }
        }
        ShowOsdToast($"Масштаб окна: {item.Content}");
    }

    private void CmbHudGpuBackend_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbHudGpuBackend?.SelectedItem is not ComboBoxItem item) return;
        string backend = item.Tag?.ToString() ?? "Auto";
        if (TxtHudGpuBackendBadge != null) TxtHudGpuBackendBadge.Text = backend.ToUpperInvariant();
        ShowOsdToast($"GPU Backend: {backend}");
    }

    private void CmbHudAudioProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbHudAudioProfile?.SelectedItem is not ComboBoxItem item) return;
        string profile = item.Tag?.ToString() ?? "normal";
        switch (profile)
        {
            case "mute":
                if (!NativeLibretroHost.Current.IsAudioMuted) NativeLibretroHost.Current.ToggleMute();
                ShowOsdToast("Звук: Выключен (Mute)");
                break;
            case "quiet":
                if (NativeLibretroHost.Current.IsAudioMuted) NativeLibretroHost.Current.ToggleMute();
                ShowOsdToast("Звук: Тихо (50%)");
                break;
            case "normal":
            default:
                if (NativeLibretroHost.Current.IsAudioMuted) NativeLibretroHost.Current.ToggleMute();
                ShowOsdToast("Звук: Обычный (100%)");
                break;
        }
    }

    // ==================== 4. DYNAMIC THEME, FONT & TRANSITION ====================
    private void OnThemeOrFontChangedStudio270()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            try
            {
                if (Application.Current.Resources["AppFontFamily"] is FontFamily ff)
                {
                    this.FontFamily = ff;
                }
                ApplySmoothTransitionAnimation(this, 140);
                this.InvalidateVisual();
            }
            catch { }
        });
    }

    public static void ApplySmoothTransitionAnimation(UIElement element, double durationMs = 140)
    {
        if (element == null) return;
        var anim = new DoubleAnimation
        {
            From = 0.82,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        element.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    // ==================== 5. ACHIEVEMENTS STUDIO 2.7.2 ====================
    public async Task RefreshAchievementsStudio270Async()
    {
        try
        {
            var profile = await AchievementsManager.FetchProfileAsync();
            if (profile != null)
            {
                if (TxtCheevosConnectionStatus != null)
                {
                    TxtCheevosConnectionStatus.Text = profile.IsConnected ? "Подключено" : "Не настроено";
                }
                if (BdrCheevosConnectionStatus != null)
                {
                    BdrCheevosConnectionStatus.Background = profile.IsConnected ?
                        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A10B981")) :
                        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1AF59E0B"));
                    BdrCheevosConnectionStatus.BorderBrush = profile.IsConnected ?
                        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")) :
                        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                }
                if (TxtCheevosProfileUsername != null) TxtCheevosProfileUsername.Text = profile.Username;
                if (TxtCheevosPoints != null) TxtCheevosPoints.Text = $"{profile.TotalPoints} Очков ({profile.SoftcorePoints} Softcore)";
                if (TxtCheevosRank != null) TxtCheevosRank.Text = profile.RankText;

                // User avatar
                if (ImgCheevosUserAvatar != null && !string.IsNullOrWhiteSpace(profile.AvatarUrl))
                {
                    try
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(profile.AvatarUrl);
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.EndInit();
                        ImgCheevosUserAvatar.Source = bi;
                    }
                    catch { }
                }

                // Fetch Game Achievements Cards
                string? gameTitle = _selectedSplitGame?.Title ?? TxtActiveGameTitle?.Text ?? "Классическая ретро-игра";
                var gameRes = await AchievementsManager.FetchGameAchievementsAsync(gameTitle);
                _studioCheevosCards.Clear();
                foreach (var card in gameRes.Cards)
                {
                    _studioCheevosCards.Add(card);
                }

                if (PbCheevosProgress != null && gameRes.TotalGamePoints > 0)
                {
                    PbCheevosProgress.Maximum = gameRes.TotalGamePoints;
                    PbCheevosProgress.Value = gameRes.EarnedPoints;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AchievementsStudio270] Refresh error: {ex.Message}");
        }
    }

    // ==================== 6. NETPLAY STUDIO 2.7.2 ====================
    private async void BtnNetplayPingRelay_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (BtnNetplayPingRelay != null) BtnNetplayPingRelay.IsEnabled = false;
            if (TxtNetplayPingResult != null) TxtNetplayPingResult.Text = "Замер пинга...";

            string host = TxtNetplayJoinIp?.Text?.Trim() ?? "relay.retroarch.com";
            if (string.IsNullOrWhiteSpace(host)) host = "relay.retroarch.com";
            int port = int.TryParse(TxtNetplayJoinPort?.Text, out int p) ? p : 55435;

            _netplayPingCts?.Cancel();
            _netplayPingCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

            int pingMs = await RollbackNetplayEngine.MeasureRelayLatencyAsync(host, port, _netplayPingCts.Token);
            if (TxtNetplayPingResult != null)
            {
                TxtNetplayPingResult.Text = pingMs >= 0 ? $"{pingMs} мс" : "Таймаут (Недоступен)";
                TxtNetplayPingResult.Foreground = pingMs >= 0 && pingMs < 80 ?
                    new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")) :
                    new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            }
        }
        catch (Exception ex)
        {
            if (TxtNetplayPingResult != null) TxtNetplayPingResult.Text = $"Ошибка: {ex.Message}";
        }
        finally
        {
            if (BtnNetplayPingRelay != null) BtnNetplayPingRelay.IsEnabled = true;
        }
    }

    private void BtnNetplayGenRoomCode_Click(object sender, RoutedEventArgs e)
    {
        string host = "127.0.0.1";
        int port = int.TryParse(TxtNetplayHostPort?.Text, out int p) ? p : 55435;
        string code = RollbackNetplayEngine.GenerateRoomCode(host, port);
        if (TxtNetplayRoomCode != null)
        {
            TxtNetplayRoomCode.Text = code;
        }
        try { Clipboard.SetText(code); } catch { }
        ShowOsdToast($"Код комнаты скопирован: {code}");
    }

    private void BtnNetplayJoinRoomCode_Click(object sender, RoutedEventArgs e)
    {
        string code = TxtNetplayRoomCode?.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(code) || code == "STORM-XXXX-XXXX")
        {
            ShowNotification("Сначала сгенерируйте или введите код комнаты");
            return;
        }
        var parsed = RollbackNetplayEngine.ParseRoomCode(code);
        if (parsed.HasValue)
        {
            if (TxtNetplayJoinIp != null) TxtNetplayJoinIp.Text = parsed.Value.Host;
            if (TxtNetplayJoinPort != null) TxtNetplayJoinPort.Text = parsed.Value.Port.ToString();
            ShowOsdToast($"Комната найдена: {parsed.Value.Host}:{parsed.Value.Port}");
        }
        else
        {
            ShowNotification("Неверный формат кода комнаты");
        }
    }

    private void SliderNetplayRollbackFrames_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int frames = (int)Math.Round(e.NewValue);
        if (TxtNetplayRollbackVal != null)
        {
            TxtNetplayRollbackVal.Text = $"{frames} кадр.";
        }
    }

    // ==================== 7. AI GUIDE STUDIO 2.7.2 ====================
    private async void BtnAiAskQuestion_Click(object sender, RoutedEventArgs e)
    {
        string question = TxtAiPrompt?.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(question))
        {
            ShowNotification("Введите вопрос для ИИ-помощника");
            return;
        }

        if (TxtAiStatusBadge != null) TxtAiStatusBadge.Text = "Анализ кадра...";

        try
        {
            string gameTitle = _selectedSplitGame?.Title ?? TxtActiveGameTitle?.Text ?? "Классическая ретро-игра";
            string? screenshotPath = null;
            if (ChkAiAttachScreenshot?.IsChecked == true)
            {
                screenshotPath = AiWalkthroughManager.GetAvailableScreenshots().FirstOrDefault();
            }

            string provider = AiWalkthroughManager.Config.Provider;
            string apiKey = AiWalkthroughManager.Config.ApiKey;
            string model = AiWalkthroughManager.Config.ModelName;

            _aiAskCts?.Cancel();
            _aiAskCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));

            string answer = await AiWalkthroughManager.AskWithScreenshotAsync(
                gameTitle, question, screenshotPath, provider, apiKey, model, _aiAskCts.Token);

            if (TxtAiResult != null)
            {
                TxtAiResult.Text = answer;
            }
            if (TxtAiStatusBadge != null) TxtAiStatusBadge.Text = "Ответ готов";
        }
        catch (Exception ex)
        {
            if (TxtAiResult != null) TxtAiResult.Text = $"Ошибка запроса к ИИ: {ex.Message}";
            if (TxtAiStatusBadge != null) TxtAiStatusBadge.Text = "Ошибка";
        }
    }

    private async void BtnAiSpeak_Click(object sender, RoutedEventArgs e)
    {
        string text = TxtAiResult?.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text)) return;
        await AiWalkthroughManager.SpeakTextFastAsync(text);
    }

    private void BtnAiStopSpeaking_Click(object sender, RoutedEventArgs e)
    {
        AiWalkthroughManager.StopSpeaking();
        ShowOsdToast("Озвучивание остановлено");
    }

    // ==================== 8. STREAMING & TELEMETRY STUDIO 2.7.2 ====================
    private async void BtnStreamCheckServer_Click(object sender, RoutedEventArgs e)
    {
        string target = (CmbStreamPlatform?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "127.0.0.1";

        if (BtnStreamCheckServer != null) BtnStreamCheckServer.IsEnabled = false;
        try
        {
            var res = await StreamAndAiManager.TestServerReachabilityAsync(target);
            if (TxtStreamServerStatus != null)
            {
                TxtStreamServerStatus.Text = res.Ok ? $"Доступен ({res.LatencyMs} мс)" : $"Недоступен ({res.Message})";
                TxtStreamServerStatus.Foreground = res.Ok ?
                    new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")) :
                    new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FB7185"));
            }
        }
        finally
        {
            if (BtnStreamCheckServer != null) BtnStreamCheckServer.IsEnabled = true;
        }
    }

    private async void BtnStreamStartReal_Click(object sender, RoutedEventArgs e)
    {
        string target = (CmbStreamPlatform?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "rtmp://live.twitch.tv/app/";
        string key = TxtStreamKey?.Text?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(key)) target = $"{target.TrimEnd('/')}/{key}";

        int bitrate = 4500;
        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

        bool started = await StreamAndAiManager.StartRealStreamAsync(target, bitrate, "fast", hwnd);
        if (started)
        {
            StartStreamTelemetryPolling();
            ShowOsdToast("Трансляция запущена!");
        }
        else
        {
            ShowNotification("Не удалось запустить трансляцию FFmpeg");
        }
    }

    private void BtnStreamStopReal_Click(object sender, RoutedEventArgs e)
    {
        StreamAndAiManager.StopRealStream();
        _streamingTelemetryTimer?.Stop();
        ShowOsdToast("Трансляция остановлена");
    }

    private void StartStreamTelemetryPolling()
    {
        _streamingTelemetryTimer?.Stop();
        _streamingTelemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _streamingTelemetryTimer.Tick += (s, e) =>
        {
            var snap = StreamAndAiManager.GetTelemetrySnapshot();
            if (TxtStreamTelemetryBitrate != null) TxtStreamTelemetryBitrate.Text = $"{snap.BitrateKbps} Кбит/с";
            if (TxtStreamTelemetryFps != null) TxtStreamTelemetryFps.Text = $"{snap.Fps:F1} FPS";
            if (TxtStreamTelemetryUptime != null) TxtStreamTelemetryUptime.Text = snap.Uptime.ToString(@"hh\:mm\:ss");
            if (TxtStreamTelemetryFfmpeg != null) TxtStreamTelemetryFfmpeg.Text = snap.IsStreaming ? "Активен" : "Остановлен";
        };
        _streamingTelemetryTimer.Start();
    }

    // ==================== 9. BIOS STUDIO 2.7.2 ====================
    public void RefreshBiosStudio270()
    {
        try
        {
            BiosManager.CheckAllBios();
            var biosList = BiosManager.Database;
            int readyCount = biosList.Count(x => x.IsFound);
            int totalCount = biosList.Count;
            int pct = totalCount > 0 ? (readyCount * 100 / totalCount) : 0;

            if (PbBiosReadiness != null)
            {
                PbBiosReadiness.Maximum = totalCount > 0 ? totalCount : 100;
                PbBiosReadiness.Value = readyCount;
            }
            if (TxtBiosReadinessLabel != null)
            {
                TxtBiosReadinessLabel.Text = $"Готово: {readyCount} / {totalCount} ({pct}%)";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BiosStudio270] Error: {ex.Message}");
        }
    }

    private void BtnScanBiosQuick_Click(object sender, RoutedEventArgs e)
    {
        RefreshBiosStudio270();
        ShowOsdToast("BIOS сканирование завершено");
    }

    // ==================== 10. CORES FILTER PILLS ====================
    private void BtnCoreStatusAll_Click(object sender, RoutedEventArgs e)
    {
        _activeCoreFilterStatus = "ALL";
        ApplyCoreFilterStudio270();
    }

    private void BtnCoreStatusInstalled_Click(object sender, RoutedEventArgs e)
    {
        _activeCoreFilterStatus = "INSTALLED";
        ApplyCoreFilterStudio270();
    }

    private void BtnCoreStatusRecommended_Click(object sender, RoutedEventArgs e)
    {
        _activeCoreFilterStatus = "RECOMMENDED";
        if (BtnCoresFilterAll != null) BtnCoresFilterAll.Tag = null;
        if (BtnCoresFilterInstalled != null) BtnCoresFilterInstalled.Tag = null;
        if (BtnCoresFilterAvailable != null) BtnCoresFilterAvailable.Tag = null;
        ApplyCoreFilterStudio270();
    }

    private void ApplyCoreFilterStudio270()
    {
        if (BtnCoreStatusRecommended != null) BtnCoreStatusRecommended.Tag = _activeCoreFilterStatus == "RECOMMENDED" ? "Active" : null;
        RefreshCoresUi();
    }
}
