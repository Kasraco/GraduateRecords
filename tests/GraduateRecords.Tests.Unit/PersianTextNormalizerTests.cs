using GraduateRecords.Domain.Common;

namespace GraduateRecords.Tests.Unit;

public sealed class PersianTextNormalizerTests
{
    [Theory]
    [InlineData("۱۲۳۴۵", "12345")]
    [InlineData("٠٩", "09")]
    [InlineData("abc۱۲", "abc12")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Digits_are_converted_but_nothing_else_changes(string? input, string expected) =>
        Assert.Equal(expected, PersianTextNormalizer.ToLatinDigits(input));

    [Fact]
    public void Arabic_and_Persian_letters_are_not_rewritten_for_comparison_keys()
    {
        // AGENTS §16: digit/identifier normalization never rewrites letters (display text is separate).
        Assert.Equal("يك", PersianTextNormalizer.ToLatinDigits("يك"));
    }

    [Theory]
    [InlineData(" 001 2345679 ", "0012345679")]
    [InlineData("۰۰۱ ۲۳۴۵۶۷۹", "0012345679")]
    [InlineData("0012345679", "0012345679")]
    public void Identifier_normalization_strips_whitespace_and_normalizes_digits(string input, string expected) =>
        Assert.Equal(expected, PersianTextNormalizer.NormalizeIdentifier(input));
}
