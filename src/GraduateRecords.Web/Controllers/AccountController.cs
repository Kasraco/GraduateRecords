using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Messaging;
using GraduateRecords.Application.Common.Time;
using GraduateRecords.Application.Features.Authentication.LocalSignIn;
using GraduateRecords.Application.Features.Authentication.SsoSignIn;
using GraduateRecords.Domain.Auditing;
using GraduateRecords.Infrastructure.Identity;
using GraduateRecords.Web.Infrastructure;
using GraduateRecords.Web.Middleware;
using GraduateRecords.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using GraduateRecords.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace GraduateRecords.Web.Controllers;

/// <summary>
/// Sign-in, sign-out and the external (SSO) login callback.
/// AGENTS §2.2: this is the only controller in Web allowed to use ASP.NET Core
/// Identity plumbing (<c>SignInManager&lt;ApplicationUser&gt;</c>, password
/// check, sign-in/sign-out, external login callback). Business decisions live
/// in the Application handlers (<c>SsoSignInHandler</c>, <c>LocalSignInHandler</c>);
/// this controller only wires Identity and redirects.
/// ADR-0015 §1/§10: two formal authentication paths — University SSO (primary)
/// and local National ID + password for eligible Staff/Administrator accounts.
/// The user explicitly chooses the path on the login page; there is no silent
/// fallback between them.
/// </summary>
public sealed class AccountController(
    IAuditWriter auditWriter,
    IClock clock,
    IOptions<IdentitySecuritySettings> identitySecuritySettings,
    IQueryHandler<LocalSignInQuery, LocalSignInTicket> localSignIn,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : Controller
{
    public const string LoginFailedPath = "/Account/LoginFailed";
    public const string LoggedOutPath = "/Account/LoggedOut";

    /// <summary>
    /// Login choice page (ADR-0015 §10, AGENTS 13.5): the user explicitly picks
    /// University SSO or local Staff/Administrator login. Anonymous by design and
    /// rate limited per client IP (AGENTS §18).
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public IActionResult Login(string? returnUrl)
    {
        // The query value is bound into ModelState (key "returnUrl") before this
        // action runs, and ModelState wins over the view model when asp-for
        // renders — an external returnUrl would be echoed back raw. Drop it; only
        // the sanitized value below may ever be rendered (ADR-0015 §8).
        ModelState.Remove(nameof(LocalLoginViewModel.ReturnUrl));

        return View(new LocalLoginViewModel
        {
            // Development may run without SSO configuration; every other environment
            // fails closed at startup, so the flag is only ever false in Development.
            SsoAvailable = identitySecuritySettings.Value.IsOidcConfigured,
            ReturnUrl = SafeReturnUrl(returnUrl),
        });
    }

    /// <summary>
    /// Starts the university SSO sign-in (ADR-0001 rules 1–3, ADR-0015 §1).
    /// SSO is the primary path but is never issued implicitly: the user asked for
    /// it on the login page.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public IActionResult SsoSignIn(string? returnUrl)
    {
        // AGENTS §13 "Development SSO / Authentication Rule": with no SSO
        // configuration (Development only — every other environment fails at
        // startup), never issue an invalid OIDC challenge; return to the login
        // choice page, where the SSO option is visibly unavailable.
        if (!identitySecuritySettings.Value.IsOidcConfigured)
        {
            return RedirectToAction(nameof(Login));
        }

        var properties = new AuthenticationProperties
        {
            // Open redirect protection: only host-relative return URLs are honoured.
            RedirectUri = SafeReturnUrl(returnUrl) ?? "/",
        };

        return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Local National ID + password sign-in for eligible Staff/Administrator
    /// accounts (ADR-0015 §1). The identifier is normalized and validated with
    /// the same canonical rules as the SSO path (ADR-0015 §4); the eligibility
    /// decision lives in <see cref="LocalSignInHandler"/>; the password check and
    /// lockout accounting are Identity plumbing (AGENTS §2.2). Students are
    /// rejected server-side before any password check (ADR-0015 §6). Every
    /// failure renders the same generic message so the response never reveals
    /// whether the National ID exists or whether a local password exists
    /// (ADR-0015 §8). Works fully independently of the university SSO provider
    /// (ADR-0015 §10).
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> LocalLogin(LocalLoginViewModel model, CancellationToken cancellationToken)
    {
        var correlationId = CorrelationIdMiddleware.GetCorrelationId(HttpContext);
        var clientIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        var result = await localSignIn.HandleAsync(
            new LocalSignInQuery(model.NationalId, clientIpAddress, userAgent, correlationId),
            cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            // Enumeration resistance: an unknown/not-perbidden National ID must not
            // answer faster than a wrong password, so a dummy verification (never a
            // store write) equalizes the work done (ADR-0015 §8).
            await EqualizeCredentialCheckTimingAsync(model.Password ?? string.Empty);
            return LocalFailure(model);
        }

        var user = await userManager.FindByIdAsync(result.Value.UserId);
        if (user is null)
        {
            await EqualizeCredentialCheckTimingAsync(model.Password ?? string.Empty);
            return LocalFailure(model);
        }

        var check = await signInManager.CheckPasswordSignInAsync(
            user, model.Password ?? string.Empty, lockoutOnFailure: true);

        if (!check.Succeeded)
        {
            // Category is audited server-side only; the user sees the one generic message.
            await auditWriter.WriteAsync(
                new AuditEntry(
                    clock.UtcNow,
                    user.Id,
                    user.FullName,
                    AuditActions.AuthenticationLocalSignInFailed,
                    "User",
                    user.Id,
                    AuditResult.Failed,
                    check.IsLockedOut
                        ? "ورود محلی به دلیل قفل بودن حساب ناموفق بود."
                        : "ورود محلی با گذرواژه نادرست ناموفق بود.",
                    DetailsJson: null,
                    IpAddress: clientIpAddress,
                    UserAgent: userAgent,
                    CorrelationId: correlationId),
                cancellationToken);

            return LocalFailure(model);
        }

        // The session is the local Identity user (ADR-0015 §3/§15): same cookie,
        // same principal factory and same absolute-session anchor as an SSO
        // session. The marker records the path for audit/logout only (§5/§16).
        var properties = new AuthenticationProperties();
        AbsoluteSessionLifetime.Anchor(properties, clock.UtcNow);
        AuthenticationMethodMarker.Mark(properties, AuthenticationMethodMarker.Local);
        await signInManager.SignInAsync(user, properties);

        // Best-effort operational sign-in audit (AGENTS §14), like every session start.
        await auditWriter.WriteAsync(
            new AuditEntry(
                clock.UtcNow,
                user.Id,
                user.FullName,
                AuditActions.AuthenticationLocalSignInSucceeded,
                "User",
                user.Id,
                AuditResult.Success,
                "ورود موفق با احراز هویت محلی",
                DetailsJson: null,
                IpAddress: clientIpAddress,
                UserAgent: userAgent,
                CorrelationId: correlationId),
            cancellationToken);

        return LocalRedirect(SafeReturnUrl(model.ReturnUrl) ?? "/");
    }

    private IActionResult LocalFailure(LocalLoginViewModel model)
    {
        // Single enumeration-resistant message for every failure category (ADR-0015 §8).
        ModelState.AddModelError(string.Empty, LocalSignInHandler.GenericFailureMessage);
        return View(nameof(Login), RebuildViewModel(model));
    }

    /// <summary>
    /// Rebuilds the page model after a failed attempt: the SSO availability flag
    /// is re-read, the entered National ID is preserved, the attempted password is
    /// dropped from model state so it is never re-rendered, and the submitted
    /// ReturnUrl is re-sanitized with its model-state entry removed, so no
    /// attacker-supplied external value is echoed (ADR-0015 §8).
    /// </summary>
    private LocalLoginViewModel RebuildViewModel(LocalLoginViewModel model)
    {
        ModelState.Remove(nameof(LocalLoginViewModel.Password));

        // Only the sanitized copy below may render; the raw form value must never
        // reach the hidden input or the SSO link (SafeReturnUrl stays authoritative).
        ModelState.Remove(nameof(LocalLoginViewModel.ReturnUrl));

        return new LocalLoginViewModel
        {
            SsoAvailable = identitySecuritySettings.Value.IsOidcConfigured,
            NationalId = model.NationalId,
            ReturnUrl = SafeReturnUrl(model.ReturnUrl),
        };
    }

    private string? SafeReturnUrl(string? returnUrl) => Url.IsLocalUrl(returnUrl) ? returnUrl : null;

    /// <summary>
    /// ADR-0015 §8: when the account must not even learn that it was looked up
    /// (invalid/unknown National ID, Student account), the submitted password is
    /// still verified against an in-memory throwaway credential with the real
    /// hashing cost, so responses do not leak existence through timing. The
    /// dummy user is never persisted and lockout accounting is disabled, so no
    /// store write can happen.
    /// </summary>
    private async Task EqualizeCredentialCheckTimingAsync(string password) =>
        await signInManager.CheckPasswordSignInAsync(
            DummyCredentialUser.Value, password, lockoutOnFailure: false);

    private static readonly Lazy<ApplicationUser> DummyCredentialUser = new(CreateDummyCredentialUser);

    private static readonly Lazy<PasswordHasher<ApplicationUser>> DummyCredentialHasher =
        new(() => new PasswordHasher<ApplicationUser>());

    private static ApplicationUser CreateDummyCredentialUser()
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return new ApplicationUser
        {
            Id = "dummy-credential-check",
            UserName = "dummy-credential-check",
            NationalId = "0000000000",
            FullName = "بررسی داخلی اعتبارسنجی",
            LockoutEnabled = false,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            PasswordHash = DummyCredentialHasher.Value.HashPassword(new ApplicationUser(), secret),
        };
    }

    /// <summary>Generic failure page. The reason category is audited server-side only;
    /// users never learn whether an account exists (no user enumeration).</summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult LoginFailed() => View();

    /// <summary>Landing page after logout (SSO end-session or local sign-out).</summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult LoggedOut() => View();

    /// <summary>
    /// ADR-0015 §15/§16: clear the local cookie and audit the sign-out with the
    /// session's authentication path. A local session performs no SSO end-session
    /// operation; an SSO session (or a pre-marker legacy session) keeps the
    /// configured identity-provider end-session behaviour (ADR-0008 §5.5).
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var actorDisplayName = User.FindFirstValue(ApplicationUserClaimsPrincipalFactory.FullNameClaimType);

        // The ticket properties survive security-stamp principal renewal, so the
        // path recorded at sign-in is still readable here (ADR-0015 §16).
        var ticket = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var method = AuthenticationMethodMarker.Get(ticket.Properties) ?? AuthenticationMethodMarker.Sso;
        var isSsoSession = method != AuthenticationMethodMarker.Local;

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
                // Allow-listed, non-sensitive observability detail (ADR-0015 §16).
                DetailsJson: JsonSerializer.Serialize(new { Method = method }),
                IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent: Request.Headers.UserAgent.ToString(),
                CorrelationId: CorrelationIdMiddleware.GetCorrelationId(HttpContext)),
            cancellationToken);

        var properties = new AuthenticationProperties { RedirectUri = LoggedOutPath };
        return isSsoSession && identitySecuritySettings.Value.IsOidcConfigured
            ? SignOut(properties, IdentityConstants.ApplicationScheme, OpenIdConnectDefaults.AuthenticationScheme)
            : SignOut(properties, IdentityConstants.ApplicationScheme);
    }

    /// <summary>
    /// External login callback (AGENTS §2.2). Runs after the OIDC handler has
    /// validated the SSO tokens: decide whether this SSO identity may start a
    /// local session, then replace the SSO principal with the LOCAL principal
    /// (ADR-0001 rule 4: all authorization and audit use the local user; the
    /// same ApplicationUser a local sign-in would resolve, ADR-0015 §3).
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

        // ADR-0015 §16: remember the path for logout behaviour and audit only.
        AuthenticationMethodMarker.Mark(context.Properties!, AuthenticationMethodMarker.Sso);

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
