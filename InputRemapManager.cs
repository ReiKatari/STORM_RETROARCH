using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;

namespace StormRetroArch;

public class RemapButtonBinding : INotifyPropertyChanged
{
    private string _boundKey = "---";
    private string _boundBtn = "---";

    public string ButtonName { get; set; } = "";
    public int RetroId { get; set; } = -1;
    public bool IsAnalogAxis { get; set; } = false;
    public string BindingCategory { get; set; } = "Buttons";
    public string DisplayLabel { get; set; } = "";
    public string RetroPadRole { get; set; } = "";
    public string CfgKey { get; set; } = "";

    public string BadgeText { get; set; } = "B";
    public string BadgeBg { get; set; } = "#258B5CF6";
    public string BadgeBorder { get; set; } = "#8B5CF6";
    public string BadgeFg { get; set; } = "#FFFFFF";

    public string IconPathData { get; set; } = "M12 2A10 10 0 0 0 2 12a10 10 0 0 0 10 10 10 10 0 0 0 10-10A10 10 0 0 0 12 2z";
    public string IconColor { get; set; } = "#8B5CF6";

    public string BoundKey
    {
        get => _boundKey;
        set
        {
            if (_boundKey != value)
            {
                _boundKey = value;
                OnPropertyChanged();
            }
        }
    }

