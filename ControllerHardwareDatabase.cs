using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace StormRetroArch;

public sealed record HardwareDeviceSignature(
    ushort VendorId,
    ushort ProductId,
    string DisplayName,
    string Manufacturer,
    InputDeviceFormFactor FormFactor,
    bool IsKnownXInput);

public sealed record UsbRegistryDeviceDetails(
    string? OemName,
    string? BusReportedName,
    bool HasXusbService);

public sealed record AutoconfigMappingResult(
    string SourceLabel,
    Dictionary<int, PhysicalInputBinding> JoypadBindings,
    Dictionary<int, PhysicalInputBinding> AnalogBindings);

/// <summary>
/// База аппаратных сигнатур VID:PID (геймпады, рули и педали, авиаджойстики HOTAS,
/// аркадные стики, HitBox, Flydigi APEX 5 EVA), сканер реестра/HID и парсер
/// официальных профилей RetroArch autoconfig + SDL gamecontrollerdb.cfg.
/// </summary>
public static class ControllerHardwareDatabase
{
    private static readonly Dictionary<(ushort Vid, ushort Pid), HardwareDeviceSignature> KnownVidPidMap = new()
    {
        // ==================== FLYDIGI (0x37D7 & XInput / Switch modes) ====================
        [(0x37D7, 0x2501)] = new(0x37D7, 0x2501, "Flydigi APEX 5 / APEX 5 EVA Wireless", "Flydigi", InputDeviceFormFactor.XboxGamepad, true),
        [(0x37D7, 0x2502)] = new(0x37D7, 0x2502, "Flydigi APEX 5 / APEX 5 EVA (Wired)", "Flydigi", InputDeviceFormFactor.XboxGamepad, true),
        [(0x37D7, 0x2401)] = new(0x37D7, 0x2401, "Flydigi APEX 4 Wireless", "Flydigi", InputDeviceFormFactor.XboxGamepad, true),
        [(0x37D7, 0x2402)] = new(0x37D7, 0x2402, "Flydigi Vader 4 Pro Wireless", "Flydigi", InputDeviceFormFactor.XboxGamepad, true),
        [(0x37D7, 0x2301)] = new(0x37D7, 0x2301, "Flydigi Vader 3 Pro", "Flydigi", InputDeviceFormFactor.XboxGamepad, true),
        [(0x37D7, 0x2302)] = new(0x37D7, 0x2302, "Flydigi APEX 3 Wireless", "Flydigi", InputDeviceFormFactor.XboxGamepad, true),
        [(0x37D7, 0x2201)] = new(0x37D7, 0x2201, "Flydigi Direwolf 2 / 3", "Flydigi", InputDeviceFormFactor.XboxGamepad, true),
        [(0x37D7, 0x2101)] = new(0x37D7, 0x2101, "Flydigi Vader 2 Pro", "Flydigi", InputDeviceFormFactor.XboxGamepad, true),
        [(0x37D7, 0x6001)] = new(0x37D7, 0x6001, "Flydigi Wireless Dongle / Charging Dock", "Flydigi", InputDeviceFormFactor.XboxGamepad, false),

        // ==================== MICROSOFT XBOX ====================
        [(0x045E, 0x028E)] = new(0x045E, 0x028E, "Xbox 360 Controller for Windows", "Microsoft", InputDeviceFormFactor.XboxGamepad, true),
        [(0x045E, 0x02A1)] = new(0x045E, 0x02A1, "Xbox 360 Wireless Receiver", "Microsoft", InputDeviceFormFactor.XboxGamepad, true),
        [(0x045E, 0x02D1)] = new(0x045E, 0x02D1, "Xbox One Controller", "Microsoft", InputDeviceFormFactor.XboxGamepad, true),
        [(0x045E, 0x02DD)] = new(0x045E, 0x02DD, "Xbox One Controller (Firmware 2015)", "Microsoft", InputDeviceFormFactor.XboxGamepad, true),
        [(0x045E, 0x02E0)] = new(0x045E, 0x02E0, "Xbox One S Controller (Bluetooth)", "Microsoft", InputDeviceFormFactor.XboxGamepad, true),
        [(0x045E, 0x02EA)] = new(0x045E, 0x02EA, "Xbox One S Controller (USB)", "Microsoft", InputDeviceFormFactor.XboxGamepad, true),
        [(0x045E, 0x02FD)] = new(0x045E, 0x02FD, "Xbox Wireless Controller", "Microsoft", InputDeviceFormFactor.XboxGamepad, true),
        [(0x045E, 0x0B00)] = new(0x045E, 0x0B00, "Xbox Elite Wireless Controller Series 2", "Microsoft", InputDeviceFormFactor.XboxGamepad, true),
        [(0x045E, 0x0B05)] = new(0x045E, 0x0B05, "Xbox Elite Wireless Controller Series 2 (BT)", "Microsoft", InputDeviceFormFactor.XboxGamepad, true),
        [(0x045E, 0x0B12)] = new(0x045E, 0x0B12, "Xbox Series X|S Wireless Controller (USB)", "Microsoft", InputDeviceFormFactor.XboxGamepad, true),
        [(0x045E, 0x0B13)] = new(0x045E, 0x0B13, "Xbox Series X|S Wireless Controller (BLE)", "Microsoft", InputDeviceFormFactor.XboxGamepad, true),

        // ==================== SONY PLAYSTATION ====================
        [(0x054C, 0x0268)] = new(0x054C, 0x0268, "Sony DualShock 3 (SIXAXIS)", "Sony", InputDeviceFormFactor.PlayStationPad, false),
        [(0x054C, 0x05C4)] = new(0x054C, 0x05C4, "Sony DualShock 4 (CUH-ZCT1)", "Sony", InputDeviceFormFactor.PlayStationPad, false),
        [(0x054C, 0x09CC)] = new(0x054C, 0x09CC, "Sony DualShock 4 v2 (CUH-ZCT2)", "Sony", InputDeviceFormFactor.PlayStationPad, false),
        [(0x054C, 0x0BA0)] = new(0x054C, 0x0BA0, "Sony DualShock 4 USB Wireless Adaptor", "Sony", InputDeviceFormFactor.PlayStationPad, false),
        [(0x054C, 0x0CE6)] = new(0x054C, 0x0CE6, "Sony DualSense Wireless Controller (PS5)", "Sony", InputDeviceFormFactor.PlayStationPad, false),
        [(0x054C, 0x0DF2)] = new(0x054C, 0x0DF2, "Sony DualSense Edge Wireless Controller", "Sony", InputDeviceFormFactor.PlayStationPad, false),

        // ==================== NINTENDO ====================
        [(0x057E, 0x2009)] = new(0x057E, 0x2009, "Nintendo Switch Pro Controller", "Nintendo", InputDeviceFormFactor.NintendoPad, false),
        [(0x057E, 0x2006)] = new(0x057E, 0x2006, "Nintendo Switch Joy-Con (L)", "Nintendo", InputDeviceFormFactor.NintendoPad, false),
        [(0x057E, 0x2007)] = new(0x057E, 0x2007, "Nintendo Switch Joy-Con (R)", "Nintendo", InputDeviceFormFactor.NintendoPad, false),
        [(0x057E, 0x2017)] = new(0x057E, 0x2017, "Nintendo SNES NSO Controller", "Nintendo", InputDeviceFormFactor.SnesPad, false),
        [(0x057E, 0x2019)] = new(0x057E, 0x2019, "Nintendo 64 NSO Controller", "Nintendo", InputDeviceFormFactor.N64Pad, false),
        [(0x057E, 0x0337)] = new(0x057E, 0x0337, "Nintendo GameCube Controller Adapter", "Nintendo", InputDeviceFormFactor.NintendoPad, false),

        // ==================== 8BITDO ====================
        [(0x2DC8, 0x3106)] = new(0x2DC8, 0x3106, "8BitDo Ultimate Wireless Controller", "8BitDo", InputDeviceFormFactor.XboxGamepad, true),
        [(0x2DC8, 0x3109)] = new(0x2DC8, 0x3109, "8BitDo Ultimate 2C Wireless", "8BitDo", InputDeviceFormFactor.XboxGamepad, true),
        [(0x2DC8, 0x310B)] = new(0x2DC8, 0x310B, "8BitDo Ultimate 2 Wireless Controller", "8BitDo", InputDeviceFormFactor.XboxGamepad, true),
        [(0x2DC8, 0x3207)] = new(0x2DC8, 0x3207, "8BitDo Wireless Receiver / Mode Adapter", "8BitDo", InputDeviceFormFactor.XboxGamepad, false),
        [(0x2DC8, 0x6003)] = new(0x2DC8, 0x6003, "8BitDo Pro 2 Bluetooth / USB", "8BitDo", InputDeviceFormFactor.PlayStationPad, false),
        [(0x2DC8, 0x3019)] = new(0x2DC8, 0x3019, "8BitDo Pro 3 Controller", "8BitDo", InputDeviceFormFactor.NintendoPad, false),
        [(0x2DC8, 0x2840)] = new(0x2DC8, 0x2840, "8BitDo M30 6-Button Fighting Pad", "8BitDo", InputDeviceFormFactor.SegaSixButton, false),
        [(0x2DC8, 0x5006)] = new(0x2DC8, 0x5006, "8BitDo M30 Bluetooth (6-Button)", "8BitDo", InputDeviceFormFactor.SegaSixButton, false),
        [(0x2DC8, 0x9015)] = new(0x2DC8, 0x9015, "8BitDo Arcade Stick", "8BitDo", InputDeviceFormFactor.ArcadeFightStick, false),

        // ==================== LOGITECH GAMEPADS & RACING WHEELS & FLIGHT STICKS ====================
        [(0x046D, 0xC21D)] = new(0x046D, 0xC21D, "Logitech Gamepad F310 (XInput)", "Logitech", InputDeviceFormFactor.XboxGamepad, true),
        [(0x046D, 0xC21E)] = new(0x046D, 0xC21E, "Logitech Gamepad F510 (XInput)", "Logitech", InputDeviceFormFactor.XboxGamepad, true),
        [(0x046D, 0xC21F)] = new(0x046D, 0xC21F, "Logitech Wireless Gamepad F710", "Logitech", InputDeviceFormFactor.XboxGamepad, true),
        [(0x046D, 0xC216)] = new(0x046D, 0xC216, "Logitech Dual Action / F310 (DInput)", "Logitech", InputDeviceFormFactor.PlayStationPad, false),
        [(0x046D, 0xC218)] = new(0x046D, 0xC218, "Logitech RumblePad 2 USB", "Logitech", InputDeviceFormFactor.PlayStationPad, false),
        [(0x046D, 0xC215)] = new(0x046D, 0xC215, "Logitech Extreme 3D Pro Flight Joystick", "Logitech", InputDeviceFormFactor.FlightJoystickHotas, false),
        [(0x046D, 0xC24F)] = new(0x046D, 0xC24F, "Logitech G29 Driving Force Racing Wheel", "Logitech", InputDeviceFormFactor.RacingWheel, false),
        [(0x046D, 0xC260)] = new(0x046D, 0xC260, "Logitech G29 Driving Force (PS3 mode)", "Logitech", InputDeviceFormFactor.RacingWheel, false),
        [(0x046D, 0xC261)] = new(0x046D, 0xC261, "Logitech G920 Driving Force (Xbox)", "Logitech", InputDeviceFormFactor.RacingWheel, false),
        [(0x046D, 0xC262)] = new(0x046D, 0xC262, "Logitech G920 Driving Force Racing Wheel", "Logitech", InputDeviceFormFactor.RacingWheel, false),
        [(0x046D, 0xC266)] = new(0x046D, 0xC266, "Logitech G923 Racing Wheel (PS/PC)", "Logitech", InputDeviceFormFactor.RacingWheel, false),
        [(0x046D, 0xC26E)] = new(0x046D, 0xC26E, "Logitech G923 Racing Wheel (Xbox/PC)", "Logitech", InputDeviceFormFactor.RacingWheel, false),
        [(0x046D, 0xC268)] = new(0x046D, 0xC268, "Logitech PRO Racing Wheel", "Logitech", InputDeviceFormFactor.RacingWheel, false),
        [(0x046D, 0xC299)] = new(0x046D, 0xC299, "Logitech G25 Racing Wheel", "Logitech", InputDeviceFormFactor.RacingWheel, false),
        [(0x046D, 0xC29A)] = new(0x046D, 0xC29A, "Logitech Driving Force GT", "Logitech", InputDeviceFormFactor.RacingWheel, false),
        [(0x046D, 0xC29B)] = new(0x046D, 0xC29B, "Logitech G27 Racing Wheel", "Logitech", InputDeviceFormFactor.RacingWheel, false),
        [(0x046D, 0xC295)] = new(0x046D, 0xC295, "Logitech MOMO Racing Force Wheel", "Logitech", InputDeviceFormFactor.RacingWheel, false),

        // ==================== THRUSTMASTER WHEELS & HOTAS ====================
        [(0x044F, 0xB66E)] = new(0x044F, 0xB66E, "Thrustmaster T300RS Racing Wheel", "Thrustmaster", InputDeviceFormFactor.RacingWheel, false),
        [(0x044F, 0xB677)] = new(0x044F, 0xB677, "Thrustmaster T150 Racing Wheel", "Thrustmaster", InputDeviceFormFactor.RacingWheel, false),
        [(0x044F, 0xB67F)] = new(0x044F, 0xB67F, "Thrustmaster TMX Racing Wheel", "Thrustmaster", InputDeviceFormFactor.RacingWheel, false),
        [(0x044F, 0xB696)] = new(0x044F, 0xB696, "Thrustmaster T248 Racing Wheel", "Thrustmaster", InputDeviceFormFactor.RacingWheel, false),
        [(0x044F, 0xB689)] = new(0x044F, 0xB689, "Thrustmaster TS-PC Racer", "Thrustmaster", InputDeviceFormFactor.RacingWheel, false),
        [(0x044F, 0xB10A)] = new(0x044F, 0xB10A, "Thrustmaster T.16000M FCS HOTAS", "Thrustmaster", InputDeviceFormFactor.FlightJoystickHotas, false),
        [(0x044F, 0xB687)] = new(0x044F, 0xB687, "Thrustmaster TWCS Throttle", "Thrustmaster", InputDeviceFormFactor.FlightJoystickHotas, false),
        [(0x044F, 0x0402)] = new(0x044F, 0x0402, "Thrustmaster HOTAS Warthog Joystick", "Thrustmaster", InputDeviceFormFactor.FlightJoystickHotas, false),
        [(0x044F, 0x0404)] = new(0x044F, 0x0404, "Thrustmaster HOTAS Warthog Throttle", "Thrustmaster", InputDeviceFormFactor.FlightJoystickHotas, false),
        [(0x044F, 0xB108)] = new(0x044F, 0xB108, "Thrustmaster T.Flight Hotas X", "Thrustmaster", InputDeviceFormFactor.FlightJoystickHotas, false),
        [(0x044F, 0xB67B)] = new(0x044F, 0xB67B, "Thrustmaster T.Flight Hotas 4", "Thrustmaster", InputDeviceFormFactor.FlightJoystickHotas, false),

        // ==================== FANATEC / MOZA / SIMAGIC / CAMMUS / PXN WHEELS ====================
        [(0x0EB7, 0x0001)] = new(0x0EB7, 0x0001, "Fanatec ClubSport Wheel Base", "Fanatec", InputDeviceFormFactor.RacingWheel, false),
        [(0x0EB7, 0x0004)] = new(0x0EB7, 0x0004, "Fanatec ClubSport Wheel Base V2.5", "Fanatec", InputDeviceFormFactor.RacingWheel, false),
        [(0x0EB7, 0x0006)] = new(0x0EB7, 0x0006, "Fanatec Podium Wheel Base DD1/DD2", "Fanatec", InputDeviceFormFactor.RacingWheel, false),
        [(0x0EB7, 0x0020)] = new(0x0EB7, 0x0020, "Fanatec CSL DD / Gran Turismo DD Pro", "Fanatec", InputDeviceFormFactor.RacingWheel, false),
        [(0x346E, 0x0002)] = new(0x346E, 0x0002, "MOZA Racing R9 / R12 Direct Drive Base", "MOZA", InputDeviceFormFactor.RacingWheel, false),
        [(0x346E, 0x0004)] = new(0x346E, 0x0004, "MOZA Racing R5 Direct Drive Base", "MOZA", InputDeviceFormFactor.RacingWheel, false),
        [(0x346E, 0x0005)] = new(0x346E, 0x0005, "MOZA Racing R3 / SRP Wheel Base", "MOZA", InputDeviceFormFactor.RacingWheel, false),
        [(0x3670, 0x0500)] = new(0x3670, 0x0500, "Simagic Alpha / M10 Wheel Base", "Simagic", InputDeviceFormFactor.RacingWheel, false),
        [(0x3416, 0x0301)] = new(0x3416, 0x0301, "Cammus C5 / DDWB Racing Wheel", "Cammus", InputDeviceFormFactor.RacingWheel, false),

        // ==================== FLIGHT STICKS (VKB / VIRPIL / WINWING / SAITEK) ====================
        [(0x231D, 0x0125)] = new(0x231D, 0x0125, "VKB-Sim Gladiator NXT EVO", "VKB", InputDeviceFormFactor.FlightJoystickHotas, false),
        [(0x231D, 0x0200)] = new(0x231D, 0x0200, "VKB-Sim Gladiator NXT", "VKB", InputDeviceFormFactor.FlightJoystickHotas, false),
        [(0x06A3, 0x0762)] = new(0x06A3, 0x0762, "Saitek / Logitech X52 Pro Flight Control", "Saitek", InputDeviceFormFactor.FlightJoystickHotas, false),
        [(0x0738, 0x2221)] = new(0x0738, 0x2221, "Saitek / Logitech X56 Rhino Stick", "Logitech", InputDeviceFormFactor.FlightJoystickHotas, false),
        [(0x0738, 0xA221)] = new(0x0738, 0xA221, "Saitek / Logitech X56 Rhino Throttle", "Logitech", InputDeviceFormFactor.FlightJoystickHotas, false),

        // ==================== ARCADE FIGHTSTICKS & LEVERLESS ====================
        [(0x0F0D, 0x017C)] = new(0x0F0D, 0x017C, "HORI Fighting Stick Alpha", "HORI", InputDeviceFormFactor.ArcadeFightStick, false),
        [(0x0F0D, 0x0184)] = new(0x0F0D, 0x0184, "HORI Fighting Stick Alpha (Xbox)", "HORI", InputDeviceFormFactor.ArcadeFightStick, true),
        [(0x0F0D, 0x0092)] = new(0x0F0D, 0x0092, "HORI Pokken / Switch Pad", "HORI", InputDeviceFormFactor.NintendoPad, false),
        [(0x0F0D, 0x00C1)] = new(0x0F0D, 0x00C1, "HORIPAD Nintendo Switch", "HORI", InputDeviceFormFactor.NintendoPad, false),
        [(0x0F0D, 0x0066)] = new(0x0F0D, 0x0066, "HORI Real Arcade Pro.4 Kai", "HORI", InputDeviceFormFactor.ArcadeFightStick, false),
        [(0x2C22, 0x2003)] = new(0x2C22, 0x2003, "Qanba Drone 2 Arcade Joystick", "Qanba", InputDeviceFormFactor.ArcadeFightStick, false),
        [(0x2C22, 0x2302)] = new(0x2C22, 0x2302, "Qanba Obsidian Arcade Joystick", "Qanba", InputDeviceFormFactor.ArcadeFightStick, false),
        [(0x1532, 0x1008)] = new(0x1532, 0x1008, "Razer Kitsune All-Button Optical Arcade", "Razer", InputDeviceFormFactor.LeverlessHitBox, false),
        [(0x1532, 0x0401)] = new(0x1532, 0x0401, "Razer Panthera Arcade Stick", "Razer", InputDeviceFormFactor.ArcadeFightStick, false),
        [(0x1532, 0x0A14)] = new(0x1532, 0x0A14, "Razer Wolverine V2", "Razer", InputDeviceFormFactor.XboxGamepad, true),
        [(0x2F24, 0x00B7)] = new(0x2F24, 0x00B7, "GuliKit KingKong 2 / 3 Pro", "GuliKit", InputDeviceFormFactor.XboxGamepad, true),
        [(0x3537, 0x1010)] = new(0x3537, 0x1010, "GameSir G7 SE / Kaleid / Cyclone 2", "GameSir", InputDeviceFormFactor.XboxGamepad, true),
        [(0x28DE, 0x1142)] = new(0x28DE, 0x1142, "Valve Steam Controller / Steam Deck", "Valve", InputDeviceFormFactor.XboxGamepad, false),

        // ==================== ASUS ROG CONTROLLERS ONLY (avoid Aura motherboard HID false positives) ====================
        [(0x0B05, 0x1A38)] = new(0x0B05, 0x1A38, "ASUS ROG Raikiri Pro", "ASUS", InputDeviceFormFactor.XboxGamepad, true),
        [(0x0B05, 0x1ABD)] = new(0x0B05, 0x1ABD, "ASUS ROG Raikiri", "ASUS", InputDeviceFormFactor.XboxGamepad, true),
        [(0x0B05, 0x195B)] = new(0x0B05, 0x195B, "ASUS ROG Kunai 3 Gamepad", "ASUS", InputDeviceFormFactor.XboxGamepad, false),
    };

