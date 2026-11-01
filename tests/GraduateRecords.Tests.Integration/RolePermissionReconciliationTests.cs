using System.Security.Claims;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Infrastructure.Identity;
using GraduateRecords.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace GraduateRecords.Tests.Integration;

/// <summary>
/// UC2.3 — canonical role/permission reconciliation against the real DI
/// composition and the in-memory Identity stores (ADR-0017 §7–§8): startup
/// wiring, idempotent convergence, exact-catalog semantics (stale removal,
/// duplicate collapse, non-permission preservation, non-canonical roles
/// untouched), user/membership preservation, and fail-closed error handling
/// (AGENTS §4: failures are never swallowed and no success is reported for a
/// failed operation).
/// </summary>
[Collection(WebApplicationCollection.Name)]
public sealed class RolePermissionReconciliationTests(GraduateRecordsWebApplicationFactory factory)
{
    private static readonly string[] ExpectedAdministratorPermissions =
        [Permissions.RolesAssign, Permissions.UsersManage]; // ordinal sort

    [Fact]
    public async Task Startup_reconciliation_has_already_converged_the_canonical_catalog()
    {
        // The host booted through Program.cs, which runs the reconciliation
        // before serving — nothing below arranges roles or claims.
        var student = await FindRoleAsync(RoleNames.Student);
        var graduateStaff = await FindRoleAsync(RoleNames.GraduateStaff);
        var administrator = await FindRoleAsync(RoleNames.Administrator);

        Assert.Single(factory.Roles.All, role => role.Name == RoleNames.Student);
        Assert.Single(factory.Roles.All, role => role.Name == RoleNames.GraduateStaff);
        Assert.Single(factory.Roles.All, role => role.Name == RoleNames.Administrator);

        Assert.Empty(PermissionValues(await GetRoleClaimsAsync(student)));
        Assert.Empty(PermissionValues(await GetRoleClaimsAsync(graduateStaff)));
        Assert.Equal(ExpectedAdministratorPermissions, PermissionValues(await GetRoleClaimsAsync(administrator)));
    }

    [Fact]
    public async Task Reconciliation_is_idempotent_and_a_second_run_changes_nothing()
    {
        await RunAsync(factory);

        var rolesBefore = factory.Roles.All.Select(role => (role.Id, role.Name)).OrderBy(row => row.Id).ToList();
        var administratorBefore = PermissionValues(await GetRoleClaimsAsync(await FindRoleAsync(RoleNames.Administrator)));

        await RunAsync(factory);

        var rolesAfter = factory.Roles.All.Select(role => (role.Id, role.Name)).OrderBy(row => row.Id).ToList();
        var administratorAfter = PermissionValues(await GetRoleClaimsAsync(await FindRoleAsync(RoleNames.Administrator)));

        // No duplicate roles, no claim drift, no removals of valid claims.
        Assert.Equal(rolesBefore, rolesAfter);
        Assert.Equal(administratorBefore, administratorAfter);
        Assert.Equal(ExpectedAdministratorPermissions, administratorAfter);
        foreach (var roleName in new[] { RoleNames.Student, RoleNames.GraduateStaff, RoleNames.Administrator })
        {
            Assert.Single(factory.Roles.All, role => role.Name == roleName);
        }
    }

    [Fact]
    public async Task Stale_permission_claims_on_a_canonical_role_are_removed()
    {
        var administrator = await FindRoleAsync(RoleNames.Administrator);
        await AddRoleClaimAsync(administrator, "Forms.Manage");

        await RunAsync(factory);

        var claims = await GetRoleClaimsAsync(await FindRoleAsync(RoleNames.Administrator));
        Assert.DoesNotContain(claims, claim =>
            claim.Type == PermissionClaims.Type && claim.Value == "Forms.Manage");
        Assert.Equal(ExpectedAdministratorPermissions, PermissionValues(claims));
    }

    [Fact]
    public async Task Non_permission_role_claims_are_preserved()
    {
        var administrator = await FindRoleAsync(RoleNames.Administrator);
        await AddRoleClaimAsync(administrator, "note", "keep-me");

        await RunAsync(factory);

        var claims = await GetRoleClaimsAsync(await FindRoleAsync(RoleNames.Administrator));
        Assert.Contains(claims, claim => claim.Type == "note" && claim.Value == "keep-me");
        Assert.Equal(ExpectedAdministratorPermissions, PermissionValues(claims));
    }

