using System;

namespace StormRetroArch;

/// <summary>
/// Real-time Studio Audio DSP & Soft-Limiter Pipeline for STORM RETROARCH.
/// Provides zero-allocation 16-bit stereo PCM processing with Bass/Treble shelving,
/// Mid/Side stereo widening, 38ms room ambience delay, and smooth soft-clipping limiter.
/// </summary>
public sealed class AudioDspEngine
{
    public static AudioDspEngine Shared { get; } = new();

    // DSP Filter States
    private float _lowL = 0f;
    private float _lowR = 0f;
    private float _trebleLowL = 0f;
    private float _trebleLowR = 0f;

    // Room Ambience Ring Buffer (~38ms cross-delay, 4096 samples power of two)
    private const int RingBufferSize = 4096;
    private const int RingBufferMask = RingBufferSize - 1;
    private readonly float[] _echoBufferL = new float[RingBufferSize];
    private readonly float[] _echoBufferR = new float[RingBufferSize];
    private int _echoWritePos = 0;

    // Cached Settings
    public string ActivePreset { get; set; } = "Чистый студийный";
    public int BassGainDb { get; set; } = 0;
    public int TrebleGainDb { get; set; } = 0;
    public int StereoWidthPercent { get; set; } = 100;
    public int ReverbPercent { get; set; } = 0;
    public bool SoftLimiterEnabled { get; set; } = true;

    public AudioDspEngine()
    {
        SyncFromSettings();
    }

    public void SyncFromSettings()
    {
        var settings = SettingsManager.Current;
        if (settings != null)
        {
            ActivePreset = settings.AudioDspPreset ?? "Чистый студийный";
            BassGainDb = settings.AudioBassGainDb;
            TrebleGainDb = settings.AudioTrebleGainDb;
            StereoWidthPercent = settings.AudioStereoWidthPercent;
            ReverbPercent = settings.AudioReverbPercent;
            SoftLimiterEnabled = settings.AudioSoftLimiter;
        }
    }

    public void ApplyPreset(string presetName)
    {
        ActivePreset = presetName ?? "Чистый студийный";

        switch (ActivePreset)
        {
            case "Аркадный бас":
                BassGainDb = 6;
                TrebleGainDb = 2;
                StereoWidthPercent = 110;
                ReverbPercent = 8;
                SoftLimiterEnabled = true;
                break;

            case "Тёплый ЭЛТ-динамик":
                BassGainDb = 3;
                TrebleGainDb = -2;
                StereoWidthPercent = 90;
                ReverbPercent = 12;
                SoftLimiterEnabled = true;
                break;

            case "Широкая 3D-панорама":
                BassGainDb = 2;
                TrebleGainDb = 3;
                StereoWidthPercent = 160;
                ReverbPercent = 25;
                SoftLimiterEnabled = true;
                break;

            case "Кристальный чиптюн":
                BassGainDb = -1;
                TrebleGainDb = 7;
                StereoWidthPercent = 125;
                ReverbPercent = 5;
                SoftLimiterEnabled = true;
                break;

            case "Ночной компрессор":
                BassGainDb = 4;
                TrebleGainDb = -1;
                StereoWidthPercent = 95;
                ReverbPercent = 0;
                SoftLimiterEnabled = true;
                break;

            case "Чистый студийный":
            default:
                ActivePreset = "Чистый студийный";
                BassGainDb = 0;
                TrebleGainDb = 0;
                StereoWidthPercent = 100;
                ReverbPercent = 0;
                SoftLimiterEnabled = true;
                break;
        }

        var s = SettingsManager.Current;
        if (s != null)
        {
            s.AudioDspPreset = ActivePreset;
            s.AudioBassGainDb = BassGainDb;
            s.AudioTrebleGainDb = TrebleGainDb;
            s.AudioStereoWidthPercent = StereoWidthPercent;
            s.AudioReverbPercent = ReverbPercent;
            s.AudioSoftLimiter = SoftLimiterEnabled;
        }
    }

