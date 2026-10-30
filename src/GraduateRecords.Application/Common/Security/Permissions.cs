namespace GraduateRecords.Application.Common.Security;

/// <summary>
/// Canonical permission identifiers (ADR-0017 §5: PascalCase.Dot is the single
/// naming convention). Phase 1A contains exactly the two constants below
/// (ADR-0017 §8); a feature-owning phase adds its permission here together
/// with its mapping, idempotent seed and policy when that feature is
/// implemented (ADR-0017 §7). No wildcards, inheritance or hierarchy exist —
/// every effective permission is one of these explicit constants
/// (ADR-0017 §6).
/// </summary>
public static class Permissions
{
    /// <summary>User-account lifecycle administration (ADR-0017 §9; consumed by UC3).</summary>
    public const string UsersManage = "Users.Manage";

    /// <summary>
    /// Grant and revoke of supported roles for users (ADR-0017 §10); no
    /// separate grant/revoke/manage capabilities exist in v1.
    /// </summary>
    public const string RolesAssign = "Roles.Assign";
}
