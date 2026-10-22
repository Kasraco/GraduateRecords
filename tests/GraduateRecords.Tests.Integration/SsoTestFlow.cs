using System.Net;
using GraduateRecords.Infrastructure.Identity;
using GraduateRecords.Web.Middleware;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;

namespace GraduateRecords.Tests.Integration;

/// <summary>A local session established through the fake SSO provider.</summary>
public sealed record SignInSession(string AuthCookie, string RawSetCookieHeader, string CorrelationId);

/// <summary>
/// Shared helpers: unique per-test client IPs (rate-limiter partitions), user
/// provisioning, the browser-side OIDC handshake against the TestServer, and
/// audit-log assertions. Cookies are handled manually so Secure cookies work
/// over the TestServer's http address deterministically.
/// </summary>
public static class SsoTestFlow
{
    private static int _clientIpCounter;
    private static int _nationalIdCounter;

    /// <summary>A unique documentation-range IP per test (198.51.100.x).</summary>
    public static string NextClientIp() => $"198.51.100.{Interlocked.Increment(ref _clientIpCounter)}";

    /// <summary>
    /// A unique checksum-valid National ID per call, so tests sharing the factory's
    /// in-memory user store never collide (algorithm as in Unit/NationalIdTests).
    /// </summary>
    public static string NextNationalId()
    {
        var nineDigits = "4" + Interlocked.Increment(ref _nationalIdCounter).ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
        var sum = 0;
        for (var index = 0; index < nineDigits.Length; index++)
        {
            sum += (nineDigits[index] - '0') * (10 - index);
        }

        var remainder = sum % 11;
        var checkDigit = remainder < 2 ? remainder : 11 - remainder;
        return nineDigits + checkDigit;
    }

    public static HttpClient CreateClient(GraduateRecordsWebApplicationFactory factory, string? clientIp = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        client.DefaultRequestHeaders.Add(TestClientIpStartupFilter.HeaderName, clientIp ?? NextClientIp());
        return client;
    }

    public static async Task<ApplicationUser> CreateUserAsync(
        GraduateRecordsWebApplicationFactory factory, string nationalId, string fullName)
    {
        var userManager = factory.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = nationalId,
            NationalId = nationalId,
            FullName = fullName,
            LockoutEnabled = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
        };

