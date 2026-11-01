namespace GraduateRecords.Application.Common.Security;

/// <summary>
/// Canonical Identity claim vocabulary for authorization (ADR-0004: the
/// role→permission mapping "is seeded into Identity role claims (`permission`
/// claim type)"; ADR-0017 §6–§8). A permission grant is an Identity role claim
/// whose <see cref="Type"/> is exactly this value and whose value is one of the
/// <see cref="Permissions"/> constants. The claims principal factory copies role
/// claims onto the authenticated principal verbatim (UC2.2); policies read them
/// back by this type (later checkpoint). Lives in Application because both the
/// Web authorization layer and the Infrastructure seeds must share it without
/// adding a Web → Infrastructure dependency (AGENTS §2).
/// </summary>
public static class PermissionClaims
{
    /// <summary>Claim type carrying one permission value (e.g. <c>Users.Manage</c>).</summary>
    public const string Type = "permission";
}
