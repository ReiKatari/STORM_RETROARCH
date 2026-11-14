using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace StormRetroArch;

public partial class MainWindow
{
    // ==================== 2.6.1 STATE & SUBSYSTEM CACHES ====================

    private bool _shadersLoaded;
    private bool _savesLoaded;
    private bool _biosLoaded;
    private bool _cheatsLoaded;
    private bool _bezelsLoaded;
    private bool _achievementsLoaded;
    private bool _isSplitInspectorVisible = true;
    private bool _suppressSplitInspectorEvents = false;
    private bool _isRouletteRunning = false;
    private int _previousNavIndex = 0;

    private string _selectedAlphabetFilter = "ВСЕ";
    private RomItem? _selectedSplitGame;
    private readonly BulkObservableCollection<ExternalEmulatorEntry> _externalEmulators = new();
    private readonly Dictionary<string, string> _gameExternalEmulatorOverrides = new(StringComparer.OrdinalIgnoreCase);

    private static readonly SolidColorBrush ActiveStarFill = CreateFrozenBrush(0xFB, 0xBF, 0x24);
    private static readonly SolidColorBrush MutedStarFill = CreateFrozenBrush(0x4B, 0x55, 0x63);
    private static readonly SolidColorBrush FavActiveFill = CreateFrozenBrush(0xFB, 0xBF, 0x24);
    private static readonly SolidColorBrush FavInactiveFill = CreateFrozenBrush(0x94, 0xA3, 0xB8);

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b, byte a = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    // ==================== 2.6.1 INITIALIZATION & BACKGROUND HYDRATION ====================

    private void InitStudio260()
    {
        try
        {
            if (LstExternalEmulators != null)
                LstExternalEmulators.ItemsSource = _externalEmulators;

            PopulateSplitInspectorCompletionStatuses();
            BuildAlphabetQuickJumpBar();
            AttachTactilePressSpringsToMainButtons();
            RefreshExternalEmulatorSelectors();
            RefreshPlayerAnalyticsDashboard();

            if (_allGames.Count > 0 && _selectedSplitGame == null)
            {
                SelectGameInSplitInspector(_filteredGames.FirstOrDefault() ?? _allGames[0]);
            }
            else
            {
                SelectGameInSplitInspector(null);
            }
        }
        catch { }
    }

    private void AttachTactilePressSpringsToMainButtons()
    {
        var candidates = new Button?[]
        {
            BtnAddFiles, BtnAddFolder, BtnDownloadBoxarts, BtnClearList,
            BtnRandomGame, BtnToggleSplitInspector, BtnSplitPlay, BtnSplitGameHub,
            BtnSplitFavorite, BtnSplitCheats, BtnSplitRomHash, BtnSplitLaunchExternal,
            BtnBigBox, BtnBigBoxPlay, BtnBigBoxHub, BtnBigBoxCore, BtnBigBoxFavorite, BtnBigBoxRandom,
            BtnScanExternalEmus, BtnAddExternalEmu, BtnImportRetroArchLpl,
            NavBtnMainMenu, NavBtnLibrary, NavBtnFavorites, NavBtnHistory,
            NavBtnCores, NavBtnCoreOptions, NavBtnBios, NavBtnInputRemap,
            NavBtnCheats, NavBtnShaders, NavBtnBezels, NavBtnImport,
            NavBtnScreenshots, NavBtnSaves, NavBtnSaveSync, NavBtnAchievements,
            NavBtnNetplay, NavBtnAiWalkthrough, NavBtnStreamAi, NavBtnSettings
        };

        foreach (var btn in candidates)
        {
            if (btn != null)
            {
                AnalyticalSpringEngine.AttachTactileButtonPress(btn);
            }
        }
    }

    private async Task HydrateSubsystemsInBackgroundAsync()
    {
        try
        {
            var gamesSnapshot = _allGames.ToList();

            var bgTask = Task.Run(() =>
            {
                var shaders = ShaderManager.GetAllShaders();
                var saves = SaveManager.ScanSavesAndStates();
                var bios = BiosManager.InspectSystemDirectory();
                BezelManager.EnsureBuiltinBezels();
                var bezels = BezelManager.ScanBezels();
                var cheevos = AchievementsManager.ScanLibraryGamesForCheevos(gamesSnapshot);
                CheatManager.CleanupDuplicateAndSampleCheats(gamesSnapshot);
                var cheats = CheatManager.ScanCheats(gamesSnapshot);
                var emus = ExternalEmulatorBridge.DetectInstalledEmulators();
                return (shaders, saves, bios, bezels, cheevos, cheats, emus);
            });

            var result = await bgTask;

            await Dispatcher.InvokeAsync(() =>
            {
                _allShaders.ReplaceAll(result.shaders);
                _shadersLoaded = true;
                ApplyShadersFilter();

                _allSaves.ReplaceAll(result.saves);
                _savesLoaded = true;
                ApplySavesFilter();

                _allBios.ReplaceAll(result.bios);
                _biosLoaded = true;
                int installedBios = result.bios.Count(b => b.IsInstalled);
                if (TxtBiosStats != null)
                    TxtBiosStats.Text = $"Всего в базе: {result.bios.Count} | Установлено: {installedBios} | Отсутствует: {result.bios.Count - installedBios}";
                ApplyBiosFilter();

                _allBezels.ReplaceAll(result.bezels);
                _bezelsLoaded = true;
                ApplyBezelsFilter();

                _allAchievements.ReplaceAll(result.cheevos);
                _filteredAchievements.ReplaceAll(result.cheevos);
                _achievementsLoaded = true;
                if (TxtCheevosLibraryCount != null)
                    TxtCheevosLibraryCount.Text = $"{_filteredAchievements.Count} игр в библиотеке";

                _allCheatFilesMaster = result.cheats;
                _allCheatFiles.ReplaceAll(result.cheats);
                _cheatsLoaded = true;
                if (BtnCheatsFilterAll != null)
                    BtnCheatsFilterAll.Content = $"Все читы ({_allCheatFilesMaster.Count:N0})";
                int libCnt = _allCheatFilesMaster.Count(x => x.IsLibraryGame);
                if (BtnCheatsFilterLibrary != null)
                    BtnCheatsFilterLibrary.Content = $"В библиотеке ({libCnt:N0})";
                PopulateCheatsPlatformFilter();
                ApplyCheatsFilter();
                _cheatsCountCts?.Cancel();
                _cheatsCountCts = new System.Threading.CancellationTokenSource();
                _ = CheatManager.PopulateCheatCountsAsync(_allCheatFilesMaster, _cheatsCountCts.Token);

                _externalEmulators.ReplaceAll(result.emus);
                RefreshExternalEmulatorSelectors();
                InitCommandPaletteItems();
            });

            RefreshLobby();
        }
        catch { }
    }

