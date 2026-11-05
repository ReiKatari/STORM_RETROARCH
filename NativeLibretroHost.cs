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

    // Rewind ring buffer
    private readonly List<byte[]> _rewindHistory = new();
    private const int MaxRewindFrames = 60;
    private ulong _frameCount = 0;

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

    public static event Action? SessionStarted;
    public static event Action? SessionStopped;

    private NativeLibretroHost()
    {
        // Allocate persistent unmanaged UTF-8 strings for directories
        string sys = Path.Combine(CoreManager.AppDir, "system");
        if (!Directory.Exists(sys)) Directory.CreateDirectory(sys);
        _ptrSystemDir = Marshal.StringToHGlobalAnsi(sys);

        string sav = Path.Combine(CoreManager.AppDir, "saves");
        if (!Directory.Exists(sav)) Directory.CreateDirectory(sav);
        _ptrSaveDir = Marshal.StringToHGlobalAnsi(sav);

        string dl = Path.Combine(CoreManager.AppDir, "downloads");
        if (!Directory.Exists(dl)) Directory.CreateDirectory(dl);
        _ptrContentDir = Marshal.StringToHGlobalAnsi(dl);

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

        // 3. Set environment and callbacks
        _retroSetEnvironment!(_envCallback!);
        _retroSetVideoRefresh!(_videoCallback!);
        _retroSetAudioSample!(_audioCallback!);
        _retroSetAudioSampleBatch!(_audioBatchCallback!);
        _retroSetInputPoll!(_inputPollCallback!);
        _retroSetInputState!(_inputStateCallback!);

        // 4. Init core
        _retroInit!();

        // 5. Handle ROM (extract if .zip)
        string effectiveRomPath = rom.FilePath;
        byte[] romBytes;
        GCHandle pinnedRomData = default;

        if (rom.FilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            onStatus?.Invoke("Распаковка ROM из архива...");
            string cacheDir = Path.Combine(CoreManager.AppDir, "cache_roms");
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

            using var archive = ZipFile.OpenRead(rom.FilePath);
            ZipArchiveEntry? romEntry = null;
            foreach (var entry in archive.Entries)
            {
                string ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (ext is ".nes" or ".sfc" or ".smc" or ".md" or ".gen" or ".gba" or ".gb" or ".gbc" or ".pce" or ".cue" or ".bin" or ".iso")
                {
                    romEntry = entry;
                    break;
                }
            }
            romEntry ??= (archive.Entries.Count > 0 ? archive.Entries[0] : null);

            if (romEntry == null)
            {
                throw new Exception("В .zip архиве не найден файл игры!");
            }

            effectiveRomPath = Path.Combine(cacheDir, romEntry.Name);
            romEntry.ExtractToFile(effectiveRomPath, true);
            romBytes = File.ReadAllBytes(effectiveRomPath);
        }
        else
        {
            romBytes = File.ReadAllBytes(effectiveRomPath);
        }

        pinnedRomData = GCHandle.Alloc(romBytes, GCHandleType.Pinned);
        IntPtr pRomPath = Marshal.StringToHGlobalAnsi(effectiveRomPath);

        try
        {
            var gameInfo = new RetroGameInfo
            {
                Path = pRomPath,
                Data = pinnedRomData.AddrOfPinnedObject(),
                Size = (nuint)romBytes.Length,
                Meta = IntPtr.Zero
            };

            onStatus?.Invoke("Загрузка игрового картриджа в память...");
            bool loaded = _retroLoadGame!(ref gameInfo);
            if (!loaded)
            {
                throw new Exception($"Ядро {rom.CoreDisplayName} отклонило загрузку ROM {rom.Name}!");
            }
        }
        finally
        {
            if (pinnedRomData.IsAllocated) pinnedRomData.Free();
            if (pRomPath != IntPtr.Zero) Marshal.FreeHGlobal(pRomPath);
        }

        // 6. Query AV info
        _retroGetSystemAvInfo!(out var avInfo);
        _targetFps = avInfo.Timing.Fps > 10.0 ? avInfo.Timing.Fps : 60.0;
        _sampleRate = avInfo.Timing.SampleRate > 8000 ? (uint)avInfo.Timing.SampleRate : 44100;

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
        _rewindHistory.Clear();

        _emulationThread = new Thread(EmulationLoop)
        {
            IsBackground = true,
            Name = "NativeLibretroEmulationThread",
            Priority = ThreadPriority.Normal
        };
        _emulationThread.Start();

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

            case 15: // RETRO_ENVIRONMENT_GET_VARIABLE
                if (data != IntPtr.Zero)
                {
                    // retro_variable { key, value }
                    Marshal.WriteIntPtr(data, IntPtr.Size, IntPtr.Zero);
                }
                return false;

            case 17: // RETRO_ENVIRONMENT_GET_VARIABLE_UPDATE
                if (data != IntPtr.Zero) Marshal.WriteByte(data, 0);
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

        // Convert raw pixels to BGRA32
        unsafe
        {
            byte* srcBytes = (byte*)data.ToPointer();
            int srcPitch = (int)pitch;
            bool applyScanlines = (_shaderFilterMode == 1);

            fixed (byte* dstFixed = _bgraBuffer)
            {
                for (int y = 0; y < h; y++)
                {
                    byte* srcRow = srcBytes + (y * srcPitch);
                    byte* dstRow = dstFixed + (y * w * 4);
                    bool isOddScanline = applyScanlines && (y % 2 != 0);

                    if (_pixelFormat == 1) // XRGB8888
                    {
                        uint* pSrc = (uint*)srcRow;
                        for (int x = 0; x < w; x++)
                        {
                            uint val = pSrc[x];
                            byte b = (byte)(val & 0xFF);
                            byte g = (byte)((val >> 8) & 0xFF);
                            byte r = (byte)((val >> 16) & 0xFF);

                            if (isOddScanline)
                            {
                                b = (byte)(b * 3 / 4);
                                g = (byte)(g * 3 / 4);
                                r = (byte)(r * 3 / 4);
                            }

                            int di = x * 4;
                            dstRow[di] = b;
                            dstRow[di + 1] = g;
                            dstRow[di + 2] = r;
                            dstRow[di + 3] = 255;
                        }
                    }
                    else if (_pixelFormat == 2) // RGB565
                    {
                        ushort* pSrc = (ushort*)srcRow;
                        for (int x = 0; x < w; x++)
                        {
                            ushort val = pSrc[x];
                            byte b = (byte)((val & 0x1F) * 255 / 31);
                            byte g = (byte)(((val >> 5) & 0x3F) * 255 / 63);
                            byte r = (byte)(((val >> 11) & 0x1F) * 255 / 31);

                            if (isOddScanline)
                            {
                                b = (byte)(b * 3 / 4);
                                g = (byte)(g * 3 / 4);
                                r = (byte)(r * 3 / 4);
                            }

                            int di = x * 4;
                            dstRow[di] = b;
                            dstRow[di + 1] = g;
                            dstRow[di + 2] = r;
                            dstRow[di + 3] = 255;
                        }
                    }
                    else // 0RGB1555
                    {
                        ushort* pSrc = (ushort*)srcRow;
                        for (int x = 0; x < w; x++)
                        {
                            ushort val = pSrc[x];
                            byte b = (byte)((val & 0x1F) * 255 / 31);
                            byte g = (byte)(((val >> 5) & 0x1F) * 255 / 31);
                            byte r = (byte)(((val >> 10) & 0x1F) * 255 / 31);

                            if (isOddScanline)
                            {
                                b = (byte)(b * 3 / 4);
                                g = (byte)(g * 3 / 4);
                                r = (byte)(r * 3 / 4);
                            }

                            int di = x * 4;
                            dstRow[di] = b;
                            dstRow[di + 1] = g;
                            dstRow[di + 2] = r;
                            dstRow[di + 3] = 255;
                        }
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
            Application.Current?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
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

    private void OnInputPoll()
    {
        // Polled before input_state
    }

    private short OnInputState(uint port, uint device, uint index, uint id)
    {
        if (port != 0 || device != 1) return 0; // Only Port 0, Joypad device

        // Check if our window is foreground
        IntPtr fg = GetForegroundWindow();
        GetWindowThreadProcessId(fg, out uint pid);
        if (pid != (uint)Environment.ProcessId) return 0;

        bool isDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        // RetroPad mapping
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
                if (_frameCount % 15 == 0 && _serializeSize > 0)
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
            byte[] state = new byte[(int)_serializeSize];
            GCHandle pin = GCHandle.Alloc(state, GCHandleType.Pinned);
            try
            {
                if (_retroSerialize(pin.AddrOfPinnedObject(), _serializeSize))
                {
                    lock (_rewindHistory)
                    {
                        if (_rewindHistory.Count >= MaxRewindFrames)
                        {
                            _rewindHistory.RemoveAt(0);
                        }
                        _rewindHistory.Add(state);
                    }
                }
            }
            finally
            {
                pin.Free();
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

    public void CycleShaderFilter()
    {
        _shaderFilterMode = (_shaderFilterMode + 1) % 4;
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
                    double h = viewportContainer.ActualHeight;
                    double w = Math.Min(viewportContainer.ActualWidth, h * 16.0 / 9.0);
                    _targetImage.Width = w;
                    _targetImage.Height = h;
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

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Game";
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name.Trim();
    }

    public bool SaveState(string? slotOrPath = null) => SaveState(_activeStateSlot, slotOrPath);

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
                        CaptureScreenshot(statePath + ".png");
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
        lock (_rewindHistory)
        {
            if (_rewindHistory.Count > 0)
            {
                prev = _rewindHistory[^1];
                _rewindHistory.RemoveAt(_rewindHistory.Count - 1);
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

        if (!string.IsNullOrEmpty(CurrentGameTitle))
        {
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
