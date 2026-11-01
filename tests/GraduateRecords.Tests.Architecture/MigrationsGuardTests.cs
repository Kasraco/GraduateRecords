using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GraduateRecords.Tests.Architecture;

/// <summary>
/// UC2.2 migration safety (AGENTS §17; UC2.2 plan §H): the three applied
/// migrations are immutable — pinned here by SHA-256 over line-ending-normalized
/// content, so the pin is byte-for-byte on every platform — and the UC2.2 role
/// migration must be purely additive: exactly three CreateTable and three
/// CreateIndex operations (with exactly the expected table and index names) in
/// Up(), exactly three DropTable operations in Down() and no other schema
/// operation in either, and it is ordered after <c>initialCatalog</c>.
/// </summary>
public sealed class MigrationsGuardTests
{
    private const string NewMigrationFileName = "20261101092531_AddIdentityRoleStore.cs";
    private const string InitialCatalogFileName = "20261029031806_initialCatalog.cs";

    private static readonly string MigrationsDirectory = Path.Combine(
        FindRepoRoot(), "src", "GraduateRecords.Infrastructure", "Persistence", "Migrations");

    /// <summary>
    /// SHA-256 of the LF-normalized content of every applied migration file
    /// (migration body + designer). AGENTS §17: never edit an applied
    /// migration — any content change fails this pin.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AppliedMigrationHashes =
        new Dictionary<string, string>
        {
            ["20261020202506_AddApplicationUserIdentity.cs"] =
                "1390674fd2e9ffbc2be8892744543fea21f1500c271897d636f95d766f571204",
            ["20261020202506_AddApplicationUserIdentity.Designer.cs"] =
                "3c4c09ebc633f9481e00de2bf8a317a8e62e24c76df2ef06fa12865d43e1d7d1",
            ["20261028204725_AddUserKindToApplicationUser.cs"] =
                "45062e2fe2214ee3eb25bc3755fe1ad29be80e0293390f611fcdbabd3bd7d15c",
            ["20261028204725_AddUserKindToApplicationUser.Designer.cs"] =
                "ad9481af471ba1e3ece738203b291ba9b21d8579dce350d158deadb782ffeb0e",
            ["20261029031806_initialCatalog.cs"] =
                "cc776b481a8c9c115bce9ebf05cd130bf9ad6f438271742aa2398898f5774062",
            ["20261029031806_initialCatalog.Designer.cs"] =
                "34c5d6a95d90d57bf5fc6cf012c5a3725f480f1728d1d5477827d124d909dfb4",
        };

    private static readonly string[] RoleStoreTables = ["AspNetRoleClaims", "AspNetRoles", "AspNetUserRoles"];

    /// <summary>The three indexes the UC2.2 migration must create (ordinal sort).</summary>
    private static readonly string[] RoleStoreIndexes =
        ["IX_AspNetRoleClaims_RoleId", "IX_AspNetUserRoles_RoleId", "RoleNameIndex"];

    [Fact]
    public void The_three_applied_migrations_are_byte_for_byte_untouched()
    {
        foreach (var (fileName, expectedHash) in AppliedMigrationHashes)
        {
            var path = Path.Combine(MigrationsDirectory, fileName);
            Assert.True(File.Exists(path), $"Applied migration file is missing: {fileName}");

            Assert.Equal(expectedHash, NormalizedSha256(File.ReadAllText(path)));
        }
    }

