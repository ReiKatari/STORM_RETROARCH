using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace StormRetroArch;

public record RomSignatureResult(string PlatformName, string PreferredCoreDll, string SignatureDetail);

/// <summary>
/// Fast, safe, zero-allocation binary header and container scanner
/// to accurately disambiguate ambiguous ROM extensions (.iso, .chd, .bin, .cue, .zip, etc.).
/// </summary>
public static class RomSignatureDetector
{
    private static readonly byte[] IsoPvdMagic = Encoding.ASCII.GetBytes("CD001");
    private static readonly byte[] SegaKatana = Encoding.ASCII.GetBytes("SEGA SEGAKATANA");
    private static readonly byte[] SegaSaturn = Encoding.ASCII.GetBytes("SEGA SEGASATURN");
    private static readonly byte[] SegaDiscSystem1 = Encoding.ASCII.GetBytes("SEGADISCSYSTEM");
    private static readonly byte[] SegaDiscSystem2 = Encoding.ASCII.GetBytes("SEGA DISCSYSTEM");
    private static readonly byte[] XboxMediaMagic = Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA");
    private static readonly byte[] Ps2Boot2Marker1 = Encoding.ASCII.GetBytes("BOOT2 = cdrom0:");
    private static readonly byte[] Ps2Boot2Marker2 = Encoding.ASCII.GetBytes("BOOT2=cdrom0:");
    private static readonly byte[] Ps1BootMarker1 = Encoding.ASCII.GetBytes("BOOT = cdrom:");
    private static readonly byte[] Ps1BootMarker2 = Encoding.ASCII.GetBytes("BOOT=cdrom:");
    private static readonly byte[] Ps1PlaystationMarker = Encoding.ASCII.GetBytes("PLAYSTATION");
    private static readonly byte[] PspGameMarker = Encoding.ASCII.GetBytes("PSP_GAME");
    private static readonly byte[] Ps3GameMarker = Encoding.ASCII.GetBytes("PS3_GAME");
    private static readonly byte[] Ps3SfbMarker = Encoding.ASCII.GetBytes("PS3_DISC.SFB");
    private static readonly byte[] NcsdMagic = Encoding.ASCII.GetBytes("NCSD");
    private static readonly byte[] NcchMagic = Encoding.ASCII.GetBytes("NCCH");
    private static readonly byte[] RvzMagic = new byte[] { 0x52, 0x56, 0x5A, 0x01 }; // RVZ\x01
    private static readonly byte[] WbfsMagic = new byte[] { 0x57, 0x42, 0x46, 0x53 }; // WBFS
    private static readonly byte[] WuaMagic = new byte[] { 0x57, 0x55, 0x41, 0x30 }; // WUA0
    private static readonly byte[] CisoMagic = Encoding.ASCII.GetBytes("CISO");
    private static readonly byte[] ZisoMagic = Encoding.ASCII.GetBytes("ZISO");
    private static readonly byte[] ChdMagic = Encoding.ASCII.GetBytes("MComprHD");

