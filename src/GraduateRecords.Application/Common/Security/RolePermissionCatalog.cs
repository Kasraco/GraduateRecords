namespace GraduateRecords.Application.Common.Security;

/// <summary>
/// Code-defined role → permission mapping (ADR-0017 §6–§8): this catalog is
/// the single code authority for which permissions a role carries; later
/// feature-owning phases extend it incrementally together with their constant,
/// seed and policy (ADR-0017 §7). Explicit and deterministic — no permission
/// inheritance, no role hierarchy, no wildcards, no implicit "Administrator
/// has everything" (ADR-0017 §6). Unknown roles yield an empty set
/// (fail-closed, ADR-0017 §21).
/// </summary>
public static class RolePermissionCatalog
{
    private static readonly IReadOnlyList<string> AdministratorPermissions =
        [Permissions.UsersManage, Permissions.RolesAssign];

    /// <summary>
    /// Returns the Phase 1A permission set for a role name (ADR-0017 §8):
    /// Administrator → { Users.Manage, Roles.Assign }; GraduateStaff → none;
    /// Student → none. Any non-canonical, unknown or blank role name returns
    /// an empty set — fail-closed, comparison is ordinal (ADR-0017 §2).
    /// </summary>
    public static IReadOnlyList<string> GetPermissions(string roleName)
    {
        if (string.Equals(roleName, RoleNames.Administrator, StringComparison.Ordinal))
        {
            return AdministratorPermissions;
        }

        // Student, GraduateStaff and every unknown role carry no Phase 1A
        // feature permissions (ADR-0017 §8); an unknown name never grants.
        return Array.Empty<string>();
    }
}