    [Fact]
    public void The_uc22_role_migration_is_purely_additive_and_ordered_after_initial_catalog()
    {
        var path = Path.Combine(MigrationsDirectory, NewMigrationFileName);
        Assert.True(File.Exists(path), $"UC2.2 migration is missing: {NewMigrationFileName}");

        var source = File.ReadAllText(path);
        var up = SliceFrom(source, "void Up", "void Down");
        var down = SliceFrom(source, "void Down", nextMarker: null);

        // Up: exactly the three role-store tables, nothing else created...
        Assert.Equal(RoleStoreTables, TableNamesIn(up, "CreateTable"));

        // ...exactly three CreateIndex operations with exactly the expected
        // index names...
        Assert.Equal(RoleStoreIndexes, IndexNamesIn(up));

        // ...and no schema operation other than CreateTable/CreateIndex. An
        // explicit operation allowlist (parsed structurally as
        // migrationBuilder.<Operation>( calls) is used instead of a fragile
        // list of forbidden substrings: any alter, drop, rename, raw SQL or
        // otherwise unexpected operation fails by name immediately.
        var upOperations = OperationsIn(up);
        Assert.Equal(6, upOperations.Count);
        Assert.Equal(3, upOperations.Count(operation => operation == "CreateTable"));
        Assert.Equal(3, upOperations.Count(operation => operation == "CreateIndex"));
        Assert.All(upOperations, operation => Assert.True(
            operation is "CreateTable" or "CreateIndex",
            $"The UC2.2 migration Up() contains unexpected schema operation '{operation}'."));

        // Down: exactly three operations, all DropTable...
        var downOperations = OperationsIn(down);
        Assert.Equal(3, downOperations.Count);
        Assert.All(downOperations, operation => Assert.Equal("DropTable", operation));

        // ...dropping only the newly introduced role-store schema.
        Assert.Equal(RoleStoreTables, TableNamesIn(down, "DropTable"));

        // Ordering: every migration id is a 14-digit timestamp prefix; the new
        // migration must be the newest one, i.e. sorted after initialCatalog.
        var migrationIds = Directory.EnumerateFiles(MigrationsDirectory, "*.cs")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null && Regex.IsMatch(name, @"^\d{14}_"))
            .Select(name => name![..14])
            .ToList();
        Assert.Equal(NewMigrationFileName[..14], migrationIds.Max(StringComparer.Ordinal));
        Assert.True(
            string.CompareOrdinal(NewMigrationFileName[..14], InitialCatalogFileName[..14]) > 0,
            "The UC2.2 migration must be ordered after the applied initialCatalog migration.");
    }

    /// <summary>The source slice from <paramref name="fromMarker"/> up to <paramref name="nextMarker"/> (or the end).</summary>
    private static string SliceFrom(string source, string fromMarker, string? nextMarker)
    {
        var start = source.IndexOf(fromMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected '{fromMarker}' in the migration source.");

        var end = nextMarker is null
            ? source.Length
            : source.IndexOf(nextMarker, start + fromMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Expected '{nextMarker}' in the migration source.");

        return source[start..end];
    }

    /// <summary>Table names passed to <paramref name="operation"/> in the given slice, sorted.</summary>
    private static List<string> TableNamesIn(string slice, string operation) =>
        Regex.Matches(slice, Regex.Escape(operation) + @"\(\s*name:\s*""([^""]+)""")
            .Select(match => match.Groups[1].Value)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Method name of every <c>migrationBuilder</c> schema operation invoked in
    /// the given slice, in source order — the structural basis for the
    /// operation allowlist assertions.
    /// </summary>
    private static List<string> OperationsIn(string slice) =>
        Regex.Matches(slice, @"migrationBuilder\.(\w+)\(")
            .Select(match => match.Groups[1].Value)
            .ToList();

    /// <summary>Names passed to <c>CreateIndex(name: …)</c> in the given slice, sorted.</summary>
    private static List<string> IndexNamesIn(string slice) =>
        Regex.Matches(slice, @"migrationBuilder\.CreateIndex\(\s*name:\s*""([^""]+)""")
            .Select(match => match.Groups[1].Value)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>SHA-256 over UTF-8 bytes of the content with CRLF/CR folded to LF.</summary>
    private static string NormalizedSha256(string content)
    {
        var normalized = Regex.Replace(content, "\r\n|\r", "\n");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GraduateRecords.sln")) &&
                Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }
}
