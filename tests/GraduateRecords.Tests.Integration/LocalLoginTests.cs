using System.Net;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Domain.Auditing;
using GraduateRecords.Infrastructure.Identity;
using GraduateRecords.Web.Controllers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;

namespace GraduateRecords.Tests.Integration;

/// <summary>
/// ADR-0015: local National ID + password authentication for eligible
/// Staff/Administrator accounts. Covers the authentication matrix (Students
/// SSO-only), enumeration-resistant generic failures, lockout, SSO independence,
/// the single ApplicationUser identity shared with SSO, audit distinguishability,
/// and the removal of the temporary /dev/login mechanism.
/// </summary>
[Collection(WebApplicationCollection.Name)]
public sealed class LocalLoginTests(GraduateRecordsWebApplicationFactory factory)
{
    // Checksum-valid but never provisioned (see Unit/NationalIdTests for the algorithm).
    private const string UnknownNationalId = "4276067103";
    private const string GenericFailureMessage = "نام کاربری یا گذرواژه نادرست است.";

    /// <summary>
    /// The on-page form of a dynamic Persian message: Razor renders expression
    /// output through <c>HtmlEncoder.Default</c> (BasicLatin), so non-ASCII text
    /// appears as numeric character entities in the HTML source (identical
    /// rendering in the browser). Template literals stay raw, which is why only
    /// expression-rendered messages need this.
    /// </summary>
    private static string OnPage(string message) =>
        System.Text.Encodings.Web.HtmlEncoder.Default.Encode(message);

    [Theory]
    [InlineData(LocalUserKind.Staff)]
    [InlineData(LocalUserKind.Administrator)]
    public async Task An_eligible_account_signs_in_locally_with_valid_credentials(LocalUserKind kind)
    {
        var nationalId = SsoTestFlow.NextNationalId();
        await SsoTestFlow.CreateUserAsync(factory, nationalId, "کاربر آزمایشی", kind);
        using var client = SsoTestFlow.CreateClient(factory);

        using var response = await PostLocalLoginAsync(client, nationalId, SsoTestFlow.TestPassword);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.ToString());

