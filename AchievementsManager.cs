using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace StormRetroArch;

public class AchievementItem : INotifyPropertyChanged
{
    private bool _isUnlocked;

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string GameTitle { get; set; } = "";
    public int Points { get; set; } = 10;
    public string Rarity { get; set; } = "15.2% (Редкое)";
    public string Icon { get; set; } = "🏆";
    public string BadgeUrl { get; set; } = "";

    public bool IsUnlocked
    {
        get => _isUnlocked;
        set
        {
            if (_isUnlocked != value)
            {
                _isUnlocked = value;
                OnPropertyChanged();
            }
        }
    }

    public string UnlockDate { get; set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class AchievementsManager
{
    public static bool IsHardcoreEnabled
    {
        get => RetroArchConfigManager.GetBool("cheevos_hardcore_mode_enable", true);
        set
        {
            RetroArchConfigManager.SetBool("cheevos_hardcore_mode_enable", value);
            RetroArchConfigManager.Save();
        }
    }

    public static string Username
    {
        get => RetroArchConfigManager.GetString("cheevos_username", "RetroMaster_2026");
        set
        {
            RetroArchConfigManager.SetString("cheevos_username", value);
            RetroArchConfigManager.Save();
        }
    }

    public static List<AchievementItem> GetSampleAchievements()
    {
        return new List<AchievementItem>
        {
            new() { Title = "Первый шаг в Ноктюрн", GameTitle = "Castlevania: Symphony of the Night (PS1)", Description = "Войдите в замок Дракулы и победите первого босса Галамота", Points = 10, Rarity = "64.1% (Обычное)", Icon = "⚔️", IsUnlocked = true, UnlockDate = "Вчера в 21:40" },
            new() { Title = "Владыка Инверсии", GameTitle = "Castlevania: Symphony of the Night (PS1)", Description = "Откройте перевёрнутый замок и исследуйте более 180% карты", Points = 25, Rarity = "28.5% (Редкое)", Icon = "🏰", IsUnlocked = true, UnlockDate = "Сегодня в 14:12" },
            new() { Title = "Апофеоз Алукарда", GameTitle = "Castlevania: Symphony of the Night (PS1)", Description = "Исследуйте 200.6% карты и получите меч Криссагрим", Points = 50, Rarity = "8.2% (Ультра-редкое)", Icon = "👑", IsUnlocked = false },
            new() { Title = "Остров Йоши пройден", GameTitle = "Super Mario World (SNES)", Description = "Пройдите все уровни первого мира без потери жизней", Points = 10, Rarity = "85.0% (Обычное)", Icon = "🦖", IsUnlocked = true, UnlockDate = "02.11.2026" },
            new() { Title = "Звёздная Дорога (Star Road)", GameTitle = "Super Mario World (SNES)", Description = "Найдите все секретные выходы на Звёздной дороге", Points = 25, Rarity = "31.4% (Редкое)", Icon = "⭐", IsUnlocked = false },
            new() { Title = "Сверхзвуковой Ёж", GameTitle = "Sonic The Hedgehog (Genesis)", Description = "Завершите зону Green Hill Act 1 быстрее 30 секунд", Points = 15, Rarity = "42.0% (Необычное)", Icon = "🦔", IsUnlocked = true, UnlockDate = "03.11.2026" },
            new() { Title = "Изумруды Хаоса", GameTitle = "Sonic The Hedgehog (Genesis)", Description = "Соберите все 6 изумрудов в специальных стадиях", Points = 50, Rarity = "11.7% (Очень редкое)", Icon = "💎", IsUnlocked = false },
            new() { Title = "Хранитель Времён", GameTitle = "Chrono Trigger (SNES)", Description = "Откройте концовку Beyond Time и спасите будущее", Points = 50, Rarity = "14.3% (Очень редкое)", Icon = "⏳", IsUnlocked = false }
        };
    }

    // ==================== CRC32 COMPUTATION ====================

    private static readonly uint[] CrcTable = new uint[256];

    static AchievementsManager()
    {
        uint poly = 0xEDB88320;
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 8; j > 0; j--)
            {
                if ((crc & 1) == 1) crc = (crc >> 1) ^ poly;
                else crc >>= 1;
            }
            CrcTable[i] = crc;
        }
    }

    public static string CalculateCrc32(string filePath)
    {
        if (!File.Exists(filePath)) return "00000000";

        try
        {
            uint crc = 0xFFFFFFFF;
            using var fs = File.OpenRead(filePath);
            byte[] buffer = new byte[65536];
            int read;

            while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    byte b = buffer[i];
                    crc = (crc >> 8) ^ CrcTable[(crc & 0xFF) ^ b];
                }
            }

            crc ^= 0xFFFFFFFF;
            return crc.ToString("X8");
        }
        catch
        {
            return "00000000";
        }
    }
}
