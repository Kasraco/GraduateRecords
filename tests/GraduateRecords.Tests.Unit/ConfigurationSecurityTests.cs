using GraduateRecords.Infrastructure;
using GraduateRecords.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GraduateRecords.Tests.Unit;

public sealed class ConfigurationSecurityTests
{
    [Theory]
    [InlineData("Encrypt=False;TrustServerCertificate=False")]
    [InlineData("Encrypt=Optional;TrustServerCertificate=False")]
    [InlineData("Encrypt=True;TrustServerCertificate=True")]
    [InlineData("Encrypt=Strict;Trust Server Certificate=yes")]
    [InlineData("not a connection string")]
    [InlineData("")]
    public void Unsafe_production_database_configuration_is_rejected_without_leaking_values(string settings)
    {
        var value = settings.Length == 0
            ? string.Empty
            : "Server=db;Database=records;Password=secret;" + settings;

        Assert.False(DatabaseOptions.IsValid(value, "Production"));
    }

    [Theory]
    [InlineData("True")]
    [InlineData("Mandatory")]
    [InlineData("Strict")]
    public void Encrypted_verified_database_configuration_is_valid(string encryption) =>
        Assert.True(DatabaseOptions.IsValid(
            $"Server=db;Database=records;Encrypt={encryption};TrustServerCertificate=False",
            "Production"));

    [Fact]
    public void Development_certificate_exception_does_not_apply_to_production()
    {
        const string value = "Server=db;Database=records;Encrypt=True;TrustServerCertificate=True";
        Assert.True(DatabaseOptions.IsValid(value, "Development"));
        Assert.True(DatabaseOptions.IsValid(value, "Testing"));
        Assert.False(DatabaseOptions.IsValid(value, "Production"));
    }

    [Fact]
    public void Storage_rejects_relative_webroot_and_traversal_paths_but_accepts_a_sibling()
    {
        var root = Path.Combine(Path.GetTempPath(), "GraduateRecordsPathTests");
        var web = Path.Combine(root, "wwwroot");

        foreach (var path in new[]
                 {
                     string.Empty,
                     "files",
                     web,
                     Path.Combine(web, "uploads"),
                     Path.Combine(root, "files", "..", "wwwroot", "uploads"),
                 })
        {
            Assert.False(FileStorageOptions.IsSafeRoot(path, web), path);
        }

        Assert.True(FileStorageOptions.IsSafeRoot(Path.Combine(root, "files"), web));
        Assert.True(FileStorageOptions.IsSafeRoot(Path.Combine(root, "wwwroot-private"), web));

        if (OperatingSystem.IsWindows())
        {
            Assert.False(FileStorageOptions.IsSafeRoot(@"\files", web));
            Assert.False(FileStorageOptions.IsSafeRoot(@"C:files", web));
            Assert.False(FileStorageOptions.IsSafeRoot(web.ToUpperInvariant(), web));
        }
    }

    [Fact]
    public void Unsafe_configuration_fails_at_startup_without_leaking_the_connection_string()
    {
        var root = Path.Combine(Path.GetTempPath(), "GraduateRecordsPathTests");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=db;Database=records;Encrypt=False;Password=secret",
                ["FileStorage:RootPath"] = Path.Combine(root, "files"),
            }).Build(),
            "Production",
            Path.Combine(root, "wwwroot"));

        using var provider = services.BuildServiceProvider();
        var error = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);
        Assert.DoesNotContain("secret", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
