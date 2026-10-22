using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Time;
using GraduateRecords.Infrastructure.Auditing;
using GraduateRecords.Infrastructure.Health;
using GraduateRecords.Infrastructure.Identity;
using GraduateRecords.Infrastructure.Options;
using GraduateRecords.Infrastructure.Persistence;
using GraduateRecords.Infrastructure.Time;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using GraduateRecords.Infrastructure.Logging;
using Serilog.Core;

namespace GraduateRecords.Infrastructure;

public static class DependencyInjection
{
    public const string ApplicationCookieName = "GraduateRecords.Auth";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName,
        string webRootPath)
    {
        services
            .AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => FileStorageOptions.IsSafeRoot(options.RootPath, webRootPath),
                "FileStorage:RootPath must be fully qualified and outside the public web root.")
            .ValidateOnStart();

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection("ConnectionStrings"))
            .Validate(options => DatabaseOptions.IsValid(options.Default, environmentName),
                "ConnectionStrings:Default is missing, invalid, or violates database TLS requirements.")
            .ValidateOnStart();

        services.AddDbContext<AppDbContext>((provider, options) =>
            options.UseSqlServer(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Default,
                sql => sql.UseCompatibilityLevel(170)));

        services.AddSingleton<ILogEventSink>(
            new DatabaseLogSink(configuration.GetConnectionString("Default") ?? string.Empty));

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
            .AddCheck<FileStorageHealthCheck>("filestorage", tags: ["ready"])
            .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: ["live"]);

        return services;
    }

    /// <summary>
    /// Phase 1A Use Case 1: local Identity store, decided security options, the
    /// application cookie (ADR-0001 rule 13, ADR-0008 §5) and the university SSO
    /// scheme (ADR-0001 rules 1–3). No local password login route exists: the
    /// break-glass administrator requires MFA (ADR-0001 rule 12), which is not
    /// decided or implemented, so password-only authentication stays impossible.
    /// </summary>
    public static IServiceCollection AddIdentityAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<IdentitySecuritySettings>()
            .Bind(configuration.GetSection(IdentitySecuritySettings.SectionName))
            .Validate(static settings => settings.IsValid(), IdentitySecuritySettings.ValidationMessage)
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();

        services.AddIdentityCore<ApplicationUser>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager();

        services.TryAddScoped<ISecurityStampValidator, SecurityStampValidator<ApplicationUser>>();
        services.Replace(ServiceDescriptor.Scoped<IUserClaimsPrincipalFactory<ApplicationUser>, ApplicationUserClaimsPrincipalFactory>());

        services.AddOptions<IdentityOptions>()
            .Configure<IServiceProvider>((options, provider) =>
                IdentityOptionsSetup.Apply(
                    options,
                    provider.GetRequiredService<IOptions<IdentitySecuritySettings>>().Value));

        // ADR-0008 §5.3: validate the security stamp on every request so a deactivated
        // user is blocked on the next request. This is stricter than the ≤ 5 min floor
        // in AGENTS §13 and ADR-0002 rule 7.2, never weaker.
        services.Configure<SecurityStampValidatorOptions>(
            options => options.ValidationInterval = TimeSpan.Zero);

        services
            .AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme)
            // Identity's SecurityStampValidator unconditionally signs out of the
            // two-factor remember-me scheme when it rejects a principal (deleted or
            // deactivated user). AddIdentity registers these auxiliary cookies; with
            // AddIdentityCore + manual composition we must register this one too, or
            // the rejection path throws (no handler for the scheme -> 500 instead of
            // a clean 401). It is never issued in v1 (no 2FA); it only lets rejection
            // complete.
            .AddCookie(IdentityConstants.TwoFactorRememberMeScheme, options =>
            {
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
            })
            // All OpenIdConnect option values are applied by the named configure below.
            .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, _ => { });

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<IServiceProvider>((options, provider) => ConfigureApplicationCookie(options, provider));
        services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
            .Configure<IServiceProvider>((options, provider) => ConfigureOpenIdConnect(options, provider));

        services.AddScoped<ILocalUserDirectory, LocalUserDirectory>();
        services.AddScoped<IAuditWriter, SerilogAuditWriter>();
        services.AddScoped<IClock, Time.SystemClock>();

        return services;
    }

    private static void ConfigureApplicationCookie(CookieAuthenticationOptions options, IServiceProvider provider)
    {
        var settings = provider.GetRequiredService<IOptions<IdentitySecuritySettings>>().Value;

        // ADR-0001 rule 13 / ADR-0008 §5.1: HttpOnly, Secure, SameSite=Lax, fixed name.
        options.Cookie.Name = ApplicationCookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;

        // Owner decision: 30-minute idle lifetime, sliding, with an 8-hour absolute cap.
        options.ExpireTimeSpan = settings.IdleSessionLifetime;
        options.SlidingExpiration = true;

        options.LoginPath = "/Account/Login";
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };

        options.Events.OnValidatePrincipal = async context =>
        {
            if (!AbsoluteSessionLifetime.IsWithinAbsoluteCap(
                    context.Properties,
                    TimeProvider.System.GetUtcNow(),
                    settings.AbsoluteSessionCap))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                return;
            }

            // ADR-0008 §5.3: per-request security stamp validation (interval = zero).
            await SecurityStampValidator.ValidatePrincipalAsync(context);
        };
    }

    private static void ConfigureOpenIdConnect(OpenIdConnectOptions options, IServiceProvider provider)
    {
        var oidc = provider.GetRequiredService<IOptions<IdentitySecuritySettings>>().Value.Oidc;

        options.Authority = oidc.Authority;
        options.ClientId = oidc.ClientId;
        options.ClientSecret = oidc.ClientSecret;
        options.UsePkce = oidc.UsePkce;

        // ADR-0001 rule 1: Authorization Code flow; PKCE when the IdP supports it
        // (discovery is verified before go-live per the ADR's verification list).
        options.ResponseType = "code";
        options.ResponseMode = "query";

        // ADR-0001 rule 5: the National ID arrives in the userinfo payload. The
        // default claim actions only map the standard OIDC keys (sub/name/email...),
        // so the university's "id" key must be mapped explicitly.
        options.GetClaimsFromUserInfoEndpoint = true;
        options.ClaimActions.MapUniqueJsonKey(NationalIdClaimMapper.ClaimType, NationalIdClaimMapper.ClaimType);

        options.SignInScheme = IdentityConstants.ApplicationScheme;
        options.SaveTokens = false;

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
    }
}
