using System.Net;
using GraduateRecords.Domain.Auditing;

namespace GraduateRecords.Tests.Integration;

[Collection(WebApplicationCollection.Name)]
public sealed class SignOutTests(GraduateRecordsWebApplicationFactory factory)
{
    [Fact]
    public async Task Logout_without_an_antiforgery_token_is_rejected()
    {
        var nationalId = SsoTestFlow.NextNationalId();
        await SsoTestFlow.CreateUserAsync(factory, nationalId, "کاربر آزمایشی");
        using var client = SsoTestFlow.CreateClient(factory);
        var session = await SsoTestFlow.SignInAsync(factory, client, nationalId);

        var correlationId = "test-" + Guid.NewGuid().ToString("N");
        using var post = new HttpRequestMessage(HttpMethod.Post, "/Account/Logout");
        post.Headers.Add("Cookie", "GraduateRecords.Auth=" + session.AuthCookie);
        post.Headers.Add("X-Correlation-Id", correlationId);
        using var response = await client.SendAsync(post);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); // CSRF (AGENTS §18)
        Assert.Empty(SsoTestFlow.AuditEvents(
            factory, AuditActions.AuthenticationSignedOut, correlationId));
    }

    [Fact]
    public async Task Logout_clears_the_local_cookie_audits_and_reaches_the_sso_end_session()
    {
        var nationalId = SsoTestFlow.NextNationalId();
        var user = await SsoTestFlow.CreateUserAsync(factory, nationalId, "کاربر آزمایشی");
        using var client = SsoTestFlow.CreateClient(factory);
        var session = await SsoTestFlow.SignInAsync(factory, client, nationalId);

        var (cookieToken, requestToken) = await SsoTestFlow.MintAntiforgeryTokensAsync(factory, user);

        var correlationId = "test-" + Guid.NewGuid().ToString("N");
        using var post = new HttpRequestMessage(HttpMethod.Post, "/Account/Logout");
        post.Headers.Add("Cookie",
            $"GraduateRecords.Auth={session.AuthCookie}; GraduateRecords.Antiforgery={cookieToken}");
        post.Headers.Add("X-CSRF-TOKEN", requestToken);
        post.Headers.Add("X-Correlation-Id", correlationId);
        using var response = await client.SendAsync(post);

        // ADR-0001 rule 14 / ADR-0008 §5.5: local cookie cleared, then SSO end session.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(FakeOpenIdProvider.Authority + "/signout",
            response.Headers.Location!.ToString(), StringComparison.Ordinal);

        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookies));
        var deletion = setCookies.FirstOrDefault(
            cookie => cookie.StartsWith("GraduateRecords.Auth=", StringComparison.Ordinal));
        Assert.NotNull(deletion);
        Assert.Contains("expires=", deletion!, StringComparison.OrdinalIgnoreCase);

        var audit = Assert.Single(SsoTestFlow.AuditEvents(
            factory, AuditActions.AuthenticationSignedOut, correlationId));
        Assert.False(SsoTestFlow.AuditContains(audit, nationalId),
            "The sign-out audit must not carry the National ID (AGENTS §14).");
    }
}
