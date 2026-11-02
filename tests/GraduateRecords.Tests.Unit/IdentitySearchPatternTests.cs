using GraduateRecords.Infrastructure.Identity;

namespace GraduateRecords.Tests.Unit;

/// <summary>
/// AGENTS §16: the SQL side of user-name search must fold the stored display
/// value exactly the way <c>PersianTextNormalizer.NormalizeSearchTerm</c> folds
/// the query input, so both sides of the comparison use one representation
/// without a normalized column (which would need a migration) and without
/// loading rows into memory.
/// </summary>
public sealed class IdentitySearchPatternTests
{
    /// <summary>ASCII + Persian (U+06F0–U+06F9) + Arabic-Indic (U+0660–U+0669) digits.</summary>
    private const string DigitClass = "[0-9۰-۹٠-٩]";

    [Fact]
    public void Persian_Yeh_in_the_term_also_matches_the_Arabic_variant_in_storage()
    {
        // Term is normalized to Persian Yeh (U+06CC); the class accepts U+06CC and U+064A.
        Assert.Equal("محمد[یي]", IdentitySearchPattern.Build("محمدی"));
    }

    [Fact]
    public void Persian_Kaf_in_the_term_also_matches_the_Arabic_variant_in_storage()
    {
        // "کتاب" starts with Persian Kaf (U+06A9); the class accepts U+06A9 and U+0643.
        Assert.Equal("[کك]تاب", IdentitySearchPattern.Build("کتاب"));
    }

    [Fact]
    public void Every_digit_form_in_storage_matches_one_ascii_digit_in_the_term()
    {
        Assert.Equal(DigitClass, IdentitySearchPattern.Build("7"));

        // Defensive: an un-normalized term produces the same class per digit.
        Assert.Equal(DigitClass + DigitClass, IdentitySearchPattern.Build("۱۴"));
    }

    [Fact]
    public void LIKE_metacharacters_in_the_term_are_escaped_to_literals()
    {
        Assert.Equal(
            DigitClass + DigitClass + DigitClass + "\\%\\_x",
            IdentitySearchPattern.Build("100%_x"));

        Assert.Equal("a\\[b\\]c", IdentitySearchPattern.Build("a[b]c"));
        Assert.Equal("\\\\", IdentitySearchPattern.Build("\\"));
    }

    [Fact]
    public void Ordinary_text_and_spaces_pass_through_unchanged()
    {
        // Only the §16 foldable characters become classes; everything else —
        // including spaces — is emitted literally.
        Assert.Equal("رضا", IdentitySearchPattern.Build("رضا"));
        Assert.Equal("abc x", IdentitySearchPattern.Build("abc x"));
    }

    [Fact]
    public void An_empty_term_produces_an_empty_pattern() =>
        Assert.Equal(string.Empty, IdentitySearchPattern.Build(string.Empty));
}
