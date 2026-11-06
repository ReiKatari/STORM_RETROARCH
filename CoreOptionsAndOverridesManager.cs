using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace StormRetroArch;

public class CoreOptionItem : INotifyPropertyChanged
{
    private string _value = "";

    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string CoreName { get; set; } = "";
    public string Category { get; set; } = "Видео и палитра";
    public string DefaultValue { get; set; } = "";
    public List<string> Options { get; set; } = new();

    public string Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                OnPropertyChanged();
            }
        }
    }

    public void EnsureValueInOptions()
    {
        if (Options == null) Options = new();
        if (Options.Count == 0)
        {
            if (!string.IsNullOrEmpty(Value)) Options.Add(Value);
            else if (!string.IsNullOrEmpty(DefaultValue)) Options.Add(DefaultValue);
            else Options.Add("enabled");
        }

        if (string.IsNullOrEmpty(Value))
        {
            Value = !string.IsNullOrEmpty(DefaultValue) && Options.Contains(DefaultValue) ? DefaultValue : Options[0];
            return;
        }

        if (!Options.Contains(Value))
        {
            var match = Options.FirstOrDefault(o => o.StartsWith(Value, StringComparison.OrdinalIgnoreCase) || Value.StartsWith(o, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                Value = match;
            }
            else
            {
                Options.Insert(0, Value);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class CoreProfileItem
{
    public string CoreName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Platform { get; set; } = "";
    public string IconPathData { get; set; } = "M3 17v2h6v-2H3zM3 5v2h10V5H3zm10 16v-2h8v-2h-8v-2h-2v6h2zM7 9v2H3v2h4v2h2V9H7zm14 4v-2H11v2h10zm-6-4h2V7h4V5h-4V3h-2v6z";
    public string IconColor { get; set; } = "#A78BFA";
    public List<CoreOptionItem> Options { get; set; } = new();
}

public static class CoreOptionsAndOverridesManager
{
    public static readonly string ConfigDir = Path.Combine(CoreManager.AppDir, "config");
    public static readonly string ShadersDir = Path.Combine(CoreManager.AppDir, "shaders");

    public static List<CoreProfileItem> GetCoreProfiles()
    {
        var profiles = new List<CoreProfileItem>();

        // 1. FCEUmm
        profiles.Add(new CoreProfileItem
        {
            CoreName = "FCEUmm", DisplayName = "FCEUmm (Nintendo NES / Dendy)", Platform = "NES", Options = new()
            {
                new() { Key = "fceumm_palette", Title = "Цветовая палитра", Description = "Палитра Dendy, NES Classic или студийная NTSC", CoreName = "FCEUmm", Category = "Видео и палитра", Value = "dendy", DefaultValue = "dendy", Options = new() { "default", "dendy", "composite-direct-fbx", "sony-cxa2025as-us", "nes-classic" } },
                new() { Key = "fceumm_color_depth", Title = "Глубина цвета видео", Description = "Разрядность цветового буфера при выводе кадра", CoreName = "FCEUmm", Category = "Видео и палитра", Value = "24-bit", DefaultValue = "24-bit", Options = new() { "16-bit", "24-bit" } },
                new() { Key = "fceumm_aspect", Title = "Соотношение сторон экрана", Description = "Пропорции кадра для плоских и кинескопных телевизоров", CoreName = "FCEUmm", Category = "Разрешение и фильтрация", Value = "auto", DefaultValue = "auto", Options = new() { "auto", "4:3", "8:7 PAR", "16:9" } },
                new() { Key = "fceumm_overscan_v", Title = "Вертикальный оверскан", Description = "Обрезка служебных строчек по верхнему и нижнему краям", CoreName = "FCEUmm", Category = "Разрешение и фильтрация", Value = "crop", DefaultValue = "crop", Options = new() { "disabled", "crop", "strict" } },
                new() { Key = "fceumm_overscan_h", Title = "Горизонтальный оверскан", Description = "Маскирование артефактов прокрутки по левому краю", CoreName = "FCEUmm", Category = "Разрешение и фильтрация", Value = "crop", DefaultValue = "crop", Options = new() { "disabled", "crop" } },
                new() { Key = "fceumm_ntsc_filter", Title = "Композитный фильтр", Description = "Аутентичные переливы и полосы кинескопного сигнала", CoreName = "FCEUmm", Category = "Разрешение и фильтрация", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "composite", "svideo", "rgb" } },
                new() { Key = "fceumm_overclocking", Title = "Разгон процессора", Description = "Устранение мерцания спрайтов и падений кадровой частоты", CoreName = "FCEUmm", Category = "Процессор и точность", Value = "2x-postrender", DefaultValue = "2x-postrender", Options = new() { "disabled", "2x-postrender", "2x-vblank" } },
                new() { Key = "fceumm_sound_quality", Title = "Качество звука", Description = "Частота дискретизации встроенного звукового генератора", CoreName = "FCEUmm", Category = "Аудио и звуковые чипы", Value = "high", DefaultValue = "high", Options = new() { "low", "high", "very high" } },
                new() { Key = "fceumm_swap_duty", Title = "Инверсия скважности импульса", Description = "Аутентичный тембр некоторых клонов Famicom и Dendy", CoreName = "FCEUmm", Category = "Аудио и звуковые чипы", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "fceumm_sndvolume", Title = "Общая громкость аудио", Description = "Мастер-уровень звука звукового ядра", CoreName = "FCEUmm", Category = "Аудио и звуковые чипы", Value = "150", DefaultValue = "150", Options = new() { "50", "100", "150", "200" } },
                new() { Key = "fceumm_zapper_mode", Title = "Режим светового пистолета", Description = "Управление световым пистолетом мышью или стиком геймпада", CoreName = "FCEUmm", Category = "Ввод и системные функции", Value = "lightgun", DefaultValue = "lightgun", Options = new() { "pointer", "lightgun", "touchscreen" } },
                new() { Key = "fceumm_turbo_delay", Title = "Задержка турбо-кнопок", Description = "Количество кадров между повторными нажатиями турбо-огня", CoreName = "FCEUmm", Category = "Ввод и системные функции", Value = "3", DefaultValue = "3", Options = new() { "1", "2", "3", "5", "10" } },
                new() { Key = "fceumm_up_down_allowed", Title = "Одновременные направления крестовины", Description = "Разрешение одновременного зажатия противоположных сторон", CoreName = "FCEUmm", Category = "Ввод и системные функции", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "enabled" } }
            }
        });

        // 2. Mesen
        profiles.Add(new CoreProfileItem
        {
            CoreName = "Mesen", DisplayName = "Mesen (Nintendo NES / Famicom)", Platform = "NES", Options = new()
            {
                new() { Key = "mesen_palette", Title = "Цветовая палитра", Description = "Цветовой профиль палитры Famicom и Dendy", CoreName = "Mesen", Category = "Видео и палитра", Value = "Default", DefaultValue = "Default", Options = new() { "Default", "Dendy", "Nestopia (RGB)", "Sony CXA", "Wavebeam" } },
                new() { Key = "mesen_hd_packs", Title = "HD-текстурные паки", Description = "Замена графики на рисованные текстуры высокой четкости", CoreName = "Mesen", Category = "Видео и палитра", Value = "Enabled", DefaultValue = "Enabled", Options = new() { "Disabled", "Enabled" } },
                new() { Key = "mesen_aspect_ratio", Title = "Соотношение сторон", Description = "Геометрия пикселя кадра экрана", CoreName = "Mesen", Category = "Разрешение и фильтрация", Value = "Auto", DefaultValue = "Auto", Options = new() { "Auto", "NTSC (8:7)", "NTSC (4:3)", "16:9" } },
                new() { Key = "mesen_ntsc_filter", Title = "Аналоговый фильтр", Description = "Эмуляция радиочастотного композитного кабеля", CoreName = "Mesen", Category = "Разрешение и фильтрация", Value = "Disabled", DefaultValue = "Disabled", Options = new() { "Disabled", "Composite", "S-Video", "RGB" } },
                new() { Key = "mesen_overscan_left", Title = "Обрезка левого края", Description = "Скрытие мапперных полос и прокрутки слева", CoreName = "Mesen", Category = "Разрешение и фильтрация", Value = "8", DefaultValue = "8", Options = new() { "0", "4", "8", "16" } },
                new() { Key = "mesen_overscan_right", Title = "Обрезка правого края", Description = "Скрытие полосы правой границы экрана", CoreName = "Mesen", Category = "Разрешение и фильтрация", Value = "8", DefaultValue = "8", Options = new() { "0", "4", "8", "16" } },
                new() { Key = "mesen_overclock_type", Title = "Режим разгона процессора", Description = "Дополнительные такты вычислений логики игры", CoreName = "Mesen", Category = "Процессор и точность", Value = "Before NMI", DefaultValue = "Before NMI", Options = new() { "None", "Before NMI", "After NMI" } },
                new() { Key = "mesen_overclock", Title = "Коэффициент разгона", Description = "Увеличение тактовой частоты процессора 6502", CoreName = "Mesen", Category = "Процессор и точность", Value = "200%", DefaultValue = "200%", Options = new() { "None", "133%", "150%", "200%", "400%" } },
                new() { Key = "mesen_ram_power_on_state", Title = "Состояние оперативной памяти", Description = "Заполнение ячеек памяти при включении консоли", CoreName = "Mesen", Category = "Процессор и точность", Value = "All 00s", DefaultValue = "All 00s", Options = new() { "All 00s", "All FFs", "Random" } },
                new() { Key = "mesen_audio_sample_rate", Title = "Частота дискретизации аудио", Description = "Качество воспроизведения звуковых дорожек", CoreName = "Mesen", Category = "Аудио и звуковые чипы", Value = "48000", DefaultValue = "48000", Options = new() { "44100", "48000", "96000" } },
                new() { Key = "mesen_audio_stereo", Title = "Псевдо-стерео эффект", Description = "Разделение звуковых каналов синтезатора по панораме", CoreName = "Mesen", Category = "Аудио и звуковые чипы", Value = "Disabled", DefaultValue = "Disabled", Options = new() { "Disabled", "Slight", "Wide" } },
                new() { Key = "mesen_dipswitch", Title = "Переключатели игровых автоматов", Description = "Настройки сложности и монетного приёмника", CoreName = "Mesen", Category = "Ввод и системные функции", Value = "Default", DefaultValue = "Default", Options = new() { "Default", "Alternate" } },
                new() { Key = "mesen_controller_turbo_speed", Title = "Скорость турбо-режима", Description = "Частота срабатывания непрерывной стрельбы", CoreName = "Mesen", Category = "Ввод и системные функции", Value = "Fast", DefaultValue = "Fast", Options = new() { "Slow", "Normal", "Fast" } }
            }
        });

        // 3. Snes9x
        profiles.Add(new CoreProfileItem
        {
            CoreName = "Snes9x", DisplayName = "Snes9x (Super Nintendo)", Platform = "SNES", Options = new()
            {
                new() { Key = "snes9x_hires_blend", Title = "Сглаживание высокого разрешения", Description = "Аппаратное смешивание полупрозрачности текста и спецэффектов", CoreName = "Snes9x", Category = "Видео и палитра", Value = "merge", DefaultValue = "merge", Options = new() { "disabled", "merge", "blur" } },
                new() { Key = "snes9x_layer_1", Title = "Фоновый слой 1", Description = "Отображение основного слоя тайлов декораций", CoreName = "Snes9x", Category = "Видео и палитра", Value = "enabled", DefaultValue = "enabled", Options = new() { "enabled", "disabled" } },
                new() { Key = "snes9x_layer_2", Title = "Фоновый слой 2", Description = "Отображение вторичного слоя декораций и задников", CoreName = "Snes9x", Category = "Видео и палитра", Value = "enabled", DefaultValue = "enabled", Options = new() { "enabled", "disabled" } },
                new() { Key = "snes9x_reduce_flicker", Title = "Подавление чересстрочного мерцания", Description = "Плавное объединение полукадров высокого разрешения", CoreName = "Snes9x", Category = "Видео и палитра", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "snes9x_aspect", Title = "Соотношение сторон", Description = "Пропорции экрана для широкоформатных мониторов", CoreName = "Snes9x", Category = "Разрешение и фильтрация", Value = "4:3", DefaultValue = "4:3", Options = new() { "4:3", "8:7", "16:9" } },
                new() { Key = "snes9x_crop_overscan", Title = "Обрезка оверскана", Description = "Скрытие служебных строк телевизионной развертки", CoreName = "Snes9x", Category = "Разрешение и фильтрация", Value = "auto", DefaultValue = "auto", Options = new() { "disabled", "enabled", "auto" } },
                new() { Key = "snes9x_overclock_cycles", Title = "Разгон процессора Super FX", Description = "Устранение тормозов в трехмерных играх", CoreName = "Snes9x", Category = "Процессор и точность", Value = "100%", DefaultValue = "100%", Options = new() { "100%", "150%", "200%", "300%" } },
                new() { Key = "snes9x_block_invalid_vram_access", Title = "Блокировка неверного доступа к видеопамяти", Description = "Строгое соответствие аппаратному чипсету", CoreName = "Snes9x", Category = "Процессор и точность", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "snes9x_audio_interpolation", Title = "Интерполяция сэмплов аудио", Description = "Метод сглаживания звучания звукового сопроцессора", CoreName = "Snes9x", Category = "Аудио и звуковые чипы", Value = "gaussian", DefaultValue = "gaussian", Options = new() { "none", "linear", "gaussian", "cubic", "sinc" } },
                new() { Key = "snes9x_sound_rate", Title = "Частота дискретизации звука", Description = "Частота вывода звукового потока", CoreName = "Snes9x", Category = "Аудио и звуковые чипы", Value = "48000", DefaultValue = "48000", Options = new() { "32000", "44100", "48000" } },
                new() { Key = "snes9x_region", Title = "Регион консоли", Description = "Выбор частоты развертки и телевизионного стандарта", CoreName = "Snes9x", Category = "Ввод и системные функции", Value = "auto", DefaultValue = "auto", Options = new() { "auto", "NTSC", "PAL" } },
                new() { Key = "snes9x_justifier1_color", Title = "Цвет прицела первого игрока", Description = "Цвет отображения лазерного прицела светового пистолета", CoreName = "Snes9x", Category = "Ввод и системные функции", Value = "Blue", DefaultValue = "Blue", Options = new() { "Blue", "Red", "Green", "White" } },
                new() { Key = "snes9x_superscope_reverse_buttons", Title = "Инверсия кнопок световой пушки", Description = "Смена функционала кнопок огня и перезарядки пушки", CoreName = "Snes9x", Category = "Ввод и системные функции", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "enabled" } }
            }
        });

        // 4. Genesis Plus GX
        profiles.Add(new CoreProfileItem
        {
            CoreName = "Genesis Plus GX", DisplayName = "Genesis Plus GX (Sega Mega Drive / Genesis)", Platform = "Sega Genesis", Options = new()
            {
                new() { Key = "genesis_plus_gx_bmap", Title = "Цветовая палитра", Description = "Профиль гаммы оригинального видеопроцессора", CoreName = "Genesis Plus GX", Category = "Видео и палитра", Value = "composite", DefaultValue = "composite", Options = new() { "composite", "pure", "raw" } },
                new() { Key = "genesis_plus_gx_render", Title = "Чересстрочная интерполяция", Description = "Сглаживание двухполосных полукадров", CoreName = "Genesis Plus GX", Category = "Видео и палитра", Value = "single field", DefaultValue = "single field", Options = new() { "single field", "double field" } },
                new() { Key = "genesis_plus_gx_aspect_ratio", Title = "Соотношение сторон кадра", Description = "Геометрия изображения видеовыхода", CoreName = "Genesis Plus GX", Category = "Разрешение и фильтрация", Value = "auto", DefaultValue = "auto", Options = new() { "auto", "4:3", "16:9" } },
                new() { Key = "genesis_plus_gx_overscan", Title = "Отображение границ оверскана", Description = "Скрытие боковых цветных полос синхронизации", CoreName = "Genesis Plus GX", Category = "Разрешение и фильтрация", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "top/bottom", "left/right", "full" } },
                new() { Key = "genesis_plus_gx_blargg_ntsc_filter", Title = "Аналоговый кинескопный фильтр", Description = "Имитация телевизионного разъема и радужных помех", CoreName = "Genesis Plus GX", Category = "Разрешение и фильтрация", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "composite", "svideo", "rgb" } },
                new() { Key = "genesis_plus_gx_overclock", Title = "Разгон процессора", Description = "Плавная работа игр без задержек и просадок", CoreName = "Genesis Plus GX", Category = "Процессор и точность", Value = "100%", DefaultValue = "100%", Options = new() { "100%", "125%", "150%", "200%" } },
                new() { Key = "genesis_plus_gx_system_hw", Title = "Тип аппаратной платформы", Description = "Аппаратная конфигурация запускаемой игры", CoreName = "Genesis Plus GX", Category = "Процессор и точность", Value = "auto", DefaultValue = "auto", Options = new() { "auto", "master system", "game gear", "mega drive" } },
                new() { Key = "genesis_plus_gx_ym2612", Title = "Эмуляция синтезатора FM-звука", Description = "Звуковой чип синтеза музыкальных инструментов", CoreName = "Genesis Plus GX", Category = "Аудио и звуковые чипы", Value = "mame (enhanced)", DefaultValue = "mame (enhanced)", Options = new() { "mame (enhanced)", "nuked (discrete)" } },
                new() { Key = "genesis_plus_gx_audio_filter", Title = "Низкочастотный фильтр звука", Description = "Теплый аналоговый звук оригинальной первой ревизии приставки", CoreName = "Genesis Plus GX", Category = "Аудио и звуковые чипы", Value = "low-pass", DefaultValue = "low-pass", Options = new() { "disabled", "low-pass" } },
                new() { Key = "genesis_plus_gx_sound_output", Title = "Режим звукового выхода", Description = "Формат звукового стереопанорамирования", CoreName = "Genesis Plus GX", Category = "Аудио и звуковые чипы", Value = "stereo", DefaultValue = "stereo", Options = new() { "mono", "stereo" } },
                new() { Key = "genesis_plus_gx_region_detect", Title = "Автоопределение региона", Description = "Выбор японского, европейского или американского региона", CoreName = "Genesis Plus GX", Category = "Ввод и системные функции", Value = "auto", DefaultValue = "auto", Options = new() { "auto", "ntsc-u", "pal", "ntsc-j" } },
                new() { Key = "genesis_plus_gx_gun_cursor", Title = "Прицел светового пистолета", Description = "Отображение точки попадания пушки", CoreName = "Genesis Plus GX", Category = "Ввод и системные функции", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "genesis_plus_gx_invert_mouse", Title = "Инверсия мыши Sega", Description = "Инвертирование вертикальной оси координатного манипулятора", CoreName = "Genesis Plus GX", Category = "Ввод и системные функции", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "enabled" } }
            }
        });

        // 5. DOSBox-Pure
        profiles.Add(new CoreProfileItem
        {
            CoreName = "DOSBox-Pure", DisplayName = "DOSBox-Pure (MS-DOS PC)", Platform = "PC / DOS", Options = new()
            {
                new() { Key = "dosbox_pure_svga", Title = "Видеоадаптер", Description = "Стандарт графической подсистемы виртуальной машины", CoreName = "DOSBox-Pure", Category = "Видео и палитра", Value = "svga_s3", DefaultValue = "svga_s3", Options = new() { "cga", "ega", "vga", "svga_s3", "vesa_nolfb" } },
                new() { Key = "dosbox_pure_cga", Title = "Цветовая гамма CGA", Description = "Набор цветов ранних графических режимов", CoreName = "DOSBox-Pure", Category = "Видео и палитра", Value = "early_auto", DefaultValue = "early_auto", Options = new() { "early_auto", "late_auto", "composite" } },
                new() { Key = "dosbox_pure_aspect", Title = "Коррекция пропорций", Description = "Исправление соотношения сторон для квадратных пикселей", CoreName = "DOSBox-Pure", Category = "Разрешение и фильтрация", Value = "true", DefaultValue = "true", Options = new() { "false", "true" } },
                new() { Key = "dosbox_pure_vrr", Title = "Плавная развертка кадров", Description = "Адаптация кадровой частоты под экраны высокой герцовки", CoreName = "DOSBox-Pure", Category = "Разрешение и фильтрация", Value = "false", DefaultValue = "false", Options = new() { "false", "true" } },
                new() { Key = "dosbox_pure_cycles", Title = "Циклы процессора", Description = "Тактовая частота виртуального компьютера", CoreName = "DOSBox-Pure", Category = "Процессор и точность", Value = "auto", DefaultValue = "auto", Options = new() { "auto", "3000", "10000", "25000", "50000", "max" } },
                new() { Key = "dosbox_pure_cputype", Title = "Архитектура процессора", Description = "Поколение эмулируемого процессора", CoreName = "DOSBox-Pure", Category = "Процессор и точность", Value = "auto", DefaultValue = "auto", Options = new() { "auto", "386", "486", "pentium", "pentium_slow" } },
                new() { Key = "dosbox_pure_memory", Title = "Объем оперативной памяти", Description = "Количество доступной основной оперативной памяти", CoreName = "DOSBox-Pure", Category = "Процессор и точность", Value = "16", DefaultValue = "16", Options = new() { "4", "8", "16", "32", "64", "128" } },
                new() { Key = "dosbox_pure_soundblaster", Title = "Звуковая карта", Description = "Модель звукового адаптера для оцифрованного звука", CoreName = "DOSBox-Pure", Category = "Аудио и звуковые чипы", Value = "sb16", DefaultValue = "sb16", Options = new() { "none", "sb2", "sbpro2", "sb16", "gb" } },
                new() { Key = "dosbox_pure_midi", Title = "Синтезатор музыки", Description = "Модель профессионального музыкального синтезатора", CoreName = "DOSBox-Pure", Category = "Аудио и звуковые чипы", Value = "sc-55", DefaultValue = "sc-55", Options = new() { "disabled", "mt-32", "sc-55", "gm" } },
                new() { Key = "dosbox_pure_bind_unused", Title = "Свободные кнопки геймпада", Description = "Назначение дополнительных клавиш на незанятые кнопки", CoreName = "DOSBox-Pure", Category = "Ввод и системные функции", Value = "true", DefaultValue = "true", Options = new() { "false", "true" } },
                new() { Key = "dosbox_pure_mouse_wheel", Title = "Колесо прокрутки мыши", Description = "Преобразование вращения колеса в нажатия клавиш", CoreName = "DOSBox-Pure", Category = "Ввод и системные функции", Value = "arrows", DefaultValue = "arrows", Options = new() { "disabled", "arrows", "pageupdown" } },
                new() { Key = "dosbox_pure_keyboard_layout", Title = "Раскладка клавиатуры", Description = "Языковой стандарт системного драйвера клавиатуры", CoreName = "DOSBox-Pure", Category = "Ввод и системные функции", Value = "us", DefaultValue = "us", Options = new() { "us", "ru", "uk", "de", "fr" } },
                new() { Key = "dosbox_pure_bootos", Title = "Загрузка установленной системы", Description = "Прямой запуск операционных систем Windows из образа", CoreName = "DOSBox-Pure", Category = "Ввод и системные функции", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "enabled" } }
            }
        });

        // 6. SwanStation
        profiles.Add(new CoreProfileItem
        {
            CoreName = "SwanStation", DisplayName = "SwanStation (Sony PlayStation 1)", Platform = "Sony PS1", Options = new()
            {
                new() { Key = "swanstation_ChromaSmoothing", Title = "Сглаживание цветовых каналов", Description = "Устранение ступенчатости видеороликов", CoreName = "SwanStation", Category = "Видео и палитра", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "swanstation_DitherMode", Title = "Сглаживание полутонов", Description = "Устранение сеточного дизеринга при выводе цвета", CoreName = "SwanStation", Category = "Видео и палитра", Value = "disabled", DefaultValue = "disabled", Options = new() { "native", "disabled" } },
                new() { Key = "swanstation_ColorDepth", Title = "Глубина цвета рендера", Description = "Разрядность цветового буфера при отрисовке кадра", CoreName = "SwanStation", Category = "Видео и палитра", Value = "32-bit", DefaultValue = "32-bit", Options = new() { "16-bit", "32-bit" } },
                new() { Key = "swanstation_ResolutionScale", Title = "Внутреннее разрешение", Description = "Коэффициент увеличения разрешения трехмерных моделей", CoreName = "SwanStation", Category = "Разрешение и фильтрация", Value = "4x", DefaultValue = "4x", Options = new() { "1x", "2x", "3x", "4x", "5x", "8x" } },
                new() { Key = "swanstation_TextureFilter", Title = "Фильтрация текстур", Description = "Метод сглаживания пикселей на поверхностях моделей", CoreName = "SwanStation", Category = "Разрешение и фильтрация", Value = "xBR", DefaultValue = "xBR", Options = new() { "Nearest", "Bilinear", "xBR", "Jinc2" } },
                new() { Key = "swanstation_WidescreenHack", Title = "Широкоформатный обзор", Description = "Рендеринг трехмерной геометрии в формате шестнадцать к девяти", CoreName = "SwanStation", Category = "Разрешение и фильтрация", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "swanstation_PGXP_Enable", Title = "Коррекция геометрии", Description = "Устранение дрожания полигонов оригинальной приставки", CoreName = "SwanStation", Category = "Процессор и точность", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled", "memory_only" } },
                new() { Key = "swanstation_PGXP_TextureCorrection", Title = "Коррекция текстур", Description = "Исправление перспективных искажений текстур в пространстве", CoreName = "SwanStation", Category = "Процессор и точность", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "swanstation_CPUOverclock", Title = "Разгон процессора", Description = "Устранение просадок кадровой частоты в насыщенных сценах", CoreName = "SwanStation", Category = "Процессор и точность", Value = "100%", DefaultValue = "100%", Options = new() { "100%", "150%", "200%", "300%" } },
                new() { Key = "swanstation_AudioInterpolation", Title = "Интерполяция звука", Description = "Сглаживание высокочастотных гармоник звукового процессора", CoreName = "SwanStation", Category = "Аудио и звуковые чипы", Value = "Gaussian", DefaultValue = "Gaussian", Options = new() { "Nearest", "Gaussian", "Cubic" } },
                new() { Key = "swanstation_AudioExpansion", Title = "Расширение стереобазы", Description = "Объемное пространственное звучание игрового мира", CoreName = "SwanStation", Category = "Аудио и звуковые чипы", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "swanstation_AnalogDeadzone", Title = "Мертвая зона аналоговых стиков", Description = "Чувствительность отклонения стиков геймпада", CoreName = "SwanStation", Category = "Ввод и системные функции", Value = "15%", DefaultValue = "15%", Options = new() { "5%", "10%", "15%", "20%" } },
                new() { Key = "swanstation_FastBoot", Title = "Быстрый запуск", Description = "Пропуск анимации приветственного экрана консоли", CoreName = "SwanStation", Category = "Ввод и системные функции", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } }
            }
        });

        // 7. mGBA
        profiles.Add(new CoreProfileItem
        {
            CoreName = "mGBA", DisplayName = "mGBA (Game Boy Advance)", Platform = "Game Boy Advance", Options = new()
            {
                new() { Key = "mgba_color_correction", Title = "Коррекция насыщенности цвета", Description = "Адаптация цветов оригинального экрана портативной консоли", CoreName = "mGBA", Category = "Видео и палитра", Value = "GBA", DefaultValue = "GBA", Options = new() { "Off", "GBA", "GBC", "NDS" } },
                new() { Key = "mgba_solar_sensor", Title = "Датчик солнечного света", Description = "Уровень освещенности для встроенного картриджного датчика", CoreName = "mGBA", Category = "Видео и палитра", Value = "0", DefaultValue = "0", Options = new() { "0", "20", "50", "80", "100" } },
                new() { Key = "mgba_sgb_borders", Title = "Рамки Super Game Boy", Description = "Отображение декоративных тематических рамок оформления", CoreName = "mGBA", Category = "Видео и палитра", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "mgba_frameskip", Title = "Пропуск кадров", Description = "Оптимизация рендеринга на маломощных системах", CoreName = "mGBA", Category = "Разрешение и фильтрация", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "1", "2", "3" } },
                new() { Key = "mgba_interframe_blending", Title = "Межкадровое смешивание", Description = "Устранение эффекта мерцания прозрачных спрайтов", CoreName = "mGBA", Category = "Разрешение и фильтрация", Value = "LCD Ghosting", DefaultValue = "LCD Ghosting", Options = new() { "disabled", "Smart", "LCD Ghosting" } },
                new() { Key = "mgba_bios", Title = "Использование системного BIOS", Description = "Загрузка официального системного файла приставки", CoreName = "mGBA", Category = "Процессор и точность", Value = "auto", DefaultValue = "auto", Options = new() { "auto", "official", "open-source" } },
                new() { Key = "mgba_idle_loop_remove", Title = "Удаление циклов ожидания", Description = "Снижение нагрузки процессора во время пауз", CoreName = "mGBA", Category = "Процессор и точность", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "mgba_audio_buffer_size", Title = "Размер аудио-буфера", Description = "Буферизация звука для предотвращения щелчков", CoreName = "mGBA", Category = "Аудио и звуковые чипы", Value = "1024", DefaultValue = "1024", Options = new() { "512", "1024", "2048" } },
                new() { Key = "mgba_sound_interpolation", Title = "Интерполяция звука", Description = "Сглаживание звучания сэмплов звукового процессора", CoreName = "mGBA", Category = "Аудио и звуковые чипы", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "mgba_sample_rate", Title = "Частота дискретизации", Description = "Частота вывода звука", CoreName = "mGBA", Category = "Аудио и звуковые чипы", Value = "48000", DefaultValue = "48000", Options = new() { "44100", "48000" } },
                new() { Key = "mgba_use_bios", Title = "Вступительная заставка", Description = "Воспроизведение заставки при запуске игр", CoreName = "mGBA", Category = "Ввод и системные функции", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "mgba_rumble", Title = "Вибрация геймпада", Description = "Передача вибрации на вибромоторы геймпада", CoreName = "mGBA", Category = "Ввод и системные функции", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "mgba_tilt_sensor", Title = "Датчик наклона", Description = "Эмуляция гироскопа в поддерживаемых картриджах", CoreName = "mGBA", Category = "Ввод и системные функции", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } }
            }
        });

        // 8. Mupen64Plus-Next
        profiles.Add(new CoreProfileItem
        {
            CoreName = "Mupen64Plus-Next", DisplayName = "Mupen64Plus-Next (Nintendo 64)", Platform = "Nintendo 64", Options = new()
            {
                new() { Key = "mupen64plus-next-fxaa", Title = "Быстрое сглаживание", Description = "Полноэкранный фильтр сглаживания контрастных граней", CoreName = "Mupen64Plus-Next", Category = "Видео и палитра", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "mupen64plus-next-color-depth", Title = "Глубина цвета буфера", Description = "Цветовая точность вывода пикселей", CoreName = "Mupen64Plus-Next", Category = "Видео и палитра", Value = "32-bit", DefaultValue = "32-bit", Options = new() { "16-bit", "32-bit" } },
                new() { Key = "mupen64plus-next-43screensize", Title = "Внутреннее разрешение", Description = "Разрешение трехмерного пространства", CoreName = "Mupen64Plus-Next", Category = "Разрешение и фильтрация", Value = "1920x1440", DefaultValue = "1920x1440", Options = new() { "640x480", "1280x960", "1920x1440", "2560x1920" } },
                new() { Key = "mupen64plus-next-aspect", Title = "Соотношение сторон", Description = "Формат кадра трехмерного окружения", CoreName = "Mupen64Plus-Next", Category = "Разрешение и фильтрация", Value = "4:3", DefaultValue = "4:3", Options = new() { "4:3", "16:9 adjusted", "16:9 widescreen hack" } },
                new() { Key = "mupen64plus-next-anti-aliasing", Title = "Сглаживание краев", Description = "Многовыборочное сглаживание ступенчатости полигонов", CoreName = "Mupen64Plus-Next", Category = "Разрешение и фильтрация", Value = "4x", DefaultValue = "4x", Options = new() { "Off", "2x", "4x", "8x" } },
                new() { Key = "mupen64plus-next-texture-filtering", Title = "Фильтрация текстур", Description = "Интерполяция пикселей поверхностей", CoreName = "Mupen64Plus-Next", Category = "Разрешение и фильтрация", Value = "Automatic", DefaultValue = "Automatic", Options = new() { "None", "Bilinear", "Automatic", "Three-point" } },
                new() { Key = "mupen64plus-next-rdp-plugin", Title = "Графический плагин", Description = "Высокопроизводительный или потактово точный графический процессор", CoreName = "Mupen64Plus-Next", Category = "Процессор и точность", Value = "GLideN64", DefaultValue = "GLideN64", Options = new() { "GLideN64", "Parallel", "Angrylion" } },
                new() { Key = "mupen64plus-next-cpucore", Title = "Тип ядра процессора", Description = "Динамическая рекомпиляция или пошаговый интерпретатор", CoreName = "Mupen64Plus-Next", Category = "Процессор и точность", Value = "dynamic_recompiler", DefaultValue = "dynamic_recompiler", Options = new() { "dynamic_recompiler", "pure_interpreter" } },
                new() { Key = "mupen64plus-next-rsp-plugin", Title = "Плагин сигнального процессора", Description = "Обработка списков команд и звуковых задач", CoreName = "Mupen64Plus-Next", Category = "Процессор и точность", Value = "parallel", DefaultValue = "parallel", Options = new() { "hle", "parallel" } },
                new() { Key = "mupen64plus-next-count-per-op", Title = "Счетчик инструкций процессора", Description = "Тонкая настройка скорости системного таймера", CoreName = "Mupen64Plus-Next", Category = "Процессор и точность", Value = "0", DefaultValue = "0", Options = new() { "0", "1", "2", "3" } },
                new() { Key = "mupen64plus-next-audio-buffer-size", Title = "Буфер аудиопотока", Description = "Длина аудио-очереди звукового сопроцессора", CoreName = "Mupen64Plus-Next", Category = "Аудио и звуковые чипы", Value = "2048", DefaultValue = "2048", Options = new() { "1024", "2048", "4096" } },
                new() { Key = "mupen64plus-next-pak1", Title = "Устройство первого порта", Description = "Контроллерный аксессуар памяти или вибрации", CoreName = "Mupen64Plus-Next", Category = "Ввод и системные функции", Value = "memory", DefaultValue = "memory", Options = new() { "none", "memory", "rumble" } },
                new() { Key = "mupen64plus-next-analog-deadzone", Title = "Мертвая зона стика", Description = "Зона покоя аналоговой рукоятки", CoreName = "Mupen64Plus-Next", Category = "Ввод и системные функции", Value = "10", DefaultValue = "10", Options = new() { "5", "10", "15", "20" } }
            }
        });

        // 9. Flycast
        profiles.Add(new CoreProfileItem
        {
            CoreName = "Flycast", DisplayName = "Flycast (Sega Dreamcast)", Platform = "Sega Dreamcast", Options = new()
            {
                new() { Key = "flycast_shadows", Title = "Отрисовка теней", Description = "Аппаратная обработка карт теней персонажей", CoreName = "Flycast", Category = "Видео и палитра", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "flycast_fog", Title = "Эмуляция тумана", Description = "Атмосферная глубина дистанции и туман", CoreName = "Flycast", Category = "Видео и палитра", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "flycast_texupscale", Title = "Апскейл двухмерных текстур", Description = "Сглаживание плоских текстур и элементов интерфейса", CoreName = "Flycast", Category = "Видео и палитра", Value = "xBRZ-2x", DefaultValue = "xBRZ-2x", Options = new() { "off", "xBRZ-2x", "xBRZ-4x" } },
                new() { Key = "flycast_cable_type", Title = "Тип видеокабеля", Description = "Режим телевизионного кабельного соединения", CoreName = "Flycast", Category = "Видео и палитра", Value = "VGA (RGB)", DefaultValue = "VGA (RGB)", Options = new() { "VGA (RGB)", "Composite", "S-Video" } },
                new() { Key = "flycast_internal_resolution", Title = "Внутреннее разрешение", Description = "Коэффициент апскейла трехмерной геометрии", CoreName = "Flycast", Category = "Разрешение и фильтрация", Value = "1440x1080", DefaultValue = "1440x1080", Options = new() { "640x480", "1280x960", "1440x1080", "1920x1440", "2880x2160" } },
                new() { Key = "flycast_widescreen_hack", Title = "Широкоформатный обзор", Description = "Рендеринг трехмерных сцен без черных полос по бокам", CoreName = "Flycast", Category = "Разрешение и фильтрация", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "flycast_anisotropic_filtering", Title = "Анизотропная фильтрация", Description = "Четкость текстур под острыми углами обзора", CoreName = "Flycast", Category = "Разрешение и фильтрация", Value = "4x", DefaultValue = "4x", Options = new() { "off", "2x", "4x", "8x", "16x" } },
                new() { Key = "flycast_div_matching", Title = "Точность деления процессора", Description = "Стабильность работы физики и игровой логики", CoreName = "Flycast", Category = "Процессор и точность", Value = "auto", DefaultValue = "auto", Options = new() { "auto", "fast", "accurate" } },
                new() { Key = "flycast_dsp", Title = "Сигнальный процессор звука", Description = "Пространственные эффекты реверберации и эха", CoreName = "Flycast", Category = "Аудио и звуковые чипы", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "flycast_audio_buffer_size", Title = "Буфер звукового потока", Description = "Размер очереди сэмплов звукового процессора", CoreName = "Flycast", Category = "Аудио и звуковые чипы", Value = "2048", DefaultValue = "2048", Options = new() { "1024", "2048", "4096" } },
                new() { Key = "flycast_digital_triggers", Title = "Цифровые триггеры", Description = "Мгновенное срабатывание нижних курков", CoreName = "Flycast", Category = "Ввод и системные функции", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "flycast_vmu_screen", Title = "Экран карты памяти", Description = "Вывод мини-экрана виртуальной карты памяти на основной дисплей", CoreName = "Flycast", Category = "Ввод и системные функции", Value = "top-left", DefaultValue = "top-left", Options = new() { "disabled", "top-left", "top-right", "bottom-left" } },
                new() { Key = "flycast_region", Title = "Регион приставки", Description = "Системный регион консоли", CoreName = "Flycast", Category = "Ввод и системные функции", Value = "auto", DefaultValue = "auto", Options = new() { "auto", "USA", "Europe", "Japan" } }
            }
        });

        // 10. PPSSPP
        profiles.Add(new CoreProfileItem
        {
            CoreName = "PPSSPP", DisplayName = "PPSSPP (Sony PlayStation Portable)", Platform = "PSP", Options = new()
            {
                new() { Key = "ppsspp_color_depth", Title = "Глубина цвета", Description = "Цветовая разрядность графического буфера", CoreName = "PPSSPP", Category = "Видео и палитра", Value = "32-bit", DefaultValue = "32-bit", Options = new() { "16-bit", "32-bit" } },
                new() { Key = "ppsspp_rendering_resolution", Title = "Разрешение рендеринга", Description = "Множитель оригинального разрешения экрана портативной консоли", CoreName = "PPSSPP", Category = "Разрешение и фильтрация", Value = "4x", DefaultValue = "4x", Options = new() { "1x", "2x", "3x", "4x", "5x", "8x" } },
                new() { Key = "ppsspp_texture_filtering", Title = "Фильтрация текстур", Description = "Интерполяция пикселей поверхностей", CoreName = "PPSSPP", Category = "Разрешение и фильтрация", Value = "auto", DefaultValue = "auto", Options = new() { "auto", "nearest", "linear", "linear_on_fmv" } },
                new() { Key = "ppsspp_texture_scaling_type", Title = "Алгоритм апскейла текстур", Description = "Метод масштабирования текстурных атласов", CoreName = "PPSSPP", Category = "Разрешение и фильтрация", Value = "xbrz", DefaultValue = "xbrz", Options = new() { "off", "xbrz", "hybrid", "bicubic" } },
                new() { Key = "ppsspp_texture_scaling_level", Title = "Уровень апскейла текстур", Description = "Степень увеличения детализации плоских текстур", CoreName = "PPSSPP", Category = "Разрешение и фильтрация", Value = "2x", DefaultValue = "2x", Options = new() { "1x", "2x", "3x", "4x", "5x" } },
                new() { Key = "ppsspp_hardware_transform", Title = "Аппаратное преобразование координат", Description = "Использование видеокарты для расчета вершин геометрии", CoreName = "PPSSPP", Category = "Процессор и точность", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "ppsspp_software_skinning", Title = "Программный скиннинг моделей", Description = "Обработка скелетной анимации персонажей", CoreName = "PPSSPP", Category = "Процессор и точность", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "ppsspp_cpu_clock", Title = "Частота процессора", Description = "Тактовая частота центрального процессора приставки", CoreName = "PPSSPP", Category = "Процессор и точность", Value = "auto", DefaultValue = "auto", Options = new() { "auto", "222", "333", "444" } },
                new() { Key = "ppsspp_fast_memory", Title = "Быстрый доступ к памяти", Description = "Прямая адресация оперативной памяти", CoreName = "PPSSPP", Category = "Процессор и точность", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "ppsspp_sound_speed_hack", Title = "Оптимизация звука", Description = "Снижение задержки звучания при падении частоты кадров", CoreName = "PPSSPP", Category = "Аудио и звуковые чипы", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "ppsspp_audio_resampler", Title = "Аудио ресемплер", Description = "Алгоритм приведения звукового потока к единой частоте", CoreName = "PPSSPP", Category = "Аудио и звуковые чипы", Value = "gaussian", DefaultValue = "gaussian", Options = new() { "linear", "gaussian", "cubic" } },
                new() { Key = "ppsspp_analog_deadzone", Title = "Мертвая зона аналогового стика", Description = "Зона нечувствительности координатного стика", CoreName = "PPSSPP", Category = "Ввод и системные функции", Value = "15%", DefaultValue = "15%", Options = new() { "5%", "10%", "15%", "20%" } },
                new() { Key = "ppsspp_button_style", Title = "Стиль обозначения кнопок", Description = "Отображение графических символов элементов интерфейса", CoreName = "PPSSPP", Category = "Ввод и системные функции", Value = "classic", DefaultValue = "classic", Options = new() { "classic", "flat", "glowing" } }
            }
        });

        // 11. melonDS
        profiles.Add(new CoreProfileItem
        {
            CoreName = "melonDS", DisplayName = "melonDS (Nintendo DS)", Platform = "Nintendo DS", Options = new()
            {
                new() { Key = "melonds_screen_layout", Title = "Расположение двух экранов", Description = "Компоновка верхнего и нижнего экранов консоли", CoreName = "melonDS", Category = "Видео и палитра", Value = "top/bottom", DefaultValue = "top/bottom", Options = new() { "top/bottom", "left/right", "top-only", "bottom-only" } },
                new() { Key = "melonds_screen_ratio", Title = "Соотношение размеров экранов", Description = "Пропорциональный размер основного и сенсорного дисплеев", CoreName = "melonDS", Category = "Разрешение и фильтрация", Value = "1:1", DefaultValue = "1:1", Options = new() { "1:1", "top-emphasized", "bottom-emphasized" } },
                new() { Key = "melonds_screen_gap", Title = "Зазор между экранами", Description = "Расстояние между верхним и нижним дисплеями", CoreName = "melonDS", Category = "Разрешение и фильтрация", Value = "0", DefaultValue = "0", Options = new() { "0", "8", "64", "90" } },
                new() { Key = "melonds_internal_resolution", Title = "Внутреннее разрешение", Description = "Множитель разрешения трехмерных объектов", CoreName = "melonDS", Category = "Разрешение и фильтрация", Value = "2x", DefaultValue = "2x", Options = new() { "1x", "2x", "3x", "4x" } },
                new() { Key = "melonds_render_3d", Title = "Трехмерный рендерер", Description = "Программный потактовый или аппаратно-ускоренный рендеринг", CoreName = "melonDS", Category = "Процессор и точность", Value = "software", DefaultValue = "software", Options = new() { "software", "opengl" } },
                new() { Key = "melonds_console_type", Title = "Ревизия приставки", Description = "Модель портативной приставки", CoreName = "melonDS", Category = "Процессор и точность", Value = "DS", DefaultValue = "DS", Options = new() { "DS", "DSi" } },
                new() { Key = "melonds_jit_enable", Title = "Динамическая рекомпиляция кода", Description = "Ускорение выполнения инструкций микропроцессора", CoreName = "melonDS", Category = "Процессор и точность", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "melonds_audio_interpolation", Title = "Интерполяция звука", Description = "Сглаживание ступенчатости звукового тракта", CoreName = "melonDS", Category = "Аудио и звуковые чипы", Value = "gaussian", DefaultValue = "gaussian", Options = new() { "none", "linear", "gaussian" } },
                new() { Key = "melonds_audio_bitrate", Title = "Качество звукового вывода", Description = "Разрядность обработки цифрового звука", CoreName = "melonDS", Category = "Аудио и звуковые чипы", Value = "16-bit", DefaultValue = "16-bit", Options = new() { "10-bit", "16-bit" } },
                new() { Key = "melonds_touch_mode", Title = "Управление сенсорным экраном", Description = "Способ взаимодействия с нижним дисплеем", CoreName = "melonDS", Category = "Ввод и системные функции", Value = "mouse", DefaultValue = "mouse", Options = new() { "mouse", "touch", "joystick" } },
                new() { Key = "melonds_firmware", Title = "Встроенная прошивка", Description = "Использование системной прошивки приставки", CoreName = "melonDS", Category = "Ввод и системные функции", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "melonds_mic_input", Title = "Ввод микрофона", Description = "Сигнал для распознавания звуков микрофона", CoreName = "melonDS", Category = "Ввод и системные функции", Value = "blow", DefaultValue = "blow", Options = new() { "none", "blow", "white-noise" } },
                new() { Key = "melonds_lid_closed", Title = "Закрытие крышки", Description = "Эмуляция закрытия створки портативной консоли", CoreName = "melonDS", Category = "Ввод и системные функции", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "enabled" } }
            }
        });

        // 12. PCSX2
        profiles.Add(new CoreProfileItem
        {
            CoreName = "PCSX2", DisplayName = "PCSX2 (Sony PlayStation 2)", Platform = "Sony PS2", Options = new()
            {
                new() { Key = "pcsx2_interlacing", Title = "Чересстрочная развертка", Description = "Алгоритм устранения чересстрочного дрожания картинки", CoreName = "PCSX2", Category = "Видео и палитра", Value = "Automatic", DefaultValue = "Automatic", Options = new() { "None", "Automatic", "Bob", "Weave" } },
                new() { Key = "pcsx2_antiblur", Title = "Подавление размытия", Description = "Устранение встроенного консольного блюра", CoreName = "PCSX2", Category = "Видео и палитра", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "pcsx2_crc_hack_level", Title = "Уровень графических исправлений", Description = "Автоматическое отключение конфликтующих эффектов", CoreName = "PCSX2", Category = "Видео и палитра", Value = "Automatic", DefaultValue = "Automatic", Options = new() { "None", "Minimum", "Automatic", "Aggressive" } },
                new() { Key = "pcsx2_upscale_multiplier", Title = "Масштабирование разрешения", Description = "Множитель исходного разрешения консоли", CoreName = "PCSX2", Category = "Разрешение и фильтрация", Value = "3x", DefaultValue = "3x", Options = new() { "1x", "2x", "3x", "4x", "6x", "8x" } },
                new() { Key = "pcsx2_texture_filtering", Title = "Анизотропная фильтрация", Description = "Улучшение четкости наклонных текстур", CoreName = "PCSX2", Category = "Разрешение и фильтрация", Value = "16x", DefaultValue = "16x", Options = new() { "off", "2x", "4x", "8x", "16x" } },
                new() { Key = "pcsx2_widescreen_patches", Title = "Широкоэкранные патчи", Description = "Автоматические широкоформатные исправления геометрии", CoreName = "PCSX2", Category = "Разрешение и фильтрация", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "pcsx2_ee_cycle_rate", Title = "Частота процессора", Description = "Тонкая настройка тактов центрального процессора", CoreName = "PCSX2", Category = "Процессор и точность", Value = "100%", DefaultValue = "100%", Options = new() { "100%", "130%", "180%", "300%" } },
                new() { Key = "pcsx2_vu_flag_hack", Title = "Оптимизация векторного сопроцессора", Description = "Ускорение расчетов трехмерных трансформаций", CoreName = "PCSX2", Category = "Процессор и точность", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "pcsx2_accurate_blending", Title = "Точность смешивания полупрозрачности", Description = "Попиксельная точность наложения полупрозрачных слоев", CoreName = "PCSX2", Category = "Процессор и точность", Value = "Medium", DefaultValue = "Medium", Options = new() { "Minimum", "Medium", "High", "Maximum" } },
                new() { Key = "pcsx2_audio_interpolation", Title = "Интерполяция аудио", Description = "Метод сглаживания звучания звукового сопроцессора", CoreName = "PCSX2", Category = "Аудио и звуковые чипы", Value = "Gaussian", DefaultValue = "Gaussian", Options = new() { "None", "Linear", "Gaussian", "Cubic" } },
                new() { Key = "pcsx2_audio_sync_mode", Title = "Синхронизация звука", Description = "Алгоритм выравнивания темпа звука и кадровой частоты", CoreName = "PCSX2", Category = "Аудио и звуковые чипы", Value = "TimeStretch", DefaultValue = "TimeStretch", Options = new() { "TimeStretch", "Async", "Mix" } },
                new() { Key = "pcsx2_fastboot", Title = "Быстрый старт", Description = "Пропуск стартовой заставки консоли", CoreName = "PCSX2", Category = "Ввод и системные функции", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "pcsx2_analog_deadzone", Title = "Мертвая зона стиков", Description = "Область покоя аналоговых рукояток геймпада", CoreName = "PCSX2", Category = "Ввод и системные функции", Value = "10%", DefaultValue = "10%", Options = new() { "5%", "10%", "15%", "20%" } }
            }
        });

        // 13. FinalBurn Neo
        profiles.Add(new CoreProfileItem
        {
            CoreName = "FinalBurn Neo", DisplayName = "FinalBurn Neo (Arcade / Neo Geo)", Platform = "Arcade", Options = new()
            {
                new() { Key = "fbneo-lightgun-crosshair", Title = "Прицел светового пистолета", Description = "Отображение маркера наведения пушки", CoreName = "FinalBurn Neo", Category = "Видео и палитра", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "fbneo-color-depth", Title = "Глубина цвета видеокадра", Description = "Разрядность цветового буфера аркадной платы", CoreName = "FinalBurn Neo", Category = "Видео и палитра", Value = "32-bit", DefaultValue = "32-bit", Options = new() { "16-bit", "32-bit" } },
                new() { Key = "fbneo-vertical-mode", Title = "Вертикальный поворот экрана", Description = "Поворот изображения для вертикальных аркадных автоматов", CoreName = "FinalBurn Neo", Category = "Разрешение и фильтрация", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "enabled", "alternate" } },
                new() { Key = "fbneo-aspect", Title = "Соотношение сторон", Description = "Пропорции кинескопа аркадного кабинета", CoreName = "FinalBurn Neo", Category = "Разрешение и фильтрация", Value = "auto", DefaultValue = "auto", Options = new() { "auto", "4:3", "3:4", "16:9" } },
                new() { Key = "fbneo-frameskip", Title = "Пропуск кадров", Description = "Пропуск промежуточных кадров на слабых конфигурациях", CoreName = "FinalBurn Neo", Category = "Разрешение и фильтрация", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "1", "2" } },
                new() { Key = "fbneo-cpu-speed-adjust", Title = "Разгон аркадного процессора", Description = "Устранение просадок кадровой частоты в масштабных перестрелках", CoreName = "FinalBurn Neo", Category = "Процессор и точность", Value = "100%", DefaultValue = "100%", Options = new() { "100%", "120%", "150%", "200%" } },
                new() { Key = "fbneo-sample-interpolation", Title = "Интерполяция сэмплов", Description = "Сглаживание звучания оцифрованных звуковых дорожек", CoreName = "FinalBurn Neo", Category = "Аудио и звуковые чипы", Value = "4-point 3rd order", DefaultValue = "4-point 3rd order", Options = new() { "disabled", "linear", "4-point 3rd order" } },
                new() { Key = "fbneo-fm-interpolation", Title = "Интерполяция синтезатора звука", Description = "Качество дискретизации звукового чипа синтеза", CoreName = "FinalBurn Neo", Category = "Аудио и звуковые чипы", Value = "4-point 3rd order", DefaultValue = "4-point 3rd order", Options = new() { "disabled", "linear", "4-point 3rd order" } },
                new() { Key = "fbneo-samplerate", Title = "Частота дискретизации аудио", Description = "Частота звукового потока аркадной платы", CoreName = "FinalBurn Neo", Category = "Аудио и звуковые чипы", Value = "48000", DefaultValue = "48000", Options = new() { "44100", "48000" } },
                new() { Key = "fbneo-hiscores", Title = "Сохранение рекордов", Description = "Автоматическое сохранение таблицы лучших очков", CoreName = "FinalBurn Neo", Category = "Ввод и системные функции", Value = "enabled", DefaultValue = "enabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "fbneo-neogeo-mode", Title = "Режим платформы Neo Geo", Description = "Переключение между домашней консолью и платным автоматом", CoreName = "FinalBurn Neo", Category = "Ввод и системные функции", Value = "AES", DefaultValue = "AES", Options = new() { "MVS", "AES", "UNIBIOS" } },
                new() { Key = "fbneo-diagnostic-input", Title = "Вход в сервисное меню", Description = "Комбинация для открытия сервисных настроек автомата", CoreName = "FinalBurn Neo", Category = "Ввод и системные функции", Value = "Hold Start", DefaultValue = "Hold Start", Options = new() { "None", "Hold Start", "Start + A + B" } },
                new() { Key = "fbneo-dipswitch", Title = "Сложность и количество жизней", Description = "Установка переключателей сложности автомата", CoreName = "FinalBurn Neo", Category = "Ввод и системные функции", Value = "Default", DefaultValue = "Default", Options = new() { "Default", "Easy", "Hard", "Expert" } }
            }
        });

        // 14. Stella
        profiles.Add(new CoreProfileItem
        {
            CoreName = "Stella", DisplayName = "Stella (Atari 2600)", Platform = "Atari 2600", Options = new()
            {
                new() { Key = "stella_palette", Title = "Цветовая палитра", Description = "Цветовой профиль классического видеочипа", CoreName = "Stella", Category = "Видео и палитра", Value = "standard", DefaultValue = "standard", Options = new() { "standard", "z26", "secam", "custom" } },
                new() { Key = "stella_color_loss", Title = "Обесцвечивание сигнала", Description = "Имитация частичной потери цвета на старых кинескопах", CoreName = "Stella", Category = "Видео и палитра", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "stella_phosphor", Title = "Свечение люминофора", Description = "Послесвечение пикселей кинескопного экрана", CoreName = "Stella", Category = "Видео и палитра", Value = "auto", DefaultValue = "auto", Options = new() { "disabled", "auto", "always" } },
                new() { Key = "stella_aspect_ratio", Title = "Соотношение сторон кадра", Description = "Пропорции телевизионного экрана", CoreName = "Stella", Category = "Разрешение и фильтрация", Value = "auto", DefaultValue = "auto", Options = new() { "auto", "4:3", "corrected" } },
                new() { Key = "stella_crop_hoverscan", Title = "Горизонтальная обрезка", Description = "Скрытие краевых артефактов телевизионной развертки", CoreName = "Stella", Category = "Разрешение и фильтрация", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "stella_filter", Title = "Видеофильтр кинескопа", Description = "Сглаживание телевизионных строк развертки", CoreName = "Stella", Category = "Разрешение и фильтрация", Value = "composite", DefaultValue = "composite", Options = new() { "composite", "svideo", "rgb", "bad adjust" } },
                new() { Key = "stella_tv_jitter", Title = "Дрожание телевизионной синхронизации", Description = "Эмуляция срыва кадровой развертки в нестандартных играх", CoreName = "Stella", Category = "Процессор и точность", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "stella_audio_sample_rate", Title = "Частота дискретизации аудио", Description = "Частота звукового процессора", CoreName = "Stella", Category = "Аудио и звуковые чипы", Value = "48000", DefaultValue = "48000", Options = new() { "31400", "44100", "48000" } },
                new() { Key = "stella_stereo", Title = "Стереофонический режим", Description = "Разделение двух звуковых каналов приставки по панораме", CoreName = "Stella", Category = "Аудио и звуковые чипы", Value = "disabled", DefaultValue = "disabled", Options = new() { "disabled", "enabled" } },
                new() { Key = "stella_console", Title = "Тип телевизионного стандарта", Description = "Американский или европейский формат приставки", CoreName = "Stella", Category = "Ввод и системные функции", Value = "auto", DefaultValue = "auto", Options = new() { "auto", "ntsc", "pal", "secam" } },
                new() { Key = "stella_paddle_sensitivity", Title = "Чувствительность дискового контроллера", Description = "Скорость реакции поворотного манипулятора", CoreName = "Stella", Category = "Ввод и системные функции", Value = "50", DefaultValue = "50", Options = new() { "25", "50", "75", "100" } },
                new() { Key = "stella_left_diff", Title = "Переключатель сложности левого игрока", Description = "Аппаратный тумблер уровня сложности", CoreName = "Stella", Category = "Ввод и системные функции", Value = "B", DefaultValue = "B", Options = new() { "A", "B" } },
                new() { Key = "stella_right_diff", Title = "Переключатель сложности правого игрока", Description = "Аппаратный тумблер уровня сложности", CoreName = "Stella", Category = "Ввод и системные функции", Value = "B", DefaultValue = "B", Options = new() { "A", "B" } }
            }
        });

        foreach (var p in profiles)
        {
            foreach (var opt in p.Options)
            {
                opt.EnsureValueInOptions();
            }
        }
        return profiles;
    }

    public static void LoadCoreOptionsFromDisk(string coreName, IEnumerable<CoreOptionItem> options)
    {
        string optPath = Path.Combine(ConfigDir, coreName, coreName + ".opt");
        if (!File.Exists(optPath)) return;

        try
        {
            var lines = File.ReadAllLines(optPath, Encoding.UTF8);
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

            foreach (var opt in options)
            {
                if (map.TryGetValue(opt.Key, out var diskVal))
                {
                    opt.Value = diskVal;
                }
            }
        }
        catch { }
    }

    public static void SaveCoreOptionsToDisk(string coreName, IEnumerable<CoreOptionItem> options)
    {
        string coreDir = Path.Combine(ConfigDir, coreName);
        if (!Directory.Exists(coreDir)) Directory.CreateDirectory(coreDir);

        string optPath = Path.Combine(coreDir, coreName + ".opt");
        var sb = new StringBuilder();

        foreach (var opt in options)
        {
            sb.AppendLine($"{opt.Key} = \"{opt.Value}\"");
        }

        File.WriteAllText(optPath, sb.ToString(), Encoding.UTF8);
    }

    public static void SaveCoreOverrideCfg(string coreName, Dictionary<string, string> overrideSettings)
    {
        string coreDir = Path.Combine(ConfigDir, coreName);
        if (!Directory.Exists(coreDir)) Directory.CreateDirectory(coreDir);

        string cfgPath = Path.Combine(coreDir, coreName + ".cfg");
        var sb = new StringBuilder();

        foreach (var kvp in overrideSettings)
        {
            sb.AppendLine($"{kvp.Key} = \"{kvp.Value}\"");
        }

        File.WriteAllText(cfgPath, sb.ToString(), Encoding.UTF8);
    }

    public static void ResetCoreOptions(string coreName)
    {
        string optPath = Path.Combine(ConfigDir, coreName, coreName + ".opt");
        if (File.Exists(optPath))
        {
            try { File.Delete(optPath); } catch { }
        }
    }

    // Custom Shader Chain Builder
    public static string SaveCustomShaderChain(string pass1Rel, string pass2Rel, float curvature, float bloom, float sharpness, float scanlines)
    {
        string customChainPath = Path.Combine(ShadersDir, "storm_custom_chain.slangp");

        var sb = new StringBuilder();
        sb.AppendLine("shaders = 2");
        sb.AppendLine($"shader0 = \"{pass1Rel}\"");
        sb.AppendLine("filter_linear0 = false");
        sb.AppendLine("scale_type0 = source");
        sb.AppendLine("scale0 = 2.0");
        sb.AppendLine($"shader1 = \"{pass2Rel}\"");
        sb.AppendLine("filter_linear1 = true");
        sb.AppendLine($"parameters = \"CURVATURE;BLOOM;SHARPNESS;SCANLINE_WEIGHT\"");
        sb.AppendLine($"CURVATURE = \"{curvature:0.000000}\"");
        sb.AppendLine($"BLOOM = \"{bloom:0.000000}\"");
        sb.AppendLine($"SHARPNESS = \"{sharpness:0.000000}\"");
        sb.AppendLine($"SCANLINE_WEIGHT = \"{scanlines:0.000000}\"");

        File.WriteAllText(customChainPath, sb.ToString(), Encoding.UTF8);

        // Apply to retroarch.cfg
        RetroArchConfigManager.SetString("video_shader_enable", "true");
        RetroArchConfigManager.SetString("video_shader", customChainPath);
        RetroArchConfigManager.Save();

        return customChainPath;
    }
}
