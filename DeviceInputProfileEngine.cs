using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace StormRetroArch;

/// <summary>
/// Глобальные пути файловой системы экосистемы STORM.
/// </summary>
public static class AppPaths
{
    public static string RootDir => CoreManager.AppDir;
}

/// <summary>
/// Аппаратный форм-фактор контроллера для выбора студийной иллюстрации и типовой раскладки.
/// 12 категорий аппаратных устройств.
/// </summary>
public enum InputDeviceFormFactor
{
    XboxGamepad = 0,
    PlayStationPad = 1,
    NintendoPad = 2,
    ArcadeFightStick = 3,
    LeverlessHitBox = 4,
    FlightJoystickHotas = 5,
    RacingWheel = 6,
    SegaSixButton = 7,
    SnesPad = 8,
    NesPad = 9,
    N64Pad = 10,
    KeyboardMouse = 11
}

/// <summary>
/// Тип физического источника ввода.
/// </summary>
public enum PhysicalInputType
{
    None = 0,
    XInputButton = 1,
    XInputTrigger = 2,
    XInputAxis = 3,
    WinMmButton = 4,
    WinMmAxis = 5,
    WinMmPovHat = 6,
    KeyboardKey = 7
}

/// <summary>
/// Физическая привязка кнопки, оси, курка или шляпки POV к виртуальной кнопке Libretro Joypad или оси Analog.
/// </summary>
public class PhysicalInputBinding
{
    public PhysicalInputType Type { get; set; } = PhysicalInputType.None;
    public int Code { get; set; }
    public int Parameter { get; set; }
    public string DisplayLabel { get; set; } = "";

    public PhysicalInputBinding() { }

    public PhysicalInputBinding(PhysicalInputType type, int code, int param = 0, string label = "")
    {
        Type = type;
        Code = code;
        Parameter = param;
        DisplayLabel = !string.IsNullOrWhiteSpace(label) ? label : GetDefaultLabel(type, code, param);
    }

    public static string GetDefaultLabel(PhysicalInputType type, int code, int param) => type switch
    {
        PhysicalInputType.XInputButton => code switch
        {
            0x0001 => "D-Pad Вверх",
            0x0002 => "D-Pad Вниз",
            0x0004 => "D-Pad Влево",
            0x0008 => "D-Pad Вправо",
            0x0010 => "Start",
            0x0020 => "Back / Select",
            0x0040 => "Левый стик L3",
            0x0080 => "Правый стик R3",
            0x0100 => "Бампер LB",
            0x0200 => "Бампер RB",
            0x0400 => "Guide / Home",
            0x1000 => "Кнопка A",
            0x2000 => "Кнопка B",
            0x4000 => "Кнопка X",
            0x8000 => "Кнопка Y",
            _ => $"Кнопка 0x{code:X4}"
        },
        PhysicalInputType.XInputTrigger => code == 0 ? "Левый триггер LT" : "Правый триггер RT",
        PhysicalInputType.XInputAxis => (code, param) switch
        {
            (0, > 0) => "Левый стик: Вправо",
            (0, < 0) => "Левый стик: Влево",
            (1, > 0) => "Левый стик: Вверх",
            (1, < 0) => "Левый стик: Вниз",
            (2, > 0) => "Правый стик: Вправо",
            (2, < 0) => "Правый стик: Влево",
            (3, > 0) => "Правый стик: Вверх",
            (3, < 0) => "Правый стик: Вниз",
            _ => $"Ось {code} {(param > 0 ? "+" : "-")}"
        },
        PhysicalInputType.WinMmButton => $"Кнопка {code + 1}",
        PhysicalInputType.WinMmPovHat => param switch
        {
            0 => "POV Вверх",
            9000 => "POV Вправо",
            18000 => "POV Вниз",
            27000 => "POV Влево",
            _ => $"POV {param / 100} град."
        },
        PhysicalInputType.WinMmAxis => $"Ось {ControllerHardwareDatabase.GetWinMmAxisName(code)} {(param > 0 ? "+" : "-")}",
        PhysicalInputType.KeyboardKey => $"Клавиша 0x{code:X2}",
        _ => "Не назначено"
    };
}

/// <summary>
/// Слой раскладки кнопок и осей (Глобальный, Для ядра или Для конкретной игры).
/// </summary>
public class BindingLayer
{
    public string LayerName { get; set; } = "Global";
    public Dictionary<int, PhysicalInputBinding> JoypadBindings { get; set; } = new();
    public Dictionary<int, PhysicalInputBinding> AnalogBindings { get; set; } = new();
    public int AnalogDeadzonePercent { get; set; } = 15;
    public int SteeringDeadzonePercent { get; set; } = 4;
    public int TriggerThresholdPercent { get; set; } = 12;
    public double AnalogSensitivity { get; set; } = 1.0;
    public int RumbleStrengthPercent { get; set; } = 100;
    public uint TurboButtonMask { get; set; } = 0;
    public PhysicalInputBinding? HotkeyEnableBinding { get; set; }
    public Dictionary<string, PhysicalInputBinding> DeviceHotkeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public BindingLayer Clone()
    {
        var copy = new BindingLayer
        {
            LayerName = LayerName,
            AnalogDeadzonePercent = AnalogDeadzonePercent,
            SteeringDeadzonePercent = SteeringDeadzonePercent,
            TriggerThresholdPercent = TriggerThresholdPercent,
            AnalogSensitivity = AnalogSensitivity,
            RumbleStrengthPercent = RumbleStrengthPercent,
            TurboButtonMask = TurboButtonMask,
            HotkeyEnableBinding = HotkeyEnableBinding != null
                ? new PhysicalInputBinding(HotkeyEnableBinding.Type, HotkeyEnableBinding.Code, HotkeyEnableBinding.Parameter, HotkeyEnableBinding.DisplayLabel)
                : null
        };

        foreach (var (k, v) in JoypadBindings)
        {
            copy.JoypadBindings[k] = new PhysicalInputBinding(v.Type, v.Code, v.Parameter, v.DisplayLabel);
        }
        if (AnalogBindings != null)
        {
            foreach (var (k, v) in AnalogBindings)
            {
                copy.AnalogBindings[k] = new PhysicalInputBinding(v.Type, v.Code, v.Parameter, v.DisplayLabel);
            }
        }
        foreach (var (k, v) in DeviceHotkeys)
        {
            copy.DeviceHotkeys[k] = new PhysicalInputBinding(v.Type, v.Code, v.Parameter, v.DisplayLabel);
        }
        return copy;
    }
}

/// <summary>
/// Иерархический профиль устройства: Глобальный слой + Срезы для конкретных ядер + Срезы для игр.
/// </summary>
public class DeviceInputProfile
{
    public string DeviceId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public ushort VendorId { get; set; }
    public ushort ProductId { get; set; }
    public InputDeviceFormFactor FormFactor { get; set; } = InputDeviceFormFactor.XboxGamepad;
    public DateTime LastConnectedUtc { get; set; } = DateTime.UtcNow;
    public bool AutoLoadOnConnect { get; set; } = true;
    public BindingLayer GlobalLayer { get; set; } = new();
    public Dictionary<string, BindingLayer> CoreLayers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, BindingLayer> GameLayers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Описание активного подключённого устройства ввода в одном из 8 портов эмулятора.
/// </summary>
public class ConnectedInputDevice
{
    public int PortIndex { get; set; }
    public string InstanceKey { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Manufacturer { get; set; } = "Generic";
    public string AutoconfigSource { get; set; } = "";
    public ushort VendorId { get; set; }
    public ushort ProductId { get; set; }
    public bool IsXInput { get; set; }
    public bool IsKeyboardVirtual { get; set; }
    public uint XInputIndex { get; set; }
    public uint WinMmIndex { get; set; }
    public uint AxisCount { get; set; }
    public uint ButtonCount { get; set; }
    public bool HasPov { get; set; }
    public uint AxisCapsMask { get; set; }
    public uint[] AxisMin { get; set; } = new uint[6];
    public uint[] AxisMax { get; set; } = new uint[6];
    public bool SupportsRumble { get; set; }
    public InputDeviceFormFactor FormFactor { get; set; } = InputDeviceFormFactor.XboxGamepad;
    public bool IsConnected { get; set; }
    public DeviceInputProfile? Profile { get; set; }
    public BindingLayer? EffectiveLayer { get; set; }
}

/// <summary>
/// Движок универсального обнаружения устройств ввода (XInput + DirectInput/WinMM + Autoconfig + Клавиатура),
/// маршрутизации 8 портов игроков и захвата физических осей/кнопок/рулей/педалей.
/// </summary>
public static class DeviceInputProfileEngine
{
    private static readonly object _syncLock = new();
    private static readonly Dictionary<string, DeviceInputProfile> _profileCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConnectedInputDevice[] _ports = new ConnectedInputDevice[8];
    private static readonly List<ConnectedInputDevice> _rawHardwareDevices = new();
    private static int _primaryPortIndex = 0;
    private static string? _activeCoreId = null;
    private static string? _activeGameId = null;
    private static bool _initialized = false;

