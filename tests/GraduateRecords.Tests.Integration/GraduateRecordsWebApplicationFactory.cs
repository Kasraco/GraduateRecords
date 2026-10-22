using System.Net;
using GraduateRecords.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;
using Serilog.Events;

namespace GraduateRecords.Tests.Integration;

public sealed class GraduateRecordsWebApplicationFactory : WebApplicationFactory<Program>
{
    public CapturingLogSink Logs { get; } = new();
    public FakeOpenIdProvider FakeIdP { get; } = new();
    public InMemoryUserStore Users { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILogEventSink>(Logs);

            // AGENTS §22: no SQL Server in CI, so the Identity store is replaced with
            // an in-memory one (never EF InMemory/SQLite; no persistence behavior is
            // under test here).
            services.RemoveAll<IUserStore<ApplicationUser>>();
            services.AddSingleton(Users);
            services.AddSingleton<IUserStore<ApplicationUser>>(_ => Users);

            // Test-only: lets each test use its own rate-limiter partition (AGENTS §18).
            services.AddSingleton<IStartupFilter, TestClientIpStartupFilter>();

            services.PostConfigure<AntiforgeryOptions>(options => options.HeaderName = "X-CSRF-TOKEN");

            // Configure (not PostConfigure, and under the scheme's name): the built-in
            // OpenIdConnect post-configure derives Options.Backchannel from
            // BackchannelHttpHandler, so the fake IdP must be set before it runs.
            services.Configure<OpenIdConnectOptions>(
                OpenIdConnectDefaults.AuthenticationScheme,
                options => options.BackchannelHttpHandler = FakeIdP);
        });

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var fileRoot = Path.Combine(Path.GetTempPath(), "GraduateRecordsTests", "files");
            Directory.CreateDirectory(fileRoot);

            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:RootPath"] = fileRoot,
                // Intentional non-routable host so readiness fails closed without needing LocalDB in CI.
                ["ConnectionStrings:Default"] =
                    "Server=127.0.0.1,65530;Database=GraduateRecords_Tests;User ID=sa;Password=unused;TrustServerCertificate=True;Encrypt=False;Connect Timeout=1;Command Timeout=1",

                // Owner-decided UC1 security values (ADR-0001/0002 + owner decisions),
                // with a test SSO provider. Secrets come from the environment in
                // production; the in-repo files keep them empty.
                ["Identity:Lockout:MaxFailedAccessAttempts"] = "5",
                ["Identity:Lockout:LockoutDurationMinutes"] = "15",
                ["Identity:Lockout:AllowedForNewUsers"] = "true",
                ["Identity:Password:RequiredLength"] = "14",
                ["Identity:CookieLifetime:IdleMinutes"] = "30",
                ["Identity:CookieLifetime:AbsoluteHours"] = "8",
                ["Identity:LoginRateLimit:PermitsPerMinute"] = "10", // the decided value; tests isolate by client IP
                ["Identity:Oidc:Authority"] = FakeOpenIdProvider.Authority,
                ["Identity:Oidc:ClientId"] = FakeOpenIdProvider.ClientId,
                ["Identity:Oidc:ClientSecret"] = "integration-test-secret",
                ["Identity:Oidc:UsePkce"] = "true",
            });
        });
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

/// <summary>
/// Rewrites the connection's remote IP from a test-only header before the pipeline
/// (and therefore before the rate limiter) runs, so every test method gets its own
/// rate-limiter partition. Never registered in production.
/// </summary>
public sealed class TestClientIpStartupFilter : IStartupFilter
{
    public const string HeaderName = "X-Test-Client-IP";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(static (context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(HeaderName, out var header)
                && IPAddress.TryParse(header, out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            return nextMiddleware();
        });

        next(app);
    };
}

public sealed class CapturingLogSink : ILogEventSink
{
    private readonly List<LogEvent> _events = [];
    private readonly object _gate = new();

    public void Emit(LogEvent logEvent)
    {
        lock (_gate)
        {
            _events.Add(logEvent);
        }
    }

    public IReadOnlyList<LogEvent> Snapshot()
    {
        lock (_gate)
        {
            return _events.ToList();
        }
    }
}
