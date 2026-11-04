using GraduateRecords.Application.Common.Results;

namespace GraduateRecords.Application.Common.Identity;

/// <summary>
/// The single Application port to local Identity user/role administration
/// (AGENTS §2: "Admin user/role/management goes through the Application port
/// <c>IIdentityAdministration</c> … implemented in Infrastructure. This port is
/// justified because Identity types must not enter Application. Do not add
/// <c>IIdentityService</c> or other identity wrappers.").
/// <para>
/// <b>Reads</b> return already-masked, Identity-free projections. <b>Mutations</b>
/// return <see cref="Result"/> with an expected failure kind — never an
/// exception — and every one of them is a state change the caller must audit
/// (ADR-0002 §9, ADR-0017 §22).
/// </para>
/// <para>
/// <b>Last-Administrator invariant (ADR-0017 §17–§19).</b> The *rule* lives in
/// the Application handler; this port supplies atomicity:
/// <list type="bullet">
/// <item>Every path that can grant or revoke the canonical
/// <c>Administrator</c> role — <see cref="GrantRoleAsync"/>,
/// <see cref="RevokeRoleAsync"/> and <see cref="CreateUserAsync"/> when the
/// requested roles include <c>Administrator</c> — first takes one exclusive
/// database lock on the <c>Administrator</c> role row, inside one database
/// transaction, before it reads anything.</item>
/// <item><see cref="RevokeRoleAsync"/> for <c>Administrator</c> then reads the
/// effective-administrator set, compares it with the caller's
/// <paramref name="observedEffectiveAdministratorIds"/> (the set the handler
/// used to decide the §17–§18 rule) and only then mutates — all inside that
/// same transaction. A mismatch rolls back and returns
/// <c>identity.admin.concurrent-change</c> (<see cref="ErrorKind.Conflict"/>),
/// so a stale decision can never be applied.</item>
/// </list>
/// The lock, the transaction and the raw SQL live only in Infrastructure
/// (AGENTS §2.1). No method here reads <c>UserKind</c> or the authentication
/// method to decide anything (ADR-0017 §1/§4/§21).
/// </para>
/// </summary>
public interface IIdentityAdministration
{
    /// <summary>
    /// Paged user list for the administration screen. The search term is matched
    /// against the display name (substring) and the canonical National ID
    /// (exact, after identifier normalization).
    /// </summary>
    Task<IdentityUserPage> ListUsersAsync(
        string? searchTerm,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>The masked user detail, or <c>null</c> when the id is unknown.</summary>
    Task<IdentityUserDetail?> FindUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Canonical role names currently held by the user (never a permission).</summary>
    Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ids of the current effective Administrators (ADR-0017 §18: role membership
    /// — no account-state model exists yet). The caller reads this once, decides
    /// the §17 rule from it and passes the same set back as the precondition.
    /// </summary>
    Task<IReadOnlyList<string>> GetEffectiveAdministratorIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a local user without any credential. Returns the new user id.
    /// When <see cref="CreateIdentityUser.Roles"/> contains
    /// <c>Administrator</c>, the grant path of the last-Administrator invariant
    /// applies (same lock, same transaction).
    /// </summary>
    Task<Result<string>> CreateUserAsync(CreateIdentityUser request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Establishes the <b>first</b> Administrator (ADR-0016 §1–§4): one
    /// transaction that takes the Administrator role-row lock, re-checks the
    /// effective-Administrator set <b>after</b> acquiring it, and only then
    /// creates the identity (without any credential) or assigns the missing
    /// role — all before a single commit.
    /// <para>
    /// Outcomes (<see cref="FirstAdministratorEstablishment"/>):
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="FirstAdministratorEstablishment.AlreadyEstablished"/> —
    /// at least one effective Administrator existed at the locked re-check; the
    /// operation performs <b>no writes at all</b> and reports a successful inert
    /// outcome (ADR-0016 §4.2 — never an error).</item>
    /// <item><see cref="FirstAdministratorEstablishment.Created"/> — no
    /// Administrator existed and the identity did not exist: the identity is
    /// created and given the role in the same transaction.</item>
    /// <item><see cref="FirstAdministratorEstablishment.RoleEstablished"/> — no
    /// Administrator existed and the identity already existed: only the role is
    /// assigned; the existing profile is never modified (ADR-0016 §4.1).</item>
    /// </list>
    /// <para>
    /// Application decides <b>whether</b> establishment may proceed (target
    /// state, configuration, environment — ADR-0016 §4.5/§14); this operation
    /// only guarantees the atomicity of the check-then-write sequence. The lock,
    /// the transaction and the raw SQL live only here (AGENTS §2.1).
    /// </para>
    /// </summary>
    Task<Result<FirstAdministratorEstablishment>> EstablishFirstAdministratorAsync(
        CreateIdentityUser request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Grants a canonical role. Returns <c>true</c> when membership changed and
    /// <c>false</c> when the user already held it (no-op success — the caller
    /// audits only a change).
    /// </summary>
    Task<Result<bool>> GrantRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a canonical role. For <c>Administrator</c>,
    /// <paramref name="observedEffectiveAdministratorIds"/> is mandatory: it is
    /// the set the handler validated the §17 rule against, re-checked inside the
    /// locked transaction before any write. For every other role it must be
    /// <c>null</c> (those roles cannot change the administrator set).
    /// Returns <c>true</c> when membership changed.
    /// </summary>
    Task<Result<bool>> RevokeRoleAsync(
        string userId,
        string roleName,
        IReadOnlyCollection<string>? observedEffectiveAdministratorIds,
        CancellationToken cancellationToken = default);
}
