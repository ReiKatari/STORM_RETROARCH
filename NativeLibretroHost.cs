using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StormRetroArch;

public class NativeLibretroHost
{
    private static readonly Lazy<NativeLibretroHost> _instance = new(() => new NativeLibretroHost());
    public static NativeLibretroHost Current => _instance.Value;

    private static readonly uint[] Rgb565Lut = new uint[65536];
    private static readonly uint[] Rgb565ScanlineLut = new uint[65536];
    private static readonly uint[] Rgb1555Lut = new uint[65536];
    private static readonly uint[] Rgb1555ScanlineLut = new uint[65536];

    static NativeLibretroHost()
    {
        for (int val = 0; val < 65536; val++)
        {
            // RGB565
            byte b565 = (byte)((val & 0x1F) * 255 / 31);
            byte g565 = (byte)(((val >> 5) & 0x3F) * 255 / 63);
            byte r565 = (byte)(((val >> 11) & 0x1F) * 255 / 31);
            Rgb565Lut[val] = 0xFF000000u | ((uint)r565 << 16) | ((uint)g565 << 8) | b565;

            byte sb565 = (byte)(b565 * 3 / 4);
            byte sg565 = (byte)(g565 * 3 / 4);
            byte sr565 = (byte)(r565 * 3 / 4);
            Rgb565ScanlineLut[val] = 0xFF000000u | ((uint)sr565 << 16) | ((uint)sg565 << 8) | sb565;

            // 0RGB1555
            byte b1555 = (byte)((val & 0x1F) * 255 / 31);
            byte g1555 = (byte)(((val >> 5) & 0x1F) * 255 / 31);
            byte r1555 = (byte)(((val >> 10) & 0x1F) * 255 / 31);
            Rgb1555Lut[val] = 0xFF000000u | ((uint)r1555 << 16) | ((uint)g1555 << 8) | b1555;

            byte sb1555 = (byte)(b1555 * 3 / 4);
            byte sg1555 = (byte)(g1555 * 3 / 4);
            byte sr1555 = (byte)(r1555 * 3 / 4);
            Rgb1555ScanlineLut[val] = 0xFF000000u | ((uint)sr1555 << 16) | ((uint)sg1555 << 8) | sb1555;
        }
    }

