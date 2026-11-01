using Microsoft.AspNetCore.Identity;

namespace GraduateRecords.Infrastructure.Identity;

/// <summary>
/// Canonical persisted Identity role (ADR-0017 §2: exactly <c>Student</c>,
/// <c>GraduateStaff</c> and <c>Administrator</c> are ever stored; <c>Staff</c>
/// is prose only). The mirror of <see cref="ApplicationUser"/>: the named,
/// sealed Identity type for this system, so the framework POCO
/// <see cref="IdentityRole"/> never leaks as "the role" through seeds,
/// administration ports or tests. Deliberately carries no fields, no behavior
/// and no navigation — role definitions and role→permission mappings are
/// code-defined (UC2.1 <c>RolePermissionCatalog</c>) and are reconciled into
/// Identity role claims by a later checkpoint (ADR-0017 §7–§8).
/// </summary>
public sealed class ApplicationRole : IdentityRole
{
}
