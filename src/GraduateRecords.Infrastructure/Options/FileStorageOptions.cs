using System.ComponentModel.DataAnnotations;

namespace GraduateRecords.Infrastructure.Options;

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    [Required]
    [MinLength(1)]
    public string RootPath { get; set; } = string.Empty;

    public static bool IsSafeRoot(string rootPath, string webRootPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Path.IsPathFullyQualified(rootPath)) return false;
            var root = CanonicalPath(rootPath);
            var web = CanonicalPath(webRootPath);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return !root.Equals(web, comparison)
                && !root.StartsWith(Path.TrimEndingDirectorySeparator(web) + Path.DirectorySeparatorChar, comparison);
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static string CanonicalPath(string path)
    {
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        foreach (var segment in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var directory = new DirectoryInfo(current);
            // Resolve existing symlinks/junctions, including ancestors of a not-yet-created root.
            if (directory.LinkTarget is not null)
                current = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                    ?? throw new IOException("Unresolvable storage link.");
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
    }
}
