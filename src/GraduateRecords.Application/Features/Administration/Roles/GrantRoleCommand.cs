using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Messaging;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Application.Common.Time;
using GraduateRecords.Domain.Auditing;

namespace GraduateRecords.Application.Features.Administration.Roles;

/// <summary>
/// Result of a role-membership change: <see cref="Changed"/> is <c>false</c>
/// when the user already held (or no longer held) that role — a no-op success
/// that is deliberately not audited as a change.
/// </summary>
public sealed record RoleChangeResult(bool Changed);

/// <summary>
/// Grants one supported role to a user (ADR-0017 §10: grant and revoke are one
/// <c>Roles.Assign</c> capability). The delegation rule of §11 applies: the
/// actor needs only <c>Roles.Assign</c> and does **not** have to hold the
/// permissions carried by the granted role, and granting never transfers those
/// permissions to the actor.
/// </summary>
public sealed record GrantRoleCommand(
    string UserId,
    string RoleName,
    RequestAuditContext Request) : ICommand<RoleChangeResult>;

public sealed class GrantRoleHandler(
    IIdentityAdministration identity,
    ICurrentUser currentUser,
    IAuditWriter auditWriter,
    IClock clock) : ICommandHandler<GrantRoleCommand, RoleChangeResult>
{
    public async Task<Result<RoleChangeResult>> HandleAsync(
        GrantRoleCommand command,
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
                    AuditActions.AuthorizationRoleGranted,
                    entityId: command.UserId?.Trim() ?? string.Empty,
                    summary: "اعطای نقش انجام نشد: " + denial.Message,
                    command.Request,
                    cancellationToken);
            }

            return Result<RoleChangeResult>.Failure(denial);
        }

        var granted = await identity.GrantRoleAsync(command.UserId!.Trim(), command.RoleName!, cancellationToken);
        if (!granted.IsSuccess)
        {
            return Result<RoleChangeResult>.Failure(granted.Error ?? AdministrationErrors.RoleMutationFailed());
        }

        if (granted.Value)
        {
            await AdministrationAudit.SuccessAsync(
                auditWriter,
                clock,
                currentUser,
                AuditActions.AuthorizationRoleGranted,
                command.UserId!.Trim(),
                summary: "نقش به کاربر اعطا شد.",
                detailsJson: System.Text.Json.JsonSerializer.Serialize(new { Role = command.RoleName }),
                command.Request,
                cancellationToken);
        }

        return Result<RoleChangeResult>.Success(new RoleChangeResult(granted.Value));
    }
}
