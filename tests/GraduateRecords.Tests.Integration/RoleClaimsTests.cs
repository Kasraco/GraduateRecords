using System.Security.Claims;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GraduateRecords.Tests.Integration;

/// <summary>
/// UC2.2 — Identity role store claims path (ADR-0017 §6/§8): an authenticated
/// principal carries (1) one canonical role-name claim per actual
/// AspNetUserRoles membership and (2) the Identity role claims from
/// AspNetRoleClaims (the persisted `permission` claims), while the existing
/// `full_name` display claim is preserved. Everything is built by the real
/// ApplicationUserClaimsPrincipalFactory against the in-memory stores; no
/// seeding of canonical roles or permissions happens here (that is UC2.3).
/// </summary>
[Collection(WebApplicationCollection.Name)]
public sealed class RoleClaimsTests(GraduateRecordsWebApplicationFactory factory)
{
    [Fact]
    public async Task The_principal_carries_canonical_role_name_claims_from_actual_identity_membership()
    {
        var user = await CreateUserAsync();
        await EnsureRoleAsync(RoleNames.Administrator);
        await AddToRoleAsync(user, RoleNames.Administrator);

        var principal = await CreatePrincipalAsync(user);

        Assert.Contains(principal.Claims, claim =>
            claim.Type == ClaimTypes.Role && claim.Value == RoleNames.Administrator);
        Assert.True(principal.IsInRole(RoleNames.Administrator));
    }

    [Fact]
    public async Task Permission_claims_stored_as_identity_role_claims_reach_the_principal()
    {
        var user = await CreateUserAsync();
        var role = await EnsureRoleAsync(RoleNames.Administrator);
        await AddToRoleAsync(user, RoleNames.Administrator);
        await EnsureRoleClaimAsync(role, PermissionClaims.Type, Permissions.UsersManage);
        await EnsureRoleClaimAsync(role, PermissionClaims.Type, Permissions.RolesAssign);

        var principal = await CreatePrincipalAsync(user);

        Assert.Contains(principal.Claims, claim =>
            claim.Type == PermissionClaims.Type && claim.Value == Permissions.UsersManage);
        Assert.Contains(principal.Claims, claim =>
            claim.Type == PermissionClaims.Type && claim.Value == Permissions.RolesAssign);
    }

    [Fact]
    public async Task The_full_name_claim_is_preserved_alongside_the_role_claims()
    {
        var user = await CreateUserAsync();
        await EnsureRoleAsync(RoleNames.GraduateStaff);
        await AddToRoleAsync(user, RoleNames.GraduateStaff);

        var principal = await CreatePrincipalAsync(user);

        Assert.Equal(
            user.FullName,
            principal.FindFirst(ApplicationUserClaimsPrincipalFactory.FullNameClaimType)?.Value);
        Assert.Contains(principal.Claims, claim =>
            claim.Type == ClaimTypes.Role && claim.Value == RoleNames.GraduateStaff);
    }

    [Fact]
    public async Task A_user_without_roles_gets_no_role_or_permission_claims()
    {
        var user = await CreateUserAsync();

        var principal = await CreatePrincipalAsync(user);

        // Fail-closed baseline (ADR-0017 §21): no membership ⇒ no role-derived
        // claim of any kind, never a default or implicit grant.
        Assert.DoesNotContain(principal.Claims, claim => claim.Type == ClaimTypes.Role);
        Assert.DoesNotContain(principal.Claims, claim => claim.Type == PermissionClaims.Type);
    }

