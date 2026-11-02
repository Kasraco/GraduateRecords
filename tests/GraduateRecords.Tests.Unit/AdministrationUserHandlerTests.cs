using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Application.Features.Administration;
using GraduateRecords.Application.Features.Administration.Users;
using GraduateRecords.Domain.Auditing;

namespace GraduateRecords.Tests.Unit;

/// <summary>
/// UC3 user-account rules (ADR-0002 §2.1 creation, ADR-0017 §9 <c>Users.Manage</c>,
/// §21 fail-closed, §1/§4 kind-independence) with fakes for the ports
/// (AGENTS §22).
/// </summary>
public sealed class AdministrationUserHandlerTests
{
    /// <summary>Checksum-valid test identifier (algorithm as in NationalIdTests).</summary>
    private const string ValidNationalId = "4000000004";

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

    private CreateUserHandler CreateHandler => new(_identity, _actor, _audit, _clock);

    private ListUsersHandler ListHandler => new(_identity, _actor);

    private GetUserHandler GetHandler => new(_identity, _actor);

    [Fact]
    public async Task Create_requires_the_Users_Manage_capability()
    {
        _actor.Permissions = [Permissions.RolesAssign]; // Roles.Assign alone is not enough

        var result = await CreateHandler.HandleAsync(ValidCommand());

        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
        Assert.Equal(AdministrationErrors.PermissionDeniedCode, result.Error.Code);
        Assert.Empty(_identity.Creations);
        Assert.Equal(AuditResult.Denied, Assert.Single(_audit.Entries).Result);
    }

    [Fact]
    public async Task Create_rejects_an_invalid_national_id()
    {
        var result = await CreateHandler.HandleAsync(ValidCommand() with { NationalId = "12345" });

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Contains("شناسه ملی", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_identity.Creations);
    }

    [Fact]
    public async Task Create_rejects_an_invalid_mobile_number()
    {
        var result = await CreateHandler.HandleAsync(ValidCommand() with { MobileNumber = "12345" });

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Contains("موبایل", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_identity.Creations);
    }

    /// <summary>ADR-0002 §1.2/§3: students are provisioned just-in-time, never by hand.</summary>
    [Fact]
    public async Task Create_refuses_a_student_kind_account()
    {
        var result = await CreateHandler.HandleAsync(ValidCommand() with { Kind = LocalUserKind.Student });

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Contains("دانشجویی", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_identity.Creations);
    }

    [Fact]
    public async Task Create_rejects_a_role_outside_the_supported_set()
    {
        var result = await CreateHandler.HandleAsync(
            ValidCommand() with { Roles = ["Staff"] });

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Equal(AdministrationErrors.UnsupportedRoleCode, result.Error.Code);
        Assert.Empty(_identity.Creations);
    }

    /// <summary>
    /// Canonical boundary normalization (AGENTS §16): Persian digits become
    /// ASCII before the value reaches the port, and the audit payload carries
    /// only kind and role names — never the National ID or mobile number.
    /// </summary>
    [Fact]
    public async Task Create_normalizes_identifiers_and_audits_without_personal_payload()
    {
        var command = ValidCommand() with
        {
            NationalId = "۴۰۰۰۰۰۰۰۰۴",
            MobileNumber = "۰۹۱۲۳۴۵۶۷۸۹",
            Roles = [RoleNames.GraduateStaff],
        };

        var result = await CreateHandler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-user-id", result.Value);
        var request = Assert.Single(_identity.Creations);
        Assert.Equal(ValidNationalId, request.NationalId);
        Assert.Equal("09123456789", request.MobileNumber);
        Assert.Equal(LocalUserKind.Staff, request.Kind);

        var audit = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.AuthorizationUserCreated, audit.Action);
        Assert.Equal(AuditResult.Success, audit.Result);
        Assert.Equal("new-user-id", audit.EntityId);
        Assert.Contains(RoleNames.GraduateStaff, audit.DetailsJson);
        Assert.DoesNotContain(ValidNationalId, audit.DetailsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("09123456789", audit.DetailsJson, StringComparison.Ordinal);
        Assert.Equal("corr-1", audit.CorrelationId);
    }