    // Capture baseline snapshots so sticky axes / pedals at rest do not false-trigger
    private static readonly XINPUT_STATE[] _baselineXInput = new XINPUT_STATE[4];
    private static readonly bool[] _baselineXInputValid = new bool[4];
    private static readonly JOYINFOEX[] _baselineWinMm = new JOYINFOEX[16];
    private static readonly JOYCAPSW[] _baselineWinMmCaps = new JOYCAPSW[16];
    private static readonly bool[] _baselineWinMmValid = new bool[16];

    // WinMM Joystick P/Invoke
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct JOYCAPSW
    {
        public ushort wMid;
        public ushort wPid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public uint wXmin;
        public uint wXmax;
        public uint wYmin;
        public uint wYmax;
        public uint wZmin;
        public uint wZmax;
        public uint wNumButtons;
        public uint wPeriodMin;
        public uint wPeriodMax;
        public uint wRmin;
        public uint wRmax;
        public uint wUmin;
        public uint wUmax;
        public uint wVmin;
        public uint wVmax;
        public uint wCaps;
        public uint wMaxAxes;
        public uint wNumAxes;
        public uint wMaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szRegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szOEMVxD;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JOYINFOEX
    {
        public uint dwSize;
        public uint dwFlags;
        public uint dwXpos;
        public uint dwYpos;
        public uint dwZpos;
        public uint dwRpos;
        public uint dwUpos;
        public uint dwVpos;
        public uint dwButtons;
        public uint dwButtonNumber;
        public uint dwPOV;
        public uint dwReserved1;
        public uint dwReserved2;
    }

    internal const uint JOY_RETURNX = 0x00000001;
    internal const uint JOY_RETURNY = 0x00000002;
    internal const uint JOY_RETURNZ = 0x00000004;
    internal const uint JOY_RETURNR = 0x00000008;
    internal const uint JOY_RETURNU = 0x00000010;
    internal const uint JOY_RETURNV = 0x00000020;
    internal const uint JOY_RETURNPOV = 0x00000040;
    internal const uint JOY_RETURNBUTTONS = 0x00000080;
    internal const uint JOY_RETURNALL = (JOY_RETURNX | JOY_RETURNY | JOY_RETURNZ |
                                         JOY_RETURNR | JOY_RETURNU | JOY_RETURNV |
                                         JOY_RETURNPOV | JOY_RETURNBUTTONS);
    internal const int JOYERR_NOERROR = 0;

    [DllImport("winmm.dll", SetLastError = true)]
    internal static extern int joyGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int joyGetDevCapsW(UIntPtr uJoyID, out JOYCAPSW pjc, uint cbjc);

    [DllImport("winmm.dll", SetLastError = true)]
    internal static extern int joyGetPosEx(uint uJoyID, ref JOYINFOEX pji);

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_GAMEPAD
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
    public struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint XInputGetStateDelegate(uint dwUserIndex, out XINPUT_STATE pState);

    private static XInputGetStateDelegate? _xinputGetState;
    private static XInputGetStateDelegate? _xinputGetStateEx;
    private static bool _xinputLoaded = false;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpLibFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", EntryPoint = "GetProcAddress", SetLastError = true)]
    private static extern IntPtr GetProcAddressByOrdinal(IntPtr hModule, IntPtr ordinal);

    /// <summary>
    /// Событие активации профиля устройства с русским статусом для OSD-уведомлений.
    /// </summary>
    public static event Action<ConnectedInputDevice, string>? OnDeviceProfileActivated;

    /// <summary>
    /// Событие изменения списка подключённых физических устройств ввода.
    /// </summary>
    public static event Action? OnDevicesChanged;

    public static string DevicesDir => Path.Combine(AppPaths.RootDir, "config", "devices");

    static DeviceInputProfileEngine()
    {
        for (int i = 0; i < 8; i++)
        {
            _ports[i] = new ConnectedInputDevice { PortIndex = i, IsConnected = false };
        }
    }

    public static void Initialize()
    {
        lock (_syncLock)
        {
            if (_initialized) return;
            _initialized = true;

            EnsureDirectories();
            InitXInput();
            SeedPresetProfilesIfEmpty();
            LoadAllStoredProfiles();
            PollDevicesAndHotplug();
        }
    }

    private static void EnsureDirectories()
    {
        try
        {
            if (!Directory.Exists(DevicesDir)) Directory.CreateDirectory(DevicesDir);
        }
        catch { }
    }

    private static void InitXInput()
    {
        if (_xinputLoaded) return;
        _xinputLoaded = true;

        string[] dlls = { "xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll" };
        foreach (var dll in dlls)
        {
            IntPtr hMod = LoadLibrary(dll);
            if (hMod != IntPtr.Zero)
            {
                // Ordinal 100 = XInputGetStateEx (supports Guide/Home bit 0x0400)
                IntPtr pEx = GetProcAddressByOrdinal(hMod, (IntPtr)100);
                if (pEx != IntPtr.Zero)
                {
                    _xinputGetStateEx = Marshal.GetDelegateForFunctionPointer<XInputGetStateDelegate>(pEx);
                }

                IntPtr pFunc = GetProcAddress(hMod, "XInputGetState");
                if (pFunc != IntPtr.Zero)
                {
                    _xinputGetState = Marshal.GetDelegateForFunctionPointer<XInputGetStateDelegate>(pFunc);
                }

                if (_xinputGetStateEx != null || _xinputGetState != null) break;
            }
        }
    }

    internal static bool TryGetXInputState(uint dwUserIndex, out XINPUT_STATE state)
    {
        InitXInput();
        if (_xinputGetStateEx != null)
        {
            return _xinputGetStateEx(dwUserIndex, out state) == 0;
        }
        if (_xinputGetState != null)
        {
            return _xinputGetState(dwUserIndex, out state) == 0;
        }
        state = default;
        return false;
    }

    public static string GetSafeDeviceId(string rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId)) return "DEVICE_UNKNOWN";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = rawId.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray();
        return new string(chars);
    }

    public static InputDeviceFormFactor ClassifyFormFactor(string name, ushort vid, ushort pid)
    {
        return ControllerHardwareDatabase.ClassifyFormFactor(name, vid, pid);
    }

    private static string? LookupRegistryOemName(ushort vid, ushort pid)
    {
        string subKey = $@"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\VID_{vid:X4}&PID_{pid:X4}";
        try
        {
            using var hkcu = Registry.CurrentUser.OpenSubKey(subKey);
            if (hkcu?.GetValue("OEMName") is string name && !string.IsNullOrWhiteSpace(name))
                return name.Trim();
        }
        catch { }

        try
        {
            using var hklm = Registry.LocalMachine.OpenSubKey(subKey);
            if (hklm?.GetValue("OEMName") is string name && !string.IsNullOrWhiteSpace(name))
                return name.Trim();
        }
        catch { }

        return null;
    }

    public static List<ConnectedInputDevice> GetConnectedDevices()
    {
        lock (_syncLock)
        {
            return _ports.Where(p => p.IsConnected).ToList();
        }
    }

    public static List<ConnectedInputDevice> GetRawHardwareDevices()
    {
        lock (_syncLock)
        {
            return _rawHardwareDevices.ToList();
        }
    }

    public static ConnectedInputDevice GetPort(int portIndex)
    {
        lock (_syncLock)
        {
            if (portIndex < 0 || portIndex >= 8) return _ports[0];
            return _ports[portIndex];
        }
    }

