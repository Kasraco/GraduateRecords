namespace GraduateRecords.Application.Common.Security;

/// <summary>
/// Scope foundation for the ARCHITECTURE §10 <c>ScopeExtensions</c> hook
/// (ROADMAP Phase 1A feature list). UC2.1 ships only the role-to-scope
/// derivation <see cref="FromRoles"/>: the comparison/resource-access
/// counterparts <c>WithinScope</c>/<c>CanAccess</c> are deliberately deferred
/// to UC2.5, where <c>ICurrentUser</c> exists and their semantics can be
/// defined against actual authorization needs instead of guessed against
/// future resource types (ADR-0017 §16 defers resource authorization to the
/// owning feature phases).
/// </summary>
public static class ScopeExtensions
{
    /// <summary>
    /// Derives the Phase 1 <see cref="StaffScope"/> from canonical role names
    /// (ADR-0017 StaffScope rule):
    /// a holder of <c>GraduateStaff</c> ⇒ <see cref="StaffScope.AllStudents"/>;
    /// <c>Student</c>, <c>Administrator</c>, empty input and unknown role names
    /// ⇒ <see cref="StaffScope.None"/> (fail-closed). A user holding both
    /// <c>Administrator</c> and <c>GraduateStaff</c> derives
    /// <see cref="StaffScope.AllStudents"/> only because of the
    /// <c>GraduateStaff</c> role — the Administrator role never grants
    /// student-data scope (ADR-0017 §12). Matching is ordinal against the
    /// canonical names (ADR-0017 §2); any non-canonical casing derives
    /// <see cref="StaffScope.None"/>.
    /// Input is role names only: neither <c>UserKind</c> nor authentication
    /// method can reach this derivation (ADR-0017 §1/§4).
    /// </summary>
    public static StaffScope FromRoles(IEnumerable<string> roleNames)
    {
        foreach (var roleName in roleNames)
        {
            if (string.Equals(roleName, RoleNames.GraduateStaff, StringComparison.Ordinal))
            {
                return StaffScope.AllStudents;
            }
        }

        return StaffScope.None;
    }
}
