using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Messaging;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Time;
using GraduateRecords.Domain.Auditing;
using GraduateRecords.Domain.Common;

namespace GraduateRecords.Application.Features.Authentication.SsoSignIn;

/// <summary>
/// SSO sign-in decision (ADR-0001 rules 5–7): a session may only start for a
/// National ID that (a) is a valid Iranian National ID and (b) maps to an existing
/// local user. Provisioning of unknown users is not part of this use case, so an
/// unknown National ID is denied and audited (ADR-0001 "Audit events (minimum)").
/// </summary>
public sealed class SsoSignInHandler(
    ILocalUserDirectory users,
    IAuditWriter auditWriter,
    IClock clock) : IQueryHandler<SsoSignInQuery, SsoSignInTicket>
{
    public const string InvalidNationalIdCode = "auth.invalid-national-id";
    public const string UnknownUserCode = "auth.unknown-user";

    // Fixed, non-sensitive summaries. No claim value, National ID or payload is recorded.
    internal const string InvalidNationalIdSummary = "شناسه ملی ارسالی از سازمان معتبر نیست.";
    internal const string UnknownUserSummary = "کاربری با این شناسه ملی در سیستم تعریف نشده است.";

    public async Task<Result<SsoSignInTicket>> HandleAsync(SsoSignInQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!NationalId.TryParse(query.RawNationalIdClaim, out var nationalId))
        {
            await WriteAuditAsync(
                AuditActions.AuthenticationInvalidNationalId,
                AuditResult.Denied,
                InvalidNationalIdSummary,
                query,
                cancellationToken);

            return Result<SsoSignInTicket>.Failure(new Error(
                ErrorKind.Validation,
                InvalidNationalIdCode,
                "شناسه ملی ارسال‌شده از سوی سازمان معتبر نیست."));
        }

        var user = await users.FindByNationalIdAsync(nationalId.Value, cancellationToken);
        if (user is null)
        {
            await WriteAuditAsync(
                AuditActions.AuthenticationUnknownUserDenied,
                AuditResult.Denied,
                UnknownUserSummary,
                query,
                cancellationToken);

            return Result<SsoSignInTicket>.Failure(new Error(
                ErrorKind.Forbidden,
                UnknownUserCode,
                "حساب شما در این سیستم تعریف نشده است."));
        }

        return Result<SsoSignInTicket>.Success(new SsoSignInTicket(user.Id, user.FullName));
    }

    private Task WriteAuditAsync(
        string action,
        AuditResult auditResult,
        string summary,
        SsoSignInQuery query,
        CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(
            new AuditEntry(
                clock.UtcNow,
                ActorUserId: null, // The user is unknown or the identity claim is unusable.
                ActorDisplayName: null,
                Action: action,
                EntityType: "User",
                EntityId: string.Empty,
                Result: auditResult,
                Summary: summary,
                DetailsJson: null,
                IpAddress: query.ClientIpAddress,
                UserAgent: query.UserAgent,
                CorrelationId: query.CorrelationId),
            cancellationToken);
}
