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

    /// <summary>
    /// Every supported canonical role, in canonical order (ADR-0017 §2). A
    /// property rather than a field, so the reflection-based catalog tests that
    /// enumerate the string constants still see exactly three names.
    /// </summary>
    public static IReadOnlyList<string> AllSupported { get; } = [Student, GraduateStaff, Administrator];

    /// <summary>
    /// The supported-role set of ADR-0017 §11: only these code-defined canonical
    /// names may ever be granted or revoked (no arbitrary role creation or
    /// editing exists in v1, §10). Ordinal, case-sensitive comparison — a
    /// non-canonical spelling is unsupported and therefore never granted
    /// (fail-closed, ADR-0017 §21). This is the single validation source for
    /// role input.
    /// </summary>
    public static bool IsSupported(string? roleName) =>
        roleName is not null &&
        (string.Equals(roleName, Student, StringComparison.Ordinal)
         || string.Equals(roleName, GraduateStaff, StringComparison.Ordinal)
         || string.Equals(roleName, Administrator, StringComparison.Ordinal));
}
