namespace GraduateRecords.Application.Common.Security;

/// <summary>
/// Canonical persisted/seeded role names (ADR-0017 §2): exactly these three,
/// with <c>GraduateStaff</c> as the exact middle-role name. The word
/// "Staff" remains prose only and MUST NEVER appear as a canonical role name.
/// Single source for seeds, validation and scope derivation.
/// </summary>
public static class RoleNames
{
    public const string Student = "Student";

    public const string GraduateStaff = "GraduateStaff";

    public const string Administrator = "Administrator";
}
