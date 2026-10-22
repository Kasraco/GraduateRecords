using System.Net;

namespace GraduateRecords.Tests.Integration;

/// <summary>
/// ADR-0001 rule 12 / break-glass decision: the mandatory MFA for the emergency
/// administrator is not decided, so NO local password login exists at all
/// (fail-closed). Sign-in always goes to the university SSO.
/// </summary>
[Collection(WebApplicationCollection.Name)]
public sealed class NoPasswordLoginTests(GraduateRecordsWebApplicationFactory factory)
{
    [Fact]
    public async Task Login_always_challenges_the_sso_provider_and_never_renders_a_password_form()
    {
        using var client = SsoTestFlow.CreateClient(factory);

        using var response = await client.GetAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(FakeOpenIdProvider.Authority + "/authorize",
            response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task There_is_no_local_password_signin_endpoint()
    {
        using var client = SsoTestFlow.CreateClient(factory);

        // The Login action is GET-only (SSO challenge). A credential POST never
        // reaches an action: the authenticated-by-default fallback policy (AGENTS
        // §13) challenges it first, so it fails closed with 401 and no cookie.
        using var post = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        using var response = await client.SendAsync(post);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var setCookies)
                     && setCookies.Any(cookie => cookie.StartsWith("GraduateRecords.Auth=", StringComparison.Ordinal)),
            "A credential POST must never issue a session cookie.");
    }
}