    public static void SetPortAssignment(int portIndex, string assignmentKey)
    {
        if (portIndex < 0 || portIndex >= 8) return;
        lock (_syncLock)
        {
            if (SettingsManager.Current != null)
            {
                SettingsManager.Current.PortDeviceAssignments ??= new Dictionary<int, string>();
                SettingsManager.Current.PortDeviceAssignments[portIndex] = string.IsNullOrWhiteSpace(assignmentKey) ? "auto" : assignmentKey;
                SettingsManager.Save();
            }
            RouteRawDevicesToPorts(forceNotify: false);
            ApplyCompiledMapsToNativeHost();
            OnDevicesChanged?.Invoke();
        }
    }

    public static int PrimaryPortIndex
    {
        get { lock (_syncLock) return _primaryPortIndex; }
        set
        {
            lock (_syncLock)
            {
                if (value >= 0 && value < 8) _primaryPortIndex = value;
            }
        }
    }

    public static void SwitchPrimaryPort(int targetPort)
    {
        lock (_syncLock)
        {
            if (targetPort >= 0 && targetPort < 8 && _primaryPortIndex != targetPort)
            {
                _primaryPortIndex = targetPort;
                ApplyCompiledMapsToNativeHost();
                var dev = _ports[targetPort];
                if (dev.IsConnected)
                {
                    string msg = $"Основной контроллер переключён на Порт {targetPort + 1} - «{dev.DisplayName}»";
                    OnDeviceProfileActivated?.Invoke(dev, msg);
                }
            }
        }
    }

    private readonly struct WinMmProbeEntry
    {
        public readonly uint Index;
        public readonly JOYCAPSW Caps;
        public readonly bool IsActive;
        public readonly string? OemName;
        public readonly bool IsXInputBacked;

        public WinMmProbeEntry(uint idx, JOYCAPSW caps, bool active, string? oem, bool xBacked)
        {
            Index = idx;
            Caps = caps;
            IsActive = active;
            OemName = oem;
            IsXInputBacked = xBacked;
        }
    }

    /// <summary>
    /// Периодический опрос подключённых контроллеров (XInput + WinMM/DirectInput + Registry VID:PID + Autoconfig),
    /// строгая дедупликация XInput vs WinMM и маршрутизация портов 1..8.
    /// </summary>
    public static void PollDevicesAndHotplug()
    {
        lock (_syncLock)
        {
            InitXInput();

            // Pre-probe all active WinMM entries
            var winMmEntries = new List<WinMmProbeEntry>();
            int numDevs = 0;
            try { numDevs = joyGetNumDevs(); } catch { }
            uint joyCapSize = (uint)Marshal.SizeOf<JOYCAPSW>();
            uint joyInfoSize = (uint)Marshal.SizeOf<JOYINFOEX>();

            for (uint wi = 0; wi < (uint)Math.Min(numDevs, 16); wi++)
            {
                var joyInfo = new JOYINFOEX { dwSize = joyInfoSize, dwFlags = JOY_RETURNBUTTONS };
                if (joyGetPosEx(wi, ref joyInfo) != JOYERR_NOERROR) continue;

                if (joyGetDevCapsW((UIntPtr)wi, out var caps, joyCapSize) != JOYERR_NOERROR) continue;
                string? oem = LookupRegistryOemName(caps.wMid, caps.wPid);
                bool xBacked = ControllerHardwareDatabase.IsXInputBackedDevice(caps.wMid, caps.wPid, oem, caps.szPname);
                winMmEntries.Add(new WinMmProbeEntry(wi, caps, true, oem, xBacked));
            }

            var rawList = new List<ConnectedInputDevice>();
            var claimedWinMm = new HashSet<uint>();

            // 1. Scan XInput 0..3
            for (uint xi = 0; xi < 4; xi++)
            {
                if (!TryGetXInputState(xi, out _)) continue;

                ushort vid = 0x045E;
                ushort pid = 0x02FD;
                string? oem = null;
                string? winMmName = null;
                uint matchedWinMmIdx = xi;
                JOYCAPSW matchedCaps = default;
                bool hasCaps = false;

                // Pair XInput slot with corresponding unclaimed XInput-backed WinMM slot
                var matchCandidate = winMmEntries.FirstOrDefault(e => e.IsXInputBacked && !claimedWinMm.Contains(e.Index));
                if (matchCandidate.IsActive)
                {
                    claimedWinMm.Add(matchCandidate.Index);
                    matchedWinMmIdx = matchCandidate.Index;
                    matchedCaps = matchCandidate.Caps;
                    hasCaps = true;
                    vid = matchedCaps.wMid;
                    pid = matchedCaps.wPid;
                    oem = matchCandidate.OemName;
                    winMmName = matchedCaps.szPname;
                }
                else
                {
                    oem = LookupRegistryOemName(vid, pid);
                }

                var (dispName, mfr, ff) = ControllerHardwareDatabase.ResolveFriendlyDeviceInfo(vid, pid, oem, winMmName, isXInput: true);
                string devId = $"VID_{vid:X4}_PID_{pid:X4}";

                var dev = new ConnectedInputDevice
                {
                    InstanceKey = $"XINPUT:{xi}",
                    DeviceId = devId,
                    DisplayName = dispName,
                    Manufacturer = mfr,
                    AutoconfigSource = "XInput Native",
                    VendorId = vid,
                    ProductId = pid,
                    IsXInput = true,
                    XInputIndex = xi,
                    WinMmIndex = matchedWinMmIdx,
                    FormFactor = ff,
                    SupportsRumble = true,
                    IsConnected = true
                };

                if (hasCaps)
                {
                    PopulateCaps(dev, matchedCaps);
                }
                else
                {
                    dev.AxisCount = 6;
                    dev.ButtonCount = 11;
                    dev.HasPov = true;
                }

                rawList.Add(dev);
            }

            // 2. Scan unclaimed WinMM DirectInput / HID devices (Wheels, Flight Sticks, Fight Sticks, DualSense USB, etc.)
            foreach (var entry in winMmEntries)
            {
                if (rawList.Count >= 8) break;
                if (claimedWinMm.Contains(entry.Index)) continue;

                // Safety deduplication: skip orphaned XInput-backed OS wrapper if XInput already holds an active slot for this VID:PID
                if (entry.IsXInputBacked && rawList.Any(r => r.IsXInput && r.VendorId == entry.Caps.wMid && r.ProductId == entry.Caps.wPid))
                {
                    continue;
                }

                ushort vid = entry.Caps.wMid;
                ushort pid = entry.Caps.wPid;
                var (dispName, mfr, ff) = ControllerHardwareDatabase.ResolveFriendlyDeviceInfo(vid, pid, entry.OemName, entry.Caps.szPname, isXInput: false);
                string devId = (vid != 0 || pid != 0) ? $"VID_{vid:X4}_PID_{pid:X4}" : $"WINMM_JOY_{entry.Index}";

                var autoCfg = ControllerHardwareDatabase.TryResolveWinMmAutoconfig(vid, pid, dispName);

                var dev = new ConnectedInputDevice
                {
                    InstanceKey = $"WINMM:{entry.Index}",
                    DeviceId = devId,
                    DisplayName = dispName,
                    Manufacturer = mfr,
                    AutoconfigSource = autoCfg?.SourceLabel ?? $"WinMM ({ff})",
                    VendorId = vid,
                    ProductId = pid,
                    IsXInput = false,
                    WinMmIndex = entry.Index,
                    FormFactor = ff,
                    SupportsRumble = false,
                    IsConnected = true
                };
                PopulateCaps(dev, entry.Caps);
                rawList.Add(dev);
            }

            _rawHardwareDevices.Clear();
            _rawHardwareDevices.AddRange(rawList);

            bool changed = RouteRawDevicesToPorts(forceNotify: true);
            if (changed)
            {
                ApplyCompiledMapsToNativeHost();
                OnDevicesChanged?.Invoke();
            }
        }
    }

    private static void PopulateCaps(ConnectedInputDevice dev, JOYCAPSW caps)
    {
        dev.AxisCount = Math.Clamp(caps.wNumAxes, 2u, 6u);
        dev.ButtonCount = caps.wNumButtons;
        dev.HasPov = (caps.wCaps & 0x0010) != 0; // JOYCAPS_HASPOV
        dev.AxisCapsMask = caps.wCaps;
        dev.AxisMin = new[] { caps.wXmin, caps.wYmin, caps.wZmin, caps.wRmin, caps.wUmin, caps.wVmin };
        dev.AxisMax = new[] { caps.wXmax, caps.wYmax, caps.wZmax, caps.wRmax, caps.wUmax, caps.wVmax };
    }

