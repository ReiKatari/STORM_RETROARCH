using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace StormRetroArch;

public class ShaderPresetItem : INotifyPropertyChanged
{
    private bool _isActive;

    public string Name { get; set; } = "";
    public string Category { get; set; } = "CRT ЭЛТ";
    public string Description { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public int PassesCount { get; set; } = 1;
    public string Icon { get; set; } = "🎨";

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class ShaderManager
{
    public static readonly string ShadersDir = Path.Combine(CoreManager.AppDir, "shaders");
    public static readonly string SlangDir = Path.Combine(ShadersDir, "shaders_slang");
    public static readonly string FiltersDir = Path.Combine(CoreManager.AppDir, "filters");

    public static readonly List<ShaderPresetItem> BuiltinShaders = new()
    {
        new() { Name = "CRT Royale", Category = "CRT ЭЛТ", Icon = "📺", PassesCount = 12,
            RelativePath = "shaders_slang/crt/crt-royale.slangp",
            Description = "Флагманский физически точный шейдер кинескопа с симуляцией теневой маски, апертурной решётки и фосфорных триад." },
        new() { Name = "CRT Geom", Category = "CRT ЭЛТ", Icon = "📺", PassesCount = 4,
            RelativePath = "shaders_slang/crt/crt-geom.slangp",
            Description = "Изогнутый классический экран кинескопа 90-х с мягким свечением сканлайнов и виньетированием." },
        new() { Name = "CRT Easymode", Category = "CRT ЭЛТ", Icon = "📺", PassesCount = 2,
            RelativePath = "shaders_slang/crt/crt-easymode.slangp",
            Description = "Ультра-быстрый лёгкий ЭЛТ-шейдер с минимальной нагрузкой на GPU. Идеально для слабых видеокарт." },
        new() { Name = "Sony Trinitron BVM", Category = "CRT ЭЛТ", Icon = "📺", PassesCount = 6,
            RelativePath = "shaders_slang/crt/crt-trinitron.slangp",
            Description = "Аутентичный студийный вещательный монитор Sony Trinitron с безупречной резкостью линий." },
        new() { Name = "xBRZ 4x Pro", Category = "Апскейл HD", Icon = "✨", PassesCount = 3,
            RelativePath = "shaders_slang/scale/xbrz-4x.slangp",
            Description = "Продвинутый алгоритм масштабирования ретро-пикселей в гладкую векторную графику без размытия." },
        new() { Name = "xBRZ 6x Ultra HD", Category = "Апскейл HD", Icon = "✨", PassesCount = 4,
            RelativePath = "shaders_slang/scale/xbrz-6x.slangp",
            Description = "Максимальный 6-кратный коэффициент апскейла для идеального отображения на 1440p и 4K дисплеях." },
        new() { Name = "ScaleFX 9x", Category = "Апскейл HD", Icon = "✨", PassesCount = 5,
            RelativePath = "shaders_slang/scale/scalefx.slangp",
            Description = "Нейроподобная интерполяция кривых линий с адаптивным сглаживанием углов." },
        new() { Name = "HQ4x Classic", Category = "Апскейл HD", Icon = "✨", PassesCount = 2,
            RelativePath = "shaders_slang/scale/hq4x.slangp",
            Description = "Классический высококачественный сглаживающий апскейлер золотой эры эмуляторов." },
        new() { Name = "AMD FSR + CAS", Category = "Сглаживание", Icon = "⚡", PassesCount = 2,
            RelativePath = "shaders_slang/edge-smoothing/fsr-cas.slangp",
            Description = "FidelityFX Super Resolution в связке с контрастно-адаптивной резкостью для четкости текстур." },
        new() { Name = "FXAA + SMAA Ultra", Category = "Сглаживание", Icon = "⚡", PassesCount = 3,
            RelativePath = "shaders_slang/anti-aliasing/smaa.slangp",
            Description = "Комбинированное субпиксельное морфологическое сглаживание для 3D игр PS1, N64 и Dreamcast." },
        new() { Name = "Game Boy DMG Grid", Category = "Портативные LCD", Icon = "🎮", PassesCount = 3,
            RelativePath = "shaders_slang/handheld/gameboy-dmg.slangp",
            Description = "Аутентичная зелёная ЖК-матрица оригинального Game Boy 1989 с межпиксельной сеткой и тенями." },
        new() { Name = "GBA LCD 3x Color", Category = "Портативные LCD", Icon = "🎮", PassesCount = 2,
            RelativePath = "shaders_slang/handheld/gba-color.slangp",
            Description = "Субпиксели и насыщенная цветокоррекция экрана Game Boy Advance SP AGS-101." },
        new() { Name = "NDS Touch Dual-Grid", Category = "Портативные LCD", Icon = "🎮", PassesCount = 2,
            RelativePath = "shaders_slang/handheld/nds-grid.slangp",
            Description = "Матрица двух экранов Nintendo DS с лёгким тёплым балансом белого." },
        new() { Name = "PSP Color Accurate", Category = "Портативные LCD", Icon = "🎮", PassesCount = 1,
            RelativePath = "shaders_slang/handheld/psp-color.slangp",
            Description = "Точная калибровка цветового охвата sRGB под оригинальный экран Sony PSP-2000." },
        new() { Name = "NTSC Composite VHS", Category = "Ретро VHS/NTSC", Icon = "📼", PassesCount = 5,
            RelativePath = "shaders_slang/vhs/ntsc-vhs.slangp",
            Description = "Аутентичный аналоговый композитный видеосигнал с цветовыми шумами и чересстрочной развёрткой." },
        new() { Name = "NTSC S-Video Clean", Category = "Ретро VHS/NTSC", Icon = "📼", PassesCount = 3,
            RelativePath = "shaders_slang/vhs/ntsc-svideo.slangp",
            Description = "Чёткий аналоговый сигнал S-Video с раздельными каналами яркости и цветности." },
        new() { Name = "Kawase Bloom Glow", Category = "Сглаживание", Icon = "🌟", PassesCount = 4,
            RelativePath = "shaders_slang/bloom/kawase-glow.slangp",
            Description = "Мягкое кинематографичное неоновое свечение ярких пикселей и спецэффектов." },
        new() { Name = "Mega Bezel Glass Reflection", Category = "CRT ЭЛТ", Icon = "📺", PassesCount = 8,
            RelativePath = "shaders_slang/bezel/mega-bezel.slangp",
            Description = "Объёмный телевизор с динамическими бликами на стекле кинескопа и 3D рамкой корпуса." },
        new() { Name = "Audio DSP Lowpass Filter", Category = "Аудио-фильтры", Icon = "🔊", PassesCount = 1,
            RelativePath = "filters/audio/lowpass.filt",
            Description = "Мягкий аналоговый срез ультразвука и шипения, имитирующий акустику старого телевизора." },
        new() { Name = "Audio DSP Arcade Reverb", Category = "Аудио-фильтры", Icon = "🔊", PassesCount = 1,
            RelativePath = "filters/audio/reverb.filt",
            Description = "Пространственная реверберация игрового зала аркадных автоматов 90-х." }
    };

    static ShaderManager()
    {
        try
        {
            if (!Directory.Exists(ShadersDir)) Directory.CreateDirectory(ShadersDir);
            if (!Directory.Exists(SlangDir)) Directory.CreateDirectory(SlangDir);
            if (!Directory.Exists(FiltersDir)) Directory.CreateDirectory(FiltersDir);
        }
        catch { }
    }

    public static List<ShaderPresetItem> GetAllShaders()
    {
        var result = new List<ShaderPresetItem>(BuiltinShaders);

        // Check active shader from retroarch.cfg
        string activeShader = RetroArchConfigManager.GetString("video_shader_dir", "");
        bool isEnabled = RetroArchConfigManager.GetBool("video_shader_enable", true);

        foreach (var item in result)
        {
            item.IsActive = isEnabled && !string.IsNullOrEmpty(activeShader) &&
                            (activeShader.Contains(Path.GetFileName(item.RelativePath), StringComparison.OrdinalIgnoreCase));
        }

        return result;
    }

    public static void ApplyShader(ShaderPresetItem item)
    {
        RetroArchConfigManager.SetBool("video_shader_enable", true);
        string fullPath = Path.Combine(CoreManager.AppDir, item.RelativePath);
        RetroArchConfigManager.SetString("video_shader_dir", fullPath);
        RetroArchConfigManager.Save();

        foreach (var s in BuiltinShaders)
        {
            s.IsActive = (s == item);
        }
    }

    public static void DisableShaders()
    {
        RetroArchConfigManager.SetBool("video_shader_enable", false);
        RetroArchConfigManager.Save();

        foreach (var s in BuiltinShaders)
        {
            s.IsActive = false;
        }
    }
}
