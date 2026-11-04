using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace StormRetroArch;

public class RemapButtonBinding : INotifyPropertyChanged
{
    private string _boundKey = "---";
    private string _boundBtn = "---";

    public string ButtonName { get; set; } = "";
    public string DisplayLabel { get; set; } = "";
    public string RetroPadRole { get; set; } = "";
    public string Icon { get; set; } = "🔘";
    public string CfgKey { get; set; } = "";

    public string BadgeText { get; set; } = "B";
    public string BadgeBg { get; set; } = "#258B5CF6";
    public string BadgeBorder { get; set; } = "#8B5CF6";
    public string BadgeFg { get; set; } = "#FFFFFF";

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
            new() { ButtonName = "b", DisplayLabel = "Кнопка B (Низ)", RetroPadRole = "Действие / Отмена / Прыжок", Icon = "🅱️", CfgKey = p + "b", BadgeText = "B", BadgeBg = "#25EF4444", BadgeBorder = "#EF4444", BadgeFg = "#FCA5A5" },
            new() { ButtonName = "y", DisplayLabel = "Кнопка Y (Лево)", RetroPadRole = "Атака / Ускорение / Магия", Icon = "🇾", CfgKey = p + "y", BadgeText = "Y", BadgeBg = "#2538BDF8", BadgeBorder = "#38BDF8", BadgeFg = "#7DD3FC" },
            new() { ButtonName = "select", DisplayLabel = "Select (Выбор)", RetroPadRole = "Переключение предметов / Карта", Icon = "🔘", CfgKey = p + "select", BadgeText = "SEL", BadgeBg = "#2564748B", BadgeBorder = "#64748B", BadgeFg = "#E2E8F0" },
            new() { ButtonName = "start", DisplayLabel = "Start (Старт)", RetroPadRole = "Пауза / Главное меню игры", Icon = "▶️", CfgKey = p + "start", BadgeText = "STA", BadgeBg = "#2564748B", BadgeBorder = "#64748B", BadgeFg = "#E2E8F0" },
            new() { ButtonName = "up", DisplayLabel = "Крестовина Вверх", RetroPadRole = "Направление вверх", Icon = "⬆️", CfgKey = p + "up", BadgeText = "▲", BadgeBg = "#258B5CF6", BadgeBorder = "#8B5CF6", BadgeFg = "#C4B5FD" },
            new() { ButtonName = "down", DisplayLabel = "Крестовина Вниз", RetroPadRole = "Направление вниз / Присесть", Icon = "⬇️", CfgKey = p + "down", BadgeText = "▼", BadgeBg = "#258B5CF6", BadgeBorder = "#8B5CF6", BadgeFg = "#C4B5FD" },
            new() { ButtonName = "left", DisplayLabel = "Крестовина Влево", RetroPadRole = "Направление влево", Icon = "⬅️", CfgKey = p + "left", BadgeText = "◄", BadgeBg = "#258B5CF6", BadgeBorder = "#8B5CF6", BadgeFg = "#C4B5FD" },
            new() { ButtonName = "right", DisplayLabel = "Крестовина Вправо", RetroPadRole = "Направление вправо", Icon = "➡️", CfgKey = p + "right", BadgeText = "►", BadgeBg = "#258B5CF6", BadgeBorder = "#8B5CF6", BadgeFg = "#C4B5FD" },
            new() { ButtonName = "a", DisplayLabel = "Кнопка A (Право)", RetroPadRole = "Подтверждение / Огонь", Icon = "🅰️", CfgKey = p + "a", BadgeText = "A", BadgeBg = "#2510B981", BadgeBorder = "#10B981", BadgeFg = "#6EE7B7" },
            new() { ButtonName = "x", DisplayLabel = "Кнопка X (Верх)", RetroPadRole = "Дополнительное действие / Инвентарь", Icon = "❌", CfgKey = p + "x", BadgeText = "X", BadgeBg = "#25FBBF24", BadgeBorder = "#FBBF24", BadgeFg = "#FDE047" },
            new() { ButtonName = "l", DisplayLabel = "Левый шифт L1", RetroPadRole = "Смена оружия влево / Блок", Icon = "◀️", CfgKey = p + "l", BadgeText = "L1", BadgeBg = "#25EC4899", BadgeBorder = "#EC4899", BadgeFg = "#F472B6" },
            new() { ButtonName = "r", DisplayLabel = "Правый шифт R1", RetroPadRole = "Смена оружия вправо / Прицел", Icon = "▶️", CfgKey = p + "r", BadgeText = "R1", BadgeBg = "#25EC4899", BadgeBorder = "#EC4899", BadgeFg = "#F472B6" },
            new() { ButtonName = "l2", DisplayLabel = "Левый триггер L2", RetroPadRole = "Аналоговый газ / Тормоз / Спецудар", Icon = "⏮️", CfgKey = p + "l2", BadgeText = "L2", BadgeBg = "#25A855F7", BadgeBorder = "#A855F7", BadgeFg = "#C084FC" },
            new() { ButtonName = "r2", DisplayLabel = "Правый триггер R2", RetroPadRole = "Аналоговый огонь / Защита", Icon = "⏭️", CfgKey = p + "r2", BadgeText = "R2", BadgeBg = "#25A855F7", BadgeBorder = "#A855F7", BadgeFg = "#C084FC" },
            new() { ButtonName = "l3", DisplayLabel = "Нажатие левого стика L3", RetroPadRole = "Бег / Центрирование камеры", Icon = "🕹️", CfgKey = p + "l3", BadgeText = "L3", BadgeBg = "#2506B6D4", BadgeBorder = "#06B6D4", BadgeFg = "#67E8F9" },
            new() { ButtonName = "r3", DisplayLabel = "Нажатие правого стика R3", RetroPadRole = "Присед / Смена вида", Icon = "🕹️", CfgKey = p + "r3", BadgeText = "R3", BadgeBg = "#2506B6D4", BadgeBorder = "#06B6D4", BadgeFg = "#67E8F9" }
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

