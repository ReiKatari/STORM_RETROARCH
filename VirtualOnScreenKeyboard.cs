using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StormRetroArch;

/// <summary>
/// Определение виртуальной клавиши экранной клавиатуры эмулятора.
/// Содержит Libretro-код, символ Юникода, байт CP866 для DOS и визуальную метку.
/// </summary>
public record VirtualKeyDef(
    string Label,
    uint RetroKey,
    char UnicodeChar,
    byte Cp866Byte,
    ushort Modifiers = 0,
    double WidthUnits = 1.0
);

public enum VirtualKeyboardLayout
{
    English,
    Russian,
    NumbersAndFKeys
}

/// <summary>
/// Двуязычная (РУС / ENG) виртуальная экранная клавиатура эмулятора.
/// Обеспечивает навигацию крестовиной D-Pad (Row, Col) и прямой ввод текста
/// во все компьютерные ядра (DOSBox-Pure, Fuse, VICE, PUAE, ScummVM).
/// </summary>
public sealed class VirtualOnScreenKeyboard
{
    private static readonly Lazy<VirtualOnScreenKeyboard> _instance = new(() => new VirtualOnScreenKeyboard());
    public static VirtualOnScreenKeyboard Instance => _instance.Value;

    private VirtualKeyboardLayout _layout = VirtualKeyboardLayout.English;
    private bool _shift = false;
    private int _currentRow = 1;
    private int _currentCol = 0;

    public VirtualKeyboardLayout Layout
    {
        get => _layout;
        set => _layout = value;
    }

    public bool Shift
    {
        get => _shift;
        set => _shift = value;
    }

    public int CurrentRow => _currentRow;
    public int CurrentCol => _currentCol;

    // Libretro Key Constants
    private const uint RETROK_BACKSPACE = 8;
    private const uint RETROK_TAB = 9;
    private const uint RETROK_RETURN = 13;
    private const uint RETROK_ESCAPE = 27;
    private const uint RETROK_SPACE = 32;
    private const uint RETROK_UP = 273;
    private const uint RETROK_DOWN = 274;
    private const uint RETROK_RIGHT = 275;
    private const uint RETROK_LEFT = 276;
    private const uint RETROK_LCTRL = 306;
    private const uint RETROK_LSHIFT = 304;
    private const uint RETROK_LALT = 308;
    private const uint RETROK_F1 = 282;

    public List<List<VirtualKeyDef>> GetCurrentRows()
    {
        return _layout switch
        {
            VirtualKeyboardLayout.Russian => BuildRussianLayout(),
            VirtualKeyboardLayout.NumbersAndFKeys => BuildFKeysLayout(),
            _ => BuildEnglishLayout()
        };
    }

