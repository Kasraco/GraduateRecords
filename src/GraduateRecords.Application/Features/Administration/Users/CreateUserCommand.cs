using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Messaging;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Application.Common.Time;
using GraduateRecords.Domain.Auditing;
using GraduateRecords.Domain.Common;

namespace GraduateRecords.Application.Features.Administration.Users;

/// <summary>
/// Creates a local user account (ADR-0002 §2.1: National ID, full name,
/// role(s), mobile number — supplied by an Administrator and audited). No
/// password, token or any other credential is ever created here: credentials
/// are a separate, explicitly authorized operation (ADR-0016 §5/§7). Students
/// are never created by hand — they are provisioned just-in-time at first SSO
/// sign-in (ADR-0002 §1.2/§3), so only Staff/Administrator kinds are accepted.
/// Gated by <c>Users.Manage</c> (ADR-0017 §9).
/// </summary>
public sealed record CreateUserCommand(
    string? NationalId,
    string? FullName,
    string? MobileNumber,
    LocalUserKind Kind,
    IReadOnlyList<string>? Roles,
    RequestAuditContext Request) : ICommand<string>;

public sealed class CreateUserHandler(
    IIdentityAdministration identity,
    ICurrentUser currentUser,
    IAuditWriter auditWriter,
    IClock clock) : ICommandHandler<CreateUserCommand, string>
{
    private const int FullNameMaxLength = 200;

    public async Task<Result<string>> HandleAsync(
        CreateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // ADR-0017 §21: the capability check runs first, fail-closed.
        if (!currentUser.HasPermission(Permissions.UsersManage))
        {
            await AuditDeniedAsync(command, "کاربر جدید ایجاد نشد؛ دسترسی لازم وجود ندارد.", cancellationToken);
            return Result<string>.Failure(AdministrationErrors.PermissionDenied());
        }

        // AGENTS §16: display text is normalized (trim, line endings, NFC,
        // control/bidi stripping, ZWNJ kept) before it is validated or stored.
        // The command keeps whatever the client sent; only the value that is
        // persisted and validated is the normalized one.
        var fullName = PersianTextNormalizer.NormalizeDisplayText(command.FullName);

        var validation = Validate(command, fullName);
        if (validation is not null)
        {
            return Result<string>.Failure(validation);
        }

        var nationalId = NationalId.Parse(command.NationalId!);
        var request = new CreateIdentityUser(
            nationalId.Value,
            fullName,
            NormalizeMobile(command.MobileNumber!),
            command.Kind,
            command.Roles ?? Array.Empty<string>());

        var created = await identity.CreateUserAsync(request, cancellationToken);
        if (!created.IsSuccess || created.Value is null)
        {
            return Result<string>.Failure(created.Error ?? AdministrationErrors.UserCreateFailed());
        }

        // Best-effort structured security event (Phase 1A; the durable AuditLog
        // arrives with Phase 1C — ADR-0016 §12.4, ADR-0017 §22). Details are an
        // allow-listed, non-sensitive shape: kind and role names only, never the
        // National ID, mobile number or any credential (AGENTS §14).
        await AdministrationAudit.SuccessAsync(
            auditWriter,
            clock,
            currentUser,
            AuditActions.AuthorizationUserCreated,
            entityId: created.Value,
            summary: "کاربر جدید توسط مدیر سامانه ایجاد شد.",
            detailsJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                Kind = command.Kind.ToString(),
                Roles = request.Roles,
            }),
            command.Request,
            cancellationToken);

        return Result<string>.Success(created.Value);
    }

    /// <summary>
    /// Input-shape rules (AGENTS §5: shape in the layer that sees the request,
    /// business rules in Application). Persian messages; no free-form regex and
    /// no normalization re-implementation (§16): the display value arrives
    /// already normalized by <see cref="PersianTextNormalizer.NormalizeDisplayText"/>
    /// and is validated against that result; identifiers are canonicalized with
    /// the §16 identifier rules. Validation is not weakened by normalization —
    /// a name that normalizes to nothing is still rejected as required.
    /// </summary>
    private static Error? Validate(CreateUserCommand command, string fullName)
    {
        if (!NationalId.TryParse(command.NationalId, out _))
        {
            return AdministrationErrors.Validation("شناسه ملی واردشده معتبر نیست.");
        }

        if (string.IsNullOrEmpty(fullName))
        {
            return AdministrationErrors.Validation("نام و نام خانوادگی الزامی است.");
        }

        if (fullName.Length > FullNameMaxLength)
        {
            return AdministrationErrors.Validation(
                $"نام و نام خانوادگی نمی‌تواند بیش از {FullNameMaxLength} نویسه باشد.");
        }

        if (!IsValidMobile(command.MobileNumber))
        {
            return AdministrationErrors.Validation("شماره موبایل معتبر نیست (قالب ۰۹۱۲۳۴۵۶۷۸۹).");
        }

        // ADR-0015 §6 + ADR-0002 §3: Student accounts come from just-in-time
        // Sama provisioning, never from this screen.
        if (command.Kind is not (LocalUserKind.Staff or LocalUserKind.Administrator))
        {
            return AdministrationErrors.Validation(
                "ایجاد حساب دانشجویی دستی مجاز نیست؛ دانشجویان به‌صورت خودکار ایجاد می‌شوند.");
        }

        foreach (var role in command.Roles ?? Array.Empty<string>())
        {
            // ADR-0017 §11: only code-defined supported roles may be assigned.
            if (!RoleNames.IsSupported(role))
            {
                return AdministrationErrors.UnsupportedRole();
            }
        }

        return null;
    }

    /// <summary>Canonical mobile identifier: ASCII digits, trimmed, <c>09xxxxxxxxx</c> (§16).</summary>
    private static string NormalizeMobile(string raw) =>
        PersianTextNormalizer.ToLatinDigits(raw).Trim();

    private static bool IsValidMobile(string? raw) =>
        NormalizeMobile(raw ?? string.Empty) is { Length: 11 } mobile
        && mobile[0] == '0'
        && mobile[1] == '9'
        && mobile.Skip(2).All(static c => c is >= '0' and <= '9');

    private Task AuditDeniedAsync(CreateUserCommand command, string summary, CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(
            new AuditEntry(
                clock.UtcNow,
                currentUser.UserId,
                currentUser.DisplayName,
                AuditActions.AuthorizationUserCreated,
                "User",
                EntityId: string.Empty,
                AuditResult.Denied,
                summary,
                DetailsJson: null,
                command.Request.ClientIpAddress,
                command.Request.UserAgent,
                command.Request.CorrelationId),
            cancellationToken);
}
