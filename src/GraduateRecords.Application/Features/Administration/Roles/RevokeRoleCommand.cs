using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Messaging;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Application.Common.Time;
using GraduateRecords.Domain.Auditing;

namespace GraduateRecords.Application.Features.Administration.Roles;

/// <summary>
/// Revokes one supported role from a user (ADR-0017 §10).
/// <para>
/// <b>Last-effective-Administrator invariant (ADR-0017 §17–§19).</b> The rule is
/// decided here, in Application, from one read of the effective-administrator
/// set (§18: <c>Administrator</c> role membership — no account-state model
/// exists yet): revoking <c>Administrator</c> is refused with
/// <c>identity.admin.last-administrator</c> when that user is the only
/// effective Administrator. The very same observed set is handed to the port as
/// the mutation precondition, so the port can re-check it inside its locked
/// transaction and return <c>identity.admin.concurrent-change</c> instead of
/// applying a decision made against stale state.
/// </para>
/// </summary>
public sealed record RevokeRoleCommand(
    string UserId,
    string RoleName,
    RequestAuditContext Request) : ICommand<RoleChangeResult>;

public sealed class RevokeRoleHandler(
    IIdentityAdministration identity,
    ICurrentUser currentUser,
    IAuditWriter auditWriter,
    IClock clock) : ICommandHandler<RevokeRoleCommand, RoleChangeResult>
{
    public async Task<Result<RoleChangeResult>> HandleAsync(
        RevokeRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var denial = await RoleChangeGuard.CheckAsync(
            command.UserId, command.RoleName, currentUser, identity, cancellationToken);
        if (denial is not null)
        {
            if (RoleChangeGuard.IsSecurityDenial(denial))
            {
                await AdministrationAudit.DeniedAsync(
                    auditWriter,
                    clock,
                    currentUser,
                    AuditActions.AuthorizationRoleRevoked,
                    entityId: command.UserId?.Trim() ?? string.Empty,
                    summary: "حذف نقش انجام نشد: " + denial.Message,
                    command.Request,
                    cancellationToken);
            }

            return Result<RoleChangeResult>.Failure(denial);
        }

        IReadOnlyCollection<string>? observedAdministrators = null;
        if (string.Equals(command.RoleName, RoleNames.Administrator, StringComparison.Ordinal))
        {
            observedAdministrators = await identity.GetEffectiveAdministratorIdsAsync(cancellationToken);

            // ADR-0017 §17–§18: refuse before any write when this removal would
            // leave the system without an effective Administrator.
            var targetIsLastAdministrator =
                observedAdministrators.Any(id => string.Equals(id, command.UserId!.Trim(), StringComparison.Ordinal))
                && observedAdministrators.Count <= 1;

            if (targetIsLastAdministrator)
            {
                var error = AdministrationErrors.LastAdministrator();
                await AdministrationAudit.DeniedAsync(
                    auditWriter,
                    clock,
                    currentUser,
                    AuditActions.AuthorizationRoleRevoked,
                    command.UserId!.Trim(),
                    summary: "حذف نقش انجام نشد: " + error.Message,
                    command.Request,
                    cancellationToken);
                return Result<RoleChangeResult>.Failure(error);
            }
        }

        var revoked = await identity.RevokeRoleAsync(
            command.UserId!.Trim(),
            command.RoleName!,
            observedAdministrators,
            cancellationToken);

        if (!revoked.IsSuccess)
        {
            // A concurrent change to the administrator set is a security-relevant
            // denial, not an ordinary failure — it is audited as such.
            if (revoked.Error is not null && RoleChangeGuard.IsSecurityDenial(revoked.Error))
            {
                await AdministrationAudit.DeniedAsync(
                    auditWriter,
                    clock,
                    currentUser,
                    AuditActions.AuthorizationRoleRevoked,
                    command.UserId!.Trim(),
                    summary: "حذف نقش انجام نشد: " + revoked.Error.Message,
                    command.Request,
                    cancellationToken);
            }

            return Result<RoleChangeResult>.Failure(revoked.Error ?? AdministrationErrors.RoleMutationFailed());
        }

        if (revoked.Value)
        {
            await AdministrationAudit.SuccessAsync(
                auditWriter,
                clock,
                currentUser,
                AuditActions.AuthorizationRoleRevoked,
                command.UserId!.Trim(),
                summary: "نقش از کاربر حذف شد.",
                detailsJson: System.Text.Json.JsonSerializer.Serialize(new { Role = command.RoleName }),
                command.Request,
                cancellationToken);
        }

        return Result<RoleChangeResult>.Success(new RoleChangeResult(revoked.Value));
    }
}
