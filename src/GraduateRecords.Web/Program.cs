using System.Threading.RateLimiting;
using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Time;
using GraduateRecords.Domain.Auditing;
using GraduateRecords.Infrastructure;
using GraduateRecords.Infrastructure.Identity;
using GraduateRecords.Infrastructure.Logging;
using GraduateRecords.Web.Controllers;
using GraduateRecords.Web.Infrastructure;
using GraduateRecords.Web.Middleware;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;

var builder = WebApplication.CreateBuilder(args);

DevelopmentConfiguration.EnsureFileStorageRoot(builder);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.EnvironmentName,
    builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"));

builder.Host.UseSerilog((context, services, configuration) =>
{
    // Deliberately NOT using .ReadFrom.Services(services): InjectedLoggerSettings adds
    // every DI-registered ILogEventSink, and the explicit loop below does the same —
    // together they duplicated each sink (every log event was emitted twice).
    var inner = new LoggerConfiguration()
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "GraduateRecords");

    foreach (var sink in services.GetServices<ILogEventSink>())
    {
        inner = inner.WriteTo.Sink(sink);
    }

    // SafeLogSink strips exception messages/stack traces before any sink sees them (AGENTS §14/§18).
    configuration
        .MinimumLevel.Information()
        .Enrich.FromLogContext()
        .WriteTo.Sink(new SafeLogSink(inner.CreateLogger()));
});

// ADR-0001/0002/0008: local Identity + university SSO + application cookie.
// Key-ring persistence in the database (ADR-0008 §1) and certificate encryption
// at rest (§2) are delivered by their own infrastructure slice; development uses
// the framework default key ring meanwhile.
builder.Services.AddDataProtection()
    .SetApplicationName("GraduateRecords")
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

// ADR-0001/0002/0008: local Identity + university SSO + application cookie.
// The environment decides whether SSO configuration is mandatory (AGENTS §13
// "Development SSO / Authentication Rule"): Development may start without it,
// every other environment fails fast at startup.
builder.Services.AddIdentityAuthentication(builder.Configuration, builder.Environment.EnvironmentName);

builder.Services.AddQueryHandlers(typeof(GraduateRecords.Application.AssemblyMarker).Assembly);

// OIDC event plumbing lives on AccountController (AGENTS §2.2 allows the external
// login callback there); Program.cs only composes it.
// MUST be registered under the scheme's options name: the handler resolves the
// named OpenIdConnectOptions, and a default-named callback would never run.
// OnTicketReceived (not OnTokenValidated) is the .NET 10 event that runs after
// the userinfo claims carrying the National ID are merged onto the principal.
builder.Services.PostConfigure<OpenIdConnectOptions>(
    OpenIdConnectDefaults.AuthenticationScheme,
    options =>
{
    options.Events ??= new OpenIdConnectEvents();
    options.Events.OnTicketReceived = AccountController.OnTicketReceivedAsync;
    options.Events.OnRemoteFailure = AccountController.OnRemoteFailureAsync;
    options.SignedOutRedirectUri = AccountController.LoggedOutPath;
});

// ADR-0008 §5.4: antiforgery uses Data Protection and a project-specific cookie name.
builder.Services.AddAntiforgery(options => options.Cookie.Name = "GraduateRecords.Antiforgery");

