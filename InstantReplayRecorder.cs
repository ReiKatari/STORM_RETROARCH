using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StormRetroArch;

/// <summary>
/// Компактный циклический рекордер мгновенных видеоповторов последних 30 секунд геймплея.
/// Захватывает каждый 4-й кадр эмуляции (15 FPS) в оптимизированный пул памяти (320x240 BGRA).
/// Экспортирует повтор в MP4 (через ffmpeg) или анимированный GIF с пониженным приоритетом процесса (BelowNormal).
/// </summary>
public sealed class InstantReplayRecorder
{
    private static readonly Lazy<InstantReplayRecorder> _instance = new(() => new InstantReplayRecorder());
    public static InstantReplayRecorder Instance => _instance.Value;

    public const int TargetWidth = 320;
    public const int TargetHeight = 240;
    public const int FrameBytes = TargetWidth * TargetHeight * 4; // 307,200 bytes
    public const int MaxFrames = 450; // ~30 секунд при захвате каждого 4-го кадра на 60 FPS
    public const int CaptureInterval = 4;

    private readonly byte[][] _frameRing = new byte[MaxFrames][];
    private readonly object _lock = new();

    private int _frameCounter = 0;
    private int _head = 0;
    private int _count = 0;

    public int RecordedFramesCount
    {
        get
        {
            lock (_lock) return _count;
        }
    }

    public InstantReplayRecorder()
    {
        // Pre-allocate or lazily allocate ring buffers
        for (int i = 0; i < MaxFrames; i++)
        {
            _frameRing[i] = new byte[FrameBytes];
        }
    }

    /// <summary>
    /// Захватывает кадр эмуляции в циклический буфер при наступлении каждого 4-го кадра.
    /// Выполняет быстрое субдискретизированное масштабирование до 320x240 BGRA.
    /// </summary>
    public void CaptureFrameIfDue(ReadOnlySpan<byte> bgraSrc, int srcW, int srcH)
    {
        if (bgraSrc.IsEmpty || srcW <= 0 || srcH <= 0) return;

        int count = Interlocked.Increment(ref _frameCounter);
        if (count % CaptureInterval != 0) return;

        lock (_lock)
        {
            byte[] dest = _frameRing[_head];

            // Fast nearest-neighbor scaler from srcW x srcH to 320 x 240 BGRA
            int xRatio = (srcW << 16) / TargetWidth;
            int yRatio = (srcH << 16) / TargetHeight;

            unsafe
            {
                fixed (byte* pSrc = bgraSrc)
                fixed (byte* pDest = dest)
                {
                    uint* src32 = (uint*)pSrc;
                    uint* dest32 = (uint*)pDest;

                    for (int y = 0; y < TargetHeight; y++)
                    {
                        int srcY = (y * yRatio) >> 16;
                        if (srcY >= srcH) srcY = srcH - 1;
                        uint* srcRow = src32 + (srcY * srcW);
                        uint* destRow = dest32 + (y * TargetWidth);

                        for (int x = 0; x < TargetWidth; x++)
                        {
                            int srcX = (x * xRatio) >> 16;
                            if (srcX >= srcW) srcX = srcW - 1;
                            destRow[x] = srcRow[srcX];
                        }
                    }
                }
            }

            _head = (_head + 1) % MaxFrames;
            if (_count < MaxFrames) _count++;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _head = 0;
            _count = 0;
            _frameCounter = 0;
        }
    }

