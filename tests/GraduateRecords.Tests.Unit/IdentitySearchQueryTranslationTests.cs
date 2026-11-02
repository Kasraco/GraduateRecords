using GraduateRecords.Infrastructure.Identity;
using GraduateRecords.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GraduateRecords.Tests.Unit;

/// <summary>
/// AGENTS §17: the user-name search must stay a *database-side* predicate —
/// normalizing both sides of the comparison must never degrade into loading
/// rows into memory. <c>ToQueryString()</c> forces EF to translate the LINQ
/// expression to SQL without opening a connection, so a construct EF cannot
/// translate fails here instead of at runtime in production.
/// </summary>
public sealed class IdentitySearchQueryTranslationTests
{
    private const string DummyConnectionString =
        "Server=translation-check;Database=translation-check;TrustServerCertificate=True;Connect Timeout=1";

    [Fact]
    public void The_user_name_search_translates_to_a_database_LIKE_predicate()
    {
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(DummyConnectionString).Options);

        var pattern = IdentitySearchPattern.Build("محمدی");
        var sql = context.Users
            .Where(user => EF.Functions.Like(
                               user.FullName.Replace("  ", " ").Replace("  ", " "),
                               pattern,
                               IdentitySearchPattern.EscapeCharacter)
                           || user.NationalId == "4000000004")
            .ToQueryString();

        // Parameterized (a bound @pattern variable, never the raw term inlined),
        // LIKE + ESCAPE on the SQL side, and the stored-side whitespace fold.
        Assert.Contains("LIKE @", sql, StringComparison.Ordinal);
        Assert.Contains("ESCAPE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REPLACE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Text.Encoding", sql, StringComparison.Ordinal); // no client-side fallback
    }
}
