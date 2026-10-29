using System.Net;

namespace GraduateRecords.Tests.Integration;

/// <summary>
/// AGENTS §18: the login endpoints are rate limited per client IP with the
/// decided budget of 10 requests per minute. Every test uses its own IP
/// partition (<see cref="TestClientIpStartupFilter"/>) so tests never exhaust
/// each other.
/// </summary>
[Collection(WebApplicationCollection.Name)]
public sealed class LoginRateLimitTests(GraduateRecordsWebApplicationFactory factory)
{
    [Fact]
    public async Task The_eleventh_login_request_in_a_minute_is_rejected_with_a_persian_message()
    {
        using var client = SsoTestFlow.CreateClient(factory); // unique IP -> fresh partition

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var allowed = await client.GetAsync("/Account/Login"); // login choice page
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var limited = await client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        var body = await limited.Content.ReadAsStringAsync();
        Assert.Contains("محدودیت تعداد درخواست", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_local_credential_login_post_is_rate_limited_with_the_same_policy()
    {
        using var client = SsoTestFlow.CreateClient(factory); // unique IP -> fresh partition

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var allowed = await client.GetAsync("/Account/Login"); // consume the partition
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        // The rate limiter runs before antiforgery/mvc, so no token is needed to observe the limit.
        using var limited = await client.SendAsync(
            SsoTestFlow.LocalLogin("4276067103", "irrelevant-password-1234", "cookie", "token", "test-" + Guid.NewGuid().ToString("N")));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }
}
