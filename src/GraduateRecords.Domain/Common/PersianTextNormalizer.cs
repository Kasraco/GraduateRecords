using System.Text;

namespace GraduateRecords.Domain.Common;

/// <summary>
/// AGENTS §16: the single implementation of Persian/Arabic normalization.
/// Two families live here, mirroring the two data classes of §16:
/// <list type="bullet">
/// <item><b>Canonical / comparison keys</b> (<see cref="ToLatinDigits"/>,
/// <see cref="NormalizeIdentifier"/>, <see cref="NormalizeSearchTerm"/>):
/// identifiers, digits and search keys, folded for comparison.</item>
/// <item><b>Display text</b> (<see cref="NormalizeDisplayText"/>): free-text
/// answers, names and filenames — preserved except for the sanitization §16
/// mandates (trim, line endings, NFC, control/bidi stripping, ZWNJ kept).</item>
/// </list>
/// No other type re-implements any of this (§16).
/// </summary>
public static class PersianTextNormalizer
{
    private const char PersianZero = '\u06F0'; // ۰
    private const char ArabicIndicZero = '\u0660'; // ٠
    // §16 comparison-key letter folds: Arabic forms are folded to Persian.
    private const char ArabicYeh = '\u064A';       // Arabic Yeh (U+064A)
    private const char PersianYeh = '\u06CC';      // Persian Yeh (U+06CC)
    private const char ArabicKaf = '\u0643';       // Arabic Kaf (U+0643)
    private const char PersianKaf = '\u06A9';      // Persian Kaf (U+06A9)

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

    /// <summary>
    /// Search-key normalization (AGENTS §16: "search keys … normalize at the
    /// boundary … Query input is normalized the same way"). The term is trimmed,
    /// its digits are folded to ASCII, Arabic <c>ي</c>/<c>ك</c> are folded to
    /// Persian <c>ی</c>/<c>ک</c>, and repeated whitespace collapses to a single
    /// space. ZWNJ (U+200C) is neither whitespace nor a folded character, so it
    /// is preserved. Only the query input is transformed here; the stored
    /// display value keeps its original characters and is folded at comparison
    /// time by the query itself (§16 keeps both representations).
    /// </summary>
    public static string NormalizeSearchTerm(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return CollapseWhitespace(FoldComparisonLetters(ToLatinDigits(value.Trim())));
    }

    /// <summary>
    /// Display-text normalization (AGENTS §16: "Preserve exactly what the
    /// user/source entered, except: trim ends, normalize line endings, Unicode
    /// NFC, strip control characters and bidi override/isolate characters
    /// (U+202A–202E, U+2066–2069). Keep ZWNJ (U+200C)"). Applied in that order:
    /// <c>\r\n</c> and <c>\r</c> become <c>\n</c>, the text is composed to NFC
    /// and trimmed, every other control character and the listed bidi controls
    /// are removed, and nothing else is rewritten. The normalized line ending
    /// survives because §16 treats it as content to normalize, not as noise —
    /// callers rendering a single-line field receive it as <c>\n</c>.
    /// </summary>
    public static string NormalizeDisplayText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var lineEndings = value.Replace("\r\n", "\n").Replace('\r', '\n');
        var composed = lineEndings.Normalize(NormalizationForm.FormC).Trim();

        var builder = new StringBuilder(composed.Length);
        foreach (var c in composed)
        {
            // '\n' is the normalized line ending (kept); every other control
            // character and the bidi override/isolate controls are stripped.
            if (c == '\n' || (!char.IsControl(c) && !IsBidiControl(c)))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>Arabic <c>ي</c>→<c>ی</c> and Arabic <c>ك</c>→<c>ک</c> (§16 comparison-key folds).</summary>
    private static string FoldComparisonLetters(string value)
    {
        if (value.IndexOf(ArabicYeh, StringComparison.Ordinal) < 0
            && value.IndexOf(ArabicKaf, StringComparison.Ordinal) < 0)
        {
            return value;
        }

        return value.Replace(ArabicYeh, PersianYeh).Replace(ArabicKaf, PersianKaf);
    }

    /// <summary>Collapses every whitespace run to one space and drops leading/trailing whitespace.</summary>
    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>Bidi override/isolate controls of §16: U+202A–U+202E and U+2066–U+2069.</summary>
    private static bool IsBidiControl(char c) =>
        c is >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069';
}
