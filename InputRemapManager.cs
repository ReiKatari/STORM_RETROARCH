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
        string p = $"input_player{playerIndex}_";

        var list = new List<RemapButtonBinding>
        {
            new() { ButtonName = "b", DisplayLabel = "Кнопка B (Низ)", RetroPadRole = "Действие / Отмена / Прыжок", CfgKey = p + "b", BadgeText = "B", BadgeBg = "#25F43F5E", BadgeBorder = "#F43F5E", BadgeFg = "#FCA5A5", IconColor = "#F43F5E" },
            new() { ButtonName = "y", DisplayLabel = "Кнопка Y (Лево)", RetroPadRole = "Атака / Ускорение / Магия", CfgKey = p + "y", BadgeText = "Y", BadgeBg = "#2538BDF8", BadgeBorder = "#38BDF8", BadgeFg = "#7DD3FC", IconColor = "#38BDF8" },
            new() { ButtonName = "select", DisplayLabel = "Select (Выбор)", RetroPadRole = "Переключение предметов / Карта", CfgKey = p + "select", BadgeText = "SEL", BadgeBg = "#2564748B", BadgeBorder = "#64748B", BadgeFg = "#E2E8F0", IconColor = "#64748B" },
            new() { ButtonName = "start", DisplayLabel = "Start (Старт)", RetroPadRole = "Пауза / Главное меню игры", CfgKey = p + "start", BadgeText = "STA", BadgeBg = "#2564748B", BadgeBorder = "#64748B", BadgeFg = "#E2E8F0", IconColor = "#64748B" },
            new() { ButtonName = "up", DisplayLabel = "Крестовина Вверх", RetroPadRole = "Направление вверх", CfgKey = p + "up", BadgeText = "▲", BadgeBg = "#258B5CF6", BadgeBorder = "#8B5CF6", BadgeFg = "#C4B5FD", IconColor = "#8B5CF6" },
            new() { ButtonName = "down", DisplayLabel = "Крестовина Вниз", RetroPadRole = "Направление вниз / Присесть", CfgKey = p + "down", BadgeText = "▼", BadgeBg = "#258B5CF6", BadgeBorder = "#8B5CF6", BadgeFg = "#C4B5FD", IconColor = "#8B5CF6" },
            new() { ButtonName = "left", DisplayLabel = "Крестовина Влево", RetroPadRole = "Направление влево", CfgKey = p + "left", BadgeText = "◄", BadgeBg = "#258B5CF6", BadgeBorder = "#8B5CF6", BadgeFg = "#C4B5FD", IconColor = "#8B5CF6" },
            new() { ButtonName = "right", DisplayLabel = "Крестовина Вправо", RetroPadRole = "Направление вправо", CfgKey = p + "right", BadgeText = "►", BadgeBg = "#258B5CF6", BadgeBorder = "#8B5CF6", BadgeFg = "#C4B5FD", IconColor = "#8B5CF6" },
            new() { ButtonName = "a", DisplayLabel = "Кнопка A (Право)", RetroPadRole = "Подтверждение / Огонь", CfgKey = p + "a", BadgeText = "A", BadgeBg = "#2510B981", BadgeBorder = "#10B981", BadgeFg = "#6EE7B7", IconColor = "#10B981" },
            new() { ButtonName = "x", DisplayLabel = "Кнопка X (Верх)", RetroPadRole = "Дополнительное действие / Инвентарь", CfgKey = p + "x", BadgeText = "X", BadgeBg = "#25A78BFA", BadgeBorder = "#A78BFA", BadgeFg = "#DDD6FE", IconColor = "#A78BFA" },
            new() { ButtonName = "l", DisplayLabel = "Левый шифт L1", RetroPadRole = "Смена оружия влево / Блок", CfgKey = p + "l", BadgeText = "L1", BadgeBg = "#25EC4899", BadgeBorder = "#EC4899", BadgeFg = "#F472B6", IconColor = "#EC4899" },
            new() { ButtonName = "r", DisplayLabel = "Правый шифт R1", RetroPadRole = "Смена оружия вправо / Прицел", CfgKey = p + "r", BadgeText = "R1", BadgeBg = "#25EC4899", BadgeBorder = "#EC4899", BadgeFg = "#F472B6", IconColor = "#EC4899" },
            new() { ButtonName = "l2", DisplayLabel = "Левый триггер L2", RetroPadRole = "Аналоговый газ / Тормоз / Спецудар", CfgKey = p + "l2", BadgeText = "L2", BadgeBg = "#25A855F7", BadgeBorder = "#A855F7", BadgeFg = "#C084FC", IconColor = "#A855F7" },
            new() { ButtonName = "r2", DisplayLabel = "Правый триггер R2", RetroPadRole = "Аналоговый огонь / Защита", CfgKey = p + "r2", BadgeText = "R2", BadgeBg = "#25A855F7", BadgeBorder = "#A855F7", BadgeFg = "#C084FC", IconColor = "#A855F7" },
            new() { ButtonName = "l3", DisplayLabel = "Нажатие левого стика L3", RetroPadRole = "Бег / Центрирование камеры", CfgKey = p + "l3", BadgeText = "L3", BadgeBg = "#2506B6D4", BadgeBorder = "#06B6D4", BadgeFg = "#67E8F9", IconColor = "#06B6D4" },
            new() { ButtonName = "r3", DisplayLabel = "Нажатие правого стика R3", RetroPadRole = "Присед / Смена вида", CfgKey = p + "r3", BadgeText = "R3", BadgeBg = "#2506B6D4", BadgeBorder = "#06B6D4", BadgeFg = "#67E8F9", IconColor = "#06B6D4" }
        };

        // Load values from retroarch.cfg
        foreach (var b in list)
        {
            string keyVal = RetroArchConfigManager.GetString(b.CfgKey, "");
            string btnVal = RetroArchConfigManager.GetString(b.CfgKey + "_btn", "");

            b.BoundKey = string.IsNullOrEmpty(keyVal) ? GetDefaultKeyboardKey(b.ButtonName) : keyVal;
            b.BoundBtn = string.IsNullOrEmpty(btnVal) ? GetDefaultGamepadBtn(b.ButtonName) : btnVal;
        }

        return list;
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
