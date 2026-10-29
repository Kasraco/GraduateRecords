using GraduateRecords.Application.Common.Identity;
using Microsoft.AspNetCore.Identity;

namespace GraduateRecords.Infrastructure.Identity;

/// <summary>
/// Local Identity user (ADR-0001 rules 4–7, ADR-0002 data model, ADR-0015 §3).
/// <see cref="UserName"/> is the normalized National ID and the dedicated
/// <see cref="NationalId"/> column carries the unique index. Both SSO and local
/// credential authentication resolve to this same record — a person never gets
/// a second user because of a different authentication path (ADR-0015 §3).
/// <see cref="UserKind"/> decides which authentication paths the account may
/// use (ADR-0015 §2); roles, access-state flags and OTP fields belong to the
/// roles/provisioning use case and are added there.
/// </summary>
public sealed class ApplicationUser : IdentityUser
{
    public const int NationalIdMaxLength = 10;
    public const int FullNameMaxLength = 200;
    public const int UserKindMaxLength = 20;

    /// <summary>ASCII National ID (10 digits, leading zeros kept, always string).</summary>
    public string NationalId { get; set; } = string.Empty;

    /// <summary>Full name as received from the university/SSO; display and audit snapshot only.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Account kind (ADR-0002 data model, ADR-0015 §2). Fail-closed default:
    /// <see cref="LocalUserKind.Student"/> — an account without an explicitly
    /// assigned kind never qualifies for local password login.
    /// </summary>
    public LocalUserKind UserKind { get; set; } = LocalUserKind.Student;
}
