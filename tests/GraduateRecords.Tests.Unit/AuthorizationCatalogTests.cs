using System.Reflection;
using GraduateRecords.Application.Common.Security;

namespace GraduateRecords.Tests.Unit;

/// <summary>
/// UC2.1 catalog foundation: canonical role names, the exact Phase 1A
/// permission constants, and the deterministic role → permission mapping
/// (ADR-0017 §2, §5, §6–§8).
/// </summary>
public sealed class AuthorizationCatalogTests
{
    private static string[] ConstantValues(Type type) => type
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
        .Select(field => (string)field.GetValue(null)!)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    [Fact]
    public void RoleNames_are_exactly_the_three_canonical_roles()
    {
        string[] expected = ["Administrator", "GraduateStaff", "Student"];

        Assert.Equal(expected, ConstantValues(typeof(RoleNames)));
    }

    [Fact]
    public void GraduateStaff_exists_and_canonical_Staff_does_not()
    {
        string[] roleNames = ConstantValues(typeof(RoleNames));

        Assert.Contains(RoleNames.GraduateStaff, roleNames);
        Assert.DoesNotContain("Staff", roleNames);
    }

    [Fact]
    public void Permissions_are_exactly_the_phase_1A_permissions()
    {
        string[] expected = ["Roles.Assign", "Users.Manage"];

        Assert.Equal(expected, ConstantValues(typeof(Permissions)));
    }

    [Fact]
    public void Administrator_receives_exactly_the_phase_1A_permissions()
    {
        string[] expected = ["Roles.Assign", "Users.Manage"];

        IReadOnlyList<string> actual = RolePermissionCatalog.GetPermissions(RoleNames.Administrator)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Student_has_no_phase_1A_permissions() =>
        Assert.Empty(RolePermissionCatalog.GetPermissions(RoleNames.Student));

    [Fact]
    public void GraduateStaff_has_no_phase_1A_permissions() =>
        Assert.Empty(RolePermissionCatalog.GetPermissions(RoleNames.GraduateStaff));

    [Fact]
    public void Unknown_role_has_no_permissions_fail_closed()
    {
        Assert.Empty(RolePermissionCatalog.GetPermissions("NotACanonicalRole"));
        Assert.Empty(RolePermissionCatalog.GetPermissions("Staff"));
        Assert.Empty(RolePermissionCatalog.GetPermissions(string.Empty));
    }

    [Fact]
    public void Catalog_introduces_no_future_permission_families()
    {
        string[] forbiddenPrefixes =
        [
            "Forms.", "Students.", "Submissions.", "Workflows.",
            "Reports.", "Documents.", "Settings.", "Audit.",
        ];

        string[] everyPermission = [.. ConstantValues(typeof(Permissions))];
        foreach (string roleName in ConstantValues(typeof(RoleNames)))
        {
            everyPermission = [.. everyPermission, .. RolePermissionCatalog.GetPermissions(roleName)];
        }

        Assert.All(
            everyPermission,
            permission => Assert.DoesNotContain(
                forbiddenPrefixes,
                prefix => permission.StartsWith(prefix, StringComparison.Ordinal)));

        // No inheritance/hierarchy/superuser: the entire reachable permission
        // universe is exactly the two Phase 1A constants.
        Assert.Equal(
            new[] { Permissions.RolesAssign, Permissions.UsersManage },
            everyPermission.Distinct().OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }
}
