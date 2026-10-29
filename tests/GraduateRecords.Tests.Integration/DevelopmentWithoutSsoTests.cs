using System.Net;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Domain.Auditing;
using GraduateRecords.Infrastructure.Identity;
using GraduateRecords.Infrastructure.Options;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Serilog.Core;
using Serilog.Events;

namespace GraduateRecords.Tests.Integration;

/// <summary>
/// Development environment with NO university SSO configuration — the exact
/// scenario of AGENTS §13 "Development SSO / Authentication Rule". The host must
/// start, the OIDC scheme must not exist, <c>/</c> must stay protected, the
/// login page must render the authentication choice, and an eligible
/// Staff/Administrator must be able to sign in through the formal local
/// credential path (ADR-0015 §10/§13/§14).
/// </summary>
public sealed class DevelopmentWithoutSsoWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestConnection =
        "Server=127.0.0.1,65530;Database=GraduateRecords_DevTests;User ID=sa;Password=unused;" +
        "TrustServerCertificate=True;Encrypt=False;Connect Timeout=1;Command Timeout=1";

    public CapturingLogSink Logs { get; } = new();
    public InMemoryUserStore Users { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IUserStore<ApplicationUser>>();
            services.AddSingleton(Users);
            services.AddSingleton<IUserStore<ApplicationUser>>(_ => Users);
            services.AddSingleton<ILogEventSink>(Logs);
            services.AddSingleton<IStartupFilter, TestClientIpStartupFilter>();
            services.PostConfigure<AntiforgeryOptions>(options => options.HeaderName = "X-CSRF-TOKEN");
        });

        // Host settings are visible while Program.cs composes the authentication
        // schemes AND in the final configuration, so this host is deterministic:
        // no SSO value can leak in from appsettings.Development.json, user secrets
        // or the machine environment.
        builder.UseSetting("Identity:Oidc:Authority", string.Empty);
        builder.UseSetting("Identity:Oidc:ClientId", string.Empty);
        builder.UseSetting("Identity:Oidc:ClientSecret", string.Empty);
        builder.UseSetting("ConnectionStrings:Default", TestConnection);
        builder.UseSetting("FileStorage:RootPath", TestStorage.Root);
    }
}

/// <summary>Shared, deliberately unreachable test infrastructure paths/values.</summary>
public static class TestStorage
{
    public static string Root { get; } = Create();