    public static RomSignatureResult? Detect(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        string fileName = Path.GetFileName(filePath);
        string parentDir = Path.GetFileName(Path.GetDirectoryName(filePath) ?? "");

        try
        {
            // 1. Nintendo 3DS specific extensions
            if (ext is ".3ds" or ".cci" or ".cxi" or ".3dsx" or ".cia")
            {
                return new RomSignatureResult("Nintendo 3DS", "azahar_libretro.dll", "3DS CTR/NCSD");
            }

            // 2. Wii U specific extensions
            if (ext is ".wua" or ".wud" or ".wux" or ".rpx")
            {
                return new RomSignatureResult("Wii U", "cemu_libretro.dll", "Wii U Title");
            }

            // 3. PS Vita VPK archive
            if (ext == ".vpk")
            {
                return new RomSignatureResult("PS Vita", "vita3k_libretro.dll", "Vita VPK");
            }

            // 4. PS3 SFB / SFO
            if (ext == ".sfb" || fileName.Equals("PS3_DISC.SFB", StringComparison.OrdinalIgnoreCase))
            {
                return new RomSignatureResult("PlayStation 3", "rpcs3_libretro.dll", "PS3 Disc SFB");
            }

            // 5. Sony PKG container (PS3 vs PS Vita)
            if (ext == ".pkg")
            {
                return DetectPkg(filePath);
            }

            // 6. GameCube / Wii RVZ, GCZ, WBFS
            if (ext is ".rvz" or ".gcz" or ".wbfs")
            {
                return DetectRvzWbfs(filePath);
            }

            // 7. Xbox XISO / XEX
            if (ext is ".xiso" or ".xex")
            {
                if (ext == ".xex")
                    return new RomSignatureResult("Xbox 360", "xenia_edge_libretro.dll", "Xbox 360 XEX");
                return new RomSignatureResult("Xbox", "xemu_libretro.dll", "Xbox XISO");
            }

            // 8. ZIP archive (Arcade MAME/FBNeo vs MS-DOS vs Console ROMs)
            if (ext == ".zip")
            {
                return DetectZipContainer(filePath);
            }

            // 9. Compressed ISO: CSO / ZSO
            if (ext is ".cso" or ".zso")
            {
                return DetectCsoZso(filePath, parentDir);
            }

            // 10. MAME CHD archive (.chd)
            if (ext == ".chd")
            {
                return DetectChd(filePath, parentDir);
            }

            // 11. ISO, BIN, IMG, MDF image files
            if (ext is ".iso" or ".bin" or ".img" or ".mdf")
            {
                return DetectOpticalDiscImage(filePath, parentDir);
            }

            // 12. CUE sheets - inspect referenced BIN/ISO file if existing
            if (ext == ".cue")
            {
                return DetectCueSheet(filePath, parentDir);
            }
        }
        catch
        {
            // Fall through safely on any IO exception
        }

        return null;
    }

