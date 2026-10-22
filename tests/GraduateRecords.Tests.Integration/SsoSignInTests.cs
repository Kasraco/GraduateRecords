using System.Net;
using GraduateRecords.Domain.Auditing;
using GraduateRecords.Web.Controllers;
using Serilog.Events;

namespace GraduateRecords.Tests.Integration;

[Collection(WebApplicationCollection.Name)]
public sealed class SsoSignInTests(GraduateRecordsWebApplicationFactory factory)
{
    // Checksum-valid but never provisioned (see Unit/NationalIdTests for the algorithm).
    private const string UnknownNationalId = "4276067103";

    [Fact]
    public async Task A_known_sso_user_signs_in_and_receives_the_local_session_cookie()
    {
        var nationalId = SsoTestFlow.NextNationalId();
        await SsoTestFlow.CreateUserAsync(factory, nationalId, "کاربر آزمایشی");
        using var client = SsoTestFlow.CreateClient(factory);

        var session = await SsoTestFlow.SignInAsync(factory, client, nationalId);

        // ADR-0008 §5.1: fixed name, HttpOnly, Secure, SameSite=Lax.
        Assert.Contains("GraduateRecords.Auth=", session.RawSetCookieHeader, StringComparison.Ordinal);
        Assert.Contains("httponly", session.RawSetCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", session.RawSetCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", session.RawSetCookieHeader, StringComparison.OrdinalIgnoreCase);

        using var home = await client.SendAsync(SsoTestFlow.Authenticated(HttpMethod.Get, "/", session.AuthCookie));
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);

        // Successful sign-in is audited, and the National ID never appears in the audit.
        var audit = Assert.Single(SsoTestFlow.AuditEvents(
            factory, AuditActions.AuthenticationSignInSucceeded, session.CorrelationId));
        Assert.False(SsoTestFlow.AuditContains(audit, nationalId),
            "The audit event must not contain the National ID (AGENTS §14).");
    }

    [Fact]
    public async Task An_unknown_national_id_is_denied_without_leaking_whether_the_account_exists()
    {
        using var client = SsoTestFlow.CreateClient(factory);

        var (callback, correlationId) = await SsoTestFlow.AuthenticateAsync(factory, client, UnknownNationalId);
        using (callback)
        {
            Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
            Assert.Equal(AccountController.LoginFailedPath, callback.Headers.Location!.ToString());
            Assert.False(callback.Headers.TryGetValues("Set-Cookie", out var setCookies)
                         && setCookies.Any(cookie => cookie.StartsWith("GraduateRecords.Auth=", StringComparison.Ordinal)),
                "No session cookie may be issued for a denied sign-in.");
        }

        var audit = Assert.Single(SsoTestFlow.AuditEvents(
            factory, AuditActions.AuthenticationUnknownUserDenied, correlationId));
        Assert.False(SsoTestFlow.AuditContains(audit, UnknownNationalId),
            "The denied audit event must not carry the National ID (AGENTS §14).");

        // The failure page is generic (no user enumeration, AGENTS §18).
        using var page = await client.GetAsync(AccountController.LoginFailedPath);
        var body = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("ورود ناموفق", body, StringComparison.Ordinal);
        Assert.DoesNotContain(UnknownNationalId, body, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeOpenIdProvider.Authority, body, StringComparison.Ordinal);

        Assert.Empty(SsoTestFlow.AuditEvents(
            factory, AuditActions.AuthenticationSignInSucceeded, correlationId));
    }

    [Fact]
    public async Task A_malformed_national_id_claim_is_denied_and_audited_by_category()
    {
        using var client = SsoTestFlow.CreateClient(factory);

        var (callback, correlationId) = await SsoTestFlow.AuthenticateAsync(factory, client, "123");
        using (callback)
        {
            Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
            Assert.Equal(AccountController.LoginFailedPath, callback.Headers.Location!.ToString());
        }

        var audit = Assert.Single(SsoTestFlow.AuditEvents(
            factory, AuditActions.AuthenticationInvalidNationalId, correlationId));
        Assert.False(SsoTestFlow.AuditEquals(audit, "123"),
            "The malformed claim value must not be echoed into the audit (AGENTS §14).");
        Assert.True(!audit.Properties.TryGetValue("AuditDetails", out var details)
                    || details is ScalarValue { Value: null },
            "The invalid-claim audit must carry no details payload (AGENTS §14).");
    }

    [Fact]
    public async Task An_sso_protocol_failure_is_audited_by_category_and_shows_the_generic_page()
    {
        using var client = SsoTestFlow.CreateClient(factory);

        // A callback with a forged state never correlates to a challenge.
        var correlationId = "test-" + Guid.NewGuid().ToString("N");
        var request = new HttpRequestMessage(HttpMethod.Get, "/signin-oidc?code=x&state=forged-state");
        request.Headers.Add("X-Correlation-Id", correlationId);
        using var callback = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal(AccountController.LoginFailedPath, callback.Headers.Location!.ToString());
        Assert.Single(SsoTestFlow.AuditEvents(factory, AuditActions.AuthenticationSignInFailed, correlationId));
        Assert.Empty(SsoTestFlow.AuditEvents(factory, AuditActions.AuthenticationSignInSucceeded, correlationId));
    }
}