    // ==================== NAV DIRECTION & VIEW TRANSITION HELPERS ====================

    private int GetSidebarNavIndex(Button btn)
    {
        var order = new Button?[]
        {
            NavBtnMainMenu, NavBtnLibrary, NavBtnFavorites, NavBtnHistory,
            NavBtnCores, NavBtnCoreOptions, NavBtnBios, NavBtnInputRemap, NavBtnCheats,
            NavBtnShaders, NavBtnBezels, NavBtnImport, NavBtnScreenshots,
            NavBtnSaves, NavBtnSaveSync, NavBtnAchievements, NavBtnNetplay,
            NavBtnAiWalkthrough, NavBtnStreamAi, NavBtnSettings
        };
        for (int i = 0; i < order.Length; i++)
        {
            if (order[i] == btn) return i;
        }
        return 0;
    }

    // ==================== ALPHABETICAL QUICK-JUMP BAR ====================

    private void BuildAlphabetQuickJumpBar()
    {
        if (PnlLibraryAlphabetBar == null) return;
        PnlLibraryAlphabetBar.Children.Clear();

        var keys = new List<string> { "ВСЕ", "#" };
        for (char c = 'A'; c <= 'Z'; c++) keys.Add(c.ToString());
        keys.Add("А-Я");

        foreach (var k in keys)
        {
            bool isSelected = string.Equals(_selectedAlphabetFilter, k, StringComparison.OrdinalIgnoreCase);
            var btn = new Button
            {
                Style = (Style)FindResource("CapsuleButton"),
                Content = k,
                Tag = k,
                MinWidth = k.Length > 1 ? 34 : 24,
                Height = 23,
                Padding = new Thickness(k.Length > 1 ? 8 : 4, 1, k.Length > 1 ? 8 : 4, 1),
                Margin = new Thickness(0, 0, 3, 2),
                FontSize = 11,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                ToolTip = k == "ВСЕ" ? "Показать все игры" : $"Фильтр по началу названия: {k}"
            };

            if (isSelected)
            {
                btn.Background = CreateFrozenBrush(0x8B, 0x5C, 0xF6, 0x45);
                btn.BorderBrush = CreateFrozenBrush(0x8B, 0x5C, 0xF6);
                btn.Foreground = CreateFrozenBrush(0xF8, 0xFA, 0xFC);
            }
            else
            {
                btn.Background = CreateFrozenBrush(0xFF, 0xFF, 0xFF, 0x10);
                btn.BorderBrush = (Brush)FindResource("ThemeBorderSubtle");
                btn.Foreground = (Brush)FindResource("ThemeTextSecondary");
            }

            string captured = k;
            btn.Click += (_, _) =>
            {
                _selectedAlphabetFilter = captured;
                BuildAlphabetQuickJumpBar();
                ApplyGamesFilter();
            };
            PnlLibraryAlphabetBar.Children.Add(btn);
        }
    }

    private bool MatchesAlphabetFilter(RomItem game)
    {
        if (string.IsNullOrEmpty(_selectedAlphabetFilter) || _selectedAlphabetFilter == "ВСЕ")
            return true;

        string name = game.Name?.Trim() ?? "";
        if (string.IsNullOrEmpty(name)) return _selectedAlphabetFilter == "#";

        char first = char.ToUpperInvariant(name[0]);
        if (_selectedAlphabetFilter == "#")
        {
            return !((first >= 'A' && first <= 'Z') || (first >= 'А' && first <= 'Я') || first == 'Ё');
        }
        if (_selectedAlphabetFilter == "А-Я")
        {
            return (first >= 'А' && first <= 'Я') || first == 'Ё';
        }
        return first == char.ToUpperInvariant(_selectedAlphabetFilter[0]);
    }

    // ==================== PLAYNITE-STYLE SPLIT GAME INSPECTOR ====================

    private void PopulateSplitInspectorCompletionStatuses()
    {
        if (CmbSplitCompletionStatus == null) return;
        _suppressSplitInspectorEvents = true;
        CmbSplitCompletionStatus.Items.Clear();
        foreach (var status in GameMetadataStore.CompletionStatuses)
        {
            CmbSplitCompletionStatus.Items.Add(status);
        }
        CmbSplitCompletionStatus.SelectedIndex = 0;
        _suppressSplitInspectorEvents = false;
    }

