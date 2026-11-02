using System.Text.RegularExpressions;

namespace GraduateRecords.Tests.Architecture;

/// <summary>
/// UC3: every code path that grants or revokes a role must be the
/// <c>IIdentityAdministration</c> implementation, because that is the single
/// place holding the ADR-0017 §17–§19 transaction and Administrator-role row
/// lock. A new call site outside it would mutate membership without the
/// last-Administrator invariant (§17 is operation-agnostic, so any future
/// feature must go through the port instead of re-implementing the mutation).
/// </summary>
public sealed class IdentityRoleMutationGuardTests
{
    private const string AllowedFile = "src/GraduateRecords.Infrastructure/Identity/IdentityAdministration.cs";

    private static readonly Regex MutationCall = new(
        @"\.(AddToRoleAsync|RemoveFromRoleAsync)\s*\(",
        RegexOptions.CultureInvariant);

    [Fact]
    public void Role_membership_is_only_ever_mutated_by_the_identity_administration_port()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();

        foreach (var path in Enumerate(Path.Combine(repoRoot, "src"), "*.cs"))
        {
            if (string.Equals(Relative(repoRoot, path), AllowedFile, StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("*", StringComparison.Ordinal))
                {
                    continue;
                }

                if (MutationCall.IsMatch(lines[i]))
                {
                    violations.Add($"{Relative(repoRoot, path)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Role membership must only be mutated inside IIdentityAdministration " +
            "(ADR-0017 §17–§19 lock + precondition):\n" + string.Join('\n', violations));
    }

    private static IEnumerable<string> Enumerate(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static string Relative(string repoRoot, string path) =>
        Path.GetRelativePath(repoRoot, path).Replace('\\', '/');

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
