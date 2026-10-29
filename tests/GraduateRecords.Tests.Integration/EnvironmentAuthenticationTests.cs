using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GraduateRecords.Tests.Integration;

/// <summary>
/// Base factory for an environment where SSO configuration EXISTS (AGENTS §22:
/// a fake <c>HttpMessageHandler</c> IdP, never a real credential and never a
/// committed value). All settings are host settings so they are visible both
/// while Program.cs composes the schemes and to the lazy options binding.
/// </summary>
public abstract class SsoConfiguredWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>Strict-TLS connection string: valid in every environment.</summary>
    public const string StrictConnection =
        "Server=127.0.0.1,65530;Database=GraduateRecords_EnvTests;User ID=sa;Password=unused;" +
        "Encrypt=True;TrustServerCertificate=False;Connect Timeout=1;Command Timeout=1";

    public FakeOpenIdProvider FakeIdP { get; } = new();

    protected abstract string EnvironmentName { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);

        builder.ConfigureServices(services =>
            services.Configure<OpenIdConnectOptions>(
                OpenIdConnectDefaults.AuthenticationScheme,
                options => options.BackchannelHttpHandler = FakeIdP));

        builder.UseSetting("Identity:Oidc:Authority", FakeOpenIdProvider.Authority);
        builder.UseSetting("Identity:Oidc:ClientId", FakeOpenIdProvider.ClientId);
        builder.UseSetting("Identity:Oidc:ClientSecret", "integration-test-secret");
        builder.UseSetting("Identity:Oidc:UsePkce", "true");
        builder.UseSetting("ConnectionStrings:Default", StrictConnection);
        builder.UseSetting("FileStorage:RootPath", TestStorage.Root);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            FakeIdP.Dispose();
        }
    }
}

public sealed class ProductionWebApplicationFactory : SsoConfiguredWebApplicationFactory
{
    protected override string EnvironmentName => "Production";
}

public sealed class StagingWebApplicationFactory : SsoConfiguredWebApplicationFactory
{
    protected override string EnvironmentName => "Staging";
}

/// <summary>Development WITH real SSO configuration: normal SSO, no /dev/login.</summary>
public sealed class DevelopmentWithSsoWebApplicationFactory : SsoConfiguredWebApplicationFactory
{
    protected override string EnvironmentName => "Development";
}

/// <summary>
/// Production with NO SSO configuration: startup must fail fast (AGENTS §13
/// "Production and Staging MUST use the real university SSO configuration and
/// MUST remain fail-closed").
/// </summary>
public sealed class ProductionWithoutSsoWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Default", SsoConfiguredWebApplicationFactory.StrictConnection);
        builder.UseSetting("FileStorage:RootPath", TestStorage.Root);
        // Identity:Oidc is deliberately absent.
    }
}

/// <summary>
/// Shared behaviour for every environment where SSO configuration exists: the
/// root stays protected, the OIDC scheme is registered, and the development-only
/// login endpoint is not available.
/// </summary>
public abstract class SsoConfiguredEnvironmentTests<TFactory>(TFactory factory) : IClassFixture<TFactory>
    where TFactory : WebApplicationFactory<Program>, new()
{
    protected TFactory Factory { get; } = factory;

    protected static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    [Fact]
    public async Task The_root_page_is_protected_for_anonymous_requests()
    {
        using var client = CreateClient(Factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task The_oidc_scheme_is_registered_when_sso_configuration_exists()
    {
        var scheme = await Factory.Services
            .GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme);

        Assert.NotNull(scheme);
    }

    [Fact]
    public async Task The_development_login_endpoint_is_never_registered()
    {
        using var client = CreateClient(Factory);
        client.DefaultRequestHeaders.Add(TestClientIpStartupFilter.HeaderName, "127.0.0.1");

        using var response = await client.GetAsync("/dev/login/dev-test");

        // The endpoint does not exist: the authenticated-by-default fallback policy
        // answers unmatched routes with 401 (404 where routing rejects it first) —
        // never the development sign-in redirect and never a session cookie.
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound,
            $"Unexpected status {(int)response.StatusCode} for a route that must not exist.");
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }
}

[Collection(WebApplicationCollection.Name)]
public sealed class ProductionEnvironmentTests(ProductionWebApplicationFactory factory)
    : SsoConfiguredEnvironmentTests<ProductionWebApplicationFactory>(factory);

[Collection(WebApplicationCollection.Name)]
public sealed class StagingEnvironmentTests(StagingWebApplicationFactory factory)
    : SsoConfiguredEnvironmentTests<StagingWebApplicationFactory>(factory);

[Collection(WebApplicationCollection.Name)]
public sealed class DevelopmentWithSsoTests(DevelopmentWithSsoWebApplicationFactory factory)
    : SsoConfiguredEnvironmentTests<DevelopmentWithSsoWebApplicationFactory>(factory)
{
    [Fact]
    public async Task Login_renders_the_authentication_choice_without_challenging_sso()
    {
        using var client = CreateClient(Factory);
        using var response = await client.GetAsync("/Account/Login");

        // ADR-0015 §10 / AGENTS 13.5: the user explicitly chooses the path; the
        // page renders both options instead of issuing an implicit SSO challenge.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Location"), "The choice page must not redirect.");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ورود از طریق سازمان دانشگاه", body, StringComparison.Ordinal);
        Assert.Contains("ورود کارکنان و مدیران", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Requesting_sso_signin_challenges_the_university_provider()
    {
        using var client = CreateClient(Factory);
        using var response = await client.GetAsync("/Account/SsoSignIn");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(
            FakeOpenIdProvider.Authority + "/authorize",
            response.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }
}

[Collection(WebApplicationCollection.Name)]
public sealed class ProductionWithoutSsoStartupTests
{
    [Fact]
    public void Production_without_sso_configuration_fails_fast_at_startup()
    {
        using var factory = new ProductionWithoutSsoWebApplicationFactory();

        var thrown = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var validation = thrown.GetBaseException() as OptionsValidationException;

        Assert.NotNull(validation);
        Assert.Contains("Identity:Oidc", validation!.Message, StringComparison.Ordinal);
    }
}

[Collection(WebApplicationCollection.Name)]
public sealed class TestingEnvironmentLoginTests(GraduateRecordsWebApplicationFactory factory)
{
    [Fact]
    public async Task The_development_login_endpoint_is_not_registered_in_testing()
    {
        using var client = SsoTestFlow.CreateClient(factory);

        using var response = await client.GetAsync("/dev/login/dev-test");

        // No development sign-in in Testing: the route is absent (fallback policy
        // answers unmatched routes with 401) and no session cookie is issued.
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound,
            $"Unexpected status {(int)response.StatusCode} for a route that must not exist.");
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }
}
