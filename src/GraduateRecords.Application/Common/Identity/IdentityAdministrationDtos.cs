namespace GraduateRecords.Application.Common.Identity;

/// <summary>One row of the administrator user list (already masked).</summary>
public sealed record IdentityUserSummary(
    string Id,
    string FullName,
    string NationalIdMasked,
    LocalUserKind Kind,
    IReadOnlyList<string> Roles);

/// <summary>Paged user-list result (page is 1-based, page size is server-chosen).</summary>
public sealed record IdentityUserPage(
    IReadOnlyList<IdentityUserSummary> Users,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>User detail as the administration screen may show it (ADR-0004 §8.2).</summary>
public sealed record IdentityUserDetail(
    string Id,
    string FullName,
    string NationalIdMasked,
    LocalUserKind Kind,
    IReadOnlyList<string> Roles);

/// <summary>
/// What an Administrator supplies when creating a local user (ADR-0002 §2.1:
/// National ID, full name, role(s), mobile number — and never a password or any
/// other credential, ADR-0016 §7).
/// </summary>
public sealed record CreateIdentityUser(
    string NationalId,
    string FullName,
    string MobileNumber,
    LocalUserKind Kind,
    IReadOnlyList<string> Roles);

/// <summary>
/// Outcome of <c>IIdentityAdministration.EstablishFirstAdministratorAsync</c>
/// (ADR-0016 §4.5). Exactly one of these is returned for every call; the
/// operation never reports success for a write it did not perform.
/// </summary>
public enum FirstAdministratorEstablishment
{
    /// <summary>The configured identity did not exist; it was created and given the Administrator role in one transaction.</summary>
    Created,

    /// <summary>The configured identity already existed as an eligible account; only the missing Administrator role was assigned (profile untouched).</summary>
    RoleEstablished,

    /// <summary>
    /// An effective Administrator already existed when the lock was acquired, so
    /// the operation performed no writes at all (ADR-0016 §4.2 completion
    /// guard re-checked inside the transaction — inert, never an error).
    /// </summary>
    AlreadyEstablished,
}
