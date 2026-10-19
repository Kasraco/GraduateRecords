using System.Text.RegularExpressions;

namespace GraduateRecords.Tests.Architecture;

public sealed class SourceGuardTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Wwwroot_js_must_not_use_innerHTML_outerHTML_or_insertAdjacentHTML()
    {
        var jsRoot = Path.Combine(RepoRoot, "src", "GraduateRecords.Web", "wwwroot", "js");
        Assert.True(Directory.Exists(jsRoot), $"Missing JS root: {jsRoot}");

        var pattern = new Regex(
            @"\b(innerHTML|outerHTML|insertAdjacentHTML)\b",
            RegexOptions.CultureInvariant);

        var violations = Directory.EnumerateFiles(jsRoot, "*.js", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadAllLines(path)
                .Select((line, index) => (path, line, number: index + 1))
                .Where(x =>
                {
                    var trimmed = x.line.TrimStart();
                    // Ignore documentation comments that only name the forbidden APIs.
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)
                        || trimmed.StartsWith("*", StringComparison.Ordinal)
                        || trimmed.StartsWith("/*", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    return pattern.IsMatch(x.line);
                })
                .Select(x => $"{Relative(x.path)}:{x.number}: {x.line.Trim()}"))
            .ToList();

        Assert.True(violations.Count == 0, "Forbidden DOM HTML APIs:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void Views_and_TagHelpers_must_not_use_physical_direction_Tailwind_classes()
    {
        var roots = new[]
        {
            Path.Combine(RepoRoot, "src", "GraduateRecords.Web", "Views"),
            Path.Combine(RepoRoot, "src", "GraduateRecords.Web", "Areas"),
            Path.Combine(RepoRoot, "src", "GraduateRecords.Web", "UI"),
        }.Where(Directory.Exists);

        // Physical-direction utilities forbidden by AGENTS.md §21 / DESIGN.md.
        var pattern = new Regex(
            @"(?:^|[\s""'`])(?<bad>(?:ml|mr|pl|pr|left|right|text-left|text-right|float-left|float-right)-[a-zA-Z0-9\[\]/%.:-]+)",
            RegexOptions.CultureInvariant);

        var violations = new List<string>();
        foreach (var root in roots)
        {
            foreach (var path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                         .Where(static p => p.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase)
                                           || p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                var lines = File.ReadAllLines(path);
                for (var i = 0; i < lines.Length; i++)
                {
                    var match = pattern.Match(lines[i]);
                    if (match.Success)
                    {
                        violations.Add($"{Relative(path)}:{i + 1}: {match.Groups["bad"].Value}");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0, "Physical-direction Tailwind classes:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void Must_not_contain_tailwind_config_js()
    {
        var forbidden = Directory.EnumerateFiles(RepoRoot, "tailwind.config.js", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(Relative)
            .ToList();

        Assert.True(forbidden.Count == 0, "tailwind.config.js is forbidden (CSS-first Tailwind v4):\n" + string.Join('\n', forbidden));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GraduateRecords.sln"))
                && Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }

    private static string Relative(string path) => Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');
}
