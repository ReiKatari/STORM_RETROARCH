using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
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
/// Физическая привязка кнопки, оси, курка или шляпки POV к виртуальной кнопке Libretro Joypad.
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
            _ => $"POV {param / 100}°"
        },
        PhysicalInputType.WinMmAxis => $"Ось {code} {(param > 0 ? "+" : "-")}",
        PhysicalInputType.KeyboardKey => $"Клавиша 0x{code:X2}",
        _ => "Не назначено"
    };
}

/// <summary>
/// Слой раскладки кнопок (Глобальный, Для ядра или Для конкретной игры).
/// </summary>
public class BindingLayer
{
    public string LayerName { get; set; } = "Global";
    public Dictionary<int, PhysicalInputBinding> JoypadBindings { get; set; } = new();
    public int AnalogDeadzonePercent { get; set; } = 15;
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
    public string DeviceId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public ushort VendorId { get; set; }
    public ushort ProductId { get; set; }
    public bool IsXInput { get; set; }
    public uint XInputIndex { get; set; }
    public uint WinMmIndex { get; set; }
    public InputDeviceFormFactor FormFactor { get; set; } = InputDeviceFormFactor.XboxGamepad;
    public bool IsConnected { get; set; }
    public DeviceInputProfile? Profile { get; set; }
    public BindingLayer? EffectiveLayer { get; set; }
}

/// <summary>
/// Высокопроизводительный движок обнаружения устройств, горячего подключения
/// и автоматического переключения профилей контроллеров для STORM RETROARCH.
/// </summary>
public static class DeviceInputProfileEngine
{
    private static readonly object _syncLock = new();
    private static readonly Dictionary<string, DeviceInputProfile> _profileCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConnectedInputDevice[] _ports = new ConnectedInputDevice[8];
    private static int _primaryPortIndex = 0;
    private static string? _activeCoreId = null;
    private static string? _activeGameId = null;
    private static bool _initialized = false;

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

    // XInput P/Invoke
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
    private static bool _xinputLoaded = false;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpLibFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

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

        string[] dlls = { "xinput1_4.dll", "xinput9_1_0.dll", "xinput1_3.dll" };
        foreach (var dll in dlls)
        {
            IntPtr hMod = LoadLibrary(dll);
            if (hMod != IntPtr.Zero)
            {
                IntPtr pFunc = GetProcAddress(hMod, "XInputGetState");
                if (pFunc != IntPtr.Zero)
                {
                    _xinputGetState = Marshal.GetDelegateForFunctionPointer<XInputGetStateDelegate>(pFunc);
                    break;
                }
            }
        }
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
        string n = (name ?? "").ToUpperInvariant();

        if (n.Contains("KBD") || n.Contains("KEYBOARD") || n.Contains("КЛАВИАТУРА") || n.Contains("MOUSE") || n.Contains("МЫШЬ"))
            return InputDeviceFormFactor.KeyboardMouse;

        if (vid == 0x054C || n.Contains("DUALSENSE") || n.Contains("DUALSHOCK") || n.Contains("PLAYSTATION") || n.Contains("PS5") || n.Contains("PS4") || n.Contains("PS3") || n.Contains("SONY"))
            return InputDeviceFormFactor.PlayStationPad;

        if (vid == 0x057E || n.Contains("SWITCH") || n.Contains("PRO CONTROLLER") || n.Contains("JOY-CON"))
            return InputDeviceFormFactor.NintendoPad;

        if (n.Contains("FIGHTSTICK") || n.Contains("ARCADE") || n.Contains("HORI") || n.Contains("QANBA") || n.Contains("MAYFLASH") || n.Contains("SANWA") || n.Contains("ALPHA"))
            return InputDeviceFormFactor.ArcadeFightStick;

        if (n.Contains("HITBOX") || n.Contains("SNACKBOX") || n.Contains("HAUTE42") || n.Contains("LEVERLESS") || n.Contains("FLATBOX"))
            return InputDeviceFormFactor.LeverlessHitBox;

        if (n.Contains("HOTAS") || n.Contains("THRUSTMASTER") || n.Contains("FLIGHT") || n.Contains("EXTREME 3D") || n.Contains("VKB") || n.Contains("T.16000M") || n.Contains("WARTHOG"))
            return InputDeviceFormFactor.FlightJoystickHotas;

        if (n.Contains("WHEEL") || n.Contains("G29") || n.Contains("G920") || n.Contains("G923") || n.Contains("FANATEC") || n.Contains("T300") || n.Contains("РУЛЬ") || n.Contains("PEDAL"))
            return InputDeviceFormFactor.RacingWheel;

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