    public void SelectGameInSplitInspector(RomItem? item)
    {
        _selectedSplitGame = item;
        if (BdrLibrarySplitInspector == null) return;

        _suppressSplitInspectorEvents = true;
        try
        {
            if (item == null)
            {
                if (ImgSplitInspectorCover != null) ImgSplitInspectorCover.Source = null;
                if (TxtSplitInspectorTitle != null) TxtSplitInspectorTitle.Text = "Выберите игру";
                if (TxtSplitInspectorPlatform != null) TxtSplitInspectorPlatform.Text = "Нажмите на карточку игры в списке";
                if (TxtSplitInspectorRegion != null) TxtSplitInspectorRegion.Text = "-";
                if (TxtSplitInspectorYear != null) TxtSplitInspectorYear.Text = "-";
                if (TxtSplitInspectorTranslation != null) TxtSplitInspectorTranslation.Text = "-";
                if (TxtSplitInspectorAudio != null) TxtSplitInspectorAudio.Text = "-";
                if (TxtSplitInspectorFiles != null) TxtSplitInspectorFiles.Text = "-";
                if (TxtSplitPlaytime != null) TxtSplitPlaytime.Text = "0 мин";
                if (TxtSplitLaunchCount != null) TxtSplitLaunchCount.Text = "0";
                if (TxtSplitNotes != null) TxtSplitNotes.Text = "";
                UpdateSplitStarVisuals(0);
                UpdateSplitFavoriteVisual(false);
                return;
            }

            if (ImgSplitInspectorCover != null) ImgSplitInspectorCover.Source = item.Icon;
            if (TxtSplitInspectorTitle != null) TxtSplitInspectorTitle.Text = item.Name;
            if (TxtSplitInspectorPlatform != null) TxtSplitInspectorPlatform.Text = $"{item.Platform} • {item.CoreDisplayName}";
            if (TxtSplitInspectorRegion != null) TxtSplitInspectorRegion.Text = item.SlotRegion;
            if (TxtSplitInspectorYear != null) TxtSplitInspectorYear.Text = item.SlotYear;
            if (TxtSplitInspectorTranslation != null) TxtSplitInspectorTranslation.Text = item.SlotTranslation;
            if (TxtSplitInspectorAudio != null) TxtSplitInspectorAudio.Text = item.SlotAudio;
            if (TxtSplitInspectorFiles != null) TxtSplitInspectorFiles.Text = item.SlotFiles;
            if (TxtSplitPlaytime != null) TxtSplitPlaytime.Text = item.PlaytimeText;
            if (TxtSplitLaunchCount != null) TxtSplitLaunchCount.Text = item.LaunchCount.ToString();

            if (CmbSplitCompletionStatus != null)
            {
                string status = string.IsNullOrWhiteSpace(item.CompletionStatus) ? GameMetadataStore.CompletionStatuses[0] : item.CompletionStatus;
                CmbSplitCompletionStatus.SelectedItem = status;
            }

            if (TxtSplitNotes != null)
                TxtSplitNotes.Text = item.UserNotes ?? "";

            UpdateSplitStarVisuals(item.StarRating);
            UpdateSplitFavoriteVisual(item.IsFavorite);
            SyncSplitExternalEmuSelection(item);
        }
        finally
        {
            _suppressSplitInspectorEvents = false;
        }
    }

    private void UpdateSplitStarVisuals(int rating)
    {
        var paths = new[] { PathSplitStar1, PathSplitStar2, PathSplitStar3, PathSplitStar4, PathSplitStar5 };
        for (int i = 0; i < paths.Length; i++)
        {
            if (paths[i] != null)
            {
                paths[i].Fill = (i < rating) ? ActiveStarFill : MutedStarFill;
            }
        }
    }

    private void UpdateSplitFavoriteVisual(bool isFavorite)
    {
        if (PathSplitFavoriteStar != null)
            PathSplitFavoriteStar.Fill = isFavorite ? FavActiveFill : FavInactiveFill;
        if (TxtSplitFavoriteLabel != null)
            TxtSplitFavoriteLabel.Text = isFavorite ? "В избранном" : "В избранное";
    }

