using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Messaging;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Security;

namespace GraduateRecords.Application.Features.Administration.Users;

/// <summary>
/// Paged administration user list (ADR-0002 §8.2: the screen shows account data
/// only — name, masked National ID, kind, roles). Gated by <c>Users.Manage</c>
/// (ADR-0017 §9).
/// </summary>
public sealed record ListUsersQuery(string? SearchTerm, int Page) : IQuery<IdentityUserPage>;

public sealed class ListUsersHandler(IIdentityAdministration identity, ICurrentUser currentUser)
    : IQueryHandler<ListUsersQuery, IdentityUserPage>
{
    /// <summary>DESIGN §8: page size 20.</summary>
    public const int PageSize = 20;

    public async Task<Result<IdentityUserPage>> HandleAsync(
        ListUsersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // ADR-0017 §21: fail closed before any data is read.
        if (!currentUser.HasPermission(Permissions.UsersManage))
        {
            return Result<IdentityUserPage>.Failure(AdministrationErrors.PermissionDenied());
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var users = await identity.ListUsersAsync(query.SearchTerm, page, PageSize, cancellationToken);
        return Result<IdentityUserPage>.Success(users);
    }
}
