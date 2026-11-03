namespace GraduateRecords.Application.Common.Security;

/// <summary>
/// Shared staff-scope decisions for the ARCHITECTURE §10 hook
/// (<c>ICurrentUser.StaffScope</c> + <c>ScopeExtensions.WithinScope/CanAccess</c>):
/// <list type="bullet">
/// <item><see cref="FromRoles"/> derives the Phase 1 scope from canonical role
/// names (ADR-0017 StaffScope rule);</item>
/// <item><see cref="WithinScope"/> gates staff <b>queries</b>;</item>
/// <item><see cref="CanAccess"/> gates staff <b>by-id</b> handlers, composing
/// ownership and scope exactly as ADR-0017 §16 prescribes.</item>
/// </list>
/// Handlers call these and map <c>false</c> to <c>NotFound</c>
/// (ARCHITECTURE §10: out-of-scope ⇒ NotFound; ADR-0017 §16). Permission is a
/// separate, earlier check (<c>ICurrentUser.HasPermission</c>) — scope never
/// substitutes for a missing permission and vice versa (ADR-0017 §1, §21).
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

    /// <summary>
    /// Query gate (ARCHITECTURE §10: "<c>WithinScope</c> … used by every staff
    /// query"): true when <paramref name="requiredScope"/> — the scope the
    /// query reads — lies within the actor's <paramref name="actorScope"/>.
    /// <para>
    /// Phase 1 (ADR-0004 §4.1): student data lives in
    /// <see cref="StaffScope.AllStudents"/> and no finer scope exists, so the
    /// complete rule is: <c>AllStudents</c> covers every requirement; <c>None</c>
    /// covers only a <c>None</c> requirement (fail-closed for the
    /// <c>AllStudents</c> case). Undefined enum values fail closed
    /// (ADR-0017 §21). Out-of-scope ⇒ the handler returns <c>NotFound</c>.
    /// </para>
    /// Ownership is intentionally not part of this method: queries express
    /// ownership in their filter (own rows only), never through scope
    /// (ADR-0017 §16 separates Ownership from StaffScope).
    /// </summary>
    public static bool WithinScope(this StaffScope actorScope, StaffScope requiredScope)
    {
        // Fail-closed on undefined values (ADR-0017 §21): an unknown scope
        // value on either side must never grant access.
        if (!IsDefined(actorScope) || !IsDefined(requiredScope))
        {
            return false;
        }

        return actorScope == StaffScope.AllStudents
            || requiredScope == StaffScope.None;
    }

    /// <summary>
    /// By-id gate (ARCHITECTURE §10: "<c>CanAccess</c> … by-id handler"):
    /// composes ADR-0017 §16 for one loaded resource —
    /// <c>ownedByActor</c> (Ownership row: a student's own resource) OR
    /// <see cref="WithinScope"/> over <paramref name="resourceScope"/>
    /// (StaffScope row: GraduateStaff over student data).
    /// <para>
    /// The caller determines ownership server-side (resource owner id ==
    /// <c>ICurrentUser.UserId</c>, never request data) and passes it in;
    /// permission is checked separately before this call. Administrator never
    /// passes the scope row (its scope is <c>None</c>, ADR-0017 §12) —
    /// Administrator does not bypass ownership/scope (ADR-0017 §16).
    /// <c>false</c> ⇒ the handler returns <c>NotFound</c> (never leaks
    /// existence, ADR-0004 §10.3).
    /// </para>
    /// Ownership grants access independently of scope (the student-owner row
    /// of ADR-0017 §16 requires no scope), so an undefined
    /// <paramref name="resourceScope"/> still yields <c>true</c> for an owner;
    /// for a non-owner every undefined value fails closed.
    /// </summary>
    public static bool CanAccess(
        this StaffScope actorScope,
        StaffScope resourceScope,
        bool ownedByActor) =>
        ownedByActor || actorScope.WithinScope(resourceScope);

    /// <summary>Only the two documented Phase 1 values participate (ADR-0017 StaffScope rule).</summary>
    private static bool IsDefined(StaffScope scope) =>
        scope is StaffScope.None or StaffScope.AllStudents;
}
