using System.Text.RegularExpressions;

namespace GraduateRecords.Tests.Architecture;

public sealed class SourceGuardTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string WebRoot = Path.Combine(RepoRoot, "src", "GraduateRecords.Web");

    [Fact]
    public void Wwwroot_js_must_not_use_unsafe_HTML_string_APIs()
    {
        var jsRoot = Path.Combine(WebRoot, "wwwroot", "js");
        Assert.True(Directory.Exists(jsRoot), $"Missing JS root: {jsRoot}");

        var pattern = new Regex(
            @"\b(innerHTML|outerHTML|insertAdjacentHTML|document\.write|setHTML)\b",
            RegexOptions.CultureInvariant);

        var violations = Enumerate(jsRoot, "*.js")
            .SelectMany(path => File.ReadAllLines(path)
                .Select((line, index) => (path, line, number: index + 1))
                .Where(x => !IsComment(x.line) && pattern.IsMatch(x.line))
                .Select(x => $"{Relative(x.path)}:{x.number}: {x.line.Trim()}"))
            .ToList();

        Assert.True(violations.Count == 0,
            "Forbidden DOM HTML string APIs (AGENTS §18):\n" + string.Join('\n', violations));
    }

    [Fact]
    public void Views_and_TagHelpers_must_not_render_dynamic_data_with_Html_Raw()
    {
        var roots = new[]
        {
            Path.Combine(WebRoot, "Views"),
            Path.Combine(WebRoot, "Areas"),
            Path.Combine(WebRoot, "UI"),
        }.Where(Directory.Exists);

        var pattern = new Regex(@"\bHtml\.Raw\s*\(", RegexOptions.CultureInvariant);
        var violations = new List<string>();

        foreach (var root in roots)
        {
            foreach (var path in Enumerate(root, "*.*")
                         .Where(static p => p.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase)
                                            || p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                var lines = File.ReadAllLines(path);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (!IsComment(lines[i]) && pattern.IsMatch(lines[i]))
                    {
                        violations.Add($"{Relative(path)}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Html.Raw on non-constant data is forbidden (AGENTS §18):\n" + string.Join('\n', violations));
    }

    [Theory]
    [InlineData("ml-4")]
    [InlineData("mr-auto")]
    [InlineData("pl-2")]
    [InlineData("pr-6")]
    [InlineData("left-0")]
    [InlineData("right-1/2")]
    [InlineData("text-left")]
    [InlineData("text-right")]
    [InlineData("float-left")]
    [InlineData("float-right")]
    [InlineData("md:ml-4")]
    [InlineData("lg:mr-2")]
    [InlineData("hover:pl-3")]
    [InlineData("focus:pr-1")]
    [InlineData("sm:hover:left-0")]
    [InlineData("dark:text-right")]
    public void Physical_direction_Tailwind_utility_is_detected(string className) =>
        Assert.Matches(PhysicalDirectionClassPattern(), $"class=\"gap-2 {className} flex\"");

    [Theory]
    [InlineData("ms-4")]
    [InlineData("me-auto")]
    [InlineData("ps-2")]
    [InlineData("pe-6")]
    [InlineData("start-0")]
    [InlineData("end-1/2")]
    [InlineData("text-start")]
    [InlineData("text-end")]
    [InlineData("float-start")]
    [InlineData("border-s")]
    [InlineData("rounded-e-lg")]
    [InlineData("md:ms-4")]
    [InlineData("hover:text-end")]
    public void Logical_direction_Tailwind_utility_is_allowed(string className) =>
        Assert.DoesNotMatch(PhysicalDirectionClassPattern(), $"class=\"gap-2 {className} flex\"");

    [Fact]
    public void Views_and_TagHelpers_must_not_use_physical_direction_Tailwind_classes()
    {
        var roots = new[]
        {
            Path.Combine(WebRoot, "Views"),
            Path.Combine(WebRoot, "Areas"),
            Path.Combine(WebRoot, "UI"),
        }.Where(Directory.Exists);

        var pattern = PhysicalDirectionClassPattern();
        var violations = new List<string>();

        foreach (var root in roots)
        {
            foreach (var path in Enumerate(root, "*.*")
                         .Where(static p => p.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase)
                                            || p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                var lines = File.ReadAllLines(path);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (!IsComment(lines[i]) && pattern.IsMatch(lines[i]))
                    {
                        violations.Add($"{Relative(path)}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Physical-direction Tailwind classes (AGENTS §21 / DESIGN §11):\n" + string.Join('\n', violations));
    }

    [Fact]
    public void Must_not_contain_tailwind_config_js()
    {
        var forbidden = Directory.EnumerateFiles(RepoRoot, "tailwind.config.js", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(Relative)
            .ToList();

        Assert.True(forbidden.Count == 0,
            "tailwind.config.js is forbidden (CSS-first Tailwind v4):\n" + string.Join('\n', forbidden));
    }

    /// <summary>
    /// Matches a physical-direction utility after any number of variant prefixes
    /// (sm:, md:, lg:, hover:, focus:, dark:, stacked variants, ...). Logical
    /// utilities (ms/me/ps/pe/start/end/text-start/text-end/float-start/...) never match.
    /// </summary>
    private static Regex PhysicalDirectionClassPattern() => new(
        @"(?:^|[\s""'`])(?:[a-z0-9-]+:)*(?<bad>(?:text-left|text-right|float-left|float-right|ml|mr|pl|pr|left|right)(?:-[^\s""'`]*)?)(?=[\s""'`]|$)",
        RegexOptions.CultureInvariant);

    private static bool IsComment(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal)
            || trimmed.StartsWith("@*", StringComparison.Ordinal);
    }

    private static IEnumerable<string> Enumerate(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

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
