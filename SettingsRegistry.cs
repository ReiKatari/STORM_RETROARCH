using System;
using System.Collections.Generic;

namespace StormRetroArch;

public static class SettingsRegistry
{
    public static List<SettingItem> CreateAllSettings()
    {
        var list = new List<SettingItem>();

        // 1. DRIVERS (Драйверы)
        list.Add(new SettingItem
        {
            CategoryId = "drivers", CategoryName = "Драйверы", CategoryIcon = "🎮",
            Title = "Видеодрайвер", CfgKey = "video_driver", Kind = SettingControlKind.ComboBox,
            Description = "Графический API вывода видео (Vulkan рекомендуется для современных GPU с минимальной задержкой).",
            Options = new() { "vulkan", "glcore", "d3d11", "d3d12", "gl", "gdi" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "drivers", CategoryName = "Драйверы", CategoryIcon = "🎮",
            Title = "Драйвер меню", CfgKey = "menu_driver", Kind = SettingControlKind.ComboBox,
            Description = "Графический интерфейс меню эмулятора (Ozone, XMB PS3-стиль, RGUI пиксельный).",
            Options = new() { "ozone", "xmb", "glui", "rgui" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "drivers", CategoryName = "Драйверы", CategoryIcon = "🎮",
            Title = "Аудиодрайвер", CfgKey = "audio_driver", Kind = SettingControlKind.ComboBox,
            Description = "Звуковая подсистема вывода (WASAPI Exclusive гарантирует задержку менее 10 мс).",
            Options = new() { "wasapi", "xaudio", "dsound", "sdl2" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "drivers", CategoryName = "Драйверы", CategoryIcon = "🎮",
            Title = "Аудио ресемплер", CfgKey = "audio_resampler", Kind = SettingControlKind.ComboBox,
            Description = "Алгоритм передискретизации частоты звука (Sinc — наивысшее качество).",
            Options = new() { "sinc", "cc", "nearest" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "drivers", CategoryName = "Драйверы", CategoryIcon = "🎮",
            Title = "Драйвер ввода клавиатуры/мыши", CfgKey = "input_driver", Kind = SettingControlKind.ComboBox,
            Description = "Низкоуровневый перехват ввода (Raw Input обходит очереди сообщений Windows).",
            Options = new() { "raw", "dinput", "xinput", "sdl2" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "drivers", CategoryName = "Драйверы", CategoryIcon = "🎮",
            Title = "Драйвер геймпада", CfgKey = "input_joypad_driver", Kind = SettingControlKind.ComboBox,
            Description = "Протокол опроса контроллеров (XInput для Xbox/совместимых, DInput для ретро).",
            Options = new() { "xinput", "dinput", "sdl2", "hid" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "drivers", CategoryName = "Драйверы", CategoryIcon = "🎮",
            Title = "Драйвер видеозаписи", CfgKey = "record_driver", Kind = SettingControlKind.ComboBox,
            Description = "Кодировщик для захвата видео и потоковой трансляции.",
            Options = new() { "null", "ffmpeg" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "drivers", CategoryName = "Драйверы", CategoryIcon = "🎮",
            Title = "MIDI драйвер", CfgKey = "midi_driver", Kind = SettingControlKind.ComboBox,
            Description = "Подсистема синтеза ретро-музыки General MIDI / MT-32.",
            Options = new() { "winmm", "null" }
        });

        // 2. VIDEO (Видео)
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", CategoryIcon = "🖥️",
            Title = "Полноэкранный режим", CfgKey = "video_fullscreen", Kind = SettingControlKind.Toggle,
            Description = "Запуск эмулятора на весь экран монитора."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", CategoryIcon = "🖥️",
            Title = "Полноэкранное окно без рамок", CfgKey = "video_windowed_fullscreen", Kind = SettingControlKind.Toggle,
            Description = "Borderless Windowed режим для мгновенного переключения по Alt+Tab."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", CategoryIcon = "🖥️",
            Title = "Целочисленное масштабирование 1:1", CfgKey = "video_scale_integer", Kind = SettingControlKind.Toggle,
            Description = "Отображение пикселей строго целыми кратными коэффициентами без размытия."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", CategoryIcon = "🖥️",
            Title = "Билинейная фильтрация", CfgKey = "video_smooth", Kind = SettingControlKind.Toggle,
            Description = "Аппаратное сглаживание пикселей (отключите для аутентичного ретро-пиксельарта)."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", CategoryIcon = "🖥️",
            Title = "Включить шейдеры", CfgKey = "video_shader_enable", Kind = SettingControlKind.Toggle,
            Description = "Активация CRT/LCD фильтров, сканлайнов и постобработки."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", CategoryIcon = "🖥️",
            Title = "Вертикальная синхронизация VSync", CfgKey = "video_vsync", Kind = SettingControlKind.Toggle,
            Description = "Устранение разрывов кадров (Tearing) синхронизацией с частотой экрана."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", CategoryIcon = "🖥️",
            Title = "Адаптивный VSync", CfgKey = "video_adaptive_vsync", Kind = SettingControlKind.Toggle,
            Description = "Отключение VSync при просадках FPS ниже частоты развёртки монитора."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", CategoryIcon = "🖥️",
            Title = "Поддержка VRR (G-Sync / FreeSync)", CfgKey = "vrr_runloop_enable", Kind = SettingControlKind.Toggle,
            Description = "Синхронизация цикла эмуляции с переменной частотой обновления дисплея."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", CategoryIcon = "🖥️",
            Title = "Жёсткая синхронизация GPU (Hard Sync)", CfgKey = "video_hard_sync", Kind = SettingControlKind.Toggle,
            Description = "Синхронизация CPU и GPU строго на 1 кадр для подавления задержки ввода."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", CategoryIcon = "🖥️",
            Title = "Многопоточное видео", CfgKey = "video_threaded", Kind = SettingControlKind.Toggle,
            Description = "Вынос графического рендера в отдельный системный поток (увеличивает задержку на 1 кадр)."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", CategoryIcon = "🖥️",
            Title = "Отображать счётчик FPS", CfgKey = "fps_show", Kind = SettingControlKind.Toggle,
            Description = "Показывать текущую частоту кадров в углу экрана."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", CategoryIcon = "🖥️",
            Title = "Отображать потребление памяти", CfgKey = "memory_show", Kind = SettingControlKind.Toggle,
            Description = "Мониторинг выделенной оперативной памяти процессом эмулятора."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", CategoryIcon = "🖥️",
            Title = "Соотношение сторон", CfgKey = "aspect_ratio_index", Kind = SettingControlKind.ComboBox,
            Description = "Пропорции экрана рендеринга (4:3 ретро, 16:9 широкий, 1:1 пиксель).",
            Options = new() { "0", "1", "2", "3", "4" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", CategoryIcon = "🖥️",
            Title = "Буфер цепочки подкачки (Swapchain)", CfgKey = "video_max_swapchain_images", Kind = SettingControlKind.Slider,
            Description = "Количество экранных буферов (2 = двойная буферизация, 3 = тройная).",
            Min = 1, Max = 4, Step = 1, Unit = "буфера"
        });

        // 3. AUDIO (Аудио)
        list.Add(new SettingItem
        {
            CategoryId = "audio", CategoryName = "Аудио", CategoryIcon = "🔊",
            Title = "Включить звук", CfgKey = "audio_enable", Kind = SettingControlKind.Toggle,
            Description = "Глобальное включение эмуляции аудиочипов и вывода звука."
        });
        list.Add(new SettingItem
        {
            CategoryId = "audio", CategoryName = "Аудио", CategoryIcon = "🔊",
            Title = "Синхронизация звука", CfgKey = "audio_sync", Kind = SettingControlKind.Toggle,
            Description = "Синхронизация скорости эмуляции по аудиотаймингам для исключения щелчков."
        });
        list.Add(new SettingItem
        {
            CategoryId = "audio", CategoryName = "Аудио", CategoryIcon = "🔊",
            Title = "WASAPI Exclusive Mode", CfgKey = "audio_wasapi_exclusive_mode", Kind = SettingControlKind.Toggle,
            Description = "Монопольный низкоуровневый доступ к аудиокарте с ультранизкой задержкой."
        });
        list.Add(new SettingItem
        {
            CategoryId = "audio", CategoryName = "Аудио", CategoryIcon = "🔊",
            Title = "Звуковые эффекты меню", CfgKey = "audio_enable_menu", Kind = SettingControlKind.Toggle,
            Description = "Аудиоотклик при навигации и выборе пунктов в интерфейсе RetroArch."
        });
        list.Add(new SettingItem
        {
            CategoryId = "audio", CategoryName = "Аудио", CategoryIcon = "🔊",
            Title = "Задержка аудио (Latency)", CfgKey = "audio_latency", Kind = SettingControlKind.Slider,
            Description = "Размер аудио-буфера в миллисекундах (32–64 мс оптимально для WASAPI).",
            Min = 8, Max = 128, Step = 8, Unit = "мс"
        });
        list.Add(new SettingItem
        {
            CategoryId = "audio", CategoryName = "Аудио", CategoryIcon = "🔊",
            Title = "Частота дискретизации", CfgKey = "audio_out_rate", Kind = SettingControlKind.ComboBox,
            Description = "Выходная частота цифро-аналогового преобразования.",
            Options = new() { "44100", "48000", "96000", "192000" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "audio", CategoryName = "Аудио", CategoryIcon = "🔊",
            Title = "Громкость звука", CfgKey = "audio_volume", Kind = SettingControlKind.Slider,
            Description = "Регулировка усиления аудиосигнала (в децибелах от -20 до +12 dB).",
            Min = -20, Max = 12, Step = 1, Unit = "dB"
        });

        // 4. INPUT & HOTKEYS (Ввод и Клавиши)
        list.Add(new SettingItem
        {
            CategoryId = "input", CategoryName = "Ввод и Клавиши", CategoryIcon = "🕹️",
            Title = "Автоопределение геймпадов", CfgKey = "input_autodetect_enable", Kind = SettingControlKind.Toggle,
            Description = "Автоматическая загрузка профилей autoconfig при подключении геймпада."
        });
        list.Add(new SettingItem
        {
            CategoryId = "input", CategoryName = "Ввод и Клавиши", CategoryIcon = "🕹️",
            Title = "Вибрация геймпада (Rumble)", CfgKey = "enable_device_vibration", Kind = SettingControlKind.Toggle,
            Description = "Передача тактильной отдачи и эффектов вибрации в моторчики контроллера."
        });
        list.Add(new SettingItem
        {
            CategoryId = "input", CategoryName = "Ввод и Клавиши", CategoryIcon = "🕹️",
            Title = "Двойное нажатие для выхода", CfgKey = "quit_press_twice", Kind = SettingControlKind.Toggle,
            Description = "Защита от случайного закрытия игры — требует нажать клавишу выхода дважды."
        });
        list.Add(new SettingItem
        {
            CategoryId = "input", CategoryName = "Ввод и Клавиши", CategoryIcon = "🕹️",
            Title = "Количество игроков", CfgKey = "input_max_users", Kind = SettingControlKind.Slider,
            Description = "Максимальное число одновременно подключенных игровых контроллеров.",
            Min = 1, Max = 8, Step = 1, Unit = "игроков"
        });
        list.Add(new SettingItem
        {
            CategoryId = "input", CategoryName = "Ввод и Клавиши", CategoryIcon = "🕹️",
            Title = "Мёртвая зона аналоговых стиков", CfgKey = "input_analog_deadzone", Kind = SettingControlKind.Slider,
            Description = "Порог нечувствительности для устранения дрифта стиков (0.00 .. 0.50).",
            Min = 0.0, Max = 0.5, Step = 0.01
        });
        list.Add(new SettingItem
        {
            CategoryId = "input", CategoryName = "Ввод и Клавиши", CategoryIcon = "🕹️",
            Title = "Меню эмулятора (HotKey)", CfgKey = "input_menu_toggle", Kind = SettingControlKind.TextBox,
            Description = "Горячая клавиша для вызова внутриигрового меню RetroArch."
        });
        list.Add(new SettingItem
        {
            CategoryId = "input", CategoryName = "Ввод и Клавиши", CategoryIcon = "🕹️",
            Title = "Десктопное меню Qt (HotKey)", CfgKey = "input_desktop_menu_toggle", Kind = SettingControlKind.TextBox,
            Description = "Горячая клавиша вызова компаньона рабочего стола RetroArch Qt."
        });
        list.Add(new SettingItem
        {
            CategoryId = "input", CategoryName = "Ввод и Клавиши", CategoryIcon = "🕹️",
            Title = "Сохранить стейт (HotKey)", CfgKey = "input_save_state", Kind = SettingControlKind.TextBox,
            Description = "Клавиша быстрого сохранения состояния эмулятора в текущий слот."
        });
        list.Add(new SettingItem
        {
            CategoryId = "input", CategoryName = "Ввод и Клавиши", CategoryIcon = "🕹️",
            Title = "Загрузить стейт (HotKey)", CfgKey = "input_load_state", Kind = SettingControlKind.TextBox,
            Description = "Клавиша быстрой загрузки состояния эмулятора из активного слота."
        });

        // 5. LATENCY (Задержка)
        list.Add(new SettingItem
        {
            CategoryId = "latency", CategoryName = "Задержка", CategoryIcon = "⚡",
            Title = "Опережающие кадры (Preemptive Frames)", CfgKey = "preemptive_frames_enable", Kind = SettingControlKind.Toggle,
            Description = "Революционная технология подавления задержки ввода до уровня оригинального железа."
        });
        list.Add(new SettingItem
        {
            CategoryId = "latency", CategoryName = "Задержка", CategoryIcon = "⚡",
            Title = "Упреждающий расчёт (Run-Ahead)", CfgKey = "run_ahead_enabled", Kind = SettingControlKind.Toggle,
            Description = "Покадровый откат логики игры для опережения встроенного запаздывания оригинальной приставки."
        });
        list.Add(new SettingItem
        {
            CategoryId = "latency", CategoryName = "Задержка", CategoryIcon = "⚡",
            Title = "Вторичный экземпляр ядра", CfgKey = "run_ahead_secondary_instance", Kind = SettingControlKind.Toggle,
            Description = "Запуск независимого клона ядра для исключения заикания звука при Run-Ahead."
        });
        list.Add(new SettingItem
        {
            CategoryId = "latency", CategoryName = "Задержка", CategoryIcon = "⚡",
            Title = "Авто-калибровка задержки кадра", CfgKey = "video_frame_delay_auto", Kind = SettingControlKind.Toggle,
            Description = "Динамический сдвиг момента рендера кадра как можно ближе к VBlank."
        });
        list.Add(new SettingItem
        {
            CategoryId = "latency", CategoryName = "Задержка", CategoryIcon = "⚡",
            Title = "Кадры Run-Ahead (1..6)", CfgKey = "run_ahead_frames", Kind = SettingControlKind.Slider,
            Description = "Количество кадров упреждения (для NES/SNES/Genesis обычно 1–2 кадра).",
            Min = 1, Max = 6, Step = 1, Unit = "кадра"
        });

        // 6. SAVING (Сохранения)
        list.Add(new SettingItem
        {
            CategoryId = "saving", CategoryName = "Сохранения", CategoryIcon = "💾",
            Title = "Автосохранение стейта при выходе", CfgKey = "savestate_auto_save", Kind = SettingControlKind.Toggle,
            Description = "Автоматическое сохранение игры в специальный стейт при закрытии."
        });
        list.Add(new SettingItem
        {
            CategoryId = "saving", CategoryName = "Сохранения", CategoryIcon = "💾",
            Title = "Автозагрузка стейта при старте", CfgKey = "savestate_auto_load", Kind = SettingControlKind.Toggle,
            Description = "Мгновенное возобновление игры с момента последнего закрытия."
        });
        list.Add(new SettingItem
        {
            CategoryId = "saving", CategoryName = "Сохранения", CategoryIcon = "💾",
            Title = "Авто-инкремент слота сохранения", CfgKey = "savestate_auto_index", Kind = SettingControlKind.Toggle,
            Description = "Автоматический переход к следующему номеру слота при каждом сохранении."
        });
        list.Add(new SettingItem
        {
            CategoryId = "saving", CategoryName = "Сохранения", CategoryIcon = "💾",
            Title = "Сжатие сохранений (RZIP)", CfgKey = "savestate_compression", Kind = SettingControlKind.Toggle,
            Description = "Сжатие файлов стейтов алгоритмом Deflate/RZIP для экономии диска."
        });
        list.Add(new SettingItem
        {
            CategoryId = "saving", CategoryName = "Сохранения", CategoryIcon = "💾",
            Title = "Сортировать сейвы по папкам ядер", CfgKey = "sort_savefiles_enable", Kind = SettingControlKind.Toggle,
            Description = "Раскладывать .srm сохранения по отдельным каталогам соответствующих платформ."
        });

        // 7. THROTTLE (Кадры и Перемотка)
        list.Add(new SettingItem
        {
            CategoryId = "throttle", CategoryName = "Кадры и Перемотка", CategoryIcon = "⏩",
            Title = "Включить перемотку назад (Rewind)", CfgKey = "rewind_enable", Kind = SettingControlKind.Toggle,
            Description = "Возможность отматывать время назад в игре нажатием горячей клавиши."
        });
        list.Add(new SettingItem
        {
            CategoryId = "throttle", CategoryName = "Кадры и Перемотка", CategoryIcon = "⏩",
            Title = "Пауза при неактивном окне", CfgKey = "pause_nonactive", Kind = SettingControlKind.Toggle,
            Description = "Автоматическая пауза эмулятора при переключении на другое приложение."
        });
        list.Add(new SettingItem
        {
            CategoryId = "throttle", CategoryName = "Кадры и Перемотка", CategoryIcon = "⏩",
            Title = "Размер буфера перемотки (МБ)", CfgKey = "rewind_buffer_size", Kind = SettingControlKind.ComboBox,
            Description = "Выделенный объём оперативной памяти для кольцевого буфера состояний.",
            Options = new() { "50", "100", "200", "500" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "throttle", CategoryName = "Кадры и Перемотка", CategoryIcon = "⏩",
            Title = "Скорость перемотки вперёд", CfgKey = "fastforward_ratio", Kind = SettingControlKind.ComboBox,
            Description = "Множитель ускорения при нажатии клавиши Fast-Forward (0 = без ограничений).",
            Options = new() { "0.000000", "1.500000", "2.000000", "3.000000", "5.000000", "10.000000" }
        });

        // 8. OSD (Экранное меню)
        list.Add(new SettingItem
        {
            CategoryId = "osd", CategoryName = "Экранное меню", CategoryIcon = "📺",
            Title = "Экранные текстовые уведомления", CfgKey = "video_font_enable", Kind = SettingControlKind.Toggle,
            Description = "Отображение всплывающих подсказок о слотах, шейдерах и сохранении."
        });
        list.Add(new SettingItem
        {
            CategoryId = "osd", CategoryName = "Экранное меню", CategoryIcon = "📺",
            Title = "Экранный виртуальный оверлей", CfgKey = "input_overlay_enable", Kind = SettingControlKind.Toggle,
            Description = "Полупрозрачные сенсорные кнопки на экране для планшетов и экранов."
        });
        list.Add(new SettingItem
        {
            CategoryId = "osd", CategoryName = "Экранное меню", CategoryIcon = "📺",
            Title = "Автомасштабирование виджетов", CfgKey = "menu_widget_scale_auto", Kind = SettingControlKind.Toggle,
            Description = "Автоматическая подгонка размера всплывающих окон под разрешение 1080p/4K."
        });

        // 9. UI (Интерфейс)
        list.Add(new SettingItem
        {
            CategoryId = "ui", CategoryName = "Интерфейс", CategoryIcon = "🎨",
            Title = "Анимации в меню", CfgKey = "menu_horizontal_animation", Kind = SettingControlKind.Toggle,
            Description = "Плавные горизонтальные переходы между категориями меню."
        });
        list.Add(new SettingItem
        {
            CategoryId = "ui", CategoryName = "Интерфейс", CategoryIcon = "🎨",
            Title = "Ограничение частоты кадров меню", CfgKey = "menu_throttle_framerate", Kind = SettingControlKind.Toggle,
            Description = "Снижение нагрузки на GPU при нахождении в меню эмулятора."
        });
        list.Add(new SettingItem
        {
            CategoryId = "ui", CategoryName = "Интерфейс", CategoryIcon = "🎨",
            Title = "Цветовая тема Ozone", CfgKey = "ozone_color_theme", Kind = SettingControlKind.ComboBox,
            Description = "Цветовая палитра оформления интерфейса Ozone.",
            Options = new() { "0", "1", "2", "3", "4" }
        });

        // 10. ACHIEVEMENTS (Достижения)
        list.Add(new SettingItem
        {
            CategoryId = "cheevos", CategoryName = "Достижения", CategoryIcon = "🏆",
            Title = "Включить RetroAchievements", CfgKey = "cheevos_enable", Kind = SettingControlKind.Toggle,
            Description = "Отслеживание внутриигровых достижений и трофеев в ретро-играх."
        });
        list.Add(new SettingItem
        {
            CategoryId = "cheevos", CategoryName = "Достижения", CategoryIcon = "🏆",
            Title = "Хардкорный режим (Hardcore Mode)", CfgKey = "cheevos_hardcore_mode_enable", Kind = SettingControlKind.Toggle,
            Description = "Блокировка стейтов и перемотки для официального зачёта в глобальном рейтинге."
        });
        list.Add(new SettingItem
        {
            CategoryId = "cheevos", CategoryName = "Достижения", CategoryIcon = "🏆",
            Title = "Таблицы рекордов (Leaderboards)", CfgKey = "cheevos_leaderboards_enable", Kind = SettingControlKind.Toggle,
            Description = "Отправка времени прохождения и очков в глобальные списки лидеров."
        });
        list.Add(new SettingItem
        {
            CategoryId = "cheevos", CategoryName = "Достижения", CategoryIcon = "🏆",
            Title = "Автоскриншот при получении ачивки", CfgKey = "cheevos_auto_screenshot", Kind = SettingControlKind.Toggle,
            Description = "Автоматический снимок экрана в момент разблокировки трофея."
        });
        list.Add(new SettingItem
        {
            CategoryId = "cheevos", CategoryName = "Достижения", CategoryIcon = "🏆",
            Title = "Логин RetroAchievements", CfgKey = "cheevos_username", Kind = SettingControlKind.TextBox,
            Description = "Ваша учётная запись на сайте retroachievements.org."
        });

        // 11. NETPLAY (Сетевая игра)
        list.Add(new SettingItem
        {
            CategoryId = "netplay", CategoryName = "Сетевая игра", CategoryIcon = "🌐",
            Title = "Публиковать комнату в публичном лобби", CfgKey = "netplay_public_announce", Kind = SettingControlKind.Toggle,
            Description = "Показывать вашу игру в глобальном списке лобби RetroArch."
        });
        list.Add(new SettingItem
        {
            CategoryId = "netplay", CategoryName = "Сетевая игра", CategoryIcon = "🌐",
            Title = "Использовать MITM транзитный сервер", CfgKey = "netplay_use_mitm_server", Kind = SettingControlKind.Toggle,
            Description = "Обход NAT и закрытых портов через официальные ретрансляторы Libretro."
        });
        list.Add(new SettingItem
        {
            CategoryId = "netplay", CategoryName = "Сетевая игра", CategoryIcon = "🌐",
            Title = "Транзитный сервер MITM", CfgKey = "netplay_mitm_server", Kind = SettingControlKind.ComboBox,
            Description = "Ближайший дата-центр ретрансляции трафика сетевой игры.",
            Options = new() { "nyc", "madrid", "saopaulo", "tokyo" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "netplay", CategoryName = "Сетевая игра", CategoryIcon = "🌐",
            Title = "Порт сетевой игры", CfgKey = "netplay_ip_port", Kind = SettingControlKind.TextBox,
            Description = "Сетевой UDP/TCP порт для прямого подключения (по умолчанию 55435)."
        });

        // 12. RECORDING (Запись)
        list.Add(new SettingItem
        {
            CategoryId = "recording", CategoryName = "Запись", CategoryIcon = "🎥",
            Title = "Аппаратное ускорение записи через GPU", CfgKey = "video_gpu_record", Kind = SettingControlKind.Toggle,
            Description = "Прямой захват кадрового буфера видеокарты без нагрузки на процессор."
        });
        list.Add(new SettingItem
        {
            CategoryId = "recording", CategoryName = "Запись", CategoryIcon = "🎥",
            Title = "Режим трансляции (Streaming)", CfgKey = "streaming_mode", Kind = SettingControlKind.ComboBox,
            Description = "Направление потокового вещания (0: выкл, 1: Twitch, 2: YouTube, 3: Custom).",
            Options = new() { "0", "1", "2", "3" }
        });

        // 13. AI SERVICE (Сервис ИИ)
        list.Add(new SettingItem
        {
            CategoryId = "ai", CategoryName = "Сервис ИИ", CategoryIcon = "🤖",
            Title = "Включить сервис перевода AI Service", CfgKey = "ai_service_enable", Kind = SettingControlKind.Toggle,
            Description = "Распознавание и перевод экранного текста на лету через нейросетевой сервер."
        });
        list.Add(new SettingItem
        {
            CategoryId = "ai", CategoryName = "Сервис ИИ", CategoryIcon = "🤖",
            Title = "Пауза эмулятора при переводе", CfgKey = "ai_service_pause", Kind = SettingControlKind.Toggle,
            Description = "Автоматическая заморозка кадра, пока сервер распознаёт и возвращает текст."
        });
        list.Add(new SettingItem
        {
            CategoryId = "ai", CategoryName = "Сервис ИИ", CategoryIcon = "🤖",
            Title = "URL сервера машинного перевода", CfgKey = "ai_service_url", Kind = SettingControlKind.TextBox,
            Description = "Адрес локального или облачного API оптического распознавания текста."
        });

        // 14. CONFIG & LOGGING (Конфигурация и Логи)
        list.Add(new SettingItem
        {
            CategoryId = "config", CategoryName = "Конфиг и Логи", CategoryIcon = "📋",
            Title = "Сохранять конфигурацию при выходе", CfgKey = "config_save_on_exit", Kind = SettingControlKind.Toggle,
            Description = "Автоматическая запись изменённых параметров в retroarch.cfg при закрытии."
        });
        list.Add(new SettingItem
        {
            CategoryId = "config", CategoryName = "Конфиг и Логи", CategoryIcon = "📋",
            Title = "Автозагрузка оверрайдов для ядер", CfgKey = "auto_overrides_enable", Kind = SettingControlKind.Toggle,
            Description = "Применение специфичных настроек видео/звука для конкретных систем."
        });
        list.Add(new SettingItem
        {
            CategoryId = "config", CategoryName = "Конфиг и Логи", CategoryIcon = "📋",
            Title = "Автозагрузка индивидуальных ремапов", CfgKey = "auto_remaps_enable", Kind = SettingControlKind.Toggle,
            Description = "Загрузка переназначенных клавиш для отдельных игр и платформ."
        });
        list.Add(new SettingItem
        {
            CategoryId = "config", CategoryName = "Конфиг и Логи", CategoryIcon = "📋",
            Title = "Записывать подробный лог в файл", CfgKey = "log_to_file", Kind = SettingControlKind.Toggle,
            Description = "Сохранение диагностической информации запуска в retroarch.log."
        });

        // 15. DIRECTORY (Каталоги)
        list.Add(new SettingItem
        {
            CategoryId = "directory", CategoryName = "Каталоги", CategoryIcon = "📁",
            Title = "Системные файлы и BIOS", CfgKey = "system_directory", Kind = SettingControlKind.DirectoryPath,
            Description = "Каталог прошивок BIOS для PS1, Saturn, Dreamcast, Neo Geo."
        });
        list.Add(new SettingItem
        {
            CategoryId = "directory", CategoryName = "Каталоги", CategoryIcon = "📁",
            Title = "Папка сохранений игр (Savefiles)", CfgKey = "savefile_directory", Kind = SettingControlKind.DirectoryPath,
            Description = "Каталог для картриджных батареечных сохранений .srm и Memory Card."
        });
        list.Add(new SettingItem
        {
            CategoryId = "directory", CategoryName = "Каталоги", CategoryIcon = "📁",
            Title = "Папка стейтов (Savestates)", CfgKey = "savestate_directory", Kind = SettingControlKind.DirectoryPath,
            Description = "Каталог мгновенных сохранений игрового процесса."
        });
        list.Add(new SettingItem
        {
            CategoryId = "directory", CategoryName = "Каталоги", CategoryIcon = "📁",
            Title = "Папка скриншотов", CfgKey = "screenshot_directory", Kind = SettingControlKind.DirectoryPath,
            Description = "Каталог для сохранения снимков экрана, сделанных по клавише F8."
        });
        list.Add(new SettingItem
        {
            CategoryId = "directory", CategoryName = "Каталоги", CategoryIcon = "📁",
            Title = "Папка шейдеров (Shaders)", CfgKey = "video_shader_dir", Kind = SettingControlKind.DirectoryPath,
            Description = "Каталог графических фильтров .slangp и .glslp."
        });

        // 16. USER (Пользователь)
        list.Add(new SettingItem
        {
            CategoryId = "user", CategoryName = "Пользователь", CategoryIcon = "👤",
            Title = "Никнейм игрока", CfgKey = "netplay_nickname", Kind = SettingControlKind.TextBox,
            Description = "Отображаемое имя в сетевых баталиях и таблицах лидеров."
        });
        list.Add(new SettingItem
        {
            CategoryId = "user", CategoryName = "Пользователь", CategoryIcon = "👤",
            Title = "Режим приватности", CfgKey = "user_privacy_mode", Kind = SettingControlKind.Toggle,
            Description = "Скрытие имени пользователя и сведений о системе из сетевых запросов."
        });
        list.Add(new SettingItem
        {
            CategoryId = "user", CategoryName = "Пользователь", CategoryIcon = "👤",
            Title = "Язык интерфейса RetroArch", CfgKey = "user_language", Kind = SettingControlKind.ComboBox,
            Description = "Язык главного меню и системных сообщений RetroArch (9 = Русский, 0 = Английский).",
            Options = new() { "9", "0", "1", "2", "3", "4", "5" }
        });

        return list;
    }
}