    private static readonly ConcurrentDictionary<string, UsbRegistryDeviceDetails> _usbDetailsCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _autoconfigLock = new();
    private static bool _autoconfigLoaded;
    private static readonly Dictionary<(ushort Vid, ushort Pid), List<ParsedCfgEntry>> _cfgByVidPid = new();
    private static readonly Dictionary<string, ParsedCfgEntry> _cfgByName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(ushort Vid, ushort Pid), ParsedSdlEntry> _sdlByVidPid = new();

    private sealed class ParsedCfgEntry
    {
        public string SourceName = "";
        public string Driver = "";
        public ushort Vid;
        public ushort Pid;
        public string DisplayName = "";
        public readonly Dictionary<string, string> KV = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ParsedSdlEntry
    {
        public string Name = "";
        public ushort Vid;
        public ushort Pid;
        public readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase);
    }

    public static HardwareDeviceSignature? LookupKnownSignature(ushort vid, ushort pid)
    {
        if (vid == 0 && pid == 0) return null;
        return KnownVidPidMap.TryGetValue((vid, pid), out var sig) ? sig : null;
    }

    public static UsbRegistryDeviceDetails QueryRegistryDeviceDetails(ushort vid, ushort pid)
    {
        string cacheKey = $"{vid:X4}_{pid:X4}";
        if (_usbDetailsCache.TryGetValue(cacheKey, out var cached)) return cached;

        string? oemName = null;
        string? busReported = null;
        bool hasXusb = false;

        if (vid != 0 || pid != 0)
        {
            string subKey = $@"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\VID_{vid:X4}&PID_{pid:X4}";
            try
            {
                using var hkcu = Registry.CurrentUser.OpenSubKey(subKey);
                if (hkcu?.GetValue("OEMName") is string s1 && !string.IsNullOrWhiteSpace(s1)) oemName = s1.Trim();
            }
            catch { }

            if (string.IsNullOrWhiteSpace(oemName))
            {
                try
                {
                    using var hklm = Registry.LocalMachine.OpenSubKey(subKey);
                    if (hklm?.GetValue("OEMName") is string s2 && !string.IsNullOrWhiteSpace(s2)) oemName = s2.Trim();
                }
                catch { }
            }

            try
            {
                string prefix = $"VID_{vid:X4}&PID_{pid:X4}";
                using var usbRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
                if (usbRoot != null)
                {
                    foreach (var keyName in usbRoot.GetSubKeyNames())
                    {
                        if (!keyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                        using var devKey = usbRoot.OpenSubKey(keyName);
                        if (devKey == null) continue;
                        foreach (var inst in devKey.GetSubKeyNames())
                        {
                            using var ik = devKey.OpenSubKey(inst);
                            if (ik == null) continue;
                            string svc = ik.GetValue("Service") as string ?? "";
                            if (svc.Contains("xusb", StringComparison.OrdinalIgnoreCase)) hasXusb = true;
                            if (string.IsNullOrWhiteSpace(busReported))
                            {
                                string br = ik.GetValue("BusReportedDeviceDesc") as string
                                    ?? ik.GetValue("FriendlyName") as string
                                    ?? "";
                                if (!string.IsNullOrWhiteSpace(br)) busReported = br.Trim();
                            }
                        }
                    }
                }
            }
            catch { }
        }

        var details = new UsbRegistryDeviceDetails(oemName, busReported, hasXusb);
        _usbDetailsCache[cacheKey] = details;
        return details;
    }

    public static bool IsXInputBackedDevice(ushort vid, ushort pid, string? oemName, string? winMmName)
    {
        if (vid == 0x045E) return true;
        var sig = LookupKnownSignature(vid, pid);
        if (sig != null && sig.IsKnownXInput) return true;

        var reg = QueryRegistryDeviceDetails(vid, pid);
        if (reg.HasXusbService) return true;

        string merged = $"{oemName} {winMmName} {reg.OemName}".ToUpperInvariant();
        if (merged.Contains("XBOX") || merged.Contains("XINPUT") ||
            merged.Contains("PC-ДЖОЙСТИКА MICROSOFT") || merged.Contains("MICROSOFT PC-JOYSTICK"))
        {
            return true;
        }
        if ((oemName ?? reg.OemName ?? "").StartsWith("Controller (", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }

    public static (string DisplayName, string Manufacturer, InputDeviceFormFactor FormFactor) ResolveFriendlyDeviceInfo(
        ushort vid,
        ushort pid,
        string? oemName,
        string? winMmName,
        bool isXInput)
    {
        var sig = LookupKnownSignature(vid, pid);
        if (sig != null)
        {
            return (sig.DisplayName, sig.Manufacturer, sig.FormFactor);
        }

        var reg = QueryRegistryDeviceDetails(vid, pid);
        string rawName = CleanDeviceName(reg.OemName)
            ?? CleanDeviceName(oemName)
            ?? CleanDeviceName(reg.BusReportedName)
            ?? CleanDeviceName(winMmName)
            ?? "";

        // Check SDL / RetroArch autoconfig friendly name if still generic
        EnsureAutoconfigLoaded();
        if (vid != 0 || pid != 0)
        {
            if (_cfgByVidPid.TryGetValue((vid, pid), out var cfgList) && cfgList.Count > 0)
            {
                string cfgName = cfgList[0].DisplayName;
                if (!string.IsNullOrWhiteSpace(cfgName) && (string.IsNullOrWhiteSpace(rawName) || IsGenericOsDriverName(rawName)))
                    rawName = cfgName;
            }
            else if (_sdlByVidPid.TryGetValue((vid, pid), out var sdl) && !string.IsNullOrWhiteSpace(sdl.Name))
            {
                if (string.IsNullOrWhiteSpace(rawName) || IsGenericOsDriverName(rawName))
                    rawName = sdl.Name;
            }
        }

        if (string.IsNullOrWhiteSpace(rawName))
        {
            rawName = isXInput ? "XInput Игровой контроллер" : $"USB / HID Контроллер ({vid:X4}:{pid:X4})";
        }

        string mfr = InferManufacturer(vid, rawName);
        var ff = ClassifyFormFactor(rawName, vid, pid);
        return (rawName, mfr, ff);
    }

    private static string? CleanDeviceName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string trimmed = name.Trim();
        if (trimmed.Equals("Controller (XBOX 360 For Windows)", StringComparison.OrdinalIgnoreCase))
            return "Xbox 360 Controller for Windows";
        if (trimmed.StartsWith("Controller (", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(')') && trimmed.Length > 13)
        {
            string inner = trimmed[12..^1].Trim();
            if (!string.IsNullOrWhiteSpace(inner)) return NormalizeVendorProductName(inner);
        }
        return IsGenericOsDriverName(trimmed) ? null : NormalizeVendorProductName(trimmed);
    }

    private static string NormalizeVendorProductName(string s)
    {
        if (s.Equals("Flydigi APEX5 Wireless", StringComparison.OrdinalIgnoreCase))
            return "Flydigi APEX 5 / APEX 5 EVA Wireless";
        if (s.Contains("APEX5", StringComparison.OrdinalIgnoreCase) && !s.Contains("EVA", StringComparison.OrdinalIgnoreCase))
            return s.Replace("APEX5", "APEX 5 / APEX 5 EVA", StringComparison.OrdinalIgnoreCase);
        return s;
    }

    public static bool IsGenericOsDriverName(string name)
    {
        string u = name.ToUpperInvariant();
        return u.Contains("ДРАЙВЕР PC-ДЖОЙСТИКА")
            || u.Contains("MICROSOFT PC-JOYSTICK")
            || u.Contains("HID-COMPLIANT GAME CONTROLLER")
            || u.Contains("USB GAMEPAD") && u.Length < 14
            || u == "ДЖОЙСТИК HID";
    }

    private static string InferManufacturer(ushort vid, string name)
    {
        string u = name.ToUpperInvariant();
        if (vid == 0x37D7 || u.Contains("FLYDIGI") || u.Contains("VADER") || u.Contains("APEX 5") || u.Contains("APEX5")) return "Flydigi";
        if (vid == 0x045E || u.Contains("XBOX")) return "Microsoft";
        if (vid == 0x054C || u.Contains("DUALSENSE") || u.Contains("DUALSHOCK")) return "Sony";
        if (vid == 0x057E || u.Contains("NINTENDO") || u.Contains("SWITCH PRO") || u.Contains("JOY-CON")) return "Nintendo";
        if (vid == 0x2DC8 || u.Contains("8BITDO")) return "8BitDo";
        if (vid == 0x046D || u.Contains("LOGITECH")) return "Logitech";
        if (vid == 0x044F || u.Contains("THRUSTMASTER")) return "Thrustmaster";
        if (vid == 0x0EB7 || u.Contains("FANATEC")) return "Fanatec";
        if (vid == 0x346E || u.Contains("MOZA")) return "MOZA Racing";
        if (vid == 0x3670 || u.Contains("SIMAGIC")) return "Simagic";
        if (vid == 0x231D || u.Contains("VKB")) return "VKB-Sim";
        if (vid == 0x3344 || u.Contains("VIRPIL")) return "VIRPIL Controls";
        if (vid == 0x4098 || u.Contains("WINWING")) return "WINWING";
        if (vid == 0x0F0D || u.Contains("HORI")) return "HORI";
        if (vid == 0x2C22 || u.Contains("QANBA")) return "Qanba";
        if (vid == 0x1532 || u.Contains("RAZER")) return "Razer";
        if (vid == 0x3537 || u.Contains("GAMESIR")) return "GameSir";
        if (vid == 0x2F24 || u.Contains("GULIKIT")) return "GuliKit";
        return "Generic HID";
    }

    public static InputDeviceFormFactor ClassifyFormFactor(string? name, ushort vid, ushort pid)
    {
        var sig = LookupKnownSignature(vid, pid);
        if (sig != null) return sig.FormFactor;

        string n = (name ?? "").ToUpperInvariant();

        if (n.Contains("KBD") || n.Contains("KEYBOARD") || n.Contains("КЛАВИАТУРА") || n.Contains("MOUSE") || n.Contains("МЫШЬ"))
            return InputDeviceFormFactor.KeyboardMouse;

        if (vid == 0x0EB7 || vid == 0x346E || vid == 0x3670 || vid == 0x3416 || vid == 0x2433 ||
            n.Contains("WHEEL") || n.Contains("G29") || n.Contains("G920") || n.Contains("G923") ||
            n.Contains("G27") || n.Contains("G25") || n.Contains("DRIVING FORCE") || n.Contains("FANATEC") ||
            n.Contains("MOZA") || n.Contains("SIMAGIC") || n.Contains("T300") || n.Contains("T150") ||
            n.Contains("T248") || n.Contains("TMX") || n.Contains("CAMMUS") || n.Contains("РУЛЬ") || n.Contains("PEDAL"))
            return InputDeviceFormFactor.RacingWheel;

        if (vid == 0x231D || vid == 0x3344 || vid == 0x4098 || vid == 0x294B || vid == 0x068E ||
            n.Contains("HOTAS") || n.Contains("FLIGHT") || n.Contains("YOKE") || n.Contains("THROTTLE") ||
            n.Contains("RUDDER") || n.Contains("EXTREME 3D") || n.Contains("VKB") || n.Contains("VIRPIL") ||
            n.Contains("WINWING") || n.Contains("T.16000M") || n.Contains("WARTHOG") || n.Contains("GLADIATOR") ||
            n.Contains("X52") || n.Contains("X56") || n.Contains("АВИА") || n.Contains("ШТУРВАЛ"))
            return InputDeviceFormFactor.FlightJoystickHotas;

        if (n.Contains("HITBOX") || n.Contains("SNACKBOX") || n.Contains("HAUTE42") || n.Contains("LEVERLESS") ||
            n.Contains("FLATBOX") || n.Contains("KITSUNE") || n.Contains("GP2040"))
            return InputDeviceFormFactor.LeverlessHitBox;

        if (vid == 0x2C22 ||
            n.Contains("FIGHTSTICK") || n.Contains("ARCADE") || n.Contains("QANBA") || n.Contains("MAYFLASH") ||
            n.Contains("SANWA") || n.Contains("SEIMITSU") || n.Contains("REAL ARCADE") || n.Contains("PANTHERA") ||
            n.Contains("ATROX") || n.Contains("HORI FIGHTING") || n.Contains("АРКАДН"))
            return InputDeviceFormFactor.ArcadeFightStick;

        if (vid == 0x054C || n.Contains("DUALSENSE") || n.Contains("DUALSHOCK") || n.Contains("PLAYSTATION") ||
            n.Contains("PS5") || n.Contains("PS4") || n.Contains("PS3") || n.Contains("SONY"))
            return InputDeviceFormFactor.PlayStationPad;

        if (vid == 0x057E || n.Contains("SWITCH") || n.Contains("PRO CONTROLLER") || n.Contains("JOY-CON"))
            return InputDeviceFormFactor.NintendoPad;

        if (n.Contains("6-BUTTON") || n.Contains("MEGA DRIVE") || n.Contains("GENESIS") || n.Contains("M30") || n.Contains("SATURN") || n.Contains("SEGA"))
            return InputDeviceFormFactor.SegaSixButton;

        if (n.Contains("SNES") || n.Contains("SUPER NINTENDO") || n.Contains("SUPER FAMICOM"))
            return InputDeviceFormFactor.SnesPad;

        if (n.Contains("NES") || n.Contains("FAMICOM") || n.Contains("DENDY"))
            return InputDeviceFormFactor.NesPad;

        if (n.Contains("N64") || n.Contains("NINTENDO 64"))
            return InputDeviceFormFactor.N64Pad;

        return InputDeviceFormFactor.XboxGamepad;
    }

    // ==================== RETROARCH AUTOCONFIG + SDL GAMECONTROLLERDB PARSER ====================

    public static void EnsureAutoconfigLoaded()
    {
        lock (_autoconfigLock)
        {
            if (_autoconfigLoaded) return;
            _autoconfigLoaded = true;

            try
            {
                string autoDir = Path.Combine(AppPaths.RootDir, "autoconfig");
                if (!Directory.Exists(autoDir)) return;

                foreach (string driverSub in new[] { "xinput", "dinput", "hid", "winraw" })
                {
                    string subPath = Path.Combine(autoDir, driverSub);
                    if (!Directory.Exists(subPath)) continue;
                    foreach (string file in Directory.EnumerateFiles(subPath, "*.cfg", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            ParseCfgFile(file, driverSub);
                        }
                        catch { }
                    }
                }

                string sdlPath = Path.Combine(autoDir, "sdl3", "gamecontrollerdb.cfg");
                if (!File.Exists(sdlPath)) sdlPath = Path.Combine(autoDir, "sdl2", "gamecontrollerdb.cfg");
                if (File.Exists(sdlPath))
                {
                    try { ParseSdlGameControllerDb(sdlPath); } catch { }
                }
            }
            catch { }
        }
    }

    private static void ParseCfgFile(string path, string driver)
    {
        var entry = new ParsedCfgEntry
        {
            SourceName = Path.GetFileName(path),
            Driver = driver
        };

        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            string key = line[..eq].Trim();
            string val = line[(eq + 1)..].Trim().Trim('"');
            entry.KV[key] = val;

            if (key.Equals("input_vendor_id", StringComparison.OrdinalIgnoreCase) && TryParseUshortId(val, out ushort v))
                entry.Vid = v;
            else if (key.Equals("input_product_id", StringComparison.OrdinalIgnoreCase) && TryParseUshortId(val, out ushort p))
                entry.Pid = p;
            else if (key.Equals("input_device_display_name", StringComparison.OrdinalIgnoreCase))
                entry.DisplayName = val;
            else if (key.Equals("input_device", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(entry.DisplayName))
                entry.DisplayName = val;
        }

        if (entry.Vid != 0 || entry.Pid != 0)
        {
            if (!_cfgByVidPid.TryGetValue((entry.Vid, entry.Pid), out var list))
            {
                list = new List<ParsedCfgEntry>();
                _cfgByVidPid[(entry.Vid, entry.Pid)] = list;
            }
            list.Add(entry);
        }
        if (!string.IsNullOrWhiteSpace(entry.DisplayName))
        {
            _cfgByName.TryAdd(entry.DisplayName, entry);
        }
    }

    private static bool TryParseUshortId(string s, out ushort val)
    {
        val = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ushort.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out val);
        return ushort.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out val);
    }

    private static void ParseSdlGameControllerDb(string path)
    {
        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length < 34 || line.StartsWith('#')) continue;
            if (!line.Contains("platform:Windows", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = line.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;

            string guid = parts[0].Trim();
            if (guid.Length < 20) continue;

            // SDL Windows USB GUID layout: 03000000 VVvv 0000 PPpp 0000 0000 00000000
            if (!TryParseLittleEndianHex16(guid, 8, out ushort vid) ||
                !TryParseLittleEndianHex16(guid, 16, out ushort pid))
            {
                continue;
            }

            var sdl = new ParsedSdlEntry { Name = parts[1].Trim(), Vid = vid, Pid = pid };
            for (int i = 2; i < parts.Length; i++)
            {
                string tok = parts[i].Trim();
                int colon = tok.IndexOf(':');
                if (colon > 0)
                {
                    sdl.Map[tok[..colon]] = tok[(colon + 1)..];
                }
            }

            if (vid != 0 || pid != 0)
                _sdlByVidPid.TryAdd((vid, pid), sdl);
        }
    }

    private static bool TryParseLittleEndianHex16(string hex, int offset, out ushort val)
    {
        val = 0;
        if (hex.Length < offset + 4) return false;
        if (!byte.TryParse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte lo)) return false;
        if (!byte.TryParse(hex.AsSpan(offset + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte hi)) return false;
        val = (ushort)(lo | (hi << 8));
        return true;
    }

    public static AutoconfigMappingResult? TryResolveWinMmAutoconfig(ushort vid, ushort pid, string? deviceName)
    {
        EnsureAutoconfigLoaded();

        ParsedCfgEntry? cfg = null;
        if ((vid != 0 || pid != 0) && _cfgByVidPid.TryGetValue((vid, pid), out var candidates))
        {
            cfg = candidates.FirstOrDefault(c => c.Driver.Equals("dinput", StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault(c => c.Driver.Equals("hid", StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault();
        }
        if (cfg == null && !string.IsNullOrWhiteSpace(deviceName))
        {
            _cfgByName.TryGetValue(deviceName.Trim(), out cfg);
        }

        if (cfg != null)
        {
            var btns = new Dictionary<int, PhysicalInputBinding>();
            var axes = new Dictionary<int, PhysicalInputBinding>();

            void MapBtnKey(int retroId, params string[] keys)
            {
                foreach (var k in keys)
                {
                    if (cfg.KV.TryGetValue(k, out var token) && TryParseRetroToken(token, out var b))
                    {
                        btns[retroId] = b;
                        return;
                    }
                }
            }

            MapBtnKey(0, "input_b_btn", "input_b_axis");
            MapBtnKey(1, "input_y_btn", "input_y_axis");
            MapBtnKey(2, "input_select_btn", "input_select_axis");
            MapBtnKey(3, "input_start_btn", "input_start_axis");
            MapBtnKey(4, "input_up_btn", "input_up_axis");
            MapBtnKey(5, "input_down_btn", "input_down_axis");
            MapBtnKey(6, "input_left_btn", "input_left_axis");
            MapBtnKey(7, "input_right_btn", "input_right_axis");
            MapBtnKey(8, "input_a_btn", "input_a_axis");
            MapBtnKey(9, "input_x_btn", "input_x_axis");
            MapBtnKey(10, "input_l_btn", "input_l_axis");
            MapBtnKey(11, "input_r_btn", "input_r_axis");
            MapBtnKey(12, "input_l2_btn", "input_l2_axis");
            MapBtnKey(13, "input_r2_btn", "input_r2_axis");
            MapBtnKey(14, "input_l3_btn", "input_l3_axis");
            MapBtnKey(15, "input_r3_btn", "input_r3_axis");

            void MapAxisKey(int analogId, string key)
            {
                if (cfg.KV.TryGetValue(key, out var token) && TryParseRetroAxisToken(token, out var b))
                    axes[analogId] = b;
            }

            MapAxisKey(100, "input_l_x_minus_axis");
            MapAxisKey(101, "input_l_x_plus_axis");
            MapAxisKey(102, "input_l_y_minus_axis");
            MapAxisKey(103, "input_l_y_plus_axis");
            MapAxisKey(104, "input_r_x_minus_axis");
            MapAxisKey(105, "input_r_x_plus_axis");
            MapAxisKey(106, "input_r_y_minus_axis");
            MapAxisKey(107, "input_r_y_plus_axis");

            if (btns.Count >= 4 || axes.Count > 0)
                return new AutoconfigMappingResult($"RetroArch/{cfg.SourceName}", btns, axes);
        }

        if ((vid != 0 || pid != 0) && _sdlByVidPid.TryGetValue((vid, pid), out var sdl))
        {
            var btns = new Dictionary<int, PhysicalInputBinding>();
            var axes = new Dictionary<int, PhysicalInputBinding>();

            void MapSdl(int retroId, string sdlKey)
            {
                if (sdl.Map.TryGetValue(sdlKey, out var tok) && TryParseSdlToken(tok, out var b))
                    btns[retroId] = b;
            }

            MapSdl(0, "a");
            MapSdl(8, "b");
            MapSdl(1, "x");
            MapSdl(9, "y");
            MapSdl(2, "back");
            MapSdl(3, "start");
            MapSdl(4, "dpup");
            MapSdl(5, "dpdown");
            MapSdl(6, "dpleft");
            MapSdl(7, "dpright");
            MapSdl(10, "leftshoulder");
            MapSdl(11, "rightshoulder");
            MapSdl(12, "lefttrigger");
            MapSdl(13, "righttrigger");
            MapSdl(14, "leftstick");
            MapSdl(15, "rightstick");

            if (sdl.Map.TryGetValue("leftx", out var lxTok) && int.TryParse(lxTok.TrimStart('a'), out int lxIdx))
            {
                axes[100] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, lxIdx, -1, $"Ось {GetWinMmAxisName(lxIdx)} -");
                axes[101] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, lxIdx, +1, $"Ось {GetWinMmAxisName(lxIdx)} +");
            }
            if (sdl.Map.TryGetValue("lefty", out var lyTok) && int.TryParse(lyTok.TrimStart('a'), out int lyIdx))
            {
                axes[102] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, lyIdx, -1, $"Ось {GetWinMmAxisName(lyIdx)} -");
                axes[103] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, lyIdx, +1, $"Ось {GetWinMmAxisName(lyIdx)} +");
            }
            if (sdl.Map.TryGetValue("rightx", out var rxTok) && int.TryParse(rxTok.TrimStart('a'), out int rxIdx))
            {
                axes[104] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, rxIdx, -1, $"Ось {GetWinMmAxisName(rxIdx)} -");
                axes[105] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, rxIdx, +1, $"Ось {GetWinMmAxisName(rxIdx)} +");
            }
            if (sdl.Map.TryGetValue("righty", out var ryTok) && int.TryParse(ryTok.TrimStart('a'), out int ryIdx))
            {
                axes[106] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, ryIdx, -1, $"Ось {GetWinMmAxisName(ryIdx)} -");
                axes[107] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, ryIdx, +1, $"Ось {GetWinMmAxisName(ryIdx)} +");
            }

            if (btns.Count >= 4 || axes.Count > 0)
                return new AutoconfigMappingResult($"SDL_GameControllerDB/{sdl.Name}", btns, axes);
        }

        return null;
    }

    public static string GetWinMmAxisName(int axisIndex) => axisIndex switch
    {
        0 => "X (Стик/Руль)",
        1 => "Y (Вертикаль)",
        2 => "Z (Курок/Газ/РУД)",
        3 => "R (Правый Y / Твист)",
        4 => "U (Правый X)",
        5 => "V (Доп. педаль)",
        _ => $"Axis {axisIndex}"
    };

    private static bool TryParseRetroToken(string token, out PhysicalInputBinding binding)
    {
        binding = new PhysicalInputBinding();
        if (string.IsNullOrWhiteSpace(token)) return false;
        token = token.Trim().ToLowerInvariant();

        if (token == "h0up") { binding = new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 0, "POV Вверх"); return true; }
        if (token == "h0right") { binding = new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 9000, "POV Вправо"); return true; }
        if (token == "h0down") { binding = new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 18000, "POV Вниз"); return true; }
        if (token == "h0left") { binding = new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 27000, "POV Влево"); return true; }

        if ((token.StartsWith('+') || token.StartsWith('-')) && int.TryParse(token[1..], out int ax))
        {
            int dir = token[0] == '+' ? 1 : -1;
            binding = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, ax, dir, $"Ось {GetWinMmAxisName(ax)} {(dir > 0 ? "+" : "-")}");
            return true;
        }

        if (int.TryParse(token, out int btnIdx) && btnIdx >= 0 && btnIdx < 32)
        {
            binding = new PhysicalInputBinding(PhysicalInputType.WinMmButton, btnIdx, 0, $"Кнопка {btnIdx + 1}");
            return true;
        }

        return false;
    }

    private static bool TryParseRetroAxisToken(string token, out PhysicalInputBinding binding)
    {
        binding = new PhysicalInputBinding();
        if (string.IsNullOrWhiteSpace(token)) return false;
        token = token.Trim();
        int dir = token.StartsWith('-') ? -1 : 1;
        string digits = token.TrimStart('+', '-');
        if (!int.TryParse(digits, out int ax) || ax < 0 || ax > 5) return false;
        binding = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, ax, dir, $"Ось {GetWinMmAxisName(ax)} {(dir > 0 ? "+" : "-")}");
        return true;
    }

    private static bool TryParseSdlToken(string token, out PhysicalInputBinding binding)
    {
        binding = new PhysicalInputBinding();
        if (string.IsNullOrWhiteSpace(token)) return false;
        token = token.Trim().ToLowerInvariant();

        if (token.StartsWith('b') && int.TryParse(token[1..], out int btn) && btn >= 0 && btn < 32)
        {
            binding = new PhysicalInputBinding(PhysicalInputType.WinMmButton, btn, 0, $"Кнопка {btn + 1}");
            return true;
        }
        if (token == "h0.1") { binding = new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 0, "POV Вверх"); return true; }
        if (token == "h0.2") { binding = new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 9000, "POV Вправо"); return true; }
        if (token == "h0.4") { binding = new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 18000, "POV Вниз"); return true; }
        if (token == "h0.8") { binding = new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 27000, "POV Влево"); return true; }
        if (token.StartsWith('a') && int.TryParse(token[1..], out int ax) && ax >= 0 && ax <= 5)
        {
            binding = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, ax, +1, $"Ось {GetWinMmAxisName(ax)} +");
            return true;
        }
        return false;
    }
}
