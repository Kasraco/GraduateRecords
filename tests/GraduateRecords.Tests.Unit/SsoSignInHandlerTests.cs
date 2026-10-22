using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Messaging;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Time;
using GraduateRecords.Application.Features.Authentication.SsoSignIn;
using GraduateRecords.Domain.Auditing;

namespace GraduateRecords.Tests.Unit;

public sealed class SsoSignInHandlerTests
{
    private const string ValidId = "0012345679";
    private const string CorrelationId = "corr-1";

    [Fact]
    public async Task Known_user_with_valid_national_id_gets_a_ticket_without_audit_noise()
    {
        var users = new FakeDirectory { User = new LocalUser("user-1", "کاربر آزمایشی") };
        var audit = new FakeAuditWriter();
        var handler = new SsoSignInHandler(users, audit, new FakeClock());

        var result = await handler.HandleAsync(Query(ValidId));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("user-1", result.Value!.UserId);
        Assert.Equal("کاربر آزمایشی", result.Value.FullName);
        Assert.Equal(ValidId, users.LastQueriedId);
        Assert.Empty(audit.Entries); // Successful sign-in is audited by the callback, not here.
    }

    [Fact]
    public async Task Persian_digit_claim_is_normalized_before_the_user_lookup()
    {
        var users = new FakeDirectory { User = new LocalUser("user-1", "نام") };
        var handler = new SsoSignInHandler(users, new FakeAuditWriter(), new FakeClock());

        var result = await handler.HandleAsync(Query("۰۰۱۲۳۴۵۶۷۹"));

        Assert.True(result.IsSuccess);
        Assert.Equal(ValidId, users.LastQueriedId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0012345678")] // bad check digit
    [InlineData("abc")]
    public async Task Invalid_national_id_claim_is_denied_and_audited_without_the_value(string? claim)
    {
        var users = new FakeDirectory { User = new LocalUser("user-1", "نام") };
        var audit = new FakeAuditWriter();
        var handler = new SsoSignInHandler(users, audit, new FakeClock());

        var result = await handler.HandleAsync(Query(claim));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Equal(SsoSignInHandler.InvalidNationalIdCode, result.Error.Code);
        Assert.Null(users.LastQueriedId); // Never reaches the user store.

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.AuthenticationInvalidNationalId, entry.Action);
        Assert.Equal(AuditResult.Denied, entry.Result);
        Assert.Equal(CorrelationId, entry.CorrelationId);
        Assert.Null(entry.DetailsJson);
        AssertNoNationalIdLeak(entry, claim);
    }

    [Fact]
    public async Task Unknown_national_id_is_forbidden_and_audited_with_a_category_only()
    {
        var users = new FakeDirectory { User = null };
        var audit = new FakeAuditWriter();
        var handler = new SsoSignInHandler(users, audit, new FakeClock());

        var result = await handler.HandleAsync(Query(ValidId));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
        Assert.Equal(SsoSignInHandler.UnknownUserCode, result.Error.Code);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.AuthenticationUnknownUserDenied, entry.Action);
        Assert.Equal(AuditResult.Denied, entry.Result);
        Assert.Null(entry.ActorUserId);
        AssertNoNationalIdLeak(entry, ValidId);
    }

    [Fact]
    public async Task Audit_entries_carry_the_http_metadata_supplied_by_the_callback()
    {
        var users = new FakeDirectory { User = null };
        var audit = new FakeAuditWriter();
        var handler = new SsoSignInHandler(users, audit, new FakeClock());

        await handler.HandleAsync(new SsoSignInQuery(ValidId, "192.0.2.10", "ua-test", CorrelationId));

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("192.0.2.10", entry.IpAddress);
        Assert.Equal("ua-test", entry.UserAgent);
        Assert.Equal(CorrelationId, entry.CorrelationId);
        Assert.Equal(FakeClock.Now, entry.OccurredAtUtc); // No DateTime.UtcNow in Application (AGENTS §6).
    }

    private static SsoSignInQuery Query(string? claim) => new(claim, null, null, CorrelationId);

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