        string rmpFile = Path.Combine(targetDir, safeName + ".rmp");
        var sb = new StringBuilder();

        foreach (var b in bindings)
        {
            sb.AppendLine($"{b.CfgKey} = \"{b.BoundKey}\"");
            sb.AppendLine($"{b.CfgKey}_btn = \"{b.BoundBtn}\"");
        }

        File.WriteAllText(rmpFile, sb.ToString(), Encoding.UTF8);
    }

    public static void ImportRemapFile(string rmpFilePath, IEnumerable<RemapButtonBinding> bindings)
    {
        if (!File.Exists(rmpFilePath)) return;

        try
        {
            var lines = File.ReadAllLines(rmpFilePath, Encoding.UTF8);
            var map = new Dictionary<string, string>();

            foreach (var l in lines)
            {
                int eq = l.IndexOf('=');
                if (eq > 0)
                {
                    string k = l.Substring(0, eq).Trim();
                    string v = l.Substring(eq + 1).Trim().Trim('"');
                    map[k] = v;
                }
            }

            foreach (var b in bindings)
            {
                if (map.TryGetValue(b.CfgKey, out var kVal)) b.BoundKey = kVal;
                if (map.TryGetValue(b.CfgKey + "_btn", out var btnVal)) b.BoundBtn = btnVal;
            }
        }
        catch { }
    }

    private static string GetDefaultKeyboardKey(string btn) => btn switch
    {
        "a" => "x",
        "b" => "z",
        "x" => "s",
        "y" => "a",
        "start" => "enter",
        "select" => "rshift",
        "up" => "up",
        "down" => "down",
        "left" => "left",
        "right" => "right",
        "l" => "q",
        "r" => "w",
        "l2" => "e",
        "r2" => "r",
        "l3" => "t",
        "r3" => "y",
        _ => "nul"
    };

    private static string GetDefaultGamepadBtn(string btn) => btn switch
    {
        "a" => "1",
        "b" => "0",
        "x" => "3",
        "y" => "2",
        "start" => "7",
        "select" => "6",
        "up" => "h0up",
        "down" => "h0down",
        "left" => "h0left",
        "right" => "h0right",
        "l" => "4",
        "r" => "5",
        "l2" => "+2",
        "r2" => "+5",
        "l3" => "8",
        "r3" => "9",
        _ => "nul"
    };
}
