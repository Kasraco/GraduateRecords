using System.Text;

namespace GraduateRecords.Domain.Common;

/// <summary>
/// AGENTS §16: the single implementation of Persian/Arabic normalization.
/// It applies to canonical/normalized data only (identifiers, digits, comparison
/// keys). Original display text (free-text answers, names, filenames) is never
/// normalized with this type.
/// </summary>
public static class PersianTextNormalizer
{
    private const char PersianZero = '\u06F0'; // ۰
    private const char ArabicIndicZero = '\u0660'; // ٠

    /// <summary>
    /// Converts Persian (U+06F0–U+06F9) and Arabic-Indic (U+0660–U+0669) digits to
    /// their ASCII equivalents. Every other character is preserved. Leading zeros
    /// are kept; the result is never interpreted numerically (ADR-0001 rule 5).
    /// </summary>
    public static string ToLatinDigits(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var buffer = new char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            buffer[i] = ConvertDigit(value[i]);
        }

        return new string(buffer);
    }

    private static char ConvertDigit(char c)
    {
        if (c is >= PersianZero and <= '۹') // ۰..۹
        {
            return (char)('0' + (c - PersianZero));
        }

        if (c is >= ArabicIndicZero and <= '٩') // ٠..٩
        {
            return (char)('0' + (c - ArabicIndicZero));
        }

        return c;
    }

    /// <summary>
    /// Canonical form of an identifier: Latin digits only, whitespace stripped.
    /// Used for identifiers such as the National ID where everything outside the
    /// expected character set is noise (AGENTS §16).
    /// </summary>
    public static string NormalizeIdentifier(string? value)
    {
        var latin = ToLatinDigits(value);
        if (string.IsNullOrEmpty(latin))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(latin.Length);
        foreach (var c in latin)
        {
            if (!char.IsWhiteSpace(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