    private List<List<VirtualKeyDef>> BuildEnglishLayout()
    {
        return new List<List<VirtualKeyDef>>
        {
            // Row 0: Function & System
            new()
            {
                new("ESC", RETROK_ESCAPE, '\x1B', 27, 0, 1.2),
                new("F1", RETROK_F1, '\0', 0),
                new("F2", RETROK_F1 + 1, '\0', 0),
                new("F3", RETROK_F1 + 2, '\0', 0),
                new("F4", RETROK_F1 + 3, '\0', 0),
                new("F5", RETROK_F1 + 4, '\0', 0),
                new("F6", RETROK_F1 + 5, '\0', 0),
                new("F7", RETROK_F1 + 6, '\0', 0),
                new("F8", RETROK_F1 + 7, '\0', 0),
                new("F9", RETROK_F1 + 8, '\0', 0),
                new("F10", RETROK_F1 + 9, '\0', 0),
                new("BKSP", RETROK_BACKSPACE, '\b', 8, 0, 1.4)
            },
            // Row 1: Numbers
            new()
            {
                new("`", 96, '`', (byte)'`'),
                new("1", 49, '1', (byte)'1'),
                new("2", 50, '2', (byte)'2'),
                new("3", 51, '3', (byte)'3'),
                new("4", 52, '4', (byte)'4'),
                new("5", 53, '5', (byte)'5'),
                new("6", 54, '6', (byte)'6'),
                new("7", 55, '7', (byte)'7'),
                new("8", 56, '8', (byte)'8'),
                new("9", 57, '9', (byte)'9'),
                new("0", 48, '0', (byte)'0'),
                new("-", 45, '-', (byte)'-'),
                new("=", 61, '=', (byte)'=')
            },
            // Row 2: QWERTY Row 1
            new()
            {
                new("TAB", RETROK_TAB, '\t', 9, 0, 1.3),
                new(_shift ? "Q" : "q", 113, _shift ? 'Q' : 'q', (byte)(_shift ? 'Q' : 'q')),
                new(_shift ? "W" : "w", 119, _shift ? 'W' : 'w', (byte)(_shift ? 'W' : 'w')),
                new(_shift ? "E" : "e", 101, _shift ? 'E' : 'e', (byte)(_shift ? 'E' : 'e')),
                new(_shift ? "R" : "r", 114, _shift ? 'R' : 'r', (byte)(_shift ? 'R' : 'r')),
                new(_shift ? "T" : "t", 116, _shift ? 'T' : 't', (byte)(_shift ? 'T' : 't')),
                new(_shift ? "Y" : "y", 121, _shift ? 'Y' : 'y', (byte)(_shift ? 'Y' : 'y')),
                new(_shift ? "U" : "u", 117, _shift ? 'U' : 'u', (byte)(_shift ? 'U' : 'u')),
                new(_shift ? "I" : "i", 105, _shift ? 'I' : 'i', (byte)(_shift ? 'I' : 'i')),
                new(_shift ? "O" : "o", 111, _shift ? 'O' : 'o', (byte)(_shift ? 'O' : 'o')),
                new(_shift ? "P" : "p", 112, _shift ? 'P' : 'p', (byte)(_shift ? 'P' : 'p')),
                new("[", 91, '[', (byte)'['),
                new("]", 93, ']', (byte)']')
            },
            // Row 3: QWERTY Row 2
            new()
            {
                new("РУС", 0, '\0', 0, 0, 1.3),
                new(_shift ? "A" : "a", 97, _shift ? 'A' : 'a', (byte)(_shift ? 'A' : 'a')),
                new(_shift ? "S" : "s", 115, _shift ? 'S' : 's', (byte)(_shift ? 'S' : 's')),
                new(_shift ? "D" : "d", 100, _shift ? 'D' : 'd', (byte)(_shift ? 'D' : 'd')),
                new(_shift ? "F" : "f", 102, _shift ? 'F' : 'f', (byte)(_shift ? 'F' : 'f')),
                new(_shift ? "G" : "g", 103, _shift ? 'G' : 'g', (byte)(_shift ? 'G' : 'g')),
                new(_shift ? "H" : "h", 104, _shift ? 'H' : 'h', (byte)(_shift ? 'H' : 'h')),
                new(_shift ? "J" : "j", 106, _shift ? 'J' : 'j', (byte)(_shift ? 'J' : 'j')),
                new(_shift ? "K" : "k", 107, _shift ? 'K' : 'k', (byte)(_shift ? 'K' : 'k')),
                new(_shift ? "L" : "l", 108, _shift ? 'L' : 'l', (byte)(_shift ? 'L' : 'l')),
                new(";", 59, ';', (byte)';'),
                new("'", 39, '\'', (byte)'\''),
                new("ENTER", RETROK_RETURN, '\r', 13, 0, 1.4)
            },
            // Row 4: QWERTY Row 3
            new()
            {
                new("SHIFT", RETROK_LSHIFT, '\0', 0, 0, 1.4),
                new(_shift ? "Z" : "z", 122, _shift ? 'Z' : 'z', (byte)(_shift ? 'Z' : 'z')),
                new(_shift ? "X" : "x", 120, _shift ? 'X' : 'x', (byte)(_shift ? 'X' : 'x')),
                new(_shift ? "C" : "c", 99, _shift ? 'C' : 'c', (byte)(_shift ? 'C' : 'c')),
                new(_shift ? "V" : "v", 118, _shift ? 'V' : 'v', (byte)(_shift ? 'V' : 'v')),
                new(_shift ? "B" : "b", 98, _shift ? 'B' : 'b', (byte)(_shift ? 'B' : 'b')),
                new(_shift ? "N" : "n", 110, _shift ? 'N' : 'n', (byte)(_shift ? 'N' : 'n')),
                new(_shift ? "M" : "m", 109, _shift ? 'M' : 'm', (byte)(_shift ? 'M' : 'm')),
                new(",", 44, ',', (byte)','),
                new(".", 46, '.', (byte)'.'),
                new("/", 47, '/', (byte)'/'),
                new("ВВЕРХ", RETROK_UP, '\0', 0, 0, 1.1)
            },
            // Row 5: Bottom Navigation
            new()
            {
                new("CTRL", RETROK_LCTRL, '\0', 0, 0, 1.2),
                new("ALT", RETROK_LALT, '\0', 0, 0, 1.1),
                new("ПРОБЕЛ", RETROK_SPACE, ' ', 32, 0, 4.0),
                new("ВЛЕВО", RETROK_LEFT, '\0', 0),
                new("ВНИЗ", RETROK_DOWN, '\0', 0),
                new("ВПРАВО", RETROK_RIGHT, '\0', 0)
            }
        };
    }

