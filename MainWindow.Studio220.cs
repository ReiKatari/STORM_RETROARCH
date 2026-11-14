using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace StormRetroArch;

/// <summary>
/// Phase 2.7.3 Studio UI, Hardware GPU Engine, Universal Bezel and Mega Bezel Integrator.
/// </summary>
public partial class MainWindow
{
    private CancellationTokenSource? _megaBezelInstallCts;

    // ==================== 1. INITIALIZATION & SUBSYSTEM SYNC ====================

    public void InitStudio220Subsystem()
    {
        LogCheckpoint("InitStudio220Subsystem start");

        // 1. Ensure system/ BIOS folder structure and heal misplaced files
        try
        {
            BiosManager.EnsureStandardBiosSubfolders();
            BiosManager.AutoHealMisplacedBiosFiles();
        }
        catch (Exception ex)
        {
            LogCheckpoint("InitStudio220Subsystem BIOS subfolders warning: " + ex.Message);
        }

        // 2. Sync UI controls with SettingsManager
        if (ChkAutoDownloadCoreOnLaunch != null)
        {
            ChkAutoDownloadCoreOnLaunch.IsChecked = SettingsManager.Current.AutoDownloadMissingCoreOnLaunch;
        }

        if (CmbGlobalGraphicsBackend != null)
        {
            string currentBackend = SettingsManager.Current.GraphicsBackend ?? "Auto";
            for (int i = 0; i < CmbGlobalGraphicsBackend.Items.Count; i++)
            {
                if (CmbGlobalGraphicsBackend.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag?.ToString(), currentBackend, StringComparison.OrdinalIgnoreCase))
                {
                    CmbGlobalGraphicsBackend.SelectedIndex = i;
                    break;
                }
            }
        }

        if (ChkUniversalCoreBezel != null)
        {
            ChkUniversalCoreBezel.IsChecked = SettingsManager.Current.UniversalBezelForAllCores;
        }

        UpdateHudGpuBackendBadge();
        RefreshMegaBezelHubStatus();

        // 3. Subscribe to external GPU engine events
        VulkanD3dEngineBridge.Current.SessionEnded += () =>
        {
            Dispatcher.Invoke(() =>
            {
                if (HwEngineGameHost != null) HwEngineGameHost.Visibility = Visibility.Collapsed;
                if (VbGameStage != null) VbGameStage.Visibility = Visibility.Visible;
                if (PnlGameLoadingOverlay != null) PnlGameLoadingOverlay.Visibility = Visibility.Collapsed;
                UpdateInGameHudButtons();
            });
        };