    /// <summary>
    /// Processes 16-bit interleaved stereo samples in-place with 0 memory allocation.
    /// </summary>
    public void ProcessStereoPcm16(Span<short> interleavedSamples, int sampleRate)
    {
        if (interleavedSamples.IsEmpty) return;
        if (sampleRate <= 8000) sampleRate = 44100;

        // Bypass check for pure neutral bypass
        bool isNeutral = BassGainDb == 0 && TrebleGainDb == 0 && StereoWidthPercent == 100 && ReverbPercent == 0;
        if (isNeutral && !SoftLimiterEnabled) return;

        // Bass 1-pole low-pass coefficient (~180 Hz)
        float alphaB = Math.Clamp(2f * MathF.PI * 180f / sampleRate, 0.001f, 0.999f);
        float bassMult = MathF.Pow(10f, BassGainDb / 20f) - 1.0f;

        // Treble 1-pole low-pass coefficient for high-shelf (~4200 Hz)
        float alphaT = Math.Clamp(2f * MathF.PI * 4200f / sampleRate, 0.001f, 0.999f);
        float trebleMult = MathF.Pow(10f, TrebleGainDb / 20f) - 1.0f;

        // Stereo widener factor
        float width = Math.Clamp(StereoWidthPercent / 100f, 0.5f, 1.8f);

        // Room Ambience (~38ms cross-delay)
        int delaySamples = (int)(sampleRate * 0.038f);
        if (delaySamples >= RingBufferSize) delaySamples = RingBufferSize - 1;
        float wet = Math.Clamp((ReverbPercent / 100f) * 0.40f, 0f, 0.5f);

        int sampleCount = interleavedSamples.Length;
        for (int i = 0; i < sampleCount - 1; i += 2)
        {
            float inL = interleavedSamples[i];
            float inR = interleavedSamples[i + 1];

            // 1. Bass Low-Shelf Filter
            _lowL += alphaB * (inL - _lowL);
            _lowR += alphaB * (inR - _lowR);
            float bL = inL + bassMult * _lowL;
            float bR = inR + bassMult * _lowR;

            // 2. Treble High-Shelf Filter
            _trebleLowL += alphaT * (bL - _trebleLowL);
            _trebleLowR += alphaT * (bR - _trebleLowR);
            float highL = bL - _trebleLowL;
            float highR = bR - _trebleLowR;
            float tL = bL + trebleMult * highL;
            float tR = bR + trebleMult * highR;

            // 3. Mid / Side Stereo Widener
            float mid = (tL + tR) * 0.5f;
            float side = (tL - tR) * 0.5f * width;
            float wL = mid + side;
            float wR = mid - side;

            // 4. Room Ambience Echo (~38ms stereo cross-delay)
            if (wet > 0.001f)
            {
                int readPos = (_echoWritePos - delaySamples + RingBufferSize) & RingBufferMask;
                float delayedL = _echoBufferL[readPos];
                float delayedR = _echoBufferR[readPos];

                // Cross-feed left to right, right to left
                wL += delayedR * wet;
                wR += delayedL * wet;

                // Update ring buffer with decay feedback
                _echoBufferL[_echoWritePos] = tL + delayedL * 0.28f;
                _echoBufferR[_echoWritePos] = tR + delayedR * 0.28f;
                _echoWritePos = (_echoWritePos + 1) & RingBufferMask;
            }

            // 5. Soft-Limiter
            if (SoftLimiterEnabled)
            {
                interleavedSamples[i] = SoftClip(wL);
                interleavedSamples[i + 1] = SoftClip(wR);
            }
            else
            {
                interleavedSamples[i] = (short)Math.Clamp((int)wL, -32767, 32767);
                interleavedSamples[i + 1] = (short)Math.Clamp((int)wR, -32767, 32767);
            }
        }
    }

    private static short SoftClip(float sample)
    {
        const float threshold = 28000f;
        if (sample > threshold)
        {
            float excess = sample - threshold;
            float compressed = threshold + (4767f * excess) / (excess + 4767f);
            return (short)Math.Clamp((int)compressed, -32767, 32767);
        }
        if (sample < -threshold)
        {
            float excess = -sample - threshold;
            float compressed = -(threshold + (4767f * excess) / (excess + 4767f));
            return (short)Math.Clamp((int)compressed, -32767, 32767);
        }
        return (short)sample;
    }
}
