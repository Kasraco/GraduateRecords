using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Application.Common.Time;
using GraduateRecords.Domain.Auditing;

namespace GraduateRecords.Tests.Unit;

/// <summary>
/// In-memory port double for the administration handlers (AGENTS §22: "unit with
/// fakes for ports"). It records every call so a test can assert both the
/// outcome and what the handler passed to the port — in particular the observed
/// effective-administrator set that carries the ADR-0017 §19 precondition.
/// </summary>
internal sealed class FakeIdentityAdministration : IIdentityAdministration
{
    public Dictionary<string, IdentityUserDetail> Users { get; } = new(StringComparer.Ordinal);

    public List<string> EffectiveAdministratorIds { get; } = [];

    public List<string> ListedSearchTerms { get; } = [];

    public List<CreateIdentityUser> Creations { get; } = [];

    public List<(string UserId, string RoleName)> Grants { get; } = [];

    public List<(string UserId, string RoleName, IReadOnlyCollection<string>? Observed)> Revocations { get; } = [];

    public Result<bool> GrantResult { get; set; } = Result<bool>.Success(true);

    public Result<bool> RevokeResult { get; set; } = Result<bool>.Success(true);

    public Result<string> CreateResult { get; set; } = Result<string>.Success("new-user-id");

    public Task<IdentityUserPage> ListUsersAsync(
        string? searchTerm,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ListedSearchTerms.Add(searchTerm ?? string.Empty);
        var all = Users.Values.OrderBy(user => user.FullName, StringComparer.Ordinal).ToList();
        var items = all
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new IdentityUserSummary(
                user.Id, user.FullName, user.NationalIdMasked, user.Kind, user.Roles))
            .ToList();
        return Task.FromResult(new IdentityUserPage(items, all.Count, page, pageSize));
    }

    public Task<IdentityUserDetail?> FindUserAsync(string userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Users.TryGetValue(userId, out var user) ? user : null);

    public Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(
            Users.TryGetValue(userId, out var user) ? user.Roles : Array.Empty<string>());

    public Task<IReadOnlyList<string>> GetEffectiveAdministratorIdsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(EffectiveAdministratorIds);

    public Task<Result<string>> CreateUserAsync(CreateIdentityUser request, CancellationToken cancellationToken = default)
    {
        Creations.Add(request);
        return Task.FromResult(CreateResult);
    }

    public Task<Result<bool>> GrantRoleAsync(
        string userId,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        Grants.Add((userId, roleName));
        return Task.FromResult(GrantResult);
    }

    public Task<Result<bool>> RevokeRoleAsync(
        string userId,
        string roleName,
        IReadOnlyCollection<string>? observedEffectiveAdministratorIds,
        CancellationToken cancellationToken = default)
    {
        Revocations.Add((userId, roleName, observedEffectiveAdministratorIds));
        return Task.FromResult(RevokeResult);
    }
}

/// <summary>Session facts the handlers read through <see cref="ICurrentUser"/>.</summary>
internal sealed class FakeCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; set; } = true;

    public string? UserId { get; set; } = "actor-id";

    public string? DisplayName { get; set; } = "مدیر آزمایشی";

    public IReadOnlyCollection<string> Roles { get; set; } = Array.Empty<string>();

    public IReadOnlyCollection<string> Permissions { get; set; } = Array.Empty<string>();

    public bool HasPermission(string permission) =>
        IsAuthenticated && !string.IsNullOrEmpty(permission)
        && Permissions.Contains(permission, StringComparer.Ordinal);

    public StaffScope StaffScope => ScopeExtensions.FromRoles(Roles);
}

/// <summary>Captures audit entries instead of writing them.</summary>
internal sealed class FakeAuditWriter : IAuditWriter
{
    public List<AuditEntry> Entries { get; } = [];

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}

/// <summary>Fixed clock — no wall-clock dependence in a test.</summary>
internal sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; } = utcNow;
}