    private static RomSignatureResult DetectPkg(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] header = new byte[128];
            int read = fs.Read(header, 0, header.Length);
            if (read >= 4 && header[0] == 0x7F && header[1] == 0x50 && header[2] == 0x4B && header[3] == 0x47) // 0x7F 'P' 'K' 'G'
            {
                string text = Encoding.ASCII.GetString(header, 0, read);
                if (text.Contains("PCS") || filePath.Contains("Vita", StringComparison.OrdinalIgnoreCase))
                {
                    return new RomSignatureResult("PS Vita", "vita3k_libretro.dll", "Vita PKG Package");
                }
                return new RomSignatureResult("PlayStation 3", "rpcs3_libretro.dll", "PS3 PKG Package");
            }
        }
        catch { }

        return new RomSignatureResult("PlayStation 3", "rpcs3_libretro.dll", "PS3 Package");
    }

    private static RomSignatureResult DetectRvzWbfs(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] buf = new byte[256];
            int read = fs.Read(buf, 0, buf.Length);
            if (read >= 0x60)
            {
                // Check GameCube magic 0xC2339F3D
                uint magic1C = (uint)((buf[0x1C] << 24) | (buf[0x1D] << 16) | (buf[0x1E] << 8) | buf[0x1F]);
                uint magic58 = (uint)((buf[0x58] << 24) | (buf[0x59] << 16) | (buf[0x5A] << 8) | buf[0x5B]);
                if (magic1C == 0xC2339F3D || magic58 == 0xC2339F3D)
                {
                    return new RomSignatureResult("GameCube", "dolphin_libretro.dll", "GameCube RVZ/WBFS");
                }

                // Check Wii magic 0x5D1C9EA3
                uint magic18 = (uint)((buf[0x18] << 24) | (buf[0x19] << 16) | (buf[0x1A] << 8) | buf[0x1B]);
                uint magic54 = (uint)((buf[0x54] << 24) | (buf[0x55] << 16) | (buf[0x5A] << 8) | buf[0x57]);
                if (magic18 == 0x5D1C9EA3 || magic54 == 0x5D1C9EA3)
                {
                    return new RomSignatureResult("Wii", "dolphin_libretro.dll", "Wii RVZ/WBFS");
                }
            }
        }
        catch { }

        return new RomSignatureResult("Nintendo GameCube / Wii", "dolphin_libretro.dll", "GameCube/Wii Image");
    }

    private static RomSignatureResult DetectCsoZso(string filePath, string parentDir)
    {
        string combined = (filePath + " " + parentDir).ToLowerInvariant();
        if (combined.Contains("ps2") || combined.Contains("playstation 2"))
        {
            return new RomSignatureResult("PlayStation 2", "pcsx2_libretro.dll", "PS2 Compressed ISO");
        }
        return new RomSignatureResult("PSP", "ppsspp_libretro.dll", "PSP Compressed ISO");
    }

    private static RomSignatureResult DetectChd(string filePath, string parentDir)
    {
        string combined = (filePath + " " + parentDir).ToLowerInvariant();

        if (combined.Contains("dreamcast") || combined.Contains("flycast") || combined.Contains("naomi"))
            return new RomSignatureResult("Dreamcast", "flycast_libretro.dll", "Dreamcast CHD");

        if (combined.Contains("saturn") || combined.Contains("yabause"))
            return new RomSignatureResult("Sega Saturn", "beetle_saturn_libretro.dll", "Sega Saturn CHD");

        if (combined.Contains("ps2") || combined.Contains("playstation 2") || combined.Contains("lrps2") || combined.Contains("pcsx2"))
            return new RomSignatureResult("PlayStation 2", "pcsx2_libretro.dll", "PS2 CHD");

        if (combined.Contains("psp"))
            return new RomSignatureResult("PSP", "ppsspp_libretro.dll", "PSP CHD");

        if (combined.Contains("sega cd") || combined.Contains("mega cd") || combined.Contains("megacd"))
            return new RomSignatureResult("Sega CD", "genesis_plus_gx_libretro.dll", "Sega CD CHD");

        if (combined.Contains("pce") || combined.Contains("pc engine") || combined.Contains("turbografx"))
            return new RomSignatureResult("PC Engine / TurboGrafx-16", "mednafen_pce_fast_libretro.dll", "PCE CD CHD");

        if (combined.Contains("3do"))
            return new RomSignatureResult("3DO", "opera_libretro.dll", "3DO CHD");

        if (combined.Contains("arcade") || combined.Contains("mame"))
            return new RomSignatureResult("Arcade", "mame_libretro.dll", "Arcade CHD");

        // Inspect CHD header metadata if available
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] header = new byte[256];
            int read = fs.Read(header, 0, header.Length);
            if (read >= 16 && StartsWith(header, ChdMagic))
            {
                // CHD v3/v4/v5 - search for metadata tag
                fs.Seek(0, SeekOrigin.Begin);
                byte[] metaSlice = new byte[4096];
                int metaRead = fs.Read(metaSlice, 0, metaSlice.Length);
                string metaText = Encoding.ASCII.GetString(metaSlice, 0, metaRead);

                if (metaText.Contains("CHGD"))
                    return new RomSignatureResult("Dreamcast", "flycast_libretro.dll", "Dreamcast GD-ROM CHD");
                if (metaText.Contains("DVD ") || metaText.Contains("CDROM"))
                {
                    if (combined.Contains("ps2"))
                        return new RomSignatureResult("PlayStation 2", "pcsx2_libretro.dll", "PS2 DVD CHD");
                }
            }
        }
        catch { }

        // Default to PS1 for CHD if unknown
        return new RomSignatureResult("Sony PlayStation 1", "swanstation_libretro.dll", "PlayStation CHD");
    }

    private static RomSignatureResult? DetectOpticalDiscImage(string filePath, string parentDir)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long length = fs.Length;
        if (length < 0x8000) return null;

        byte[] headerSlice = new byte[512];
        fs.Seek(0, SeekOrigin.Begin);
        int readHead = fs.Read(headerSlice, 0, headerSlice.Length);

        // 1. Sega Dreamcast header check (SEGA SEGAKATANA)
        if (ContainsSequence(headerSlice, SegaKatana))
        {
            return new RomSignatureResult("Dreamcast", "flycast_libretro.dll", "Dreamcast GD/CDI");
        }

        // 2. Sega Saturn header check (SEGA SEGASATURN)
        if (ContainsSequence(headerSlice, SegaSaturn))
        {
            return new RomSignatureResult("Sega Saturn", "beetle_saturn_libretro.dll", "Sega Saturn CD");
        }

        // 3. Sega CD / Mega-CD header check (SEGADISCSYSTEM / SEGA DISCSYSTEM)
        if (ContainsSequence(headerSlice, SegaDiscSystem1) || ContainsSequence(headerSlice, SegaDiscSystem2))
        {
            return new RomSignatureResult("Sega CD", "genesis_plus_gx_libretro.dll", "Sega Mega-CD");
        }

        // 4. GameCube Disc Magic at 0x1C (0xC2339F3D)
        if (readHead >= 0x20)
        {
            uint gcMagic = (uint)((headerSlice[0x1C] << 24) | (headerSlice[0x1D] << 16) | (headerSlice[0x1E] << 8) | headerSlice[0x1F]);
            if (gcMagic == 0xC2339F3D)
                return new RomSignatureResult("GameCube", "dolphin_libretro.dll", "GameCube Disc");

            uint wiiMagic = (uint)((headerSlice[0x18] << 24) | (headerSlice[0x19] << 16) | (headerSlice[0x1A] << 8) | headerSlice[0x1B]);
            if (wiiMagic == 0x5D1C9EA3)
                return new RomSignatureResult("Wii", "dolphin_libretro.dll", "Wii Disc");
        }

        // 5. Original Xbox XISO check: "MICROSOFT*XBOX*MEDIA" at offset 0x10000 or 0x10000 + 0x800
        if (length >= 0x11000)
        {
            byte[] xboxBuf = new byte[128];
            fs.Seek(0x10000, SeekOrigin.Begin);
            int xboxRead = fs.Read(xboxBuf, 0, xboxBuf.Length);
            if (xboxRead >= 20 && StartsWith(xboxBuf, XboxMediaMagic))
            {
                return new RomSignatureResult("Xbox", "xemu_libretro.dll", "Xbox XISO");
            }
        }

        // 6. 3DS NCSD header at 0x100
        if (length >= 0x200)
        {
            byte[] ncsdBuf = new byte[16];
            fs.Seek(0x100, SeekOrigin.Begin);
            int ncsdRead = fs.Read(ncsdBuf, 0, ncsdBuf.Length);
            if (ncsdRead >= 4 && (StartsWith(ncsdBuf, NcsdMagic) || StartsWith(ncsdBuf, NcchMagic)))
            {
                return new RomSignatureResult("Nintendo 3DS", "azahar_libretro.dll", "3DS CTR/NCSD");
            }
        }

        // 7. Scan first 4 MB (in 64 KB chunks) for PS2 (BOOT2), PSP (PSP_GAME), PS3 (PS3_GAME), PS1 (PLAYSTATION)
        long scanLimit = Math.Min(length, 4 * 1024 * 1024);
        byte[] chunk = new byte[65536];
        long pos = 0;
        bool foundPs1Marker = false;

        fs.Seek(0, SeekOrigin.Begin);
        while (pos < scanLimit)
        {
            int toRead = (int)Math.Min(chunk.Length, scanLimit - pos);
            int bytesRead = fs.Read(chunk, 0, toRead);
            if (bytesRead <= 0) break;

            // Check PS3 Game marker
            if (ContainsSequence(chunk, Ps3GameMarker) || ContainsSequence(chunk, Ps3SfbMarker))
            {
                return new RomSignatureResult("PlayStation 3", "rpcs3_libretro.dll", "PS3 Disc/PKG");
            }

            // Check PS2 BOOT2 marker
            if (ContainsSequence(chunk, Ps2Boot2Marker1) || ContainsSequence(chunk, Ps2Boot2Marker2))
            {
                return new RomSignatureResult("PlayStation 2", "pcsx2_libretro.dll", "PS2 ISO/DVD");
            }

            // Check PSP marker
            if (ContainsSequence(chunk, PspGameMarker))
            {
                return new RomSignatureResult("PSP", "ppsspp_libretro.dll", "PSP UMD ISO");
            }

            // Check PS1 markers
            if (ContainsSequence(chunk, Ps1BootMarker1) || ContainsSequence(chunk, Ps1BootMarker2) || ContainsSequence(chunk, Ps1PlaystationMarker))
            {
                foundPs1Marker = true;
            }

            pos += bytesRead;
        }

        if (foundPs1Marker)
        {
            return new RomSignatureResult("Sony PlayStation 1", "swanstation_libretro.dll", "PS1 CD-ROM");
        }

        // Check parent folder name hints for ambiguous ISOs
        string combined = (filePath + " " + parentDir).ToLowerInvariant();
        if (combined.Contains("ps2") || combined.Contains("playstation 2"))
            return new RomSignatureResult("PlayStation 2", "pcsx2_libretro.dll", "PS2 Disc");
        if (combined.Contains("psp"))
            return new RomSignatureResult("PSP", "ppsspp_libretro.dll", "PSP Disc");
        if (combined.Contains("gamecube") || combined.Contains("gc"))
            return new RomSignatureResult("GameCube", "dolphin_libretro.dll", "GameCube Disc");
        if (combined.Contains("wii"))
            return new RomSignatureResult("Wii", "dolphin_libretro.dll", "Wii Disc");

        return null;
    }

    private static RomSignatureResult? DetectCueSheet(string cuePath, string parentDir)
    {
        try
        {
            string cueDir = Path.GetDirectoryName(cuePath) ?? "";
            string[] lines = File.ReadAllLines(cuePath);
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("FILE", StringComparison.OrdinalIgnoreCase))
                {
                    int firstQuote = trimmed.IndexOf('"');
                    int lastQuote = trimmed.LastIndexOf('"');
                    if (firstQuote >= 0 && lastQuote > firstQuote)
                    {
                        string binFileName = trimmed.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
                        string binFullPath = Path.Combine(cueDir, binFileName);
                        if (File.Exists(binFullPath))
                        {
                            var binResult = Detect(binFullPath);
                            if (binResult != null) return binResult;
                        }
                    }
                }
            }
        }
        catch { }

        string combined = (cuePath + " " + parentDir).ToLowerInvariant();
        if (combined.Contains("saturn"))
            return new RomSignatureResult("Sega Saturn", "beetle_saturn_libretro.dll", "Sega Saturn CUE");
        if (combined.Contains("sega cd") || combined.Contains("mega-cd") || combined.Contains("megacd"))
            return new RomSignatureResult("Sega CD", "genesis_plus_gx_libretro.dll", "Sega CD CUE");
        if (combined.Contains("pce") || combined.Contains("pc engine") || combined.Contains("turbografx"))
            return new RomSignatureResult("PC Engine / TurboGrafx-16", "mednafen_pce_fast_libretro.dll", "PCE CD CUE");

        return new RomSignatureResult("Sony PlayStation 1", "swanstation_libretro.dll", "PS1 CUE");
    }

    private static RomSignatureResult DetectZipContainer(string zipPath)
    {
        string name = Path.GetFileName(zipPath).ToLowerInvariant();
        string pathLower = zipPath.ToLowerInvariant();

        // 1. MS-DOS PC indicators in filename or path
        if (pathLower.Contains("ms-dos") || pathLower.Contains("(dos-") || pathLower.Contains(" dos-") || pathLower.Contains("dosbox"))
        {
            return new RomSignatureResult("MS-DOS PC", "dosbox_pure_libretro.dll", "MS-DOS PC Archive");
        }

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);

            // Check if archive contains MS-DOS executables
            foreach (var entry in archive.Entries)
            {
                string entryExt = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (entryExt is ".exe" or ".com" or ".bat" or ".dosz")
                {
                    return new RomSignatureResult("MS-DOS PC", "dosbox_pure_libretro.dll", "MS-DOS PC Archive");
                }
            }

            // Check if archive contains a standard console ROM
            foreach (var entry in archive.Entries)
            {
                string entryExt = Path.GetExtension(entry.Name).ToLowerInvariant();
                switch (entryExt)
                {
                    case ".nes" or ".fds":
                        return new RomSignatureResult("Nintendo Entertainment System", "fceumm_libretro.dll", "NES ROM");
                    case ".sfc" or ".smc":
                        return new RomSignatureResult("Super Nintendo (SNES)", "snes9x_libretro.dll", "SNES ROM");
                    case ".md" or ".gen" or ".smd":
                        return new RomSignatureResult("Sega Genesis / Mega Drive", "genesis_plus_gx_libretro.dll", "Genesis ROM");
                    case ".gba":
                        return new RomSignatureResult("Game Boy Advance", "mgba_libretro.dll", "GBA ROM");
                    case ".gb" or ".gbc":
                        return new RomSignatureResult("Game Boy / Color", "gambatte_libretro.dll", "Game Boy ROM");
                    case ".n64" or ".z64" or ".v64":
                        return new RomSignatureResult("Nintendo 64", "mupen64plus_next_libretro.dll", "N64 ROM");
                    case ".nds":
                        return new RomSignatureResult("Nintendo DS / DSi", "melondsds_libretro.dll", "NDS ROM");
                    case ".pce" or ".sgx":
                        return new RomSignatureResult("PC Engine / TurboGrafx-16", "mednafen_pce_fast_libretro.dll", "PCE ROM");
                    case ".sms" or ".gg":
                        return new RomSignatureResult("Sega Master System / Game Gear", "genesis_plus_gx_libretro.dll", "Sega 8-Bit ROM");
                    case ".a26":
                        return new RomSignatureResult("Atari 2600", "stella_libretro.dll", "Atari 2600 ROM");
                }
            }

            // If it contains chip ROMs (.bin, .rom, .ic*, .u*, .p1, .c1, .s1, etc.) -> Arcade MAME / FBNeo
            string preferredArcadeCore = CoreManager.IsCoreInstalled("mame_libretro.dll") ? "mame_libretro.dll" : "fbneo_libretro.dll";
            return new RomSignatureResult("Arcade", preferredArcadeCore, "MAME/FBNeo ROMset");
        }
        catch
        {
            // Default to Arcade FBNeo on unreadable or encrypted zip
            return new RomSignatureResult("Arcade", "fbneo_libretro.dll", "Arcade ROMset");
        }
    }

    private static bool StartsWith(byte[] data, byte[] prefix)
    {
        if (data.Length < prefix.Length) return false;
        for (int i = 0; i < prefix.Length; i++)
        {
            if (data[i] != prefix[i]) return false;
        }
        return true;
    }

    private static bool ContainsSequence(byte[] data, byte[] seq)
    {
        if (data.Length < seq.Length) return false;
        int max = data.Length - seq.Length;
        for (int i = 0; i <= max; i++)
        {
            bool match = true;
            for (int j = 0; j < seq.Length; j++)
            {
                if (data[i + j] != seq[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return true;
        }
        return false;
    }
}
