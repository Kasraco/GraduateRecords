using System.Net;
using GraduateRecords.Domain.Auditing;
using GraduateRecords.Infrastructure.Identity;
using GraduateRecords.Infrastructure.Options;
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
/// start, the OIDC scheme must not exist, <c>/</c> must stay protected, Login must
/// render the safe local unavailable state, and only the controlled development
/// identity may sign in through <c>/dev/login</c>.
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
    // The controlled development identity. Internal in Web, so the literal is used
    // here on purpose: this is the ONLY key /dev/login accepts.
    private const string DevelopmentIdentityKey = "dev-test";

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
    public async Task Login_shows_the_safe_unavailable_state_without_an_oidc_challenge()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/Account/Login");

        // 200 with the existing failure page — not a 302 challenge and not a 500
        // from a missing authentication scheme.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Location"), "An OIDC challenge must not be issued without SSO configuration.");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ورود ناموفق", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_controlled_development_identity_signs_in_through_the_normal_pipeline()
    {
        using var client = CreateClient();
        using var signIn = await client.GetAsync("/dev/login/" + DevelopmentIdentityKey);

        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        Assert.Equal("/", signIn.Headers.Location!.ToString());

        var cookie = AuthCookie(signIn);
        using var home = await client.SendAsync(SsoTestFlow.Authenticated(HttpMethod.Get, "/", cookie));
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);

        // The development sign-in is audited like every other session start (AGENTS §14).
        var audit = Assert.Single(AuditEvents(_factory.Logs, AuditActions.AuthenticationSignInSucceeded));
        Assert.True(AuditContains(audit, "کاربر آزمایشی توسعه"), "The audit must carry the controlled test identity name.");
        Assert.DoesNotContain(_factory.Users.All, user => user.UserName != DevelopmentIdentityKey);
    }

    [Fact]
    public async Task Arbitrary_national_identifiers_are_never_accepted_by_the_development_login()
    {
        using var client = CreateClient();

        foreach (var key in new[] { "4276067103", "0012345679", DevelopmentIdentityKey + "-admin" })
        {
            using var response = await client.GetAsync("/dev/login/" + key);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"), "A rejected identity must never receive a session cookie.");
        }
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        // Loopback client: /dev/login only answers local callers.
        client.DefaultRequestHeaders.Add(TestClientIpStartupFilter.HeaderName, "127.0.0.1");
        return client;
    }

    private static string AuthCookie(HttpResponseMessage response)
    {
        var pair = response.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';', 2)[0])
            .FirstOrDefault(value => value.StartsWith("GraduateRecords.Auth=", StringComparison.Ordinal));

        Assert.NotNull(pair);
        return pair!["GraduateRecords.Auth=".Length..];
    }

    private static IReadOnlyList<LogEvent> AuditEvents(CapturingLogSink logs, string action) =>
        logs.Snapshot().Where(logEvent =>
            logEvent.Properties.TryGetValue("AuditAction", out var value) &&
            value is ScalarValue { Value: string name } &&
            name == action).ToList();

    private static bool AuditContains(LogEvent logEvent, string text) =>
        logEvent.Properties.Values.OfType<ScalarValue>()
            .Any(value => value.Value is string property && property.Contains(text, StringComparison.Ordinal));
}