    [Fact]
    public async Task Non_canonical_roles_and_their_claims_are_never_touched()
    {
        var customRole = await EnsureRoleAsync("CustomRole");
        if (!(await GetRoleClaimsAsync(customRole)).Any(claim =>
                claim.Type == PermissionClaims.Type && claim.Value == Permissions.UsersManage))
        {
            await AddRoleClaimAsync(customRole, Permissions.UsersManage);
        }

        await RunAsync(factory);

        // Reconciliation's jurisdiction is the three canonical roles only:
        // this custom role keeps both itself and its (stale-for-canonical) claim.
        var customRoleAfter = await FindRoleAsync("CustomRole");
        Assert.Contains(
            await GetRoleClaimsAsync(customRoleAfter),
            claim => claim.Type == PermissionClaims.Type && claim.Value == Permissions.UsersManage);
    }

    [Fact]
    public async Task Duplicate_expected_permission_claims_converge_to_exactly_one()
    {
        var administrator = await FindRoleAsync(RoleNames.Administrator);
        await AddRoleClaimAsync(administrator, Permissions.UsersManage); // second occurrence

        Assert.Equal(2, (await GetRoleClaimsAsync(administrator)).Count(claim =>
            claim.Type == PermissionClaims.Type && claim.Value == Permissions.UsersManage));

        await RunAsync(factory);

        var claims = await GetRoleClaimsAsync(await FindRoleAsync(RoleNames.Administrator));
        Assert.Equal(1, claims.Count(claim =>
            claim.Type == PermissionClaims.Type && claim.Value == Permissions.UsersManage));
        Assert.Equal(1, claims.Count(claim =>
            claim.Type == PermissionClaims.Type && claim.Value == Permissions.RolesAssign));
    }

