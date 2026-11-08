using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace StormRetroArch;

/// <summary>
/// Представляет отдельную привязку горячей клавиши эмулятора с поддержкой уведомлений об изменениях.
/// </summary>
public class HotkeyBindingItem : INotifyPropertyChanged
{
    private string _actionId = "";
    private string _title = "";
    private string _category = "";
    private string _description = "";
    private string _keyName = "";
    private string _defaultKeyName = "";
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

    public string KeyBadgeText => string.IsNullOrWhiteSpace(KeyName) ? "—" : KeyName;
    public bool IsCustom => !string.Equals(KeyName, DefaultKeyName, StringComparison.OrdinalIgnoreCase);
    public Visibility ConflictVisibility => HasConflict ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Менеджер настраиваемых горячих клавиш эмулятора.
/// 13 ключевых действий на русском языке без английских скобок.
/// </summary>
public static class HotkeyManager
{
    public record DefaultDef(string ActionId, string Title, string Category, string Description, string DefaultKey);

    private static readonly DefaultDef[] Defaults =
    {
        new("quick_menu", "Быстрое меню сессии", "Сессия", "Открытие панели быстрого доступа к слотам, шейдерам и настройкам сессии", "F1"),
        new("save_state", "Быстрое сохранение в слот", "Стейты", "Мгновенная фиксация состояния игры в выбранный слот", "F2"),
        new("load_state", "Быстрая загрузка из слота", "Стейты", "Загрузка сохранённого состояния игры из выбранного слота", "F4"),
        new("game_screen", "Переход на экран игры", "Навигация", "Мгновенное переключение фокуса на экран активной игры", "F5"),
        new("instant_replay", "Сохранить видеоповтор 30 сек", "Медиа", "Запись и сохранение последних 30 секунд игрового процесса в видеофайл", "F7"),
        new("screenshot", "Скриншот кадра экрана", "Медиа", "Создание снимка экрана игрового процесса в высоком разрешении", "F8"),
        new("shader_cycle", "Переключение ЭЛТ-шейдера", "Графика", "Циклическое переключение профилей ЭЛТ-шейдеров и фильтров", "F9"),
        new("bigbox_mode", "Консольный режим ТВ", "Навигация", "Включение полноэкранного 10-футового интерфейса для ТВ", "F10"),
        new("fullscreen", "Полноэкранный режим", "Графика", "Переключение полноэкранного и оконного режимов отображения", "F11"),
        new("frame_advance", "Покадровый шаг эмуляции", "Сессия", "Точный покадровый шаг эмуляции в режиме паузы", "K"),
        new("rewind_hold", "Перемотка времени назад", "Сессия", "Плавная перемотка игрового времени назад при удержании", "R"),
        new("fast_forward", "Турбо-ускорение (2x–4x)", "Сессия", "Ускоренное воспроизведение игрового процесса", "Tab"),
        new("mute_audio", "Отключение звука", "Звук", "Быстрое включение и отключение звукового сопровождения", "M")
    };

    public static List<HotkeyBindingItem> GetAllBindings()
    {
        var list = new List<HotkeyBindingItem>();
        var custom = SettingsManager.Current.CustomHotkeys ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var def in Defaults)
        {
            string keyName = custom.TryGetValue(def.ActionId, out var val) && !string.IsNullOrWhiteSpace(val)
                ? val
                : def.DefaultKey;

            list.Add(new HotkeyBindingItem
            {
                ActionId = def.ActionId,
                Title = def.Title,
                Category = def.Category,
                Description = def.Description,
                KeyName = keyName,
                DefaultKeyName = def.DefaultKey
            });
        }

        UpdateConflicts(list);
        return list;
    }

    public static void SetBinding(string actionId, Key key)
    {
        if (string.IsNullOrWhiteSpace(actionId)) return;
        string keyName = key.ToString();
        SettingsManager.Current.CustomHotkeys ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        SettingsManager.Current.CustomHotkeys[actionId] = keyName;
        SettingsManager.Save();
    }

    public static void ResetAllToDefaults()
    {
        SettingsManager.Current.CustomHotkeys?.Clear();
        SettingsManager.Save();
    }

    public static bool Matches(string actionId, Key pressedKey)
    {
        if (pressedKey == Key.None) return false;
        string keyName = GetKeyLabel(actionId);
        if (Enum.TryParse<Key>(keyName, true, out var expectedKey))
        {
            return pressedKey == expectedKey;
        }
        return false;
    }

    public static string GetKeyLabel(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId)) return "";
        if (SettingsManager.Current.CustomHotkeys != null &&
            SettingsManager.Current.CustomHotkeys.TryGetValue(actionId, out var val) &&
            !string.IsNullOrWhiteSpace(val))
        {
            return val;
        }

        var match = Array.Find(Defaults, d => string.Equals(d.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
        return match?.DefaultKey ?? "";
    }

    public static void UpdateConflicts(IEnumerable<HotkeyBindingItem> items)
    {
        var grouped = items
            .Where(i => !string.IsNullOrWhiteSpace(i.KeyName))
            .GroupBy(i => i.KeyName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var it in items)
        {
            it.HasConflict = grouped.TryGetValue(it.KeyName, out int cnt) && cnt > 1;
        }
    }
}
