using System.ComponentModel.DataAnnotations;

namespace GraduateRecords.Infrastructure.Options;

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    [Required]
    [MinLength(1)]
    public string RootPath { get; set; } = string.Empty;
}
