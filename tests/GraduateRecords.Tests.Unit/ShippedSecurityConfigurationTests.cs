using System.Text.Json;
using GraduateRecords.Infrastructure.Options;

namespace GraduateRecords.Tests.Unit;

/// <summary>
/// Binds the owner-decided security values (lockout 5/15, password 14, cookie 30 min
/// idle + 8 h absolute, login rate limit 10/min) to the shipped configuration file,
/// so a later config edit that weakens them fails the build. Secrets are asserted to
/// be absent (ADR-0001 rule 17).
/// </summary>
public sealed class ShippedSecurityConfigurationTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void The_shipped_configuration_carries_the_decided_security_values()
    {
        var path = Path.Combine(RepoRoot, "src", "GraduateRecords.Web", "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var identity = document.RootElement.GetProperty("Identity");

        Assert.Equal("5", identity.GetProperty("Lockout").GetProperty("MaxFailedAccessAttempts").GetRawText());
        Assert.Equal("15", identity.GetProperty("Lockout").GetProperty("LockoutDurationMinutes").GetRawText());
        Assert.Equal("14", identity.GetProperty("Password").GetProperty("RequiredLength").GetRawText());
        Assert.Equal("30", identity.GetProperty("CookieLifetime").GetProperty("IdleMinutes").GetRawText());
        Assert.Equal("8", identity.GetProperty("CookieLifetime").GetProperty("AbsoluteHours").GetRawText());
        Assert.Equal("10", identity.GetProperty("LoginRateLimit").GetProperty("PermitsPerMinute").GetRawText());
    }

    [Fact]
    public void No_secret_or_environment_specific_sso_value_is_committed()
    {
        var path = Path.Combine(RepoRoot, "src", "GraduateRecords.Web", "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var oidc = document.RootElement.GetProperty("Identity").GetProperty("Oidc");

        Assert.Equal(string.Empty, oidc.GetProperty("ClientSecret").GetString());
        Assert.Equal(string.Empty, oidc.GetProperty("Authority").GetString()); // supplied per environment
        Assert.True(oidc.GetProperty("UsePkce").GetBoolean());
    }

    [Fact]
    public void The_decided_values_form_a_valid_identity_configuration()
    {
        var settings = new IdentitySecuritySettings
        {
            Lockout = new() { MaxFailedAccessAttempts = 5, LockoutDurationMinutes = 15, AllowedForNewUsers = true },
            Password = new() { RequiredLength = 14 },
            CookieLifetime = new() { IdleMinutes = 30, AbsoluteHours = 8 },
            LoginRateLimit = new() { PermitsPerMinute = 10 },
            Oidc = new()
            {
                Authority = "https://idp.ajums.ac.ir",
                ClientId = "graduate-records",
                ClientSecret = "supplied-by-the-environment",
                UsePkce = true,
            },
        };

        Assert.True(settings.IsValid(), IdentitySecuritySettings.ValidationMessage);
        Assert.Equal(TimeSpan.FromMinutes(30), settings.IdleSessionLifetime);
        Assert.Equal(TimeSpan.FromHours(8), settings.AbsoluteSessionCap);
        Assert.Equal(TimeSpan.FromMinutes(1), settings.LoginRateLimit.Window);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GraduateRecords.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
