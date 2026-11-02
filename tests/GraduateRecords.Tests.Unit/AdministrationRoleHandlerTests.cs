using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Application.Features.Administration;
using GraduateRecords.Application.Features.Administration.Roles;
using GraduateRecords.Domain.Auditing;

namespace GraduateRecords.Tests.Unit;

/// <summary>
/// UC3 role-assignment rules (ADR-0017 §10 capability, §11 delegation, §17–§19
/// last-Administrator invariant, §20 self-role-change ban, §21 fail-closed) with
/// fakes for the ports (AGENTS §22). The database-side serialization of §19 is
/// proven separately by the SQL Server integration test; these tests prove the
/// *rule* and the precondition hand-off only.
/// </summary>
public sealed class AdministrationRoleHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);

    private readonly FakeIdentityAdministration _identity = new();
    private readonly FakeCurrentUser _actor = new()
    {
        UserId = "actor-1",
        DisplayName = "مدیر سامانه",
        Permissions = [Permissions.UsersManage, Permissions.RolesAssign],
    };
    private readonly FakeAuditWriter _audit = new();
    private readonly FakeClock _clock = new(Now);

    private GrantRoleHandler GrantHandler => new(_identity, _actor, _audit, _clock);

    private RevokeRoleHandler RevokeHandler => new(_identity, _actor, _audit, _clock);

    [Fact]
    public async Task Grant_requires_the_Roles_Assign_capability()
    {
        _actor.Permissions = [Permissions.UsersManage]; // Users.Manage alone is not enough
        _identity.Users["u1"] = Detail("u1", LocalUserKind.Staff, [RoleNames.GraduateStaff]);

        var result = await GrantHandler.HandleAsync(Command("u1", RoleNames.GraduateStaff));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
        Assert.Equal(AdministrationErrors.PermissionDeniedCode, result.Error.Code);
        Assert.Empty(_identity.Grants); // fail-closed: the port is never reached
        Assert.Single(_audit.Entries);
        Assert.Equal(AuditResult.Denied, _audit.Entries[0].Result);
    }

    [Fact]
    public async Task Grant_rejects_a_role_outside_the_supported_set()
    {
        _identity.Users["u1"] = Detail("u1", LocalUserKind.Staff, []);

        var result = await GrantHandler.HandleAsync(Command("u1", "Staff")); // prose name, never canonical

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Equal(AdministrationErrors.UnsupportedRoleCode, result.Error.Code);
        Assert.Empty(_identity.Grants);
        Assert.Empty(_audit.Entries); // input validation is not a security denial
    }

    [Fact]
    public async Task Grant_refuses_to_change_the_actors_own_roles()
    {
        var result = await GrantHandler.HandleAsync(Command(_actor.UserId!, RoleNames.GraduateStaff));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
        Assert.Equal(AdministrationErrors.SelfRoleChangeCode, result.Error.Code);
        Assert.Empty(_identity.Grants);
        Assert.Equal(AuditResult.Denied, Assert.Single(_audit.Entries).Result);
    }

    [Fact]
    public async Task Grant_reports_an_unknown_target_user_as_not_found()
    {
        var result = await GrantHandler.HandleAsync(Command("missing", RoleNames.GraduateStaff));

        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
        Assert.Empty(_identity.Grants);
    }

    /// <summary>
    /// ADR-0017 §11 (delegation): an Administrator holding only the two Phase 1A
    /// capabilities may assign GraduateStaff without holding GraduateStaff's
    /// permissions, and the assignment never transfers those permissions to the
    /// actor.
    /// </summary>
    [Fact]
    public async Task Grant_allows_a_role_whose_permissions_the_actor_does_not_hold()
    {
        Assert.Equal(
            new[] { Permissions.RolesAssign, Permissions.UsersManage },
            _actor.Permissions.OrderBy(value => value, StringComparer.Ordinal));

        _identity.Users["u1"] = Detail("u1", LocalUserKind.Staff, []);

        var result = await GrantHandler.HandleAsync(Command("u1", RoleNames.GraduateStaff));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Changed);
        Assert.Equal(("u1", RoleNames.GraduateStaff), Assert.Single(_identity.Grants));

        // Assigning a role never grants its permissions to the actor (§11).
        Assert.Equal(
            new[] { Permissions.RolesAssign, Permissions.UsersManage },
            _actor.Permissions.OrderBy(value => value, StringComparer.Ordinal));

        var audit = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.AuthorizationRoleGranted, audit.Action);
        Assert.Equal(AuditResult.Success, audit.Result);
        Assert.Equal("actor-1", audit.ActorUserId);
        Assert.Equal("مدیر سامانه", audit.ActorDisplayName);
        Assert.Contains(RoleNames.GraduateStaff, audit.DetailsJson);
        Assert.DoesNotContain("nationalId", audit.DetailsJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Grant_that_changes_nothing_writes_no_audit_event()
    {
        _identity.Users["u1"] = Detail("u1", LocalUserKind.Staff, [RoleNames.GraduateStaff]);
        _identity.GrantResult = Result<bool>.Success(false); // already held

        var result = await GrantHandler.HandleAsync(Command("u1", RoleNames.GraduateStaff));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Changed);
        Assert.Empty(_audit.Entries); // only state changes are audited
    }

    [Fact]
    public async Task Revoke_of_the_last_effective_administrator_is_refused()
    {
        _identity.Users["u1"] = Detail("u1", LocalUserKind.Administrator, [RoleNames.Administrator]);
        _identity.EffectiveAdministratorIds.Add("u1");

        var result = await RevokeHandler.HandleAsync(Revoke("u1", RoleNames.Administrator));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
        Assert.Equal(AdministrationErrors.LastAdministratorCode, result.Error.Code);
        Assert.Empty(_identity.Revocations); // refused before any mutation
        Assert.Equal(AuditResult.Denied, Assert.Single(_audit.Entries).Result);
    }

    /// <summary>
    /// ADR-0017 §19: the handler hands the exact set it decided §17 against to
    /// the port, which re-checks it inside its locked transaction.
    /// </summary>
    [Fact]
    public async Task Revoke_of_a_non_last_administrator_passes_the_observed_set_to_the_port()
    {
        _identity.Users["u1"] = Detail("u1", LocalUserKind.Administrator, [RoleNames.Administrator]);
        _identity.Users["u2"] = Detail("u2", LocalUserKind.Administrator, [RoleNames.Administrator]);
        _identity.EffectiveAdministratorIds.AddRange(["u1", "u2"]);

        var result = await RevokeHandler.HandleAsync(Revoke("u1", RoleNames.Administrator));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Changed);
        var revocation = Assert.Single(_identity.Revocations);
        Assert.Equal("u1", revocation.UserId);
        Assert.Equal(RoleNames.Administrator, revocation.RoleName);
        Assert.Equal(new[] { "u1", "u2" }, revocation.Observed!.OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(AuditActions.AuthorizationRoleRevoked, Assert.Single(_audit.Entries).Action);
    }

    [Fact]
    public async Task Revoke_of_a_non_administrator_role_carries_no_administrator_precondition()
    {
        _identity.Users["u1"] = Detail("u1", LocalUserKind.Staff, [RoleNames.GraduateStaff]);

        var result = await RevokeHandler.HandleAsync(Revoke("u1", RoleNames.GraduateStaff));

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(_identity.Revocations).Observed);
    }

    /// <summary>
    /// ADR-0017 §19: a precondition that no longer matches is reported as a
    /// conflict (never applied) and is audited as a security denial.
    /// </summary>
    [Fact]
    public async Task Revoke_with_a_stale_administrator_observation_returns_conflict()
    {
        _identity.Users["u1"] = Detail("u1", LocalUserKind.Administrator, [RoleNames.Administrator]);
        _identity.EffectiveAdministratorIds.AddRange(["u1", "u2"]);
        _identity.RevokeResult = Result<bool>.Failure(AdministrationErrors.ConcurrentAdministratorChange());

        var result = await RevokeHandler.HandleAsync(Revoke("u1", RoleNames.Administrator));

        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
        Assert.Equal(AdministrationErrors.ConcurrentAdministratorChangeCode, result.Error.Code);
        Assert.Equal(AuditResult.Denied, Assert.Single(_audit.Entries).Result);
    }

    [Fact]
    public async Task Revoke_that_changes_nothing_writes_no_audit_event()
    {
        _identity.Users["u1"] = Detail("u1", LocalUserKind.Staff, [RoleNames.GraduateStaff]);
        _identity.RevokeResult = Result<bool>.Success(false); // already revoked

        var result = await RevokeHandler.HandleAsync(Revoke("u1", RoleNames.GraduateStaff));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Changed);
        Assert.Empty(_audit.Entries);
    }

    /// <summary>
    /// ADR-0017 §1/§4/§21: authorization must not read the account kind. The
    /// same command against targets of every kind yields the same outcome.
    /// </summary>
    [Theory]
    [InlineData(LocalUserKind.Student)]
    [InlineData(LocalUserKind.Staff)]
    [InlineData(LocalUserKind.Administrator)]
    public async Task Role_change_outcome_does_not_depend_on_the_target_user_kind(LocalUserKind kind)
    {
        _identity.Users["u1"] = Detail("u1", kind, []);

        var result = await GrantHandler.HandleAsync(Command("u1", RoleNames.GraduateStaff));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Changed);
    }

    [Fact]
    public void The_actor_context_exposes_no_kind_or_authentication_membership()
    {
        // Structural fail-closed guard (ADR-0017 §1/§4/§21): the account kind
        // and the authentication path must not even be reachable from here.
        var properties = typeof(ICurrentUser).GetProperties();

        Assert.DoesNotContain(properties, property =>
            property.Name.Contains("Kind", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property =>
            property.PropertyType == typeof(LocalUserKind));
        Assert.DoesNotContain(properties, property =>
            property.Name.Contains("Method", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_permissions_page_projects_the_canonical_catalog_read_only()
    {
        var handler = new GetRolePermissionsHandler(_actor);

        var result = await handler.HandleAsync(new GetRolePermissionsQuery());

        Assert.True(result.IsSuccess);
        var rows = result.Value!;
        Assert.Equal(
            new[] { RoleNames.Student, RoleNames.GraduateStaff, RoleNames.Administrator },
            rows.Select(row => row.RoleName));
        Assert.Empty(rows.Single(row => row.RoleName == RoleNames.Student).Permissions);
        Assert.Empty(rows.Single(row => row.RoleName == RoleNames.GraduateStaff).Permissions);
        Assert.Equal(
            new[] { Permissions.RolesAssign, Permissions.UsersManage },
            rows.Single(row => row.RoleName == RoleNames.Administrator)
                .Permissions.OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_permissions_page_requires_the_Users_Manage_capability()
    {
        _actor.Permissions = [Permissions.RolesAssign];
        var handler = new GetRolePermissionsHandler(_actor);

        var result = await handler.HandleAsync(new GetRolePermissionsQuery());

        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
    }

    private static GrantRoleCommand Command(string userId, string roleName) =>
        new(userId, roleName, new RequestAuditContext("198.51.100.7", "xunit", "corr-1"));

    private static RevokeRoleCommand Revoke(string userId, string roleName) =>
        new(userId, roleName, new RequestAuditContext("198.51.100.7", "xunit", "corr-1"));

    private static IdentityUserDetail Detail(string id, LocalUserKind kind, IReadOnlyList<string> roles) =>
        new(id, "کاربر آزمایشی", "001****67", kind, roles);
}