    private List<List<VirtualKeyDef>> BuildRussianLayout()
    {
        return new List<List<VirtualKeyDef>>
        {
            // Row 0: Function & System
            new()
            {
                new("ESC", RETROK_ESCAPE, '\x1B', 27, 0, 1.2),
                new("F1", RETROK_F1, '\0', 0),
                new("F2", RETROK_F1 + 1, '\0', 0),
                new("F3", RETROK_F1 + 2, '\0', 0),
                new("F4", RETROK_F1 + 3, '\0', 0),
                new("F5", RETROK_F1 + 4, '\0', 0),
                new("F6", RETROK_F1 + 5, '\0', 0),
                new("F7", RETROK_F1 + 6, '\0', 0),
                new("F8", RETROK_F1 + 7, '\0', 0),
                new("F9", RETROK_F1 + 8, '\0', 0),
                new("F10", RETROK_F1 + 9, '\0', 0),
                new("BKSP", RETROK_BACKSPACE, '\b', 8, 0, 1.4)
            },
            // Row 1: Numbers & Ё
            new()
            {
                new(_shift ? "Ё" : "ё", 96, _shift ? 'Ё' : 'ё', (byte)(_shift ? 0xF0 : 0xF1)),
                new("1", 49, '1', (byte)'1'),
                new("2", 50, '2', (byte)'2'),
                new("3", 51, '3', (byte)'3'),
                new("4", 52, '4', (byte)'4'),
                new("5", 53, '5', (byte)'5'),
                new("6", 54, '6', (byte)'6'),
                new("7", 55, '7', (byte)'7'),
                new("8", 56, '8', (byte)'8'),
                new("9", 57, '9', (byte)'9'),
                new("0", 48, '0', (byte)'0'),
                new("-", 45, '-', (byte)'-'),
                new("=", 61, '=', (byte)'=')
            },
            // Row 2: ЙЦУКЕН Row 1
            new()
            {
                new("TAB", RETROK_TAB, '\t', 9, 0, 1.3),
                new(_shift ? "Й" : "й", 113, _shift ? 'Й' : 'й', (byte)(_shift ? 0x89 : 0xA9)),
                new(_shift ? "Ц" : "ц", 119, _shift ? 'Ц' : 'ц', (byte)(_shift ? 0x96 : 0xE6)),
                new(_shift ? "У" : "у", 101, _shift ? 'У' : 'у', (byte)(_shift ? 0x93 : 0xE3)),
                new(_shift ? "К" : "к", 114, _shift ? 'К' : 'к', (byte)(_shift ? 0x8A : 0xAA)),
                new(_shift ? "Е" : "е", 116, _shift ? 'Е' : 'е', (byte)(_shift ? 0x85 : 0xA5)),
                new(_shift ? "Н" : "н", 121, _shift ? 'Н' : 'н', (byte)(_shift ? 0x8D : 0xAD)),
                new(_shift ? "Г" : "г", 117, _shift ? 'Г' : 'г', (byte)(_shift ? 0x83 : 0xA3)),
                new(_shift ? "Ш" : "ш", 105, _shift ? 'Ш' : 'ш', (byte)(_shift ? 0x98 : 0xE8)),
                new(_shift ? "Щ" : "щ", 111, _shift ? 'Щ' : 'щ', (byte)(_shift ? 0x99 : 0xE9)),
                new(_shift ? "З" : "з", 112, _shift ? 'З' : 'з', (byte)(_shift ? 0x87 : 0xA7)),
                new(_shift ? "Х" : "х", 91, _shift ? 'Х' : 'х', (byte)(_shift ? 0x95 : 0xE5)),
                new(_shift ? "Ъ" : "ъ", 93, _shift ? 'Ъ' : 'ъ', (byte)(_shift ? 0x9A : 0xEA))
            },
            // Row 3: ЙЦУКЕН Row 2
            new()
            {
                new("ENG", 0, '\0', 0, 0, 1.3),
                new(_shift ? "Ф" : "ф", 97, _shift ? 'Ф' : 'ф', (byte)(_shift ? 0x94 : 0xE4)),
                new(_shift ? "Ы" : "ы", 115, _shift ? 'Ы' : 'ы', (byte)(_shift ? 0x9B : 0xEB)),
                new(_shift ? "В" : "в", 100, _shift ? 'В' : 'в', (byte)(_shift ? 0x82 : 0xA2)),
                new(_shift ? "А" : "а", 102, _shift ? 'А' : 'а', (byte)(_shift ? 0x80 : 0xA0)),
                new(_shift ? "П" : "п", 103, _shift ? 'П' : 'п', (byte)(_shift ? 0x8F : 0xAF)),
                new(_shift ? "Р" : "р", 104, _shift ? 'Р' : 'р', (byte)(_shift ? 0x8F : 0xE0)),
                new(_shift ? "О" : "о", 106, _shift ? 'О' : 'о', (byte)(_shift ? 0x8E : 0xAE)),
                new(_shift ? "Л" : "л", 107, _shift ? 'Л' : 'л', (byte)(_shift ? 0x8B : 0xAB)),
                new(_shift ? "Д" : "д", 108, _shift ? 'Д' : 'д', (byte)(_shift ? 0x84 : 0xA4)),
                new(_shift ? "Ж" : "ж", 59, _shift ? 'Ж' : 'ж', (byte)(_shift ? 0x86 : 0xA6)),
                new(_shift ? "Э" : "э", 39, _shift ? 'Э' : 'э', (byte)(_shift ? 0x9D : 0xED)),
                new("ENTER", RETROK_RETURN, '\r', 13, 0, 1.4)
            },
            // Row 4: ЙЦУКЕН Row 3
            new()
            {
                new("SHIFT", RETROK_LSHIFT, '\0', 0, 0, 1.4),
                new(_shift ? "Я" : "я", 122, _shift ? 'Я' : 'я', (byte)(_shift ? 0x9F : 0xEF)),
                new(_shift ? "Ч" : "ч", 120, _shift ? 'Ч' : 'ч', (byte)(_shift ? 0x97 : 0xE7)),
                new(_shift ? "С" : "с", 99, _shift ? 'С' : 'с', (byte)(_shift ? 0x91 : 0xE1)),
                new(_shift ? "М" : "м", 118, _shift ? 'М' : 'м', (byte)(_shift ? 0x8C : 0xAC)),
                new(_shift ? "И" : "и", 98, _shift ? 'И' : 'и', (byte)(_shift ? 0x88 : 0xA8)),
                new(_shift ? "Т" : "т", 110, _shift ? 'Т' : 'т', (byte)(_shift ? 0x92 : 0xE2)),
                new(_shift ? "Ь" : "ь", 109, _shift ? 'Ь' : 'ь', (byte)(_shift ? 0x9C : 0xEC)),
                new(_shift ? "Б" : "б", 44, _shift ? 'Б' : 'б', (byte)(_shift ? 0x81 : 0xA1)),
                new(_shift ? "Ю" : "ю", 46, _shift ? 'Ю' : 'ю', (byte)(_shift ? 0x9E : 0xEE)),
                new(".", 47, '.', (byte)'.'),
                new("ВВЕРХ", RETROK_UP, '\0', 0, 0, 1.1)
            },
            // Row 5: Bottom Navigation
            new()
            {
                new("CTRL", RETROK_LCTRL, '\0', 0, 0, 1.2),
                new("ALT", RETROK_LALT, '\0', 0, 0, 1.1),
                new("ПРОБЕЛ", RETROK_SPACE, ' ', 32, 0, 4.0),
                new("ВЛЕВО", RETROK_LEFT, '\0', 0),
                new("ВНИЗ", RETROK_DOWN, '\0', 0),
                new("ВПРАВО", RETROK_RIGHT, '\0', 0)
            }
        };
    }