    // ==================== LIBRETRO C API STRUCTS ====================
    [StructLayout(LayoutKind.Sequential)]
    public struct RetroGameGeometry
    {
        public uint BaseWidth;
        public uint BaseHeight;
        public uint MaxWidth;
        public uint MaxHeight;
        public float AspectRatio;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RetroSystemTiming
    {
        public double Fps;
        public double SampleRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RetroSystemAvInfo
    {
        public RetroGameGeometry Geometry;
        public RetroSystemTiming Timing;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RetroSystemInfo
    {
        public IntPtr LibraryName;
        public IntPtr LibraryVersion;
        public IntPtr ValidExtensions;
        [MarshalAs(UnmanagedType.I1)]
        public bool NeedFullpath;
        [MarshalAs(UnmanagedType.I1)]
        public bool BlockExtract;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RetroGameInfo
    {
        public IntPtr Path;
        public IntPtr Data;
        public nuint Size;
        public IntPtr Meta;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RetroVariable
    {
        public IntPtr Key;
        public IntPtr Value;
    }

    // ==================== DELEGATES ====================
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RetroKeyboardEvent([MarshalAs(UnmanagedType.I1)] bool down, uint keycode, uint character, ushort key_modifiers);

    private RetroKeyboardEvent? _keyboardCallback;

    public delegate void RetroCheatReset();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RetroCheatSet(uint index, [MarshalAs(UnmanagedType.I1)] bool enable, [MarshalAs(UnmanagedType.LPUTF8Str)] string code);

    public delegate void RetroInit();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RetroDeinit();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate uint RetroApiVersion();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RetroGetSystemInfo(out RetroSystemInfo info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RetroGetSystemAvInfo(out RetroSystemAvInfo info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public delegate bool RetroEnvironmentCallback(uint cmd, IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RetroSetEnvironment(RetroEnvironmentCallback cb);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RetroVideoRefreshCallback(IntPtr data, uint width, uint height, nuint pitch);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RetroSetVideoRefresh(RetroVideoRefreshCallback cb);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RetroAudioSampleCallback(short left, short right);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RetroSetAudioSample(RetroAudioSampleCallback cb);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate nuint RetroAudioSampleBatchCallback(IntPtr data, nuint frames);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RetroSetAudioSampleBatch(RetroAudioSampleBatchCallback cb);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RetroInputPollCallback();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RetroSetInputPoll(RetroInputPollCallback cb);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate short RetroInputStateCallback(uint port, uint device, uint index, uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RetroSetInputState(RetroInputStateCallback cb);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RetroSetControllerPortDevice(uint port, uint device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RetroReset();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RetroRun();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate nuint RetroSerializeSize();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public delegate bool RetroSerialize(IntPtr data, nuint size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public delegate bool RetroUnserialize(IntPtr data, nuint size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public delegate bool RetroLoadGame(ref RetroGameInfo game);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RetroUnloadGame();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate uint RetroGetRegion();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr RetroGetMemoryData(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate nuint RetroGetMemorySize(uint id);

    // Win32 API
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // WinMM Audio API
    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [DllImport("winmm.dll")]
    private static extern int waveOutOpen(out IntPtr hWaveOut, uint uDeviceID, ref WAVEFORMATEX lpFormat, IntPtr dwCallback, IntPtr dwInstance, uint dwFlags);

    [DllImport("winmm.dll")]
    private static extern int waveOutPrepareHeader(IntPtr hWaveOut, ref WAVEHDR lpWaveOutHdr, uint uSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutWrite(IntPtr hWaveOut, ref WAVEHDR lpWaveOutHdr, uint uSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, ref WAVEHDR lpWaveOutHdr, uint uSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutReset(IntPtr hWaveOut);

    [DllImport("winmm.dll")]
    private static extern int waveOutClose(IntPtr hWaveOut);

    // ==================== FIELDS & RUNTIME STATE ====================
    // Cheats
    private RetroCheatReset? _retroCheatReset;
    private RetroCheatSet? _retroCheatSet;

    // Core Options
    private readonly Dictionary<string, string> _coreOptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IntPtr> _coreOptionPtrs = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _optionsUpdated = false;

    // Multi-Disc & Disc Control
    private readonly List<string> _multiDiscPaths = new();
    private int _currentDiscIndex = 0;
    private bool _isDiscEjected = false;

    public IReadOnlyList<string> MultiDiscPaths => _multiDiscPaths;
    public int CurrentDiscIndex => _currentDiscIndex;
    public bool IsDiscEjected => _isDiscEjected;

    // XInput Gamepad Integration
    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint dwPacketNumber;
        public XInputGamepad Gamepad;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint XInputGetStateDelegate(uint dwUserIndex, out XInputState pState);

    private static XInputGetStateDelegate? _xinputGetState;
    private static bool _xinputInitialized = false;

    private static void EnsureXInputInitialized()
    {
        if (_xinputInitialized) return;
        _xinputInitialized = true;

        string[] dlls = { "xinput1_4.dll", "xinput9_1_0.dll", "xinput1_3.dll" };
        foreach (var dll in dlls)
        {
            if (NativeLibrary.TryLoad(dll, out var hDll))
            {
                if (NativeLibrary.TryGetExport(hDll, "XInputGetState", out var pProc))
                {
                    _xinputGetState = Marshal.GetDelegateForFunctionPointer<XInputGetStateDelegate>(pProc);
                    break;
                }
            }
        }
    }

    private readonly XInputState[] _xinputStates = new XInputState[4];
    private readonly bool[] _xinputConnected = new bool[4];

    public bool ToggleDiscEject()
    {
        _isDiscEjected = !_isDiscEjected;
        return _isDiscEjected;
    }

    public int CycleNextDisc()
    {
        if (_multiDiscPaths.Count <= 1) return _currentDiscIndex;
        _currentDiscIndex = (_currentDiscIndex + 1) % _multiDiscPaths.Count;
        return _currentDiscIndex;
    }

    public int CyclePrevDisc()
    {
        if (_multiDiscPaths.Count <= 1) return _currentDiscIndex;
        _currentDiscIndex = (_currentDiscIndex - 1 + _multiDiscPaths.Count) % _multiDiscPaths.Count;
        return _currentDiscIndex;
    }

    public int ApplyCheats(IEnumerable<CheatItem> cheats)
    {
        lock (_coreLock)
        {
            if (_retroCheatReset == null || _retroCheatSet == null) return 0;
            try
            {
                _retroCheatReset();
                uint applied = 0;
                uint idx = 0;
                foreach (var c in cheats)
                {
                    if (!c.IsEnabled || string.IsNullOrWhiteSpace(c.Code)) continue;
                    _retroCheatSet(idx++, true, c.Code.Trim());
                    applied++;
                }
                return (int)applied;
            }
            catch { return 0; }
        }
    }

    private void LoadCoreOptions(string coreName, string coreDll)
    {
        lock (_coreLock)
        {
            ClearCoreOptionPointers();
            _coreOptions.Clear();

            string coreBaseName = Path.GetFileNameWithoutExtension(coreDll);
            string[] possiblePaths =
            {
                Path.Combine(CoreManager.AppDir, "config", coreName, coreName + ".opt"),
                Path.Combine(CoreManager.AppDir, "config", coreBaseName, coreBaseName + ".opt"),
                Path.Combine(CoreManager.AppDir, "retroarch-core-options.cfg")
            };

            foreach (var p in possiblePaths)
            {
                if (File.Exists(p))
                {
                    try
                    {
                        var lines = File.ReadAllLines(p);
                        foreach (var line in lines)
                        {
                            int eq = line.IndexOf('=');
                            if (eq > 0)
                            {
                                string k = line.Substring(0, eq).Trim();
                                string v = line.Substring(eq + 1).Trim().Trim('"');
                                if (!string.IsNullOrEmpty(k)) _coreOptions[k] = v;
                            }
                        }
                    }
                    catch { }
                }
            }
        }
    }

    private void ClearCoreOptionPointers()
    {
        foreach (var kvp in _coreOptionPtrs)
        {
            if (kvp.Value != IntPtr.Zero)
            {
                try { Marshal.FreeCoTaskMem(kvp.Value); } catch { }
            }
        }
        _coreOptionPtrs.Clear();
    }

    public void NotifyCoreOptionsUpdated()
    {
        lock (_coreLock)
        {
            _optionsUpdated = true;
            if (!string.IsNullOrEmpty(CurrentCoreName))
            {
                LoadCoreOptions(CurrentCoreName, "");
            }
        }
    }

    private IntPtr _hCoreDll = IntPtr.Zero;
    private Thread? _emulationThread;
    private volatile bool _isRunning;
    private volatile bool _isPaused;
    private volatile bool _isFastForward;
    private int _shaderFilterMode = 0; // 0 = Nearest, 1 = CRT Scanlines, 2 = Bilinear, 3 = LCD Grid
    private int _pixelFormat = 0; // 0 = 0RGB1555, 1 = XRGB8888, 2 = RGB565

    private RetroInit? _retroInit;
    private RetroDeinit? _retroDeinit;
    private RetroGetSystemInfo? _retroGetSystemInfo;
    private RetroGetSystemAvInfo? _retroGetSystemAvInfo;
    private RetroSetEnvironment? _retroSetEnvironment;
    private RetroSetVideoRefresh? _retroSetVideoRefresh;
    private RetroSetAudioSample? _retroSetAudioSample;
    private RetroSetAudioSampleBatch? _retroSetAudioSampleBatch;
    private RetroSetInputPoll? _retroSetInputPoll;
    private RetroSetInputState? _retroSetInputState;
    private RetroSetControllerPortDevice? _retroSetControllerPortDevice;
    private RetroReset? _retroReset;
    private RetroRun? _retroRun;
    private RetroSerializeSize? _retroSerializeSize;
    private RetroSerialize? _retroSerialize;
    private RetroUnserialize? _retroUnserialize;
    private RetroLoadGame? _retroLoadGame;
    private RetroUnloadGame? _retroUnloadGame;
    private RetroGetMemoryData? _retroGetMemoryData;
    private RetroGetMemorySize? _retroGetMemorySize;

    // Pinned Callbacks so GC NEVER collects them
    private RetroEnvironmentCallback? _envCallback;
    private RetroVideoRefreshCallback? _videoCallback;
    private RetroAudioSampleCallback? _audioCallback;
    private RetroAudioSampleBatchCallback? _audioBatchCallback;
    private RetroInputPollCallback? _inputPollCallback;
    private RetroInputStateCallback? _inputStateCallback;

    private IntPtr _ptrSystemDir = IntPtr.Zero;
    private IntPtr _ptrSaveDir = IntPtr.Zero;
    private IntPtr _ptrContentDir = IntPtr.Zero;

    private readonly object _coreLock = new();
    private Image? _targetImage;
    private WriteableBitmap? _bitmap;
    private byte[] _bgraBuffer = new byte[0];
    private int _lastWidth = 256;
    private int _lastHeight = 240;
    private int _isRendering = 0;
    private double _targetFps = 60.0;
    private uint _sampleRate = 44100;
    private nuint _serializeSize = 0;
    private int _activeStateSlot = 0;
    private volatile bool _isAudioMuted = false;
    private int _aspectMode = 0; // 0 = 4:3 (Uniform), 1 = 16:9, 2 = 1:1 Pixel, 3 = Fill

    // Circular Ring Rewind Buffer (360 frames = ~12 sec of smooth zero-copy rewind at 30 fps)
    private readonly byte[]?[] _rewindRing = new byte[360][];
    private int _rewindHead = 0;
    private int _rewindCount = 0;
    private ulong _frameCount = 0;

    public int RewindBufferCount => _rewindCount;
    public int RewindBufferCapacity => _rewindRing.Length;
    public double RewindFillPercent => _rewindRing.Length > 0 ? (_rewindCount * 100.0 / _rewindRing.Length) : 0.0;

    // Interactive Shader Studio Parameters (1.7.0)
    private double _scanlineIntensity = 0.32;
    public double ScanlineIntensity
    {
        get => _scanlineIntensity;
        set
        {
            _scanlineIntensity = Math.Clamp(value, 0.0, 0.85);
            if (SettingsManager.Current != null)
                SettingsManager.Current.ShaderScanlineIntensity = _scanlineIntensity;
        }
    }

    private double _maskIntensity = 0.20;
    public double MaskIntensity
    {
        get => _maskIntensity;
        set
        {
            _maskIntensity = Math.Clamp(value, 0.0, 0.75);
            if (SettingsManager.Current != null)
                SettingsManager.Current.ShaderMaskIntensity = _maskIntensity;
        }
    }

    private double _vignetteIntensity = 0.18;
    public double VignetteIntensity
    {
        get => _vignetteIntensity;
        set
        {
            _vignetteIntensity = Math.Clamp(value, 0.0, 0.70);
            if (SettingsManager.Current != null)
                SettingsManager.Current.ShaderVignetteIntensity = _vignetteIntensity;
        }
    }

    private double _brightnessGain = 1.04;
    public double BrightnessGain
    {
        get => _brightnessGain;
        set
        {
            _brightnessGain = Math.Clamp(value, 0.75, 1.40);
            if (SettingsManager.Current != null)
                SettingsManager.Current.ShaderBrightnessGain = _brightnessGain;
        }
    }

    private int _colorTemperatureMode = 0;
    public int ColorTemperatureMode
    {
        get => _colorTemperatureMode;
        set
        {
            _colorTemperatureMode = Math.Clamp(value, 0, 2);
            if (SettingsManager.Current != null)
                SettingsManager.Current.ShaderColorTemperatureMode = _colorTemperatureMode;
        }
    }

    public void ApplyShaderStudioPreset(string presetId)
    {
        switch (presetId.ToLowerInvariant())
        {
            case "trinitron":
                ScanlineIntensity = 0.35;
                MaskIntensity = 0.25;
                VignetteIntensity = 0.18;
                BrightnessGain = 1.08;
                ColorTemperatureMode = 2;
                _shaderFilterMode = 3;
                break;
            case "arcade_rgb":
                ScanlineIntensity = 0.40;
                MaskIntensity = 0.30;
                VignetteIntensity = 0.15;
                BrightnessGain = 1.12;
                ColorTemperatureMode = 0;
                _shaderFilterMode = 1;
                break;
            case "ibm_vga":
                ScanlineIntensity = 0.25;
                MaskIntensity = 0.15;
                VignetteIntensity = 0.10;
                BrightnessGain = 1.02;
                ColorTemperatureMode = 0;
                _shaderFilterMode = 4;
                break;
            case "gb_lcd":
                ScanlineIntensity = 0.15;
                MaskIntensity = 0.35;
                VignetteIntensity = 0.12;
                BrightnessGain = 0.98;
                ColorTemperatureMode = 1;
                _shaderFilterMode = 1;
                break;
            case "vhs":
                ScanlineIntensity = 0.30;
                MaskIntensity = 0.18;
                VignetteIntensity = 0.28;
                BrightnessGain = 1.05;
                ColorTemperatureMode = 1;
                _shaderFilterMode = 1;
                break;
            case "clean_raw":
            default:
                ScanlineIntensity = 0.0;
                MaskIntensity = 0.0;
                VignetteIntensity = 0.0;
                BrightnessGain = 1.0;
                ColorTemperatureMode = 0;
                _shaderFilterMode = 0;
                break;
        }

        SettingsManager.Current.ShaderScanlineIntensity = ScanlineIntensity;
        SettingsManager.Current.ShaderMaskIntensity = MaskIntensity;
        SettingsManager.Current.ShaderVignetteIntensity = VignetteIntensity;
        SettingsManager.Current.ShaderBrightnessGain = BrightnessGain;
        SettingsManager.Current.ShaderColorTemperatureMode = ColorTemperatureMode;
        SettingsManager.Save();
    }

    // Audio Output
    private IntPtr _hWaveOut = IntPtr.Zero;
    private const int AudioChunkSamples = 1024;
    private short[] _audioBuffer = new short[AudioChunkSamples * 2];
    private int _audioBufferPos = 0;
    private const int WaveHdrCount = 8;
    private WAVEHDR[] _waveHdrs = new WAVEHDR[WaveHdrCount];
    private IntPtr[] _waveBuffers = new IntPtr[WaveHdrCount];
    private int _currentWaveHdr = 0;

    // Public properties
    public bool IsRunning => _isRunning;
    public bool IsPaused => _isPaused;
    public bool IsFastForward => _isFastForward;
    public int ActiveStateSlot
    {
        get => _activeStateSlot;
        set => _activeStateSlot = Math.Clamp(value, 0, 99);
    }
    public bool IsAudioMuted
    {
        get => _isAudioMuted;
        set => _isAudioMuted = value;
    }
    public int AspectMode
    {
        get => _aspectMode;
        set => _aspectMode = value;
    }
    public string LastSavedStatePath { get; private set; } = "";
    public int ShaderFilterMode
    {
        get => _shaderFilterMode;
        set => _shaderFilterMode = value;
    }
    public string CurrentGameTitle { get; private set; } = "";
    public string CurrentCoreName { get; private set; } = "";
    public double Fps => _targetFps;

    public double CoreAspectRatio { get; private set; } = 4.0 / 3.0;
    public static event Action? GeometryChanged;
    public static event Action? SessionStarted;
    public static event Action? SessionStopped;
    private DateTime? _sessionStartUtc;

    private NativeLibretroHost()
    {
        // Allocate persistent unmanaged UTF-8 strings for directories
        string sys = Path.Combine(CoreManager.AppDir, "system");
        if (!Directory.Exists(sys)) Directory.CreateDirectory(sys);
        _ptrSystemDir = Marshal.StringToCoTaskMemUTF8(sys);

        string sav = Path.Combine(CoreManager.AppDir, "saves");
        if (!Directory.Exists(sav)) Directory.CreateDirectory(sav);
        _ptrSaveDir = Marshal.StringToCoTaskMemUTF8(sav);

        string dl = Path.Combine(CoreManager.AppDir, "downloads");
        if (!Directory.Exists(dl)) Directory.CreateDirectory(dl);
        _ptrContentDir = Marshal.StringToCoTaskMemUTF8(dl);

        // Keep pinned references
        _envCallback = OnEnvironment;
        _videoCallback = OnVideoRefresh;
        _audioCallback = OnAudioSample;
        _audioBatchCallback = OnAudioSampleBatch;
        _inputPollCallback = OnInputPoll;
        _inputStateCallback = OnInputState;
    }

    public async Task<bool> StartGameAsync(RomItem rom, Image targetImage, Action<string>? onStatus = null)
    {
        if (_isRunning)
        {
            StopGame();
            await Task.Delay(100);
        }

        CurrentGameTitle = rom.Name;
        CurrentCoreName = rom.CoreDisplayName;
        _sessionStartUtc = DateTime.UtcNow;
        GameMetadataStore.RecordLaunch(rom.Name);
        _targetImage = targetImage;

        onStatus?.Invoke($"Инициализация ядра {rom.CoreDisplayName}...");

        string corePath = Path.Combine(CoreManager.CoresDir, rom.CoreDll);
        if (!File.Exists(corePath))
        {
            // Auto download core
            onStatus?.Invoke($"Загрузка ядра {rom.CoreDll}...");
            bool ok = await CoreManager.EnsureCoreDownloadedAsync(rom.CoreDll);
            if (!ok || !File.Exists(corePath))
            {
                throw new FileNotFoundException($"Библиотека ядра {rom.CoreDll} не найдена в {CoreManager.CoresDir}");
            }
        }

        // 1. Load native library
        _hCoreDll = NativeLibrary.Load(corePath);
        if (_hCoreDll == IntPtr.Zero)
        {
            throw new Exception($"Не удалось загрузить библиотеку ядра: {corePath}");
        }

        // 2. Resolve exports
        ResolveExports();

        // 3. Prepare core options and input state before environment callback registration
        _isDosCore = rom.CoreDll.Contains("dosbox", StringComparison.OrdinalIgnoreCase);
        _keyboardCallback = null;
        Array.Clear(_prevRetroKeyDown, 0, _prevRetroKeyDown.Length);
        LoadCoreOptions(rom.CoreDisplayName, rom.CoreDll);

        // 4. Set environment and callbacks
        _retroSetEnvironment!(_envCallback!);
        _retroSetVideoRefresh!(_videoCallback!);
        _retroSetAudioSample!(_audioCallback!);
        _retroSetAudioSampleBatch!(_audioBatchCallback!);
        _retroSetInputPoll!(_inputPollCallback!);
        _retroSetInputState!(_inputStateCallback!);

        // Reset video & rendering state so switching cores never leaks stale pixel format
        _pixelFormat = 0;
        _lastWidth = 0;
        _lastHeight = 0;
        _bitmap = null;
        _isRendering = 0;

        // 5. Init core
        _retroInit!();

        // 5. Handle ROM (extract if .zip, parse if .m3u)
        _multiDiscPaths.Clear();
        _currentDiscIndex = 0;
        _isDiscEjected = false;
        lock (_rewindRing) { _rewindHead = 0; _rewindCount = 0; }

        string effectiveRomPath = rom.FilePath;
        if (rom.FilePath.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) && File.Exists(rom.FilePath))
        {
            string m3uDir = Path.GetDirectoryName(rom.FilePath) ?? "";
            try
            {
                var lines = File.ReadAllLines(rom.FilePath);
                foreach (var rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    string resolved = Path.IsPathRooted(line) ? line : Path.Combine(m3uDir, line);
                    if (File.Exists(resolved))
                    {
                        _multiDiscPaths.Add(resolved);
                    }
                }
            }
            catch { }

            if (_multiDiscPaths.Count > 0)
            {
                effectiveRomPath = _multiDiscPaths[0];
            }
        }

        _retroGetSystemInfo!(out var sysInfo);
        bool isDosOrArchiveCore = sysInfo.BlockExtract ||
                                  rom.CoreDll.Contains("dosbox", StringComparison.OrdinalIgnoreCase) ||
                                  rom.CoreDll.Contains("fbneo", StringComparison.OrdinalIgnoreCase) ||
                                  rom.CoreDll.Contains("mame", StringComparison.OrdinalIgnoreCase) ||
                                  rom.CoreDll.Contains("scummvm", StringComparison.OrdinalIgnoreCase);
        bool needFullpath = sysInfo.NeedFullpath || isDosOrArchiveCore;

        string cacheDir = Path.Combine(CoreManager.AppDir, "cache_roms");
        if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

        if (effectiveRomPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            if (isDosOrArchiveCore)
            {
                // For DOSBox-Pure / MAME / FBNeo: pass the .zip archive directly WITHOUT extracting single files!
                // Only if this .zip is a 1G+1M outer container holding inner .zip archives (and no direct .exe/.com/.bat), extract the preferred inner .zip.
                try
                {
                    using var archive = ZipFile.OpenRead(effectiveRomPath);
                    bool hasDirectDosExec = archive.Entries.Any(e =>
                    {
                        string ext = Path.GetExtension(e.Name).ToLowerInvariant();
                        return ext is ".exe" or ".com" or ".bat" or ".iso" or ".cue" or ".img" or ".ima" or ".vhd" or ".conf";
                    });
                    if (!hasDirectDosExec)
                    {
                        var innerZips = archive.Entries.Where(e => e.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
                        if (innerZips.Count > 0)
                        {
                            var chosenZip = innerZips.FirstOrDefault(e => e.Name.Contains("RUS", StringComparison.OrdinalIgnoreCase) || e.Name.Contains("MOD", StringComparison.OrdinalIgnoreCase))
                                            ?? innerZips[0];
                            string innerDest = Path.Combine(cacheDir, chosenZip.Name);
                            chosenZip.ExtractToFile(innerDest, true);
                            effectiveRomPath = innerDest;
                        }
                    }
                }
                catch { }
            }
            else
            {
                using var archive = ZipFile.OpenRead(effectiveRomPath);
                var validEntries = archive.Entries.Where(e =>
                {
                    if (string.IsNullOrEmpty(e.Name)) return false;
                    string ext = Path.GetExtension(e.Name).ToLowerInvariant();
                    return ext is not (".txt" or ".pdf" or ".nfo" or ".md" or ".html" or ".htm" or ".jpg" or ".png" or ".zip" or ".diz");
                }).ToList();

                ZipArchiveEntry? romEntry = validEntries.FirstOrDefault(e => e.Name.Contains("RUS", StringComparison.OrdinalIgnoreCase) || e.Name.Contains("MOD", StringComparison.OrdinalIgnoreCase))
                                            ?? validEntries.FirstOrDefault();

                if (romEntry != null)
                {
                    effectiveRomPath = Path.Combine(cacheDir, romEntry.Name);
                    romEntry.ExtractToFile(effectiveRomPath, true);
                }
                else
                {
                    // Check if 1G+1M container with inner .zip
                    var innerZips = archive.Entries.Where(e => e.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
                    var chosenInnerZip = innerZips.FirstOrDefault(e => e.Name.Contains("RUS", StringComparison.OrdinalIgnoreCase) || e.Name.Contains("MOD", StringComparison.OrdinalIgnoreCase))
                                         ?? innerZips.FirstOrDefault();
                    if (chosenInnerZip != null)
                    {
                        string tempInnerZip = Path.Combine(cacheDir, chosenInnerZip.Name);
                        chosenInnerZip.ExtractToFile(tempInnerZip, true);
                        using var innerArch = ZipFile.OpenRead(tempInnerZip);
                        var innerRom = innerArch.Entries.FirstOrDefault(e => !string.IsNullOrEmpty(e.Name) && Path.GetExtension(e.Name).ToLowerInvariant() is not (".txt" or ".pdf" or ".nfo"));
                        if (innerRom != null)
                        {
                            effectiveRomPath = Path.Combine(cacheDir, innerRom.Name);
                            innerRom.ExtractToFile(effectiveRomPath, true);
                        }
                    }
                    else
                    {
                        throw new Exception("ROM file not found in .zip archive!");
                    }
                }
            }
        }

        byte[]? romBytes = null;
        GCHandle pinnedRomData = default;
        IntPtr pRomPath = Marshal.StringToCoTaskMemUTF8(effectiveRomPath);

        try
        {
            bool loaded = false;
            if (needFullpath)
            {
                var gameInfoFull = new RetroGameInfo
                {
                    Path = pRomPath,
                    Data = IntPtr.Zero,
                    Size = 0,
                    Meta = IntPtr.Zero
                };
                loaded = _retroLoadGame!(ref gameInfoFull);
            }

            if (!loaded)
            {
                romBytes = File.ReadAllBytes(effectiveRomPath);
                pinnedRomData = GCHandle.Alloc(romBytes, GCHandleType.Pinned);
                var gameInfoMem = new RetroGameInfo
                {
                    Path = pRomPath,
                    Data = pinnedRomData.AddrOfPinnedObject(),
                    Size = (nuint)romBytes.Length,
                    Meta = IntPtr.Zero
                };
                loaded = _retroLoadGame!(ref gameInfoMem);
            }

            if (!loaded)
            {
                throw new Exception($"Core {rom.CoreDisplayName} failed to load {rom.Name}!");
            }
        }
        finally
        {
            if (pinnedRomData.IsAllocated) pinnedRomData.Free();
            if (pRomPath != IntPtr.Zero) Marshal.FreeCoTaskMem(pRomPath);
        }

        // 6. Query AV info
        _retroGetSystemAvInfo!(out var avInfo);
        _targetFps = avInfo.Timing.Fps > 10.0 ? avInfo.Timing.Fps : 60.0;
        _sampleRate = avInfo.Timing.SampleRate > 8000 ? (uint)avInfo.Timing.SampleRate : 44100;
        CoreAspectRatio = avInfo.Geometry.AspectRatio > 0.2f ? avInfo.Geometry.AspectRatio : (avInfo.Geometry.BaseWidth > 0 && avInfo.Geometry.BaseHeight > 0 ? (double)avInfo.Geometry.BaseWidth / avInfo.Geometry.BaseHeight : 4.0 / 3.0);
        GeometryChanged?.Invoke();

        _serializeSize = _retroSerializeSize != null ? _retroSerializeSize() : 0;

        // 7. Init Audio
        InitAudio(_sampleRate);

        // 8. Load Battery / SRAM save if exists
        LoadBatterySave(rom.Name);

        // 9. Start background emulation thread
        _isRunning = true;
        _isPaused = false;
        _isFastForward = false;
        _frameCount = 0;
        lock (_rewindRing) { _rewindHead = 0; _rewindCount = 0; }

        _emulationThread = new Thread(EmulationLoop)
        {
            IsBackground = true,
            Name = "NativeLibretroEmulationThread",
            Priority = ThreadPriority.Normal
        };
        _emulationThread.Start();

        // Auto-resume state if enabled
        if (SettingsManager.Current.AutoSaveLoadOnExit)
        {
            try
            {
                string statesDir = Path.Combine(CoreManager.AppDir, "states");
                string autoResumePath = Path.Combine(statesDir, $"{SanitizeFileName(rom.Name)}.autoresume");
                if (File.Exists(autoResumePath))
                {
                    LoadState(0, autoResumePath);
                }
            }
            catch { }
        }

        // Auto-apply matching cheats if available
        try
        {
            string gameName = Path.GetFileNameWithoutExtension(rom.FilePath);
            var allCheats = CheatManager.ScanCheats();
            var match = allCheats.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) ||
                                                      c.GameName.Equals(rom.Name, StringComparison.OrdinalIgnoreCase));
            if (match != null && File.Exists(match.FilePath))
            {
                var parsedCheats = CheatManager.ParseCht(match.FilePath);
                if (parsedCheats.Any(c => c.IsEnabled))
                {
                    ApplyCheats(parsedCheats);
                }
            }
        }
        catch { }

        SessionStarted?.Invoke();
        onStatus?.Invoke($"Игра {rom.Name} запущена в STORM RETROARCH!");
        return true;
    }

    private void ResolveExports()
    {
        T GetExport<T>(string name) where T : Delegate
        {
            IntPtr p = NativeLibrary.GetExport(_hCoreDll, name);
            return Marshal.GetDelegateForFunctionPointer<T>(p);
        }

        _retroInit = GetExport<RetroInit>("retro_init");
        _retroDeinit = GetExport<RetroDeinit>("retro_deinit");
        _retroGetSystemInfo = GetExport<RetroGetSystemInfo>("retro_get_system_info");
        _retroGetSystemAvInfo = GetExport<RetroGetSystemAvInfo>("retro_get_system_av_info");
        _retroSetEnvironment = GetExport<RetroSetEnvironment>("retro_set_environment");
        _retroSetVideoRefresh = GetExport<RetroSetVideoRefresh>("retro_set_video_refresh");
        _retroSetAudioSample = GetExport<RetroSetAudioSample>("retro_set_audio_sample");
        _retroSetAudioSampleBatch = GetExport<RetroSetAudioSampleBatch>("retro_set_audio_sample_batch");
        _retroSetInputPoll = GetExport<RetroSetInputPoll>("retro_set_input_poll");
        _retroSetInputState = GetExport<RetroSetInputState>("retro_set_input_state");
        _retroSetControllerPortDevice = GetExport<RetroSetControllerPortDevice>("retro_set_controller_port_device");
        _retroReset = GetExport<RetroReset>("retro_reset");
        _retroRun = GetExport<RetroRun>("retro_run");
        _retroSerializeSize = GetExport<RetroSerializeSize>("retro_serialize_size");
        _retroSerialize = GetExport<RetroSerialize>("retro_serialize");
        _retroUnserialize = GetExport<RetroUnserialize>("retro_unserialize");
        _retroLoadGame = GetExport<RetroLoadGame>("retro_load_game");
        _retroUnloadGame = GetExport<RetroUnloadGame>("retro_unload_game");
        _retroGetMemoryData = GetExport<RetroGetMemoryData>("retro_get_memory_data");
        _retroGetMemorySize = GetExport<RetroGetMemorySize>("retro_get_memory_size");

        if (NativeLibrary.TryGetExport(_hCoreDll, "retro_cheat_reset", out var pCheatReset))
            _retroCheatReset = Marshal.GetDelegateForFunctionPointer<RetroCheatReset>(pCheatReset);
        else
            _retroCheatReset = null;

        if (NativeLibrary.TryGetExport(_hCoreDll, "retro_cheat_set", out var pCheatSet))
            _retroCheatSet = Marshal.GetDelegateForFunctionPointer<RetroCheatSet>(pCheatSet);
        else
            _retroCheatSet = null;
    }

    private bool OnEnvironment(uint cmd, IntPtr data)
    {
        switch (cmd)
        {
            case 3: // RETRO_ENVIRONMENT_GET_CAN_DUPE
                if (data != IntPtr.Zero) Marshal.WriteByte(data, 1);
                return true;

            case 8: // RETRO_ENVIRONMENT_SET_PERFORMANCE_LEVEL
            case 11: // RETRO_ENVIRONMENT_SET_INPUT_DESCRIPTORS
            case 16: // RETRO_ENVIRONMENT_SET_VARIABLES
            case 18: // RETRO_ENVIRONMENT_SET_SUPPORT_NO_GAME
            case 37: // RETRO_ENVIRONMENT_SET_GEOMETRY
                if (data != IntPtr.Zero)
                {
                    try
                    {
                        var geom = Marshal.PtrToStructure<RetroGameGeometry>(data);
                        if (geom.AspectRatio > 0.2f)
                        {
                            CoreAspectRatio = geom.AspectRatio;
                            GeometryChanged?.Invoke();
                        }
                        else if (geom.BaseWidth > 0 && geom.BaseHeight > 0)
                        {
                            CoreAspectRatio = (double)geom.BaseWidth / geom.BaseHeight;
                            GeometryChanged?.Invoke();
                        }
                    }
                    catch { }
                }
                return true;

            case 9: // RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY
                if (data != IntPtr.Zero) Marshal.WriteIntPtr(data, _ptrSystemDir);
                return true;

            case 10: // RETRO_ENVIRONMENT_SET_PIXEL_FORMAT
                if (data != IntPtr.Zero)
                {
                    _pixelFormat = Marshal.ReadInt32(data);
                }
                return true;

            case 12: // RETRO_ENVIRONMENT_SET_KEYBOARD_CALLBACK
                if (data != IntPtr.Zero)
                {
                    IntPtr fnPtr = Marshal.ReadIntPtr(data);
                    if (fnPtr != IntPtr.Zero)
                    {
                        try { _keyboardCallback = Marshal.GetDelegateForFunctionPointer<RetroKeyboardEvent>(fnPtr); } catch { }
                    }
                }
                return true;

            case 15: // RETRO_ENVIRONMENT_GET_VARIABLE
                if (data != IntPtr.Zero)
                {
                    IntPtr keyPtr = Marshal.ReadIntPtr(data);
                    if (keyPtr != IntPtr.Zero)
                    {
                        string? key = Marshal.PtrToStringUTF8(keyPtr);
                        if (!string.IsNullOrEmpty(key) && _coreOptions.TryGetValue(key, out var val))
                        {
                            if (!_coreOptionPtrs.TryGetValue(key, out var pVal) || pVal == IntPtr.Zero)
                            {
                                pVal = Marshal.StringToCoTaskMemUTF8(val);
                                _coreOptionPtrs[key] = pVal;
                            }
                            Marshal.WriteIntPtr(data, IntPtr.Size, pVal);
                            return true;
                        }
                    }
                    Marshal.WriteIntPtr(data, IntPtr.Size, IntPtr.Zero);
                }
                return false;

            case 17: // RETRO_ENVIRONMENT_GET_VARIABLE_UPDATE
                if (data != IntPtr.Zero)
                {
                    Marshal.WriteByte(data, (byte)(_optionsUpdated ? 1 : 0));
                    _optionsUpdated = false;
                }
                return true;

            case 27: // RETRO_ENVIRONMENT_GET_LOG_INTERFACE
                return false;

            case 30: // RETRO_ENVIRONMENT_GET_CONTENT_DIRECTORY
                if (data != IntPtr.Zero) Marshal.WriteIntPtr(data, _ptrContentDir);
                return true;

            case 31: // RETRO_ENVIRONMENT_GET_SAVE_DIRECTORY
                if (data != IntPtr.Zero) Marshal.WriteIntPtr(data, _ptrSaveDir);
                return true;

            case 47: // RETRO_ENVIRONMENT_GET_FASTFORWARDING
                if (data != IntPtr.Zero) Marshal.WriteByte(data, (byte)(_isFastForward ? 1 : 0));
                return true;

            default:
                return false;
        }
    }

    private void OnVideoRefresh(IntPtr data, uint width, uint height, nuint pitch)
    {
        if (data == IntPtr.Zero || width == 0 || height == 0) return;

        int w = (int)width;
        int h = (int)height;
        int requiredBytes = w * h * 4;

        if (_bgraBuffer.Length < requiredBytes)
        {
            _bgraBuffer = new byte[requiredBytes];
        }

        // Convert raw pixels to BGRA32 with 6 CRT shader modes and Interactive Shader Studio Parameters
        unsafe
        {
            byte* srcBytes = (byte*)data.ToPointer();
            int srcPitch = (int)pitch;
            bool applyScanlines = (_shaderFilterMode == 1 || _shaderFilterMode == 3 || _scanlineIntensity > 0.05);

            int scanFactor = Math.Clamp(100 - (int)(_scanlineIntensity * 100), 15, 100);
            int maskPercent = Math.Clamp((int)(_maskIntensity * 100), 0, 75);
            int gainVal = Math.Clamp((int)(_brightnessGain * 100), 75, 140);
            int tempMode = _colorTemperatureMode;
            bool hasVignette = _vignetteIntensity > 0.05;
            int vigStr = (int)(_vignetteIntensity * 100);
            int cx = w / 2;
            int cy = h / 2;

            fixed (byte* dstFixed = _bgraBuffer)
            {
                for (int y = 0; y < h; y++)
                {
                    byte* srcRow = srcBytes + (y * srcPitch);
                    byte* dstRow = dstFixed + (y * w * 4);
                    bool isOddScanline = applyScanlines && (y % 2 != 0);

                    uint* dstUint = (uint*)dstRow;
                    int dy = y - cy;
                    int dyTerm = hasVignette ? ((dy * dy * 100) / (cy * cy + 1)) : 0;

                    for (int x = 0; x < w; x++)
                    {
                        uint val;
                        if (_pixelFormat == 1) // XRGB8888
                        {
                            val = ((uint*)srcRow)[x];
                        }
                        else if (_pixelFormat == 2) // RGB565
                        {
                            val = Rgb565Lut[((ushort*)srcRow)[x]];
                        }
                        else // 0RGB1555
                        {
                            val = Rgb1555Lut[((ushort*)srcRow)[x]];
                        }

                        uint b = val & 0xFF;
                        uint g = (val >> 8) & 0xFF;
                        uint r = (val >> 16) & 0xFF;

                        if (_shaderFilterMode == 4) // IBM 5151 Green Phosphor (P1)
                        {
                            uint yLum = (r * 77u + g * 150u + b * 29u) >> 8;
                            if (isOddScanline) yLum = (yLum * (uint)scanFactor) / 100u;
                            r = (yLum * 35) >> 8;
                            g = Math.Min(255u, (yLum * 270) >> 8);
                            b = (yLum * 45) >> 8;
                        }
                        else if (_shaderFilterMode == 5) // Amber Retro Monitor P3
                        {
                            uint yLum = (r * 77u + g * 150u + b * 29u) >> 8;
                            if (isOddScanline) yLum = (yLum * (uint)scanFactor) / 100u;
                            r = Math.Min(255u, (yLum * 255) >> 8);
                            g = Math.Min(255u, (yLum * 175) >> 8);
                            b = (yLum * 20) >> 8;
                        }
                        else
                        {
                            // Scanlines
                            if (isOddScanline)
                            {
                                r = (r * (uint)scanFactor) / 100u;
                                g = (g * (uint)scanFactor) / 100u;
                                b = (b * (uint)scanFactor) / 100u;
                            }

                            // Aperture Grille / Subpixel Mask
                            if (maskPercent > 5)
                            {
                                int sub = x % 3;
                                uint boost = (uint)(100 + maskPercent);
                                uint dim = (uint)(100 - maskPercent / 2);
                                if (sub == 0)
                                {
                                    r = Math.Min(255u, (r * boost) / 100u);
                                    g = (g * dim) / 100u;
                                    b = (b * dim) / 100u;
                                }
                                else if (sub == 1)
                                {
                                    r = (r * dim) / 100u;
                                    g = Math.Min(255u, (g * boost) / 100u);
                                    b = (b * dim) / 100u;
                                }
                                else
                                {
                                    r = (r * dim) / 100u;
                                    g = (g * dim) / 100u;
                                    b = Math.Min(255u, (b * boost) / 100u);
                                }
                            }

                            // Color Temperature Mode
                            if (tempMode == 1) // Warm NTSC 5500K
                            {
                                r = Math.Min(255u, (r * 106u) / 100u);
                                b = (b * 93u) / 100u;
                            }
                            else if (tempMode == 2) // Cool Trinitron 7500K
                            {
                                r = (r * 94u) / 100u;
                                b = Math.Min(255u, (b * 107u) / 100u);
                            }

                            // Brightness Gain
                            if (gainVal != 100)
                            {
                                r = Math.Min(255u, (r * (uint)gainVal) / 100u);
                                g = Math.Min(255u, (g * (uint)gainVal) / 100u);
                                b = Math.Min(255u, (b * (uint)gainVal) / 100u);
                            }

                            // Vignette
                            if (hasVignette)
                            {
                                int dx = x - cx;
                                int dxTerm = (dx * dx * 100) / (cx * cx + 1);
                                int dist = dxTerm + dyTerm;
                                int vMult = Math.Clamp(100 - (dist * vigStr / 200), 45, 100);
                                r = (r * (uint)vMult) / 100u;
                                g = (g * (uint)vMult) / 100u;
                                b = (b * (uint)vMult) / 100u;
                            }
                        }

                        dstUint[x] = 0xFF000000u | (r << 16) | (g << 8) | b;
                    }
                }
            }
        }
        // Non-blocking UI update: only dispatch if UI has completed the previous frame
        if (Interlocked.CompareExchange(ref _isRendering, 1, 0) == 0)
        {
            byte[] frameCopy = ArrayPool<byte>.Shared.Rent(requiredBytes);
            Buffer.BlockCopy(_bgraBuffer, 0, frameCopy, 0, requiredBytes);

            _lastWidth = w; _lastHeight = h;
            Application.Current?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
            {
                try
                {
                    if (_targetImage != null)
                    {
                        if (_bitmap == null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
                        {
                            _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                            _targetImage.Source = _bitmap;
                        }
                        _bitmap.WritePixels(new Int32Rect(0, 0, w, h), frameCopy, w * 4, 0);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(frameCopy);
                    Interlocked.Exchange(ref _isRendering, 0);
                }
            }));
        }
    }

    private void OnAudioSample(short left, short right)
    {
        _audioBuffer[_audioBufferPos++] = left;
        _audioBuffer[_audioBufferPos++] = right;
        if (_audioBufferPos >= _audioBuffer.Length)
        {
            FlushAudioChunk();
        }
    }

    private nuint OnAudioSampleBatch(IntPtr data, nuint frames)
    {
        if (data == IntPtr.Zero || frames == 0) return 0;
        int samples = (int)frames * 2;
        unsafe
        {
            short* p = (short*)data.ToPointer();
            for (int i = 0; i < samples; i++)
            {
                _audioBuffer[_audioBufferPos++] = p[i];
                if (_audioBufferPos >= _audioBuffer.Length)
                {
                    FlushAudioChunk();
                }
            }
        }
        return frames;
    }

    private void FlushAudioChunk()
    {
        if (_isAudioMuted)
        {
            _audioBufferPos = 0;
            return;
        }
        if (_hWaveOut == IntPtr.Zero || _audioBufferPos == 0)
        {
            _audioBufferPos = 0;
            return;
        }

        try
        {
            int hdrIdx = _currentWaveHdr;
            _currentWaveHdr = (_currentWaveHdr + 1) % WaveHdrCount;

            ref var hdr = ref _waveHdrs[hdrIdx];
            if ((hdr.dwFlags & 0x00000001) != 0) // WHDR_PREPARED
            {
                waveOutUnprepareHeader(_hWaveOut, ref hdr, (uint)Marshal.SizeOf<WAVEHDR>());
            }

            int bytesToWrite = _audioBufferPos * sizeof(short);
            Marshal.Copy(_audioBuffer, 0, _waveBuffers[hdrIdx], _audioBufferPos);

            hdr.lpData = _waveBuffers[hdrIdx];
            hdr.dwBufferLength = (uint)bytesToWrite;
            hdr.dwBytesRecorded = 0;
            hdr.dwFlags = 0;

            waveOutPrepareHeader(_hWaveOut, ref hdr, (uint)Marshal.SizeOf<WAVEHDR>());
            waveOutWrite(_hWaveOut, ref hdr, (uint)Marshal.SizeOf<WAVEHDR>());
        }
        catch { }

        _audioBufferPos = 0;
    }

    private void InitAudio(uint sampleRate)
    {
        try
        {
            CloseAudio();

            var wfx = new WAVEFORMATEX
            {
                wFormatTag = 1, // WAVE_FORMAT_PCM
                nChannels = 2,
                nSamplesPerSec = sampleRate,
                wBitsPerSample = 16,
                nBlockAlign = 4,
                nAvgBytesPerSec = sampleRate * 4,
                cbSize = 0
            };

            int res = waveOutOpen(out _hWaveOut, 0xFFFFFFFF, ref wfx, IntPtr.Zero, IntPtr.Zero, 0);
            if (res == 0)
            {
                for (int i = 0; i < WaveHdrCount; i++)
                {
                    _waveBuffers[i] = Marshal.AllocHGlobal(AudioChunkSamples * 2 * sizeof(short));
                    _waveHdrs[i] = new WAVEHDR();
                }
            }
        }
        catch { }
    }

    private void CloseAudio()
    {
        if (_hWaveOut != IntPtr.Zero)
        {
            try
            {
                waveOutReset(_hWaveOut);
                for (int i = 0; i < WaveHdrCount; i++)
                {
                    if ((_waveHdrs[i].dwFlags & 0x00000001) != 0)
                    {
                        waveOutUnprepareHeader(_hWaveOut, ref _waveHdrs[i], (uint)Marshal.SizeOf<WAVEHDR>());
                    }
                    if (_waveBuffers[i] != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(_waveBuffers[i]);
                        _waveBuffers[i] = IntPtr.Zero;
                    }
                }
                waveOutClose(_hWaveOut);
            }
            catch { }
            _hWaveOut = IntPtr.Zero;
        }
    }

    private static int RetroKeyToVirtualKey(uint rk)
    {
        if (rk >= 97 && rk <= 122) return (int)(rk - 97 + 0x41); // a..z -> A..Z
        if (rk >= 48 && rk <= 57) return (int)rk;                 // 0..9
        if (rk >= 256 && rk <= 265) return (int)(rk - 256 + 0x60); // numpad 0..9
        if (rk >= 282 && rk <= 293) return (int)(rk - 282 + 0x70); // F1..F12
        return rk switch
        {
            8 => 0x08,   // Backspace
            9 => 0x09,   // Tab
            13 => 0x0D,  // Enter
            27 => 0x1B,  // Escape
            32 => 0x20,  // Space
            127 => 0x2E, // Delete
            273 => 0x26, // Up
            274 => 0x28, // Down
            275 => 0x27, // Right
            276 => 0x25, // Left
            277 => 0x2D, // Insert
            278 => 0x24, // Home
            279 => 0x23, // End
            280 => 0x21, // Page Up
            281 => 0x22, // Page Down
            303 => 0xA1, // RShift
            304 => 0xA0, // LShift
            305 => 0xA3, // RCtrl
            306 => 0xA2, // LCtrl
            307 => 0xA5, // RAlt
            308 => 0xA4, // LAlt
            _ => 0
        };
    }

        private static int KeyNameToVirtualKey(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName)) return 0;
        string k = keyName.Trim().ToLowerInvariant();
        return k switch
        {
            "a" => 0x41, "b" => 0x42, "c" => 0x43, "d" => 0x44, "e" => 0x45, "f" => 0x46,
            "g" => 0x47, "h" => 0x48, "i" => 0x49, "j" => 0x4A, "k" => 0x4B, "l" => 0x4C,
            "m" => 0x4D, "n" => 0x4E, "o" => 0x4F, "p" => 0x50, "q" => 0x51, "r" => 0x52,
            "s" => 0x53, "t" => 0x54, "u" => 0x55, "v" => 0x56, "w" => 0x57, "x" => 0x58,
            "y" => 0x59, "z" => 0x5A,
            "0" => 0x30, "1" or "num1" => 0x31, "2" or "num2" => 0x32, "3" or "num3" => 0x33,
            "4" or "num4" => 0x34, "5" or "num5" => 0x35, "6" or "num6" => 0x36, "7" or "num7" => 0x37,
            "8" or "num8" => 0x38, "9" or "num9" => 0x39,
            "up" => 0x26, "down" => 0x28, "left" => 0x25, "right" => 0x27,
            "enter" or "return" => 0x0D, "space" => 0x20,
            "shift" or "lshift" => 0xA0, "rshift" => 0xA1,
            "ctrl" or "lctrl" => 0xA2, "rctrl" => 0xA3,
            "alt" or "lalt" => 0x12, "back" or "backspace" => 0x08,
            "tab" => 0x09, "escape" => 0x1B,
            _ => 0
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private volatile bool _isDosCore;
    private POINT _lastMousePt;
    private int _mouseDeltaX;
    private int _mouseDeltaY;
    private readonly bool[] _prevRetroKeyDown = new bool[320];
    private static readonly uint[] MonitoredRetroKeys = new uint[]
    {
        8, 9, 13, 27, 32, 44, 45, 46, 47,
        48, 49, 50, 51, 52, 53, 54, 55, 56, 57,
        59, 61, 91, 92, 93,
        97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109,
        110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 127,
        256, 257, 258, 259, 260, 261, 262, 263, 264, 265,
        273, 274, 275, 276, 277, 278, 279, 280, 281,
        282, 283, 284, 285, 286, 287, 288, 289, 290, 291, 292, 293,
        303, 304, 305, 306, 307, 308
    };

    private void OnInputPoll()
    {
        EnsureXInputInitialized();
        if (_xinputGetState != null)
        {
            for (uint i = 0; i < 4; i++)
            {
                _xinputConnected[i] = (_xinputGetState(i, out _xinputStates[i]) == 0);
            }
        }

        if (GetCursorPos(out var pt))
        {
            _mouseDeltaX = Math.Clamp(pt.X - _lastMousePt.X, -120, 120);
            _mouseDeltaY = Math.Clamp(pt.Y - _lastMousePt.Y, -120, 120);
            _lastMousePt = pt;
        }

        if (_keyboardCallback != null)
        {
            IntPtr fg = GetForegroundWindow();
            GetWindowThreadProcessId(fg, out uint pid);
            bool isFg = (pid == (uint)Environment.ProcessId);

            ushort mods = 0;
            if (isFg)
            {
                if ((GetAsyncKeyState(0xA0) & 0x8000) != 0 || (GetAsyncKeyState(0xA1) & 0x8000) != 0) mods |= 1; // RETROKMOD_SHIFT
                if ((GetAsyncKeyState(0xA2) & 0x8000) != 0 || (GetAsyncKeyState(0xA3) & 0x8000) != 0) mods |= 2; // RETROKMOD_CTRL
                if ((GetAsyncKeyState(0xA4) & 0x8000) != 0 || (GetAsyncKeyState(0xA5) & 0x8000) != 0) mods |= 4; // RETROKMOD_ALT
            }

            foreach (uint rk in MonitoredRetroKeys)
            {
                int vk = RetroKeyToVirtualKey(rk);
                bool down = isFg && vk > 0 && (GetAsyncKeyState(vk) & 0x8000) != 0;
                if (rk < (uint)_prevRetroKeyDown.Length && down != _prevRetroKeyDown[rk])
                {
                    _prevRetroKeyDown[rk] = down;
                    uint ch = (rk >= 32 && rk <= 126) ? rk : 0;
                    if (ch >= 97 && ch <= 122 && (mods & 1) != 0) ch -= 32;
                    try { _keyboardCallback(down, rk, ch, mods); } catch { }
                }
            }
        }
    }

    private short OnInputState(uint port, uint device, uint index, uint id)
    {
        // Handle PC Keyboard (RETRO_DEVICE_KEYBOARD = 3)
        if (device == 3)
        {
            IntPtr fg = GetForegroundWindow();
            GetWindowThreadProcessId(fg, out uint pid);
            if (pid != (uint)Environment.ProcessId) return 0;

            int vk = RetroKeyToVirtualKey(id);
            if (vk > 0 && (GetAsyncKeyState(vk) & 0x8000) != 0)
            {
                return 1;
            }
            return 0;
        }

        // Handle PC Mouse (RETRO_DEVICE_MOUSE = 2)
        if (device == 2)
        {
            IntPtr fg = GetForegroundWindow();
            GetWindowThreadProcessId(fg, out uint pid);
            if (pid != (uint)Environment.ProcessId) return 0;

            return id switch
            {
                0 => (short)_mouseDeltaX,                                     // Mouse X delta
                1 => (short)_mouseDeltaY,                                     // Mouse Y delta
                2 => (short)((GetAsyncKeyState(0x01) & 0x8000) != 0 ? 1 : 0), // Left button
                3 => (short)((GetAsyncKeyState(0x02) & 0x8000) != 0 ? 1 : 0), // Right button
                6 => (short)((GetAsyncKeyState(0x04) & 0x8000) != 0 ? 1 : 0), // Middle button
                _ => 0
            };
        }

        if (device != 1) return 0; // RETRO_DEVICE_JOYPAD
        if (port >= 4) return 0;

        // 1. Hardware XInput Gamepad for ports 0..3
        if (_xinputConnected[port])
        {
            var gp = _xinputStates[port].Gamepad;
            const ushort XINPUT_GAMEPAD_DPAD_UP = 0x0001;
            const ushort XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
            const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
            const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
            const ushort XINPUT_GAMEPAD_START = 0x0010;
            const ushort XINPUT_GAMEPAD_BACK = 0x0020;
            const ushort XINPUT_GAMEPAD_LEFT_THUMB = 0x0040;
            const ushort XINPUT_GAMEPAD_RIGHT_THUMB = 0x0080;
            const ushort XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100;
            const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
            const ushort XINPUT_GAMEPAD_A = 0x1000;
            const ushort XINPUT_GAMEPAD_B = 0x2000;
            const ushort XINPUT_GAMEPAD_X = 0x4000;
            const ushort XINPUT_GAMEPAD_Y = 0x8000;
            const short Deadzone = 7849;

            bool pressed = id switch
            {
                0 => (gp.wButtons & (XINPUT_GAMEPAD_A | XINPUT_GAMEPAD_B)) != 0, // B button
                1 => (gp.wButtons & (XINPUT_GAMEPAD_X | XINPUT_GAMEPAD_Y)) != 0, // Y button
                2 => (gp.wButtons & XINPUT_GAMEPAD_BACK) != 0,                   // Select
                3 => (gp.wButtons & XINPUT_GAMEPAD_START) != 0,                  // Start
                4 => (gp.wButtons & XINPUT_GAMEPAD_DPAD_UP) != 0 || gp.sThumbLY > Deadzone,
                5 => (gp.wButtons & XINPUT_GAMEPAD_DPAD_DOWN) != 0 || gp.sThumbLY < -Deadzone,
                6 => (gp.wButtons & XINPUT_GAMEPAD_DPAD_LEFT) != 0 || gp.sThumbLX < -Deadzone,
                7 => (gp.wButtons & XINPUT_GAMEPAD_DPAD_RIGHT) != 0 || gp.sThumbLX > Deadzone,
                8 => (gp.wButtons & (XINPUT_GAMEPAD_A | XINPUT_GAMEPAD_B)) != 0, // A button
                9 => (gp.wButtons & (XINPUT_GAMEPAD_X | XINPUT_GAMEPAD_Y)) != 0, // X button
                10 => (gp.wButtons & XINPUT_GAMEPAD_LEFT_SHOULDER) != 0 || gp.bLeftTrigger > 30, // L
                11 => (gp.wButtons & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0 || gp.bRightTrigger > 30, // R
                12 => gp.bLeftTrigger > 30,                                      // L2
                13 => gp.bRightTrigger > 30,                                     // R2
                14 => (gp.wButtons & XINPUT_GAMEPAD_LEFT_THUMB) != 0,            // L3
                15 => (gp.wButtons & XINPUT_GAMEPAD_RIGHT_THUMB) != 0,           // R3
                _ => false
            };

            if (pressed) return 1;
        }

        // 2. Keyboard for Port 0 (if foreground window)
        if (port == 0)
        {
            IntPtr fg = GetForegroundWindow();
            GetWindowThreadProcessId(fg, out uint pid);
            if (pid != (uint)Environment.ProcessId) return 0;

            bool isDown(int vk) => vk > 0 && (GetAsyncKeyState(vk) & 0x8000) != 0;

            string configKey = id switch
            {
                0 => "input_player1_b",
                1 => "input_player1_y",
                2 => "input_player1_select",
                3 => "input_player1_start",
                4 => "input_player1_up",
                5 => "input_player1_down",
                6 => "input_player1_left",
                7 => "input_player1_right",
                8 => "input_player1_a",
                9 => "input_player1_x",
                10 => "input_player1_l",
                11 => "input_player1_r",
                12 => "input_player1_l2",
                13 => "input_player1_r2",
                14 => "input_player1_l3",
                15 => "input_player1_r3",
                _ => ""
            };

            if (!string.IsNullOrEmpty(configKey))
            {
                string configuredKey = RetroArchConfigManager.GetString(configKey, "");
                int customVk = KeyNameToVirtualKey(configuredKey);
                if (customVk > 0 && isDown(customVk)) return 1;
            }

            // For DOSBox-Pure, PC Keyboard is handled natively via RETRO_DEVICE_KEYBOARD / _keyboardCallback,
            // while Arrow Keys + Enter/Z/X navigate the DOSBox-Pure Start Menu seamlessly.
            if (_isDosCore)
            {
                return id switch
                {
                    0 => (short)(isDown(0x0D) || isDown(0x5A) ? 1 : 0), // B / Run in DOSBox-Pure Menu (Enter or Z)
                    2 => (short)(isDown(0x08) ? 1 : 0),                 // Select (Backspace)
                    3 => (short)(isDown(0x0D) ? 1 : 0),                 // Start (Enter)
                    4 => (short)(isDown(0x26) ? 1 : 0),                 // Up Arrow
                    5 => (short)(isDown(0x28) ? 1 : 0),                 // Down Arrow
                    6 => (short)(isDown(0x25) ? 1 : 0),                 // Left Arrow
                    7 => (short)(isDown(0x27) ? 1 : 0),                 // Right Arrow
                    8 => (short)(isDown(0x58) ? 1 : 0),                 // A (X)
                    _ => 0
                };
            }

            // Default keyboard mappings fallback for console cores
            return id switch
            {
                0 => (short)(isDown(0x5A) || isDown(0x4A) || isDown(0x20) || isDown(0x61) ? 1 : 0), // B (Z, J, Space, Num1)
                1 => (short)(isDown(0x41) || isDown(0x55) || isDown(0x63) ? 1 : 0),                 // Y (A, U, Num3)
                2 => (short)(isDown(0xA1) || isDown(0xA0) || isDown(0x08) ? 1 : 0),                 // Select (RShift, LShift, Back)
                3 => (short)(isDown(0x0D) ? 1 : 0),                                                 // Start (Enter)
                4 => (short)(isDown(0x26) || isDown(0x57) ? 1 : 0),                                 // Up (Up Arrow, W)
                5 => (short)(isDown(0x28) || isDown(0x53) ? 1 : 0),                                 // Down (Down Arrow, S)
                6 => (short)(isDown(0x25) ? 1 : 0),                                                 // Left (Left Arrow)
                7 => (short)(isDown(0x27) || isDown(0x44) ? 1 : 0),                                 // Right (Right Arrow, D)
                8 => (short)(isDown(0x58) || isDown(0x4B) || isDown(0x62) ? 1 : 0),                 // A (X, K, Num2)
                9 => (short)(isDown(0x49) || isDown(0x64) ? 1 : 0),                                 // X (I, Num4)
                10 => (short)(isDown(0x51) ? 1 : 0),                                                // L (Q)
                11 => (short)(isDown(0x45) ? 1 : 0),                                                // R (E)
                _ => 0
            };
        }

        return 0;
    }

    private void EmulationLoop()
    {
        var sw = Stopwatch.StartNew();
        double targetFrameMs = 1000.0 / (_targetFps > 0 ? _targetFps : 60.0);
        double nextTickMs = sw.Elapsed.TotalMilliseconds;

        while (_isRunning)
        {
            if (_isPaused)
            {
                Thread.Sleep(15);
                nextTickMs = sw.Elapsed.TotalMilliseconds;
                continue;
            }

            int framesToRun = _isFastForward ? 3 : 1;
            lock (_coreLock)
            {
                for (int i = 0; i < framesToRun && _isRunning; i++)
                {
                    _retroRun!();
                }

                if (_serializeSize == 0 && _retroSerializeSize != null)
                {
                    try { _serializeSize = _retroSerializeSize(); } catch { }
                }

                _frameCount++;
                if (_frameCount % 2 == 0 && _serializeSize > 0)
                {
                    CaptureRewindFrameLocked();
                }
            }

            nextTickMs += targetFrameMs;
            double wait = nextTickMs - sw.Elapsed.TotalMilliseconds;
            if (wait > 1.0)
            {
                Thread.Sleep((int)wait);
            }
            else if (wait < -50.0)
            {
                nextTickMs = sw.Elapsed.TotalMilliseconds;
            }
        }
    }

    private void CaptureRewindFrameLocked()
    {
        if (_retroSerialize == null || _serializeSize == 0) return;
        try
        {
            int sz = (int)_serializeSize;
            lock (_rewindRing)
            {
                byte[]? slot = _rewindRing[_rewindHead];
                if (slot == null || slot.Length != sz)
                {
                    slot = new byte[sz];
                    _rewindRing[_rewindHead] = slot;
                }

                GCHandle pin = GCHandle.Alloc(slot, GCHandleType.Pinned);
                try
                {
                    if (_retroSerialize(pin.AddrOfPinnedObject(), _serializeSize))
                    {
                        _rewindHead = (_rewindHead + 1) % _rewindRing.Length;
                        if (_rewindCount < _rewindRing.Length)
                            _rewindCount++;
                    }
                }
                finally
                {
                    pin.Free();
                }
            }
        }
        catch { }
    }

    public void TogglePause()
    {
        _isPaused = !_isPaused;
    }

    public void ToggleFastForward()
    {
        _isFastForward = !_isFastForward;
    }

    public void ToggleMute()
    {
        _isAudioMuted = !_isAudioMuted;
        if (_isAudioMuted && _hWaveOut != IntPtr.Zero)
        {
            try { waveOutReset(_hWaveOut); } catch { }
        }
    }

    public void ResetGame()
    {
        if (!_isRunning || _retroReset == null) return;
        lock (_coreLock)
        {
            try { _retroReset(); } catch { }
        }
    }

    public void StepSingleFrame()
    {
        if (!_isRunning || !_isPaused || _retroRun == null) return;
        lock (_coreLock)
        {
            _retroRun();
            _frameCount++;
        }
    }

    public string GetShaderModeTitle() => _shaderFilterMode switch
    {
        0 => "Чёткие пиксели",
        1 => "ЭЛТ-сканлайны",
        2 => "Мягкое сглаживание",
        3 => "Апертурная решётка Тринитрон",
        4 => "Зелёный монитор IBM 5151",
        5 => "Янтарный ретро-монитор",
        _ => "Чёткие пиксели"
    };

    public void CycleShaderFilter()
    {
        _shaderFilterMode = (_shaderFilterMode + 1) % 6;
        if (_targetImage != null)
        {
            RenderOptions.SetBitmapScalingMode(_targetImage,
                (_shaderFilterMode == 2) ? BitmapScalingMode.Linear : BitmapScalingMode.NearestNeighbor);
        }
    }

    public void CycleAspectMode(FrameworkElement? viewportContainer = null)
    {
        _aspectMode = (_aspectMode + 1) % 4;
        ApplyAspectMode(viewportContainer);
    }

    public void ApplyAspectMode(FrameworkElement? viewportContainer = null)
    {
        if (_targetImage == null) return;
        switch (_aspectMode)
        {
            case 0: // 4:3 Standard
                _targetImage.Stretch = Stretch.Uniform;
                _targetImage.Width = double.NaN;
                _targetImage.Height = double.NaN;
                break;
            case 1: // 16:9 Widescreen
                _targetImage.Stretch = Stretch.Fill;
                if (viewportContainer != null && viewportContainer.ActualHeight > 100)
                {
                    double availH = Math.Max(50, viewportContainer.ActualHeight - _targetImage.Margin.Top - _targetImage.Margin.Bottom);
                    double availW = Math.Max(50, viewportContainer.ActualWidth - _targetImage.Margin.Left - _targetImage.Margin.Right);
                    double w = Math.Min(availW, availH * 16.0 / 9.0);
                    _targetImage.Width = w;
                    _targetImage.Height = availH;
                }
                else
                {
                    _targetImage.Width = double.NaN;
                    _targetImage.Height = double.NaN;
                }
                break;
            case 2: // 1:1 Integer / Original Pixel Scale (2x/3x)
                _targetImage.Stretch = Stretch.Uniform;
                _targetImage.Width = _lastWidth * 2;
                _targetImage.Height = _lastHeight * 2;
                break;
            case 3: // Full Stretch
                _targetImage.Stretch = Stretch.Fill;
                _targetImage.Width = double.NaN;
                _targetImage.Height = double.NaN;
                break;
        }
    }

    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Game";
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name.Trim();
    }

    public bool SaveState(string? slotOrPath = null) => SaveState(_activeStateSlot, slotOrPath);

    public bool SaveStateToFile(string path) => SaveState(_activeStateSlot, path);

    public BitmapSource? GetCurrentFrameSnapshot(int maxWidth = 320)
    {
        try
        {
            int w = _lastWidth;
            int h = _lastHeight;
            if (w <= 0 || h <= 0) return null;
            int req = w * h * 4;
            if (_bgraBuffer.Length < req) return null;

            byte[] copy = new byte[req];
            Buffer.BlockCopy(_bgraBuffer, 0, copy, 0, req);
            var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, copy, w * 4);

            if (maxWidth > 0 && w > maxWidth)
            {
                double scale = (double)maxWidth / w;
                var transformed = new TransformedBitmap(src, new ScaleTransform(scale, scale));
                transformed.Freeze();
                return transformed;
            }

            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
    }

    public void SaveFrameSnapshotPng(string pngPath, int maxWidth = 320)
    {
        try
        {
            string? dir = Path.GetDirectoryName(pngPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            BitmapSource? snap = GetCurrentFrameSnapshot(maxWidth);
            if (snap != null)
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(snap));
                using var fs = File.Create(pngPath);
                enc.Save(fs);
            }
            else
            {
                CaptureScreenshot(pngPath);
            }
        }
        catch { }
    }

    public bool SaveState(int slot, string? customPath = null)
    {
        if (!_isRunning || _retroSerialize == null) return false;
        try
        {
            string safeTitle = SanitizeFileName(CurrentGameTitle);
            string statesDir = Path.Combine(CoreManager.AppDir, "states");
            if (!Directory.Exists(statesDir)) Directory.CreateDirectory(statesDir);

            string statePath = !string.IsNullOrWhiteSpace(customPath)
                ? customPath
                : Path.Combine(statesDir, $"{safeTitle}.state{slot}");

            string? d = Path.GetDirectoryName(statePath);
            if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);

            lock (_coreLock)
            {
                nuint sz = _retroSerializeSize != null ? _retroSerializeSize() : _serializeSize;
                if (sz == 0) sz = _serializeSize;
                if (sz == 0) return false;
                _serializeSize = sz;

                byte[] state = new byte[(int)sz];
                GCHandle pin = GCHandle.Alloc(state, GCHandleType.Pinned);
                try
                {
                    if (_retroSerialize(pin.AddrOfPinnedObject(), sz))
                    {
                        File.WriteAllBytes(statePath, state);
                        LastSavedStatePath = statePath;

                        // Also save battery SRAM (.srm)
                        SaveBatterySave(safeTitle);

                        // Also save PNG state preview thumbnail
                        SaveFrameSnapshotPng(statePath + ".png", 320);
                        return true;
                    }
                }
                finally
                {
                    pin.Free();
                }
            }
        }
        catch { }
        return false;
    }

    public bool LoadState(string? slotOrPath = null) => LoadState(_activeStateSlot, slotOrPath);

    public bool LoadState(int slot, string? customPath = null)
    {
        if (!_isRunning || _retroUnserialize == null) return false;
        try
        {
            string safeTitle = SanitizeFileName(CurrentGameTitle);
            string statesDir = Path.Combine(CoreManager.AppDir, "states");
            string statePath = !string.IsNullOrWhiteSpace(customPath)
                ? customPath
                : Path.Combine(statesDir, $"{safeTitle}.state{slot}");

            if (!File.Exists(statePath) && string.IsNullOrWhiteSpace(customPath) && Directory.Exists(statesDir))
            {
                // Fallback: look for any state of the current game if this exact slot was not found
                var match = Directory.GetFiles(statesDir, $"{safeTitle}.state*")
                    .FirstOrDefault(f => !f.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(match)) statePath = match;
            }

            if (!File.Exists(statePath)) return false;

            byte[] state = File.ReadAllBytes(statePath);
            lock (_coreLock)
            {
                GCHandle pin = GCHandle.Alloc(state, GCHandleType.Pinned);
                try
                {
                    return _retroUnserialize(pin.AddrOfPinnedObject(), (nuint)state.Length);
                }
                finally
                {
                    pin.Free();
                }
            }
        }
        catch { }
        return false;
    }

    public void StepRewind()
    {
        if (!_isRunning || _retroUnserialize == null) return;
        byte[]? prev = null;
        lock (_rewindRing)
        {
            if (_rewindCount > 0)
            {
                _rewindHead = (_rewindHead - 1 + _rewindRing.Length) % _rewindRing.Length;
                _rewindCount--;
                prev = _rewindRing[_rewindHead];
            }
        }

        if (prev != null)
        {
            lock (_coreLock)
            {
                GCHandle pin = GCHandle.Alloc(prev, GCHandleType.Pinned);
                try
                {
                    _retroUnserialize(pin.AddrOfPinnedObject(), (nuint)prev.Length);
                }
                finally
                {
                    pin.Free();
                }
            }
        }
    }

    public bool CaptureScreenshot(string path)
    {
        try
        {
            string? d = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);

            int w = _lastWidth;
            int h = _lastHeight;
            int req = w * h * 4;
            if (w > 0 && h > 0 && _bgraBuffer.Length >= req)
            {
                byte[] copy = new byte[req];
                Buffer.BlockCopy(_bgraBuffer, 0, copy, 0, req);
                var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, copy, w * 4);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var fs = File.Create(path);
                enc.Save(fs);
                return true;
            }
            else if (_bitmap != null)
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(_bitmap));
                using var fs = File.Create(path);
                enc.Save(fs);
                return true;
            }
        }
        catch { }
        return false;
    }

    private void LoadBatterySave(string gameName)
    {
        if (_retroGetMemoryData == null || _retroGetMemorySize == null) return;
        try
        {
            string srmPath = Path.Combine(CoreManager.AppDir, "saves", $"{gameName}.srm");
            if (!File.Exists(srmPath)) return;

            IntPtr sram = _retroGetMemoryData(0); // RETRO_MEMORY_SAVE_RAM = 0
            nuint size = _retroGetMemorySize(0);
            if (sram != IntPtr.Zero && size > 0)
            {
                byte[] bytes = File.ReadAllBytes(srmPath);
                int copyLen = Math.Min(bytes.Length, (int)size);
                Marshal.Copy(bytes, 0, sram, copyLen);
            }
        }
        catch { }
    }

    private void SaveBatterySave(string gameName)
    {
        if (_retroGetMemoryData == null || _retroGetMemorySize == null) return;
        try
        {
            IntPtr sram = _retroGetMemoryData(0);
            nuint size = _retroGetMemorySize(0);
            if (sram != IntPtr.Zero && size > 0)
            {
                byte[] bytes = new byte[(int)size];
                Marshal.Copy(sram, bytes, 0, (int)size);
                string srmDir = Path.Combine(CoreManager.AppDir, "saves");
                if (!Directory.Exists(srmDir)) Directory.CreateDirectory(srmDir);
                File.WriteAllBytes(Path.Combine(srmDir, $"{gameName}.srm"), bytes);
            }
        }
        catch { }
    }

    public void StopGame()
    {
        if (!_isRunning) return;

        _isRunning = false;
        try
        {
            _emulationThread?.Join(300);
        }
        catch { }
        _emulationThread = null;

        CloseAudio();
        ClearCoreOptionPointers();
        _coreOptions.Clear();
        _multiDiscPaths.Clear();
        _currentDiscIndex = 0;
        _isDiscEjected = false;
        lock (_rewindRing) { _rewindHead = 0; _rewindCount = 0; }

        if (!string.IsNullOrEmpty(CurrentGameTitle))
        {
            if (SettingsManager.Current.AutoSaveLoadOnExit)
            {
                try
                {
                    string statesDir = Path.Combine(CoreManager.AppDir, "states");
                    if (!Directory.Exists(statesDir)) Directory.CreateDirectory(statesDir);
                    string autoResumePath = Path.Combine(statesDir, $"{SanitizeFileName(CurrentGameTitle)}.autoresume");
                    SaveState(0, autoResumePath);
                }
                catch { }
            }

            if (_sessionStartUtc.HasValue)
            {
                long elapsedSec = (long)(DateTime.UtcNow - _sessionStartUtc.Value).TotalSeconds;
                if (elapsedSec > 0)
                {
                    GameMetadataStore.RecordSession(CurrentGameTitle, elapsedSec);
                }
            }
            _sessionStartUtc = null;

            SaveBatterySave(CurrentGameTitle);
        }

        try
        {
            _retroUnloadGame?.Invoke();
            _retroDeinit?.Invoke();
        }
        catch { }

        if (_hCoreDll != IntPtr.Zero)
        {
            try
            {
                NativeLibrary.Free(_hCoreDll);
            }
            catch { }
            _hCoreDll = IntPtr.Zero;
        }

        SessionStopped?.Invoke();
    }
}
