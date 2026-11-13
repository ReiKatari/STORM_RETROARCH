using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StormRetroArch;

/// <summary>
/// Phase 2.6.0 Studio UI, Regional Cover Art, Strict Platform Cores, Libretro Cheats Database and RAM Trainer Integrator.
/// </summary>
public partial class MainWindow
{
    public void InitStudio230Subsystem()
    {
        LogCheckpoint("InitStudio230Subsystem start");
        try
        {
            PopulateCoresPlatformFilter();
            RefreshAllLibraryFiltersAndCounts();
        }
        catch (Exception ex)
        {
            LogCheckpoint("InitStudio230Subsystem warning: " + ex.Message);
        }
    }

    public async Task RunScreenshot230SuiteAsync(string outDir)
    {
        string logPath = Path.Combine(outDir, "run230.log");
        void Log(string msg) { try { File.AppendAllText(logPath, msg + "\n"); } catch { } }

        try
        {
            Log("RunScreenshot230SuiteAsync started into " + outDir);
            Directory.CreateDirectory(outDir);

            Width = 1600;
            Height = 960;
            UpdateLayout();

            void Snap(string fileName)
            {
                AnalyticalSpringEngine.FlushAllImmediately();
                UpdateLayout();
                int sw = 1600;
                int sh = 960;
                var rtb = new RenderTargetBitmap(sw, sh, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(this);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                string targetFile = Path.Combine(outDir, fileName);
                using var fs = File.Create(targetFile);
                enc.Save(fs);
                Log("Saved " + targetFile + " (" + new FileInfo(targetFile).Length + " bytes)");
            }

            // Backup user playlist
            string playlistDir = Path.Combine(CoreManager.AppDir, "playlists");
            string libraryJsonPath = Path.Combine(playlistDir, "storm_library.json");
            string libraryJsonBackup = Path.Combine(playlistDir, "storm_library.json.bak230");
            try
            {
                if (File.Exists(libraryJsonPath))
                {
                    File.Copy(libraryJsonPath, libraryJsonBackup, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                Log("Backup playlist warning: " + ex.Message);
            }

            // 1. Create demo roms folder
            string demoDir = Path.Combine(outDir, "_demo_roms");
            Directory.CreateDirectory(demoDir);

            void CreateNes(string filename)
            {
                byte[] data = new byte[32784];
                data[0] = 0x4E; data[1] = 0x45; data[2] = 0x53; data[3] = 0x1A; // NES<EOF>
                data[4] = 2; // 2x 16KB PRG
                data[5] = 1; // 1x 8KB CHR
                File.WriteAllBytes(Path.Combine(demoDir, filename), data);
            }

            void CreateSfc(string filename)
            {
                byte[] data = new byte[65536];
                File.WriteAllBytes(Path.Combine(demoDir, filename), data);
            }

            void CreateMd(string filename)
            {
                byte[] data = new byte[65536];
                byte[] magic = Encoding.ASCII.GetBytes("SEGA MEGA DRIVE ");
                Array.Copy(magic, 0, data, 0x100, magic.Length);
                File.WriteAllBytes(Path.Combine(demoDir, filename), data);
            }

            void CreateDummy(string filename, int size)
            {
                byte[] data = new byte[size];
                if (filename.EndsWith(".chd", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] chdMagic = Encoding.ASCII.GetBytes("MComprHD");
                    Array.Copy(chdMagic, 0, data, 0, chdMagic.Length);
                }
                File.WriteAllBytes(Path.Combine(demoDir, filename), data);
            }

            CreateNes("Contra (Japan).nes");
            CreateNes("Contra (USA).nes");
            CreateNes("Probotector (Europe).nes");
            CreateSfc("Super Metroid (Japan, USA).sfc");
            CreateMd("Castlevania - Bloodlines (USA).md");
            CreateMd("Vampire Killer (Japan).md");
            CreateMd("Streets of Rage 2 (USA).md");
            CreateMd("Bare Knuckle II (Japan).md");
            CreateDummy("Metal Gear Solid (USA).chd", 4096);
            CreateDummy("God of War II (USA).iso", 32768);

            // Scan directory through main pipeline
            ScanDirectory(demoDir);
            if (StormAlertOverlay != null) StormAlertOverlay.Visibility = Visibility.Collapsed;

            // Parallel regional cover art fetch + cheat count population
            try
            {
                using var ctsCover = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                await CoverArtService.LoadCoversParallelAsync(_allGames, forceRefresh: true);
            }
            catch (Exception ex)
            {
                Log("Cover loading notice: " + ex.Message);
            }

            try
            {
                using var ctsCheat = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await CheatManager.PopulateCheatCountsAsync(_allCheatFilesMaster, ctsCheat.Token);
            }
            catch (Exception ex)
            {
                Log("Cheats count notice: " + ex.Message);
            }

            RefreshAllLibraryFiltersAndCounts();
            await Task.Delay(400);

            // -------------------------------------------------------------
            // SCREENSHOT 1: 01_cheats_all_26800_games.png
            // ViewCheats, "Все игры базы (26 797+)", CmbCheatsPlatformFilter visible, real cheat selected
            // -------------------------------------------------------------
            NavBtn_Click(NavBtnCheats, new RoutedEventArgs());
            if (BtnCheatsFilterAll != null)
            {
                BtnCheatsFilter_Click(BtnCheatsFilterAll, new RoutedEventArgs());
            }
            await Task.Delay(300);
            if (ListCheatFiles != null && ListCheatFiles.Items.Count > 0)
            {
                if (ListCheatFiles.SelectedIndex < 0) ListCheatFiles.SelectedIndex = 0;
            }
            await Task.Delay(250);
            Snap("01_cheats_all_26800_games.png");

            // -------------------------------------------------------------
            // SCREENSHOT 2: 02_library_regional_covers_and_counts.png
            // ViewLibrary ("Все платформы"), distinct regional covers Japan vs USA vs Europe, dynamic counts on chips & filters
            // -------------------------------------------------------------
            NavBtn_Click(NavBtnLibrary, new RoutedEventArgs());
            _selectedChipPlatform = "Все платформы";
            SetSelectedPlatform("Все платформы");
            _selectedSmartCollection = "Все игры";
            UpdateSmartCollectionPills();
            UpdatePlatformFilterChips();
            ApplyGamesFilter();
            await Task.Delay(350);
            Snap("02_library_regional_covers_and_counts.png");

            // -------------------------------------------------------------
            // SCREENSHOT 3: 03_library_strict_platform_cores.png
            // Specific platform selected ("Super Nintendo (SNES)"), CmbPlatformCoreTarget synced, only SNES cores shown
            // -------------------------------------------------------------
            string targetPlat = "Super Nintendo (SNES)";
            _selectedChipPlatform = targetPlat;
            SetSelectedPlatform(targetPlat);
            UpdatePlatformFilterChips();
            UpdateLibraryCoreFilterDropdown(targetPlat);
            SyncPlatformCoreTargetTo(targetPlat);
            ApplyGamesFilter();
            RefreshAllLibraryFiltersAndCounts();
            ApplyGamesFilter();
            var snesSample = _filteredGames.FirstOrDefault(g => g.Platform == targetPlat) ?? _allGames.FirstOrDefault(g => g.Platform == targetPlat);
            if (snesSample != null)
            {
                OpenCoreSelectorForGame(snesSample);
            }
            await Task.Delay(300);
            Snap("03_library_strict_platform_cores.png");
            CloseCoreSelectorModal();

            // -------------------------------------------------------------
            // SCREENSHOT 4: 04_cheats_library_matched.png
            // ViewCheats in "Моя библиотека (10)" mode - all 10 added games displayed
            // -------------------------------------------------------------
            NavBtn_Click(NavBtnCheats, new RoutedEventArgs());
            if (BtnCheatsFilterLibrary != null)
            {
                BtnCheatsFilter_Click(BtnCheatsFilterLibrary, new RoutedEventArgs());
            }
            await Task.Delay(300);
            if (ListCheatFiles != null && ListCheatFiles.Items.Count > 0)
            {
                ListCheatFiles.SelectedIndex = 0;
            }
            await Task.Delay(250);
            Snap("04_cheats_library_matched.png");

            // -------------------------------------------------------------
            // CLEANUP: restore user state
            // -------------------------------------------------------------
            try
            {
                var demoItems = _allGames.Where(g => g.FilePath.StartsWith(demoDir, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var d in demoItems)
                {
                    _allGames.Remove(d);
                    _filteredGames.Remove(d);
                }

                if (File.Exists(libraryJsonBackup))
                {
                    File.Copy(libraryJsonBackup, libraryJsonPath, overwrite: true);
                    File.Delete(libraryJsonBackup);
                }
                else
                {
                    SaveLibraryToPlaylist();
                }

                Directory.Delete(demoDir, recursive: true);
                Log("Cleanup completed successfully");
            }
            catch (Exception ex)
            {
                Log("Cleanup notice: " + ex.Message);
            }

            Log("Screenshot suite finished successfully");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Log("RunScreenshot230SuiteAsync fatal error: " + ex);
            Environment.Exit(1);
        }
    }
}
