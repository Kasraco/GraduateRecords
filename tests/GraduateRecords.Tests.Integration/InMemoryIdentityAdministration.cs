using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Application.Features.Administration;
using GraduateRecords.Domain.Common;
using GraduateRecords.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace GraduateRecords.Tests.Integration;

/// <summary>
/// In-memory <see cref="IIdentityAdministration"/> for HTTP-level tests
/// (AGENTS §22: no SQL Server in CI, never EF InMemory/SQLite).
/// <para>
/// The production implementation needs a real database for the transaction and
/// the <c>UPDLOCK/HOLDLOCK</c> row lock of ADR-0017 §17–§19, so those tests live
/// in <c>SqlServerLastAdministratorInvariantTests</c>. This double implements the
/// <b>same contract semantics</b> — supported-roles-only, an in-process
/// exclusive gate for Administrator mutations, and the observed-set precondition
/// that must match before any Administrator revocation is applied — so the
/// handlers, endpoints, authorization and audit behaviour under test here are
/// exactly the production ones.
/// </para>
/// </summary>
public sealed class InMemoryIdentityAdministration(
    InMemoryUserStore users,
    UserManager<ApplicationUser> userManager) : IIdentityAdministration
{
    /// <summary>Stands in for the database row lock: one gate for every Administrator mutation.</summary>
    private readonly SemaphoreSlim _administratorGate = new(1, 1);

    public async Task<IdentityUserPage> ListUsersAsync(
        string? searchTerm,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;

        var all = users.All.AsEnumerable();

        // AGENTS §16: the query input is normalized at the boundary and the
        // stored display value is folded through the same single Domain
        // implementation before comparison — the production query folds the
        // stored side with IdentitySearchPattern's LIKE classes instead (no
        // normalized column exists without a migration).
        var normalized = PersianTextNormalizer.NormalizeSearchTerm(searchTerm);
        if (!string.IsNullOrEmpty(normalized))
        {
            var nationalId = PersianTextNormalizer.NormalizeIdentifier(normalized);
            all = all.Where(user =>
                PersianTextNormalizer.NormalizeSearchTerm(user.FullName)
                    .Contains(normalized, StringComparison.Ordinal)
                || string.Equals(user.NationalId, nationalId, StringComparison.Ordinal));
        }

        var ordered = all
            .OrderBy(user => user.FullName, StringComparer.Ordinal)
            .ThenBy(user => user.NationalId, StringComparer.Ordinal)
            .ToList();

        var pageItems = new List<IdentityUserSummary>();
        foreach (var user in ordered.Skip((page - 1) * pageSize).Take(pageSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            pageItems.Add(new IdentityUserSummary(
                user.Id,
                user.FullName,
                IdentityDisplayMask.NationalId(user.NationalId),
                user.UserKind,
                (await userManager.GetRolesAsync(user)).ToList()));
        }

        return new IdentityUserPage(pageItems, ordered.Count, page, pageSize);
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

        return new IdentityUserDetail(
            user.Id,
            user.FullName,
            IdentityDisplayMask.NationalId(user.NationalId),
            user.UserKind,
            (await userManager.GetRolesAsync(user)).ToList());
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.Trim());
        return user is null
            ? Array.Empty<string>()
            : (await userManager.GetRolesAsync(user)).ToList();
    }

    public async Task<IReadOnlyList<string>> GetEffectiveAdministratorIdsAsync(
        CancellationToken cancellationToken = default)
    {
        var administrators = new List<string>();
        foreach (var user in users.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await userManager.IsInRoleAsync(user, RoleNames.Administrator))
            {
                administrators.Add(user.Id);
            }
        }

        return administrators;
    }

    public async Task<Result<string>> CreateUserAsync(
        CreateIdentityUser request,
        CancellationToken cancellationToken = default)
    {
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

        var created = await userManager.CreateAsync(user);
        if (!created.Succeeded)
        {
            return Result<string>.Failure(AdministrationErrors.UserCreateFailed());
        }

        foreach (var roleName in request.Roles.Distinct(StringComparer.Ordinal))
        {
            var assigned = await userManager.AddToRoleAsync(user, roleName);
            if (!assigned.Succeeded)
            {
                return Result<string>.Failure(AdministrationErrors.RoleMutationFailed());
            }
        }

        return Result<string>.Success(user.Id);
    }

    /// <summary>
    /// Contract parity with the production <c>EstablishFirstAdministratorAsync</c>
    /// (ADR-0016 §4.5, decision D8): the in-process <c>_administratorGate</c>
    /// stands in for the production <c>UPDLOCK/HOLDLOCK</c> on the Administrator
    /// role row, and the effective-Administrator re-check runs *inside* that
    /// gate before any write, so <c>AlreadyEstablished</c> performs no writes.
    /// <para>
    /// This double proves handler/HTTP behaviour only — transaction atomicity
    /// and real lock contention are proven exclusively by the SQL Server tests
    /// (AGENTS §22; a fake never substitutes for database concurrency evidence).
    /// A mid-write failure is not rolled back here, exactly like the pre-existing
    /// <see cref="CreateUserAsync"/> double.
    /// </para>
    /// </summary>
    public async Task<Result<FirstAdministratorEstablishment>> EstablishFirstAdministratorAsync(
        CreateIdentityUser request,
        CancellationToken cancellationToken = default)
    {
        foreach (var roleName in request.Roles)
        {
            if (!RoleNames.IsSupported(roleName))
            {
                return Result<FirstAdministratorEstablishment>.Failure(AdministrationErrors.UnsupportedRole());
            }
        }

        if (!request.Roles.Contains(RoleNames.Administrator))
        {
            return Result<FirstAdministratorEstablishment>.Failure(AdministrationErrors.UnsupportedRole());
        }

        var nationalId = PersianTextNormalizer.NormalizeIdentifier(request.NationalId);

        await _administratorGate.WaitAsync(cancellationToken);
        try
        {
            var currentAdministrators = await GetEffectiveAdministratorIdsAsync(cancellationToken);
            if (currentAdministrators.Count > 0)
            {
                return Result<FirstAdministratorEstablishment>.Success(
                    FirstAdministratorEstablishment.AlreadyEstablished);
            }

            var existing = await userManager.FindByNameAsync(nationalId);
            if (existing is null)
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

                var created = await userManager.CreateAsync(user);
                if (!created.Succeeded)
                {
                    return Result<FirstAdministratorEstablishment>.Failure(AdministrationErrors.UserCreateFailed());
                }

                foreach (var roleName in request.Roles.Distinct(StringComparer.Ordinal))
                {
                    var assigned = await userManager.AddToRoleAsync(user, roleName);
                    if (!assigned.Succeeded)
                    {
                        return Result<FirstAdministratorEstablishment>.Failure(AdministrationErrors.RoleMutationFailed());
                    }
                }

                return Result<FirstAdministratorEstablishment>.Success(FirstAdministratorEstablishment.Created);
            }

            foreach (var roleName in request.Roles.Distinct(StringComparer.Ordinal))
            {
                if (await userManager.IsInRoleAsync(existing, roleName))
                {
                    continue;
                }

                var assigned = await userManager.AddToRoleAsync(existing, roleName);
                if (!assigned.Succeeded)
                {
                    return Result<FirstAdministratorEstablishment>.Failure(AdministrationErrors.RoleMutationFailed());
                }
            }

            return Result<FirstAdministratorEstablishment>.Success(FirstAdministratorEstablishment.RoleEstablished);
        }
        finally
        {
            _administratorGate.Release();
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

        var gateAdministrator = string.Equals(roleName, RoleNames.Administrator, StringComparison.Ordinal);
        if (gateAdministrator)
        {
            await _administratorGate.WaitAsync(cancellationToken);
        }

        try
        {
            if (await userManager.IsInRoleAsync(user, roleName))
            {
                return Result<bool>.Success(false);
            }

            var granted = await userManager.AddToRoleAsync(user, roleName);
            return granted.Succeeded
                ? Result<bool>.Success(true)
                : Result<bool>.Failure(AdministrationErrors.RoleMutationFailed());
        }
        finally
        {
            if (gateAdministrator)
            {
                _administratorGate.Release();
            }
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

        var gateAdministrator = string.Equals(roleName, RoleNames.Administrator, StringComparison.Ordinal);
        if (!gateAdministrator)
        {
            if (!await userManager.IsInRoleAsync(user, roleName))
            {
                return Result<bool>.Success(false);
            }

            var plain = await userManager.RemoveFromRoleAsync(user, roleName);
            return plain.Succeeded
                ? Result<bool>.Success(true)
                : Result<bool>.Failure(AdministrationErrors.RoleMutationFailed());
        }

        if (observedEffectiveAdministratorIds is null)
        {
            return Result<bool>.Failure(AdministrationErrors.MissingAdministratorPrecondition());
        }

        await _administratorGate.WaitAsync(cancellationToken);
        try
        {
            var current = await GetEffectiveAdministratorIdsAsync(cancellationToken);
            if (!SameSet(current, observedEffectiveAdministratorIds))
            {
                return Result<bool>.Failure(AdministrationErrors.ConcurrentAdministratorChange());
            }

            if (!await userManager.IsInRoleAsync(user, roleName))
            {
                return Result<bool>.Success(false);
            }

            var revoked = await userManager.RemoveFromRoleAsync(user, roleName);
            return revoked.Succeeded
                ? Result<bool>.Success(true)
                : Result<bool>.Failure(AdministrationErrors.RoleMutationFailed());
        }
        finally
        {
            _administratorGate.Release();
        }
    }

    private static bool SameSet(IReadOnlyCollection<string> current, IReadOnlyCollection<string> observed)
    {
        var currentSet = current.ToHashSet(StringComparer.Ordinal);
        return currentSet.Count == observed.Count && observed.All(currentSet.Contains);
    }
}
