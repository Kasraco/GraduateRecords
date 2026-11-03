using GraduateRecords.Application.Common.Security;

namespace GraduateRecords.Tests.Unit;

/// <summary>
/// UC2.5 resource-access rules (ARCHITECTURE §10 <c>WithinScope</c>/<c>CanAccess</c>;
/// ADR-0017 §16 composition "Permission + Ownership" OR "Permission + StaffScope";
/// ADR-0017 §21 fail-closed; ADR-0004 §4.1 Phase 1 scope). The fake-scope tests
/// are mandatory (ARCHITECTURE §10: "A test with a restrictive fake scope is
/// mandatory"): a restrictive scope must deny even when roles or permissions
/// would otherwise suggest access. Handlers map a <c>false</c> result to
/// <c>NotFound</c> (out-of-scope ⇒ NotFound, ADR-0017 §16).
/// </summary>
public sealed class StaffScopeAccessTests
{
    /// <summary>The scope every student resource lives in during Phase 1 (ADR-0004 §4.1).</summary>
    private const StaffScope StudentDataScope = StaffScope.AllStudents;

    // ------------------------------------------------------------------
    // WithinScope — the staff QUERY gate (ARCHITECTURE §10).
    // ------------------------------------------------------------------

    [Theory]
    // actor scope × required scope → covered?
    [InlineData(StaffScope.AllStudents, StaffScope.AllStudents, true)] // staff query over student data
    [InlineData(StaffScope.AllStudents, StaffScope.None, true)] // AllStudents covers an unrestricted requirement
    [InlineData(StaffScope.None, StaffScope.AllStudents, false)] // student / administrator: out of scope ⇒ NotFound
    [InlineData(StaffScope.None, StaffScope.None, true)] // no scope required — permission still applies separately
    // Undefined enum values fail closed (ADR-0017 §21):
    [InlineData((StaffScope)99, StaffScope.AllStudents, false)]
    [InlineData((StaffScope)99, StaffScope.None, false)]
    [InlineData(StaffScope.AllStudents, (StaffScope)99, false)]
    [InlineData(StaffScope.None, (StaffScope)99, false)]
    public void WithinScope_covers_only_what_the_actor_scope_allows(
        StaffScope actorScope,
        StaffScope requiredScope,
        bool expected) =>
        Assert.Equal(expected, actorScope.WithinScope(requiredScope));

    // ------------------------------------------------------------------
    // CanAccess — the staff BY-ID gate (ARCHITECTURE §10), composing the two
    // rows of ADR-0017 §16: Ownership OR StaffScope (permission is separate).
    // ------------------------------------------------------------------

    [Theory]
    // ownedByActor × actor scope × resource scope → allowed?
    [InlineData(true, StaffScope.None, StudentDataScope, true)] // student's own resource (Ownership row)
    [InlineData(true, StaffScope.AllStudents, StudentDataScope, true)] // owner who is also in scope
    [InlineData(true, StaffScope.None, (StaffScope)99, true)] // ownership is scope-independent
    [InlineData(true, (StaffScope)99, StudentDataScope, true)] // owner passes even with an undefined actor scope
    [InlineData(false, StaffScope.AllStudents, StudentDataScope, true)] // GraduateStaff: StaffScope row
    [InlineData(false, StaffScope.None, StudentDataScope, false)] // student-other / administrator / restricted staff
    [InlineData(false, StaffScope.None, (StaffScope)99, false)] // undefined resource scope fails closed for non-owners
    [InlineData(false, (StaffScope)99, StudentDataScope, false)] // undefined actor scope fails closed for non-owners
    public void CanAccess_composes_ownership_and_scope(
        bool ownedByActor,
        StaffScope actorScope,
        StaffScope resourceScope,
        bool expected) =>
        Assert.Equal(expected, actorScope.CanAccess(resourceScope, ownedByActor));

    // ------------------------------------------------------------------
    // The documented handler gate (ADR-0017 §16): Permission + (Ownership |
    // StaffScope). Neither half substitutes for the other (ADR-0017 §1).
    // ------------------------------------------------------------------

    [Theory]
    // hasPermission × actor scope × isOwner → allowed?
    [InlineData(true, StaffScope.AllStudents, false, true)] // permitted staff, in scope
    [InlineData(true, StaffScope.None, true, true)] // permitted owner (student's own resource)
    [InlineData(true, StaffScope.None, false, false)] // permission alone: out-of-ownership AND out-of-scope
    [InlineData(false, StaffScope.AllStudents, false, false)] // scope alone: missing permission blocks
    [InlineData(false, StaffScope.None, true, false)] // owner without permission blocks (permission is always required)
    [InlineData(false, StaffScope.None, false, false)] // neither half
    public void Resource_gate_requires_permission_plus_ownership_or_scope(
        bool grantPermission,
        StaffScope scope,
        bool isOwner,
        bool expected)
    {
        var user = new FakeCurrentUser
        {
            UserId = "u1",
            Permissions = grantPermission
                ? new[] { Permissions.UsersManage }
                : Array.Empty<string>(),
            ScopeOverride = scope,
        };

        // The composition a by-id handler performs (permission from the action
        // attribute / handler check, ownership computed server-side, scope from
        // ICurrentUser). The permission value is incidental: the rule is
        // permission-agnostic (ADR-0017 §16 rows are stated per permission).
        var allowed = user.HasPermission(Permissions.UsersManage)
            && user.StaffScope.CanAccess(StudentDataScope, isOwner);

        Assert.Equal(expected, allowed);
    }