    private List<List<VirtualKeyDef>> BuildFKeysLayout()
    {
        return new List<List<VirtualKeyDef>>
        {
            new()
            {
                new("F1", RETROK_F1, '\0', 0),
                new("F2", RETROK_F1 + 1, '\0', 0),
                new("F3", RETROK_F1 + 2, '\0', 0),
                new("F4", RETROK_F1 + 3, '\0', 0),
                new("F5", RETROK_F1 + 4, '\0', 0),
                new("F6", RETROK_F1 + 5, '\0', 0)
            },
            new()
            {
                new("F7", RETROK_F1 + 6, '\0', 0),
                new("F8", RETROK_F1 + 7, '\0', 0),
                new("F9", RETROK_F1 + 8, '\0', 0),
                new("F10", RETROK_F1 + 9, '\0', 0),
                new("F11", RETROK_F1 + 10, '\0', 0),
                new("F12", RETROK_F1 + 11, '\0', 0)
            },
            new()
            {
                new("7", 55, '7', (byte)'7'),
                new("8", 56, '8', (byte)'8'),
                new("9", 57, '9', (byte)'9'),
                new("/", 47, '/', (byte)'/'),
                new("*", 42, '*', (byte)'*'),
                new("BKSP", RETROK_BACKSPACE, '\b', 8)
            },
            new()
            {
                new("4", 52, '4', (byte)'4'),
                new("5", 53, '5', (byte)'5'),
                new("6", 54, '6', (byte)'6'),
                new("-", 45, '-', (byte)'-'),
                new("+", 43, '+', (byte)'+'),
                new("ENTER", RETROK_RETURN, '\r', 13)
            },
            new()
            {
                new("1", 49, '1', (byte)'1'),
                new("2", 50, '2', (byte)'2'),
                new("3", 51, '3', (byte)'3'),
                new("0", 48, '0', (byte)'0'),
                new(".", 46, '.', (byte)'.'),
                new("ESC", RETROK_ESCAPE, '\x1B', 27)
            }
        };
    }

