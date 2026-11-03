using System.Text.RegularExpressions;

namespace GraduateRecords.Tests.Architecture;

/// <summary>
/// UC2.5 (ARCHITECTURE §10): "The handler gates the resource: ownership for
/// students, scope for staff." The scope decision primitives
/// (<c>ICurrentUser.StaffScope</c>, <c>ScopeExtensions.WithinScope/CanAccess</c>)
/// are Application-layer logic and must be called from handlers, never from
/// controllers, areas, tag helpers or views. Presentation code may only receive
/// already-decided display data through handler-produced DTOs — hidden UI is
/// never authorization (ADR-0004 §10.1; ADR-0017 §21), and a scope check that
/// lives in a controller can be skipped by any future action that forgets it.
/// </summary>
public sealed class ScopeDecisionGuardTests
{
    /// <summary>
    /// The scope decision vocabulary. Word-bounded so a DTO property such as
    /// <c>ScopeLabel</c> never matches.
    /// </summary>
    private static readonly Regex ScopeDecisionToken = new(
        @"\b(StaffScope|ScopeExtensions|WithinScope|CanAccess)\b",
        RegexOptions.CultureInvariant);

    private static readonly string[] PresentationFolders =
    [
        "Controllers",
        "Areas",
        "Views",
        "UI",
    ];

    [Fact]
    public void Scope_decisions_are_never_made_in_the_presentation_layer()
    {
        var repoRoot = FindRepoRoot();
        var webRoot = Path.Combine(repoRoot, "src", "GraduateRecords.Web");
        var violations = new List<string>();

        foreach (var folder in PresentationFolders)
        {
            var root = Path.Combine(webRoot, folder);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var path in Enumerate(root))
            {
                var lines = File.ReadAllLines(path);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (IsComment(lines[i]))
                    {
                        continue;
                    }

                    if (ScopeDecisionToken.IsMatch(lines[i]))
                    {
                        violations.Add($"{Relative(repoRoot, path)}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Staff-scope and ownership decisions belong in Application handlers, not in " +
            "controllers/views (ARCHITECTURE §10; ADR-0004 §10.1):\n" +
            string.Join('\n', violations));
    }

    private static IEnumerable<string> Enumerate(string root) =>
        Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static bool IsComment(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal)
            || trimmed.StartsWith("@*", StringComparison.Ordinal);
    }

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
