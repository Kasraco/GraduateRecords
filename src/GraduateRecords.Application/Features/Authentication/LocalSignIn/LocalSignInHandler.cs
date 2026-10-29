using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Messaging;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Time;
using GraduateRecords.Domain.Auditing;
using GraduateRecords.Domain.Common;

namespace GraduateRecords.Application.Features.Authentication.LocalSignIn;

/// <summary>
/// Local credential sign-in decision (ADR-0015 §1, §4, §6, §9): a session may
/// only proceed to the password check when the entered National ID (a) is a
/// valid Iranian National ID after canonical normalization and (b) maps to an
/// existing local <b>Staff or Administrator</b> account. Students are SSO-only
/// and are rejected here, server-side, even if a password hash exists.
/// Password verification and lockout accounting stay in the Identity plumbing
/// of <c>AccountController</c> (AGENTS §2.2); this handler never sees a password.
/// Every failure returns the same generic user-facing message so the response
/// never reveals whether the National ID exists, whether a local password
/// exists, or which check failed (ADR-0015 §8).
/// </summary>
public sealed class LocalSignInHandler(
    ILocalUserDirectory users,
    IAuditWriter auditWriter,
    IClock clock) : IQueryHandler<LocalSignInQuery, LocalSignInTicket>
{
    public const string InvalidNationalIdCode = "auth.local.invalid-national-id";
    public const string UnknownUserCode = "auth.local.unknown-user";
    public const string NotPermittedCode = "auth.local.not-permitted";

    /// <summary>
    /// The single enumeration-resistant failure message shown for every local
    /// sign-in failure category (ADR-0015 §8). The category is audited
    /// server-side only, never shown to the user.
    /// </summary>
    public const string GenericFailureMessage = "نام کاربری یا گذرواژه نادرست است.";

    // Fixed, non-sensitive summaries. No identifier value or payload is recorded.
    public const string InvalidNationalIdSummary = "شناسه ملی واردشده برای ورود محلی معتبر نیست.";
    public const string UnknownUserSummary = "کاربری با این شناسه ملی در سیستم تعریف نشده است.";
    public const string NotPermittedSummary = "ورود محلی برای این نوع حساب مجاز نیست.";

    public async Task<Result<LocalSignInTicket>> HandleAsync(
        LocalSignInQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!NationalId.TryParse(query.RawNationalId, out var nationalId))
        {
            await WriteAuditAsync(InvalidNationalIdSummary, query, cancellationToken);
            return Failure(ErrorKind.Validation, InvalidNationalIdCode);
        }

        var user = await users.FindByNationalIdAsync(nationalId.Value, cancellationToken);
        if (user is null)
        {
            await WriteAuditAsync(UnknownUserSummary, query, cancellationToken);
            return Failure(ErrorKind.Forbidden, UnknownUserCode);
        }

        // ADR-0015 §2/§6: Students are SSO-only. This gate runs before any
        // password check, so a Student who somehow holds a password hash is
        // still rejected by the ordinary local-login path.
        if (user.Kind is not (LocalUserKind.Staff or LocalUserKind.Administrator))
        {
            await WriteAuditAsync(NotPermittedSummary, query, cancellationToken);
            return Failure(ErrorKind.Forbidden, NotPermittedCode);
        }

        return Result<LocalSignInTicket>.Success(new LocalSignInTicket(user.Id, user.FullName));
    }

    private static Result<LocalSignInTicket> Failure(ErrorKind kind, string code) =>
        Result<LocalSignInTicket>.Failure(new Error(kind, code, GenericFailureMessage));

    private Task WriteAuditAsync(
        string summary,
        LocalSignInQuery query,
        CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(
            new AuditEntry(
                clock.UtcNow,
                ActorUserId: null, // The account is unknown or not permitted; no session identity exists.
                ActorDisplayName: null,
                Action: AuditActions.AuthenticationLocalSignInFailed,
                EntityType: "User",
                EntityId: string.Empty,
                Result: AuditResult.Denied,
                Summary: summary,
                DetailsJson: null,
                IpAddress: query.ClientIpAddress,
                UserAgent: query.UserAgent,
                CorrelationId: query.CorrelationId),
            cancellationToken);
}