    public static ConnectedInputDevice GetPort(int portIndex)
    {
        lock (_syncLock)
        {
            if (portIndex < 0 || portIndex >= 8) return _ports[0];
            return _ports[portIndex];
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

    /// <summary>
    /// Периодический опрос подключённых контроллеров (XInput + WinMM) и авто-подключение.
    /// </summary>
    public static void PollDevicesAndHotplug()
    {
        lock (_syncLock)
        {
            InitXInput();

            var discovered = new List<ConnectedInputDevice>();
            var claimedWinMm = new HashSet<uint>();

            // 1. Scan XInput 0..3
            if (_xinputGetState != null)
            {
                for (uint xi = 0; xi < 4; xi++)
                {
                    if (_xinputGetState(xi, out var st) == 0)
                    {
                        string devId = $"XINPUT_PORT_{xi}";
                        string dispName = $"Xbox Wireless Controller (Порт {xi + 1})";
                        ushort vid = 0x045E;
                        ushort pid = 0x02FD;

                        // Try to enrich XInput with WinMM registry OEM info
                        int winNum = 0;
                        try { winNum = joyGetNumDevs(); } catch { }
                        for (uint w = 0; w < (uint)Math.Min(winNum, 16); w++)
                        {
                            if (claimedWinMm.Contains(w)) continue;
                            if (joyGetDevCapsW((UIntPtr)w, out var caps, (uint)Marshal.SizeOf<JOYCAPSW>()) == JOYERR_NOERROR)
                            {
                                string pname = caps.szPname ?? "";
                                if (pname.Contains("XBOX", StringComparison.OrdinalIgnoreCase) ||
                                    pname.Contains("XInput", StringComparison.OrdinalIgnoreCase) ||
                                    caps.wMid == 0x045E)
                                {
                                    claimedWinMm.Add(w);
                                    vid = caps.wMid;
                                    pid = caps.wPid;
                                    string? oem = LookupRegistryOemName(vid, pid);
                                    if (!string.IsNullOrWhiteSpace(oem)) dispName = oem;
                                    else if (!string.IsNullOrWhiteSpace(pname)) dispName = pname;
                                    devId = $"VID_{vid:X4}_PID_{pid:X4}";
                                    break;
                                }
                            }
                        }

                        var dev = new ConnectedInputDevice
                        {
                            PortIndex = discovered.Count,
                            DeviceId = devId,
                            DisplayName = dispName,
                            VendorId = vid,
                            ProductId = pid,
                            IsXInput = true,
                            XInputIndex = xi,
                            FormFactor = ClassifyFormFactor(dispName, vid, pid),
                            IsConnected = true
                        };
                        discovered.Add(dev);
                    }
                }
            }

            // 2. Scan WinMM DirectInput / HID Joysticks
            int numDevs = 0;
            try { numDevs = joyGetNumDevs(); } catch { }
            for (uint wi = 0; wi < (uint)Math.Min(numDevs, 16); wi++)
            {
                if (discovered.Count >= 8) break;
                if (claimedWinMm.Contains(wi)) continue;

                var joyInfo = new JOYINFOEX { dwSize = (uint)Marshal.SizeOf<JOYINFOEX>(), dwFlags = JOY_RETURNBUTTONS };
                if (joyGetPosEx(wi, ref joyInfo) == JOYERR_NOERROR)
                {
                    ushort vid = 0;
                    ushort pid = 0;
                    string dispName = $"Джойстик HID #{wi + 1}";

                    if (joyGetDevCapsW((UIntPtr)wi, out var caps, (uint)Marshal.SizeOf<JOYCAPSW>()) == JOYERR_NOERROR)
                    {
                        vid = caps.wMid;
                        pid = caps.wPid;
                        string? oem = LookupRegistryOemName(vid, pid);
                        if (!string.IsNullOrWhiteSpace(oem)) dispName = oem;
                        else if (!string.IsNullOrWhiteSpace(caps.szPname)) dispName = caps.szPname;
                    }

                    string devId = (vid != 0 || pid != 0) ? $"VID_{vid:X4}_PID_{pid:X4}" : $"WINMM_JOY_{wi}";

                    var dev = new ConnectedInputDevice
                    {
                        PortIndex = discovered.Count,
                        DeviceId = devId,
                        DisplayName = dispName,
                        VendorId = vid,
                        ProductId = pid,
                        IsXInput = false,
                        WinMmIndex = wi,
                        FormFactor = ClassifyFormFactor(dispName, vid, pid),
                        IsConnected = true
                    };
                    discovered.Add(dev);
                }
            }

            // 3. Fallback virtual Keyboard & Mouse device on port 0 if no controllers are connected
            if (discovered.Count == 0)
            {
                var kbd = new ConnectedInputDevice
                {
                    PortIndex = 0,
                    DeviceId = "KBD_0000_0001",
                    DisplayName = "Клавиатура и мышь ПК",
                    VendorId = 0,
                    ProductId = 0,
                    IsXInput = false,
                    FormFactor = InputDeviceFormFactor.KeyboardMouse,
                    IsConnected = true
                };
                discovered.Add(kbd);
            }

            // Update port array and check for connection transitions
            bool changed = false;
            for (int i = 0; i < 8; i++)
            {
                if (i < discovered.Count)
                {
                    var newDev = discovered[i];
                    newDev.PortIndex = i;
                    var oldDev = _ports[i];

                    bool newlyConnected = !oldDev.IsConnected || !string.Equals(oldDev.DeviceId, newDev.DeviceId, StringComparison.OrdinalIgnoreCase);

                    // Load or resolve profile
                    var prof = GetOrCreateProfile(newDev.DeviceId, newDev.DisplayName, newDev.FormFactor, newDev.VendorId, newDev.ProductId);
                    newDev.Profile = prof;
                    newDev.FormFactor = prof.FormFactor; // User override takes precedence
                    newDev.EffectiveLayer = ResolveEffectiveLayer(newDev.DeviceId, _activeCoreId, _activeGameId);
                    _ports[i] = newDev;

                    if (newlyConnected)
                    {
                        changed = true;
                        string coreTag = !string.IsNullOrWhiteSpace(_activeCoreId) ? $" - загружен профиль для ядра {_activeCoreId}" : "";
                        string msg = $"Подключён контроллер «{newDev.DisplayName}» (Порт {i + 1}){coreTag}";
                        OnDeviceProfileActivated?.Invoke(newDev, msg);
                    }
                }
                else
                {
                    if (_ports[i].IsConnected)
                    {
                        _ports[i] = new ConnectedInputDevice { PortIndex = i, IsConnected = false };
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                ApplyCompiledMapsToNativeHost();
                OnDevicesChanged?.Invoke();
            }
        }
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
                    dev.EffectiveLayer = ResolveEffectiveLayer(dev.DeviceId, _activeCoreId, _activeGameId);
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
    /// Автоматическое переключение активного первичного устройства (Auto-Handoff),
    /// если контроллер на неосновном порту нажал кнопку во время ожидания.
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
    public static BindingLayer ResolveEffectiveLayer(string deviceId, string? activeCoreId, string? activeGameId)
    {
        lock (_syncLock)
        {
            if (!_profileCache.TryGetValue(deviceId, out var profile))
            {
                profile = GetOrCreateProfile(deviceId, deviceId, InputDeviceFormFactor.XboxGamepad);
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

        foreach (var (k, v) in overrideLayer.DeviceHotkeys)
        {
            target.DeviceHotkeys[k] = new PhysicalInputBinding(v.Type, v.Code, v.Parameter, v.DisplayLabel);
        }
    }

    public static DeviceInputProfile GetOrCreateProfile(string deviceId, string displayName, InputDeviceFormFactor formFactor, ushort vid = 0, ushort pid = 0)
    {
        lock (_syncLock)
        {
            if (_profileCache.TryGetValue(deviceId, out var existing))
                return existing;

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
                        _profileCache[deviceId] = loaded;
                        return loaded;
                    }
                }
                catch { }
            }

            // Create default profile for this device
            var profile = CreateDefaultProfileForFormFactor(deviceId, displayName, formFactor, vid, pid);
            _profileCache[deviceId] = profile;
            SaveProfile(profile);
            return profile;
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

    public static DeviceInputProfile CreateDefaultProfileForFormFactor(string deviceId, string displayName, InputDeviceFormFactor formFactor, ushort vid, ushort pid)
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
            // Virtual Keyboard mappings (Z=B, X=A, A=Y, S=X, Q=L, W=R, Enter=Start, Shift=Select, Arrows=DPad)
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
        }
        else
        {
            // Standard XInput / Gamepad Layout
            layer.JoypadBindings[0] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x2000, 0, "Кнопка B");
            layer.JoypadBindings[1] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x4000, 0, "Кнопка X");
            layer.JoypadBindings[2] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0020, 0, "Back / Select");
            layer.JoypadBindings[3] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0010, 0, "Start");
            layer.JoypadBindings[4] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0001, 0, "D-Pad Вверх");
            layer.JoypadBindings[5] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0002, 0, "D-Pad Вниз");
            layer.JoypadBindings[6] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0004, 0, "D-Pad Влево");
            layer.JoypadBindings[7] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0008, 0, "D-Pad Вправо");
            layer.JoypadBindings[8] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x1000, 0, "Кнопка A");
            layer.JoypadBindings[9] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x8000, 0, "Кнопка Y");
            layer.JoypadBindings[10] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0100, 0, "Бампер LB");
            layer.JoypadBindings[11] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0200, 0, "Бампер RB");
            layer.JoypadBindings[12] = new PhysicalInputBinding(PhysicalInputType.XInputTrigger, 0, 12, "Триггер LT");
            layer.JoypadBindings[13] = new PhysicalInputBinding(PhysicalInputType.XInputTrigger, 1, 12, "Триггер RT");
            layer.JoypadBindings[14] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0040, 0, "Левый стик L3");
            layer.JoypadBindings[15] = new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0080, 0, "Правый стик R3");

            // Default Hotkey Chord Enable: Back/Select (0x0020)
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
        }

        return prof;
    }

    /// <summary>
    /// Автоматическое заполнение 6 эталонных пресетов на первом запуске.
    /// </summary>
    private static void SeedPresetProfilesIfEmpty()
    {
        try
        {
            EnsureDirectories();
            var files = Directory.GetFiles(DevicesDir, "*.json");
            if (files.Length > 0) return;

            // 1. Xbox Wireless Controller
            var xbox = CreateDefaultProfileForFormFactor("VID_045E_PID_02FD", "Xbox Wireless Controller", InputDeviceFormFactor.XboxGamepad, 0x045E, 0x02FD);
            SaveProfile(xbox);

            // 2. Sony DualSense Wireless Controller
            var ps = CreateDefaultProfileForFormFactor("VID_054C_PID_0CE6", "Sony DualSense Wireless Controller", InputDeviceFormFactor.PlayStationPad, 0x054C, 0x0CE6);
            ps.GlobalLayer.JoypadBindings[0].DisplayLabel = "Кнопка Крест ✕";
            ps.GlobalLayer.JoypadBindings[1].DisplayLabel = "Кнопка Квадрат □";
            ps.GlobalLayer.JoypadBindings[8].DisplayLabel = "Кнопка Круг ○";
            ps.GlobalLayer.JoypadBindings[9].DisplayLabel = "Кнопка Треугольник △";
            SaveProfile(ps);

            // 3. Hori Fighting Stick Alpha
            var hori = CreateDefaultProfileForFormFactor("VID_0F0D_PID_017C", "Hori Fighting Stick Alpha (Vewlix 8-Btn)", InputDeviceFormFactor.ArcadeFightStick, 0x0F0D, 0x017C);
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

            // 4. Thrustmaster T.16000M FCS HOTAS
            var hotas = CreateDefaultProfileForFormFactor("VID_044F_PID_B10A", "Thrustmaster T.16000M FCS HOTAS", InputDeviceFormFactor.FlightJoystickHotas, 0x044F, 0xB10A);
            SaveProfile(hotas);

            // 5. Logitech G29 Driving Force Racing Wheel
            var wheel = CreateDefaultProfileForFormFactor("VID_046D_PID_C24F", "Logitech G29 Driving Force Racing Wheel", InputDeviceFormFactor.RacingWheel, 0x046D, 0xC24F);
            SaveProfile(wheel);

            // 6. 8BitDo M30 6-Button Fighting Pad
            var m30 = CreateDefaultProfileForFormFactor("VID_2DC8_PID_2840", "8BitDo M30 Bluetooth Gamepad (6 кнопок)", InputDeviceFormFactor.SegaSixButton, 0x2DC8, 0x2840);
            var mdLayer = m30.GlobalLayer.Clone();
            mdLayer.LayerName = "Genesis Plus GX";
            m30.CoreLayers["genesis_plus_gx"] = mdLayer;
            m30.CoreLayers["picodrive"] = mdLayer;
            SaveProfile(m30);
        }
        catch { }
    }

    /// <summary>
    /// Опрос всех физических контроллеров для захвата любой нажатой кнопки, триггера, шляпки POV или оси в UI.
    /// </summary>
    public static PhysicalInputBinding? PollAnyPhysicalInputForCapture()
    {
        lock (_syncLock)
        {
            InitXInput();

            // 1. Scan XInput 0..3
            if (_xinputGetState != null)
            {
                for (uint xi = 0; xi < 4; xi++)
                {
                    if (_xinputGetState(xi, out var st) == 0)
                    {
                        var gp = st.Gamepad;
                        ushort b = gp.wButtons;
                        if (b != 0)
                        {
                            ushort[] masks = { 0x1000, 0x2000, 0x4000, 0x8000, 0x0100, 0x0200, 0x0010, 0x0020, 0x0040, 0x0080, 0x0001, 0x0002, 0x0004, 0x0008 };
                            foreach (var m in masks)
                            {
                                if ((b & m) != 0)
                                {
                                    return new PhysicalInputBinding(PhysicalInputType.XInputButton, m);
                                }
                            }
                        }

                        if (gp.bLeftTrigger > 80)
                            return new PhysicalInputBinding(PhysicalInputType.XInputTrigger, 0, 12, "Левый триггер LT");
                        if (gp.bRightTrigger > 80)
                            return new PhysicalInputBinding(PhysicalInputType.XInputTrigger, 1, 12, "Правый триггер RT");

                        const short stickThresh = 22000;
                        if (gp.sThumbLX > stickThresh) return new PhysicalInputBinding(PhysicalInputType.XInputAxis, 0, +1, "Левый стик: Вправо");
                        if (gp.sThumbLX < -stickThresh) return new PhysicalInputBinding(PhysicalInputType.XInputAxis, 0, -1, "Левый стик: Влево");
                        if (gp.sThumbLY > stickThresh) return new PhysicalInputBinding(PhysicalInputType.XInputAxis, 1, +1, "Левый стик: Вверх");
                        if (gp.sThumbLY < -stickThresh) return new PhysicalInputBinding(PhysicalInputType.XInputAxis, 1, -1, "Левый стик: Вниз");

                        if (gp.sThumbRX > stickThresh) return new PhysicalInputBinding(PhysicalInputType.XInputAxis, 2, +1, "Правый стик: Вправо");
                        if (gp.sThumbRX < -stickThresh) return new PhysicalInputBinding(PhysicalInputType.XInputAxis, 2, -1, "Правый стик: Влево");
                        if (gp.sThumbRY > stickThresh) return new PhysicalInputBinding(PhysicalInputType.XInputAxis, 3, +1, "Правый стик: Вверх");
                        if (gp.sThumbRY < -stickThresh) return new PhysicalInputBinding(PhysicalInputType.XInputAxis, 3, -1, "Правый стик: Вниз");
                    }
                }
            }

            // 2. Scan WinMM devices
            int numDevs = 0;
            try { numDevs = joyGetNumDevs(); } catch { }
            for (uint wi = 0; wi < (uint)Math.Min(numDevs, 16); wi++)
            {
                var joyInfo = new JOYINFOEX { dwSize = (uint)Marshal.SizeOf<JOYINFOEX>(), dwFlags = JOY_RETURNALL };
                if (joyGetPosEx(wi, ref joyInfo) == JOYERR_NOERROR)
                {
                    uint btns = joyInfo.dwButtons;
                    if (btns != 0)
                    {
                        for (int bi = 0; bi < 32; bi++)
                        {
                            if ((btns & (1u << bi)) != 0)
                            {
                                return new PhysicalInputBinding(PhysicalInputType.WinMmButton, bi, 0, $"Кнопка {bi + 1}");
                            }
                        }
                    }

                    if (joyInfo.dwPOV != 0xFFFF && joyInfo.dwPOV <= 35900)
                    {
                        uint p = joyInfo.dwPOV;
                        if (p >= 31500 || p <= 4500) return new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 0, "POV Вверх");
                        if (p >= 4500 && p <= 13500) return new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 9000, "POV Вправо");
                        if (p >= 13500 && p <= 22500) return new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 18000, "POV Вниз");
                        if (p >= 22500 && p <= 31500) return new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 27000, "POV Влево");
                    }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Синхронизация скомпилированных раскладок для хоста NativeLibretroHost.
    /// </summary>
    public static void ApplyCompiledMapsToNativeHost()
    {
        try
        {
            NativeLibretroHost.Current.RebuildCompiledInputMaps();
        }
        catch { }
    }
}