    /// <summary>
    /// Экспортирует последние 30 секунд геймплея в MP4 или анимированный GIF в фоновом режиме.
    /// </summary>
    public async Task<(bool Success, string OutputPath, int FrameCount, string MessageRu)> SaveLast30SecondsAsync(string gameName)
    {
        int snapshotCount;
        byte[][] framesSnapshot;

        lock (_lock)
        {
            snapshotCount = _count;
            if (snapshotCount == 0)
            {
                return (false, "", 0, "Буфер повтора пуст — запись начнётся после запуска игры.");
            }

            framesSnapshot = new byte[snapshotCount][];
            int start = (_head - snapshotCount + MaxFrames) % MaxFrames;
            for (int i = 0; i < snapshotCount; i++)
            {
                int ringIdx = (start + i) % MaxFrames;
                framesSnapshot[i] = new byte[FrameBytes];
                Buffer.BlockCopy(_frameRing[ringIdx], 0, framesSnapshot[i], 0, FrameBytes);
            }
        }

        return await Task.Run(() =>
        {
            try
            {
                string recDir = Path.Combine(SettingsManager.AppRootDir, "recordings");
                if (!Directory.Exists(recDir)) Directory.CreateDirectory(recDir);

                string safeName = string.Join("_", (string.IsNullOrWhiteSpace(gameName) ? "Gameplay" : gameName).Split(Path.GetInvalidFileNameChars()));
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                string? ffmpegPath = FindFfmpegExecutable();

                if (!string.IsNullOrEmpty(ffmpegPath))
                {
                    string mp4Path = Path.Combine(recDir, $"{safeName}_{timestamp}.mp4");
                    bool mp4Ok = EncodeToMp4WithFfmpeg(ffmpegPath, mp4Path, framesSnapshot);
                    if (mp4Ok && File.Exists(mp4Path))
                    {
                        return (true, mp4Path, snapshotCount, $"Видеоповтор сохранён в MP4: {Path.GetFileName(mp4Path)}");
                    }
                }

                // Fallback to native WPF GifBitmapEncoder
                string gifPath = Path.Combine(recDir, $"{safeName}_{timestamp}.gif");
                bool gifOk = EncodeToGif(gifPath, framesSnapshot);
                if (gifOk && File.Exists(gifPath))
                {
                    return (true, gifPath, snapshotCount, $"Видеоповтор сохранён в GIF: {Path.GetFileName(gifPath)}");
                }

                return (false, "", 0, "Не удалось выполнить кодирование видеоповтора.");
            }
            catch (Exception ex)
            {
                return (false, "", 0, $"Ошибка сохранения повтора: {ex.Message}");
            }
        }).ConfigureAwait(false);
    }

    private static string? FindFfmpegExecutable()
    {
        string local1 = Path.Combine(SettingsManager.AppRootDir, "ffmpeg.exe");
        if (File.Exists(local1)) return local1;

        string local2 = Path.Combine(SettingsManager.AppRootDir, "tools", "ffmpeg.exe");
        if (File.Exists(local2)) return local2;

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var p in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    string candidate = Path.Combine(p, "ffmpeg.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
        }

        return null;
    }

    private static bool EncodeToMp4WithFfmpeg(string ffmpegPath, string outputPath, byte[][] frames)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-y -f rawvideo -vcodec rawvideo -s {TargetWidth}x{TargetHeight} -pix_fmt bgra -r 15 -i - -c:v libx264 -preset fast -crf 23 -pix_fmt yuv420p \"{outputPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true
            };

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            // Set BelowNormal priority
            try { proc.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

            using (var stdin = proc.StandardInput.BaseStream)
            {
                foreach (var frame in frames)
                {
                    stdin.Write(frame, 0, frame.Length);
                }
                stdin.Flush();
            }

            proc.WaitForExit(15000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool EncodeToGif(string outputPath, byte[][] frames)
    {
        try
        {
            var encoder = new GifBitmapEncoder();
            int stride = TargetWidth * 4;

            // Sample every 2nd captured frame for GIF to keep file size reasonable (~7.5 FPS)
            for (int i = 0; i < frames.Length; i += 2)
            {
                var frameBmp = BitmapSource.Create(
                    TargetWidth,
                    TargetHeight,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null,
                    frames[i],
                    stride
                );
                frameBmp.Freeze();
                encoder.Frames.Add(BitmapFrame.Create(frameBmp));
            }

            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(fs);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
