using System.Security.Claims;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Infrastructure.Persistence.Seed;

namespace GraduateRecords.Tests.Unit;

/// <summary>
/// UC2.3 — pure permission-claim delta semantics (ADR-0017 §7–§8): the
/// persisted role claims converge exactly to the Application catalog — ordinal
/// and case-sensitive, duplicate occurrences collapse to one retained claim,
/// and claims outside <see cref="PermissionClaims.Type"/> never enter the
/// delta (so applying it preserves them).
/// </summary>
public sealed class RolePermissionReconciliationDeltaTests
{
    private static Claim Permission(string value) => new(PermissionClaims.Type, value);

    [Fact]
    public void Missing_expected_permissions_are_added()
    {
        var (toAdd, toRemove) = RolePermissionReconciliation.ComputePermissionClaimDelta(
            existingClaims: Array.Empty<Claim>(),
            expectedPermissions: [Permissions.UsersManage, Permissions.RolesAssign]);

        Assert.Equal([Permissions.UsersManage, Permissions.RolesAssign], toAdd.Select(claim => claim.Value).ToArray());
        Assert.All(toAdd, claim => Assert.Equal(PermissionClaims.Type, claim.Type));
        Assert.Empty(toRemove);
    }

    [Fact]
    public void Existing_expected_permissions_are_unchanged()
    {
        var (toAdd, toRemove) = RolePermissionReconciliation.ComputePermissionClaimDelta(
            existingClaims: [Permission(Permissions.UsersManage), Permission(Permissions.RolesAssign)],
            expectedPermissions: [Permissions.UsersManage, Permissions.RolesAssign]);

        Assert.Empty(toAdd);
        Assert.Empty(toRemove);
    }

    [Fact]
    public void Stale_permissions_are_removed()
    {
        var (toAdd, toRemove) = RolePermissionReconciliation.ComputePermissionClaimDelta(
            existingClaims: [Permission("Forms.Manage"), Permission(Permissions.UsersManage)],
            expectedPermissions: [Permissions.UsersManage]);

        Assert.Empty(toAdd);
        var removed = Assert.Single(toRemove);
        Assert.Equal("Forms.Manage", removed.Value);
        Assert.Equal(PermissionClaims.Type, removed.Type);
    }

    [Fact]
    public void Case_mismatch_adds_the_canonical_value_and_removes_the_incorrect_casing()
    {
        // Ordinal, case-sensitive: "users.manage" is stale, "Users.Manage" is added.
        var (toAdd, toRemove) = RolePermissionReconciliation.ComputePermissionClaimDelta(
            existingClaims: [Permission("users.manage")],
            expectedPermissions: [Permissions.UsersManage]);

        var added = Assert.Single(toAdd);
        Assert.Equal(Permissions.UsersManage, added.Value);
        var removed = Assert.Single(toRemove);
        Assert.Equal("users.manage", removed.Value);
    }

    [Fact]
    public void Non_permission_claims_never_enter_the_delta()
    {
        var (toAdd, toRemove) = RolePermissionReconciliation.ComputePermissionClaimDelta(
            existingClaims:
            [
                new Claim("note", "keep-me"),
                Permission(Permissions.UsersManage),
                new Claim(PermissionClaims.Type + "x", "Users.Manage"), // not the permission type
            ],
            expectedPermissions: [Permissions.UsersManage]);

        // The catalog is fully satisfied, so nothing changes; and neither output
        // contains any claim that reconciliation may not touch.
        Assert.Empty(toAdd);
        Assert.Empty(toRemove);
        Assert.DoesNotContain(toAdd, claim => claim.Type == "note");
        Assert.DoesNotContain(toRemove, claim => claim.Type == "note");
    }

    [Fact]
    public void Duplicate_expected_permissions_keep_exactly_one_occurrence()
    {
        var (toAdd, toRemove) = RolePermissionReconciliation.ComputePermissionClaimDelta(
            existingClaims:
            [
                Permission(Permissions.UsersManage),
                Permission(Permissions.UsersManage),
                Permission(Permissions.UsersManage),
            ],
            expectedPermissions: [Permissions.UsersManage]);

        Assert.Empty(toAdd);
        Assert.Equal(2, toRemove.Count); // one retained, two excess occurrences removed
        Assert.All(toRemove, claim =>
        {
            Assert.Equal(PermissionClaims.Type, claim.Type);
            Assert.Equal(Permissions.UsersManage, claim.Value);
        });
    }

    [Fact]
    public void Empty_expected_catalog_removes_all_permission_claims()
    {
        // Student/GraduateStaff currently carry no Phase 1A permissions: any
        // permission claim on them is stale by definition.
        var (toAdd, toRemove) = RolePermissionReconciliation.ComputePermissionClaimDelta(
            existingClaims:
            [
                Permission("Users.Manage"),
                Permission("Forms.Manage"),
                new Claim("note", "untouched"),
            ],
            expectedPermissions: []);

        Assert.Empty(toAdd);
        Assert.Equal(2, toRemove.Count);
        // ToRemove preserves the existing-claims order (deterministic); both
        // stale values are removed regardless of order.
        Assert.Equal(["Users.Manage", "Forms.Manage"], toRemove.Select(claim => claim.Value).ToArray());
        Assert.DoesNotContain(toRemove, claim => claim.Type == "note");
    }

    [Fact]
    public void Empty_existing_claims_and_empty_expected_catalog_produce_no_changes()
    {
        var (toAdd, toRemove) = RolePermissionReconciliation.ComputePermissionClaimDelta(
            existingClaims: Array.Empty<Claim>(),
            expectedPermissions: []);

        Assert.Empty(toAdd);
        Assert.Empty(toRemove);
    }
}
