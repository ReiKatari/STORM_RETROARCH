using System;
using System.Runtime.InteropServices;

namespace StormRetroArch;

/// <summary>
/// Аппаратный низколатентный аудио-движок на базе Windows CoreAudio WASAPI Shared (буфер 15 мс)
/// со встроенным динамическим контролем частоты дискретизации (Dynamic Rate Control, DRC).
/// Гарантирует отсутствие щелчков, заиканий и рассинхронизации звука на дисплеях 59.94 / 60 / 120 / 144 Гц / VRR.
/// </summary>
public sealed class WasapiAudioOutputEngine : IDisposable
{
    private static readonly Lazy<WasapiAudioOutputEngine> _shared = new(() => new WasapiAudioOutputEngine());
    public static WasapiAudioOutputEngine Shared => _shared.Value;

    // COM GUIDs
    private static readonly Guid ClsidMMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IidIAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    private static readonly Guid IidIAudioRenderClient = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
    private static readonly Guid SubtypeIeeeFloat = new("00000003-0000-0010-8000-00AA00389B71");

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(in Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out int pdwState);
    }

    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, IntPtr audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint pNumBufferFrames);
        [PreserveSig] int GetStreamLatency(out long phnsLatency);
        [PreserveSig] int GetCurrentPadding(out uint pNumPaddingFrames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr ppDeviceFormat);
        [PreserveSig] int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }

    [Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioRenderClient
    {
        [PreserveSig] int GetBuffer(uint numFramesRequested, out IntPtr ppData);
        [PreserveSig] int ReleaseBuffer(uint numFramesWritten, int dwFlags);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
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

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WAVEFORMATEXTENSIBLE
    {
        public WAVEFORMATEX Format;
        public ushort wValidBitsPerSample;
        public uint dwChannelMask;
        public Guid SubFormat;
    }

    private IAudioClient? _audioClient;
    private IAudioRenderClient? _renderClient;
    private IntPtr _mixFormatPtr = IntPtr.Zero;
    private uint _bufferFrameCount = 0;
    private int _channels = 2;
    private int _sampleRate = 48000;
    private bool _isFloat = true;
    private bool _isActive = false;

    // Dynamic Rate Control (DRC) state
    private double _resamplePhase = 0.0;

    public bool IsActive => _isActive;
    public double CurrentBufferFillPercent { get; private set; } = 50.0;
    public double ActiveDrcRatio { get; private set; } = 1.0;
    public int OutputSampleRate => _sampleRate;

    public bool Initialize(int coreInputSampleRate = 48000)
    {
        Stop();

        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            // eRender = 0, eMultimedia = 1
            int hr = enumerator.GetDefaultAudioEndpoint(0, 1, out IMMDevice device);
            if (hr != 0 || device == null)
            {
                _isActive = false;
                return false;
            }

            // CLSCTX_ALL = 23
            hr = device.Activate(IidIAudioClient, 23, IntPtr.Zero, out object clientObj);
            if (hr != 0 || clientObj == null)
            {
                _isActive = false;
                return false;
            }

            _audioClient = (IAudioClient)clientObj;
            hr = _audioClient.GetMixFormat(out _mixFormatPtr);
            if (hr != 0 || _mixFormatPtr == IntPtr.Zero)
            {
                Stop();
                return false;
            }

            var wfx = Marshal.PtrToStructure<WAVEFORMATEX>(_mixFormatPtr);
            _channels = Math.Max(1, (int)wfx.nChannels);
            _sampleRate = (int)wfx.nSamplesPerSec;

            if (wfx.wFormatTag == 3) // WAVE_FORMAT_IEEE_FLOAT
            {
                _isFloat = true;
            }
            else if (wfx.wFormatTag == 0xFFFE && wfx.cbSize >= 22) // WAVE_FORMAT_EXTENSIBLE
            {
                var ext = Marshal.PtrToStructure<WAVEFORMATEXTENSIBLE>(_mixFormatPtr);
                _isFloat = ext.SubFormat == SubtypeIeeeFloat || ext.Format.wBitsPerSample == 32;
            }
            else
            {
                _isFloat = wfx.wBitsPerSample == 32;
            }

            // AUDCLNT_SHAREMODE_SHARED = 0
            // AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000, NOPERSIST = 0x00080000
            int streamFlags = unchecked((int)0x80080000);
            long bufferDurationHns = 150000; // 15 ms low-latency buffer

            hr = _audioClient.Initialize(0, streamFlags, bufferDurationHns, 0, _mixFormatPtr, IntPtr.Zero);
            if (hr != 0)
            {
                // Retry without AUTOCONVERTPCM
                hr = _audioClient.Initialize(0, 0x00080000, bufferDurationHns, 0, _mixFormatPtr, IntPtr.Zero);
                if (hr != 0)
                {
                    Stop();
                    return false;
                }
            }

            _audioClient.GetBufferSize(out _bufferFrameCount);
            hr = _audioClient.GetService(IidIAudioRenderClient, out object renderObj);
            if (hr != 0 || renderObj == null)
            {
                Stop();
                return false;
            }

            _renderClient = (IAudioRenderClient)renderObj;

            // Pre-fill buffer with silence to guarantee clean startup
            if (_bufferFrameCount > 0 && _renderClient.GetBuffer(_bufferFrameCount, out IntPtr initialData) == 0)
            {
                int bytes = (int)_bufferFrameCount * _channels * (_isFloat ? 4 : 2);
                unsafe
                {
                    NativeMemory.Clear(initialData.ToPointer(), (nuint)bytes);
                }
                _renderClient.ReleaseBuffer(_bufferFrameCount, 0);
            }

            hr = _audioClient.Start();
            if (hr != 0)
            {
                Stop();
                return false;
            }

            _isActive = true;
            return true;
        }
        catch
        {
            Stop();
            _isActive = false;
            return false;
        }
    }

    /// <summary>
    /// Записывает стерео-сэмплы PCM16 из ядра Libretro с динамической коррекцией частоты дискретизации (DRC).
    /// </summary>
    public void WriteAudioSamples(ReadOnlySpan<short> stereoPcm16, int coreInputSampleRate)
    {
        if (!_isActive || _audioClient == null || _renderClient == null || stereoPcm16.IsEmpty)
            return;

        int hr = _audioClient.GetCurrentPadding(out uint paddingFrames);
        if (hr != 0) return;

        double fillRatio = _bufferFrameCount > 0 ? (double)paddingFrames / _bufferFrameCount : 0.5;
        CurrentBufferFillPercent = fillRatio * 100.0;

        uint availableFrames = _bufferFrameCount > paddingFrames ? _bufferFrameCount - paddingFrames : 0;
        if (availableFrames == 0) return;

        // Dynamic Rate Control (DRC) calculation
        double nominalRatio = (double)coreInputSampleRate / _sampleRate;
        if (SettingsManager.Current.DynamicRateControl)
        {
            // If buffer is emptier than 50%, increase ratio slightly to pull more samples
            // If buffer is fuller than 50%, decrease ratio slightly
            double delta = (0.5 - fillRatio) * 2.0 * SettingsManager.Current.DrcMaxDelta;
            delta = Math.Clamp(delta, -SettingsManager.Current.DrcMaxDelta, SettingsManager.Current.DrcMaxDelta);
            ActiveDrcRatio = nominalRatio * (1.0 + delta);
        }
        else
        {
            ActiveDrcRatio = nominalRatio;
        }

        int inputStereoFrames = stereoPcm16.Length / 2;
        int maxOutputFramesPossible = (int)Math.Min(availableFrames, (inputStereoFrames / Math.Max(0.1, ActiveDrcRatio)) + 8);
        if (maxOutputFramesPossible <= 0) return;

        hr = _renderClient.GetBuffer((uint)maxOutputFramesPossible, out IntPtr pData);
        if (hr != 0 || pData == IntPtr.Zero) return;

        uint writtenFrames = 0;
        unsafe
        {
            float* floatPtr = (float*)pData.ToPointer();
            short* pcm16Ptr = (short*)pData.ToPointer();

            while (writtenFrames < (uint)maxOutputFramesPossible)
            {
                int sampleIdx = (int)_resamplePhase;
                if (sampleIdx + 1 >= inputStereoFrames)
                {
                    break;
                }

                double frac = _resamplePhase - sampleIdx;

                short s0L = stereoPcm16[sampleIdx * 2];
                short s0R = stereoPcm16[sampleIdx * 2 + 1];
                short s1L = stereoPcm16[(sampleIdx + 1) * 2];
                short s1R = stereoPcm16[(sampleIdx + 1) * 2 + 1];

                // Linear fractional interpolation
                double interpL = s0L + (s1L - s0L) * frac;
                double interpR = s0R + (s1R - s0R) * frac;

                if (_isFloat)
                {
                    float outL = (float)(interpL / 32768.0);
                    float outR = (float)(interpR / 32768.0);

                    if (_channels >= 2)
                    {
                        floatPtr[writtenFrames * _channels] = outL;
                        floatPtr[writtenFrames * _channels + 1] = outR;
                        for (int c = 2; c < _channels; c++)
                        {
                            floatPtr[writtenFrames * _channels + c] = 0f;
                        }
                    }
                    else
                    {
                        floatPtr[writtenFrames] = (float)((outL + outR) * 0.5);
                    }
                }
                else
                {
                    short outL = (short)Math.Clamp(interpL, short.MinValue, short.MaxValue);
                    short outR = (short)Math.Clamp(interpR, short.MinValue, short.MaxValue);

                    if (_channels >= 2)
                    {
                        pcm16Ptr[writtenFrames * _channels] = outL;
                        pcm16Ptr[writtenFrames * _channels + 1] = outR;
                        for (int c = 2; c < _channels; c++)
                        {
                            pcm16Ptr[writtenFrames * _channels + c] = 0;
                        }
                    }
                    else
                    {
                        pcm16Ptr[writtenFrames] = (short)((outL + outR) / 2);
                    }
                }

                writtenFrames++;
                _resamplePhase += ActiveDrcRatio;
            }

            // Wrap resample phase back to keep precision
            int consumed = (int)_resamplePhase;
            _resamplePhase -= consumed;
        }

        _renderClient.ReleaseBuffer(writtenFrames, 0);
    }

    public void Stop()
    {
        _isActive = false;

        try { _audioClient?.Stop(); } catch { }
        try { _audioClient?.Reset(); } catch { }

        _renderClient = null;
        _audioClient = null;

        if (_mixFormatPtr != IntPtr.Zero)
        {
            try { Marshal.FreeCoTaskMem(_mixFormatPtr); } catch { }
            _mixFormatPtr = IntPtr.Zero;
        }

        _resamplePhase = 0.0;
    }

    public void Dispose()
    {
        Stop();
    }
}