    public void Move(int deltaRow, int deltaCol)
    {
        var rows = GetCurrentRows();
        if (rows.Count == 0) return;

        _currentRow = Math.Clamp(_currentRow + deltaRow, 0, rows.Count - 1);
        int colsInRow = rows[_currentRow].Count;
        if (colsInRow > 0)
        {
            _currentCol = Math.Clamp(_currentCol + deltaCol, 0, colsInRow - 1);
        }
    }

    public VirtualKeyDef? GetSelectedKey()
    {
        var rows = GetCurrentRows();
        if (_currentRow >= 0 && _currentRow < rows.Count)
        {
            var row = rows[_currentRow];
            if (_currentCol >= 0 && _currentCol < row.Count)
            {
                return row[_currentCol];
            }
        }
        return null;
    }

    /// <summary>
    /// Отправляет нажатие клавиши в хост Libretro.
    /// Поддерживает переключение языков (РУС ↔ ENG) и удержание Shift.
    /// </summary>
    public async Task SendKeyTapAsync(NativeLibretroHost host, VirtualKeyDef key, int durationMs = 60)
    {
        if (key.Label == "РУС")
        {
            _layout = VirtualKeyboardLayout.Russian;
            return;
        }
        if (key.Label == "ENG")
        {
            _layout = VirtualKeyboardLayout.English;
            return;
        }
        if (key.Label == "SHIFT")
        {
            _shift = !_shift;
            return;
        }

        ushort mods = key.Modifiers;
        if (_shift) mods |= 0x0001; // RETROKMOD_SHIFT

        host.InjectVirtualKeyEvent(true, key.RetroKey, key.UnicodeChar, mods);
        await Task.Delay(durationMs);
        host.InjectVirtualKeyEvent(false, key.RetroKey, key.UnicodeChar, mods);
    }
}