// AGENTS §18: built-in rate limiting on the login endpoint, per client IP
// (owner decision: 10 attempts per minute).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.ContentType = "text/html; charset=utf-8";
        await context.HttpContext.Response.WriteAsync(
            "<!DOCTYPE html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\" />" +
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />" +
            "<title>محدودیت تعداد درخواست</title></head>" +
            "<body class=\"min-h-screen bg-background text-foreground\"><main class=\"mx-auto max-w-xl p-6\">" +
            "<h1 class=\"text-2xl font-bold\">محدودیت تعداد درخواست</h1>" +
            "<p class=\"mt-4 text-base leading-7\">تعداد درخواست‌های شما بیش از حد مجاز است. لطفاً کمی بعد دوباره تلاش کنید.</p>" +
            "</main></body></html>");
    };

    options.AddPolicy<string>(RateLimitPolicies.Login, context =>
    {
        var settings = context.RequestServices
            .GetRequiredService<IOptions<GraduateRecords.Infrastructure.Options.IdentitySecuritySettings>>().Value;
        var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = settings.LoginRateLimit.PermitsPerMinute,
            Window = settings.LoginRateLimit.Window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    });
});
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AuthorizeFilter());
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
// Request-completion events fall back to Serilog's process-wide static Log when
// RequestLoggingOptions.Logger is unset, which is wrong whenever more than one
// host runs in the same process (the integration tests boot several hosts) and is
// fragile in general. Bind the completion event to the logger this host installed
// during builder.Build(), so each host writes its own request logs.
var requestCompletionLogger = Serilog.Log.Logger;
app.UseSerilogRequestLogging(options =>
{
    options.Logger = requestCompletionLogger;
    options.MessageTemplate = "HTTP request completed {StatusCode} in {Elapsed:0.0000} ms";
});
app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// AGENTS §13 "Development SSO / Authentication Rule": an explicit,
// development-only authentication mechanism for exercising protected UI without
// the university IdP. It is registered ONLY in the Development environment and
// only while real SSO configuration is absent, accepts ONLY the fixed controlled
// test identity (never an arbitrary National ID, password or university
// credential), signs in through the normal Identity pipeline and therefore
// grants nothing extra: fallback policy, permission, ownership and staff-scope
// checks are untouched.
if (app.Environment.IsDevelopment() &&
    !app.Services.GetRequiredService<IOptions<GraduateRecords.Infrastructure.Options.IdentitySecuritySettings>>().Value.IsOidcConfigured)
{
    app.MapGet("/dev/login/{identityKey}", async (
        string identityKey,
        HttpContext http,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        IAuditWriter auditWriter,
        IClock clock) =>
    {
        // Loopback callers only, and only the controlled development identity.
        var isLoopback = System.Net.IPAddress.IsLoopback(
            http.Connection.RemoteIpAddress ?? System.Net.IPAddress.None);

        if (!isLoopback ||
            !string.Equals(identityKey, DevelopmentConfiguration.TestIdentityKey, StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        var user = await users.FindByNameAsync(DevelopmentConfiguration.TestIdentityUserName);
        if (user is null)
        {
            user = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString("N"),
                UserName = DevelopmentConfiguration.TestIdentityUserName,
                FullName = DevelopmentConfiguration.TestIdentityFullName,
                SecurityStamp = Guid.NewGuid().ToString("N"),
            };

            var created = await users.CreateAsync(user);
            if (!created.Succeeded)
            {
                return Results.NotFound();
            }
        }

        var properties = new AuthenticationProperties();
        AbsoluteSessionLifetime.Anchor(properties, clock.UtcNow);
        await signIn.SignInAsync(user, properties);

        // Best-effort operational sign-in audit (AGENTS §14), like every session start.
        await auditWriter.WriteAsync(
            new AuditEntry(
                clock.UtcNow,
                user.Id,
                user.FullName,
                AuditActions.AuthenticationSignInSucceeded,
                "User",
                user.Id,
                AuditResult.Success,
                "ورود آزمایشی محیط توسعه",
                DetailsJson: null,
                IpAddress: http.Connection.RemoteIpAddress?.ToString(),
                UserAgent: http.Request.Headers.UserAgent.ToString(),
                CorrelationId: CorrelationIdMiddleware.GetCorrelationId(http)),
            http.RequestAborted);

        return Results.Redirect("/");
    }).AllowAnonymous();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = static check => check.Tags.Contains("live"),
    ResponseWriter = HealthCheckResponseWriter.WriteAsync,
    ResultStatusCodes =
    {
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy] = StatusCodes.Status200OK,
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded] = StatusCodes.Status200OK,
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    },
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = static check => check.Tags.Contains("ready"),
    ResponseWriter = HealthCheckResponseWriter.WriteAsync,
    ResultStatusCodes =
    {
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy] = StatusCodes.Status200OK,
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded] = StatusCodes.Status200OK,
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    },
}).AllowAnonymous();

if (app.Environment.IsEnvironment("Testing"))
{
    // Test-only route; never registered outside the Testing environment.
    app.MapGet("/__test/throw", static (HttpContext _) =>
        throw new InvalidOperationException("test-exception-secret-123456")).AllowAnonymous();
}

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

try
{
    await app.RunAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program;
