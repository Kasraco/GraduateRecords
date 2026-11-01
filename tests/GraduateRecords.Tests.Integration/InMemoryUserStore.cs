using System.Collections.Concurrent;
using GraduateRecords.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace GraduateRecords.Tests.Integration;

/// <summary>
/// In-memory Identity store replacing the EF Core store in integration tests
/// (AGENTS §22: no SQL Server/CI database; never EF InMemory or SQLite). It
/// implements only the interfaces the UC1/UC2.2 code paths touch. Role
/// membership (<see cref="IUserRoleStore{TUser}"/>) mirrors the EF store's
/// semantics: membership keys are normalized role names, and only memberships
/// whose role still exists resolve to a name (the SQL join's behavior).
/// </summary>
public sealed class InMemoryUserStore :
    IUserStore<ApplicationUser>,
    IUserPasswordStore<ApplicationUser>,
    IUserLockoutStore<ApplicationUser>,
    IUserSecurityStampStore<ApplicationUser>,
    IUserRoleStore<ApplicationUser>
{
    private readonly ConcurrentDictionary<string, ApplicationUser> _users = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _memberships = new(StringComparer.Ordinal);
    private readonly InMemoryRoleStore? _roles;

    /// <param name="roles">
    /// The role store to resolve membership names from. Hosts that never assign
    /// roles may omit it; any membership attempt without it fails, like the EF
    /// store's missing-role failure.
    /// </param>
    public InMemoryUserStore(InMemoryRoleStore? roles = null) => _roles = roles;

    public IReadOnlyCollection<ApplicationUser> All => _users.Values.ToArray();

    public void Remove(ApplicationUser user) => _users.TryRemove(user.Id, out _);

    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.Id);

    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.UserName);

    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        _users[user.Id] = user;
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        _users[user.Id] = user;
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        _users.TryRemove(user.Id, out _);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        Task.FromResult(_users.TryGetValue(userId, out var user) ? user : null);

    public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        Task.FromResult(_users.Values.FirstOrDefault(
            user => string.Equals(user.NormalizedUserName, normalizedUserName, StringComparison.Ordinal)));

    public Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PasswordHash is not null);

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutEnd);

    public Task SetLockoutEndDateAsync(ApplicationUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        user.LockoutEnd = lockoutEnd;
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount++;
        return Task.FromResult(user.AccessFailedCount);
    }

    public Task ResetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.AccessFailedCount);

    public Task<bool> GetLockoutEnabledAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutEnabled);

    public Task SetLockoutEnabledAsync(ApplicationUser user, bool enabled, CancellationToken cancellationToken)
    {
        user.LockoutEnabled = enabled;
        return Task.CompletedTask;
    }

    public Task SetSecurityStampAsync(ApplicationUser user, string? stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.SecurityStamp);

    public Task AddToRoleAsync(ApplicationUser user, string normalizedRoleName, CancellationToken cancellationToken)
    {
        // Mirrors UserStore.AddToRoleAsync: an unknown role is a hard failure,
        // so a test can never silently accumulate membership in a missing role.
        if (_roles?.FindByNormalizedName(normalizedRoleName) is null)
        {
            throw new InvalidOperationException($"Role '{normalizedRoleName}' does not exist.");
        }

        _memberships.GetOrAdd(user.Id, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal))[
            normalizedRoleName] = 0;
        return Task.CompletedTask;
    }

    public Task RemoveFromRoleAsync(ApplicationUser user, string normalizedRoleName, CancellationToken cancellationToken)
    {
        if (_memberships.TryGetValue(user.Id, out var memberships))
        {
            memberships.TryRemove(normalizedRoleName, out _);
        }

        return Task.CompletedTask;
    }

    public Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        // Mirrors the EF store's join over AspNetUserRoles × AspNetRoles: only
        // memberships whose role still exists yield the role's display name
        // (which is what the claims principal factory adds as the role claim).
        var names = new List<string>();
        if (_memberships.TryGetValue(user.Id, out var memberships))
        {
            foreach (var normalizedRoleName in memberships.Keys)
            {
                if (_roles?.FindByNormalizedName(normalizedRoleName) is { Name: not null } role)
                {
                    names.Add(role.Name);
                }
            }
        }

        return Task.FromResult<IList<string>>(names);
    }

    public Task<bool> IsInRoleAsync(ApplicationUser user, string normalizedRoleName, CancellationToken cancellationToken) =>
        Task.FromResult(
            _memberships.TryGetValue(user.Id, out var memberships) &&
            memberships.ContainsKey(normalizedRoleName));

    public Task<IList<ApplicationUser>> GetUsersInRoleAsync(string normalizedRoleName, CancellationToken cancellationToken)
    {
        // Mirrors the EF store: an unknown role yields no users at all.
        if (_roles?.FindByNormalizedName(normalizedRoleName) is null)
        {
            return Task.FromResult<IList<ApplicationUser>>(new List<ApplicationUser>());
        }

        IList<ApplicationUser> users = _users.Values
            .Where(user => _memberships.TryGetValue(user.Id, out var memberships)
                           && memberships.ContainsKey(normalizedRoleName))
            .ToList();
        return Task.FromResult(users);
    }

    public void Dispose()
    {
    }
}