    private static string Create()
    {
        var path = Path.Combine(Path.GetTempPath(), "GraduateRecordsTests", "files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

[Collection(WebApplicationCollection.Name)]
public sealed class DevelopmentWithoutSsoTests : IClassFixture<DevelopmentWithoutSsoWebApplicationFactory>
{
    // Checksum-valid but never provisioned (see Unit/NationalIdTests for the algorithm).
    private const string UnknownNationalId = "4276067103";

    private readonly DevelopmentWithoutSsoWebApplicationFactory _factory;

    public DevelopmentWithoutSsoTests(DevelopmentWithoutSsoWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task The_application_starts_without_sso_configuration_and_without_an_oidc_scheme()
    {
        var settings = _factory.Services.GetRequiredService<IOptions<IdentitySecuritySettings>>().Value;
        Assert.False(settings.IsOidcConfigured);

        var scheme = await _factory.Services.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>()
            .GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme);
        Assert.Null(scheme);
    }

    [Fact]
    public async Task The_root_page_stays_protected_for_anonymous_requests()
    {
        using var client = CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/Home/Index")).StatusCode);
    }

    [Fact]
    public async Task The_login_page_renders_the_authentication_choice_without_an_oidc_challenge()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/Account/Login");

        // 200 with the choice page — not a 302 challenge and not a 500 from a
        // missing authentication scheme. The SSO option is visibly unavailable
        // (ADR-0015 §14: the absence of SSO is clearly represented).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Location"), "An OIDC challenge must not be issued without SSO configuration.");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ورود کارکنان و مدیران", body, StringComparison.Ordinal);
        Assert.Contains("امکان ورود از طریق سازمان دانشگاه فراهم نیست", body, StringComparison.Ordinal);
        Assert.DoesNotContain("/Account/SsoSignIn", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_development_login_endpoint_is_removed()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/dev/login/dev-test");

        // ADR-0015 §13: no hidden development authentication bypass may exist.
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound,
            $"Unexpected status {(int)response.StatusCode} for a route that must not exist.");
        Assert.False(response.Headers.Contains("Set-Cookie"), "A removed endpoint must never issue a session cookie.");
    }

    [Fact]
    public async Task Staff_can_sign_in_locally_while_sso_is_not_configured()
    {
        var nationalId = SsoTestFlow.NextNationalId();
        await CreateUserAsync(nationalId, "کاربر آزمایشی", LocalUserKind.Staff);
        using var client = CreateClient();

        var (cookieToken, requestToken) = await SsoTestFlow.MintAnonymousAntiforgeryTokensAsync(_factory.Services);
        var correlationId = "test-" + Guid.NewGuid().ToString("N");
        using var post = SsoTestFlow.LocalLogin(nationalId, SsoTestFlow.TestPassword, cookieToken, requestToken, correlationId);
        using var signIn = await client.SendAsync(post);

        // ADR-0015 §10: local authentication is fully independent of SSO availability.
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        Assert.Equal("/", signIn.Headers.Location!.ToString());

        var cookie = SsoTestFlow.ExtractCookieValue(signIn, "GraduateRecords.Auth");
        using var home = await client.SendAsync(SsoTestFlow.Authenticated(HttpMethod.Get, "/", cookie));
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);

        var audit = Assert.Single(AuditEvents(_factory.Logs, AuditActions.AuthenticationLocalSignInSucceeded, correlationId));
        Assert.False(AuditContains(audit, nationalId), "The audit must not carry the National ID (AGENTS §14).");
    }

    [Fact]
    public async Task Student_accounts_cannot_use_the_local_login_path()
    {
        var nationalId = SsoTestFlow.NextNationalId();
        await CreateUserAsync(nationalId, "دانشجوی آزمایشی", LocalUserKind.Student);
        using var client = CreateClient();

        var (cookieToken, requestToken) = await SsoTestFlow.MintAnonymousAntiforgeryTokensAsync(_factory.Services);
        var correlationId = "test-" + Guid.NewGuid().ToString("N");
        using var post = SsoTestFlow.LocalLogin(nationalId, SsoTestFlow.TestPassword, cookieToken, requestToken, correlationId);
        using var response = await client.SendAsync(post);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"), "A Student must never receive a session cookie from local login.");

        var body = await response.Content.ReadAsStringAsync();
        // Razor renders dynamic Persian text through the default (BasicLatin)
        // HTML encoder, so the on-page form of the message is entity-encoded.
        Assert.Contains(
            System.Text.Encodings.Web.HtmlEncoder.Default.Encode("نام کاربری یا گذرواژه نادرست است."),
            body,
            StringComparison.Ordinal);
        Assert.Single(AuditEvents(_factory.Logs, AuditActions.AuthenticationLocalSignInFailed, correlationId));
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        // Unique IP per client so the login rate limiter (10/min) never couples tests.
        client.DefaultRequestHeaders.Add(TestClientIpStartupFilter.HeaderName, SsoTestFlow.NextClientIp());
        return client;
    }

    /// <summary>Creates a user in this factory's in-memory store (SsoTestFlow is typed to the collection factory).</summary>
    private async Task<ApplicationUser> CreateUserAsync(string nationalId, string fullName, LocalUserKind kind)
    {
        // Development validates DI scopes, so UserManager (scoped) must come from a scope.
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = nationalId,
            NationalId = nationalId,
            FullName = fullName,
            UserKind = kind,
            LockoutEnabled = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
        };

        var result = await userManager.CreateAsync(user, SsoTestFlow.TestPassword);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Code)));
        return user;
    }

    private static IReadOnlyList<LogEvent> AuditEvents(CapturingLogSink logs, string action, string correlationId) =>
        logs.Snapshot().Where(logEvent =>
            logEvent.Properties.TryGetValue("AuditAction", out var value) &&
            value is ScalarValue { Value: string name } &&
            name == action &&
            logEvent.Properties.TryGetValue("CorrelationId", out var correlation) &&
            correlation is ScalarValue { Value: string logged } &&
            logged == correlationId).ToList();

    private static bool AuditContains(LogEvent logEvent, string text) =>
        logEvent.Properties.Values.OfType<ScalarValue>()
            .Any(value => value.Value is string property && property.Contains(text, StringComparison.Ordinal));
}
