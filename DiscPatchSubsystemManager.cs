using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StormRetroArch;

public static class DiscPatchSubsystemManager
{
    // ==================== M3U MULTI-DISC PLAYLIST GENERATOR ====================

    public static string GenerateM3uPlaylist(string gameTitle, IEnumerable<string> discPaths, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameTitle)) gameTitle = "MultiDiscGame";
        if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);

        string m3uPath = Path.Combine(outputDirectory, $"{gameTitle}.m3u");
        var sb = new StringBuilder();

        foreach (var disc in discPaths)
        {
            if (File.Exists(disc))
            {
                // Write relative file name if in same directory, or full path
                string rel = Path.GetRelativePath(outputDirectory, disc);
                sb.AppendLine(rel);
            }
        }

        File.WriteAllText(m3uPath, sb.ToString(), Encoding.UTF8);
        return m3uPath;
    }

    // Disc Eject / Swap commands via UDP
    public static void EjectOrInsertDisc() => UdpCommandBridge.DiskEjectToggle();
    public static void NextDisc() => UdpCommandBridge.DiskNext();
    public static void PreviousDisc() => UdpCommandBridge.DiskPrev();

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
        if (string.IsNullOrEmpty(exe))
        {
            onError?.Invoke("Исполняемый файл retroarch.exe не найден");
            return;
        }

        string coreDll = Path.Combine(CoreManager.CoresDir, "snes9x_libretro.dll");
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
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Ошибка запуска Super Game Boy: {ex.Message}");
        }
    }
}
