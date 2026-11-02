using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Security;

namespace GraduateRecords.Application.Features.Administration.Roles;

/// <summary>
/// The checks every role-membership change performs before it touches the port,
/// in order (ADR-0017 §10 capability, §11 supported roles only, §20
/// self-role-change ban, then target existence). Shared by grant and revoke so
/// the two paths can never drift apart.
/// </summary>
internal static class RoleChangeGuard
{
    /// <summary>Returns the expected failure, or <c>null</c> when the change may proceed.</summary>
    public static async Task<Error?> CheckAsync(
        string? userId,
        string? roleName,
        ICurrentUser currentUser,
        IIdentityAdministration identity,
        CancellationToken cancellationToken)
    {
        // ADR-0017 §10: role grant/revoke is the single Roles.Assign capability.
        if (!currentUser.HasPermission(Permissions.RolesAssign))
        {
            return AdministrationErrors.PermissionDenied();
        }

        // ADR-0017 §11: only code-defined supported roles exist; anything else
        // (including a look-alike spelling) is rejected, never granted.
        if (!RoleNames.IsSupported(roleName))
        {
            return AdministrationErrors.UnsupportedRole();
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return AdministrationErrors.UserNotFound();
        }

        // ADR-0017 §20 / ADR-0004 §1.3: an Administrator cannot change their own
        // roles — separate from last-Administrator protection and never waived.
        if (string.Equals(userId.Trim(), currentUser.UserId, StringComparison.Ordinal))
        {
            return AdministrationErrors.SelfRoleChange();
        }

        var target = await identity.FindUserAsync(userId.Trim(), cancellationToken);
        return target is null ? AdministrationErrors.UserNotFound() : null;
    }

    /// <summary>
    /// True when the failure is an authorization/invariant denial worth a
    /// best-effort security event (ADR-0004 §9): a forbidden capability, the
    /// last-Administrator refusal, or a lost Administrator precondition. Plain
    /// input validation is not audited.
    /// </summary>
    public static bool IsSecurityDenial(Error error) =>
        error.Kind == ErrorKind.Forbidden
        || string.Equals(error.Code, AdministrationErrors.LastAdministratorCode, StringComparison.Ordinal)
        || string.Equals(error.Code, AdministrationErrors.ConcurrentAdministratorChangeCode, StringComparison.Ordinal);
}