    [Fact]
    public async Task Create_surfaces_a_duplicate_national_id_without_an_audit_event()
    {
        _identity.CreateResult = Result<string>.Failure(AdministrationErrors.DuplicateNationalId());

        var result = await CreateHandler.HandleAsync(ValidCommand());

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Equal(AdministrationErrors.DuplicateNationalIdCode, result.Error.Code);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task List_requires_the_Users_Manage_capability()
    {
        _actor.Permissions = [];

        var result = await ListHandler.HandleAsync(new ListUsersQuery(null, 1));

        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
        Assert.Empty(_identity.ListedSearchTerms);
    }

    [Fact]
    public async Task List_pages_and_clamps_the_requested_page()
    {
        var result = await ListHandler.HandleAsync(new ListUsersQuery(null, 0));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Page);
        Assert.Equal(ListUsersHandler.PageSize, result.Value.PageSize);
    }

    [Fact]
    public async Task Get_user_requires_the_Users_Manage_capability_and_reports_missing_users()
    {
        _actor.Permissions = [];
        Assert.Equal(
            ErrorKind.Forbidden,
            (await GetHandler.HandleAsync(new GetUserQuery("u1"))).Error!.Kind);

        _actor.Permissions = [Permissions.UsersManage];
        Assert.Equal(
            ErrorKind.NotFound,
            (await GetHandler.HandleAsync(new GetUserQuery("missing"))).Error!.Kind);
    }

    /// <summary>
    /// AGENTS §16: display text is normalized (trim, line endings, NFC,
    /// control/bidi stripping, ZWNJ kept), never rejected for containing a
    /// character the standard says to strip.
    /// </summary>
    [Fact]
    public async Task Create_normalizes_display_text_instead_of_rejecting_it()
    {
        var dirty = "  علی\r\nمحمد" + (char)0x0007 + (char)0x202E + "  ";
        var result = await CreateHandler.HandleAsync(ValidCommand() with { FullName = dirty });

        Assert.True(result.IsSuccess);
        var request = Assert.Single(_identity.Creations);
        Assert.Equal("علی\nمحمد", request.FullName);
    }

    [Fact]
    public async Task Create_keeps_ZWNJ_and_composes_names_to_NFC()
    {
        // ZWNJ (U+200C) must survive; "e" + combining acute (U+0301) must compose to U+00E9.
        var name = "کار" + (char)0x200C + "شناسی e" + (char)0x0301;
        var expected = "کار" + (char)0x200C + "شناسی " + (char)0x00E9;

        var result = await CreateHandler.HandleAsync(ValidCommand() with { FullName = name });

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, Assert.Single(_identity.Creations).FullName);
    }

    [Fact]
    public async Task Create_still_rejects_a_name_that_normalizes_to_nothing()
    {
        // Validation is not weakened: a value made only of stripped characters
        // is empty after normalization and must still fail as required.
        var result = await CreateHandler.HandleAsync(ValidCommand() with
        {
            FullName = ((char)0x0007).ToString() + ((char)0x202E).ToString(),
        });

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Contains("الزامی", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_identity.Creations);
    }

    /// <summary>ADR-0004 §5.3 / DESIGN §17: identity values are always masked at the port.</summary>
    [Theory]
    [InlineData("4000000004", "400****04")]
    [InlineData("123", "****")]
    [InlineData(null, "****")]
    [InlineData("400000000a", "****")]
    public void National_ids_are_masked_before_they_leave_the_port(string? raw, string expected) =>
        Assert.Equal(expected, IdentityDisplayMask.NationalId(raw));

    private static CreateUserCommand ValidCommand() =>
        new(
            ValidNationalId,
            "کارمند آزمایشی",
            "09123456789",
            LocalUserKind.Staff,
            [RoleNames.GraduateStaff],
            new RequestAuditContext("198.51.100.7", "xunit", "corr-1"));
}
