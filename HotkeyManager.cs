using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace StormRetroArch;

/// <summary>
/// Представляет отдельную привязку горячей клавиши и геймпада эмулятора с поддержкой уведомлений об изменениях.
/// </summary>
public class HotkeyBindingItem : INotifyPropertyChanged
{
    private string _actionId = "";
    private string _title = "";
    private string _category = "";
    private string _description = "";
    private string _keyName = "";
    private string _defaultKeyName = "";
    private string _gamepadChord = "";
    private string _defaultGamepadChord = "";
    private bool _hasConflict;

    public string ActionId
    {
        get => _actionId;
        set { _actionId = value; OnPropertyChanged(); }
    }

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public string Category
    {
        get => _category;
        set { _category = value; OnPropertyChanged(); }
    }

    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    public string KeyName
    {
        get => _keyName;
        set
        {
            if (_keyName != value)
            {
                _keyName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(KeyBadgeText));
                OnPropertyChanged(nameof(IsCustom));
            }
        }
    }

    public string DefaultKeyName
    {
        get => _defaultKeyName;
        set
        {
            if (_defaultKeyName != value)
            {
                _defaultKeyName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCustom));
            }
        }
    }

    public string GamepadChord
    {
        get => _gamepadChord;
        set
        {
            if (_gamepadChord != value)
            {
                _gamepadChord = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GamepadBadgeText));
                OnPropertyChanged(nameof(IsCustom));
            }
        }
    }

    public string DefaultGamepadChord
    {
        get => _defaultGamepadChord;
        set
        {
            if (_defaultGamepadChord != value)
            {
                _defaultGamepadChord = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCustom));
            }
        }
    }

    public bool HasConflict
    {
        get => _hasConflict;
        set
        {
            if (_hasConflict != value)
            {
                _hasConflict = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ConflictVisibility));
            }
        }
    }

    public string KeyBadgeText => string.IsNullOrWhiteSpace(KeyName) ? "-" : KeyName;
    public string GamepadBadgeText => string.IsNullOrWhiteSpace(GamepadChord) || GamepadChord == "None" ? "-" : GamepadChord;
    public bool IsCustom => !string.Equals(KeyName, DefaultKeyName, StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(GamepadChord, DefaultGamepadChord, StringComparison.OrdinalIgnoreCase);
    public Visibility ConflictVisibility => HasConflict ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Менеджер настраиваемых горячих клавиш клавиатуры и геймпада (Dual Keyboard + Gamepad Hotkey Studio).
/// 18 структурированных действий в 4 категориях.
/// </summary>
public static class HotkeyManager
{
    public record DefaultDef(string ActionId, string Title, string Category, string Description, string DefaultKey, string DefaultGamepadChord);

    public static readonly DefaultDef[] Defaults =
    {
        // 1. Сессия и меню
        new("quick_menu", "Быстрое меню игры", "Сессия и меню", "Открытие оверлея быстрого доступа к стейтам, шейдерам и настройкам", "F1", "Select + Start"),
        new("pause_toggle", "Пауза / Продолжить", "Сессия и меню", "Мгновенная приостановка и возобновление эмуляции", "P", "Select + A"),
        new("frame_advance", "Шаг на 1 кадр вперёд", "Сессия и меню", "Точный покадровый шаг эмуляции в режиме паузы", "K", "Select + DPadRight"),
        new("reset_game", "Перезапуск картриджа / ядра", "Сессия и меню", "Аппаратный перезапуск активной игры", "H", "Select + B"),

        // 2. Сохранения и время
        new("save_state", "Сохранить быстрый стейт", "Сохранения и время", "Мгновенная фиксация состояния игры в текущий слот", "F2", "Select + RB"),
        new("load_state", "Загрузить быстрый стейт", "Сохранения и время", "Загрузка сохранённого состояния игры из текущего слота", "F4", "Select + LB"),
        new("state_slot_next", "Следующий слот стейта (+)", "Сохранения и время", "Переключение на следующий слот быстрых сохранений", "F6", "Select + DPadUp"),
        new("state_slot_prev", "Предыдущий слот стейта (-)", "Сохранения и время", "Переключение на предыдущий слот быстрых сохранений", "F3", "Select + DPadDown"),
        new("rewind_hold", "Перемотка времени назад", "Сохранения и время", "Плавная перемотка игрового времени назад при удержании", "Back", "Select + LT"),
        new("fast_forward_hold", "Ускорение (удержание)", "Сохранения и время", "Турбо-ускорение эмуляции при удержании клавиши", "Space", "Select + RT"),
        new("fast_forward_toggle", "Переключить турбо-скорость", "Сохранения и время", "Фиксация ускоренного режима воспроизведения", "L", "Select + R3"),

        // 3. Экран и звук
        new("fullscreen_toggle", "Полноэкранный режим", "Экран и звук", "Переключение между полноэкранным и оконным режимами", "F11", "Select + L3"),
        new("shader_next", "Следующий ЭЛТ-шейдер", "Экран и звук", "Циклическое переключение профилей ЭЛТ-шейдеров и фильтров", "M", "None"),
        new("bezel_toggle", "Переключить рамку Bezel", "Экран и звук", "Включение и выключение художественной рамки ТВ", "B", "None"),
        new("audio_mute", "Включить / выключить звук", "Экран и звук", "Быстрое отключение и включение звукового сопровождения", "F9", "None"),

        // 4. Захват и инструменты
        new("screenshot", "Скриншот экрана", "Захват и инструменты", "Создание снимка экрана игрового процесса в высоком разрешении", "F8", "Select + Y"),
        new("instant_replay", "Сохранить повтор 30 сек", "Захват и инструменты", "Запись и сохранение последних 30 секунд игрового процесса", "F7", "Select + X"),
        new("osk_toggle", "Экранная клавиатура РУС/ENG", "Захват и инструменты", "Открытие двуязычной экранной клавиатуры для ввода", "F12", "None"),

        // Совместимость с дополнительными действиями
        new("game_screen", "Переход на экран игры", "Сессия и меню", "Мгновенное переключение фокуса на экран активной игры", "F5", "None"),
        new("bigbox_mode", "Консольный режим ТВ", "Сессия и меню", "Включение полноэкранного 10-футового интерфейса для ТВ", "F10", "None")
    };

    public static string NormalizeActionId(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId)) return "";
        string a = actionId.Trim().ToLowerInvariant();
        return a switch
        {
            "shader_cycle" => "shader_next",
            "fullscreen" => "fullscreen_toggle",
            "fast_forward" => "fast_forward_hold",
            "mute_audio" => "audio_mute",
            _ => a
        };
    }

    public static List<HotkeyBindingItem> GetAllBindings()
    {
        var list = new List<HotkeyBindingItem>();
        var custom = SettingsManager.Current.CustomHotkeys ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Display only the primary 18 flagship actions in the studio matrix
        foreach (var def in Defaults.Take(18))
        {
            string keyName = custom.TryGetValue(def.ActionId, out var val) && !string.IsNullOrWhiteSpace(val)
                ? val
                : def.DefaultKey;

            string padChord = custom.TryGetValue(def.ActionId + "_pad", out var pVal) && !string.IsNullOrWhiteSpace(pVal)
                ? pVal
                : def.DefaultGamepadChord;

            list.Add(new HotkeyBindingItem
            {
                ActionId = def.ActionId,
                Title = def.Title,
                Category = def.Category,
                Description = def.Description,
                KeyName = keyName,
                DefaultKeyName = def.DefaultKey,
                GamepadChord = padChord,
                DefaultGamepadChord = def.DefaultGamepadChord
            });
        }

        UpdateConflicts(list);
        return list;
    }

    public static void SetBinding(string actionId, Key key, ModifierKeys modifiers = ModifierKeys.None)
    {
        if (string.IsNullOrWhiteSpace(actionId)) return;
        string normId = NormalizeActionId(actionId);
        string comboStr = FormatKeyCombo(key, modifiers);

        SettingsManager.Current.CustomHotkeys ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        SettingsManager.Current.CustomHotkeys[normId] = comboStr;
        SettingsManager.Save();
    }

    public static void SetGamepadBinding(string actionId, string chord)
    {
        if (string.IsNullOrWhiteSpace(actionId)) return;
        string normId = NormalizeActionId(actionId);

        SettingsManager.Current.CustomHotkeys ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        SettingsManager.Current.CustomHotkeys[normId + "_pad"] = chord;
        SettingsManager.Save();
    }

    public static void SetGamepadBinding(string actionId, PhysicalInputBinding? binding, string displayChord)
    {
        if (string.IsNullOrWhiteSpace(actionId)) return;
        string normId = NormalizeActionId(actionId);

        SettingsManager.Current.CustomHotkeys ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        SettingsManager.Current.CustomHotkeys[normId + "_pad"] = displayChord;
        if (binding != null && binding.Type != PhysicalInputType.None)
            SettingsManager.Current.CustomHotkeys[normId + "_padbind"] = SerializePhysicalBinding(binding);
        else
            SettingsManager.Current.CustomHotkeys.Remove(normId + "_padbind");
        SettingsManager.Save();
    }

    public static void ClearGamepadBinding(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId)) return;
        string normId = NormalizeActionId(actionId);
        SettingsManager.Current.CustomHotkeys ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        SettingsManager.Current.CustomHotkeys[normId + "_pad"] = "None";
        SettingsManager.Current.CustomHotkeys.Remove(normId + "_padbind");
        SettingsManager.Save();
    }

    public static PhysicalInputBinding? ResolveGamepadTriggerBinding(string actionId, out bool requiresModifier)
    {
        requiresModifier = !string.Equals(SettingsManager.Current?.GamepadChordModifier, "SingleButton", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(actionId)) return null;
        string normId = NormalizeActionId(actionId);

        var custom = SettingsManager.Current?.CustomHotkeys;
        if (custom != null && custom.TryGetValue(normId + "_padbind", out var raw) &&
            TryDeserializePhysicalBinding(raw, out var parsed))
        {
            if (custom.TryGetValue(normId + "_pad", out var disp) && !string.IsNullOrWhiteSpace(disp))
            {
                if (disp.Equals("None", StringComparison.OrdinalIgnoreCase)) return null;
                if (!disp.Contains('+', StringComparison.Ordinal)) requiresModifier = false;
            }
            return parsed;
        }

        string chordLabel = GetGamepadChord(normId);
        if (string.IsNullOrWhiteSpace(chordLabel) || chordLabel.Equals("None", StringComparison.OrdinalIgnoreCase) || chordLabel == "-")
            return null;

        return ParseChordDisplayToPhysicalBinding(chordLabel, ref requiresModifier);
    }

    public static string SerializePhysicalBinding(PhysicalInputBinding b)
    {
        return $"{(int)b.Type}|{b.Code}|{b.Parameter}|{b.DisplayLabel}";
    }

    public static bool TryDeserializePhysicalBinding(string raw, out PhysicalInputBinding binding)
    {
        binding = new PhysicalInputBinding();
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var parts = raw.Split('|', 4);
        if (parts.Length < 3) return false;
        if (!int.TryParse(parts[0], out int tInt) || !Enum.IsDefined(typeof(PhysicalInputType), tInt)) return false;
        if (!int.TryParse(parts[1], out int code)) return false;
        if (!int.TryParse(parts[2], out int param)) return false;
        string label = parts.Length > 3 ? parts[3] : "";
        binding = new PhysicalInputBinding((PhysicalInputType)tInt, code, param, label);
        return binding.Type != PhysicalInputType.None;
    }

    public static PhysicalInputBinding? ParseChordDisplayToPhysicalBinding(string chord, ref bool requiresModifier)
    {
        if (string.IsNullOrWhiteSpace(chord) || chord.Equals("None", StringComparison.OrdinalIgnoreCase)) return null;
        string part = chord.Trim();
        int plus = part.LastIndexOf('+');
        if (plus >= 0)
        {
            requiresModifier = true;
            part = part[(plus + 1)..].Trim();
        }
        else if (!part.Equals("Select", StringComparison.OrdinalIgnoreCase) && !part.Equals("Guide", StringComparison.OrdinalIgnoreCase))
        {
            // Keep global setting if no prefix was explicit
        }

        string u = part.ToUpperInvariant();
        if (u == "A" || u == "КНОПКА A") return new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x1000, 0, "Кнопка A");
        if (u == "B" || u == "КНОПКА B") return new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x2000, 0, "Кнопка B");
        if (u == "X" || u == "КНОПКА X") return new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x4000, 0, "Кнопка X");
        if (u == "Y" || u == "КНОПКА Y") return new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x8000, 0, "Кнопка Y");
        if (u == "START") return new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0010, 0, "Start");
        if (u == "BACK" || u == "SELECT" || u == "BACK / SELECT") return new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0020, 0, "Back / Select");
        if (u == "LB" || u == "L1" || u == "БАМПЕР LB") return new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0100, 0, "Бампер LB");
        if (u == "RB" || u == "R1" || u == "БАМПЕР RB") return new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0200, 0, "Бампер RB");
        if (u == "L3" || u == "ЛЕВЫЙ СТИК L3") return new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0040, 0, "Левый стик L3");
        if (u == "R3" || u == "ПРАВЫЙ СТИК R3") return new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0080, 0, "Правый стик R3");
        if (u == "GUIDE" || u == "HOME" || u == "GUIDE / HOME") return new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0400, 0, "Guide / Home");
        if (u == "LT" || u == "L2" || u == "ТРИГГЕР LT" || u == "ЛЕВЫЙ ТРИГГЕР LT") return new PhysicalInputBinding(PhysicalInputType.XInputTrigger, 0, 12, "Триггер LT");
        if (u == "RT" || u == "R2" || u == "ТРИГГЕР RT" || u == "ПРАВЫЙ ТРИГГЕР RT") return new PhysicalInputBinding(PhysicalInputType.XInputTrigger, 1, 12, "Триггер RT");
        if (u == "DPADUP" || u == "D-PAD ВВЕРХ") return new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0001, 0, "D-Pad Вверх");
        if (u == "DPADDOWN" || u == "D-PAD ВНИЗ") return new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0002, 0, "D-Pad Вниз");
        if (u == "DPADLEFT" || u == "D-PAD ВЛЕВО") return new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0004, 0, "D-Pad Влево");
        if (u == "DPADRIGHT" || u == "D-PAD ВПРАВО") return new PhysicalInputBinding(PhysicalInputType.XInputButton, 0x0008, 0, "D-Pad Вправо");

        if (u.StartsWith("КНОПКА ") && int.TryParse(u[7..].Trim(), out int wb) && wb >= 1 && wb <= 32)
            return new PhysicalInputBinding(PhysicalInputType.WinMmButton, wb - 1, 0, part);
        if (u == "POV ВВЕРХ") return new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 0, "POV Вверх");
        if (u == "POV ВПРАВО") return new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 9000, "POV Вправо");
        if (u == "POV ВНИЗ") return new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 18000, "POV Вниз");
        if (u == "POV ВЛЕВО") return new PhysicalInputBinding(PhysicalInputType.WinMmPovHat, 0, 27000, "POV Влево");

        return null;
    }

    public static void ResetActionToDefault(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId)) return;
        string normId = NormalizeActionId(actionId);
        if (SettingsManager.Current.CustomHotkeys != null)
        {
            SettingsManager.Current.CustomHotkeys.Remove(normId);
            SettingsManager.Current.CustomHotkeys.Remove(normId + "_pad");
            SettingsManager.Current.CustomHotkeys.Remove(normId + "_padbind");
            SettingsManager.Save();
        }
    }

    public static void ResetAllToDefaults()
    {
        SettingsManager.Current.CustomHotkeys?.Clear();
        SettingsManager.Save();
    }

    public static bool Matches(string actionId, Key pressedKey, ModifierKeys modifiers = ModifierKeys.None)
    {
        if (pressedKey == Key.None) return false;
        string normId = NormalizeActionId(actionId);
        string keyStr = GetKeyLabel(normId);
        if (string.IsNullOrWhiteSpace(keyStr)) return false;

        if (ParseKeyCombo(keyStr, out var expectedKey, out var expectedMods))
        {
            if (expectedKey != pressedKey) return false;

            ModifierKeys actualMods = modifiers;
            if (actualMods == ModifierKeys.None)
            {
                try
                {
                    actualMods = Keyboard.Modifiers;
                }
                catch { }
            }

            const ModifierKeys mask = ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift;
            return (actualMods & mask) == (expectedMods & mask);
        }

        return false;
    }

    public static string GetKeyLabel(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId)) return "";
        string normId = NormalizeActionId(actionId);

        if (SettingsManager.Current.CustomHotkeys != null &&
            SettingsManager.Current.CustomHotkeys.TryGetValue(normId, out var val) &&
            !string.IsNullOrWhiteSpace(val))
        {
            return val;
        }

        var match = Array.Find(Defaults, d => string.Equals(d.ActionId, normId, StringComparison.OrdinalIgnoreCase));
        return match?.DefaultKey ?? "";
    }

    public static string GetGamepadChord(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId)) return "";
        string normId = NormalizeActionId(actionId);

        if (SettingsManager.Current.CustomHotkeys != null &&
            SettingsManager.Current.CustomHotkeys.TryGetValue(normId + "_pad", out var val) &&
            !string.IsNullOrWhiteSpace(val))
        {
            return val;
        }

        var match = Array.Find(Defaults, d => string.Equals(d.ActionId, normId, StringComparison.OrdinalIgnoreCase));
        return match?.DefaultGamepadChord ?? "None";
    }

    public static bool ParseKeyCombo(string comboStr, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;
        if (string.IsNullOrWhiteSpace(comboStr)) return false;

        var parts = comboStr.Split(new[] { '+', '-' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts)
        {
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Control;
            else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Alt;
            else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Shift;
            else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase) || p.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Windows;
            else if (Enum.TryParse<Key>(p, true, out var k))
                key = k;
        }
        return key != Key.None;
    }

    public static string FormatKeyCombo(Key key, ModifierKeys modifiers)
    {
        if (key == Key.None) return "";
        var sb = new List<string>();
        if ((modifiers & ModifierKeys.Control) != 0) sb.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) sb.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) sb.Add("Shift");
        if ((modifiers & ModifierKeys.Windows) != 0) sb.Add("Win");
        sb.Add(key.ToString());
        return string.Join("+", sb);
    }

    public static List<string> FindConflicts()
    {
        var conflicts = new List<string>();
        var items = GetAllBindings();

        // 1. Keyboard conflicts
        var keyGroups = items
            .Where(i => !string.IsNullOrWhiteSpace(i.KeyName) && i.KeyName != "-")
            .GroupBy(i => i.KeyName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var g in keyGroups)
        {
            conflicts.Add($"Конфликт клавиши «{g.Key}»: {string.Join(", ", g.Select(x => x.Title))}");
        }

        // 2. Gamepad chord conflicts
        var padGroups = items
            .Where(i => !string.IsNullOrWhiteSpace(i.GamepadChord) && i.GamepadChord != "None" && i.GamepadChord != "-")
            .GroupBy(i => i.GamepadChord, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var g in padGroups)
        {
            conflicts.Add($"Конфликт геймпада «{g.Key}»: {string.Join(", ", g.Select(x => x.Title))}");
        }

        return conflicts;
    }

    public static void UpdateConflicts(IEnumerable<HotkeyBindingItem> items)
    {
        var keyCounts = items
            .Where(i => !string.IsNullOrWhiteSpace(i.KeyName) && i.KeyName != "-")
            .GroupBy(i => i.KeyName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var padCounts = items
            .Where(i => !string.IsNullOrWhiteSpace(i.GamepadChord) && i.GamepadChord != "None" && i.GamepadChord != "-")
            .GroupBy(i => i.GamepadChord, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var it in items)
        {
            bool keyConflict = !string.IsNullOrWhiteSpace(it.KeyName) && keyCounts.TryGetValue(it.KeyName, out int kc) && kc > 1;
            bool padConflict = !string.IsNullOrWhiteSpace(it.GamepadChord) && it.GamepadChord != "None" && padCounts.TryGetValue(it.GamepadChord, out int pc) && pc > 1;
            it.HasConflict = keyConflict || padConflict;
        }
    }
}
