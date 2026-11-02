using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Application.Features.Administration;
using GraduateRecords.Domain.Common;
using GraduateRecords.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace GraduateRecords.Infrastructure.Identity;

/// <summary>
/// <see cref="IIdentityAdministration"/> implemented with ASP.NET Core Identity
/// (AGENTS §2: Identity types stay out of Application; no other identity
/// wrapper exists).
/// <para>
/// <b>Last-Administrator atomicity (ADR-0017 §17–§19).</b> Every path that can
/// grant or revoke the canonical <c>Administrator</c> role —
/// <see cref="GrantRoleAsync"/>, <see cref="RevokeRoleAsync"/> and
/// <see cref="CreateUserAsync"/> when the requested roles include it — runs
/// inside one database transaction whose <b>first statement</b> takes an
/// exclusive key lock on the single <c>AspNetRoles</c> row of
/// <c>Administrator</c> (<c>WITH (UPDLOCK, HOLDLOCK)</c>, held to commit):
/// <list type="bullet">
/// <item>All such transactions contend for the same one row, so they are
/// strictly serialized — no two can interleave their read/validate/mutate
/// sequence, and no deadlock is possible because only one lock is ever
/// taken.</item>
/// <item>Inside that transaction, and before any commit, this class reads the
/// effective-administrator set, compares it with the caller's observed set
/// (the precondition the Application handler decided its rule against) and
/// only then mutates the membership. A mismatch rolls back and returns
/// <c>identity.admin.concurrent-change</c>.</item>
/// <item>The raw SQL and the transaction exist only here (AGENTS §2.1); the
/// *rule* (may this removal happen?) stays in the Application handler.</item>
/// </list>
/// Non-Administrator roles take no lock and no transaction: they cannot change
/// the administrator set (ADR-0017 §18 counts <c>Administrator</c> membership
/// only). Nothing here reads <c>UserKind</c> or the authentication method
/// (ADR-0017 §1/§4/§21).
/// </para>
/// </summary>
public sealed class IdentityAdministration(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    AppDbContext db,
    ILogger<IdentityAdministration> logger) : IIdentityAdministration
{
    private const int DefaultPageSize = 20;

    // ASP.NET Core Identity's own error codes (IdentityErrorDescriber writes
    // nameof(...) as the Code). They are framework contract strings, not a
    // second naming convention for permissions or roles.
    private const string DuplicateUserNameCode = "DuplicateUserName";
    private const string ConcurrencyFailureCode = "ConcurrencyFailure";

    public async Task<IdentityUserPage> ListUsersAsync(
        string? searchTerm,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? DefaultPageSize : pageSize;

        var query = db.Users.AsNoTracking();

        // AGENTS §16: the query input is normalized at the boundary (digits
        // folded, ي→ی / ك→ک folded, whitespace collapsed) and the stored display
        // name is folded the same way on the SQL side by IdentitySearchPattern,
        // so both sides of the comparison use one representation. The stored
        // value keeps its original characters (§16) and never leaves the
        // database: only the LIKE-matched rows are materialized. Whitespace runs
        // in the stored value collapse only up to four characters wide (two
        // Replace passes) — longer runs are a documented residual limitation of
        // doing this without a normalized column (which would need a migration).
        var normalized = PersianTextNormalizer.NormalizeSearchTerm(searchTerm);
        if (!string.IsNullOrEmpty(normalized))
        {
            var nationalId = PersianTextNormalizer.NormalizeIdentifier(normalized);
            var pattern = IdentitySearchPattern.Build(normalized);

            query = query.Where(user =>
                EF.Functions.Like(
                    user.FullName.Replace("  ", " ").Replace("  ", " "),
                    pattern,
                    IdentitySearchPattern.EscapeCharacter)
                || user.NationalId == nationalId);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var users = await query
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.NationalId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var rolesByUser = await LoadRolesAsync(
            users.Select(user => user.Id).ToList(),
            cancellationToken);

        var items = users
            .Select(user => new IdentityUserSummary(
                user.Id,
                user.FullName,
                IdentityDisplayMask.NationalId(user.NationalId),
                user.UserKind,
                rolesByUser.TryGetValue(user.Id, out var roles) ? roles : Array.Empty<string>()))
            .ToList();

        return new IdentityUserPage(items, totalCount, page, pageSize);
    }

    public async Task<IdentityUserDetail?> FindUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var user = await userManager.FindByIdAsync(userId.Trim());
        if (user is null)
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);
        return new IdentityUserDetail(
            user.Id,
            user.FullName,
            IdentityDisplayMask.NationalId(user.NationalId),
            user.UserKind,
            roles.ToList());
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<string>();
        }

        var user = await userManager.FindByIdAsync(userId.Trim());
        if (user is null)
        {
            return Array.Empty<string>();
        }

        var roles = await userManager.GetRolesAsync(user);
        return roles.ToList();
    }

    public async Task<IReadOnlyList<string>> GetEffectiveAdministratorIdsAsync(
        CancellationToken cancellationToken = default)
    {
        var ids = await ReadEffectiveAdministratorIdsAsync(cancellationToken);
        if (ids.Count == 0 && !await AdministratorRoleExistsAsync(cancellationToken))
        {
            // Fail closed (ADR-0017 §21): without the canonical role row no
            // account is an effective Administrator, so no removal may proceed.
            logger.LogWarning(
                "The canonical {RoleName} role row is missing; effective administrator count resolves to zero.",
                RoleNames.Administrator);
        }

        return ids;
    }

    public async Task<Result<string>> CreateUserAsync(
        CreateIdentityUser request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defence in depth: the Application validator already rejected anything
        // outside the supported set (ADR-0017 §11); re-check here so no path
        // can create membership in a non-canonical role.
        foreach (var roleName in request.Roles)
        {
            if (!RoleNames.IsSupported(roleName))
            {
                return Result<string>.Failure(AdministrationErrors.UnsupportedRole());
            }
        }

        var nationalId = PersianTextNormalizer.NormalizeIdentifier(request.NationalId);
        if (await userManager.FindByNameAsync(nationalId) is not null)
        {
            return Result<string>.Failure(AdministrationErrors.DuplicateNationalId());
        }

        var roles = request.Roles
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Grant path of the invariant: creating an Administrator must serialize
        // with every other administrator mutation (ADR-0017 §17).
        var transaction = roles.Contains(RoleNames.Administrator)
            ? await BeginAdministratorMutationAsync(cancellationToken)
            : null;

        if (roles.Contains(RoleNames.Administrator) && transaction is null)
        {
            return Result<string>.Failure(AdministrationErrors.AdministratorRoleUnavailable());
        }

        try
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString("N"),
                UserName = nationalId,
                NationalId = nationalId,
                FullName = request.FullName.Trim(),
                PhoneNumber = PersianTextNormalizer.ToLatinDigits(request.MobileNumber).Trim(),
                UserKind = request.Kind,
                LockoutEnabled = true,
                SecurityStamp = Guid.NewGuid().ToString("N"),
            };

            // No password: ADR-0016 §7 — provisioning never creates a
            // credential; local credentials come from a separate, explicitly
            // authorized operation that is not part of this use case.
            var created = await userManager.CreateAsync(user);
            if (!created.Succeeded)
            {
                logger.LogWarning(
                    "User creation failed for an administrator-supplied account. Identity errors: {IdentityErrors}",
                    Describe(created));
                return Result<string>.Failure(
                    HasError(created, DuplicateUserNameCode)
                        ? AdministrationErrors.DuplicateNationalId()
                        : AdministrationErrors.UserCreateFailed());
            }

            foreach (var roleName in roles)
            {
                var assigned = await userManager.AddToRoleAsync(user, roleName);
                if (!assigned.Succeeded)
                {
                    // The enclosing finally rolls the whole creation back: never
                    // leave a half-provisioned account behind (ADR-0016 §14.7
                    // spirit).
                    logger.LogWarning(
                        "Role assignment failed during user creation; creation rolled back. Role={RoleName} IdentityErrors={IdentityErrors}",
                        roleName,
                        Describe(assigned));
                    return Result<string>.Failure(AdministrationErrors.RoleMutationFailed());
                }
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return Result<string>.Success(user.Id);
        }
        catch (DbUpdateException ex)
        {
            // Unique-index violation on a racing duplicate (National ID /
            // user name) surfaces as an exception from the Identity store.
            logger.LogWarning(ex, "User creation hit a database uniqueness violation.");
            return Result<string>.Failure(AdministrationErrors.DuplicateNationalId());
        }
        finally
        {
            await DisposeQuietlyAsync(transaction);
        }
    }

    public async Task<Result<bool>> GrantRoleAsync(
        string userId,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        if (!RoleNames.IsSupported(roleName))
        {
            return Result<bool>.Failure(AdministrationErrors.UnsupportedRole());
        }

        var user = await userManager.FindByIdAsync(userId.Trim());
        if (user is null)
        {
            return Result<bool>.Failure(AdministrationErrors.UserNotFound());
        }

        if (!string.Equals(roleName, RoleNames.Administrator, StringComparison.Ordinal))
        {
            // Not an administrator grant: it cannot change the effective
            // administrator set, so no lock or transaction is required.
            if (await userManager.IsInRoleAsync(user, roleName))
            {
                return Result<bool>.Success(false);
            }

            var result = await userManager.AddToRoleAsync(user, roleName);
            return result.Succeeded
                ? Result<bool>.Success(true)
                : Result<bool>.Failure(MapRoleMutationFailure(result));
        }

        var transaction = await BeginAdministratorMutationAsync(cancellationToken);
        if (transaction is null)
        {
            return Result<bool>.Failure(AdministrationErrors.AdministratorRoleUnavailable());
        }

        try
        {
            if (await userManager.IsInRoleAsync(user, roleName))
            {
                await transaction.CommitAsync(cancellationToken);
                return Result<bool>.Success(false);
            }

            var granted = await userManager.AddToRoleAsync(user, roleName);
            if (!granted.Succeeded)
            {
                // The enclosing finally rolls the transaction back.
                return Result<bool>.Failure(MapRoleMutationFailure(granted));
            }

            await transaction.CommitAsync(cancellationToken);
            return Result<bool>.Success(true);
        }
        finally
        {
            await DisposeQuietlyAsync(transaction);
        }
    }

    public async Task<Result<bool>> RevokeRoleAsync(
        string userId,
        string roleName,
        IReadOnlyCollection<string>? observedEffectiveAdministratorIds,
        CancellationToken cancellationToken = default)
    {
        if (!RoleNames.IsSupported(roleName))
        {
            return Result<bool>.Failure(AdministrationErrors.UnsupportedRole());
        }

        var user = await userManager.FindByIdAsync(userId.Trim());
        if (user is null)
        {
            return Result<bool>.Failure(AdministrationErrors.UserNotFound());
        }

        if (!string.Equals(roleName, RoleNames.Administrator, StringComparison.Ordinal))
        {
            if (!await userManager.IsInRoleAsync(user, roleName))
            {
                return Result<bool>.Success(false);
            }

            var result = await userManager.RemoveFromRoleAsync(user, roleName);
            return result.Succeeded
                ? Result<bool>.Success(true)
                : Result<bool>.Failure(MapRoleMutationFailure(result));
        }

        if (observedEffectiveAdministratorIds is null)
        {
            // Fail closed: an Administrator removal without the caller's observed
            // set can never be verified against the §17 rule.
            logger.LogError(
                "Administrator role revocation for {UserId} was requested without an observed administrator set.",
                userId);
            return Result<bool>.Failure(AdministrationErrors.MissingAdministratorPrecondition());
        }

        var transaction = await BeginAdministratorMutationAsync(cancellationToken);
        if (transaction is null)
        {
            return Result<bool>.Failure(AdministrationErrors.AdministratorRoleUnavailable());
        }

        try
        {
            // Read + validate + mutate, all inside the locked transaction.
            var currentAdministrators = await ReadEffectiveAdministratorIdsAsync(cancellationToken);
            if (!SameSet(currentAdministrators, observedEffectiveAdministratorIds))
            {
                logger.LogWarning(
                    "Administrator role revocation for {UserId} lost its precondition: observed {ObservedCount}, current {CurrentCount}.",
                    userId,
                    observedEffectiveAdministratorIds.Count,
                    currentAdministrators.Count);
                // The enclosing finally rolls the transaction back.
                return Result<bool>.Failure(AdministrationErrors.ConcurrentAdministratorChange());
            }

            if (!await userManager.IsInRoleAsync(user, roleName))
            {
                await transaction.CommitAsync(cancellationToken);
                return Result<bool>.Success(false);
            }

            var revoked = await userManager.RemoveFromRoleAsync(user, roleName);
            if (!revoked.Succeeded)
            {
                // The enclosing finally rolls the transaction back.
                return Result<bool>.Failure(MapRoleMutationFailure(revoked));
            }

            await transaction.CommitAsync(cancellationToken);
            return Result<bool>.Success(true);
        }
        finally
        {
            await DisposeQuietlyAsync(transaction);
        }
    }

    /// <summary>
    /// Opens the one transaction every Administrator grant/revoke path uses and
    /// takes its first statement: an exclusive, commit-held key lock on the
    /// single <c>AspNetRoles</c> row of the canonical Administrator role. All
    /// such transactions contend for that same row, so they serialize.
    /// Returns <c>null</c> when the canonical role row is missing — the caller
    /// must fail closed and mutate nothing.
    /// </summary>
    private async Task<IDbContextTransaction?> BeginAdministratorMutationAsync(
        CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var administrator = await roleManager.FindByNameAsync(RoleNames.Administrator);
            if (administrator is null || string.IsNullOrEmpty(administrator.Id))
            {
                await transaction.DisposeAsync();
                return null;
            }

            var lockedRow = await db.Roles
                .FromSqlInterpolated(
                    $"SELECT * FROM [AspNetRoles] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {administrator.Id}")
                .AsNoTracking()
                .Select(role => role.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (lockedRow is null)
            {
                // The row vanished between the lookup and the lock: no lock was
                // taken, so no mutation may proceed.
                await transaction.DisposeAsync();
                return null;
            }

            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Ids of the current effective Administrators, read inside the caller's
    /// transaction (ADR-0017 §18: role membership; no account-state model exists
    /// yet, so membership *is* the effective set).
    /// </summary>
    private async Task<IReadOnlyList<string>> ReadEffectiveAdministratorIdsAsync(
        CancellationToken cancellationToken)
    {
        var administrator = await roleManager.FindByNameAsync(RoleNames.Administrator);
        if (administrator is null || string.IsNullOrEmpty(administrator.Id))
        {
            return Array.Empty<string>();
        }

        var roleKey = administrator.Id;
        return await db.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.RoleId == roleKey)
            .Select(userRole => userRole.UserId)
            .ToListAsync(cancellationToken);
    }

    private async Task<bool> AdministratorRoleExistsAsync(CancellationToken cancellationToken)
    {
        var administrator = await roleManager.FindByNameAsync(RoleNames.Administrator);
        if (administrator is null || string.IsNullOrEmpty(administrator.Id))
        {
            return false;
        }

        var roleKey = administrator.Id;
        return await db.Roles.AsNoTracking().AnyAsync(role => role.Id == roleKey, cancellationToken);
    }

    /// <summary>One query for the whole page — no N+1 (AGENTS §17).</summary>
    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadRolesAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        }

        var memberships = await (
            from userRole in db.UserRoles.AsNoTracking()
            where userIds.Contains(userRole.UserId)
            join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where role.Name != null
            select new { userRole.UserId, RoleName = role.Name! })
            .ToListAsync(cancellationToken);

        return memberships
            .GroupBy(membership => membership.UserId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(membership => membership.RoleName).ToList(),
                StringComparer.Ordinal);
    }

    private static bool SameSet(IReadOnlyCollection<string> current, IReadOnlyCollection<string> observed)
    {
        var currentSet = current.ToHashSet(StringComparer.Ordinal);
        return currentSet.Count == observed.Count && observed.All(currentSet.Contains);
    }

    private static Error MapRoleMutationFailure(IdentityResult result) =>
        HasError(result, ConcurrencyFailureCode)
            ? AdministrationErrors.ConcurrentAdministratorChange()
            : AdministrationErrors.RoleMutationFailed();

    private static bool HasError(IdentityResult result, string code) =>
        result.Errors.Any(error => string.Equals(error.Code, code, StringComparison.Ordinal));

    private static string Describe(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));

    /// <summary>
    /// Disposes an uncommitted transaction, which rolls it back. Never throws:
    /// a rollback failure must not mask the operation's own outcome.
    /// </summary>
    private static async Task DisposeQuietlyAsync(IDbContextTransaction? transaction)
    {
        if (transaction is null)
        {
            return;
        }

        try
        {
            await transaction.DisposeAsync();
        }
        catch
        {
            // Rolling back an already-committed or already-failed transaction is
            // not an error the caller can act on; the outcome is already decided.
        }
    }
}
