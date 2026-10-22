using System.Security.Claims;
using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Messaging;
using GraduateRecords.Application.Common.Time;
using GraduateRecords.Application.Features.Authentication.SsoSignIn;
using GraduateRecords.Domain.Auditing;
using GraduateRecords.Infrastructure.Identity;
using GraduateRecords.Web.Infrastructure;
using GraduateRecords.Web.Middleware;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GraduateRecords.Web.Controllers;

/// <summary>
/// Sign-in, sign-out and the external (SSO) login callback.
/// AGENTS §2.2: this is the only controller in Web allowed to use ASP.NET Core
/// Identity plumbing (<c>SignInManager&lt;ApplicationUser&gt;</c>, external login
/// callback). All business decisions live in the Application handler
/// (<c>SsoSignInHandler</c>); this controller only wires Identity and redirects.
/// </summary>
public sealed class AccountController(
    IAuditWriter auditWriter,
    IClock clock) : Controller
{
    public const string LoginFailedPath = "/Account/LoginFailed";
    public const string LoggedOutPath = "/Account/LoggedOut";

    /// <summary>
    /// Starts the university SSO sign-in (ADR-0001 rules 1–3). Anonymous by
    /// design and rate limited per client IP (AGENTS §18).
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public IActionResult Login(string? returnUrl)
    {
        var properties = new AuthenticationProperties
        {
            // Open redirect protection: only host-relative return URLs are honoured.
            RedirectUri = Url.IsLocalUrl(returnUrl) ? returnUrl : "/",
        };

        return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Generic failure page. The reason category is audited server-side only;
    /// users never learn whether an account exists (no user enumeration).
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult LoginFailed() => View();

    /// <summary>Landing page after the SSO end-session redirect.</summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult LoggedOut() => View();

    /// <summary>
    /// ADR-0001 rule 14 / ADR-0008 §5.5: clear the local cookie, audit the
    /// sign-out, then redirect to the SSO end-session endpoint.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var actorDisplayName = User.FindFirstValue(ApplicationUserClaimsPrincipalFactory.FullNameClaimType);

        await auditWriter.WriteAsync(
            new AuditEntry(
                clock.UtcNow,
                actorUserId,
                actorDisplayName,
                AuditActions.AuthenticationSignedOut,
                "User",
                actorUserId ?? string.Empty,
                AuditResult.Success,
                "خروج کاربر از سامانه",
                DetailsJson: null,
                IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent: Request.Headers.UserAgent.ToString(),
                CorrelationId: CorrelationIdMiddleware.GetCorrelationId(HttpContext)),
            cancellationToken);

        var properties = new AuthenticationProperties { RedirectUri = LoggedOutPath };
        return SignOut(properties, IdentityConstants.ApplicationScheme, OpenIdConnectDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// External login callback (AGENTS §2.2). Runs after the OIDC handler has
    /// validated the SSO tokens: decide whether this SSO identity may start a
    /// local session, then replace the SSO principal with the LOCAL principal
    /// (ADR-0001 rule 4: all authorization and audit use the local user).
    ///
    /// .NET 10 note: this must be <c>OnTicketReceived</c>, not
    /// <c>OnTokenValidated</c> — the handler fires TokenValidated BEFORE the
    /// userinfo claims (which carry the National ID, ADR-0001 rule 5) are merged
    /// onto the principal. TicketReceived is the first event that sees them, and
    /// its Result/Principal/Properties flow into the cookie sign-in.
    /// </summary>
    public static async Task OnTicketReceivedAsync(TicketReceivedContext context)
    {
        var httpContext = context.HttpContext;
        var services = httpContext.RequestServices;
        var cancellationToken = httpContext.RequestAborted;
        var correlationId = CorrelationIdMiddleware.GetCorrelationId(httpContext);

        var signIn = services.GetRequiredService<IQueryHandler<SsoSignInQuery, SsoSignInTicket>>();
        var auditWriter = services.GetRequiredService<IAuditWriter>();
        var clock = services.GetRequiredService<IClock>();

        var query = new SsoSignInQuery(
            NationalIdClaimMapper.Map(context.Principal),
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.ToString(),
            correlationId);

        var result = await signIn.HandleAsync(query, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            context.HandleResponse();
            context.Response.Redirect(LoginFailedPath);
            return;
        }

        // ADR-0001 rule 4: the session is the local Identity user, never the SSO token.
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(result.Value.UserId);
        if (user is null)
        {
            context.HandleResponse();
            context.Response.Redirect(LoginFailedPath);
            return;
        }

        var principalFactory = services.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        context.Principal = await principalFactory.CreateAsync(user);

        // ADR-0001 rule 13 / owner decision: anchor the absolute session cap now.
        AbsoluteSessionLifetime.Anchor(context.Properties!, clock.UtcNow);

        // ADR-0001 "Audit events (minimum)": successful sign-in (best-effort, AGENTS §14).
        await auditWriter.WriteAsync(
            new AuditEntry(
                clock.UtcNow,
                user.Id,
                user.FullName,
                AuditActions.AuthenticationSignInSucceeded,
                "User",
                user.Id,
                AuditResult.Success,
                "ورود موفق به سامانه",
                DetailsJson: null,
                IpAddress: query.ClientIpAddress,
                UserAgent: query.UserAgent,
                CorrelationId: correlationId),
            cancellationToken);
    }

    /// <summary>
    /// SSO protocol or handshake failure (expired state, unreachable IdP, bad
    /// token...). Audited by category only: the exception text, tokens and
    /// National IDs never reach audit or the user (AGENTS §18, ADR-0001 rule 18).
    /// </summary>
    public static async Task OnRemoteFailureAsync(RemoteFailureContext context)
    {
        var httpContext = context.HttpContext;
        var services = httpContext.RequestServices;
        var clock = services.GetRequiredService<IClock>();

        await services.GetRequiredService<IAuditWriter>().WriteAsync(
            new AuditEntry(
                clock.UtcNow,
                ActorUserId: null,
                ActorDisplayName: null,
                AuditActions.AuthenticationSignInFailed,
                "User",
                EntityId: string.Empty,
                AuditResult.Failed,
                Summary: "ورود از طریق سازمان ناموفق بود",
                DetailsJson: null,
                IpAddress: httpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent: httpContext.Request.Headers.UserAgent.ToString(),
                CorrelationId: CorrelationIdMiddleware.GetCorrelationId(httpContext)),
            httpContext.RequestAborted);

        context.HandleResponse();
        context.Response.Redirect(LoginFailedPath);
    }
}