    public string BoundBtn
    {
        get => _boundBtn;
        set
        {
            if (_boundBtn != value)
            {
                _boundBtn = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class InputRemapManager
{
    public static readonly string RemapsDir = Path.Combine(CoreManager.AppDir, "remaps");

    static InputRemapManager()
    {
        try
        {
            if (!Directory.Exists(RemapsDir)) Directory.CreateDirectory(RemapsDir);
        }
        catch { }
    }

    public static List<RemapButtonBinding> CreateDefaultBindings(int playerIndex = 1)
    {
        return CreateUnifiedStudioBindings(Math.Max(0, playerIndex - 1), null, null, InputDeviceFormFactor.XboxGamepad, includeAnalog: false);
    }

    public static List<RemapButtonBinding> CreateUnifiedStudioBindings(
        int portIndex,
        DeviceInputProfile? profile,
        BindingLayer? layer,
        InputDeviceFormFactor formFactor = InputDeviceFormFactor.XboxGamepad,
        bool includeAnalog = true)
    {
        int playerIndex = Math.Clamp(portIndex + 1, 1, 8);
        string p = $"input_player{playerIndex}_";
        bool isWheel = formFactor == InputDeviceFormFactor.RacingWheel;
        bool isFlight = formFactor == InputDeviceFormFactor.FlightJoystickHotas;

        var list = new List<RemapButtonBinding>
        {
            new() { RetroId = 0, BindingCategory = "Buttons", ButtonName = "b", DisplayLabel = isWheel ? "Лепесток / B (Низ)" : "Кнопка B (Низ)", RetroPadRole = "Действие / Отмена / Прыжок", CfgKey = p + "b", BadgeText = "B", BadgeBg = "#25F43F5E", BadgeBorder = "#F43F5E", BadgeFg = "#FCA5A5", IconColor = "#F43F5E" },
            new() { RetroId = 1, BindingCategory = "Buttons", ButtonName = "y", DisplayLabel = isWheel ? "Ручник / Y (Лево)" : "Кнопка Y (Лево)", RetroPadRole = "Атака / Ускорение / Магия", CfgKey = p + "y", BadgeText = "Y", BadgeBg = "#2538BDF8", BadgeBorder = "#38BDF8", BadgeFg = "#7DD3FC", IconColor = "#38BDF8" },
            new() { RetroId = 2, BindingCategory = "Buttons", ButtonName = "select", DisplayLabel = "Select (Выбор)", RetroPadRole = "Переключение предметов / Модификатор", CfgKey = p + "select", BadgeText = "SEL", BadgeBg = "#2564748B", BadgeBorder = "#64748B", BadgeFg = "#E2E8F0", IconColor = "#64748B" },
            new() { RetroId = 3, BindingCategory = "Buttons", ButtonName = "start", DisplayLabel = "Start (Старт)", RetroPadRole = "Пауза / Главное меню игры", CfgKey = p + "start", BadgeText = "STA", BadgeBg = "#2564748B", BadgeBorder = "#64748B", BadgeFg = "#E2E8F0", IconColor = "#64748B" },
            new() { RetroId = 4, BindingCategory = "Buttons", ButtonName = "up", DisplayLabel = "Крестовина Вверх", RetroPadRole = "Направление вверх / POV", CfgKey = p + "up", BadgeText = "▲", BadgeBg = "#258B5CF6", BadgeBorder = "#8B5CF6", BadgeFg = "#C4B5FD", IconColor = "#8B5CF6" },
            new() { RetroId = 5, BindingCategory = "Buttons", ButtonName = "down", DisplayLabel = "Крестовина Вниз", RetroPadRole = "Направление вниз / POV", CfgKey = p + "down", BadgeText = "▼", BadgeBg = "#258B5CF6", BadgeBorder = "#8B5CF6", BadgeFg = "#C4B5FD", IconColor = "#8B5CF6" },
            new() { RetroId = 6, BindingCategory = "Buttons", ButtonName = "left", DisplayLabel = "Крестовина Влево", RetroPadRole = "Направление влево / POV", CfgKey = p + "left", BadgeText = "◄", BadgeBg = "#258B5CF6", BadgeBorder = "#8B5CF6", BadgeFg = "#C4B5FD", IconColor = "#8B5CF6" },
            new() { RetroId = 7, BindingCategory = "Buttons", ButtonName = "right", DisplayLabel = "Крестовина Вправо", RetroPadRole = "Направление вправо / POV", CfgKey = p + "right", BadgeText = "►", BadgeBg = "#258B5CF6", BadgeBorder = "#8B5CF6", BadgeFg = "#C4B5FD", IconColor = "#8B5CF6" },
            new() { RetroId = 8, BindingCategory = "Buttons", ButtonName = "a", DisplayLabel = isFlight ? "Огонь / A (Право)" : "Кнопка A (Право)", RetroPadRole = "Подтверждение / Основной огонь", CfgKey = p + "a", BadgeText = "A", BadgeBg = "#2510B981", BadgeBorder = "#10B981", BadgeFg = "#6EE7B7", IconColor = "#10B981" },
            new() { RetroId = 9, BindingCategory = "Buttons", ButtonName = "x", DisplayLabel = "Кнопка X (Верх)", RetroPadRole = "Дополнительное действие / Инвентарь", CfgKey = p + "x", BadgeText = "X", BadgeBg = "#25A78BFA", BadgeBorder = "#A78BFA", BadgeFg = "#DDD6FE", IconColor = "#A78BFA" },
            new() { RetroId = 10, BindingCategory = "Buttons", ButtonName = "l", DisplayLabel = isWheel ? "Левый подрулевой (L1)" : "Левый шифт L1 / LB", RetroPadRole = "Передача - / Блок", CfgKey = p + "l", BadgeText = "L1", BadgeBg = "#25EC4899", BadgeBorder = "#EC4899", BadgeFg = "#F472B6", IconColor = "#EC4899" },
            new() { RetroId = 11, BindingCategory = "Buttons", ButtonName = "r", DisplayLabel = isWheel ? "Правый подрулевой (R1)" : "Правый шифт R1 / RB", RetroPadRole = "Передача + / Прицел", CfgKey = p + "r", BadgeText = "R1", BadgeBg = "#25EC4899", BadgeBorder = "#EC4899", BadgeFg = "#F472B6", IconColor = "#EC4899" },
            new() { RetroId = 12, BindingCategory = "Buttons", ButtonName = "l2", DisplayLabel = isWheel ? "Педаль тормоза (L2)" : isFlight ? "Твист / РУД - (L2)" : "Левый триггер L2 / LT", RetroPadRole = "Аналоговый курок / Педаль тормоза", CfgKey = p + "l2", BadgeText = "L2", BadgeBg = "#25A855F7", BadgeBorder = "#A855F7", BadgeFg = "#C084FC", IconColor = "#A855F7" },
            new() { RetroId = 13, BindingCategory = "Buttons", ButtonName = "r2", DisplayLabel = isWheel ? "Педаль газа (R2)" : isFlight ? "РУД + / Форсаж (R2)" : "Правый триггер R2 / RT", RetroPadRole = "Аналоговый курок / Педаль газа", CfgKey = p + "r2", BadgeText = "R2", BadgeBg = "#25A855F7", BadgeBorder = "#A855F7", BadgeFg = "#C084FC", IconColor = "#A855F7" },
            new() { RetroId = 14, BindingCategory = "Buttons", ButtonName = "l3", DisplayLabel = "Нажатие стика L3", RetroPadRole = "Бег / Камера / Клаксон", CfgKey = p + "l3", BadgeText = "L3", BadgeBg = "#2506B6D4", BadgeBorder = "#06B6D4", BadgeFg = "#67E8F9", IconColor = "#06B6D4" },
            new() { RetroId = 15, BindingCategory = "Buttons", ButtonName = "r3", DisplayLabel = "Нажатие стика R3", RetroPadRole = "Присед / Зеркало / Обзор", CfgKey = p + "r3", BadgeText = "R3", BadgeBg = "#2506B6D4", BadgeBorder = "#06B6D4", BadgeFg = "#67E8F9", IconColor = "#06B6D4" }
        };

        if (includeAnalog)
        {
            list.AddRange(new RemapButtonBinding[]
            {
                new() { RetroId = 100, IsAnalogAxis = true, BindingCategory = "Analog", ButtonName = "l_x_minus", DisplayLabel = isWheel ? "Руль: Поворот влево" : "Левый стик: Влево (-X)", RetroPadRole = isWheel ? "Аналоговая ось руля влево" : "Аналоговая ось L-Stick X-", CfgKey = p + "l_x_minus", BadgeText = "L◄", BadgeBg = "#2538BDF8", BadgeBorder = "#38BDF8", BadgeFg = "#7DD3FC", IconColor = "#38BDF8" },
                new() { RetroId = 101, IsAnalogAxis = true, BindingCategory = "Analog", ButtonName = "l_x_plus", DisplayLabel = isWheel ? "Руль: Поворот вправо" : "Левый стик: Вправо (+X)", RetroPadRole = isWheel ? "Аналоговая ось руля вправо" : "Аналоговая ось L-Stick X+", CfgKey = p + "l_x_plus", BadgeText = "L►", BadgeBg = "#2538BDF8", BadgeBorder = "#38BDF8", BadgeFg = "#7DD3FC", IconColor = "#38BDF8" },
                new() { RetroId = 102, IsAnalogAxis = true, BindingCategory = "Analog", ButtonName = "l_y_minus", DisplayLabel = isFlight ? "РУС: От себя (-Y)" : "Левый стик: Вверх (-Y)", RetroPadRole = "Аналоговая ось L-Stick Y-", CfgKey = p + "l_y_minus", BadgeText = "L▲", BadgeBg = "#2538BDF8", BadgeBorder = "#38BDF8", BadgeFg = "#7DD3FC", IconColor = "#38BDF8" },
                new() { RetroId = 103, IsAnalogAxis = true, BindingCategory = "Analog", ButtonName = "l_y_plus", DisplayLabel = isFlight ? "РУС: На себя (+Y)" : "Левый стик: Вниз (+Y)", RetroPadRole = "Аналоговая ось L-Stick Y+", CfgKey = p + "l_y_plus", BadgeText = "L▼", BadgeBg = "#2538BDF8", BadgeBorder = "#38BDF8", BadgeFg = "#7DD3FC", IconColor = "#38BDF8" },
                new() { RetroId = 104, IsAnalogAxis = true, BindingCategory = "Analog", ButtonName = "r_x_minus", DisplayLabel = isWheel ? "Педаль тормоза (Ось -)" : isFlight ? "Рыскание / Твист (-RX)" : "Правый стик: Влево (-RX)", RetroPadRole = "Аналоговая ось R-Stick X-", CfgKey = p + "r_x_minus", BadgeText = "R◄", BadgeBg = "#2510B981", BadgeBorder = "#10B981", BadgeFg = "#6EE7B7", IconColor = "#10B981" },
                new() { RetroId = 105, IsAnalogAxis = true, BindingCategory = "Analog", ButtonName = "r_x_plus", DisplayLabel = isWheel ? "Педаль сцепления (Ось +)" : isFlight ? "Рыскание / Твист (+RX)" : "Правый стик: Вправо (+RX)", RetroPadRole = "Аналоговая ось R-Stick X+", CfgKey = p + "r_x_plus", BadgeText = "R►", BadgeBg = "#2510B981", BadgeBorder = "#10B981", BadgeFg = "#6EE7B7", IconColor = "#10B981" },
                new() { RetroId = 106, IsAnalogAxis = true, BindingCategory = "Analog", ButtonName = "r_y_minus", DisplayLabel = isWheel ? "Педаль газа (Ось -RY)" : isFlight ? "Тяга РУД вперед (-RY)" : "Правый стик: Вверх (-RY)", RetroPadRole = "Аналоговая ось R-Stick Y-", CfgKey = p + "r_y_minus", BadgeText = "R▲", BadgeBg = "#2510B981", BadgeBorder = "#10B981", BadgeFg = "#6EE7B7", IconColor = "#10B981" },
                new() { RetroId = 107, IsAnalogAxis = true, BindingCategory = "Analog", ButtonName = "r_y_plus", DisplayLabel = isWheel ? "Тормоз / Задний ход (+RY)" : isFlight ? "Тяга РУД назад (+RY)" : "Правый стик: Вниз (+RY)", RetroPadRole = "Аналоговая ось R-Stick Y+", CfgKey = p + "r_y_plus", BadgeText = "R▼", BadgeBg = "#2510B981", BadgeBorder = "#10B981", BadgeFg = "#6EE7B7", IconColor = "#10B981" }
            });
        }

        var playerKbd = (SettingsManager.Current?.PlayerKeyboardBindings != null &&
                         SettingsManager.Current.PlayerKeyboardBindings.TryGetValue(portIndex, out var pk))
            ? pk
            : null;

        foreach (var b in list)
        {
            // Keyboard slot resolution: Settings per-player -> retroarch.cfg -> Layer KeyboardKey -> port defaults
            string? kVal = (playerKbd != null && playerKbd.TryGetValue(b.RetroId, out var kCustom)) ? kCustom : null;
            if (string.IsNullOrWhiteSpace(kVal))
                kVal = RetroArchConfigManager.GetString(b.CfgKey, "");
            if (string.IsNullOrWhiteSpace(kVal) && layer != null)
            {
                var srcDict = b.IsAnalogAxis ? layer.AnalogBindings : layer.JoypadBindings;
                if (srcDict != null && srcDict.TryGetValue(b.RetroId, out var pb) && pb.Type == PhysicalInputType.KeyboardKey)
                    kVal = pb.DisplayLabel;
            }
            b.BoundKey = string.IsNullOrWhiteSpace(kVal)
                ? (portIndex == 0 ? GetDefaultKeyboardKey(b.ButtonName) : GetDefaultKeyboardKeyPort2(portIndex, b.ButtonName))
                : kVal;

            // Physical controller slot resolution: Active Device Layer -> retroarch.cfg -> defaults
            string? btnVal = null;
            if (layer != null)
            {
                var srcDict = b.IsAnalogAxis ? layer.AnalogBindings : layer.JoypadBindings;
                if (srcDict != null && srcDict.TryGetValue(b.RetroId, out var pb) &&
                    pb.Type != PhysicalInputType.None && pb.Type != PhysicalInputType.KeyboardKey)
                {
                    btnVal = pb.DisplayLabel;
                }
            }
            if (string.IsNullOrWhiteSpace(btnVal))
                btnVal = RetroArchConfigManager.GetString(b.CfgKey + "_btn", "");
            b.BoundBtn = string.IsNullOrWhiteSpace(btnVal) ? GetDefaultGamepadBtn(b.ButtonName) : btnVal;
        }

        return list;
    }

    private static string GetDefaultKeyboardKeyPort2(int portIndex, string button)
    {
        if (portIndex == 1)
        {
            return button switch
            {
                "up" => "num8", "down" => "num2", "left" => "num4", "right" => "num6",
                "b" => "j", "a" => "k", "y" => "u", "x" => "i",
                "l" => "o", "r" => "p", "start" => "num0", "select" => "num5",
                _ => "---"
            };
        }
        return "---";
    }

    public static string ConvertKeyToRetroArch(Key key)
    {
        return key switch
        {
            Key.Z => "z",
            Key.X => "x",
            Key.A => "a",
            Key.S => "s",
            Key.Q => "q",
            Key.W => "w",
            Key.E => "e",
            Key.R => "r",
            Key.D => "d",
            Key.C => "c",
            Key.V => "v",
            Key.F => "f",
            Key.Up => "up",
            Key.Down => "down",
            Key.Left => "left",
            Key.Right => "right",
            Key.Enter => "enter",
            Key.RightShift => "rshift",
            Key.LeftShift => "shift",
            Key.LeftCtrl => "ctrl",
            Key.RightCtrl => "rctrl",
            Key.LeftAlt => "alt",
            Key.RightAlt => "ralt",
            Key.Space => "space",
            Key.Tab => "tab",
            Key.Back => "backspace",
            Key.Delete => "del",
            Key.Insert => "insert",
            Key.Home => "home",
            Key.End => "end",
            Key.PageUp => "pageup",
            Key.PageDown => "pagedown",
            Key.NumPad0 => "num0",
            Key.NumPad1 => "num1",
            Key.NumPad2 => "num2",
            Key.NumPad3 => "num3",
            Key.NumPad4 => "num4",
            Key.NumPad5 => "num5",
            Key.NumPad6 => "num6",
            Key.NumPad7 => "num7",
            Key.NumPad8 => "num8",
            Key.NumPad9 => "num9",
            _ => key.ToString().ToLowerInvariant()
        };
    }

    public static void SaveBindingsToConfig(IEnumerable<RemapButtonBinding> bindings)
    {
        foreach (var b in bindings)
        {
            RetroArchConfigManager.SetString(b.CfgKey, b.BoundKey);
            RetroArchConfigManager.SetString(b.CfgKey + "_btn", b.BoundBtn);
        }
        RetroArchConfigManager.Save();
    }

    public static void ExportRemapFile(string coreOrGameName, IEnumerable<RemapButtonBinding> bindings)
    {
        string safeName = Path.GetFileNameWithoutExtension(coreOrGameName);
        string targetDir = Path.Combine(RemapsDir, safeName);
        if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

        string remapPath = Path.Combine(targetDir, $"{safeName}.rmp");
        var sb = new StringBuilder();
        sb.AppendLine("# STORM RETROARCH Remap Profile");
        sb.AppendLine($"# Generated for: {safeName}");
        sb.AppendLine();

        foreach (var b in bindings)
        {
            sb.AppendLine($"{b.CfgKey} = \"{b.BoundKey}\"");
            sb.AppendLine($"{b.CfgKey}_btn = \"{b.BoundBtn}\"");
        }

        File.WriteAllText(remapPath, sb.ToString());
    }

    public static void ResetToRetroPadDefaults(IEnumerable<RemapButtonBinding> bindings)
    {
        foreach (var b in bindings)
        {
            b.BoundKey = GetDefaultKeyboardKey(b.ButtonName);
            b.BoundBtn = GetDefaultGamepadBtn(b.ButtonName);
        }
        SaveBindingsToConfig(bindings);
    }

    private static string GetDefaultKeyboardKey(string button) => button switch
    {
        "b" => "z",
        "y" => "a",
        "select" => "rshift",
        "start" => "enter",
        "up" => "up",
        "down" => "down",
        "left" => "left",
        "right" => "right",
        "a" => "x",
        "x" => "s",
        "l" => "q",
        "r" => "w",
        "l2" => "e",
        "r2" => "r",
        "l3" => "c",
        "r3" => "v",
        _ => "---"
    };

    private static string GetDefaultGamepadBtn(string button) => button switch
    {
        "b" => "0",
        "y" => "2",
        "select" => "6",
        "start" => "7",
        "up" => "h0up",
        "down" => "h0down",
        "left" => "h0left",
        "right" => "h0right",
        "a" => "1",
        "x" => "3",
        "l" => "4",
        "r" => "5",
        "l2" => "+2",
        "r2" => "+5",
        "l3" => "8",
        "r3" => "9",
        _ => "---"
    };

    public static void ImportRemapFile(string filePath, IEnumerable<RemapButtonBinding>? bindings = null)
    {
        if (!File.Exists(filePath)) return;
        try
        {
            var dict = new Dictionary<string, string>();
            var lines = File.ReadAllLines(filePath);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
                var parts = trimmed.Split('=', 2);
                if (parts.Length == 2)
                {
                    string key = parts[0].Trim();
                    string val = parts[1].Trim().Trim('"');
                    dict[key] = val;
                    RetroArchConfigManager.SetString(key, val);
                }
            }
            RetroArchConfigManager.Save();

            if (bindings != null)
            {
                foreach (var b in bindings)
                {
                    if (dict.TryGetValue(b.CfgKey, out var kVal)) b.BoundKey = kVal;
                    if (dict.TryGetValue(b.CfgKey + "_btn", out var bVal)) b.BoundBtn = bVal;
                }
            }
        }
        catch { }
    }

}
