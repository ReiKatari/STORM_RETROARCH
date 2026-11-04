using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace StormRetroArch;

public class BiosInfoItem : INotifyPropertyChanged
{
    private bool _isInstalled;
    private string _statusText = "⚠️ Не найден в system/";

    public string FileName { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Description { get; set; } = "";
    public string ExpectedMd5 { get; set; } = "";
    public long ExpectedSize { get; set; }
    public string CurrentFilePath { get; set; } = "";
    public string ActualMd5 { get; set; } = "";

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled != value)
            {
                _isInstalled = value;
                OnPropertyChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText != value)
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class BiosManager
{
    public static readonly string SystemDir = Path.Combine(CoreManager.AppDir, "system");

    static BiosManager()
    {
        try
        {
            if (!Directory.Exists(SystemDir)) Directory.CreateDirectory(SystemDir);
        }
        catch { }
    }

    public static readonly List<BiosInfoItem> Database = new()
    {
        new() { FileName = "scph5500.bin", Platform = "Sony PlayStation 1 [Japan]", ExpectedMd5 = "8dd7d5296a650fac7319bce665a6a53c", ExpectedSize = 524288,
            Description = "Оригинальный японский BIOS для PS1 (NTSC-J). Необходим для максимальной совместимости японских ромов в SwanStation/Beetle." },
        new() { FileName = "scph5501.bin", Platform = "Sony PlayStation 1 [USA]", ExpectedMd5 = "490f666e1afb15b7362b406ed1cea246", ExpectedSize = 524288,
            Description = "Американский эталонный BIOS для PS1 (NTSC-U). Обеспечивает 100% совместимость американских версий игр." },
        new() { FileName = "scph5502.bin", Platform = "Sony PlayStation 1 [Europe]", ExpectedMd5 = "32736f17079d0b2b7024407c39bd3050", ExpectedSize = 524288,
            Description = "Европейский PAL BIOS для PS1 (PAL 50Hz). Необходим для европейских версий и переводов на базе PAL." },
        new() { FileName = "sega_101.bin", Platform = "Sega Saturn [Japan]", ExpectedMd5 = "85ec9ca47d8f6807718151cbcca8b964", ExpectedSize = 524288,
            Description = "Японский BIOS для Sega Saturn v1.01. Критически важен для запуска эксклюзивных японских хитов в Beetle Saturn." },
        new() { FileName = "mpr-17933.bin", Platform = "Sega Saturn [USA/EU]", ExpectedMd5 = "436611c67d1e3132eb72008ff770e517", ExpectedSize = 524288,
            Description = "Западный BIOS для Sega Saturn (USA/Europe). Обеспечивает запуск англоязычных релизов." },
        new() { FileName = "dc_boot.bin", Platform = "Sega Dreamcast [Boot ROM]", ExpectedMd5 = "e10c53c2f8b90bab96ead2d368858623", ExpectedSize = 2097152,
            Description = "Основной загрузочный системный ROM приставки Dreamcast для ядра Flycast." },
        new() { FileName = "dc_flash.bin", Platform = "Sega Dreamcast [Flash ROM]", ExpectedMd5 = "0a93f7940c455905ab6e36b66ca692e2", ExpectedSize = 131072,
            Description = "Энергонезависимая память настроек времени, языка и даты Dreamcast." },
        new() { FileName = "bios7.bin", Platform = "Nintendo DS [ARM7]", ExpectedMd5 = "df692a80a5b1bc90728bc3df32d56223", ExpectedSize = 16384,
            Description = "Микрокод вспомогательного сопроцессора ARM7 для ядра melonDS DS." },
        new() { FileName = "bios9.bin", Platform = "Nintendo DS [ARM9]", ExpectedMd5 = "a392174eb3e572fed6447e08d8758517", ExpectedSize = 4096,
            Description = "Микрокод основного вычислительного процессора ARM9 для Nintendo DS." },
        new() { FileName = "firmware.bin", Platform = "Nintendo DS [Firmware]", ExpectedMd5 = "e45033d9b0fa6b0de071292bba7c9d13", ExpectedSize = 262144,
            Description = "Официальная прошивка операционной системы Nintendo DS с системными часами и сенсорным вводом." },
        new() { FileName = "gba_bios.bin", Platform = "Game Boy Advance", ExpectedMd5 = "a860e8c0b6d573d191e4ec7db1b1e4f6", ExpectedSize = 16384,
            Description = "Официальный BIOS Game Boy Advance. Необходим для аутентичной заставки и математических подпрограмм SWI." },
        new() { FileName = "syscard3.pce", Platform = "PC Engine CD / TurboGrafx-CD", ExpectedMd5 = "38179f2dc629731613045624cf5b376a", ExpectedSize = 262144,
            Description = "Super System Card v3.0 для запуска CD-ROM игр PC Engine в Beetle PCE Fast." },
        new() { FileName = "bios_CD_U.bin", Platform = "Sega CD [USA]", ExpectedMd5 = "8d829dc8017210e75a0044432247dc03", ExpectedSize = 131072,
            Description = "BIOS привода Sega CD Model 2 для американского региона в Genesis Plus GX / Picodrive." },
        new() { FileName = "bios_CD_E.bin", Platform = "Sega CD [Europe]", ExpectedMd5 = "e66fa1dc5820d254611fd9fb60731703", ExpectedSize = 131072,
            Description = "BIOS привода Mega CD для европейского региона." },
        new() { FileName = "bios_CD_J.bin", Platform = "Mega CD [Japan]", ExpectedMd5 = "278a9397e144c4e1b8dc0320308d6d63", ExpectedSize = 131072,
            Description = "BIOS оригинального японского привода Mega CD Model 1." },
        new() { FileName = "neogeo.zip", Platform = "SNK Neo Geo AES / MVS", ExpectedMd5 = "", ExpectedSize = 0,
            Description = "Мульти-региональный биос-пак аркадных автоматов Neo Geo MVS и домашней консоли AES для ядра FBNeo." },
        new() { FileName = "panafz10.bin", Platform = "Panasonic 3DO Interactive", ExpectedMd5 = "5824e6fb41c8398ad2a143d94721f456", ExpectedSize = 1048576,
            Description = "Оригинальный BIOS японской/американской консоли Panasonic 3DO REAL FZ-10." },
        new() { FileName = "coleco.rom", Platform = "ColecoVision", ExpectedMd5 = "3aa93ef441604c7533887ae233fa486e", ExpectedSize = 8192,
            Description = "Встроенное ПЗУ операционной системы ColecoVision." },
        new() { FileName = "exec.bin", Platform = "Mattel Intellivision", ExpectedMd5 = "cbce86f761db81c40f807264a2754c74", ExpectedSize = 8192,
            Description = "Исполняемое ПЗУ операционной системы Executive ROM для Intellivision." },
        new() { FileName = "grom.bin", Platform = "Mattel Intellivision [Graphics]", ExpectedMd5 = "a57f84261e30d3507911175404f6286f", ExpectedSize = 2048,
            Description = "Графическое ПЗУ шрифтов и спрайтов Graphics ROM для Intellivision." },
        new() { FileName = "kick34005.A500", Platform = "Commodore Amiga 500", ExpectedMd5 = "c4f0f55f1e3bb332f31de4537c107f7e", ExpectedSize = 262144,
            Description = "Kickstart ROM 1.3 для Amiga 500 / 2000 — золотой стандарт классических игр Amiga." },
        new() { FileName = "kick40068.A1200", Platform = "Commodore Amiga 1200", ExpectedMd5 = "646773759326fbac3b2311fd8c8793ee", ExpectedSize = 524288,
            Description = "Kickstart ROM 3.1 для Amiga 1200 с чипсетом AGA." },
        new() { FileName = "lynxboot.img", Platform = "Atari Lynx", ExpectedMd5 = "fcd403db69f07173b85933a00e12b737", ExpectedSize = 512,
            Description = "Загрузочный мини-образ портативной консоли Atari Lynx для ядра Handy." },
        new() { FileName = "ps2-0230a-20080220.bin", Platform = "Sony PlayStation 2", ExpectedMd5 = "0558ebbe75b1c97a8ecdae1e07504cb0", ExpectedSize = 4194304,
            Description = "BIOS консоли PlayStation 2 v2.30 для ядер PCSX2 / Play!." },
        new() { FileName = "tos.img", Platform = "Atari ST / Falcon", ExpectedMd5 = "3a42ebbb7a233303698eaefd95155f69", ExpectedSize = 196608,
            Description = "Операционная система TOS 1.04 Rainbow для 16-битных компьютеров Atari ST." }
    };

    public static List<BiosInfoItem> InspectSystemDirectory()
    {
        var result = new List<BiosInfoItem>();

        foreach (var def in Database)
        {
            var item = new BiosInfoItem
            {
                FileName = def.FileName,
                Platform = def.Platform,
                Description = def.Description,
                ExpectedMd5 = def.ExpectedMd5,
                ExpectedSize = def.ExpectedSize
            };

            string directPath = Path.Combine(SystemDir, def.FileName);
            if (File.Exists(directPath))
            {
                item.CurrentFilePath = directPath;
                item.IsInstalled = true;
                item.StatusText = "✅ Установлен (Файл найден)";

                if (!string.IsNullOrEmpty(def.ExpectedMd5))
                {
                    string md5 = CalculateMd5(directPath);
                    item.ActualMd5 = md5;
                    if (md5.Equals(def.ExpectedMd5, StringComparison.OrdinalIgnoreCase))
                    {
                        item.StatusText = "✅ Установлен (MD5 OK)";
                    }
                    else
                    {
                        item.StatusText = "⚠️ Установлен (Несовпадение MD5)";
                    }
                }
            }
            else
            {
                item.IsInstalled = false;
                item.StatusText = "⚠️ Не найден в system/";
            }

            result.Add(item);
        }

        return result;
    }

    public static string CalculateMd5(string filePath)
    {
        try
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }

    public static (bool Success, string Message) ImportBios(string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath))
            return (false, "Файл не существует.");

        string md5 = CalculateMd5(sourceFilePath);
        var fi = new FileInfo(sourceFilePath);

        // Check if matches known BIOS by MD5 or size
        var match = Database.FirstOrDefault(b =>
            (!string.IsNullOrEmpty(b.ExpectedMd5) && b.ExpectedMd5.Equals(md5, StringComparison.OrdinalIgnoreCase)) ||
            (b.ExpectedSize > 0 && b.ExpectedSize == fi.Length && b.FileName.Equals(fi.Name, StringComparison.OrdinalIgnoreCase)));

        string targetName = match != null ? match.FileName : fi.Name;
        string targetPath = Path.Combine(SystemDir, targetName);

        try
        {
            File.Copy(sourceFilePath, targetPath, overwrite: true);
            string extra = match != null ? $" ({match.Platform})" : "";
            return (true, $"BIOS {targetName}{extra} успешно скопирован в system/!");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка копирования BIOS: {ex.Message}");
        }
    }
}
