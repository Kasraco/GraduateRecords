using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Messaging;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Time;
using GraduateRecords.Application.Features.Authentication.LocalSignIn;
using GraduateRecords.Domain.Auditing;

namespace GraduateRecords.Tests.Unit;

/// <summary>
/// ADR-0015 §1/§2/§4/§6/§8: the local credential sign-in decision permits only
/// valid National IDs that map to Staff/Administrator accounts, rejects Students
/// server-side, audits by category without leaking identifiers, and returns one
/// generic user-facing message for every failure.
/// </summary>
public sealed class LocalSignInHandlerTests
{
    private const string ValidId = "0012345679";
    private const string CorrelationId = "corr-1";

    [Theory]
    [InlineData(LocalUserKind.Staff)]
    [InlineData(LocalUserKind.Administrator)]
    public async Task Eligible_accounts_get_a_ticket_without_audit_noise(LocalUserKind kind)
    {
        var users = new FakeDirectory { User = new LocalUser("user-1", "کاربر آزمایشی", kind) };
        var audit = new FakeAuditWriter();
        var handler = new LocalSignInHandler(users, audit, new FakeClock());

        var result = await handler.HandleAsync(Query(ValidId));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("user-1", result.Value!.UserId);
        Assert.Equal("کاربر آزمایشی", result.Value.FullName);
        Assert.Equal(ValidId, users.LastQueriedId);
        Assert.Empty(audit.Entries); // Successful local sign-in is audited by the controller, not here.
    }

    [Fact]
    public async Task A_student_account_is_denied_even_though_it_exists()
    {
        var users = new FakeDirectory { User = new LocalUser("user-1", "دانشجو", LocalUserKind.Student) };
        var audit = new FakeAuditWriter();
        var handler = new LocalSignInHandler(users, audit, new FakeClock());

        var result = await handler.HandleAsync(Query(ValidId));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
        Assert.Equal(LocalSignInHandler.NotPermittedCode, result.Error.Code);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.AuthenticationLocalSignInFailed, entry.Action);
        Assert.Equal(AuditResult.Denied, entry.Result);
        Assert.Equal(LocalSignInHandler.NotPermittedSummary, entry.Summary);
        AssertNoNationalIdLeak(entry, ValidId);
    }

    [Fact]
    public async Task Persian_digit_input_is_normalized_before_the_user_lookup()
    {
        var users = new FakeDirectory { User = new LocalUser("user-1", "نام", LocalUserKind.Staff) };
        var handler = new LocalSignInHandler(users, new FakeAuditWriter(), new FakeClock());

        var result = await handler.HandleAsync(Query("۰۰۱۲۳۴۵۶۷۹"));

        Assert.True(result.IsSuccess);
        Assert.Equal(ValidId, users.LastQueriedId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0012345678")] // bad check digit
    [InlineData("abc")]
    public async Task Invalid_national_id_input_is_denied_before_the_store_and_audited_without_the_value(string? raw)
    {
        var users = new FakeDirectory { User = new LocalUser("user-1", "نام", LocalUserKind.Staff) };
        var audit = new FakeAuditWriter();
        var handler = new LocalSignInHandler(users, audit, new FakeClock());

        var result = await handler.HandleAsync(Query(raw));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Equal(LocalSignInHandler.InvalidNationalIdCode, result.Error.Code);
        Assert.Equal(LocalSignInHandler.GenericFailureMessage, result.Error.Message);
        Assert.Null(users.LastQueriedId); // Never reaches the user store.

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.AuthenticationLocalSignInFailed, entry.Action);
        Assert.Equal(AuditResult.Denied, entry.Result);
        Assert.Equal(CorrelationId, entry.CorrelationId);
        Assert.Null(entry.DetailsJson);
        AssertNoNationalIdLeak(entry, raw);
    }

    [Fact]
    public async Task An_unknown_national_id_is_denied_and_audited_with_a_category_only()
    {
        var users = new FakeDirectory { User = null };
        var audit = new FakeAuditWriter();
        var handler = new LocalSignInHandler(users, audit, new FakeClock());

        var result = await handler.HandleAsync(Query(ValidId));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
        Assert.Equal(LocalSignInHandler.UnknownUserCode, result.Error.Code);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.AuthenticationLocalSignInFailed, entry.Action);
        Assert.Equal(AuditResult.Denied, entry.Result);
        Assert.Equal(LocalSignInHandler.UnknownUserSummary, entry.Summary);
        Assert.Null(entry.ActorUserId);
        AssertNoNationalIdLeak(entry, ValidId);
    }

    [Theory]
    [InlineData(LocalSignInHandler.InvalidNationalIdCode)]
    [InlineData(LocalSignInHandler.UnknownUserCode)]
    [InlineData(LocalSignInHandler.NotPermittedCode)]
    public async Task Every_failure_carries_the_same_generic_user_facing_message(string code)
    {
        var audit = new FakeAuditWriter();
        var handler = new LocalSignInHandler(
            new FakeDirectory
            {
                // Enough state for whichever branch the code selects.
                User = code == LocalSignInHandler.UnknownUserCode
                    ? null
                    : new LocalUser("user-1", "نام", LocalUserKind.Student),
            },
            audit,
            new FakeClock());

        var raw = code == LocalSignInHandler.InvalidNationalIdCode ? "123" : ValidId;
        var result = await handler.HandleAsync(Query(raw));

        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.Error!.Code);
        Assert.Equal(LocalSignInHandler.GenericFailureMessage, result.Error.Message);
    }

    [Fact]
    public async Task Audit_entries_carry_the_http_metadata_supplied_by_the_request()
    {
        var users = new FakeDirectory { User = null };
        var audit = new FakeAuditWriter();
        var handler = new LocalSignInHandler(users, audit, new FakeClock());

        await handler.HandleAsync(new LocalSignInQuery(ValidId, "192.0.2.10", "ua-test", CorrelationId));

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("192.0.2.10", entry.IpAddress);
        Assert.Equal("ua-test", entry.UserAgent);
        Assert.Equal(CorrelationId, entry.CorrelationId);
        Assert.Equal(FakeClock.Now, entry.OccurredAtUtc); // No DateTime.UtcNow in Application (AGENTS §6).
    }

    private static LocalSignInQuery Query(string? raw) => new(raw, null, null, CorrelationId);

    private static void AssertNoNationalIdLeak(AuditEntry entry, string? claim)
    {
        if (string.IsNullOrWhiteSpace(claim))
        {
            return;
        }

        var haystack = string.Join(' ', entry.Summary, entry.DetailsJson, entry.EntityId, entry.ActorDisplayName);
        Assert.DoesNotContain(PersianNormalize(claim), PersianNormalize(haystack));
    }

    private static string PersianNormalize(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                >= '۰' and <= '۹' => (char)('0' + (chars[i] - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (chars[i] - '٠')),
                _ => chars[i],
            };
        }

        return new string(chars);
    }

    private sealed class FakeDirectory : ILocalUserDirectory
    {
        public LocalUser? User { get; set; }

        public string? LastQueriedId { get; private set; }

        public Task<LocalUser?> FindByNationalIdAsync(string normalizedNationalId, CancellationToken cancellationToken = default)
        {
            LastQueriedId = normalizedNationalId;
            return Task.FromResult(User);
        }
    }

    private sealed class FakeAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClock : IClock
    {
        public static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => Now;
    }
}
