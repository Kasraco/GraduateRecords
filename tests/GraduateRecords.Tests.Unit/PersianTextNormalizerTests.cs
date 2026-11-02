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

    // --- AGENTS §16: search-key normalization (query input) ---

    [Theory]
    [InlineData("  علي   رضا  ", "علی رضا")] // trim + whitespace collapse + Arabic ي→ی
    [InlineData("محمدي", "محمدی")] // Arabic Yeh folded to Persian Yeh
    [InlineData("كتاب درسی", "کتاب درسی")] // Arabic Kaf folded to Persian Kaf
    [InlineData("سال ۱۴۰۳", "سال 1403")] // Persian digits folded to ASCII
    [InlineData("سال ١٤٠٣", "سال 1403")] // Arabic-Indic digits folded to ASCII
    [InlineData("\t علی \n رضا \t", "علی رضا")] // whitespace runs (any kind) collapse to one space
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Search_terms_are_normalized_for_comparison(string? input, string expected) =>
        Assert.Equal(expected, PersianTextNormalizer.NormalizeSearchTerm(input));

    [Fact]
    public void Search_term_normalization_preserves_ZWNJ()
    {
        // ZWNJ (U+200C) is neither whitespace nor a folded character (§16 keeps it).
        const string withZwnj = "کار\u200Cشناسی";

        Assert.Equal(withZwnj, PersianTextNormalizer.NormalizeSearchTerm(withZwnj));
    }

    [Fact]
    public void Search_term_normalization_does_not_change_identifier_normalization_behaviour()
    {
        // The letter fold is scoped to search keys: identifier rules stay digit-only.
        Assert.Equal("يك", PersianTextNormalizer.ToLatinDigits("يك"));
    }

    // --- AGENTS §16: display-text normalization ---

    [Theory]
    [InlineData("  علی  ", "علی")] // trim ends only
    [InlineData("الف\r\nب\rج", "الف\nب\nج")] // line endings normalized to \n
    [InlineData("الف\u0007ب", "الفب")] // other control characters stripped
    [InlineData("الف\tب", "الفب")] // tab is a control character, stripped
    [InlineData("الف\u202Eب", "الفب")] // bidi override (RTL mark) stripped
    [InlineData("\u2066الف\u2069", "الف")] // bidi isolate stripped
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Display_text_is_trimmed_and_sanitized(string? input, string expected) =>
        Assert.Equal(expected, PersianTextNormalizer.NormalizeDisplayText(input));

    [Fact]
    public void Display_text_is_composed_to_NFC()
    {
        // "e" + combining acute accent composes to "é" (U+00E9).
        Assert.Equal("\u00E9", PersianTextNormalizer.NormalizeDisplayText("e\u0301"));
    }

    [Fact]
    public void Display_text_normalization_preserves_ZWNJ_and_unrelated_content()
    {
        const string withZwnj = "کار\u200Cشناسی";

        Assert.Equal(withZwnj, PersianTextNormalizer.NormalizeDisplayText(withZwnj));
        Assert.Equal("علی رضا", PersianTextNormalizer.NormalizeDisplayText("علی رضا"));
    }
}
