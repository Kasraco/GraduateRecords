using System.Collections.Concurrent;
using System.Security.Claims;
using GraduateRecords.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace GraduateRecords.Tests.Integration;

/// <summary>
/// In-memory Identity role store replacing the EF Core role store in integration
/// tests (AGENTS §22: no SQL Server/CI database; never EF InMemory or SQLite).
/// Implements only the interfaces the UC2.2 code paths touch: role CRUD with
/// normalized-name lookup (what <c>RoleManager</c> needs) and role claims
/// (<see cref="IRoleClaimStore{TRole}"/> — what the role-aware
/// <c>UserClaimsPrincipalFactory&lt;TUser, TRole&gt;</c> copies onto the
/// principal). Deliberately not a general-purpose Identity fake: no IQueryable
/// surface, no validators, no persistence semantics beyond what the tests
/// observe. Like <c>AspNetRoleClaims</c> (which has no uniqueness constraint),
/// duplicate (type, value) claims are kept rather than suppressed, so a future
/// idempotent-reconciliation test cannot be masked — tests set claims up
/// idempotently instead.
/// </summary>
public sealed class InMemoryRoleStore : IRoleStore<ApplicationRole>, IRoleClaimStore<ApplicationRole>
{
    private readonly ConcurrentDictionary<string, ApplicationRole> _roles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Claim>> _claims = new(StringComparer.Ordinal);
    private readonly object _claimGate = new();

    public IReadOnlyCollection<ApplicationRole> All => _roles.Values.ToArray();

    /// <summary>Lookup by the already-normalized role name (RoleManager normalizes before calling the store).</summary>
    public ApplicationRole? FindByNormalizedName(string normalizedName) =>
        _roles.Values.FirstOrDefault(
            role => string.Equals(role.NormalizedName, normalizedName, StringComparison.Ordinal));

    public Task<IdentityResult> CreateAsync(ApplicationRole role, CancellationToken cancellationToken)
    {
        _roles[role.Id] = role;
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> UpdateAsync(ApplicationRole role, CancellationToken cancellationToken) =>
        CreateAsync(role, cancellationToken);

    public Task<IdentityResult> DeleteAsync(ApplicationRole role, CancellationToken cancellationToken)
    {
        _roles.TryRemove(role.Id, out _);
        lock (_claimGate)
        {
            _claims.Remove(role.Id);
        }

        return Task.FromResult(IdentityResult.Success);
    }

    public Task<ApplicationRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken) =>
        Task.FromResult(_roles.TryGetValue(roleId, out var role) ? role : null);

    public Task<ApplicationRole?> FindByNameAsync(string normalizedName, CancellationToken cancellationToken) =>
        Task.FromResult(FindByNormalizedName(normalizedName));

    public Task<string> GetRoleIdAsync(ApplicationRole role, CancellationToken cancellationToken) =>
        Task.FromResult(role.Id);

    public Task<string?> GetRoleNameAsync(ApplicationRole role, CancellationToken cancellationToken) =>
        Task.FromResult(role.Name);

    public Task SetRoleNameAsync(ApplicationRole role, string? roleName, CancellationToken cancellationToken)
    {
        role.Name = roleName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedRoleNameAsync(ApplicationRole role, CancellationToken cancellationToken) =>
        Task.FromResult(role.NormalizedName);

    public Task SetNormalizedRoleNameAsync(ApplicationRole role, string? normalizedName, CancellationToken cancellationToken)
    {
        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    public Task<IList<Claim>> GetClaimsAsync(ApplicationRole role, CancellationToken cancellationToken)
    {
        lock (_claimGate)
        {
            IList<Claim> claims = _claims.TryGetValue(role.Id, out var stored)
                ? stored.ToList()
                : new List<Claim>();
            return Task.FromResult(claims);
        }
    }

    public Task AddClaimAsync(ApplicationRole role, Claim claim, CancellationToken cancellationToken)
    {
        lock (_claimGate)
        {
            if (!_claims.TryGetValue(role.Id, out var stored))
            {
                _claims[role.Id] = stored = new List<Claim>();
            }

            stored.Add(claim);
        }

        return Task.CompletedTask;
    }

    public Task RemoveClaimAsync(ApplicationRole role, Claim claim, CancellationToken cancellationToken)
    {
        lock (_claimGate)
        {
            if (_claims.TryGetValue(role.Id, out var stored))
            {
                stored.RemoveAll(existing =>
                    string.Equals(existing.Type, claim.Type, StringComparison.Ordinal) &&
                    string.Equals(existing.Value, claim.Value, StringComparison.Ordinal));
            }
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}
