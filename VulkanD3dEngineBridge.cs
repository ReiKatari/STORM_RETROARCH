using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace StormRetroArch;

/// <summary>
/// Hardware Engine Bridge for Vulkan, Direct3D 12, Direct3D 11, and OpenGL Core 4.6 (glcore)
/// with full native .slangp HSM Mega Bezel & Duimon Mega Bezel rendering.
/// Wraps retroarch_engine.exe on N:\STORM RETROARCH and embeds the hardware-accelerated
/// viewport directly into STORM RETROARCH.
/// </summary>
public class VulkanD3dEngineBridge
{
    public static VulkanD3dEngineBridge Current { get; } = new();

    private Process? _process;
    private EmbeddedGameHost? _currentEmbedHost;

    public bool IsRunning => _process != null && !_process.HasExited;
    public bool IsPaused { get; private set; }
    public bool IsFastForward { get; private set; }
    public string ActiveDriverName { get; private set; } = "vulkan";

    public event Action? SessionEnded;

    // Static set of core DLLs requiring Vulkan / D3D / glcore hardware acceleration
    private static readonly HashSet<string> VulkanD3dCoreDlls = new(StringComparer.OrdinalIgnoreCase)
    {
        "vita3k_libretro.dll",
        "rpcs3_libretro.dll",
        "pcsx2_libretro.dll",
        "play_libretro.dll",
        "pcee2_libretro.dll",
        "cemu_libretro.dll",
        "xenia_edge_libretro.dll",
        "xemu_libretro.dll",
        "dolphin_libretro.dll",
        "azahar_libretro.dll",
        "citra_libretro.dll",
        "panda3ds_libretro.dll",
        "supermodel_libretro.dll",
        "flycast_libretro.dll",
        "beetle_psx_hw_libretro.dll",
        "ppsspp_libretro.dll",
        "mupen64plus_next_libretro.dll",
        "parallel_n64_libretro.dll",
        "kronos_libretro.dll"
    };

    // Win32 Interop for HWND discovery and embedding
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    public static string ResolveEngineExePath()
    {
        string baseDir = Directory.Exists(CoreManager.InstallDir) ? CoreManager.InstallDir : CoreManager.AppDir;
        string enginePath = Path.Combine(baseDir, "retroarch_engine.exe");
        if (File.Exists(enginePath)) return enginePath;

        string raPath = Path.Combine(baseDir, "retroarch.exe");
        if (File.Exists(raPath)) return raPath;

        string nEngine = @"N:\STORM RETROARCH\retroarch_engine.exe";
        if (File.Exists(nEngine)) return nEngine;

        string nRa = @"N:\STORM RETROARCH\retroarch.exe";
        if (File.Exists(nRa)) return nRa;

        string localEngine = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "retroarch_engine.exe");
        if (File.Exists(localEngine)) return localEngine;