        // 18 characters: valid under the decided policy (min 14, no composition rules).
        var result = await userManager.CreateAsync(user, "IntegrationTest1234");
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Code)));
        return user;
    }

    /// <summary>
    /// Runs the browser half of the handshake: challenge to the SSO authorize
    /// endpoint, then the callback redirect with the state and correlation cookie.
    /// Returns the raw callback response plus the correlation id of that request,
    /// which the caller uses to scope audit assertions to this flow only.
    /// </summary>
    public static async Task<(HttpResponseMessage Response, string CorrelationId)> AuthenticateAsync(
        GraduateRecordsWebApplicationFactory factory, HttpClient client, string ssoNationalId)
    {
        factory.FakeIdP.UserInfoNationalId = ssoNationalId;

        using var challenge = await client.GetAsync("/Account/Login", HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);

        var authorizeUri = challenge.Headers.Location!;
        Assert.Equal(FakeOpenIdProvider.Authority, authorizeUri.GetLeftPart(UriPartial.Authority));

        var authorizeQuery = QueryHelpers.ParseQuery(authorizeUri.Query);
        Assert.Equal("code", authorizeQuery["response_type"].ToString());
        Assert.True(authorizeQuery.ContainsKey("code_challenge"), "PKCE code_challenge missing from the authorize request.");
        factory.FakeIdP.NextNonce = authorizeQuery["nonce"].ToString();

        var correlationId = "test-" + Guid.NewGuid().ToString("N");
        var callback = new HttpRequestMessage(HttpMethod.Get,
            "/signin-oidc?code=fake-authorization-code&state=" + Uri.EscapeDataString(authorizeQuery["state"].ToString()));
        callback.Headers.Add("Cookie", CookieHeader(challenge));
        callback.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId);
        return (await client.SendAsync(callback), correlationId);
    }

    /// <summary>Completes the handshake and returns the local session cookie.</summary>
    public static async Task<SignInSession> SignInAsync(
        GraduateRecordsWebApplicationFactory factory, HttpClient client, string ssoNationalId)
    {
        var (callback, correlationId) = await AuthenticateAsync(factory, client, ssoNationalId);
        using (callback)
        {
            Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
            Assert.Equal("/", callback.Headers.Location!.ToString());
            Assert.True(factory.FakeIdP.TokenRequestBodies.Any(
                    body => body.Contains("code_verifier=", StringComparison.Ordinal)),
                "The token exchange must carry the PKCE code_verifier.");

            var rawSetCookie = callback.Headers.GetValues("Set-Cookie")
                .First(value => value.StartsWith("GraduateRecords.Auth=", StringComparison.Ordinal));
            return new SignInSession(
                ExtractCookieValue(callback, "GraduateRecords.Auth"), rawSetCookie, correlationId);
        }
    }

    public static HttpRequestMessage Authenticated(HttpMethod method, string path, string authCookie)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", "GraduateRecords.Auth=" + authCookie);
        return request;
    }

    /// <summary>Mints an antiforgery token pair exactly like a rendered form would carry.</summary>
    public static async Task<(string CookieToken, string RequestToken)> MintAntiforgeryTokensAsync(
        GraduateRecordsWebApplicationFactory factory, ApplicationUser user)
    {
        using var scope = factory.Services.CreateScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        var principalFactory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        httpContext.User = await principalFactory.CreateAsync(user);

        var antiforgery = scope.ServiceProvider.GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(httpContext);

        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();
        var cookieToken = ParseCookiePair(setCookieHeader, "GraduateRecords.Antiforgery");
        Assert.NotNull(cookieToken);
        return (cookieToken!, tokens.RequestToken!);
    }

    /// <summary>
    /// Audit log events with the given action name (AGENTS §14). When a correlation
    /// id is given, only events produced by that exact request are returned, so
    /// tests sharing the collection factory never see each other's audit entries.
    /// </summary>
    public static IReadOnlyList<LogEvent> AuditEvents(
        GraduateRecordsWebApplicationFactory factory, string action, string? correlationId = null) =>
        factory.Logs.Snapshot()
            .Where(logEvent => logEvent.Properties.TryGetValue("AuditAction", out var value)
                               && value is ScalarValue { Value: string name }
                               && name == action)
            .Where(logEvent => correlationId is null
                               || (logEvent.Properties.TryGetValue("CorrelationId", out var correlation)
                                   && correlation is ScalarValue { Value: string logged }
                                   && logged == correlationId))
            .ToList();

    /// <summary>True when a string property of the audit event contains the given text.</summary>
    public static bool AuditContains(LogEvent logEvent, string text) =>
        logEvent.Properties.Values
            .OfType<ScalarValue>()
            .Any(value => value.Value is string property && property.Contains(text, StringComparison.Ordinal));

    /// <summary>True when a string property of the audit event equals the given text exactly.</summary>
    public static bool AuditEquals(LogEvent logEvent, string value) =>
        logEvent.Properties.Values
            .OfType<ScalarValue>()
            .Any(scalar => scalar.Value is string property && property == value);

    private static string CookieHeader(HttpResponseMessage response)
    {
        IEnumerable<string> pairs = response.Headers.TryGetValues("Set-Cookie", out var setCookies)
            ? setCookies.Select(cookie => cookie.Split(';', 2)[0])
            : [];
        return string.Join("; ", pairs);
    }

    private static string ExtractCookieValue(HttpResponseMessage response, string name)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookies),
            $"No Set-Cookie header; expected the '{name}' cookie.");

        var pair = setCookies.Select(cookie => ParseCookiePair(cookie, name)).FirstOrDefault(value => value is not null);
        Assert.True(pair is not null, $"Cookie '{name}' was not set on the response.");
        return pair!;
    }

    private static string? ParseCookiePair(string setCookieHeader, string name)
    {
        foreach (var segment in setCookieHeader.Split(';'))
        {
            var pair = segment.Trim();
            if (pair.StartsWith(name + "=", StringComparison.Ordinal))
            {
                return pair[(name.Length + 1)..];
            }
        }

        return null;
    }
}
