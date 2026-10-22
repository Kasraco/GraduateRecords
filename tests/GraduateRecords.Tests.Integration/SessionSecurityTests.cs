using System.Net;
using GraduateRecords.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GraduateRecords.Tests.Integration;

[Collection(WebApplicationCollection.Name)]
public sealed class SessionSecurityTests(GraduateRecordsWebApplicationFactory factory)
{
    [Fact]
    public async Task Deactivating_a_user_blocks_their_session_on_the_next_request()
    {
        var nationalId = SsoTestFlow.NextNationalId();
        var user = await SsoTestFlow.CreateUserAsync(factory, nationalId, "کاربر آزمایشی");
        using var client = SsoTestFlow.CreateClient(factory);
        var session = await SsoTestFlow.SignInAsync(factory, client, nationalId);

        using (var ok = await client.SendAsync(SsoTestFlow.Authenticated(HttpMethod.Get, "/", session.AuthCookie)))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        // ADR-0008 §5.3 (every-request stamp validation): removing the user (the
        // stand-in for deactivation) kills the session on the very next request.
        factory.Users.Remove(user);

        using var denied = await client.SendAsync(SsoTestFlow.Authenticated(HttpMethod.Get, "/", session.AuthCookie));
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task A_ticket_past_the_absolute_session_cap_is_rejected_by_the_cookie_events()
    {
        var context = await CreateValidationContextAsync(TimeSpan.FromHours(-9));
        var validatePrincipal = ValidatePrincipalDelegate();
        Assert.NotNull(validatePrincipal);

        await validatePrincipal(context);

        Assert.Null(context.Principal); // anchor older than the 8h cap -> rejected, fail-closed
    }

    [Fact]
    public async Task A_fresh_ticket_passes_the_cookie_validation_events()
    {
        var context = await CreateValidationContextAsync(TimeSpan.Zero);
        var validatePrincipal = ValidatePrincipalDelegate();
        Assert.NotNull(validatePrincipal);

        await validatePrincipal(context);

        Assert.NotNull(context.Principal);
    }

    private Func<CookieValidatePrincipalContext, Task>? ValidatePrincipalDelegate() =>
        factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme).Events.OnValidatePrincipal;

    /// <summary>
    /// Builds the exact context the cookie handler passes to OnValidatePrincipal,
    /// with the absolute-cap anchor set to <paramref name="anchorOffset"/> from now.
    /// </summary>
    private async Task<CookieValidatePrincipalContext> CreateValidationContextAsync(TimeSpan anchorOffset)
    {
        var nationalId = SsoTestFlow.NextNationalId();
        await SsoTestFlow.CreateUserAsync(factory, nationalId, "کاربر آزمایشی");

        var userManager = factory.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByNameAsync(nationalId))!;
        var principalFactory = factory.Services
            .GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        var principal = await principalFactory.CreateAsync(user);

        var properties = new AuthenticationProperties();
        AbsoluteSessionLifetime.Anchor(properties, DateTimeOffset.UtcNow.Add(anchorOffset));
        var ticket = new AuthenticationTicket(principal, properties, IdentityConstants.ApplicationScheme);

        var scheme = new AuthenticationScheme(
            IdentityConstants.ApplicationScheme,
            IdentityConstants.ApplicationScheme,
            typeof(CookieAuthenticationHandler));
        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        return new CookieValidatePrincipalContext(
            new DefaultHttpContext { RequestServices = factory.Services }, scheme, options, ticket);
    }
}
