using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Messaging;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Security;

namespace GraduateRecords.Application.Features.Administration.Users;

/// <summary>
/// One user's administration detail (ADR-0004 §8.2 allowlist: name, masked
/// National ID, kind, roles — no student-profile link, no credential data).
/// Gated by <c>Users.Manage</c> (ADR-0017 §9).
/// </summary>
public sealed record GetUserQuery(string UserId) : IQuery<IdentityUserDetail>;

public sealed class GetUserHandler(IIdentityAdministration identity, ICurrentUser currentUser)
    : IQueryHandler<GetUserQuery, IdentityUserDetail>
{
    public async Task<Result<IdentityUserDetail>> HandleAsync(
        GetUserQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!currentUser.HasPermission(Permissions.UsersManage))
        {
            return Result<IdentityUserDetail>.Failure(AdministrationErrors.PermissionDenied());
        }

        if (string.IsNullOrWhiteSpace(query.UserId))
        {
            return Result<IdentityUserDetail>.Failure(AdministrationErrors.UserNotFound());
        }

        var user = await identity.FindUserAsync(query.UserId.Trim(), cancellationToken);
        return user is null
            ? Result<IdentityUserDetail>.Failure(AdministrationErrors.UserNotFound())
            : Result<IdentityUserDetail>.Success(user);
    }
}
