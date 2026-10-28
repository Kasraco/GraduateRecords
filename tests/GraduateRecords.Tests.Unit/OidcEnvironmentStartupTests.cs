using GraduateRecords.Infrastructure;
using GraduateRecords.Infrastructure.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GraduateRecords.Tests.Unit;

/// <summary>
/// AGENTS §13 "Development SSO / Authentication Rule": SSO configuration is
/// optional only in the Development environment; Production, Staging and Testing
/// are fail-closed at startup; the OIDC scheme exists exactly when real
/// configuration exists (no placeholder credentials anywhere).
/// </summary>
public sealed class OidcEnvironmentStartupTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "GraduateRecordsOidcStartupTests");

    private static Dictionary<string, string?> SecurityValues() => new()
    {
        ["ConnectionStrings:Default"] =
            "Server=db;Database=GraduateRecords;Encrypt=True;TrustServerCertificate=False",
        ["FileStorage:RootPath"] = Path.Combine(Root, "files"),
        ["Identity:Lockout:MaxFailedAccessAttempts"] = "5",
        ["Identity:Lockout:LockoutDurationMinutes"] = "15",
        ["Identity:Lockout:AllowedForNewUsers"] = "true",
        ["Identity:Password:RequiredLength"] = "14",
        ["Identity:CookieLifetime:IdleMinutes"] = "30",
        ["Identity:CookieLifetime:AbsoluteHours"] = "8",
        ["Identity:LoginRateLimit:PermitsPerMinute"] = "10",
    };

    private static IConfiguration Configuration(bool withSso)
    {
        var values = SecurityValues();

        if (withSso)
        {
            values["Identity:Oidc:Authority"] = "https://idp.university.test";
            values["Identity:Oidc:ClientId"] = "graduate-records";
            values["Identity:Oidc:ClientSecret"] = "from-secret-configuration";
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ServiceProvider Build(string environment, bool withSso) =>
        Build(Configuration(withSso), environment);

    private static ServiceProvider Build(IConfiguration configuration, string environment)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration, environment, Path.Combine(Root, "wwwroot"));
        services.AddIdentityAuthentication(configuration, environment);
        return services.BuildServiceProvider();
    }

    private static IdentitySecuritySettings Settings(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<IdentitySecuritySettings>>().Value;

    [Fact]
    public async Task Development_starts_without_sso_configuration_and_without_an_oidc_scheme()
    {
        using var provider = Build(Environments.Development, withSso: false);

        // No OptionsValidationException: startup succeeds with every SSO value absent.
        Assert.False(Settings(provider).IsOidcConfigured);

        var scheme = await provider.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme);
        Assert.Null(scheme);
    }

    [Fact]
    public async Task Development_with_real_sso_configuration_registers_the_oidc_scheme()
    {
        using var provider = Build(Environments.Development, withSso: true);

        Assert.True(Settings(provider).IsOidcConfigured);

        var scheme = await provider.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme);
        Assert.NotNull(scheme);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task Missing_sso_configuration_fails_fast_outside_development(string environment)
    {
        using var provider = Build(environment, withSso: false);

        var error = Assert.Throws<OptionsValidationException>(() => _ = Settings(provider));
        Assert.Contains("Identity:Oidc", error.Message, StringComparison.Ordinal);

        var scheme = await provider.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme);
        Assert.Null(scheme);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Valid_sso_configuration_is_accepted_and_registers_the_oidc_scheme(string environment)
    {
        using var provider = Build(environment, withSso: true);

        Assert.True(Settings(provider).IsOidcConfigured);

        var scheme = await provider.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme);
        Assert.NotNull(scheme);
    }

    [Fact]
    public void A_partially_configured_sso_section_fails_production_startup_without_leaking_configuration_values()
    {
        var values = SecurityValues();
        values["Identity:Oidc:Authority"] = "https://partial.university.test"; // ClientId/ClientSecret missing

        using var provider = Build(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            "Production");

        var error = Assert.Throws<OptionsValidationException>(() => _ = Settings(provider));

        Assert.Contains("Identity:Oidc", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("partial.university.test", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
