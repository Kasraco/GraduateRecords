using GraduateRecords.Domain.Common;

namespace GraduateRecords.Tests.Unit;

public sealed class NationalIdTests
{
    // Computed with the official checksum: sum(d[i] * (10 - i)) % 11, remainder < 2 -> itself.
    private const string ValidId = "0012345679";

    [Theory]
    [InlineData(ValidId)]
    [InlineData("۰۰۱۲۳۴۵۶۷۹")] // Persian digits
    [InlineData("٠٠١٢٣٤٥٦٧٩")] // Arabic-Indic digits
    [InlineData(" 001 2345679 ")] // whitespace is stripped for identifiers
    public void Valid_national_ids_are_parsed_with_digits_preserved(string value)
    {
        Assert.True(NationalId.TryParse(value, out var nationalId));
        Assert.Equal(ValidId, nationalId.Value);
        Assert.Equal(10, nationalId.Value.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")] // too short
    [InlineData("00123456790")] // too long
    [InlineData("0012345678")] // wrong check digit
    [InlineData("abcdefghij")]
    public void Invalid_national_ids_are_rejected(string? value) =>
        Assert.False(NationalId.TryParse(value, out _));

    [Fact]
    public void All_identical_digits_are_rejected_even_when_the_checksum_matches()
    {
        // "1111111111" and "0000000000" both pass the arithmetic checksum but are never real IDs.
        Assert.False(NationalId.IsValid("1111111111"));
        Assert.False(NationalId.IsValid("0000000000"));
    }

    [Fact]
    public void Leading_zeros_are_kept_as_a_string() =>
        Assert.StartsWith("00", NationalId.Parse(ValidId).Value);

    [Fact]
    public void Parse_throws_for_invalid_input() =>
        Assert.Throws<FormatException>(() => NationalId.Parse("0012345678"));
}
