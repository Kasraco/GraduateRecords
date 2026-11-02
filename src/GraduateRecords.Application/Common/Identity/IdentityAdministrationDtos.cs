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
