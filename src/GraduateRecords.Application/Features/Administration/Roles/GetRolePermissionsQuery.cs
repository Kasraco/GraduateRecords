using GraduateRecords.Application.Common.Messaging;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Security;

namespace GraduateRecords.Application.Features.Administration.Roles;

/// <summary>
/// One row of the read-only role → permission view.
/// </summary>
public sealed record RolePermissionItem(string RoleName, IReadOnlyList<string> Permissions);

/// <summary>
/// Read-only view of the code-defined role → permission catalog (ADR-0017 §6–§8).
/// <para>
/// v1 provides **no** editing of this mapping and no per-user permission
/// assignment (ADR-0004 §2, ADR-0017 §6/§10): the catalog is code-defined and
/// reconciled into Identity role claims at startup. This query only projects
/// it. Gated by <c>Users.Manage</c>.
/// </para>
/// </summary>
public sealed record GetRolePermissionsQuery : IQuery<IReadOnlyList<RolePermissionItem>>;

public sealed class GetRolePermissionsHandler(ICurrentUser currentUser)
    : IQueryHandler<GetRolePermissionsQuery, IReadOnlyList<RolePermissionItem>>
{
    public Task<Result<IReadOnlyList<RolePermissionItem>>> HandleAsync(
        GetRolePermissionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!currentUser.HasPermission(Permissions.UsersManage))
        {
            return Task.FromResult(Result<IReadOnlyList<RolePermissionItem>>.Failure(
                AdministrationErrors.PermissionDenied()));
        }

        // Deterministic order: ADR-0017 §2 canonical names.
        string[] order = [RoleNames.Student, RoleNames.GraduateStaff, RoleNames.Administrator];

        IReadOnlyList<RolePermissionItem> items = order
            .Select(roleName => new RolePermissionItem(
                roleName,
                RolePermissionCatalog.GetPermissions(roleName)))
            .ToArray();

        return Task.FromResult(Result<IReadOnlyList<RolePermissionItem>>.Success(items));
    }
}