    // ------------------------------------------------------------------
    // Fake-scope tests through ICurrentUser (mandatory, ARCHITECTURE §10).
    // ------------------------------------------------------------------

    /// <summary>
    /// The mandatory restrictive-fake-scope test: the gate must read the
    /// <see cref="StaffScope"/> value itself — not the role names behind it —
    /// so a restrictive scope denies even a GraduateStaff-role session.
    /// </summary>
    [Fact]
    public void A_restrictive_fake_scope_denies_query_and_by_id_access_even_for_a_graduate_staff_role()
    {
        var restrictive = new FakeCurrentUser
        {
            UserId = "staff-1",
            Roles = [RoleNames.GraduateStaff], // would derive AllStudents …
            ScopeOverride = StaffScope.None, // … but the fake scope says None
            Permissions = [Permissions.UsersManage],
        };

        Assert.Equal(StaffScope.None, restrictive.StaffScope);
        Assert.False(restrictive.StaffScope.WithinScope(StudentDataScope));
        Assert.False(restrictive.StaffScope.CanAccess(StudentDataScope, ownedByActor: false));

        // Permission does not compensate for the restrictive scope (ADR-0017 §16).
        Assert.True(restrictive.HasPermission(Permissions.UsersManage));
    }

    /// <summary>ADR-0004 §3: a Student may access only their own resource; another student's ⇒ denied.</summary>
    [Fact]
    public void A_student_may_access_only_their_own_resource()
    {
        var student = new FakeCurrentUser
        {
            UserId = "student-1",
            Roles = [RoleNames.Student],
        };

        Assert.Equal(StaffScope.None, student.StaffScope);

        // Ownership row (ADR-0017 §16): own resource, no scope needed.
        Assert.True(student.StaffScope.CanAccess(StudentDataScope, ownedByActor: true));

        // Another student's resource: out-of-ownership AND out-of-scope ⇒ NotFound.
        Assert.False(student.StaffScope.CanAccess(StudentDataScope, ownedByActor: false));

        // Students never run staff queries over student data.
        Assert.False(student.StaffScope.WithinScope(StudentDataScope));
    }

    /// <summary>ADR-0004 §4.1: GraduateStaff scope is every student — queries and by-id both pass.</summary>
    [Fact]
    public void A_graduate_staff_member_in_scope_may_access_any_student_resource()
    {
        var staff = new FakeCurrentUser
        {
            UserId = "staff-1",
            Roles = [RoleNames.GraduateStaff],
        };

        Assert.Equal(StaffScope.AllStudents, staff.StaffScope);
        Assert.True(staff.StaffScope.WithinScope(StudentDataScope));
        Assert.True(staff.StaffScope.CanAccess(StudentDataScope, ownedByActor: false));
    }

    /// <summary>
    /// ADR-0017 §12/§16: Administrator's scope is <c>None</c> and never bypasses
    /// student data — while the administrative plane stays permission-alone
    /// (ADR-0017 §1), so the same session still passes its capability checks.
    /// </summary>
    [Fact]
    public void An_administrator_scope_is_None_and_never_bypasses_student_data()
    {
        var admin = new FakeCurrentUser
        {
            UserId = "admin-1",
            Roles = [RoleNames.Administrator],
            Permissions = [Permissions.UsersManage, Permissions.RolesAssign],
        };

        Assert.Equal(StaffScope.None, admin.StaffScope);
        Assert.False(admin.StaffScope.WithinScope(StudentDataScope));
        Assert.False(admin.StaffScope.CanAccess(StudentDataScope, ownedByActor: false));

        // The administrator plane is gated by permission alone (ADR-0017 §1).
        Assert.True(admin.HasPermission(Permissions.UsersManage));
    }

    /// <summary>
    /// Multi-role (ADR-0017 §3): scope derives from GraduateStaff only — the
    /// Administrator role contributes no student-data scope (ADR-0017 §12).
    /// </summary>
    [Fact]
    public void An_administrator_who_also_holds_graduate_staff_reaches_student_data_only_via_graduate_staff()
    {
        var dual = new FakeCurrentUser
        {
            UserId = "dual-1",
            Roles = [RoleNames.Administrator, RoleNames.GraduateStaff],
        };

        Assert.Equal(StaffScope.AllStudents, dual.StaffScope);
        Assert.True(dual.StaffScope.CanAccess(StudentDataScope, ownedByActor: false));

        // Dropping GraduateStaff drops the scope — Administrator alone never grants it.
        dual.Roles = [RoleNames.Administrator];
        Assert.Equal(StaffScope.None, dual.StaffScope);
        Assert.False(dual.StaffScope.CanAccess(StudentDataScope, ownedByActor: false));
    }

    /// <summary>ADR-0017 §21: an unauthenticated context fails closed — no scope, no capability.</summary>
    [Fact]
    public void An_unauthenticated_context_has_no_scope_and_no_capability()
    {
        var anonymous = new FakeCurrentUser
        {
            IsAuthenticated = false,
            UserId = null,
            Roles = [],
            Permissions = [],
        };

        Assert.Equal(StaffScope.None, anonymous.StaffScope);
        Assert.False(anonymous.StaffScope.WithinScope(StudentDataScope));
        Assert.False(anonymous.StaffScope.CanAccess(StudentDataScope, ownedByActor: false));
        Assert.False(anonymous.HasPermission(Permissions.UsersManage));
    }
}