    private static bool RouteRawDevicesToPorts(bool forceNotify)
    {
        var assignments = SettingsManager.Current?.PortDeviceAssignments ?? new Dictionary<int, string>();
        var routed = new ConnectedInputDevice?[8];
        var claimedInstances = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Pass 1: explicit port assignments (disabled, kbd, or specific controller key)
        for (int p = 0; p < 8; p++)
        {
            if (!assignments.TryGetValue(p, out var assign) || string.IsNullOrWhiteSpace(assign) ||
                assign.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (assign.Equals("disabled", StringComparison.OrdinalIgnoreCase))
            {
                routed[p] = new ConnectedInputDevice
                {
                    PortIndex = p,
                    InstanceKey = "DISABLED",
                    DeviceId = "DISABLED",
                    DisplayName = $"Порт {p + 1} отключён",
                    IsConnected = false
                };
                continue;
            }

            if (assign.Equals("kbd", StringComparison.OrdinalIgnoreCase))
            {
                routed[p] = CreateVirtualKeyboardDevice(p);
                continue;
            }

            var hw = _rawHardwareDevices.FirstOrDefault(d =>
                !claimedInstances.Contains(d.InstanceKey) &&
                (d.InstanceKey.Equals(assign, StringComparison.OrdinalIgnoreCase) ||
                 d.DeviceId.Equals(assign, StringComparison.OrdinalIgnoreCase)));

            if (hw != null)
            {
                claimedInstances.Add(hw.InstanceKey);
                routed[p] = CloneDeviceForPort(hw, p);
            }
            else
            {
                routed[p] = new ConnectedInputDevice
                {
                    PortIndex = p,
                    InstanceKey = assign,
                    DeviceId = assign,
                    DisplayName = $"Не подключён ({assign})",
                    IsConnected = false
                };
            }
        }

        // Pass 2: auto ports pick next unclaimed hardware devices
        for (int p = 0; p < 8; p++)
        {
            if (routed[p] != null) continue;

            var nextHw = _rawHardwareDevices.FirstOrDefault(d => !claimedInstances.Contains(d.InstanceKey));
            if (nextHw != null)
            {
                claimedInstances.Add(nextHw.InstanceKey);
                routed[p] = CloneDeviceForPort(nextHw, p);
            }
            else if (p == 0)
            {
                // Port 1 falls back to Keyboard if zero hardware controllers are assigned/connected
                routed[p] = CreateVirtualKeyboardDevice(0);
            }
            else
            {
                routed[p] = new ConnectedInputDevice { PortIndex = p, IsConnected = false };
            }
        }

        bool changed = false;
        for (int i = 0; i < 8; i++)
        {
            var newDev = routed[i] ?? new ConnectedInputDevice { PortIndex = i, IsConnected = false };
            var oldDev = _ports[i];

            if (!newDev.IsConnected)
            {
                if (oldDev.IsConnected || !string.Equals(oldDev.DeviceId, newDev.DeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    _ports[i] = newDev;
                    changed = true;
                }
                continue;
            }

            bool newlyConnected = !oldDev.IsConnected
                || !string.Equals(oldDev.DeviceId, newDev.DeviceId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(oldDev.InstanceKey, newDev.InstanceKey, StringComparison.OrdinalIgnoreCase);

            var prof = GetOrCreateProfile(newDev.DeviceId, newDev.DisplayName, newDev.FormFactor, newDev.VendorId, newDev.ProductId, newDev.IsXInput);
            newDev.Profile = prof;
            newDev.FormFactor = prof.FormFactor;
            newDev.EffectiveLayer = ResolveEffectiveLayer(newDev.DeviceId, _activeCoreId, _activeGameId, newDev.IsXInput);
            _ports[i] = newDev;

            if (newlyConnected)
            {
                changed = true;
                if (forceNotify)
                {
                    string coreTag = !string.IsNullOrWhiteSpace(_activeCoreId) ? $" - профиль ядра {_activeCoreId}" : "";
                    string msg = $"Подключён контроллер «{newDev.DisplayName}» (Порт {i + 1}){coreTag}";
                    OnDeviceProfileActivated?.Invoke(newDev, msg);
                }
            }
        }

        return changed;
    }

    private static ConnectedInputDevice CreateVirtualKeyboardDevice(int port)
    {
        return new ConnectedInputDevice
        {
            PortIndex = port,
            InstanceKey = "KBD",
            DeviceId = port == 0 ? "KBD_0000_0001" : $"KBD_PLAYER_{port + 1}",
            DisplayName = port == 0 ? "Клавиатура и мышь ПК" : $"Клавиатура (Игрок {port + 1})",
            Manufacturer = "PC HID",
            AutoconfigSource = "Keyboard Engine",
            VendorId = 0,
            ProductId = 0,
            IsXInput = false,
            IsKeyboardVirtual = true,
            FormFactor = InputDeviceFormFactor.KeyboardMouse,
            IsConnected = true
        };
    }

    private static ConnectedInputDevice CloneDeviceForPort(ConnectedInputDevice src, int portIndex)
    {
        return new ConnectedInputDevice
        {
            PortIndex = portIndex,
            InstanceKey = src.InstanceKey,
            DeviceId = src.DeviceId,
            DisplayName = src.DisplayName,
            Manufacturer = src.Manufacturer,
            AutoconfigSource = src.AutoconfigSource,
            VendorId = src.VendorId,
            ProductId = src.ProductId,
            IsXInput = src.IsXInput,
            IsKeyboardVirtual = src.IsKeyboardVirtual,
            XInputIndex = src.XInputIndex,
            WinMmIndex = src.WinMmIndex,
            AxisCount = src.AxisCount,
            ButtonCount = src.ButtonCount,
            HasPov = src.HasPov,
            AxisCapsMask = src.AxisCapsMask,
            AxisMin = (uint[])src.AxisMin.Clone(),
            AxisMax = (uint[])src.AxisMax.Clone(),
            SupportsRumble = src.SupportsRumble,
            FormFactor = src.FormFactor,
            IsConnected = true
        };
    }

    /// <summary>
    /// Уведомление о смене активного ядра Libretro или игры для автоматической активации среза профиля.
    /// </summary>
    public static void NotifyCoreOrGameChanged(string? coreId, string? gameId)
    {
        lock (_syncLock)
        {
            _activeCoreId = string.IsNullOrWhiteSpace(coreId) ? null : coreId.Trim().ToLowerInvariant();
            _activeGameId = string.IsNullOrWhiteSpace(gameId) ? null : gameId.Trim();

            for (int i = 0; i < 8; i++)
            {
                var dev = _ports[i];
                if (dev.IsConnected && !string.IsNullOrWhiteSpace(dev.DeviceId))
                {
                    dev.EffectiveLayer = ResolveEffectiveLayer(dev.DeviceId, _activeCoreId, _activeGameId, dev.IsXInput);
                    string coreTag = _activeCoreId != null ? $" - профиль ядра {_activeCoreId}" : "";
                    string gameTag = _activeGameId != null ? $" / {_activeGameId}" : "";
                    string msg = $"Активирован профиль контроллера «{dev.DisplayName}»{coreTag}{gameTag}";
                    OnDeviceProfileActivated?.Invoke(dev, msg);
                }
            }

            ApplyCompiledMapsToNativeHost();
        }
    }

    /// <summary>
    /// Автоматическое переключение активного первичного устройства (Auto-Handoff).
    /// </summary>
    public static void NotifyDeviceActivityOnPort(int portIndex)
    {
        lock (_syncLock)
        {
            if (portIndex >= 0 && portIndex < 8 && portIndex != _primaryPortIndex)
            {
                var dev = _ports[portIndex];
                if (dev.IsConnected)
                {
                    _primaryPortIndex = portIndex;
                    ApplyCompiledMapsToNativeHost();
                    string msg = $"Авто-перехват управления: активен порт {portIndex + 1} «{dev.DisplayName}»";
                    OnDeviceProfileActivated?.Invoke(dev, msg);
                }
            }
        }
    }

    /// <summary>
    /// Разрешение эффективного слоя: Global <- CoreLayers[activeCoreId] <- GameLayers[activeGameId].
    /// </summary>
    public static BindingLayer ResolveEffectiveLayer(string deviceId, string? activeCoreId, string? activeGameId, bool isXInput = true)
    {
        lock (_syncLock)
        {
            if (!_profileCache.TryGetValue(deviceId, out var profile))
            {
                profile = GetOrCreateProfile(deviceId, deviceId, InputDeviceFormFactor.XboxGamepad, 0, 0, isXInput);
            }

            var result = profile.GlobalLayer.Clone();

            if (!string.IsNullOrWhiteSpace(activeCoreId))
            {
                string normCore = activeCoreId.Trim().ToLowerInvariant();
                if (profile.CoreLayers.TryGetValue(normCore, out var coreLayer))
                {
                    MergeLayerInto(result, coreLayer);
                }
            }

            if (!string.IsNullOrWhiteSpace(activeGameId))
            {
                string normGame = activeGameId.Trim();
                if (profile.GameLayers.TryGetValue(normGame, out var gameLayer))
                {
                    MergeLayerInto(result, gameLayer);
                }
            }

            return result;
        }
    }

    private static void MergeLayerInto(BindingLayer target, BindingLayer overrideLayer)
    {
        target.AnalogDeadzonePercent = overrideLayer.AnalogDeadzonePercent;
        target.SteeringDeadzonePercent = overrideLayer.SteeringDeadzonePercent;
        target.TriggerThresholdPercent = overrideLayer.TriggerThresholdPercent;
        target.AnalogSensitivity = overrideLayer.AnalogSensitivity;
        target.RumbleStrengthPercent = overrideLayer.RumbleStrengthPercent;
        target.TurboButtonMask = overrideLayer.TurboButtonMask;

        if (overrideLayer.HotkeyEnableBinding != null)
        {
            target.HotkeyEnableBinding = new PhysicalInputBinding(
                overrideLayer.HotkeyEnableBinding.Type,
                overrideLayer.HotkeyEnableBinding.Code,
                overrideLayer.HotkeyEnableBinding.Parameter,
                overrideLayer.HotkeyEnableBinding.DisplayLabel);
        }

        foreach (var (k, v) in overrideLayer.JoypadBindings)
        {
            target.JoypadBindings[k] = new PhysicalInputBinding(v.Type, v.Code, v.Parameter, v.DisplayLabel);
        }

        if (overrideLayer.AnalogBindings != null)
        {
            target.AnalogBindings ??= new Dictionary<int, PhysicalInputBinding>();
            foreach (var (k, v) in overrideLayer.AnalogBindings)
            {
                target.AnalogBindings[k] = new PhysicalInputBinding(v.Type, v.Code, v.Parameter, v.DisplayLabel);
            }
        }

        foreach (var (k, v) in overrideLayer.DeviceHotkeys)
        {
            target.DeviceHotkeys[k] = new PhysicalInputBinding(v.Type, v.Code, v.Parameter, v.DisplayLabel);
        }
    }

    public static DeviceInputProfile GetOrCreateProfile(
        string deviceId,
        string displayName,
        InputDeviceFormFactor formFactor,
        ushort vid = 0,
        ushort pid = 0,
        bool isXInput = true)
    {
        lock (_syncLock)
        {
            if (_profileCache.TryGetValue(deviceId, out var existing))
            {
                UpgradeGenericProfileMetadataIfNeeded(existing, displayName, formFactor, vid, pid, isXInput);
                return existing;
            }

            string safeId = GetSafeDeviceId(deviceId);
            string filePath = Path.Combine(DevicesDir, $"{safeId}.json");

            if (File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    var loaded = JsonSerializer.Deserialize<DeviceInputProfile>(json);
                    if (loaded != null)
                    {
                        UpgradeGenericProfileMetadataIfNeeded(loaded, displayName, formFactor, vid, pid, isXInput);
                        _profileCache[deviceId] = loaded;
                        return loaded;
                    }
                }
                catch { }
            }

            var profile = CreateDefaultProfileForFormFactor(deviceId, displayName, formFactor, vid, pid, isXInput);
            _profileCache[deviceId] = profile;
            SaveProfile(profile);
            return profile;
        }
    }

    private static void UpgradeGenericProfileMetadataIfNeeded(
        DeviceInputProfile profile,
        string freshDisplayName,
        InputDeviceFormFactor freshFormFactor,
        ushort vid,
        ushort pid,
        bool isXInput)
    {
        bool changed = false;
        if ((profile.VendorId == 0 && vid != 0) || (profile.ProductId == 0 && pid != 0))
        {
            profile.VendorId = vid;
            profile.ProductId = pid;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(freshDisplayName) &&
            !freshDisplayName.Equals(profile.DeviceId, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(profile.DisplayName) ||
             ControllerHardwareDatabase.IsGenericOsDriverName(profile.DisplayName) ||
             profile.DisplayName.StartsWith("Джойстик HID", StringComparison.OrdinalIgnoreCase) ||
             (profile.DisplayName.StartsWith("Xbox Wireless Controller", StringComparison.OrdinalIgnoreCase) && vid != 0x045E)))
        {
            profile.DisplayName = freshDisplayName;
            profile.FormFactor = freshFormFactor;
            changed = true;
        }

        if (profile.GlobalLayer.AnalogBindings == null || profile.GlobalLayer.AnalogBindings.Count == 0)
        {
            var fresh = CreateDefaultProfileForFormFactor(profile.DeviceId, profile.DisplayName, profile.FormFactor, vid, pid, isXInput);
            profile.GlobalLayer.AnalogBindings = fresh.GlobalLayer.AnalogBindings;
            changed = true;
        }

        if (changed)
        {
            SaveProfile(profile);
        }
    }

    public static List<DeviceInputProfile> GetAllStoredProfiles()
    {
        lock (_syncLock)
        {
            LoadAllStoredProfiles();
            return _profileCache.Values.ToList();
        }
    }

    public static void SaveProfile(DeviceInputProfile profile)
    {
        lock (_syncLock)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.DeviceId)) return;
            profile.LastConnectedUtc = DateTime.UtcNow;
            _profileCache[profile.DeviceId] = profile;

            try
            {
                EnsureDirectories();
                string safeId = GetSafeDeviceId(profile.DeviceId);
                string filePath = Path.Combine(DevicesDir, $"{safeId}.json");
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(profile, options);
                File.WriteAllText(filePath, json);
            }
            catch { }
        }
    }