    private void BdrSplitInspectorCover_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_selectedSplitGame != null)
        {
            OpenMediaLightbox(_selectedSplitGame, 0);
            e.Handled = true;
        }
    }

    private void BtnToggleSplitInspector_Click(object sender, RoutedEventArgs e)
    {
        _isSplitInspectorVisible = !_isSplitInspectorVisible;
        if (BdrLibrarySplitInspector != null)
        {
            BdrLibrarySplitInspector.Visibility = _isSplitInspectorVisible ? Visibility.Visible : Visibility.Collapsed;
            if (_isSplitInspectorVisible)
            {
                AnalyticalSpringEngine.AnimatePanelReveal(BdrLibrarySplitInspector, 10.0, 420f, 32f);
            }
        }
        if (BtnToggleSplitInspector != null)
        {
            BtnToggleSplitInspector.Tag = _isSplitInspectorVisible ? "Active" : null;
        }
    }

    private void LvGames_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LvGames?.SelectedItem is RomItem item)
        {
            SelectGameInSplitInspector(item);
        }
    }

    private async void BtnSplitPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSplitGame == null) return;
        await LaunchGameSmartAsync(_selectedSplitGame);
    }

    private void BtnSplitGameHub_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSplitGame != null) OpenGameHubModal(_selectedSplitGame);
    }

    private void BtnSplitFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSplitGame == null) return;
        ToggleGameFavorite(_selectedSplitGame);
        UpdateSplitFavoriteVisual(_selectedSplitGame.IsFavorite);
    }

    private void ToggleGameFavorite(RomItem item)
    {
        item.IsFavorite = !item.IsFavorite;
        if (item.IsFavorite)
        {
            if (!_favorites.Any(f => f.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase)))
                _favorites.Add(item);
            ShowOsdToast($"Игра «{item.Name}» добавлена в Избранное");
        }
        else
        {
            var existing = _favorites.FirstOrDefault(f => f.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
            if (existing != null) _favorites.Remove(existing);
            ShowOsdToast($"Игра «{item.Name}» удалена из Избранного");
        }
        SavePlaylists();
        UpdateSmartCollectionPills();
        UpdatePlaylistEmptyStates();
    }

    private void BtnSplitStar_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSplitGame == null) return;
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int rating))
        {
            _selectedSplitGame.StarRating = rating;
            GameMetadataStore.UpdateFromRomItem(_selectedSplitGame);
            UpdateSplitStarVisuals(rating);
            RefreshPlayerAnalyticsDashboard();
        }
    }

    private void CmbSplitCompletionStatus_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSplitInspectorEvents || _selectedSplitGame == null) return;
        if (CmbSplitCompletionStatus?.SelectedItem is string status)
        {
            _selectedSplitGame.CompletionStatus = status;
            GameMetadataStore.UpdateFromRomItem(_selectedSplitGame);
            RefreshAllLibraryFiltersAndCounts();
            RefreshPlayerAnalyticsDashboard();
        }
    }

    private void TxtSplitNotes_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSplitInspectorEvents || _selectedSplitGame == null) return;
        _selectedSplitGame.UserNotes = TxtSplitNotes?.Text ?? "";
        GameMetadataStore.UpdateFromRomItem(_selectedSplitGame);
    }

    private void BtnSplitSaveNotes_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSplitGame == null) return;
        _selectedSplitGame.UserNotes = TxtSplitNotes?.Text ?? "";
        if (CmbSplitCompletionStatus?.SelectedItem is string st)
            _selectedSplitGame.CompletionStatus = st;
        GameMetadataStore.UpdateFromRomItem(_selectedSplitGame);
        RefreshAllLibraryFiltersAndCounts();
        RefreshPlayerAnalyticsDashboard();
        ShowOsdToast("Заметка и статус игры сохранены");
    }

    private void BtnSplitCheats_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSplitGame != null) OpenCheatsForGame(_selectedSplitGame);
    }

    private void BtnSplitChangeCore_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSplitGame != null) OpenCoreSelectorForGame(_selectedSplitGame);
    }

    private async void BtnSplitRomHash_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSplitGame == null) return;
        NavBtn_Click(NavBtnImport, new RoutedEventArgs());
        if (!File.Exists(_selectedSplitGame.FilePath))
        {
            ShowOsdToast("Файл ROM не найден на диске");
            return;
        }
        await RunRomInspectionAsync(_selectedSplitGame.FilePath);
    }

    private void RefreshExternalEmulatorSelectors()
    {
        if (CmbSplitExternalEmu == null) return;
        _suppressSplitInspectorEvents = true;
        try
        {
            CmbSplitExternalEmu.Items.Clear();
            CmbSplitExternalEmu.Items.Add(new ComboBoxItem { Tag = "INTERNAL", Content = "Встроенный Libretro" });
            foreach (var emu in _externalEmulators.Where(x => x.IsEnabled && x.ExistsOnDisk))
            {
                CmbSplitExternalEmu.Items.Add(new ComboBoxItem
                {
                    Tag = emu.Id,
                    Content = $"Внешний: {emu.Name}"
                });
            }
            if (_selectedSplitGame != null)
                SyncSplitExternalEmuSelection(_selectedSplitGame);
            else
                CmbSplitExternalEmu.SelectedIndex = 0;
        }
        finally
        {
            _suppressSplitInspectorEvents = false;
        }
    }

    private void SyncSplitExternalEmuSelection(RomItem item)
    {
        if (CmbSplitExternalEmu == null) return;
        string targetTag = _gameExternalEmulatorOverrides.TryGetValue(item.FilePath, out var id) ? id : "INTERNAL";
        for (int i = 0; i < CmbSplitExternalEmu.Items.Count; i++)
        {
            if (CmbSplitExternalEmu.Items[i] is ComboBoxItem cbi &&
                string.Equals(cbi.Tag?.ToString(), targetTag, StringComparison.OrdinalIgnoreCase))
            {
                CmbSplitExternalEmu.SelectedIndex = i;
                return;
            }
        }
        if (CmbSplitExternalEmu.Items.Count > 0) CmbSplitExternalEmu.SelectedIndex = 0;
    }

    private void CmbSplitExternalEmu_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSplitInspectorEvents || _selectedSplitGame == null) return;
        if (CmbSplitExternalEmu?.SelectedItem is ComboBoxItem cbi)
        {
            string tag = cbi.Tag?.ToString() ?? "INTERNAL";
            if (tag == "INTERNAL")
                _gameExternalEmulatorOverrides.Remove(_selectedSplitGame.FilePath);
            else
                _gameExternalEmulatorOverrides[_selectedSplitGame.FilePath] = tag;
        }
    }

    private async void BtnSplitLaunchExternal_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSplitGame == null) return;
        string ext = System.IO.Path.GetExtension(_selectedSplitGame.FilePath).ToLowerInvariant();
        if (ext is ".exe" or ".lnk" or ".bat" or ".cmd")
        {
            var res = ExternalEmulatorBridge.LaunchWithExternalEmulator(null, _selectedSplitGame);
            ShowOsdToast(res.Message);
            SelectGameInSplitInspector(_selectedSplitGame);
            RefreshPlayerAnalyticsDashboard();
            return;
        }

        string tag = (CmbSplitExternalEmu?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "INTERNAL";
        var emu = _externalEmulators.FirstOrDefault(x => x.Id.Equals(tag, StringComparison.OrdinalIgnoreCase))
                  ?? FindMatchingExternalEmulator(_selectedSplitGame);

        if (emu == null)
        {
            await StartGameInViewportAsync(_selectedSplitGame);
            return;
        }

        var launch = ExternalEmulatorBridge.LaunchWithExternalEmulator(emu, _selectedSplitGame);
        ShowOsdToast(launch.Message);
        SelectGameInSplitInspector(_selectedSplitGame);
        RefreshPlayerAnalyticsDashboard();
    }

    private ExternalEmulatorEntry? FindMatchingExternalEmulator(RomItem game)
    {
        if (string.IsNullOrWhiteSpace(game.Platform)) return null;
        return _externalEmulators.FirstOrDefault(e =>
            e.IsEnabled && e.ExistsOnDisk &&
            e.PlatformTags.Split(',').Any(tag =>
                game.Platform.Contains(tag.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    public async Task LaunchGameSmartAsync(RomItem game)
    {
        if (game == null) return;
        string ext = System.IO.Path.GetExtension(game.FilePath).ToLowerInvariant();
        if (ext is ".exe" or ".lnk" or ".bat" or ".cmd")
        {
            var direct = ExternalEmulatorBridge.LaunchWithExternalEmulator(null, game);
            ShowOsdToast(direct.Message);
            SelectGameInSplitInspector(game);
            RefreshPlayerAnalyticsDashboard();
            return;
        }

        if (_gameExternalEmulatorOverrides.TryGetValue(game.FilePath, out var emuId))
        {
            var emu = _externalEmulators.FirstOrDefault(e => e.Id.Equals(emuId, StringComparison.OrdinalIgnoreCase) && e.ExistsOnDisk);
            if (emu != null)
            {
                var extRes = ExternalEmulatorBridge.LaunchWithExternalEmulator(emu, game);
                ShowOsdToast(extRes.Message);
                SelectGameInSplitInspector(game);
                RefreshPlayerAnalyticsDashboard();
                return;
            }
        }

        await StartGameInViewportAsync(game);
    }

    // ==================== RANDOM GAME ROULETTE ====================

    private async void BtnRandomGame_Click(object sender, RoutedEventArgs e)
    {
        await StartRandomGameRouletteAsync();
    }

    private async void BtnBigBoxRandom_Click(object sender, RoutedEventArgs e)
    {
        await StartRandomGameRouletteAsync(inBigBox: true);
    }

    public async Task StartRandomGameRouletteAsync(bool inBigBox = false)
    {
        if (_isRouletteRunning) return;
        var pool = _filteredGames.Count > 0 ? _filteredGames.ToList() : _allGames.ToList();
        if (pool.Count == 0)
        {
            ShowOsdToast("Добавьте игры в библиотеку, чтобы запустить рулетку!");
            return;
        }

        _isRouletteRunning = true;
        try
        {
            if (!inBigBox && _activeNavView != "Library")
            {
                NavBtn_Click(NavBtnLibrary, new RoutedEventArgs());
            }

            if (!inBigBox && BdrLibrarySplitInspector != null && !_isSplitInspectorVisible)
            {
                _isSplitInspectorVisible = true;
                BdrLibrarySplitInspector.Visibility = Visibility.Visible;
                if (BtnToggleSplitInspector != null) BtnToggleSplitInspector.Tag = "Active";
            }

            var rng = Random.Shared;
            int steps = Math.Min(9, Math.Max(4, pool.Count));
            int[] delaysMs = { 45, 55, 65, 75, 85, 95, 105, 115, 130 };
            RomItem winner = pool[rng.Next(pool.Count)];

            for (int i = 0; i < steps; i++)
            {
                var pick = (i == steps - 1) ? winner : pool[rng.Next(pool.Count)];
                if (inBigBox && LstBigBoxStrip != null)
                {
                    LstBigBoxStrip.SelectedItem = pick;
                    LstBigBoxStrip.ScrollIntoView(pick);
                }
                else
                {
                    SelectGameInSplitInspector(pick);
                }

                await Task.Delay(delaysMs[Math.Min(i, delaysMs.Length - 1)]);
            }

            if (!inBigBox)
            {
                if (LvGames != null && LvGames.Visibility == Visibility.Visible)
                {
                    LvGames.SelectedItem = winner;
                    LvGames.ScrollIntoView(winner);
                }
                if (BdrLibrarySplitInspector != null)
                {
                    AnalyticalSpringEngine.AnimatePanelReveal(BdrLibrarySplitInspector, 8.0, 500f, 30f);
                }
                ShowOsdToast($"Рулетка выбрала: «{winner.Name}»! Нажмите «Играть» для запуска");
            }
            else
            {
                ShowOsdToast($"BigBox рулетка: «{winner.Name}» (Enter для запуска)");
            }
        }
        finally
        {
            _isRouletteRunning = false;
        }
    }

    // ==================== PLAYER ANALYTICS & BACKLOG FUNNEL ====================

    public void RefreshPlayerAnalyticsDashboard()
    {
        try
        {
            int total = _allGames.Count;
            long totalSecs = _allGames.Sum(g => g.TotalPlaySeconds);
            int totalLaunches = _allGames.Sum(g => g.LaunchCount);

            var rated = _allGames.Where(g => g.StarRating > 0).ToList();
            double avgRating = rated.Count > 0 ? rated.Average(g => g.StarRating) : 0.0;

            int completed = _allGames.Count(g =>
                string.Equals(g.CompletionStatus, "Пройдено", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(g.CompletionStatus, "Пройдено на 100%", StringComparison.OrdinalIgnoreCase));
            int playing = _allGames.Count(g =>
                string.Equals(g.CompletionStatus, "Играю сейчас", StringComparison.OrdinalIgnoreCase));
            int planned = _allGames.Count(g =>
                string.Equals(g.CompletionStatus, "В планах", StringComparison.OrdinalIgnoreCase));
            int unstarted = Math.Max(0, total - completed - playing - planned);

            double completionPct = total > 0 ? (completed * 100.0 / total) : 0.0;
            double totalHours = totalSecs / 3600.0;

            if (TxtAnalyticsTotalPlaytime != null)
                TxtAnalyticsTotalPlaytime.Text = $"{totalHours:F1} ч";
            if (TxtAnalyticsTotalLaunches != null)
                TxtAnalyticsTotalLaunches.Text = totalLaunches.ToString("N0");
            if (TxtAnalyticsAvgRating != null)
                TxtAnalyticsAvgRating.Text = $"{avgRating:F1} / 5";
            if (TxtAnalyticsCompletionRate != null)
                TxtAnalyticsCompletionRate.Text = $"{completionPct:F0}%";

            SetFunnelBar(PbAnalyticsCompleted, TxtAnalyticsCompletedCount, completed, total);
            SetFunnelBar(PbAnalyticsPlaying, TxtAnalyticsPlayingCount, playing, total);
            SetFunnelBar(PbAnalyticsPlanned, TxtAnalyticsPlannedCount, planned, total);
            SetFunnelBar(PbAnalyticsUnstarted, TxtAnalyticsUnstartedCount, unstarted, total);

            if (PnlAnalyticsTopPlatforms != null)
            {
                PnlAnalyticsTopPlatforms.Children.Clear();
                var top3 = _allGames
                    .Where(g => !string.IsNullOrWhiteSpace(g.Platform))
                    .GroupBy(g => g.Platform)
                    .OrderByDescending(g => g.Count())
                    .Take(3)
                    .ToList();

                if (top3.Count == 0)
                {
                    PnlAnalyticsTopPlatforms.Children.Add(new TextBlock
                    {
                        Text = "Добавьте ROM-файлы для статистики платформ",
                        Foreground = (Brush)FindResource("ThemeTextMuted"),
                        FontSize = 11.5
                    });
                }
                else
                {
                    foreach (var grp in top3)
                    {
                        var pill = new Border
                        {
                            Background = CreateFrozenBrush(0x8B, 0x5C, 0xF6, 0x22),
                            BorderBrush = CreateFrozenBrush(0x8B, 0x5C, 0xF6, 0x80),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(6),
                            Padding = new Thickness(10, 4, 10, 4),
                            Margin = new Thickness(0, 0, 8, 4)
                        };
                        pill.Child = new TextBlock
                        {
                            Text = $"{grp.Key}: {grp.Count()}",
                            Foreground = CreateFrozenBrush(0xDD, 0xD6, 0xFE),
                            FontSize = 11.5,
                            FontWeight = FontWeights.SemiBold
                        };
                        PnlAnalyticsTopPlatforms.Children.Add(pill);
                    }
                }
            }
        }
        catch { }
    }

    private static void SetFunnelBar(ProgressBar? bar, TextBlock? label, int count, int total)
    {
        double pct = total > 0 ? (count * 100.0 / total) : 0.0;
        if (bar != null) bar.Value = pct;
        if (label != null) label.Text = $"{count} ({pct:F0}%)";
    }

    // ==================== EXTERNAL EMULATORS & .LPL IMPORTER ====================

    private async void BtnScanExternalEmus_Click(object sender, RoutedEventArgs e)
    {
        ShowOsdToast("Поиск установленных эмуляторов (PCSX2, RPCS3, Dolphin, Switch, Cemu...)");
        var detected = await Task.Run(() => ExternalEmulatorBridge.DetectInstalledEmulators());
        _externalEmulators.ReplaceAll(detected);
        RefreshExternalEmulatorSelectors();
        if (TxtExternalEmuStats != null)
            TxtExternalEmuStats.Text = $"Обнаружено эмуляторов: {detected.Count(x => x.ExistsOnDisk)}";
        ShowOsdToast($"Готово: найдено {detected.Count(x => x.ExistsOnDisk)} внешних эмуляторов");
    }

    private void BtnAddExternalEmu_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите исполняемый файл внешнего эмулятора (.exe)",
            Filter = "Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        string exePath = dlg.FileName;
        string name = System.IO.Path.GetFileNameWithoutExtension(exePath);
        if (_externalEmulators.Any(x => x.ExecutablePath.Equals(exePath, StringComparison.OrdinalIgnoreCase)))
        {
            ShowOsdToast($"Эмулятор «{name}» уже добавлен");
            return;
        }

        _externalEmulators.Add(new ExternalEmulatorEntry
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = name,
            ExecutablePath = exePath,
            ArgumentsTemplate = "\"{ROM}\"",
            PlatformTags = "Custom",
            IsAutoDetected = false,
            IsEnabled = true
        });
        ExternalEmulatorBridge.SaveEmulators(_externalEmulators);
        RefreshExternalEmulatorSelectors();
        if (TxtExternalEmuStats != null)
            TxtExternalEmuStats.Text = $"Настроено эмуляторов: {_externalEmulators.Count(x => x.ExistsOnDisk)}";
        ShowOsdToast($"Добавлен внешний эмулятор: {name}");
    }

    private void BtnRemoveExternalEmu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ExternalEmulatorEntry entry })
        {
            _externalEmulators.Remove(entry);
            ExternalEmulatorBridge.SaveEmulators(_externalEmulators);
            RefreshExternalEmulatorSelectors();
        }
    }

    private async void BtnImportRetroArchLpl_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Выберите папку playlists/ оригинального RetroArch (.lpl)"
        };
        if (dlg.ShowDialog() != true) return;

        string dir = dlg.FolderName;
        var snapshot = _allGames.ToList();
        var (imported, added, scanned) = await Task.Run(() =>
        {
            var items = ExternalEmulatorBridge.ImportRetroArchPlaylists(dir, snapshot, out int a, out int s);
            return (items, a, s);
        });

        if (imported.Count > 0)
        {
            _allGames.AddRange(imported);
            RefreshAllLibraryFiltersAndCounts();
            SaveLibraryToPlaylist();
            ApplyGamesFilter();
            UpdateStats();
            RefreshPlayerAnalyticsDashboard();
            InitCommandPaletteItems();
            _ = CoverArtService.LoadCoversParallelAsync(imported, forceRefresh: false);
        }

        ShowNotification("Импорт плейлистов RetroArch (.lpl)",
            $"Просканировано записей: {scanned}\nДобавлено новых игр: {added}");
    }

    // ==================== SUBSEQUENCE FUZZY COMMAND PALETTE SCORING ====================

    private static int ComputeFuzzyScore(string text, string query)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return 0;

        string t = text.ToLowerInvariant();
        string q = query.ToLowerInvariant();

        int idx = t.IndexOf(q, StringComparison.Ordinal);
        if (idx >= 0)
        {
            int score = 1000 - Math.Min(idx, 200);
            if (idx == 0) score += 500;
            else if (!char.IsLetterOrDigit(t[idx - 1])) score += 250;
            return score;
        }

        // Subsequence fuzzy match
        int ti = 0;
        int qi = 0;
        int consecutive = 0;
        int totalScore = 100;

        while (ti < t.Length && qi < q.Length)
        {
            if (t[ti] == q[qi])
            {
                totalScore += 20 + consecutive * 25;
                if (ti == 0 || !char.IsLetterOrDigit(t[ti - 1]))
                    totalScore += 35;
                consecutive++;
                qi++;
            }
            else
            {
                consecutive = 0;
                totalScore -= 1;
            }
            ti++;
        }

        return qi == q.Length ? Math.Max(10, totalScore) : 0;
    }

    private void AppendStudio260CommandPaletteItems()
    {
        _allPaletteItems.Add(new CommandPaletteItem
        {
            Title = "Случайная игра (Мне повезёт!)",
            Subtitle = "Запустить рулетку выбора случайной игры из вашей коллекции",
            Category = "Команда",
            IconGeometry = "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zM7.5 18c-.83 0-1.5-.67-1.5-1.5S6.67 15 7.5 15s1.5.67 1.5 1.5S8.33 18 7.5 18zm0-9C6.67 9 6 8.33 6 7.5S6.67 6 7.5 6 9 6.67 9 7.5 8.33 9 7.5 9zm4.5 4.5c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5zm4.5 4.5c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5zm0-9c-.83 0-1.5-.67-1.5-1.5S15.67 6 16.5 6s1.5.67 1.5 1.5S17.33 9 16.5 9z",
            IconBrush = CreateFrozenBrush(0xF5, 0x9E, 0x0B),
            IconBgBrush = CreateFrozenBrush(0xF5, 0x9E, 0x0B, 40),
            BadgeTextBrush = CreateFrozenBrush(0xFD, 0xE6, 0x8A),
            BadgeBgBrush = CreateFrozenBrush(0xF5, 0x9E, 0x0B, 40),
            BadgeBorderBrush = CreateFrozenBrush(0xF5, 0x9E, 0x0B),
            ExecuteAction = () => _ = StartRandomGameRouletteAsync()
        });

        _allPaletteItems.Add(new CommandPaletteItem
        {
            Title = "Импорт плейлистов RetroArch (.lpl)",
            Subtitle = "Импортировать игры из существующих файлов плейлистов RetroArch",
            Category = "Команда",
            IconGeometry = "M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z",
            IconBrush = CreateFrozenBrush(0x38, 0xBD, 0xF8),
            IconBgBrush = CreateFrozenBrush(0x38, 0xBD, 0xF8, 40),
            BadgeTextBrush = CreateFrozenBrush(0xBA, 0xE6, 0xFD),
            BadgeBgBrush = CreateFrozenBrush(0x38, 0xBD, 0xF8, 40),
            BadgeBorderBrush = CreateFrozenBrush(0x38, 0xBD, 0xF8),
            ExecuteAction = () => BtnImportRetroArchLpl_Click(this, new RoutedEventArgs())
        });

        _allPaletteItems.Add(new CommandPaletteItem
        {
            Title = "Внешние эмуляторы (PCSX2, RPCS3, Dolphin, Switch)",
            Subtitle = "Открыть сканер и настройки автономных эмуляторов",
            Category = "Команда",
            IconGeometry = "M20 12h-2V8h-4V6a2 2 0 0 0-4 0v2H6a2 2 0 0 0-2 2v4h2a2 2 0 0 1 0 4H4v4a2 2 0 0 0 2 2h4v-2a2 2 0 0 1 4 0v2h4a2 2 0 0 0 2-2v-4h-2a2 2 0 0 1 0-4h2v-4a2 2 0 0 0-2-2z",
            IconBrush = CreateFrozenBrush(0xA7, 0x8B, 0xFA),
            IconBgBrush = CreateFrozenBrush(0x8B, 0x5C, 0xF6, 40),
            BadgeTextBrush = CreateFrozenBrush(0xDD, 0xD6, 0xFE),
            BadgeBgBrush = CreateFrozenBrush(0x8B, 0x5C, 0xF6, 40),
            BadgeBorderBrush = CreateFrozenBrush(0x8B, 0x5C, 0xF6),
            ExecuteAction = () =>
            {
                NavBtn_Click(NavBtnImport, new RoutedEventArgs());
                BtnScanExternalEmus_Click(this, new RoutedEventArgs());
            }
        });

        _allPaletteItems.Add(new CommandPaletteItem
        {
            Title = "Статистика игрока и бэклог",
            Subtitle = "Показать аналитику времени игры, воронки прохождения и топ платформ",
            Category = "Команда",
            IconGeometry = "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zM9 17H7v-7h2v7zm4 0h-2V7h2v10zm4 0h-2v-4h2v4z",
            IconBrush = CreateFrozenBrush(0x34, 0xD3, 0x99),
            IconBgBrush = CreateFrozenBrush(0x10, 0xB9, 0x81, 40),
            BadgeTextBrush = CreateFrozenBrush(0xA7, 0xF3, 0xD0),
            BadgeBgBrush = CreateFrozenBrush(0x10, 0xB9, 0x81, 40),
            BadgeBorderBrush = CreateFrozenBrush(0x10, 0xB9, 0x81),
            ExecuteAction = () =>
            {
                RefreshPlayerAnalyticsDashboard();
                NavBtn_Click(NavBtnMainMenu, new RoutedEventArgs());
            }
        });

        foreach (var emu in _externalEmulators.Where(x => x.IsEnabled && x.ExistsOnDisk))
        {
            var capturedEmu = emu;
            _allPaletteItems.Add(new CommandPaletteItem
            {
                Title = $"Эмулятор: {capturedEmu.Name}",
                Subtitle = $"{capturedEmu.PlatformTags} • {capturedEmu.ExecutablePath}",
                Category = "Эмулятор",
                IconGeometry = "M21 6H3c-1.1 0-2 .9-2 2v8c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2z",
                IconBrush = CreateFrozenBrush(0x38, 0xBD, 0xF8),
                IconBgBrush = CreateFrozenBrush(0x38, 0xBD, 0xF8, 35),
                BadgeTextBrush = CreateFrozenBrush(0xBA, 0xE6, 0xFD),
                BadgeBgBrush = CreateFrozenBrush(0x38, 0xBD, 0xF8, 35),
                BadgeBorderBrush = CreateFrozenBrush(0x38, 0xBD, 0xF8),
                ExecuteAction = () =>
                {
                    try { Process.Start(new ProcessStartInfo(capturedEmu.ExecutablePath) { UseShellExecute = true }); }
                    catch { }
                }
            });
        }

        foreach (var core in CoreManager.AllCoresCatalog.Take(60))
        {
            var capturedCore = core;
            _allPaletteItems.Add(new CommandPaletteItem
            {
                Title = $"Ядро: {capturedCore.Name}",
                Subtitle = $"{capturedCore.Platform} • {capturedCore.DllName}",
                Category = "Ядро",
                IconGeometry = "M20 12h-2V8h-4V6a2 2 0 0 0-4 0v2H6a2 2 0 0 0-2 2v4h2a2 2 0 0 1 0 4H4v4a2 2 0 0 0 2 2h4v-2a2 2 0 0 1 4 0v2h4a2 2 0 0 0 2-2v-4h-2a2 2 0 0 1 0-4h2v-4a2 2 0 0 0-2-2z",
                IconBrush = CreateFrozenBrush(0xA7, 0x8B, 0xFA),
                IconBgBrush = CreateFrozenBrush(0x8B, 0x5C, 0xF6, 35),
                BadgeTextBrush = CreateFrozenBrush(0xDD, 0xD6, 0xFE),
                BadgeBgBrush = CreateFrozenBrush(0x8B, 0x5C, 0xF6, 35),
                BadgeBorderBrush = CreateFrozenBrush(0x8B, 0x5C, 0xF6),
                ExecuteAction = () =>
                {
                    NavBtn_Click(NavBtnCores, new RoutedEventArgs());
                    if (TxtSearchCores != null) TxtSearchCores.Text = capturedCore.Name;
                }
            });
        }

        foreach (var setting in _allSettings.Take(80))
        {
            var capturedSetting = setting;
            _allPaletteItems.Add(new CommandPaletteItem
            {
                Title = capturedSetting.Title,
                Subtitle = $"{capturedSetting.CategoryName} • {capturedSetting.CfgKey}",
                Category = "Настройка",
                IconGeometry = "M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58a.49.49 0 0 0 .12-.61l-1.92-3.32a.488.488 0 0 0-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54a.484.484 0 0 0-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58a.49.49 0 0 0-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58z",
                IconBrush = CreateFrozenBrush(0x94, 0xA3, 0xB8),
                IconBgBrush = CreateFrozenBrush(0x64, 0x74, 0x8B, 35),
                BadgeTextBrush = CreateFrozenBrush(0xCB, 0xD5, 0xE1),
                BadgeBgBrush = CreateFrozenBrush(0x64, 0x74, 0x8B, 35),
                BadgeBorderBrush = CreateFrozenBrush(0x64, 0x74, 0x8B),
                ExecuteAction = () =>
                {
                    NavBtn_Click(NavBtnSettings, new RoutedEventArgs());
                    if (TxtGlobalSearch != null) TxtGlobalSearch.Text = capturedSetting.Title;
                }
            });
        }
    }
}