        // ADR-0008 §5.1: the local session is the SAME application cookie with the
        // same security flags as an SSO session (ADR-0015 §15).
        var cookie = SsoTestFlow.ExtractCookieValue(response, "GraduateRecords.Auth");
        Assert.Contains("httponly", response.Headers.GetValues("Set-Cookie").First(
            value => value.StartsWith("GraduateRecords.Auth=", StringComparison.Ordinal)), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", response.Headers.GetValues("Set-Cookie").First(
            value => value.StartsWith("GraduateRecords.Auth=", StringComparison.Ordinal)), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", response.Headers.GetValues("Set-Cookie").First(
            value => value.StartsWith("GraduateRecords.Auth=", StringComparison.Ordinal)), StringComparison.OrdinalIgnoreCase);

        using var home = await client.SendAsync(SsoTestFlow.Authenticated(HttpMethod.Get, "/", cookie));
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);

        // ADR-0015 §16: local sign-in is audited as Local, without the National ID.
        var audit = Assert.Single(AuditEvents(AuditActions.AuthenticationLocalSignInSucceeded, LastCorrelationId));
        Assert.False(SsoTestFlow.AuditContains(audit, nationalId),
            "The audit event must not contain the National ID (AGENTS §14).");
    }

    [Fact]
    public async Task A_student_account_is_rejected_by_the_local_login_path_even_with_a_password_hash()
    {
        var nationalId = SsoTestFlow.NextNationalId();
        await SsoTestFlow.CreateUserAsync(factory, nationalId, "دانشجوی آزمایشی", LocalUserKind.Student);
        using var client = SsoTestFlow.CreateClient(factory);

        using var response = await PostLocalLoginAsync(client, nationalId, SsoTestFlow.TestPassword);

        // Server-side enforcement (ADR-0015 §6), not a hidden UI: the user exists
        // AND holds a valid password, yet the ordinary local path rejects it.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"), "A Student must never receive a session cookie from local login.");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(OnPage(GenericFailureMessage), body, StringComparison.Ordinal);

        var audit = Assert.Single(AuditEvents(AuditActions.AuthenticationLocalSignInFailed, LastCorrelationId));
        Assert.True(SsoTestFlow.AuditEquals(audit, "ورود محلی برای این نوع حساب مجاز نیست."),
            "The category (not permitted) is audited server-side only.");
    }

    [Fact]
    public async Task An_invalid_password_is_rejected_with_the_generic_error()
    {
        var nationalId = SsoTestFlow.NextNationalId();
        await SsoTestFlow.CreateUserAsync(factory, nationalId, "کاربر آزمایشی", LocalUserKind.Staff);
        using var client = SsoTestFlow.CreateClient(factory);

        using var response = await PostLocalLoginAsync(client, nationalId, "WrongPassword1234");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(OnPage(GenericFailureMessage), body, StringComparison.Ordinal);

        var audit = Assert.Single(AuditEvents(AuditActions.AuthenticationLocalSignInFailed, LastCorrelationId));
        Assert.True(SsoTestFlow.AuditEquals(audit, "ورود محلی با گذرواژه نادرست ناموفق بود."),
            "The category (bad password) is audited server-side only.");
    }

    [Fact]
    public async Task An_unknown_national_id_produces_the_same_generic_failure_as_a_wrong_password()
    {
        using var client = SsoTestFlow.CreateClient(factory);

        using var unknownUser = await PostLocalLoginAsync(client, UnknownNationalId, SsoTestFlow.TestPassword);
        Assert.Equal(HttpStatusCode.OK, unknownUser.StatusCode);
        Assert.False(unknownUser.Headers.Contains("Set-Cookie"));
        Assert.Contains(OnPage(GenericFailureMessage), await unknownUser.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Single(AuditEvents(AuditActions.AuthenticationLocalSignInFailed, LastCorrelationId));

        // The very same generic message as the wrong-password case: no enumeration.
        var nationalId = SsoTestFlow.NextNationalId();
        await SsoTestFlow.CreateUserAsync(factory, nationalId, "کاربر آزمایشی", LocalUserKind.Staff);
        using var wrongPassword = await PostLocalLoginAsync(client, nationalId, "WrongPassword1234");
        Assert.Equal(HttpStatusCode.OK, wrongPassword.StatusCode);
        Assert.Contains(OnPage(GenericFailureMessage), await wrongPassword.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_malformed_national_id_is_rejected_with_the_same_generic_failure()
    {
        using var client = SsoTestFlow.CreateClient(factory);

        using var response = await PostLocalLoginAsync(client, "123", SsoTestFlow.TestPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Contains(OnPage(GenericFailureMessage), await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var audit = Assert.Single(AuditEvents(AuditActions.AuthenticationLocalSignInFailed, LastCorrelationId));
        Assert.False(SsoTestFlow.AuditEquals(audit, "123"),
            "The malformed value must never be echoed into the audit (AGENTS §14).");
    }

    [Fact]
    public async Task A_persian_digit_national_id_is_normalized_before_the_lookup()
    {
        var nationalId = SsoTestFlow.NextNationalId();
        await SsoTestFlow.CreateUserAsync(factory, nationalId, "کاربر آزمایشی", LocalUserKind.Staff);
        using var client = SsoTestFlow.CreateClient(factory);

        var persianDigits = string.Concat(nationalId.Select(
            c => (char)('۰' + (c - '0'))));

        using var response = await PostLocalLoginAsync(client, persianDigits, SsoTestFlow.TestPassword);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task A_removed_account_cannot_use_the_local_login_path()
    {
        var nationalId = SsoTestFlow.NextNationalId();
        var user = await SsoTestFlow.CreateUserAsync(factory, nationalId, "کاربر آزمایشی", LocalUserKind.Staff);
        using var client = SsoTestFlow.CreateClient(factory);

        // The current stand-in for deactivation: the account no longer exists /
        // its sessions are dead via stamp validation (the dedicated IsActive flag
        // arrives with the provisioning slice of ADR-0002).
        factory.Users.Remove(user);

        using var response = await PostLocalLoginAsync(client, nationalId, SsoTestFlow.TestPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Contains(OnPage(GenericFailureMessage), await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Single(AuditEvents(AuditActions.AuthenticationLocalSignInFailed, LastCorrelationId));
    }

    [Fact]
    public async Task Repeated_failed_passwords_lock_the_account_and_the_correct_password_then_fails_generically()
    {
        var nationalId = SsoTestFlow.NextNationalId();
        var user = await SsoTestFlow.CreateUserAsync(factory, nationalId, "کاربر آزمایشی", LocalUserKind.Staff);
        using var client = SsoTestFlow.CreateClient(factory);

        var (cookieToken, requestToken) = await SsoTestFlow.MintAnonymousAntiforgeryTokensAsync(factory.Services);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failed = await SendLocalLoginAsync(
                client, nationalId, "WrongPassword1234", cookieToken, requestToken);
            Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        }

        var userManager = factory.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var locked = await userManager.FindByNameAsync(nationalId);
        Assert.NotNull(locked);
        Assert.True(await userManager.IsLockedOutAsync(locked!),
            "The decided policy (5 attempts / 15 minutes) must lock the account.");

        // Even the CORRECT password must fail while locked out — generically.
        using var whileLocked = await SendLocalLoginAsync(
            client, nationalId, SsoTestFlow.TestPassword, cookieToken, requestToken);
        Assert.Equal(HttpStatusCode.OK, whileLocked.StatusCode);
        Assert.False(whileLocked.Headers.Contains("Set-Cookie"));
        Assert.Contains(OnPage(GenericFailureMessage), await whileLocked.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var audit = Assert.Single(AuditEvents(
            AuditActions.AuthenticationLocalSignInFailed, LastCorrelationId,
            summary: "ورود محلی به دلیل قفل بودن حساب ناموفق بود."));
        Assert.True(SsoTestFlow.AuditEquals(audit, locked!.Id), "The locked-out attempt is audited against the real account.");
    }

    [Fact]
    public async Task Local_login_never_contacts_the_university_sso_provider()
    {
        var nationalId = SsoTestFlow.NextNationalId();
        await SsoTestFlow.CreateUserAsync(factory, nationalId, "کاربر آزمایشی", LocalUserKind.Administrator);
        using var client = SsoTestFlow.CreateClient(factory);

        var requestsBefore = factory.FakeIdP.RequestCount;

        using var response = await PostLocalLoginAsync(client, nationalId, SsoTestFlow.TestPassword);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(requestsBefore == factory.FakeIdP.RequestCount,
            "The local authentication path must not call the university SSO provider (ADR-0015 §10).");
    }

    [Fact]
    public async Task Sso_and_local_authentication_resolve_to_the_same_application_user_with_the_same_access()
    {
        var nationalId = SsoTestFlow.NextNationalId();
        var user = await SsoTestFlow.CreateUserAsync(factory, nationalId, "کاربر آزمایشی", LocalUserKind.Staff);

        // Path 1: university SSO.
        using var ssoClient = SsoTestFlow.CreateClient(factory);
        var ssoSession = await SsoTestFlow.SignInAsync(factory, ssoClient, nationalId);
        var ssoAudit = Assert.Single(SsoTestFlow.AuditEvents(
            factory, AuditActions.AuthenticationSignInSucceeded, ssoSession.CorrelationId));

        // Path 2: local credentials.
        using var localClient = SsoTestFlow.CreateClient(factory);
        using var localResponse = await PostLocalLoginAsync(localClient, nationalId, SsoTestFlow.TestPassword);
        Assert.Equal(HttpStatusCode.Redirect, localResponse.StatusCode);
        var localAudit = Assert.Single(AuditEvents(AuditActions.AuthenticationLocalSignInSucceeded, LastCorrelationId));

        // ADR-0015 §3: exactly one ApplicationUser per person, and both sessions
        // were issued for that same user.
        Assert.Single(factory.Users.All, candidate => candidate.UserName == nationalId);
        Assert.Equal(user.Id, AuditEntityId(ssoAudit));
        Assert.Equal(user.Id, AuditEntityId(localAudit));

        // Same authorization outcome: both sessions reach the protected root.
        using (var viaSso = await ssoClient.SendAsync(
                   SsoTestFlow.Authenticated(HttpMethod.Get, "/", ssoSession.AuthCookie)))
        {
            Assert.Equal(HttpStatusCode.OK, viaSso.StatusCode);
        }

        var localCookie = SsoTestFlow.ExtractCookieValue(localResponse, "GraduateRecords.Auth");
        using var viaLocal = await localClient.SendAsync(SsoTestFlow.Authenticated(HttpMethod.Get, "/", localCookie));
        Assert.Equal(HttpStatusCode.OK, viaLocal.StatusCode);

        // ADR-0015 §5: the session principal is always built by the same claims
        // factory regardless of the authentication path, so the path cannot grant
        // or remove roles, permissions or claims.
        var principalFactory = factory.Services
            .GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        Assert.IsType<ApplicationUserClaimsPrincipalFactory>(principalFactory);

        var signInManager = factory.Services.GetRequiredService<SignInManager<ApplicationUser>>();
        Assert.IsType<ApplicationUserClaimsPrincipalFactory>(signInManager.ClaimsFactory);
    }

    [Fact]
    public async Task Local_sign_out_clears_the_session_without_an_sso_end_session_redirect()
    {
        var nationalId = SsoTestFlow.NextNationalId();
        var user = await SsoTestFlow.CreateUserAsync(factory, nationalId, "کاربر آزمایشی", LocalUserKind.Staff);
        using var client = SsoTestFlow.CreateClient(factory);
        using var signIn = await PostLocalLoginAsync(client, nationalId, SsoTestFlow.TestPassword);
        var cookie = SsoTestFlow.ExtractCookieValue(signIn, "GraduateRecords.Auth");

        var (cookieToken, requestToken) = await SsoTestFlow.MintAntiforgeryTokensAsync(factory, user);
        var correlationId = "test-" + Guid.NewGuid().ToString("N");
        using var post = new HttpRequestMessage(HttpMethod.Post, "/Account/Logout");
        post.Headers.Add("Cookie",
            $"GraduateRecords.Auth={cookie}; GraduateRecords.Antiforgery={cookieToken}");
        post.Headers.Add("X-CSRF-TOKEN", requestToken);
        post.Headers.Add("X-Correlation-Id", correlationId);
        using var response = await client.SendAsync(post);

        // ADR-0015 §15: local logout clears the local session and performs no
        // SSO end-session operation.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(AccountController.LoggedOutPath, response.Headers.Location!.ToString());

        var deletion = response.Headers.GetValues("Set-Cookie").First(
            value => value.StartsWith("GraduateRecords.Auth=", StringComparison.Ordinal));
        Assert.Contains("expires=", deletion, StringComparison.OrdinalIgnoreCase);

        // ADR-0015 §16: the sign-out audit distinguishes the Local path.
        var audit = Assert.Single(SsoTestFlow.AuditEvents(
            factory, AuditActions.AuthenticationSignedOut, correlationId));
        Assert.True(audit.Properties.TryGetValue("AuditDetails", out var details)
                    && details is ScalarValue { Value: string detailsJson }
                    && detailsJson.Contains("\"Method\":\"local\"", StringComparison.Ordinal),
            "The sign-out audit of a local session must carry the local method marker.");
    }

    [Fact]
    public async Task The_login_page_offers_both_authentication_paths()
    {
        using var client = SsoTestFlow.CreateClient(factory);
        using var response = await client.GetAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Location"), "The choice page must not issue an implicit challenge.");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ورود از طریق سازمان دانشگاه", body, StringComparison.Ordinal);
        Assert.Contains("ورود کارکنان و مدیران", body, StringComparison.Ordinal);
        Assert.Contains("دانشجویان تنها از طریق ورود سازمانی", body, StringComparison.Ordinal);
        Assert.Contains("/Account/SsoSignIn", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_local_login_post_requires_an_antiforgery_token()
    {
        using var client = SsoTestFlow.CreateClient(factory);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Account/LocalLogin")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["NationalId"] = UnknownNationalId,
                ["Password"] = SsoTestFlow.TestPassword,
            }),
        };
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); // CSRF (AGENTS §18)
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Local_login_never_redirects_to_an_external_return_url()
    {
        var nationalId = SsoTestFlow.NextNationalId();
        await SsoTestFlow.CreateUserAsync(factory, nationalId, "کاربر آزمایشی", LocalUserKind.Staff);
        using var client = SsoTestFlow.CreateClient(factory);

        // Attacker supplies an absolute URL in the posted ReturnUrl field.
        using var tampered = await PostLocalLoginAsync(
            client, nationalId, SsoTestFlow.TestPassword, returnUrl: "https://evil.example");

        // The account still authenticates; only the redirect target is neutralised
        // (AccountController.SafeReturnUrl -> Url.IsLocalUrl fails -> "/").
        Assert.Equal(HttpStatusCode.Redirect, tampered.StatusCode);
        var tamperedLocation = tampered.Headers.Location!.ToString();
        Assert.Equal("/", tamperedLocation);
        Assert.DoesNotContain("evil.example", tamperedLocation, StringComparison.Ordinal);
        Assert.True(
            tampered.Headers.TryGetValues("Set-Cookie", out var tamperedCookies)
            && tamperedCookies.Any(value => value.StartsWith("GraduateRecords.Auth=", StringComparison.Ordinal)),
            "Authentication must still succeed; only the redirect target is defended.");

        // Positive control: a host-relative URL is still honoured, so the defence
        // discriminates instead of blindly returning "/".
        using var local = await PostLocalLoginAsync(
            client, nationalId, SsoTestFlow.TestPassword, returnUrl: "/Home/Index");
        Assert.Equal(HttpStatusCode.Redirect, local.StatusCode);
        Assert.Equal("/Home/Index", local.Headers.Location!.ToString());
    }

    [Fact]
    public async Task The_login_page_never_offers_an_external_return_url_as_a_redirect_target()
    {
        using var client = SsoTestFlow.CreateClient(factory);

        // Tampered absolute URL: the page itself must not redirect anywhere.
        using var external = await client.GetAsync(
            "/Account/Login?returnUrl=" + Uri.EscapeDataString("https://evil.example"));

        Assert.Equal(HttpStatusCode.OK, external.StatusCode);
        Assert.False(external.Headers.Contains("Location"),
            "The choice page must not redirect anywhere.");

        var externalBody = await external.Content.ReadAsStringAsync();

        // Post-fix behavior: AccountController.Login drops the raw returnUrl entry
        // from ModelState, so the page no longer echoes the external value — the
        // hidden ReturnUrl input renders empty, and the SSO entry link (built from
        // the sanitized view model, null for absolute URLs) carries no returnUrl
        // parameter at all. Defense in depth: even if a value were submitted back,
        // LocalLogin re-validates through SafeReturnUrl and falls back to "/" —
        // proven end-to-end by Local_login_never_redirects_to_an_external_return_url.
        Assert.DoesNotContain("evil.example", externalBody, StringComparison.Ordinal);

        var ssoLink = System.Text.RegularExpressions.Regex.Match(
            externalBody, "href=\"([^\"]*SsoSignIn[^\"]*)\"");
        Assert.True(ssoLink.Success, "The SSO entry link must be rendered.");
        Assert.DoesNotContain("returnUrl", ssoLink.Groups[1].Value, StringComparison.OrdinalIgnoreCase);

        // Positive control: a host-relative URL is preserved as the hidden value.
        using var local = await client.GetAsync("/Account/Login?returnUrl=/Home/Index");
        Assert.Equal(HttpStatusCode.OK, local.StatusCode);
        var localBody = await local.Content.ReadAsStringAsync();
        Assert.Contains("value=\"/Home/Index\"", localBody, StringComparison.Ordinal);

        var localSsoLink = System.Text.RegularExpressions.Regex.Match(
            localBody, "href=\"([^\"]*SsoSignIn[^\"]*)\"");
        Assert.True(localSsoLink.Success, "The SSO entry link must be rendered.");
        Assert.Contains("returnUrl", localSsoLink.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task There_is_no_public_registration_endpoint()
    {
        var usersBefore = factory.Users.All.Count;
        using var client = SsoTestFlow.CreateClient(factory);

        foreach (var path in new[] { "/Account/Register", "/Register", "/api/users" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["NationalId"] = UnknownNationalId,
                    ["Password"] = SsoTestFlow.TestPassword,
                }),
            };
            using var response = await client.SendAsync(request);

            Assert.True(
                response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound,
                $"Unexpected status {(int)response.StatusCode} for {path}: no registration endpoint may exist (ADR-0015 §7).");
            Assert.False(response.Headers.Contains("Set-Cookie"));
        }

        Assert.Equal(usersBefore, factory.Users.All.Count);
    }

    [Fact]
    public async Task The_removed_development_login_endpoint_is_not_available()
    {
        using var client = SsoTestFlow.CreateClient(factory);

        using var response = await client.GetAsync("/dev/login/dev-test");

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound,
            $"Unexpected status {(int)response.StatusCode} for a route that must not exist (ADR-0015 §13).");
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public void Local_sso_and_break_glass_audit_paths_are_distinguishable()
    {
        // ADR-0015 §16: sign-in actions differ per path. Break-glass remains a
        // separate, not-yet-implemented emergency mechanism (§11) — no ordinary
        // local event shares a name with it, and no break-glass action exists yet,
        // so ordinary local login can never be recorded as break-glass.
        Assert.NotEqual(AuditActions.AuthenticationSignInSucceeded, AuditActions.AuthenticationLocalSignInSucceeded);
        Assert.NotEqual(AuditActions.AuthenticationSignInFailed, AuditActions.AuthenticationLocalSignInFailed);

        Assert.DoesNotContain(
            typeof(AuditActions).GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static),
            field => field.Name.Contains("BreakGlass", StringComparison.Ordinal));
    }

    private string? LastCorrelationId { get; set; }

    private async Task<HttpResponseMessage> PostLocalLoginAsync(
        HttpClient client, string nationalId, string password, string? returnUrl = null)
    {
        var (cookieToken, requestToken) = await SsoTestFlow.MintAnonymousAntiforgeryTokensAsync(factory.Services);
        return await SendLocalLoginAsync(client, nationalId, password, cookieToken, requestToken, returnUrl);
    }

    private async Task<HttpResponseMessage> SendLocalLoginAsync(
        HttpClient client,
        string nationalId,
        string password,
        string cookieToken,
        string requestToken,
        string? returnUrl = null)
    {
        var correlationId = "test-" + Guid.NewGuid().ToString("N");
        LastCorrelationId = correlationId;
        using var request = SsoTestFlow.LocalLogin(
            nationalId, password, cookieToken, requestToken, correlationId, returnUrl);
        return await client.SendAsync(request);
    }

    private IReadOnlyList<LogEvent> AuditEvents(
        string action, string? correlationId = null, string? summary = null) =>
        SsoTestFlow.AuditEvents(factory, action, correlationId)
            .Where(logEvent => summary is null
                               || (logEvent.Properties.TryGetValue("AuditSummary", out var value)
                                   && value is ScalarValue { Value: string text }
                                   && text == summary))
            .ToList();

    private static string? AuditEntityId(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("EntityId", out var value)
        && value is ScalarValue { Value: string id }
            ? id
            : null;
}