    private static void LoadAllStoredProfiles()
    {
        EnsureDirectories();
        try
        {
            foreach (var f in Directory.GetFiles(DevicesDir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(f);
                    var p = JsonSerializer.Deserialize<DeviceInputProfile>(json);
                    if (p != null && !string.IsNullOrWhiteSpace(p.DeviceId))
                    {
                        _profileCache[p.DeviceId] = p;
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    public static DeviceInputProfile CreateDefaultProfileForFormFactor(
        string deviceId,
        string displayName,
        InputDeviceFormFactor formFactor,
        ushort vid,
        ushort pid,
        bool isXInput = true)
    {
        var prof = new DeviceInputProfile
        {
            DeviceId = deviceId,
            DisplayName = displayName,
            VendorId = vid,
            ProductId = pid,
            FormFactor = formFactor,
            LastConnectedUtc = DateTime.UtcNow,
            AutoLoadOnConnect = true,
            GlobalLayer = new BindingLayer { LayerName = "Global" }
        };

        var layer = prof.GlobalLayer;

        if (formFactor == InputDeviceFormFactor.KeyboardMouse)
        {
            layer.JoypadBindings[0] = new PhysicalInputBinding(PhysicalInputType.KeyboardKey, 0x5A, 0, "Z (B)");
            layer.JoypadBindings[1] = new PhysicalInputBinding(PhysicalInputType.KeyboardKey, 0x41, 0, "A (Y)");
            layer.JoypadBindings[2] = new PhysicalInputBinding(PhysicalInputType.KeyboardKey, 0xA0, 0, "Shift (Select)");
            layer.JoypadBindings[3] = new PhysicalInputBinding(PhysicalInputType.KeyboardKey, 0x0D, 0, "Enter (Start)");
            layer.JoypadBindings[4] = new PhysicalInputBinding(PhysicalInputType.KeyboardKey, 0x26, 0, "Стрелка Вверх");
            layer.JoypadBindings[5] = new PhysicalInputBinding(PhysicalInputType.KeyboardKey, 0x28, 0, "Стрелка Вниз");
            layer.JoypadBindings[6] = new PhysicalInputBinding(PhysicalInputType.KeyboardKey, 0x25, 0, "Стрелка Влево");
            layer.JoypadBindings[7] = new PhysicalInputBinding(PhysicalInputType.KeyboardKey, 0x27, 0, "Стрелка Вправо");
            layer.JoypadBindings[8] = new PhysicalInputBinding(PhysicalInputType.KeyboardKey, 0x58, 0, "X (A)");
            layer.JoypadBindings[9] = new PhysicalInputBinding(PhysicalInputType.KeyboardKey, 0x53, 0, "S (X)");
            layer.JoypadBindings[10] = new PhysicalInputBinding(PhysicalInputType.KeyboardKey, 0x51, 0, "Q (L1)");
            layer.JoypadBindings[11] = new PhysicalInputBinding(PhysicalInputType.KeyboardKey, 0x57, 0, "W (R1)");
            layer.JoypadBindings[12] = new PhysicalInputBinding(PhysicalInputType.KeyboardKey, 0x45, 0, "E (L2)");
            layer.JoypadBindings[13] = new PhysicalInputBinding(PhysicalInputType.KeyboardKey, 0x52, 0, "R (R2)");
            layer.JoypadBindings[14] = new PhysicalInputBinding(PhysicalInputType.KeyboardKey, 0x43, 0, "C (L3)");
            layer.JoypadBindings[15] = new PhysicalInputBinding(PhysicalInputType.KeyboardKey, 0x56, 0, "V (R3)");
            return prof;
        }

        if (!isXInput)
        {
            // DirectInput / HID (Wheels, HOTAS, DualSense USB, Switch USB, Arcade Sticks, etc.)
            var autoCfg = ControllerHardwareDatabase.TryResolveWinMmAutoconfig(vid, pid, displayName);
            if (autoCfg != null)
            {
                foreach (var (k, v) in autoCfg.JoypadBindings) layer.JoypadBindings[k] = v;
                foreach (var (k, v) in autoCfg.AnalogBindings) layer.AnalogBindings[k] = v;
            }

            if (layer.JoypadBindings.Count == 0)
            {
                if (formFactor == InputDeviceFormFactor.RacingWheel)
                {
                    layer.SteeringDeadzonePercent = 4;
                    layer.AnalogDeadzonePercent = 5;
                    layer.JoypadBindings[0] = new PhysicalInputBinding(PhysicalInputType.WinMmButton, 0, 0, "Кнопка 1");
                    layer.JoypadBindings[8] = new PhysicalInputBinding(PhysicalInputType.WinMmButton, 1, 0, "Кнопка 2");
                    layer.JoypadBindings[1] = new PhysicalInputBinding(PhysicalInputType.WinMmButton, 2, 0, "Кнопка 3");
                    layer.JoypadBindings[9] = new PhysicalInputBinding(PhysicalInputType.WinMmButton, 3, 0, "Кнопка 4");
                    layer.JoypadBindings[10] = new PhysicalInputBinding(PhysicalInputType.WinMmButton, 4, 0, "Левый лепесток");
                    layer.JoypadBindings[11] = new PhysicalInputBinding(PhysicalInputType.WinMmButton, 5, 0, "Правый лепесток");
                    layer.JoypadBindings[2] = new PhysicalInputBinding(PhysicalInputType.WinMmButton, 8, 0, "Select");
                    layer.JoypadBindings[3] = new PhysicalInputBinding(PhysicalInputType.WinMmButton, 9, 0, "Start");
                    layer.JoypadBindings[4] = new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 0, "POV Вверх");
                    layer.JoypadBindings[5] = new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 18000, "POV Вниз");
                    layer.JoypadBindings[6] = new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 27000, "POV Влево");
                    layer.JoypadBindings[7] = new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 9000, "POV Вправо");
                    layer.JoypadBindings[12] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, 3, -1, "Педаль тормоза");
                    layer.JoypadBindings[13] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, 1, -1, "Педаль газа");
                }
                else
                {
                    layer.JoypadBindings[0] = new PhysicalInputBinding(PhysicalInputType.WinMmButton, 0, 0, "Кнопка 1 (B)");
                    layer.JoypadBindings[8] = new PhysicalInputBinding(PhysicalInputType.WinMmButton, 1, 0, "Кнопка 2 (A)");
                    layer.JoypadBindings[1] = new PhysicalInputBinding(PhysicalInputType.WinMmButton, 2, 0, "Кнопка 3 (Y)");
                    layer.JoypadBindings[9] = new PhysicalInputBinding(PhysicalInputType.WinMmButton, 3, 0, "Кнопка 4 (X)");
                    layer.JoypadBindings[10] = new PhysicalInputBinding(PhysicalInputType.WinMmButton, 4, 0, "Кнопка 5 (L1)");
                    layer.JoypadBindings[11] = new PhysicalInputBinding(PhysicalInputType.WinMmButton, 5, 0, "Кнопка 6 (R1)");
                    layer.JoypadBindings[12] = new PhysicalInputBinding(PhysicalInputType.WinMmButton, 6, 0, "Кнопка 7 (L2)");
                    layer.JoypadBindings[13] = new PhysicalInputBinding(PhysicalInputType.WinMmButton, 7, 0, "Кнопка 8 (R2)");
                    layer.JoypadBindings[2] = new PhysicalInputBinding(PhysicalInputType.WinMmButton, 8, 0, "Кнопка 9 (Select)");
                    layer.JoypadBindings[3] = new PhysicalInputBinding(PhysicalInputType.WinMmButton, 9, 0, "Кнопка 10 (Start)");
                    layer.JoypadBindings[14] = new PhysicalInputBinding(PhysicalInputType.WinMmButton, 10, 0, "Кнопка 11 (L3)");
                    layer.JoypadBindings[15] = new PhysicalInputBinding(PhysicalInputType.WinMmButton, 11, 0, "Кнопка 12 (R3)");
                    layer.JoypadBindings[4] = new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 0, "POV Вверх");
                    layer.JoypadBindings[5] = new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 18000, "POV Вниз");
                    layer.JoypadBindings[6] = new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 27000, "POV Влево");
                    layer.JoypadBindings[7] = new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 9000, "POV Вправо");
                }
            }

            if (layer.AnalogBindings.Count == 0)
            {
                if (formFactor == InputDeviceFormFactor.RacingWheel)
                {
                    layer.AnalogBindings[100] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, 0, -1, "Руль Влево (Ось X -)");
                    layer.AnalogBindings[101] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, 0, +1, "Руль Вправо (Ось X +)");
                    layer.AnalogBindings[106] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, 1, -1, "Педаль газа (Ось Y -)");
                    layer.AnalogBindings[107] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, 3, +1, "Педаль тормоза (Ось R +)");
                }
                else
                {
                    layer.AnalogBindings[100] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, 0, -1, "Ось X (Стик) -");
                    layer.AnalogBindings[101] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, 0, +1, "Ось X (Стик) +");
                    layer.AnalogBindings[102] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, 1, -1, "Ось Y (Вертикаль) -");
                    layer.AnalogBindings[103] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, 1, +1, "Ось Y (Вертикаль) +");
                    layer.AnalogBindings[104] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, 2, -1, "Ось Z / РУД -");
                    layer.AnalogBindings[105] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, 2, +1, "Ось Z / РУД +");
                    layer.AnalogBindings[106] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, 3, -1, "Ось R / Твист -");
                    layer.AnalogBindings[107] = new PhysicalInputBinding(PhysicalInputType.WinMmAxis, 3, +1, "Ось R / Твист +");
                }
            }

            layer.HotkeyEnableBinding = layer.JoypadBindings.TryGetValue(2, out var selB)
                ? new PhysicalInputBinding(selB.Type, selB.Code, selB.Parameter, selB.DisplayLabel)
                : new PhysicalInputBinding(PhysicalInputType.WinMmButton, 8, 0, "Кнопка 9 (Select)");
            return prof;
        }

        // Standard XInput / Flydigi / Xbox / DualSense XInput-emulated Layout
        layer.JoypadBindings[0] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x1000, 0, "Кнопка A (Низ)");
        layer.JoypadBindings[1] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x4000, 0, "Кнопка X (Лево)");
        layer.JoypadBindings[2] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0020, 0, "Back / Select");
        layer.JoypadBindings[3] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0010, 0, "Start");
        layer.JoypadBindings[4] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0001, 0, "D-Pad Вверх");
        layer.JoypadBindings[5] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0002, 0, "D-Pad Вниз");
        layer.JoypadBindings[6] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0004, 0, "D-Pad Влево");
        layer.JoypadBindings[7] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0008, 0, "D-Pad Вправо");
        layer.JoypadBindings[8] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x2000, 0, "Кнопка B (Право)");
        layer.JoypadBindings[9] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x8000, 0, "Кнопка Y (Верх)");
        layer.JoypadBindings[10] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0100, 0, "Бампер LB");
        layer.JoypadBindings[11] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0200, 0, "Бампер RB");
        layer.JoypadBindings[12] = new PhysicalInputBinding(PhysicalInputType.XInputTrigger, 0, 12, "Триггер LT");
        layer.JoypadBindings[13] = new PhysicalInputBinding(PhysicalInputType.XInputTrigger, 1, 12, "Триггер RT");
        layer.JoypadBindings[14] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0040, 0, "Левый стик L3");
        layer.JoypadBindings[15] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0080, 0, "Правый стик R3");

        layer.AnalogBindings[100] = new PhysicalInputBinding(PhysicalInputType.XInputAxis, 0, -1, "Левый стик: Влево");
        layer.AnalogBindings[101] = new PhysicalInputBinding(PhysicalInputType.XInputAxis, 0, +1, "Левый стик: Вправо");
        layer.AnalogBindings[102] = new PhysicalInputBinding(PhysicalInputType.XInputAxis, 1, +1, "Левый стик: Вверх");
        layer.AnalogBindings[103] = new PhysicalInputBinding(PhysicalInputType.XInputAxis, 1, -1, "Левый стик: Вниз");
        layer.AnalogBindings[104] = new PhysicalInputBinding(PhysicalInputType.XInputAxis, 2, -1, "Правый стик: Влево");
        layer.AnalogBindings[105] = new PhysicalInputBinding(PhysicalInputType.XInputAxis, 2, +1, "Правый стик: Вправо");
        layer.AnalogBindings[106] = new PhysicalInputBinding(PhysicalInputType.XInputAxis, 3, +1, "Правый стик: Вверх");
        layer.AnalogBindings[107] = new PhysicalInputBinding(PhysicalInputType.XInputAxis, 3, -1, "Правый стик: Вниз");

        if (formFactor == InputDeviceFormFactor.RacingWheel)
        {
            layer.SteeringDeadzonePercent = 4;
            layer.AnalogDeadzonePercent = 5;
        }

        layer.HotkeyEnableBinding = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0020, 0, "Back / Select");
        layer.DeviceHotkeys["quick_menu"] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0010, 0, "Start");
        layer.DeviceHotkeys["save_state"] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0200, 0, "Бампер RB");
        layer.DeviceHotkeys["load_state"] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0100, 0, "Бампер LB");
        layer.DeviceHotkeys["pause_toggle"] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x1000, 0, "Кнопка A");
        layer.DeviceHotkeys["reset_game"] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x2000, 0, "Кнопка B");
        layer.DeviceHotkeys["screenshot"] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x8000, 0, "Кнопка Y");
        layer.DeviceHotkeys["instant_replay"] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x4000, 0, "Кнопка X");
        layer.DeviceHotkeys["state_slot_next"] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0001, 0, "D-Pad Вверх");
        layer.DeviceHotkeys["state_slot_prev"] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0002, 0, "D-Pad Вниз");
        layer.DeviceHotkeys["rewind_hold"] = new PhysicalInputBinding(PhysicalInputType.XInputTrigger, 0, 12, "Триггер LT");
        layer.DeviceHotkeys["fast_forward_hold"] = new PhysicalInputBinding(PhysicalInputType.XInputTrigger, 1, 12, "Триггер RT");
        layer.DeviceHotkeys["fast_forward_toggle"] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0080, 0, "Правый стик R3");
        layer.DeviceHotkeys["fullscreen_toggle"] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0040, 0, "Левый стик L3");
        layer.DeviceHotkeys["frame_advance"] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0008, 0, "D-Pad Вправо");

        return prof;
    }

    private static void SeedPresetProfilesIfEmpty()
    {
        try
        {
            EnsureDirectories();
            var files = Directory.GetFiles(DevicesDir, "*.json");
            if (files.Length > 0) return;

            var xbox = CreateDefaultProfileForFormFactor("VID_045E_PID_02FD", "Xbox Wireless Controller", InputDeviceFormFactor.XboxGamepad, 0x045E, 0x02FD, true);
            SaveProfile(xbox);

            var ps = CreateDefaultProfileForFormFactor("VID_054C_PID_0CE6", "Sony DualSense Wireless Controller", InputDeviceFormFactor.PlayStationPad, 0x054C, 0x0CE6, false);
            SaveProfile(ps);

            var hori = CreateDefaultProfileForFormFactor("VID_0F0D_PID_017C", "Hori Fighting Stick Alpha (Vewlix 8-Btn)", InputDeviceFormFactor.ArcadeFightStick, 0x0F0D, 0x017C, true);
            var fbneoLayer = hori.GlobalLayer.Clone();
            fbneoLayer.LayerName = "FBNeo Arcade";
            fbneoLayer.JoypadBindings[9] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x4000, 0, "LP (X)");
            fbneoLayer.JoypadBindings[1] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x8000, 0, "MP (Y)");
            fbneoLayer.JoypadBindings[11] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0200, 0, "HP (RB)");
            fbneoLayer.JoypadBindings[0] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x1000, 0, "LK (A)");
            fbneoLayer.JoypadBindings[8] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x2000, 0, "MK (B)");
            fbneoLayer.JoypadBindings[13] = new PhysicalInputBinding(PhysicalInputType.XInputTrigger, 1, 12, "HK (RT)");
            hori.CoreLayers["fbneo"] = fbneoLayer;
            SaveProfile(hori);

            var hotas = CreateDefaultProfileForFormFactor("VID_044F_PID_B10A", "Thrustmaster T.16000M FCS HOTAS", InputDeviceFormFactor.FlightJoystickHotas, 0x044F, 0xB10A, false);
            SaveProfile(hotas);

            var wheel = CreateDefaultProfileForFormFactor("VID_046D_PID_C24F", "Logitech G29 Driving Force Racing Wheel", InputDeviceFormFactor.RacingWheel, 0x046D, 0xC24F, false);
            SaveProfile(wheel);

            var m30 = CreateDefaultProfileForFormFactor("VID_2DC8_PID_2840", "8BitDo M30 Bluetooth Gamepad (6 кнопок)", InputDeviceFormFactor.SegaSixButton, 0x2DC8, 0x2840, false);
            var mdLayer = m30.GlobalLayer.Clone();
            mdLayer.LayerName = "Genesis Plus GX";
            m30.CoreLayers["genesis_plus_gx"] = mdLayer;
            m30.CoreLayers["picodrive"] = mdLayer;
            SaveProfile(m30);
        }
        catch { }
    }

    /// <summary>
    /// Фиксирует текущее состояние покоя всех XInput/WinMM устройств перед началом захвата,
    /// чтобы руль, педали с обратным отсчётом или смещённые стики не срабатывали ложно.
    /// </summary>
    public static void BeginInputCaptureSnapshot()
    {
        lock (_syncLock)
        {
            InitXInput();
            for (uint xi = 0; xi < 4; xi++)
            {
                _baselineXInputValid[xi] = TryGetXInputState(xi, out _baselineXInput[xi]);
            }

            int numDevs = 0;
            try { numDevs = joyGetNumDevs(); } catch { }
            uint joyInfoSize = (uint)Marshal.SizeOf<JOYINFOEX>();
            uint joyCapSize = (uint)Marshal.SizeOf<JOYCAPSW>();

            for (uint wi = 0; wi < 16; wi++)
            {
                _baselineWinMmValid[wi] = false;
                if (wi >= (uint)numDevs) continue;

                var joyInfo = new JOYINFOEX { dwSize = joyInfoSize, dwFlags = JOY_RETURNALL };
                if (joyGetPosEx(wi, ref joyInfo) == JOYERR_NOERROR)
                {
                    _baselineWinMm[wi] = joyInfo;
                    _baselineWinMmValid[wi] = true;
                    joyGetDevCapsW((UIntPtr)wi, out _baselineWinMmCaps[wi], joyCapSize);
                }
            }
        }
    }

    public static PhysicalInputBinding? PollAnyPhysicalInputForCapture() => PollAnyPhysicalInputForCapture(null);

    /// <summary>
    /// Опрос физических контроллеров относительно снимка покоя (XInput + WinMM кнопки/POV/все 6 осей).
    /// </summary>
    public static PhysicalInputBinding? PollAnyPhysicalInputForCapture(int? preferredPort)
    {
        lock (_syncLock)
        {
            InitXInput();

            int? prefXInput = null;
            int? prefWinMm = null;
            if (preferredPort.HasValue && preferredPort.Value >= 0 && preferredPort.Value < 8)
            {
                var pDev = _ports[preferredPort.Value];
                if (pDev.IsConnected)
                {
                    if (pDev.IsXInput) prefXInput = (int)pDev.XInputIndex;
                    else if (!pDev.IsKeyboardVirtual) prefWinMm = (int)pDev.WinMmIndex;
                }
            }

            // 1. Scan XInput 0..3 (prioritize preferredPort)
            var xiOrder = Enumerable.Range(0, 4).OrderBy(x => prefXInput == x ? 0 : 1);
            foreach (int xi in xiOrder)
            {
                if (!TryGetXInputState((uint)xi, out var st)) continue;
                var gp = st.Gamepad;
                var baseGp = _baselineXInputValid[xi] ? _baselineXInput[xi].Gamepad : default;

                ushort newlyPressed = (ushort)(gp.wButtons & ~baseGp.wButtons);
                ushort checkButtons = newlyPressed != 0 ? newlyPressed : (_baselineXInputValid[xi] ? (ushort)0 : gp.wButtons);
                if (checkButtons != 0)
                {
                    ushort[] masks = { 0x1000, 0x2000, 0x4000, 0x8000, 0x0100, 0x0200, 0x0010, 0x0020, 0x0040, 0x0080, 0x0400, 0x0001, 0x0002, 0x0004, 0x0008 };
                    foreach (var m in masks)
                    {
                        if ((checkButtons & m) != 0)
                        {
                            return new PhysicalInputBinding(PhysicalInputType.XInputButton, m);
                        }
                    }
                }

                if (gp.bLeftTrigger > 110 && baseGp.bLeftTrigger < 60)
                    return new PhysicalInputBinding(PhysicalInputType.XInputTrigger, 0, 12, "Левый триггер LT");
                if (gp.bRightTrigger > 110 && baseGp.bRightTrigger < 60)
                    return new PhysicalInputBinding(PhysicalInputType.XInputTrigger, 1, 12, "Правый триггер RT");

                const short stickThresh = 22000;
                if (gp.sThumbLX > stickThresh && Math.Abs(gp.sThumbLX - baseGp.sThumbLX) > 10000)
                    return new PhysicalInputBinding(PhysicalInputType.XInputAxis, 0, +1, "Левый стик: Вправо");
                if (gp.sThumbLX < -stickThresh && Math.Abs(gp.sThumbLX - baseGp.sThumbLX) > 10000)
                    return new PhysicalInputBinding(PhysicalInputType.XInputAxis, 0, -1, "Левый стик: Влево");
                if (gp.sThumbLY > stickThresh && Math.Abs(gp.sThumbLY - baseGp.sThumbLY) > 10000)
                    return new PhysicalInputBinding(PhysicalInputType.XInputAxis, 1, +1, "Левый стик: Вверх");
                if (gp.sThumbLY < -stickThresh && Math.Abs(gp.sThumbLY - baseGp.sThumbLY) > 10000)
                    return new PhysicalInputBinding(PhysicalInputType.XInputAxis, 1, -1, "Левый стик: Вниз");

                if (gp.sThumbRX > stickThresh && Math.Abs(gp.sThumbRX - baseGp.sThumbRX) > 10000)
                    return new PhysicalInputBinding(PhysicalInputType.XInputAxis, 2, +1, "Правый стик: Вправо");
                if (gp.sThumbRX < -stickThresh && Math.Abs(gp.sThumbRX - baseGp.sThumbRX) > 10000)
                    return new PhysicalInputBinding(PhysicalInputType.XInputAxis, 2, -1, "Правый стик: Влево");
                if (gp.sThumbRY > stickThresh && Math.Abs(gp.sThumbRY - baseGp.sThumbRY) > 10000)
                    return new PhysicalInputBinding(PhysicalInputType.XInputAxis, 3, +1, "Правый стик: Вверх");
                if (gp.sThumbRY < -stickThresh && Math.Abs(gp.sThumbRY - baseGp.sThumbRY) > 10000)
                    return new PhysicalInputBinding(PhysicalInputType.XInputAxis, 3, -1, "Правый стик: Вниз");
            }

            // 2. Scan WinMM devices (skip XInput OS wrappers when preferred XInput isn't WinMM)
            int numDevs = 0;
            try { numDevs = joyGetNumDevs(); } catch { }
            int maxWinMm = Math.Min(numDevs, 16);
            var wiOrder = Enumerable.Range(0, maxWinMm).OrderBy(w => prefWinMm == w ? 0 : 1);
            uint joyInfoSize = (uint)Marshal.SizeOf<JOYINFOEX>();

            foreach (int wi in wiOrder)
            {
                if (_baselineWinMmValid[wi] &&
                    ControllerHardwareDatabase.IsXInputBackedDevice(_baselineWinMmCaps[wi].wMid, _baselineWinMmCaps[wi].wPid, null, _baselineWinMmCaps[wi].szPname))
                {
                    continue;
                }

                var joyInfo = new JOYINFOEX { dwSize = joyInfoSize, dwFlags = JOY_RETURNALL };
                if (joyGetPosEx((uint)wi, ref joyInfo) != JOYERR_NOERROR) continue;

                var baseInfo = _baselineWinMmValid[wi] ? _baselineWinMm[wi] : default;
                uint newBtns = _baselineWinMmValid[wi] ? (joyInfo.dwButtons & ~baseInfo.dwButtons) : joyInfo.dwButtons;
                if (newBtns != 0)
                {
                    for (int bi = 0; bi < 32; bi++)
                    {
                        if ((newBtns & (1u << bi)) != 0)
                        {
                            return new PhysicalInputBinding(PhysicalInputType.WinMmButton, bi, 0, $"Кнопка {bi + 1}");
                        }
                    }
                }

                if (joyInfo.dwPOV != 0xFFFF && joyInfo.dwPOV <= 35900 && joyInfo.dwPOV != baseInfo.dwPOV)
                {
                    uint p = joyInfo.dwPOV;
                    if (p >= 31500 || p <= 4500) return new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 0, "POV Вверх");
                    if (p >= 4500 && p <= 13500) return new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 9000, "POV Вправо");
                    if (p >= 13500 && p <= 22500) return new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 18000, "POV Вниз");
                    if (p >= 22500 && p <= 31500) return new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 27000, "POV Влево");
                }

                // Scan all 6 WinMM axes (X, Y, Z, R, U, V) using baseline-delta detection
                if (_baselineWinMmValid[wi])
                {
                    var caps = _baselineWinMmCaps[wi];
                    uint[] curAxes = { joyInfo.dwXpos, joyInfo.dwYpos, joyInfo.dwZpos, joyInfo.dwRpos, joyInfo.dwUpos, joyInfo.dwVpos };
                    uint[] baseAxes = { baseInfo.dwXpos, baseInfo.dwYpos, baseInfo.dwZpos, baseInfo.dwRpos, baseInfo.dwUpos, baseInfo.dwVpos };
                    uint[] minAxes = { caps.wXmin, caps.wYmin, caps.wZmin, caps.wRmin, caps.wUmin, caps.wVmin };
                    uint[] maxAxes = { caps.wXmax, caps.wYmax, caps.wZmax, caps.wRmax, caps.wUmax, caps.wVmax };

                    for (int ax = 0; ax < 6; ax++)
                    {
                        if (maxAxes[ax] <= minAxes[ax]) continue;
                        double range = maxAxes[ax] - minAxes[ax];
                        double normCur = ((curAxes[ax] - minAxes[ax]) / range) * 2.0 - 1.0;
                        double normBase = ((baseAxes[ax] - minAxes[ax]) / range) * 2.0 - 1.0;
                        double delta = normCur - normBase;
                        if (Math.Abs(delta) > 0.42)
                        {
                            int dir = delta > 0 ? +1 : -1;
                            return new PhysicalInputBinding(
                                PhysicalInputType.WinMmAxis,
                                ax,
                                dir,
                                $"Ось {ControllerHardwareDatabase.GetWinMmAxisName(ax)} {(dir > 0 ? "+" : "-")}");
                        }
                    }
                }
            }

            return null;
        }
    }

    public static void ApplyCompiledMapsToNativeHost()
    {
        try
        {
            NativeLibretroHost.Current.RebuildCompiledInputMaps();
        }
        catch { }
    }
}