        return enginePath;
    }

    public static bool IsVulkanOrD3dCore(string? coreDllName)
    {
        if (string.IsNullOrWhiteSpace(coreDllName)) return false;
        string fn = Path.GetFileName(coreDllName);
        return VulkanD3dCoreDlls.Contains(fn);
    }

    public static bool ShouldRouteToHardwareEngine(string? coreDllName, string? activeSlangpPath)
    {
        string exePath = ResolveEngineExePath();
        if (!File.Exists(exePath)) return false;

        string backend = SettingsManager.Current.GraphicsBackend;
        if (string.Equals(backend, "NativeAVX2", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(backend, "Vulkan", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(backend, "Direct3D12", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(backend, "Direct3D11", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(backend, "OpenGLCore", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(backend, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            if (IsVulkanOrD3dCore(coreDllName) || !string.IsNullOrWhiteSpace(activeSlangpPath))
                return true;
        }

        return false;
    }

    public static string SelectBestVideoDriver(string? coreDllName, string? activeSlangpPath)
    {
        string backend = SettingsManager.Current.GraphicsBackend;
        if (string.Equals(backend, "Vulkan", StringComparison.OrdinalIgnoreCase)) return "vulkan";
        if (string.Equals(backend, "Direct3D12", StringComparison.OrdinalIgnoreCase)) return "d3d12";
        if (string.Equals(backend, "Direct3D11", StringComparison.OrdinalIgnoreCase)) return "d3d11";
        if (string.Equals(backend, "OpenGLCore", StringComparison.OrdinalIgnoreCase)) return "glcore";

        // Auto selection
        string fn = string.IsNullOrWhiteSpace(coreDllName) ? "" : Path.GetFileName(coreDllName).ToLowerInvariant();

        if (fn.Contains("pcsx2"))
        {
            // PCSX2 libretro runs great on d3d11 in RetroArch; if Mega Bezel slangp is active, d3d11 supports Slang shaders natively
            return "d3d11";
        }

        if (fn.Contains("vita3k") || fn.Contains("rpcs3") || fn.Contains("cemu") ||
            fn.Contains("xenia") || fn.Contains("dolphin") || fn.Contains("flycast") ||
            fn.Contains("beetle_psx_hw"))
        {
            return "vulkan";
        }

        return "vulkan";
    }

    public async Task<bool> StartAsync(
        RomItem rom,
        string coreDllPath,
        EmbeddedGameHost? embedHost,
        BezelPresetItem? activeBezel,
        string? explicitSlangpPath,
        Action<string>? onStatus = null)
    {
        Stop();

        string exePath = ResolveEngineExePath();
        if (!File.Exists(exePath))
        {
            onStatus?.Invoke("Исполняемый файл retroarch_engine.exe не найден.");
            return false;
        }

        if (!File.Exists(coreDllPath))
        {
            onStatus?.Invoke($"Библиотека ядра {Path.GetFileName(coreDllPath)} не найдена.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(rom.FilePath) || !File.Exists(rom.FilePath))
        {
            onStatus?.Invoke("Файл игры не найден.");
            return false;
        }

        string activeSlangp = !string.IsNullOrWhiteSpace(explicitSlangpPath)
            ? explicitSlangpPath
            : (activeBezel != null && !string.IsNullOrWhiteSpace(activeBezel.SlangpPath) ? activeBezel.SlangpPath : "");

        string driver = SelectBestVideoDriver(Path.GetFileName(coreDllPath), activeSlangp);
        ActiveDriverName = driver;

        string baseDir = Directory.Exists(CoreManager.InstallDir) ? CoreManager.InstallDir : CoreManager.AppDir;
        string configDir = Path.Combine(baseDir, "config");
        Directory.CreateDirectory(configDir);
        string cfgPath = Path.Combine(configDir, "storm_hw_runtime.cfg");

        string systemDir = Path.Combine(baseDir, "system");
        string saveDir = Path.Combine(baseDir, "save sync");
        string stateDir = Path.Combine(baseDir, "states");
        string screenDir = Path.Combine(baseDir, "screenshots");
        string coresDir = Path.Combine(baseDir, "cores");
        string infoDir = Directory.Exists(Path.Combine(baseDir, "info")) ? Path.Combine(baseDir, "info") : CoreManager.InfoDir;
        string shaderDir = Path.Combine(baseDir, "shaders");
        string overlayDir = Path.Combine(baseDir, "overlays");

        try
        {
            Directory.CreateDirectory(systemDir);
            Directory.CreateDirectory(saveDir);
            Directory.CreateDirectory(stateDir);
            Directory.CreateDirectory(screenDir);
        }
        catch { }

        var sb = new StringBuilder();
        sb.AppendLine($"video_driver = \"{driver}\"");
        sb.AppendLine($"system_directory = \"{systemDir.Replace('\\', '/')}\"");
        sb.AppendLine($"savefile_directory = \"{saveDir.Replace('\\', '/')}\"");
        sb.AppendLine($"savestate_directory = \"{stateDir.Replace('\\', '/')}\"");
        sb.AppendLine($"screenshot_directory = \"{screenDir.Replace('\\', '/')}\"");
        sb.AppendLine($"libretro_directory = \"{coresDir.Replace('\\', '/')}\"");
        sb.AppendLine($"libretro_info_path = \"{infoDir.Replace('\\', '/')}\"");
        sb.AppendLine($"video_shader_dir = \"{shaderDir.Replace('\\', '/')}\"");
        sb.AppendLine($"overlay_directory = \"{overlayDir.Replace('\\', '/')}\"");
        sb.AppendLine("video_fullscreen = \"false\"");
        sb.AppendLine("video_windowed_fullscreen = \"false\"");
        sb.AppendLine("video_vsync = \"true\"");
        sb.AppendLine("pause_nonactive = \"false\"");
        sb.AppendLine("network_cmd_enable = \"true\"");
        sb.AppendLine("network_cmd_port = \"55355\"");
        sb.AppendLine("menu_driver = \"ozone\"");
        sb.AppendLine("input_joypad_driver = \"xinput\"");

        string? shaderArg = null;
        if (!string.IsNullOrWhiteSpace(activeSlangp) && File.Exists(activeSlangp))
        {
            shaderArg = activeSlangp;
            sb.AppendLine("video_shader_enable = \"true\"");
            sb.AppendLine($"video_shader = \"{activeSlangp.Replace('\\', '/')}\"");
        }
        else if (activeBezel != null && !string.IsNullOrWhiteSpace(activeBezel.ImagePath) && File.Exists(activeBezel.ImagePath))
        {
            string overlayCfg = BezelManager.EnsureRetroArchOverlayCfg(activeBezel);
            if (!string.IsNullOrWhiteSpace(overlayCfg) && File.Exists(overlayCfg))
            {
                sb.AppendLine("input_overlay_enable = \"true\"");
                sb.AppendLine($"input_overlay = \"{overlayCfg.Replace('\\', '/')}\"");
                sb.AppendLine("input_overlay_opacity = \"1.000000\"");
                sb.AppendLine("input_overlay_auto_scale = \"true\"");
                sb.AppendLine("input_overlay_scale_landscape = \"1.000000\"");
            }
        }

        File.WriteAllText(cfgPath, sb.ToString(), Encoding.UTF8);

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? baseDir,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var argSb = new StringBuilder();
        argSb.Append($"-L \"{coreDllPath}\" ");
        argSb.Append($"--config \"{cfgPath}\" ");
        if (!string.IsNullOrWhiteSpace(shaderArg))
        {
            argSb.Append($"--set-shader \"{shaderArg}\" ");
        }
        argSb.Append($"\"{rom.FilePath}\"");

        psi.Arguments = argSb.ToString();

        try
        {
            _currentEmbedHost = embedHost;
            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.Exited += OnProcessExited;
            _process.Start();

            onStatus?.Invoke($"Запуск аппаратного движка RetroArch ({driver.ToUpperInvariant()})...");

            // Poll for window handle up to 7000ms
            IntPtr targetHwnd = IntPtr.Zero;
            for (int i = 0; i < 88; i++)
            {
                await Task.Delay(80);
                if (_process == null || _process.HasExited) break;

                try
                {
                    _process.Refresh();
                    if (_process.MainWindowHandle != IntPtr.Zero && IsWindowVisible(_process.MainWindowHandle))
                    {
                        targetHwnd = _process.MainWindowHandle;
                        break;
                    }

                    uint pid = (uint)_process.Id;
                    IntPtr foundHwnd = IntPtr.Zero;
                    EnumWindows((hWnd, lParam) =>
                    {
                        GetWindowThreadProcessId(hWnd, out uint wndPid);
                        if (wndPid == pid && IsWindowVisible(hWnd))
                        {
                            foundHwnd = hWnd;
                            return false;
                        }
                        return true;
                    }, IntPtr.Zero);

                    if (foundHwnd != IntPtr.Zero)
                    {
                        targetHwnd = foundHwnd;
                        break;
                    }
                }
                catch { }
            }

            if (targetHwnd != IntPtr.Zero && embedHost != null)
            {
                embedHost.Dispatcher.Invoke(() => embedHost.AttachGameWindow(targetHwnd));
                onStatus?.Invoke("Аппаратный контекст успешно встроен в окно STORM RETROARCH.");
                return true;
            }

            return _process != null && !_process.HasExited;
        }
        catch (Exception ex)
        {
            onStatus?.Invoke($"Ошибка запуска аппаратного движка: {ex.Message}");
            Stop();
            return false;
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        try
        {
            _currentEmbedHost?.Dispatcher.Invoke(() => _currentEmbedHost.DetachGameWindow());
        }
        catch { }

        IsPaused = false;
        IsFastForward = false;
        SessionEnded?.Invoke();
    }

    public void TogglePause()
    {
        UdpCommandBridge.PauseToggle();
        IsPaused = !IsPaused;
    }

    public void SaveState() => UdpCommandBridge.SaveState();
    public void LoadState() => UdpCommandBridge.LoadState();
    public void Reset() => UdpCommandBridge.Reset();

    public void ToggleFastForward()
    {
        UdpCommandBridge.FastForward();
        IsFastForward = !IsFastForward;
    }

    public void TakeScreenshot() => UdpCommandBridge.Screenshot();
    public void ToggleMute() => UdpCommandBridge.Mute();

    public void Stop()
    {
        if (_process != null && !_process.HasExited)
        {
            try
            {
                UdpCommandBridge.Quit();
            }
            catch { }

            try
            {
                if (!_process.WaitForExit(700))
                {
                    _process.Kill(true);
                }
            }
            catch { }
        }

        try
        {
            _currentEmbedHost?.Dispatcher.Invoke(() => _currentEmbedHost.DetachGameWindow());
        }
        catch { }

        _process = null;
        _currentEmbedHost = null;
        IsPaused = false;
        IsFastForward = false;
    }
}
