using System;
using System.Text.RegularExpressions;

namespace StormRetroArch;

/// <summary>
/// Обеспечивает экранную микротипографику русского языка.
/// Предотвращает висячие 1-2 буквенные предлоги/союзы и висячие тире в конце строк.
/// </summary>
public static class TypographyShield
{
    private static readonly Regex PrepositionRegex = new(
        @"(?<=(^|[\s\(\[\""«]))(в|во|на|с|со|к|ко|по|из|от|до|за|у|о|об|не|ни|и|а|но)\s+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex EmDashRegex = new(
        @"\s+-",
        RegexOptions.Compiled);

    /// <summary>
    /// Связывает неразрывным пробелом (\u00A0) короткие предлоги со следующими словами и тире с предшествующим словом.
    /// </summary>
    public static string ApplyRussianMicroTypography(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;

        // Связываем 1-2 буквенные предлоги, союзы и частицы со следующим словом
        string result = PrepositionRegex.Replace(text, "$2\u00A0");

        // Привязываем длинное тире к предыдущему слову неразрывным пробелом
        result = EmDashRegex.Replace(result, "\u00A0-");

        return result;
    }
}