    [Fact]
    public async Task Users_and_their_role_memberships_are_never_touched()
    {
        var user = await SsoTestFlow.CreateUserAsync(
            factory, SsoTestFlow.NextNationalId(), "کاربر آزمایشی");

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var result = await userManager.AddToRoleAsync(user, RoleNames.Administrator);
            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Code)));
        }

        await RunAsync(factory);

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True(await userManager.IsInRoleAsync(user, RoleNames.Administrator));
        }

        Assert.Contains(factory.Users.All, candidate => candidate.Id == user.Id);
        Assert.Equal(
            ExpectedAdministratorPermissions,
            PermissionValues(await GetRoleClaimsAsync(await FindRoleAsync(RoleNames.Administrator))));
    }

    [Fact]
    public async Task Cancellation_propagates_and_is_not_swallowed()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RunAsync(factory, cancellation.Token));
    }

    [Fact]
    public async Task Role_creation_failure_fails_reconciliation_unless_the_role_appears_concurrently()
    {
        // Arrange: remove Student so reconciliation must create it again
        // (restored by the concurrent-success scenario below).
        var student = await FindRoleAsync(RoleNames.Student);
        using (var scope = factory.Services.CreateScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            Assert.True((await roleManager.DeleteAsync(student)).Succeeded);
        }

        // Scenario A: creation fails and no row appears → fail closed with the
        // Identity errors in the exception message (no silent success).
        factory.Roles.FailNextCreate = true;
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(factory));
        Assert.Contains(RoleNames.Student, thrown.Message, StringComparison.Ordinal);
        Assert.Contains("SimulatedCreateFailure", thrown.Message, StringComparison.Ordinal);

        // Scenario B: creation fails but a concurrent writer's row exists →
        // the re-check treats it as success and reconciliation completes.
        factory.Roles.FailNextCreateAfterWriting = true;
        await RunAsync(factory); // must not throw

        var repaired = await FindRoleAsync(RoleNames.Student);
        Assert.Equal(RoleNames.Student, repaired.Name);
        Assert.Empty(PermissionValues(await GetRoleClaimsAsync(repaired)));
    }

    [Fact]
    public async Task Permission_claim_addition_failure_propagates_and_the_next_run_repairs()
    {
        var administrator = await FindRoleAsync(RoleNames.Administrator);
        await AddRoleClaimAsync(administrator, "note", "keep-me");
        var rolesAssign = (await GetRoleClaimsAsync(administrator)).Single(claim =>
            claim.Type == PermissionClaims.Type && claim.Value == Permissions.RolesAssign);
        using (var scope = factory.Services.CreateScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            Assert.True((await roleManager.RemoveClaimAsync(administrator, rolesAssign)).Succeeded);
        }

        // Arm the store-level UPDATE that RoleManager runs AFTER the claim
        // mutation (its UpdateRoleAsync path): the claim write completes at the
        // store, but the role update returns IdentityResult.Failed with the
        // SimulatedAddClaimUpdateFailure code — the realistic EF flow where the
        // claim row is saved and the post-claim role update fails.
        factory.Roles.FailNextAddClaimUpdate = true;
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(factory));

        // Not swallowed: the exception carries the failed IdentityResult's code
        // (only reachable through the Succeeded check on RoleManager's result —
        // no store exception is thrown for this scenario).
        Assert.Contains("SimulatedAddClaimUpdateFailure", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("add permission claim", thrown.Message, StringComparison.Ordinal);
        Assert.Contains(RoleNames.Administrator, thrown.Message, StringComparison.Ordinal);

        // The failed run leaves non-permission claims untouched.
        Assert.Contains(
            await GetRoleClaimsAsync(await FindRoleAsync(RoleNames.Administrator)),
            claim => claim.Type == "note" && claim.Value == "keep-me");

        // The one-shot fault is consumed, so the next run converges to the catalog.
        await RunAsync(factory);
        var claims = await GetRoleClaimsAsync(await FindRoleAsync(RoleNames.Administrator));
        Assert.Contains(claims, claim => claim.Type == "note" && claim.Value == "keep-me");
        Assert.Equal(ExpectedAdministratorPermissions, PermissionValues(claims));
    }

    [Fact]
    public async Task Permission_claim_removal_failure_propagates_and_the_next_run_repairs()
    {
        var administrator = await FindRoleAsync(RoleNames.Administrator);
        await AddRoleClaimAsync(administrator, "note", "keep-me");
        await AddRoleClaimAsync(administrator, "Forms.Manage");

        // Arm the store-level UPDATE that RoleManager runs AFTER the claim
        // mutation (its UpdateRoleAsync path): the removal completes at the
        // store, but the role update returns IdentityResult.Failed with the
        // SimulatedRemoveClaimUpdateFailure code.
        factory.Roles.FailNextRemoveClaimUpdate = true;
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(factory));

        // Not swallowed: the exception carries the failed IdentityResult's code
        // (only reachable through the Succeeded check on RoleManager's result —
        // no store exception is thrown for this scenario).
        Assert.Contains("SimulatedRemoveClaimUpdateFailure", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("remove permission claim", thrown.Message, StringComparison.Ordinal);
        Assert.Contains(RoleNames.Administrator, thrown.Message, StringComparison.Ordinal);

        // The failed run leaves non-permission claims untouched.
        Assert.Contains(
            await GetRoleClaimsAsync(await FindRoleAsync(RoleNames.Administrator)),
            claim => claim.Type == "note" && claim.Value == "keep-me");

        // The one-shot fault is consumed, so the next run converges to the catalog.
        await RunAsync(factory);
        var claims = await GetRoleClaimsAsync(await FindRoleAsync(RoleNames.Administrator));
        Assert.DoesNotContain(claims, claim =>
            claim.Type == PermissionClaims.Type && claim.Value == "Forms.Manage");
        Assert.Contains(claims, claim => claim.Type == "note" && claim.Value == "keep-me");
        Assert.Equal(ExpectedAdministratorPermissions, PermissionValues(claims));
    }

    private static async Task RunAsync(
        GraduateRecordsWebApplicationFactory factory, CancellationToken cancellationToken = default)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<RolePermissionReconciliation>()
            .RunAsync(cancellationToken);
    }

    private async Task<ApplicationRole> FindRoleAsync(string roleName)
    {
        using var scope = factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var role = await roleManager.FindByNameAsync(roleName);
        Assert.NotNull(role);
        return role!;
    }

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

    private async Task<List<Claim>> GetRoleClaimsAsync(ApplicationRole role)
    {
        using var scope = factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        return (await roleManager.GetClaimsAsync(role)).ToList();
    }

    private async Task AddRoleClaimAsync(ApplicationRole role, string value) =>
        await AddRoleClaimAsync(role, PermissionClaims.Type, value);

    private async Task AddRoleClaimAsync(ApplicationRole role, string claimType, string claimValue)
    {
        using var scope = factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var result = await roleManager.AddClaimAsync(role, new Claim(claimType, claimValue));
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Code)));
    }

    private static List<string> PermissionValues(IEnumerable<Claim> claims) =>
        claims
            .Where(claim => claim.Type == PermissionClaims.Type)
            .Select(claim => claim.Value)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
}
