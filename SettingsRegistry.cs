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
            CategoryId = "drivers", CategoryName = "Драйверы", Title = "Видеодрайвер", CfgKey = "video_driver", Kind = SettingControlKind.ComboBox,
            Description = "Графический API вывода видео (Vulkan рекомендуется для современных GPU с минимальной задержкой).",
            Options = new() { "vulkan", "glcore", "d3d11", "d3d12", "gl", "gdi" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "drivers", CategoryName = "Драйверы", Title = "Драйвер меню", CfgKey = "menu_driver", Kind = SettingControlKind.ComboBox,
            Description = "Графический интерфейс меню эмулятора (Ozone, XMB PS3-стиль, RGUI пиксельный).",
            Options = new() { "ozone", "xmb", "glui", "rgui" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "drivers", CategoryName = "Драйверы", Title = "Аудиодрайвер", CfgKey = "audio_driver", Kind = SettingControlKind.ComboBox,
            Description = "Звуковая подсистема вывода (WASAPI Exclusive гарантирует задержку менее 10 мс).",
            Options = new() { "wasapi", "xaudio", "dsound", "sdl2" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "drivers", CategoryName = "Драйверы", Title = "Аудио ресемплер", CfgKey = "audio_resampler", Kind = SettingControlKind.ComboBox,
            Description = "Алгоритм передискретизации частоты звука (Sinc — наивысшее качество).",
            Options = new() { "sinc", "cc", "nearest" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "drivers", CategoryName = "Драйверы", Title = "Драйвер ввода клавиатуры/мыши", CfgKey = "input_driver", Kind = SettingControlKind.ComboBox,
            Description = "Низкоуровневый перехват ввода (Raw Input обходит очереди сообщений Windows).",
            Options = new() { "raw", "dinput", "xinput", "sdl2" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "drivers", CategoryName = "Драйверы", Title = "Драйвер геймпада", CfgKey = "input_joypad_driver", Kind = SettingControlKind.ComboBox,
            Description = "Протокол опроса контроллеров (XInput для Xbox/совместимых, DInput для ретро).",
            Options = new() { "xinput", "dinput", "sdl2", "hid" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "drivers", CategoryName = "Драйверы", Title = "Драйвер видеозаписи", CfgKey = "record_driver", Kind = SettingControlKind.ComboBox,
            Description = "Кодировщик для захвата видео и потоковой трансляции.",
            Options = new() { "null", "ffmpeg" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "drivers", CategoryName = "Драйверы", Title = "MIDI драйвер", CfgKey = "midi_driver", Kind = SettingControlKind.ComboBox,
            Description = "Подсистема синтеза ретро-музыки General MIDI / MT-32.",
            Options = new() { "winmm", "null" }
        });

        // 2. VIDEO (Видео)
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Полноэкранный режим", CfgKey = "video_fullscreen", Kind = SettingControlKind.Toggle,
            Description = "Запуск эмулятора на весь экран монитора."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Полноэкранное окно без рамок", CfgKey = "video_windowed_fullscreen", Kind = SettingControlKind.Toggle,
            Description = "Borderless Windowed режим для мгновенного переключения по Alt+Tab."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Целочисленное масштабирование 1:1", CfgKey = "video_scale_integer", Kind = SettingControlKind.Toggle,
            Description = "Отображение пикселей строго целыми кратными коэффициентами без размытия."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Билинейная фильтрация", CfgKey = "video_smooth", Kind = SettingControlKind.Toggle,
            Description = "Аппаратное сглаживание пикселей (отключите для аутентичного ретро-пиксельарта)."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Включить шейдеры", CfgKey = "video_shader_enable", Kind = SettingControlKind.Toggle,
            Description = "Активация CRT/LCD фильтров, сканлайнов и постобработки."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Вертикальная синхронизация VSync", CfgKey = "video_vsync", Kind = SettingControlKind.Toggle,
            Description = "Устранение разрывов кадров синхронизацией с частотой экрана."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Адаптивный VSync", CfgKey = "video_adaptive_vsync", Kind = SettingControlKind.Toggle,
            Description = "Отключение VSync при просадках FPS ниже частоты развёртки монитора."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Поддержка VRR", CfgKey = "vrr_runloop_enable", Kind = SettingControlKind.Toggle,
            Description = "Синхронизация цикла эмуляции с переменной частотой обновления дисплея."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Жёсткая синхронизация GPU", CfgKey = "video_hard_sync", Kind = SettingControlKind.Toggle,
            Description = "Синхронизация CPU и GPU строго на 1 кадр для подавления задержки ввода."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Многопоточное видео", CfgKey = "video_threaded", Kind = SettingControlKind.Toggle,
            Description = "Вынос графического рендера в отдельный системный поток (увеличивает задержку на 1 кадр)."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Отображать счётчик FPS", CfgKey = "fps_show", Kind = SettingControlKind.Toggle,
            Description = "Показывать текущую частоту кадров в углу экрана."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Отображать потребление памяти", CfgKey = "memory_show", Kind = SettingControlKind.Toggle,
            Description = "Мониторинг выделенной оперативной памяти процессом эмулятора."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Соотношение сторон", CfgKey = "aspect_ratio_index", Kind = SettingControlKind.ComboBox,
            Description = "Пропорции экрана рендеринга (4:3 ретро, 16:9 широкий, 1:1 пиксель).",
            Options = new() { "0", "1", "2", "3", "4" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Буфер цепочки подкачки", CfgKey = "video_max_swapchain_images", Kind = SettingControlKind.Slider,
            Description = "Количество экранных буферов (2 = двойная буферизация, 3 = тройная).",
            Min = 1, Max = 4, Step = 1, Unit = "буфера"
        });

                list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Ширина полноэкранного разрешения", CfgKey = "video_fullscreen_x", Kind = SettingControlKind.ComboBox,
            Description = "Горизонтальное разрешение экрана при запуске в полноэкранном режиме (0 = родное разрешение монитора).",
            Options = new() { "0", "1280", "1920", "2560", "3840" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Высота полноэкранного разрешения", CfgKey = "video_fullscreen_y", Kind = SettingControlKind.ComboBox,
            Description = "Вертикальное разрешение экрана при запуске в полноэкранном режиме (0 = родное разрешение монитора).",
            Options = new() { "0", "720", "1080", "1440", "2160" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Фиксированный размер окна", CfgKey = "video_window_custom_size_enable", Kind = SettingControlKind.Toggle,
            Description = "Принудительно использовать заданные ширину и высоту окна вместо автоматического масштаба."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Ширина окна (видео)", CfgKey = "video_window_x", Kind = SettingControlKind.ComboBox,
            Description = "Точная ширина окна эмулятора в пикселях.",
            Options = new() { "960", "1280", "1440", "1600", "1920", "2560" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Высота окна (видео)", CfgKey = "video_window_y", Kind = SettingControlKind.ComboBox,
            Description = "Точная высота окна эмулятора в пикселях.",
            Options = new() { "720", "900", "960", "1080", "1440" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Кратность масштаба окна (1x–6x)", CfgKey = "video_scale", Kind = SettingControlKind.ComboBox,
            Description = "Коэффициент увеличения окна относительно базового разрешения ретро-консоли.",
            Options = new() { "1", "2", "3", "4", "5", "6" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Выбор активного монитора", CfgKey = "video_monitor_index", Kind = SettingControlKind.ComboBox,
            Description = "Номер дисплея в системе Windows для вывода видео (0 = автоматический выбор основного).",
            Options = new() { "0", "1", "2", "3" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Частота обновления экрана (Гц)", CfgKey = "video_refresh_rate", Kind = SettingControlKind.ComboBox,
            Description = "Целевая кадровая частота развёртки дисплея для идеальной плавной синхронизации звука и видео.",
            Options = new() { "59.940000", "60.000000", "75.000000", "120.000000", "144.000000", "165.000000", "240.000000" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Поворот экрана (TATE / Аркады)", CfgKey = "video_rotation", Kind = SettingControlKind.ComboBox,
            Description = "Ориентация вывода изображения для вертикальных аркадных шутеров Shoot 'em up.",
            Options = new() { "0", "1", "2", "3" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Целочисленный масштаб с заполнением", CfgKey = "video_scale_integer_overscale", Kind = SettingControlKind.Toggle,
            Description = "Округление целочисленного масштаба в большую сторону для полного заполнения экрана."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Обрезка краёв кадра", CfgKey = "video_crop_overscan", Kind = SettingControlKind.Toggle,
            Description = "Скрытие служебных строк телевизионного сигнала и неиспользуемых бордюров ЭЛТ."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Запоминать размер и позицию окна", CfgKey = "video_window_save_positions", Kind = SettingControlKind.Toggle,
            Description = "Автоматическое сохранение координат и габаритов окна при закрытии."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Показывать системную рамку окна", CfgKey = "video_window_show_decorations", Kind = SettingControlKind.Toggle,
            Description = "Отображение стандартного заголовка и кнопок управления окном в оконном режиме."
        });
        list.Add(new SettingItem
        {
            CategoryId = "video", CategoryName = "Видео", Title = "Расширенный динамический диапазон", CfgKey = "video_hdr_enable", Kind = SettingControlKind.Toggle,
            Description = "Активация 10-битного HDR вывода для повышенной контрастности и кинематографичной яркости."
        });

        // 3. AUDIO (Аудио)
        list.Add(new SettingItem
        {
            CategoryId = "audio", CategoryName = "Аудио", Title = "Включить звук", CfgKey = "audio_enable", Kind = SettingControlKind.Toggle,
            Description = "Глобальное включение эмуляции аудиочипов и вывода звука."
        });
        list.Add(new SettingItem
        {
            CategoryId = "audio", CategoryName = "Аудио", Title = "Синхронизация звука", CfgKey = "audio_sync", Kind = SettingControlKind.Toggle,
            Description = "Синхронизация скорости эмуляции по аудиотаймингам для исключения щелчков."
        });
        list.Add(new SettingItem
        {
            CategoryId = "audio", CategoryName = "Аудио", Title = "WASAPI Exclusive Mode", CfgKey = "audio_wasapi_exclusive_mode", Kind = SettingControlKind.Toggle,
            Description = "Монопольный низкоуровневый доступ к аудиокарте с ультранизкой задержкой."
        });
        list.Add(new SettingItem
        {
            CategoryId = "audio", CategoryName = "Аудио", Title = "Звуковые эффекты меню", CfgKey = "audio_enable_menu", Kind = SettingControlKind.Toggle,
            Description = "Аудиоотклик при навигации и выборе пунктов в интерфейсе RetroArch."
        });
        list.Add(new SettingItem
        {
            CategoryId = "audio", CategoryName = "Аудио", Title = "Задержка аудио", CfgKey = "audio_latency", Kind = SettingControlKind.Slider,
            Description = "Размер аудио-буфера в миллисекундах (32–64 мс оптимально для WASAPI).",
            Min = 8, Max = 128, Step = 8, Unit = "мс"
        });
        list.Add(new SettingItem
        {
            CategoryId = "audio", CategoryName = "Аудио", Title = "Частота дискретизации", CfgKey = "audio_out_rate", Kind = SettingControlKind.ComboBox,
            Description = "Выходная частота цифро-аналогового преобразования.",
            Options = new() { "44100", "48000", "96000", "192000" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "audio", CategoryName = "Аудио", Title = "Громкость звука", CfgKey = "audio_volume", Kind = SettingControlKind.Slider,
            Description = "Регулировка усиления аудиосигнала (в децибелах от -20 до +12 dB).",
            Min = -20, Max = 12, Step = 1, Unit = "dB"
        });

        // 4. INPUT & HOTKEYS (Ввод и Клавиши)
        list.Add(new SettingItem
        {
            CategoryId = "input", CategoryName = "Ввод и Клавиши", Title = "Автоопределение геймпадов", CfgKey = "input_autodetect_enable", Kind = SettingControlKind.Toggle,
            Description = "Автоматическая загрузка профилей autoconfig при подключении геймпада."
        });
        list.Add(new SettingItem
        {
            CategoryId = "input", CategoryName = "Ввод и Клавиши", Title = "Вибрация геймпада", CfgKey = "enable_device_vibration", Kind = SettingControlKind.Toggle,
            Description = "Передача тактильной отдачи и эффектов вибрации в моторчики контроллера."
        });
        list.Add(new SettingItem
        {
            CategoryId = "input", CategoryName = "Ввод и Клавиши", Title = "Двойное нажатие для выхода", CfgKey = "quit_press_twice", Kind = SettingControlKind.Toggle,
            Description = "Защита от случайного закрытия игры — требует нажать клавишу выхода дважды."
        });
        list.Add(new SettingItem
        {
            CategoryId = "input", CategoryName = "Ввод и Клавиши", Title = "Количество игроков", CfgKey = "input_max_users", Kind = SettingControlKind.Slider,
            Description = "Максимальное число одновременно подключенных игровых контроллеров.",
            Min = 1, Max = 8, Step = 1, Unit = "игроков"
        });
        list.Add(new SettingItem
        {
            CategoryId = "input", CategoryName = "Ввод и Клавиши", Title = "Мёртвая зона аналоговых стиков", CfgKey = "input_analog_deadzone", Kind = SettingControlKind.Slider,
            Description = "Порог нечувствительности для устранения дрифта стиков (0.00 .. 0.50).",
            Min = 0.0, Max = 0.5, Step = 0.01
        });
        list.Add(new SettingItem
        {
            CategoryId = "input", CategoryName = "Ввод и Клавиши", Title = "Меню эмулятора", CfgKey = "input_menu_toggle", Kind = SettingControlKind.TextBox,
            Description = "Горячая клавиша для вызова внутриигрового меню RetroArch."
        });
        list.Add(new SettingItem
        {
            CategoryId = "input", CategoryName = "Ввод и Клавиши", Title = "Десктопное меню Qt", CfgKey = "input_desktop_menu_toggle", Kind = SettingControlKind.TextBox,
            Description = "Горячая клавиша вызова компаньона рабочего стола RetroArch Qt."
        });
        list.Add(new SettingItem
        {
            CategoryId = "input", CategoryName = "Ввод и Клавиши", Title = "Сохранить стейт", CfgKey = "input_save_state", Kind = SettingControlKind.TextBox,
            Description = "Клавиша быстрого сохранения состояния эмулятора в текущий слот."
        });
        list.Add(new SettingItem
        {
            CategoryId = "input", CategoryName = "Ввод и Клавиши", Title = "Загрузить стейт", CfgKey = "input_load_state", Kind = SettingControlKind.TextBox,
            Description = "Клавиша быстрой загрузки состояния эмулятора из активного слота."
        });

        // 5. LATENCY (Задержка)
        list.Add(new SettingItem
        {
            CategoryId = "latency", CategoryName = "Задержка", Title = "Опережающие кадры", CfgKey = "preemptive_frames_enable", Kind = SettingControlKind.Toggle,
            Description = "Революционная технология подавления задержки ввода до уровня оригинального железа."
        });
        list.Add(new SettingItem
        {
            CategoryId = "latency", CategoryName = "Задержка", Title = "Упреждающий расчёт", CfgKey = "run_ahead_enabled", Kind = SettingControlKind.Toggle,
            Description = "Покадровый откат логики игры для опережения встроенного запаздывания оригинальной приставки."
        });
        list.Add(new SettingItem
        {
            CategoryId = "latency", CategoryName = "Задержка", Title = "Вторичный экземпляр ядра", CfgKey = "run_ahead_secondary_instance", Kind = SettingControlKind.Toggle,
            Description = "Запуск независимого клона ядра для исключения заикания звука при Run-Ahead."
        });
        list.Add(new SettingItem
        {
            CategoryId = "latency", CategoryName = "Задержка", Title = "Авто-калибровка задержки кадра", CfgKey = "video_frame_delay_auto", Kind = SettingControlKind.Toggle,
            Description = "Динамический сдвиг момента рендера кадра как можно ближе к VBlank."
        });
        list.Add(new SettingItem
        {
            CategoryId = "latency", CategoryName = "Задержка", Title = "Кадры Run-Ahead (1..6)", CfgKey = "run_ahead_frames", Kind = SettingControlKind.Slider,
            Description = "Количество кадров упреждения (для NES/SNES/Genesis обычно 1–2 кадра).",
            Min = 1, Max = 6, Step = 1, Unit = "кадра"
        });

        // 6. SAVING (Сохранения)
        list.Add(new SettingItem
        {
            CategoryId = "saving", CategoryName = "Сохранения", Title = "Автосохранение стейта при выходе", CfgKey = "savestate_auto_save", Kind = SettingControlKind.Toggle,
            Description = "Автоматическое сохранение игры в специальный стейт при закрытии."
        });
        list.Add(new SettingItem
        {
            CategoryId = "saving", CategoryName = "Сохранения", Title = "Автозагрузка стейта при старте", CfgKey = "savestate_auto_load", Kind = SettingControlKind.Toggle,
            Description = "Мгновенное возобновление игры с момента последнего закрытия."
        });
        list.Add(new SettingItem
        {
            CategoryId = "saving", CategoryName = "Сохранения", Title = "Авто-инкремент слота сохранения", CfgKey = "savestate_auto_index", Kind = SettingControlKind.Toggle,
            Description = "Автоматический переход к следующему номеру слота при каждом сохранении."
        });
        list.Add(new SettingItem
        {
            CategoryId = "saving", CategoryName = "Сохранения", Title = "Сжатие сохранений", CfgKey = "savestate_compression", Kind = SettingControlKind.Toggle,
            Description = "Сжатие файлов стейтов алгоритмом Deflate/RZIP для экономии диска."
        });
        list.Add(new SettingItem
        {
            CategoryId = "saving", CategoryName = "Сохранения", Title = "Сортировать сейвы по папкам ядер", CfgKey = "sort_savefiles_enable", Kind = SettingControlKind.Toggle,
            Description = "Раскладывать .srm сохранения по отдельным каталогам соответствующих платформ."
        });

        // 7. THROTTLE (Кадры и Перемотка)
        list.Add(new SettingItem
        {
            CategoryId = "throttle", CategoryName = "Кадры и Перемотка", Title = "Включить перемотку назад", CfgKey = "rewind_enable", Kind = SettingControlKind.Toggle,
            Description = "Возможность отматывать время назад в игре нажатием горячей клавиши."
        });
        list.Add(new SettingItem
        {
            CategoryId = "throttle", CategoryName = "Кадры и Перемотка", Title = "Пауза при неактивном окне", CfgKey = "pause_nonactive", Kind = SettingControlKind.Toggle,
            Description = "Автоматическая пауза эмулятора при переключении на другое приложение."
        });
        list.Add(new SettingItem
        {
            CategoryId = "throttle", CategoryName = "Кадры и Перемотка", Title = "Размер буфера перемотки (МБ)", CfgKey = "rewind_buffer_size", Kind = SettingControlKind.ComboBox,
            Description = "Выделенный объём оперативной памяти для кольцевого буфера состояний.",
            Options = new() { "50", "100", "200", "500" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "throttle", CategoryName = "Кадры и Перемотка", Title = "Скорость перемотки вперёд", CfgKey = "fastforward_ratio", Kind = SettingControlKind.ComboBox,
            Description = "Множитель ускорения при нажатии клавиши Fast-Forward (0 = без ограничений).",
            Options = new() { "0.000000", "1.500000", "2.000000", "3.000000", "5.000000", "10.000000" }
        });

        // 8. OSD (Экранное меню)
        list.Add(new SettingItem
        {
            CategoryId = "osd", CategoryName = "Экранное меню", Title = "Экранные текстовые уведомления", CfgKey = "video_font_enable", Kind = SettingControlKind.Toggle,
            Description = "Отображение всплывающих подсказок о слотах, шейдерах и сохранении."
        });
        list.Add(new SettingItem
        {
            CategoryId = "osd", CategoryName = "Экранное меню", Title = "Экранный виртуальный оверлей", CfgKey = "input_overlay_enable", Kind = SettingControlKind.Toggle,
            Description = "Полупрозрачные сенсорные кнопки на экране для планшетов и экранов."
        });
        list.Add(new SettingItem
        {
            CategoryId = "osd", CategoryName = "Экранное меню", Title = "Автомасштабирование виджетов", CfgKey = "menu_widget_scale_auto", Kind = SettingControlKind.Toggle,
            Description = "Автоматическая подгонка размера всплывающих окон под разрешение 1080p/4K."
        });

                list.Add(new SettingItem
        {
            CategoryId = "osd", CategoryName = "Экранное меню", Title = "Скрывать рамку Bezel внутри меню", CfgKey = "input_overlay_hide_in_menu", Kind = SettingControlKind.Toggle,
            Description = "Автоматически убирать рамку и оверлей при вызове игрового меню RetroArch."
        });
        list.Add(new SettingItem
        {
            CategoryId = "osd", CategoryName = "Экранное меню", Title = "Авто-масштабирование рамки Bezel под экран", CfgKey = "input_overlay_auto_scale", Kind = SettingControlKind.Toggle,
            Description = "Масштабировать оверлей и рамку под текущее разрешение монитора без искажения пропорций."
        });
        list.Add(new SettingItem
        {
            CategoryId = "osd", CategoryName = "Экранное меню", Title = "Масштаб рамки Bezel", CfgKey = "input_overlay_scale_landscape", Kind = SettingControlKind.ComboBox,
            Description = "Коэффициент масштаба горизонтального оверлея рамки Bezel.",
            Options = new() { "0.800000", "0.900000", "1.000000", "1.050000", "1.100000", "1.250000" }
        });

        // 9. UI (Интерфейс)
        list.Add(new SettingItem
        {
            CategoryId = "ui", CategoryName = "Интерфейс", Title = "Анимации в меню", CfgKey = "menu_horizontal_animation", Kind = SettingControlKind.Toggle,
            Description = "Плавные горизонтальные переходы между категориями меню."
        });
        list.Add(new SettingItem
        {
            CategoryId = "ui", CategoryName = "Интерфейс", Title = "Ограничение частоты кадров меню", CfgKey = "menu_throttle_framerate", Kind = SettingControlKind.Toggle,
            Description = "Снижение нагрузки на GPU при нахождении в меню эмулятора."
        });
        list.Add(new SettingItem
        {
            CategoryId = "ui", CategoryName = "Интерфейс", Title = "Цветовая тема Ozone", CfgKey = "ozone_color_theme", Kind = SettingControlKind.ComboBox,
            Description = "Цветовая палитра оформления интерфейса Ozone.",
            Options = new() { "0", "1", "2", "3", "4" }
        });

        // 10. ACHIEVEMENTS (Достижения)
        list.Add(new SettingItem
        {
            CategoryId = "cheevos", CategoryName = "Достижения", Title = "Включить RetroAchievements", CfgKey = "cheevos_enable", Kind = SettingControlKind.Toggle,
            Description = "Отслеживание внутриигровых достижений и трофеев в ретро-играх."
        });
        list.Add(new SettingItem
        {
            CategoryId = "cheevos", CategoryName = "Достижения", Title = "Хардкорный режим", CfgKey = "cheevos_hardcore_mode_enable", Kind = SettingControlKind.Toggle,
            Description = "Блокировка стейтов и перемотки для официального зачёта в глобальном рейтинге."
        });
        list.Add(new SettingItem
        {
            CategoryId = "cheevos", CategoryName = "Достижения", Title = "Таблицы рекордов", CfgKey = "cheevos_leaderboards_enable", Kind = SettingControlKind.Toggle,
            Description = "Отправка времени прохождения и очков в глобальные списки лидеров."
        });
        list.Add(new SettingItem
        {
            CategoryId = "cheevos", CategoryName = "Достижения", Title = "Автоскриншот при получении ачивки", CfgKey = "cheevos_auto_screenshot", Kind = SettingControlKind.Toggle,
            Description = "Автоматический снимок экрана в момент разблокировки трофея."
        });
        list.Add(new SettingItem
        {
            CategoryId = "cheevos", CategoryName = "Достижения", Title = "Логин RetroAchievements", CfgKey = "cheevos_username", Kind = SettingControlKind.TextBox,
            Description = "Ваша учётная запись на сайте retroachievements.org."
        });

        // 11. NETPLAY (Сетевая игра)
        list.Add(new SettingItem
        {
            CategoryId = "netplay", CategoryName = "Сетевая игра", Title = "Публиковать комнату в публичном лобби", CfgKey = "netplay_public_announce", Kind = SettingControlKind.Toggle,
            Description = "Показывать вашу игру в глобальном списке лобби RetroArch."
        });
        list.Add(new SettingItem
        {
            CategoryId = "netplay", CategoryName = "Сетевая игра", Title = "Использовать MITM транзитный сервер", CfgKey = "netplay_use_mitm_server", Kind = SettingControlKind.Toggle,
            Description = "Обход NAT и закрытых портов через официальные ретрансляторы Libretro."
        });
        list.Add(new SettingItem
        {
            CategoryId = "netplay", CategoryName = "Сетевая игра", Title = "Транзитный сервер MITM", CfgKey = "netplay_mitm_server", Kind = SettingControlKind.ComboBox,
            Description = "Ближайший дата-центр ретрансляции трафика сетевой игры.",
            Options = new() { "nyc", "madrid", "saopaulo", "tokyo" }
        });
        list.Add(new SettingItem
        {
            CategoryId = "netplay", CategoryName = "Сетевая игра", Title = "Порт сетевой игры", CfgKey = "netplay_ip_port", Kind = SettingControlKind.TextBox,
            Description = "Сетевой UDP/TCP порт для прямого подключения (по умолчанию 55435)."
        });

        // 12. RECORDING (Запись)
        list.Add(new SettingItem
        {
            CategoryId = "recording", CategoryName = "Запись", Title = "Аппаратное ускорение записи через GPU", CfgKey = "video_gpu_record", Kind = SettingControlKind.Toggle,
            Description = "Прямой захват кадрового буфера видеокарты без нагрузки на процессор."
        });
        list.Add(new SettingItem
        {
            CategoryId = "recording", CategoryName = "Запись", Title = "Режим трансляции", CfgKey = "streaming_mode", Kind = SettingControlKind.ComboBox,
            Description = "Направление потокового вещания (0: выкл, 1: Twitch, 2: YouTube, 3: Custom).",
            Options = new() { "0", "1", "2", "3" }
        });

        // 13. AI SERVICE (Сервис ИИ)
        list.Add(new SettingItem
        {
            CategoryId = "ai", CategoryName = "Сервис ИИ", Title = "Включить сервис перевода AI Service", CfgKey = "ai_service_enable", Kind = SettingControlKind.Toggle,
            Description = "Распознавание и перевод экранного текста на лету через нейросетевой сервер."
        });
        list.Add(new SettingItem
        {
            CategoryId = "ai", CategoryName = "Сервис ИИ", Title = "Пауза эмулятора при переводе", CfgKey = "ai_service_pause", Kind = SettingControlKind.Toggle,
            Description = "Автоматическая заморозка кадра, пока сервер распознаёт и возвращает текст."
        });
        list.Add(new SettingItem
        {
            CategoryId = "ai", CategoryName = "Сервис ИИ", Title = "URL сервера машинного перевода", CfgKey = "ai_service_url", Kind = SettingControlKind.TextBox,
            Description = "Адрес локального или облачного API оптического распознавания текста."
        });

        // 14. CONFIG & LOGGING (Конфигурация и Логи)
        list.Add(new SettingItem
        {
            CategoryId = "config", CategoryName = "Конфиг и Логи", Title = "Сохранять конфигурацию при выходе", CfgKey = "config_save_on_exit", Kind = SettingControlKind.Toggle,
            Description = "Автоматическая запись изменённых параметров в retroarch.cfg при закрытии."
        });
        list.Add(new SettingItem
        {
            CategoryId = "config", CategoryName = "Конфиг и Логи", Title = "Автозагрузка оверрайдов для ядер", CfgKey = "auto_overrides_enable", Kind = SettingControlKind.Toggle,
            Description = "Применение специфичных настроек видео/звука для конкретных систем."
        });
        list.Add(new SettingItem
        {
            CategoryId = "config", CategoryName = "Конфиг и Логи", Title = "Автозагрузка индивидуальных ремапов", CfgKey = "auto_remaps_enable", Kind = SettingControlKind.Toggle,
            Description = "Загрузка переназначенных клавиш для отдельных игр и платформ."
        });
        list.Add(new SettingItem
        {
            CategoryId = "config", CategoryName = "Конфиг и Логи", Title = "Записывать подробный лог в файл", CfgKey = "log_to_file", Kind = SettingControlKind.Toggle,
            Description = "Сохранение диагностической информации запуска в retroarch.log."
        });

        // 15. DIRECTORY (Каталоги)
        list.Add(new SettingItem
        {
            CategoryId = "directory", CategoryName = "Каталоги", Title = "Системные файлы и BIOS", CfgKey = "system_directory", Kind = SettingControlKind.DirectoryPath,
            Description = "Каталог прошивок BIOS для PS1, Saturn, Dreamcast, Neo Geo."
        });
        list.Add(new SettingItem
        {
            CategoryId = "directory", CategoryName = "Каталоги", Title = "Папка сохранений игр", CfgKey = "savefile_directory", Kind = SettingControlKind.DirectoryPath,
            Description = "Каталог для картриджных батареечных сохранений .srm и Memory Card."
        });
        list.Add(new SettingItem
        {
            CategoryId = "directory", CategoryName = "Каталоги", Title = "Папка стейтов", CfgKey = "savestate_directory", Kind = SettingControlKind.DirectoryPath,
            Description = "Каталог мгновенных сохранений игрового процесса."
        });
        list.Add(new SettingItem
        {
            CategoryId = "directory", CategoryName = "Каталоги", Title = "Папка скриншотов", CfgKey = "screenshot_directory", Kind = SettingControlKind.DirectoryPath,
            Description = "Каталог для сохранения снимков экрана, сделанных по клавише F8."
        });
        list.Add(new SettingItem
        {
            CategoryId = "directory", CategoryName = "Каталоги", Title = "Папка шейдеров", CfgKey = "video_shader_dir", Kind = SettingControlKind.DirectoryPath,
            Description = "Каталог графических фильтров .slangp и .glslp."
        });

        // 16. USER (Пользователь)
        list.Add(new SettingItem
        {
            CategoryId = "user", CategoryName = "Пользователь", Title = "Никнейм игрока", CfgKey = "netplay_nickname", Kind = SettingControlKind.TextBox,
            Description = "Отображаемое имя в сетевых баталиях и таблицах лидеров."
        });
        list.Add(new SettingItem
        {
            CategoryId = "user", CategoryName = "Пользователь", Title = "Режим приватности", CfgKey = "user_privacy_mode", Kind = SettingControlKind.Toggle,
            Description = "Скрытие имени пользователя и сведений о системе из сетевых запросов."
        });
        list.Add(new SettingItem
        {
            CategoryId = "user", CategoryName = "Пользователь", Title = "Язык интерфейса RetroArch", CfgKey = "user_language", Kind = SettingControlKind.ComboBox,
            Description = "Язык главного меню и системных сообщений RetroArch (9 = Русский, 0 = Английский).",
            Options = new() { "9", "0", "1", "2", "3", "4", "5" }
        });

        foreach (var it in list) AssignCategoryVisuals(it);
                foreach (var item in list)
        {
            item.Description = TypographyShield.ApplyRussianMicroTypography(item.Description);
            item.Title = TypographyShield.ApplyRussianMicroTypography(item.Title);
        }

        return list;
    }
    private static void AssignCategoryVisuals(SettingItem item)
    {
        string key = (item.CfgKey + " " + item.Title).ToLowerInvariant();

        // 1. Specific CfgKey / Title based semantic vector icons and vibrant colors
        if (key.Contains("video_driver") || key.Contains("video") || key.Contains("fullscreen") || key.Contains("scale") || key.Contains("aspect") || key.Contains("monitor") || key.Contains("screen") || key.Contains("resolution"))
        {
            // Monitor / Screen Icon (#A78BFA Purple)
            item.IconGeometry = "M21 16V4H3v12h18zm0-14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-7l2 3v1H8v-1l2-3H3a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h18z";
            item.IconColor = "#A78BFA";
            item.IconBgColor = "#22A78BFA";
        }
        else if (key.Contains("audio") || key.Contains("sound") || key.Contains("volume") || key.Contains("mixer") || key.Contains("resampler") || key.Contains("wasapi") || key.Contains("dsp") || key.Contains("midi"))
        {
            // Audio / Sound Wave Icon (#34D399 Emerald)
            item.IconGeometry = "M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02zM14 3.23v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z";
            item.IconColor = "#34D399";
            item.IconBgColor = "#2234D399";
        }
        else if (key.Contains("menu_driver") || key.Contains("menu") || key.Contains("ozone") || key.Contains("xmb") || key.Contains("rgui") || key.Contains("theme") || key.Contains("font") || key.Contains("wallpaper"))
        {
            // Menu / Layers Icon (#EC4899 Pink)
            item.IconGeometry = "M11.99 18.54l-7.37-5.73L3 14.07l9 7 9-7-1.63-1.27-7.38 5.74zM12 16l7.36-5.73L21 9.07l-9-7-9 7 1.63 1.27L12 16z";
            item.IconColor = "#EC4899";
            item.IconBgColor = "#22EC4899";
        }
        else if (key.Contains("input") || key.Contains("joypad") || key.Contains("controller") || key.Contains("gamepad") || key.Contains("analog") || key.Contains("autodetect") || key.Contains("remap") || key.Contains("dinput") || key.Contains("xinput"))
        {
            // Gamepad Icon (#FBBF24 Amber)
            item.IconGeometry = "M21 6H3c-1.1 0-2 .9-2 2v8c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm-10 7H8v3H6v-3H3v-2h3V8h2v3h3v2zm4.5 2c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5zm3-3c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5z";
            item.IconColor = "#FBBF24";
            item.IconBgColor = "#22FBBF24";
        }
        else if (key.Contains("camera") || key.Contains("record") || key.Contains("stream") || key.Contains("rtmp") || key.Contains("video_record"))
        {
            // Video Recording Camera Icon (#EF4444 Red)
            item.IconGeometry = "M17 10.5V7a1 1 0 0 0-1-1H4a1 1 0 0 0-1 1v10a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-3.5l4 4v-11l-4 4z";
            item.IconColor = "#EF4444";
            item.IconBgColor = "#22EF4444";
        }
        else if (key.Contains("location") || key.Contains("netplay") || key.Contains("network") || key.Contains("ip") || key.Contains("port") || key.Contains("server"))
        {
            // Globe Network Icon (#06B6D4 Cyan)
            item.IconGeometry = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z";
            item.IconColor = "#06B6D4";
            item.IconBgColor = "#2206B6D4";
        }
        else if (key.Contains("save") || key.Contains("state") || key.Contains("srm") || key.Contains("savestate") || key.Contains("backup"))
        {
            // Save / Floppy Disk Icon (#F97316 Orange)
            item.IconGeometry = "M17 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V7l-4-4zm-5 16c-1.66 0-3-1.34-3-3s1.34-3 3-3 3 1.34 3 3-1.34 3-3 3zm3-10H5V5h10v4z";
            item.IconColor = "#F97316";
            item.IconBgColor = "#22F97316";
        }
        else if (key.Contains("latency") || key.Contains("run_ahead") || key.Contains("preemptive") || key.Contains("frame_delay") || key.Contains("hard_sync") || key.Contains("vsync") || key.Contains("speed") || key.Contains("fast_forward"))
        {
            // Bolt / Fast Latency Icon (#FB7185 Rose)
            item.IconGeometry = "M7 2v11h3v9l7-12h-4l4-8z";
            item.IconColor = "#FB7185";
            item.IconBgColor = "#22FB7185";
        }
        else if (key.Contains("dir") || key.Contains("path") || key.Contains("folder") || key.Contains("directory"))
        {
            // Directory / Folder Icon (#EAB308 Yellow)
            item.IconGeometry = "M10 4H4c-1.11 0-2 .89-2 2v12a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V8c0-1.1-.89-2-2-2h-8l-2-2z";
            item.IconColor = "#EAB308";
            item.IconBgColor = "#22EAB308";
        }
        else if (key.Contains("ai_") || key.Contains("ai_service") || key.Contains("translate") || key.Contains("voice") || key.Contains("ocr") || key.Contains("walkthrough"))
        {
            // AI Sparkle / Brain Icon (#8B5CF6 Violet)
            item.IconGeometry = "M12 2L9.19 8.63 2 9.24l5.46 4.73L5.82 21 12 17.27 18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2z";
            item.IconColor = "#8B5CF6";
            item.IconBgColor = "#228B5CF6";
        }
        else if (key.Contains("cheevos") || key.Contains("achievement") || key.Contains("trophy") || key.Contains("badge") || key.Contains("leaderboard"))
        {
            // Trophy Icon (#F59E0B Gold)
            item.IconGeometry = "M19 5h-2V3H7v2H5c-1.1 0-2 .9-2 2v1c0 2.55 1.92 4.63 4.39 4.94A5.01 5.01 0 0 0 11 15.9V19H7v2h10v-2h-4v-3.1c1.86-.44 3.32-1.84 3.61-3.66C19.08 11.63 21 9.55 21 8V7c0-1.1-.9-2-2-2zM5 8V7h2v3.82C5.84 10.4 5 9.3 5 8zm14 0c0 1.3-.84 2.4-2 2.82V7h2v1z";
            item.IconColor = "#F59E0B";
            item.IconBgColor = "#22F59E0B";
        }
        else if (key.Contains("shader") || key.Contains("filter") || key.Contains("crt") || key.Contains("bezel") || key.Contains("curvature"))
        {
            // Palette Icon (#EC4899 Pink)
            item.IconGeometry = "M12 3c-4.97 0-9 4.03-9 9 0 2.12.74 4.07 1.97 5.61L4.35 19.4c-.39.39-.39 1.02 0 1.41.39.39 1.02.39 1.41 0l1.9-1.9C9.22 19.59 10.56 20 12 20c4.97 0 9-4.03 9-9s-4.03-9-9-9zm0 15c-3.31 0-6-2.69-6-6s2.69-6 6-6 6 2.69 6 6-2.69 6-6 6z";
            item.IconColor = "#EC4899";
            item.IconBgColor = "#22EC4899";
        }
        else if (key.Contains("fps") || key.Contains("osd") || key.Contains("stat") || key.Contains("msg") || key.Contains("notification") || key.Contains("memory_show"))
        {
            // OSD Screen Display Icon (#10B981 Emerald)
            item.IconGeometry = "M21 3H3c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h5v2h8v-2h5c1.1 0 1.99-.9 1.99-2L23 5c0-1.1-.9-2-2-2zm0 14H3V5h18v12z";
            item.IconColor = "#10B981";
            item.IconBgColor = "#2210B981";
        }
        else if (key.Contains("core") || key.Contains("libretro") || key.Contains("system") || key.Contains("firmware") || key.Contains("bios"))
        {
            // Core / Puzzle Icon (#60A5FA Blue)
            item.IconGeometry = "M20 12h-2V8h-4V6a2 2 0 0 0-4 0v2H6a2 2 0 0 0-2 2v4h2a2 2 0 0 1 0 4H4v4a2 2 0 0 0 2 2h4v-2a2 2 0 0 1 4 0v2h4a2 2 0 0 0 2-2v-4h-2a2 2 0 0 1 0-4h2v-4a2 2 0 0 0-2-2z";
            item.IconColor = "#60A5FA";
            item.IconBgColor = "#2260A5FA";
        }
        else if (key.Contains("power") || key.Contains("battery") || key.Contains("suspend") || key.Contains("throttle"))
        {
            // Power / Battery Icon (#14B8A6 Teal)
            item.IconGeometry = "M16 4h-2V2h-4v2H8c-.55 0-1 .45-1 1v16c0 .55.45 1 1 1h8c.55 0 1-.45 1-1V5c0-.55-.45-1-1-1zm-1 10h-2v4h-2v-4H9l3-6v4h2l-1 2z";
            item.IconColor = "#14B8A6";
            item.IconBgColor = "#2214B8A6";
        }
        else
        {
            // Default Steering Wheel / Drivers / Settings Sliders (#38BDF8 Cyan)
            item.IconGeometry = "M12 2a10 10 0 1 0 10 10A10 10 0 0 0 12 2zm1 17.93V15a3 3 0 0 1-2 0v4.93A8 8 0 0 1 4.07 13H9a3 3 0 0 1 0-2H4.07A8 8 0 0 1 11 4.07V9a3 3 0 0 1 2 0V4.07A8 8 0 0 1 19.93 11H15a3 3 0 0 1 0 2h4.93A8 8 0 0 1 13 19.93z";
            item.IconColor = "#38BDF8";
            item.IconBgColor = "#2238BDF8";
        }
    }
}