    [Fact]
    public async Task Security_stamp_validation_rebuilds_the_principal_with_roles_and_permissions()
    {
        // ADR-0008 §5.3: validation interval is zero, so the cookie's
        // OnValidatePrincipal rebuilds the principal through the factory on
        // essentially every request — role/permission changes take effect on the
        // next request without any SecurityStamp rotation. The user starts with
        // NO roles, so the original principal cannot already contain the claims
        // the rebuilt principal is asserted to gain.
        var user = await CreateUserAsync();

        // Steps 1-2: the starting principal proves this user has no role or
        // permission claims yet.
        var originalPrincipal = await CreatePrincipalAsync(user);
        Assert.DoesNotContain(originalPrincipal.Claims, claim => claim.Type == ClaimTypes.Role);
        Assert.DoesNotContain(originalPrincipal.Claims, claim => claim.Type == PermissionClaims.Type);

        // Steps 3-5: arrange membership and role claims AFTER the original
        // principal exists — this is what makes the rebuild observable.
        var role = await EnsureRoleAsync(RoleNames.Administrator);
        await AddToRoleAsync(user, RoleNames.Administrator);
        await EnsureRoleClaimAsync(role, PermissionClaims.Type, Permissions.UsersManage);

        // Steps 6-7: validate the ORIGINAL principal through the real
        // OnValidatePrincipal callback (harness unchanged).
        var context = CreateValidationContext(originalPrincipal);
        var validatePrincipal = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme).Events.OnValidatePrincipal;
        Assert.NotNull(validatePrincipal);

        await validatePrincipal(context);

        // Step 8: the rebuilt principal reflects the current Identity state.
        Assert.NotNull(context.Principal);
        Assert.True(context.ShouldRenew, "A validated session must renew with the rebuilt principal.");
        Assert.Contains(context.Principal!.Claims, claim =>
            claim.Type == ClaimTypes.Role && claim.Value == RoleNames.Administrator);
        Assert.Contains(context.Principal.Claims, claim =>
            claim.Type == PermissionClaims.Type && claim.Value == Permissions.UsersManage);
        Assert.Equal(
            user.FullName,
            context.Principal.FindFirst(ApplicationUserClaimsPrincipalFactory.FullNameClaimType)?.Value);
    }

    private async Task<ApplicationUser> CreateUserAsync()
    {
        var nationalId = SsoTestFlow.NextNationalId();
        return await SsoTestFlow.CreateUserAsync(factory, nationalId, "کاربر آزمایشی");
    }

    /// <summary>
    /// Find-or-create by canonical name: the collection factory (and therefore
    /// the in-memory role store) is shared by every test in the collection, so
    /// setup must be idempotent (ADR-0017 §2 canonical names).
    /// </summary>
    private async Task<ApplicationRole> EnsureRoleAsync(string roleName)
    {
        using var scope = factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        var existing = await roleManager.FindByNameAsync(roleName);
        if (existing is not null)
        {
            return existing;
        }

        var role = new ApplicationRole { Id = Guid.NewGuid().ToString("N"), Name = roleName };
        var result = await roleManager.CreateAsync(role);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Code)));
        return role;
    }

    private async Task AddToRoleAsync(ApplicationUser user, string roleName)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (await userManager.IsInRoleAsync(user, roleName))
        {
            return;
        }

        var result = await userManager.AddToRoleAsync(user, roleName);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Code)));
    }

    private async Task EnsureRoleClaimAsync(ApplicationRole role, string type, string value)
    {
        using var scope = factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        var existing = await roleManager.GetClaimsAsync(role);
        if (existing.Any(claim => claim.Type == type && claim.Value == value))
        {
            return;
        }

        var result = await roleManager.AddClaimAsync(role, new Claim(type, value));
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Code)));
    }

    private async Task<ClaimsPrincipal> CreatePrincipalAsync(ApplicationUser user)
    {
        using var scope = factory.Services.CreateScope();
        var principalFactory =
            scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        return await principalFactory.CreateAsync(user);
    }

    /// <summary>
    /// Builds the exact context the cookie handler passes to OnValidatePrincipal
    /// around the SUPPLIED principal (same harness as SessionSecurityTests), with
    /// a fresh absolute-cap anchor.
    /// </summary>
    private CookieValidatePrincipalContext CreateValidationContext(ClaimsPrincipal principal)
    {
        var properties = new AuthenticationProperties();
        AbsoluteSessionLifetime.Anchor(properties, DateTimeOffset.UtcNow);

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