        LogCheckpoint("InitStudio220Subsystem complete");
    }

    private void UpdateHudGpuBackendBadge()
    {
        if (TxtHudGpuBackendBadge == null) return;
        string backend = SettingsManager.Current.GraphicsBackend ?? "Auto";
        TxtHudGpuBackendBadge.Text = backend switch
        {
            "Vulkan" => "GPU: Vulkan",
            "Direct3D12" => "GPU: D3D12",
            "Direct3D11" => "GPU: D3D11",
            "OpenGLCore" => "GPU: GL Core",
            "NativeAVX2" => "GPU: AVX2",
            _ => "GPU: Авто"
        };
    }

    // ==================== 2. CORES & GRAPHICS BACKEND HANDLERS ====================

    private void ChkAutoDownloadCoreOnLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (ChkAutoDownloadCoreOnLaunch == null) return;
        SettingsManager.Current.AutoDownloadMissingCoreOnLaunch = ChkAutoDownloadCoreOnLaunch.IsChecked == true;
        SettingsManager.Save();
        ShowOsdToast(SettingsManager.Current.AutoDownloadMissingCoreOnLaunch
            ? "Автозагрузка ядер: ВКЛ"
            : "Автозагрузка ядер: ВЫКЛ");
    }

    private void CmbGlobalGraphicsBackend_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || CmbGlobalGraphicsBackend?.SelectedItem is not ComboBoxItem item) return;

        string backend = item.Tag?.ToString() ?? "Auto";
        SettingsManager.Current.GraphicsBackend = backend;
        SettingsManager.Save();
        UpdateHudGpuBackendBadge();
        ShowOsdToast($"Графический движок: {backend}");
    }

    private void BtnHudCycleGpuBackend_Click(object sender, RoutedEventArgs e)
    {
        string current = SettingsManager.Current.GraphicsBackend ?? "Auto";
        string next = current switch
        {
            "Auto" => "Vulkan",
            "Vulkan" => "Direct3D12",
            "Direct3D12" => "Direct3D11",
            "Direct3D11" => "OpenGLCore",
            "OpenGLCore" => "NativeAVX2",
            _ => "Auto"
        };

        SettingsManager.Current.GraphicsBackend = next;
        SettingsManager.Save();

        if (CmbGlobalGraphicsBackend != null)
        {
            for (int i = 0; i < CmbGlobalGraphicsBackend.Items.Count; i++)
            {
                if (CmbGlobalGraphicsBackend.Items[i] is ComboBoxItem cbi &&
                    string.Equals(cbi.Tag?.ToString(), next, StringComparison.OrdinalIgnoreCase))
                {
                    CmbGlobalGraphicsBackend.SelectedIndex = i;
                    break;
                }
            }
        }

        UpdateHudGpuBackendBadge();
        ShowOsdToast($"Графический движок: {next}");
    }

    // ==================== 3. BIOS SUBFOLDERS & FIRMWARE TOOLBAR ====================

    private async void BtnImportBiosPack_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите архив с файлами BIOS (ZIP, 7Z, TAR) или системную прошивку",
            Filter = "Архивы BIOS и прошивки (*.zip;*.7z;*.tar;*.bin)|*.zip;*.7z;*.tar;*.bin|Все файлы (*.*)|*.*",
            Multiselect = false
        };

        if (dlg.ShowDialog() != true) return;

        ShowOsdToast("Импорт файлов BIOS из архива...");
        try
        {
            var result = await BiosManager.ImportBiosPackAsync(dlg.FileName);
            LoadBios();
            ShowNotification("Импорт BIOS завершён", result.Summary);
        }
        catch (Exception ex)
        {
            ShowNotification("Ошибка импорта BIOS", ex.Message);
        }
    }

    private void BtnEnsureBiosSubfolders_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BiosManager.EnsureStandardBiosSubfolders();
            int healed = BiosManager.AutoHealMisplacedBiosFiles();
            LoadBios();
            ShowNotification("Структура system/ проверена",
                $"Созданы стандартные подпапки (pcsx2, rpcs3, vita3k, dc, xemu). Перемещено файлов: {healed}");
        }
        catch (Exception ex)
        {
            ShowNotification("Ошибка проверки структуры BIOS", ex.Message);
        }
    }

    private async void BtnDownloadOpenBiosDb_Click(object sender, RoutedEventArgs e)
    {
        ShowOsdToast("Загрузка базы совместимости PCSX2 GameIndex.yaml...");
        try
        {
            bool ok = await BiosManager.DownloadOpenFirmwareResourcesAsync(status =>
            {
                Dispatcher.Invoke(() => ShowOsdToast(status));
            });
            LoadBios();
            ShowNotification("База данных GameIndex.yaml",
                ok ? "Файл pcsx2/resources/GameIndex.yaml успешно загружен!" : "Не удалось загрузить GameIndex.yaml");
        }
        catch (Exception ex)
        {
            ShowNotification("Ошибка загрузки GameIndex.yaml", ex.Message);
        }
    }

    // ==================== 4. MEGA BEZEL ONLINE HUB & MANAGEMENT ====================

    public void RefreshMegaBezelHubStatus()
    {
        try
        {
            var hsm = MegaBezelPackManager.GetPackStatus("hsm_mega_bezel");
            if (TxtHsmMegaBezelStatus != null)
            {
                TxtHsmMegaBezelStatus.Text = hsm.IsInstalled ? $"Установлен ({hsm.PresetCount})" : "Не установлен (~15 МБ)";
                TxtHsmMegaBezelStatus.Foreground = hsm.IsInstalled
                    ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                    : new SolidColorBrush(Color.FromRgb(56, 189, 248));
            }

            var duimon = MegaBezelPackManager.GetPackStatus("duimon_mega_bezel");
            if (TxtDuimonMegaBezelStatus != null)
            {
                TxtDuimonMegaBezelStatus.Text = duimon.IsInstalled ? $"Установлен ({duimon.PresetCount})" : "Не установлен (~910 МБ)";
                TxtDuimonMegaBezelStatus.Foreground = duimon.IsInstalled
                    ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                    : new SolidColorBrush(Color.FromRgb(167, 139, 250));
            }

            var potato = MegaBezelPackManager.GetPackStatus("duimon_potato");
            if (TxtDuimonPotatoStatus != null)
            {
                TxtDuimonPotatoStatus.Text = potato.IsInstalled ? $"Установлен ({potato.PresetCount})" : "Не установлен (~180 МБ)";
                TxtDuimonPotatoStatus.Foreground = potato.IsInstalled
                    ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                    : new SolidColorBrush(Color.FromRgb(52, 211, 153));
            }
        }
        catch (Exception ex)
        {
            LogCheckpoint("RefreshMegaBezelHubStatus warning: " + ex.Message);
        }
    }

    private async void BtnDownloadMegaBezelPack_Click(object sender, RoutedEventArgs e)
    {
        string packId = (sender as FrameworkElement)?.Tag?.ToString() ?? "hsm_mega_bezel";

        if (PnlMegaBezelDownloadBar != null) PnlMegaBezelDownloadBar.Visibility = Visibility.Visible;
        if (PbMegaBezelInstall != null) PbMegaBezelInstall.Value = 0;
        if (TxtMegaBezelInstallProgress != null) TxtMegaBezelInstallProgress.Text = "Подключение к репозиторию GitHub...";

        _megaBezelInstallCts = new CancellationTokenSource();

        var progress = new Progress<double>(pct =>
        {
            if (PbMegaBezelInstall != null) PbMegaBezelInstall.Value = pct;
        });

        Action<string> onStatus = msg =>
        {
            Dispatcher.Invoke(() =>
            {
                if (TxtMegaBezelInstallProgress != null) TxtMegaBezelInstallProgress.Text = msg;
            });
        };

        try
        {
            var (ok, msg) = await MegaBezelPackManager.InstallPackOnlineAsync(
                packId, progress, onStatus, _megaBezelInstallCts.Token);

            RefreshMegaBezelHubStatus();
            await LoadBezelsAsync(forceReload: true);
            ShowNotification(ok ? "Mega Bezel установлен" : "Ошибка установки Mega Bezel", msg);
        }
        catch (OperationCanceledException)
        {
            ShowNotification("Mega Bezel", "Загрузка пакета отменена пользователем.");
        }
        catch (Exception ex)
        {
            ShowNotification("Ошибка установки Mega Bezel", ex.Message);
        }
        finally
        {
            if (PnlMegaBezelDownloadBar != null) PnlMegaBezelDownloadBar.Visibility = Visibility.Collapsed;
            _megaBezelInstallCts?.Dispose();
            _megaBezelInstallCts = null;
        }
    }

    private void BtnCancelMegaBezelInstall_Click(object sender, RoutedEventArgs e)
    {
        _megaBezelInstallCts?.Cancel();
        if (TxtMegaBezelInstallProgress != null) TxtMegaBezelInstallProgress.Text = "Отмена загрузки...";
    }

    private async void BtnValidateMegaBezel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var report = MegaBezelPackManager.ValidateAndRepairStructure();
            if (TxtMegaBezelValidationSummary != null)
                TxtMegaBezelValidationSummary.Text = report.SummaryMessage;
            RefreshMegaBezelHubStatus();
            await LoadBezelsAsync(forceReload: true);
            ShowNotification("Проверка пакетов Mega Bezel", report.SummaryMessage);
        }
        catch (Exception ex)
        {
            ShowNotification("Ошибка проверки Mega Bezel", ex.Message);
        }
    }

    private async void BtnImportMegaBezelZip_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите ZIP-архив пакета HSM Mega Bezel или Duimon",
            Filter = "ZIP архивы (*.zip)|*.zip|Все файлы (*.*)|*.*"
        };

        if (dlg.ShowDialog() != true) return;

        string fileName = Path.GetFileName(dlg.FileName).ToLowerInvariant();
        string packId = "hsm_mega_bezel";
        if (fileName.Contains("potato")) packId = "duimon_potato";
        else if (fileName.Contains("duimon")) packId = "duimon_mega_bezel";

        if (PnlMegaBezelDownloadBar != null) PnlMegaBezelDownloadBar.Visibility = Visibility.Visible;
        if (PbMegaBezelInstall != null) PbMegaBezelInstall.Value = 0;

        _megaBezelInstallCts = new CancellationTokenSource();
        var progress = new Progress<double>(pct =>
        {
            if (PbMegaBezelInstall != null) PbMegaBezelInstall.Value = pct;
        });

        Action<string> onStatus = msg =>
        {
            Dispatcher.Invoke(() =>
            {
                if (TxtMegaBezelInstallProgress != null) TxtMegaBezelInstallProgress.Text = msg;
            });
        };

        try
        {
            var (ok, msg) = await MegaBezelPackManager.ImportPackZipAsync(
                packId, dlg.FileName, progress, onStatus, _megaBezelInstallCts.Token);

            RefreshMegaBezelHubStatus();
            _ = LoadBezelsAsync(forceReload: true);
            ShowNotification(ok ? "Импорт завершён" : "Ошибка импорта", msg);
        }
        catch (Exception ex)
        {
            ShowNotification("Ошибка импорта ZIP", ex.Message);
        }
        finally
        {
            if (PnlMegaBezelDownloadBar != null) PnlMegaBezelDownloadBar.Visibility = Visibility.Collapsed;
            _megaBezelInstallCts?.Dispose();
            _megaBezelInstallCts = null;
        }
    }

    private void ChkUniversalCoreBezel_Click(object sender, RoutedEventArgs e)
    {
        if (ChkUniversalCoreBezel == null) return;
        SettingsManager.Current.UniversalBezelForAllCores = ChkUniversalCoreBezel.IsChecked == true;
        SettingsManager.Save();
        ApplyBezelsFilter();
        ShowOsdToast(SettingsManager.Current.UniversalBezelForAllCores
            ? "Универсальный режим рамок для 360 ядер: ВКЛ"
            : "Универсальный режим рамок: ВЫКЛ");
    }

    // ==================== 5. UNIFIED 2.6.1 LAUNCH PIPELINE ====================

    public async Task LaunchGameUnified220Async(RomItem item)
    {
        _activeRomItem = item;

        // 1. Resolve best core if empty
        if (string.IsNullOrWhiteSpace(item.CoreDll))
        {
            var (plat, dll, name) = CoreManager.GetBestCoreForRom(item.FilePath);
            if (!string.IsNullOrEmpty(dll))
            {
                item.CoreDll = dll;
                if (string.IsNullOrEmpty(item.Platform)) item.Platform = plat;
                if (string.IsNullOrEmpty(item.CoreDisplayName)) item.CoreDisplayName = name;
            }
        }

        // 2. Automatically download core if missing and setting enabled
        if (!CoreManager.IsCoreInstalled(item.CoreDll) && SettingsManager.Current.AutoDownloadMissingCoreOnLaunch)
        {
            if (PnlGameLoadingOverlay != null) PnlGameLoadingOverlay.Visibility = Visibility.Visible;
            if (TxtGameViewportStatusTitle != null)
                TxtGameViewportStatusTitle.Text = $"Автозагрузка ядра {item.CoreDisplayName}...";

            await CoreManager.EnsureCoreDownloadedWithProgressAsync(
                item.CoreDll,
                (msg, pct) => Dispatcher.Invoke(() =>
                {
                    if (TxtGameViewportStatusTitle != null)
                        TxtGameViewportStatusTitle.Text = $"Автозагрузка ядра {item.CoreDisplayName} ({pct:F0}%)...";
                    if (TxtGameViewportStatusDesc != null)
                        TxtGameViewportStatusDesc.Text = msg;
                }));

            RefreshCoresUi();
        }

        // Ensure clean no-filter default before checking per-game overrides
        string defPreset = LiveVisualEffectsEngine.NormalizeShaderId(SettingsManager.Current.ActiveShaderPreset);
        if (defPreset == "none")
        {
            ApplyShaderPresetStudio270("none", "Без фильтра");
        }
        if (BdrAmbiluxAmbientGlow != null)
        {
            BdrAmbiluxAmbientGlow.Visibility = SettingsManager.Current.AmbiluxGlowEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        // Apply per-game profile override if any
        var profile = PerGameProfileManager.TryLoadProfileForGame(item.Name);
        if (profile != null)
        {
            if (!string.IsNullOrEmpty(profile.ShaderPresetName))
            {
                var matchShader = _allShaders.FirstOrDefault(s => s.Name.Equals(profile.ShaderPresetName, StringComparison.OrdinalIgnoreCase));
                if (matchShader != null)
                {
                    ShaderManager.ApplyShader(matchShader);
                    foreach (var s in _allShaders) s.IsActive = (s == matchShader);
                }
            }
            if (!string.IsNullOrEmpty(profile.BezelName))
            {
                var matchBezel = _allBezels.FirstOrDefault(b => b.Name.Equals(profile.BezelName, StringComparison.OrdinalIgnoreCase));
                if (matchBezel != null)
                {
                    BezelManager.ApplyBezel(matchBezel);
                    foreach (var b in _allBezels) b.IsActive = (b == matchBezel);
                }
            }
            if (profile.RunAheadFrames >= 0)
            {
                NativeLibretroHost.Current.RunAheadFrames = profile.RunAheadFrames;
            }
            if (!string.IsNullOrEmpty(profile.AudioDspPreset))
            {
                AudioDspEngine.Shared.ApplyPreset(profile.AudioDspPreset);
            }
        }

        RecordGameHistory(item);

        // Switch to Game Viewport
        System.Windows.Input.Keyboard.ClearFocus();
        if (BtnNavGamePlayer != null) NavBtn_Click(BtnNavGamePlayer, new RoutedEventArgs());
        if (ViewGamePlayer != null) ViewGamePlayer.Visibility = Visibility.Visible;
        if (PnlInGameQuickMenu != null) PnlInGameQuickMenu.Visibility = Visibility.Collapsed;
        GameViewportContainer?.Focus();

        if (TxtActiveGameTitle != null) TxtActiveGameTitle.Text = item.Name;
        if (TxtActiveGamePlatform != null) TxtActiveGamePlatform.Text = item.Platform;
        if (TxtActiveGameCore != null) TxtActiveGameCore.Text = item.CoreDisplayName;
        if (PnlGameLoadingOverlay != null) PnlGameLoadingOverlay.Visibility = Visibility.Visible;
        if (TxtGameViewportStatusTitle != null) TxtGameViewportStatusTitle.Text = $"Запуск «{item.Name}»...";
        if (TxtGameViewportStatusDesc != null)
            TxtGameViewportStatusDesc.Text = $"Загрузка ядра {item.CoreDisplayName} и подготовка графического конвейера...";

        // 3. Select active bezel
        BezelPresetItem? activeBezel = null;
        if (SettingsManager.Current.AutoBezelByPlatform || SettingsManager.Current.UniversalBezelForAllCores)
        {
            activeBezel = BezelManager.FindBestBezelForPlatformOrCore(item.Platform, item.CoreDll);
            if (activeBezel != null)
            {
                BezelManager.ApplyBezel(activeBezel);
                foreach (var b in _allBezels) b.IsActive = (b == activeBezel);
                ApplyActiveBezelToViewport();
            }
        }

        try
        {
            // 4. Hardware routing decision
            bool routeToHwEngine = VulkanD3dEngineBridge.ShouldRouteToHardwareEngine(item.CoreDll, activeBezel?.SlangpPath);

            if (routeToHwEngine)
            {
                NativeLibretroHost.Current.StopGame();
                if (VbGameStage != null) VbGameStage.Visibility = Visibility.Collapsed;
                if (HwEngineGameHost != null) HwEngineGameHost.Visibility = Visibility.Visible;

                string driver = VulkanD3dEngineBridge.SelectBestVideoDriver(item.CoreDll, activeBezel?.SlangpPath);
                if (TxtHudGpuBackendBadge != null)
                    TxtHudGpuBackendBadge.Text = "GPU: " + driver.ToUpperInvariant();

                string coreFullPath = Path.Combine(CoreManager.CoresDir, item.CoreDll ?? "");
                bool hwStarted = await VulkanD3dEngineBridge.Current.StartAsync(
                    item,
                    coreFullPath,
                    HwEngineGameHost,
                    activeBezel,
                    activeBezel?.SlangpPath,
                    status => Dispatcher.Invoke(() =>
                    {
                        if (TxtGameViewportStatusDesc != null) TxtGameViewportStatusDesc.Text = status;
                    }));

                if (hwStarted)
                {
                    if (PnlGameLoadingOverlay != null) PnlGameLoadingOverlay.Visibility = Visibility.Collapsed;
                    UpdateInGameHudButtons();
                    GameViewportContainer?.Focus();
                    ShowOsdToast($"Игра «{item.Name}» запущена на аппаратном движке ({driver.ToUpperInvariant()})");
                }
                else
                {
                    if (TxtGameViewportStatusTitle != null) TxtGameViewportStatusTitle.Text = "Ошибка запуска GPU движка";
                }
            }
            else
            {
                if (HwEngineGameHost != null) HwEngineGameHost.Visibility = Visibility.Collapsed;
                if (VbGameStage != null) VbGameStage.Visibility = Visibility.Visible;
                VulkanD3dEngineBridge.Current.Stop();

                bool started = await NativeLibretroHost.Current.StartGameAsync(item, NativeGameImage, status =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (TxtGameViewportStatusDesc != null) TxtGameViewportStatusDesc.Text = status;
                    });
                });

                if (!started && NativeLibretroHost.Current.RequestedExternalGpuBackend)
                {
                    // Dynamic fallback to external GPU engine
                    if (VbGameStage != null) VbGameStage.Visibility = Visibility.Collapsed;
                    if (HwEngineGameHost != null) HwEngineGameHost.Visibility = Visibility.Visible;

                    string driver = NativeLibretroHost.Current.RequestedGpuBackendName;
                    if (string.IsNullOrEmpty(driver)) driver = "vulkan";
                    if (TxtHudGpuBackendBadge != null)
                        TxtHudGpuBackendBadge.Text = "GPU: " + driver.ToUpperInvariant();

                    string coreFullPath = Path.Combine(CoreManager.CoresDir, item.CoreDll ?? "");
                    started = await VulkanD3dEngineBridge.Current.StartAsync(
                        item,
                        coreFullPath,
                        HwEngineGameHost,
                        activeBezel,
                        activeBezel?.SlangpPath,
                        status => Dispatcher.Invoke(() =>
                        {
                            if (TxtGameViewportStatusDesc != null) TxtGameViewportStatusDesc.Text = status;
                        }));
                }

                if (started)
                {
                    if (PnlGameLoadingOverlay != null) PnlGameLoadingOverlay.Visibility = Visibility.Collapsed;
                    UpdateInGameHudButtons();

                    string coreId = Path.GetFileNameWithoutExtension(item.CoreDll ?? "");
                    DeviceInputProfileEngine.NotifyCoreOrGameChanged(coreId, item.Name);
                    ApplyActiveBezelToViewport();
                    GameViewportContainer?.Focus();
                    ShowOsdToast($"Игра «{item.Name}» запущена • F1: Меню • F2/F4: Стейты • F11: Полный экран");
                }
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

    // ==================== 6. SCREENSHOT 2.6.1 SUITE ====================

    public async Task RunScreenshot220SuiteAsync(string outDir)
    {
        string logPath = Path.Combine(outDir, "run220.log");
        void Log(string msg) { try { File.AppendAllText(logPath, msg + "\n"); } catch { } }

        try
        {
            Log("RunScreenshot220SuiteAsync started into " + outDir);
            Directory.CreateDirectory(outDir);

            Width = 1540;
            Height = 920;
            UpdateLayout();

            void Snap(string fileName)
            {
                AnalyticalSpringEngine.FlushAllImmediately();
                UpdateLayout();
                int sw = 1540;
                int sh = 920;
                var rtb = new RenderTargetBitmap(sw, sh, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(this);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                string targetFile = Path.Combine(outDir, fileName);
                using var fs = File.Create(targetFile);
                enc.Save(fs);
                Log("Saved " + targetFile + " (" + new FileInfo(targetFile).Length + " bytes)");
            }

            // 1. v220_dashboard.png - Dashboard showing 2.6.1
            NavBtn_Click(NavBtnMainMenu, new RoutedEventArgs());
            if (PnlOsdToast != null) PnlOsdToast.Visibility = Visibility.Collapsed;
            if (BdrDeviceHotplugToast != null) BdrDeviceHotplugToast.Visibility = Visibility.Collapsed;
            await Task.Delay(250);
            Snap("v220_dashboard.png");

            // 2. v220_cores_all_360.png - ViewCores showing all 360 cores, Vulkan/D3D11-12 selector, auto-download checkbox, HW API badges
            NavBtn_Click(NavBtnCores, new RoutedEventArgs());
            PopulateCoresList("Все ядра (350+)");
            if (ChkAutoDownloadCoreOnLaunch != null) ChkAutoDownloadCoreOnLaunch.IsChecked = true;
            if (CmbGlobalGraphicsBackend != null) CmbGlobalGraphicsBackend.SelectedIndex = 0;
            await Task.Delay(250);
            Snap("v220_cores_all_360.png");

            // 3. v220_cores_3d_nextgen.png - ViewCores filtered to "3D: PS2 / PS3 / PSV / Xbox"
            PopulateCoresList("3D: PS2 / PS3 / PSV / Xbox");
            await Task.Delay(250);
            Snap("v220_cores_3d_nextgen.png");

            // 4. v220_bios_subfolders.png - ViewBios filtered to "PS2 / PS3 / PS Vita"
            NavBtn_Click(NavBtnBios, new RoutedEventArgs());
            _selectedBiosFilter = "PS2 / PS3 / PS Vita";
            ApplyBiosFilter();
            await Task.Delay(250);
            Snap("v220_bios_subfolders.png");

            // 5. v220_mega_bezel_studio.png - ViewBezels showing 3-column Online HSM + Duimon 4K + Duimon Potato
            NavBtn_Click(NavBtnBezels, new RoutedEventArgs());
            RefreshMegaBezelHubStatus();
            ApplyBezelsFilter();
            await Task.Delay(250);
            Snap("v220_mega_bezel_studio.png");

            // 6. v220_ingame_gpu_bezel.png - ViewGamePlayer with active bezel + GPU: VULKAN badge in HUD
            if (BtnNavGamePlayer != null) NavBtn_Click(BtnNavGamePlayer, new RoutedEventArgs());
            if (ViewGamePlayer != null) ViewGamePlayer.Visibility = Visibility.Visible;
            if (TxtHudGpuBackendBadge != null) TxtHudGpuBackendBadge.Text = "GPU: VULKAN";
            if (TxtActiveGameTitle != null) TxtActiveGameTitle.Text = "Gran Turismo 4 (PS2)";
            if (TxtActiveGamePlatform != null) TxtActiveGamePlatform.Text = "Sony PlayStation 2";
            if (TxtActiveGameCore != null) TxtActiveGameCore.Text = "LRPS2";
            if (PnlGameLoadingOverlay != null) PnlGameLoadingOverlay.Visibility = Visibility.Collapsed;
            // Render crisp synthetic 640x480 3D preview frame without blocking UI dispatcher
            if (ImgGameViewport != null)
            {
                var dv = new System.Windows.Media.DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    var bgGrad = new LinearGradientBrush(Color.FromRgb(12, 18, 42), Color.FromRgb(30, 14, 56), 45);
                    dc.DrawRectangle(bgGrad, null, new Rect(0, 0, 640, 480));
                    var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(65, 56, 189, 248)), 1.2);
                    for (int gx = 0; gx <= 640; gx += 40) dc.DrawLine(gridPen, new Point(gx, 260), new Point(320 + (gx - 320) * 2.2, 480));
                    for (int gy = 260; gy <= 480; gy += 28) dc.DrawLine(gridPen, new Point(0, gy), new Point(640, gy));
                    var ftTitle = new System.Windows.Media.FormattedText("SONY PLAYSTATION 2 - VULKAN / D3D11", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 22, Brushes.White, 1.0);
                    dc.DrawText(ftTitle, new Point((640 - ftTitle.Width) / 2, 150));
                    var ftSub = new System.Windows.Media.FormattedText("LRPS2 + HSM MEGA BEZEL PIPELINE READY • 60.0 FPS", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), 14, new SolidColorBrush(Color.FromRgb(56, 189, 248)), 1.0);
                    dc.DrawText(ftSub, new Point((640 - ftSub.Width) / 2, 195));
                }
                var rtbGame = new RenderTargetBitmap(640, 480, 96, 96, PixelFormats.Pbgra32);
                rtbGame.Render(dv);
                ImgGameViewport.Source = rtbGame;
            }
            var ps2Bezel = BezelManager.FindBestBezelForPlatformOrCore("Sony PlayStation 2", "pcsx2_libretro.dll");
            if (ps2Bezel != null)
            {
                BezelManager.ApplyBezel(ps2Bezel);
                ApplyActiveBezelToViewport();
            }
            await Task.Delay(250);
            Snap("v220_ingame_gpu_bezel.png");

            Log("RunScreenshot220SuiteAsync finished successfully!");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Log("RunScreenshot220SuiteAsync exception: " + ex);
        }
    }
}
