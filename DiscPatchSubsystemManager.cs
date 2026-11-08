using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace StormRetroArch;

/// <summary>
/// Отчёт о контрольных суммах и формате контейнера ROM-файла.
/// </summary>
public class RomHashReport
{
    public string FileName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string FileSizeFormatted { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string Crc32 { get; set; } = "";
    public string Md5 { get; set; } = "";
    public string Sha1 { get; set; } = "";
    public string ContainerFormat { get; set; } = "";
    public string HeaderSummary { get; set; } = "";
}

public static class DiscPatchSubsystemManager
{
    // ==================== M3U MULTI-DISC PLAYLIST GENERATOR ====================

    private static readonly uint[] Crc32Table = new uint[256];

    static DiscPatchSubsystemManager()
    {
        const uint poly = 0xEDB88320u;
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
            {
                crc = (crc & 1) != 0 ? (poly ^ (crc >> 1)) : (crc >> 1);
            }
            Crc32Table[i] = crc;
        }
    }

    /// <summary>
    /// Создает файл плейлиста .m3u в кодировке UTF-8 без BOM и возвращает абсолютный путь к файлу.
    /// </summary>
    public static string CreateM3uPlaylist(string gameTitle, IEnumerable<string> discPaths, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameTitle)) gameTitle = "MultiDiscGame";
        if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);

        string m3uPath = Path.Combine(outputDirectory, $"{gameTitle}.m3u");
        var sb = new StringBuilder();

        foreach (var disc in discPaths)
        {
            if (File.Exists(disc))
            {
                string rel = Path.GetRelativePath(outputDirectory, disc);
                sb.AppendLine(rel);
            }
        }

        File.WriteAllText(m3uPath, sb.ToString(), new UTF8Encoding(false));
        return m3uPath;
    }

    public static string GenerateM3uPlaylist(string gameTitle, IEnumerable<string> discPaths, string outputDirectory)
    {
        return CreateM3uPlaylist(gameTitle, discPaths, outputDirectory);
    }

    /// <summary>
    /// Быстрое асинхронное вычисление CRC32, MD5, SHA-1 и определение формата ROM-файла.
    /// Потоковое чтение с защитой от зависания на гигантских ISO-образах (лимит хеширования 128 МБ).
    /// </summary>
    public static async Task<RomHashReport> InspectRomFileAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            var fi = new FileInfo(filePath);
            if (!fi.Exists)
            {
                return new RomHashReport
                {
                    FileName = Path.GetFileName(filePath),
                    FullPath = filePath,
                    ContainerFormat = "Файл не найден"
                };
            }

            long fileSizeBytes = fi.Length;
            string sizeFormatted = RomItem.FormatFileSize(fileSizeBytes);
            const long maxHashBytes = 128 * 1024 * 1024; // 128 MB max cap

            byte[] headerBytes = new byte[Math.Min(4096, (int)Math.Min(fileSizeBytes, 4096))];
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.ReadExactly(headerBytes, 0, headerBytes.Length);
            }

            uint crc = 0xFFFFFFFFu;
            byte[] md5Hash;
            byte[] sha1Hash;

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var md5 = System.Security.Cryptography.MD5.Create())
            using (var sha1 = System.Security.Cryptography.SHA1.Create())
            {
                byte[] buffer = new byte[65536];
                long totalRead = 0;
                int bytesRead;

                while (totalRead < maxHashBytes && (bytesRead = fs.Read(buffer, 0, (int)Math.Min(buffer.Length, maxHashBytes - totalRead))) > 0)
                {
                    for (int i = 0; i < bytesRead; i++)
                    {
                        crc = Crc32Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
                    }
                    md5.TransformBlock(buffer, 0, bytesRead, null, 0);
                    sha1.TransformBlock(buffer, 0, bytesRead, null, 0);
                    totalRead += bytesRead;
                }

                md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                md5Hash = md5.Hash ?? Array.Empty<byte>();
                sha1Hash = sha1.Hash ?? Array.Empty<byte>();
            }

            crc ^= 0xFFFFFFFFu;
            string crcStr = crc.ToString("X8");
            string md5Str = Convert.ToHexString(md5Hash).ToUpperInvariant();
            string sha1Str = Convert.ToHexString(sha1Hash).ToUpperInvariant();

            var (format, summary) = DetectFormatAndSummary(filePath, headerBytes, fileSizeBytes);

            return new RomHashReport
            {
                FileName = fi.Name,
                FullPath = fi.FullName,
                FileSizeBytes = fileSizeBytes,
                FileSizeFormatted = sizeFormatted,
                Crc32 = crcStr,
                Md5 = md5Str,
                Sha1 = sha1Str,
                ContainerFormat = format,
                HeaderSummary = summary
            };
        });
    }

    private static (string Format, string Summary) DetectFormatAndSummary(string filePath, byte[] h, long fileSize)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();

        if (h.Length >= 4 && h[0] == (byte)'N' && h[1] == (byte)'E' && h[2] == (byte)'S' && h[3] == 0x1A)
        {
            int prg16k = h.Length > 4 ? h[4] : 0;
            int chr8k = h.Length > 5 ? h[5] : 0;
            int mapper = (h.Length > 7) ? ((h[6] >> 4) | (h[7] & 0xF0)) : 0;
            return ("NES iNES 1.0/2.0", $"PRG: {prg16k * 16} KB, CHR: {chr8k * 8} KB, Mapper: #{mapper}");
        }

        if (h.Length >= 8 && h[0] == (byte)'M' && h[1] == (byte)'C' && h[2] == (byte)'o' && h[3] == (byte)'m' &&
            h[4] == (byte)'p' && h[5] == (byte)'r' && h[6] == (byte)'H' && h[7] == (byte)'D')
        {
            return ("CHD v5 Disc Container", "Сжатый образ оптического диска CHD v5");
        }

        if (h.Length >= 4 && h[0] == 0x50 && h[1] == 0x4B && h[2] == 0x03 && h[3] == 0x04)
        {
            return ("ZIP Archive", "Сжатый архив с игровым образом");
        }

        if (h.Length >= 2 && h[0] == (byte)'M' && h[1] == (byte)'Z')
        {
            return ("MS-DOS EXE/ZIP", "Исполняемый файл DOS MZ / Самораспаковывающийся архив");
        }

        if (h.Length >= 4 && ((h[0] == 0x80 && h[1] == 0x37 && h[2] == 0x12 && h[3] == 0x40) ||
                             (h[0] == 0x37 && h[1] == 0x80 && h[2] == 0x40 && h[3] == 0x12) ||
                             (h[0] == 0x40 && h[1] == 0x12 && h[2] == 0x37 && h[3] == 0x80)) ||
            ext is ".z64" or ".n64" or ".v64")
        {
            return ("Nintendo 64 ROM", "Образ картриджа Nintendo 64 (MIPS VR4300)");
        }

        if (ext == ".gba" || (h.Length >= 4 && h[3] == 0xEA))
        {
            return ("Game Boy Advance ROM", "Картридж GBA (ARM7TDMI 16.78 MHz)");
        }

        if (ext is ".md" or ".gen" or ".smd" || (h.Length >= 0x104 && h[0x100] == (byte)'S' && h[0x101] == (byte)'E' && h[0x102] == (byte)'G' && h[0x103] == (byte)'A'))
        {
            return ("SEGA Mega Drive / Genesis", "Картридж Sega 16-бит (Motorola 68000)");
        }

        if (ext is ".smc" or ".sfc")
        {
            return ("SFC/SNES ROM", "Картридж Super Nintendo (WDC 65C816)");
        }

        if (ext is ".iso" or ".cue" or ".bin" or ".pbp")
        {
            return ("PS1 ISO/CUE Disc", "Образ оптического диска Sony PlayStation");
        }

        return ("Бинарный ROM", $"Формат файла {ext.ToUpperInvariant()} ({RomItem.FormatFileSize(fileSize)})");
    }


    // Disc Eject / Swap commands via in-app engine or UDP
    public static void EjectOrInsertDisc()
    {
        if (NativeLibretroHost.Current.IsRunning)
        {
            bool ejected = NativeLibretroHost.Current.ToggleDiscEject();
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (System.Windows.Application.Current.MainWindow is MainWindow mw)
                {
                    mw.ShowOsdToast(ejected ? "Дисковод: Диск извлечен [EJECT]" : "Дисковод: Диск вставлен [INSERT]");
                }
            });
        }
        else
        {
            UdpCommandBridge.DiskEjectToggle();
        }
    }

    public static void NextDisc()
    {
        if (NativeLibretroHost.Current.IsRunning)
        {
            int idx = NativeLibretroHost.Current.CycleNextDisc();
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (System.Windows.Application.Current.MainWindow is MainWindow mw)
                {
                    mw.ShowOsdToast($"Дисковод: Переключено на Диск #{idx + 1}");
                }
            });
        }
        else
        {
            UdpCommandBridge.DiskNext();
        }
    }

    public static void PreviousDisc()
    {
        if (NativeLibretroHost.Current.IsRunning)
        {
            int idx = NativeLibretroHost.Current.CyclePrevDisc();
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (System.Windows.Application.Current.MainWindow is MainWindow mw)
                {
                    mw.ShowOsdToast($"Дисковод: Переключено на Диск #{idx + 1}");
                }
            });
        }
        else
        {
            UdpCommandBridge.DiskPrev();
        }
    }

    // ==================== SOFT & HARD PATCHING (.IPS, .BPS, .UPS) ====================

    public static (bool Success, string Message) AttachSoftPatch(string romPath, string patchPath)
    {
        if (!File.Exists(romPath)) return (false, "ROM-файл не найден");
        if (!File.Exists(patchPath)) return (false, "Файл патча не найден");

        try
        {
            string romDir = Path.GetDirectoryName(romPath) ?? "";
            string romBaseName = Path.GetFileNameWithoutExtension(romPath);
            string patchExt = Path.GetExtension(patchPath);

            string targetPatchPath = Path.Combine(romDir, romBaseName + patchExt);
            File.Copy(patchPath, targetPatchPath, true);

            return (true, $"Мягкий патч привязан: {Path.GetFileName(targetPatchPath)}\nRetroArch применит его автоматически при запуске!");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка привязки патча: {ex.Message}");
        }
    }

    public static (bool Success, string Message) ApplyIpsPatch(string romPath, string ipsPath, string outputPath)
    {
        if (!File.Exists(romPath) || !File.Exists(ipsPath))
            return (false, "Исходные файлы не найдены");

        try
        {
            byte[] rom = File.ReadAllBytes(romPath);
            byte[] patch = File.ReadAllBytes(ipsPath);

            if (patch.Length < 8 || Encoding.ASCII.GetString(patch, 0, 5) != "PATCH")
                return (false, "Неверный заголовок IPS патча (ожидается 'PATCH')");

            int offset = 5;
            var mem = new MemoryStream();
            mem.Write(rom, 0, rom.Length);

            while (offset < patch.Length - 2)
            {
                if (offset + 3 <= patch.Length && Encoding.ASCII.GetString(patch, offset, 3) == "EOF")
                    break;

                int targetOffset = (patch[offset] << 16) | (patch[offset + 1] << 8) | patch[offset + 2];
                offset += 3;

                int size = (patch[offset] << 8) | patch[offset + 1];
                offset += 2;

                if (size == 0) // RLE
                {
                    int rleSize = (patch[offset] << 8) | patch[offset + 1];
                    offset += 2;
                    byte rleByte = patch[offset++];

                    if (targetOffset + rleSize > mem.Length) mem.SetLength(targetOffset + rleSize);
                    mem.Position = targetOffset;
                    for (int i = 0; i < rleSize; i++) mem.WriteByte(rleByte);
                }
                else // Literal
                {
                    if (targetOffset + size > mem.Length) mem.SetLength(targetOffset + size);
                    mem.Position = targetOffset;
                    mem.Write(patch, offset, size);
                    offset += size;
                }
            }

            File.WriteAllBytes(outputPath, mem.ToArray());
            return (true, $"ROM успешно пропатчен:\n{Path.GetFileName(outputPath)}");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка применения IPS: {ex.Message}");
        }
    }

    // ==================== SUBSYSTEM DUAL-SLOT LAUNCHER ====================

    public static void LaunchSuperGameBoy(string snesRomPath, string gbRomPath, Action<string>? onError = null)
    {
        string? exe = CoreManager.FindRetroArchExecutable();
        string coreDll = Path.Combine(CoreManager.CoresDir, "snes9x_libretro.dll");

        if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
        {
            string args = $"-L \"{coreDll}\" --subsystem sgb \"{snesRomPath}\" \"{gbRomPath}\"";
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    WorkingDirectory = CoreManager.AppDir,
                    UseShellExecute = false
                });
                return;
            }
            catch (Exception ex)
            {
                onError?.Invoke($"Ошибка запуска Super Game Boy: {ex.Message}");
                return;
            }
        }

        // 100% In-App fallback: launch via native engine
        try
        {
            var item = new RomItem
            {
                Name = Path.GetFileNameWithoutExtension(gbRomPath) + " [SGB]",
                FilePath = gbRomPath,
                Platform = "Game Boy / Color",
                CoreDll = "snes9x_libretro.dll",
                CoreDisplayName = "Snes9x (Super Game Boy Subsystem)"
            };
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (System.Windows.Application.Current.MainWindow is MainWindow mw)
                {
                    _ = mw.StartGameInViewportAsync(item);
                }
            });
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Запуск Super Game Boy: {ex.Message}");
        }
    }
}
