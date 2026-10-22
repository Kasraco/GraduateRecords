namespace GraduateRecords.Domain.Common;

/// <summary>
/// Iranian National ID value object (AGENTS §16, ADR-0001 rule 5):
/// exactly 10 ASCII digits, not all digits identical, valid checksum.
/// Always stored and compared as a string so leading zeros survive.
/// </summary>
public readonly record struct NationalId
{
    public const int Length = 10;

    private NationalId(string value) => Value = value;

    /// <summary>The 10-digit ASCII representation. Never numeric.</summary>
    public string Value { get; }

    public static NationalId Parse(string value)
    {
        if (!TryParse(value, out var nationalId))
        {
            throw new FormatException("The value is not a valid Iranian National ID.");
        }

        return nationalId;
    }

    /// <summary>
    /// Normalizes Persian/Arabic digits and whitespace, then validates length,
    /// non-repetition and the checksum. Never throws.
    /// </summary>
    public static bool TryParse(string? value, out NationalId nationalId)
    {
        nationalId = default;

        var digits = PersianTextNormalizer.NormalizeIdentifier(value);
        if (digits.Length != Length)
        {
            return false;
        }

        if (AllDigitsIdentical(digits))
        {
            return false; // e.g. 0000000000 or 1111111111 pass the checksum but are never real.
        }

        if (!HasValidChecksum(digits))
        {
            return false;
        }

        nationalId = new NationalId(digits);
        return true;
    }

    public static bool IsValid(string? value) => TryParse(value, out _);

    public override string ToString() => Value;

    private static bool AllDigitsIdentical(string digits)
    {
        for (var i = 1; i < digits.Length; i++)
        {
            if (digits[i] != digits[0])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Standard checksum: sum of digit[i] * (10 - i) for i in 0..8, modulo 11.
    /// Values below 2 map to themselves, otherwise the check digit is 11 - remainder.
    /// </summary>
    private static bool HasValidChecksum(string digits)
    {
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (digits[i] - '0') * (10 - i);
        }

        var remainder = sum % 11;
        var expected = remainder < 2 ? remainder : 11 - remainder;
        return expected == digits[9] - '0';
    }
}
