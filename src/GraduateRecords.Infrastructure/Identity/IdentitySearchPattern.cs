using System.Text;

namespace GraduateRecords.Infrastructure.Identity;

/// <summary>
/// Builds the SQL <c>LIKE</c> pattern that compares a **normalized** search term
/// against the stored, un-normalized display name (AGENTS §16: "Searchable
/// fields keep BOTH: the original (display) and a normalized … representation.
/// Query input is normalized the same way").
/// <para>
/// <c>PersianTextNormalizer</c> folds the query input (digits → ASCII,
/// <c>ي</c>→<c>ی</c>, <c>ك</c>→<c>ک</c>, whitespace collapsed). The stored
/// column cannot be folded by that C# method inside SQL, and adding a normalized
/// column would require a migration (forbidden without an accepted ADR). So the
/// fold is expressed on the SQL side as LIKE character classes that accept
/// <b>either</b> script form of every foldable character — both sides then
/// compare the same folded representation without loading any row into memory.
/// </para>
/// <para>
/// Literal user input (<c>%</c>, <c>_</c>, <c>[</c>, <c>]</c>, <c>\</c>) is
/// escaped with <see cref="EscapeCharacter"/> so a search term can never become
/// a wildcard. Input must already be passed through
/// <c>PersianTextNormalizer.NormalizeSearchTerm</c>.
/// </para>
/// </summary>
public static class IdentitySearchPattern
{
    /// <summary>ESCAPE character paired with <c>EF.Functions.Like</c>.</summary>
    public const string EscapeCharacter = "\\";

    /// <summary>Builds the LIKE pattern for an already-normalized search term.</summary>
    public static string Build(string normalizedSearchTerm)
    {
        if (string.IsNullOrEmpty(normalizedSearchTerm))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(normalizedSearchTerm.Length * 2);
        foreach (var c in normalizedSearchTerm)
        {
            switch (c)
            {
                // Persian form accepted together with its Arabic counterpart.
                case 'ی': // Persian Yeh
                    builder.Append("[یي]");
                    break;

                case 'ک': // Persian Kaf
                    builder.Append("[کك]");
                    break;

                // ASCII, Persian and Arabic-Indic digits all compare equal.
                case >= '0' and <= '9':
                case >= '۰' and <= '۹':
                case >= '٠' and <= '٩':
                    builder.Append("[0-9۰-۹٠-٩]");
                    break;

                // LIKE metacharacters become literals.
                case '%':
                case '_':
                case '[':
                case ']':
                case '\\':
                    builder.Append('\\').Append(c);
                    break;

                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }
}
