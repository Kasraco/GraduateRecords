using System.Net;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace GraduateRecords.Tests.Integration;

/// <summary>
/// Shared arrange/act helpers for the Phase 1A UC3 administration tests:
/// provisioning users with canonical roles, establishing an SSO session for
/// them, and sending cookie + antiforgery-carrying form posts the same way a
/// browser would.
/// </summary>
internal static class AdministrationTestFlow
{
    /// <summary>
    /// Creates a user and assigns the given canonical roles. Roles must already
    /// exist — the startup reconciliation seeds all three (ADR-0017 §8).
    /// </summary>
    public static async Task<ApplicationUser> CreateUserAsync(
        GraduateRecordsWebApplicationFactory factory,
        string fullName,
        LocalUserKind kind,
        params string[] roles)
    {
        var user = await SsoTestFlow.CreateUserAsync(
            factory, SsoTestFlow.NextNationalId(), fullName, kind);

        if (roles.Length == 0)
        {
            return user;
        }

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var roleName in roles)
        {
            var result = await userManager.AddToRoleAsync(user, roleName);
            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Code)));
        }

        return user;
    }

    /// <summary>Completes the fake-SSO handshake and returns the session cookie.</summary>
    public static async Task<string> SignInAsync(
        GraduateRecordsWebApplicationFactory factory,
        HttpClient client,
        ApplicationUser user)
    {
        var session = await SsoTestFlow.SignInAsync(factory, client, user.NationalId);
        return session.AuthCookie;
    }

    public static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string authCookie)
    {
        using var request = SsoTestFlow.Authenticated(HttpMethod.Get, path, authCookie);
        return await client.SendAsync(request);
    }

    /// <summary>
    /// Form post exactly as a browser renders it: session cookie, antiforgery
    /// cookie and the <c>X-CSRF-TOKEN</c> header (AGENTS §18).
    /// </summary>
    public static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string path,
        IEnumerable<KeyValuePair<string, string>> form,
        string authCookie,
        string antiforgeryCookie,
        string requestToken,
        string correlationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Add("Cookie",
            $"GraduateRecords.Auth={authCookie}; GraduateRecords.Antiforgery={antiforgeryCookie}");
        request.Headers.Add("X-CSRF-TOKEN", requestToken);
        request.Headers.Add("X-Correlation-Id", correlationId);
        return await client.SendAsync(request);
    }

    /// <summary>Mints an antiforgery pair bound to the given user's principal.</summary>
    public static Task<(string CookieToken, string RequestToken)> AntiforgeryAsync(
        GraduateRecordsWebApplicationFactory factory,
        ApplicationUser user) =>
        SsoTestFlow.MintAntiforgeryTokensAsync(factory, user);

    /// <summary>
    /// Follows a redirect with the session cookie, so a Post-Redirect-Get flow
    /// (and the TempData toast it carries) can be asserted end to end.
    /// </summary>
    public static async Task<string> FollowAsync(HttpClient client, HttpResponseMessage redirect, string authCookie)
    {
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);

        var location = redirect.Headers.Location!.IsAbsoluteUri
            ? redirect.Headers.Location.PathAndQuery
            : redirect.Headers.Location.ToString();

        // The session cookie alone is not enough: the Post-Redirect-Get must also
        // carry the TempData cookie the POST response set, or the toast it carries
        // is lost (DESIGN §8).
        var cookies = new List<string> { "GraduateRecords.Auth=" + authCookie };
        if (redirect.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var header in setCookies)
            {
                var pair = header.Split(';', 2)[0].Trim();
                if (pair.StartsWith(".AspNetCore.Mvc.CookieTempDataProvider=", StringComparison.Ordinal))
                {
                    cookies.Add(pair);
                }
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, location);
        request.Headers.Add("Cookie", string.Join("; ", cookies));
        var response = await client.SendAsync(request);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"GET {location} returned {(int)response.StatusCode}.");
        var text = await response.Content.ReadAsStringAsync();
        return text;
    }

    /// <summary>
    /// Removes the canonical Administrator role from every user except
    /// <paramref name="keep"/>, so a test can reason about a deterministic
    /// effective-administrator set (ADR-0017 §18). Test-fixture only.
    /// </summary>
    public static async Task DemoteAdministratorsExceptAsync(
        GraduateRecordsWebApplicationFactory factory,
        ApplicationUser keep)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var candidate in factory.Users.All.Where(user => user.Id != keep.Id).ToList())
        {
            if (await userManager.IsInRoleAsync(candidate, RoleNames.Administrator))
            {
                var result = await userManager.RemoveFromRoleAsync(candidate, RoleNames.Administrator);
                Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Code)));
            }
        }
    }

    /// <summary>
    /// A test-only role that carries both canonical Phase 1A permission claims
    /// (<c>Users.Manage</c> + <c>Roles.Assign</c>) without carrying the
    /// Administrator role. It lets a test reach the user/role screens as an actor
    /// who is not an effective Administrator — exactly the arrangement in which
    /// the last-Administrator invariant (ADR-0017 §17) is the only remaining
    /// guard, since §9 and §10 capabilities alone never imply role membership.
    /// Never created outside tests.
    /// </summary>
    public static async Task<ApplicationUser> CreateCapableNonAdministratorAsync(
        GraduateRecordsWebApplicationFactory factory,
        string fullName)
    {
        const string roleName = "TestRoleWithAdministrationCapabilities";

        using (var scope = factory.Services.CreateScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var existing = await roleManager.FindByNameAsync(roleName);
            if (existing is null)
            {
                var created = await roleManager.CreateAsync(new ApplicationRole
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = roleName,
                });
                Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(error => error.Code)));
            }

            var role = await roleManager.FindByNameAsync(roleName);
            var claims = await roleManager.GetClaimsAsync(role!);
            foreach (var permission in new[] { Permissions.RolesAssign, Permissions.UsersManage })
            {
                if (claims.Any(claim => claim.Type == PermissionClaims.Type && claim.Value == permission))
                {
                    continue;
                }

                var added = await roleManager.AddClaimAsync(
                    role!, new System.Security.Claims.Claim(PermissionClaims.Type, permission));
                Assert.True(added.Succeeded, string.Join(", ", added.Errors.Select(error => error.Code)));
            }
        }

        return await CreateUserAsync(factory, fullName, LocalUserKind.Staff, roleName);
    }
}
