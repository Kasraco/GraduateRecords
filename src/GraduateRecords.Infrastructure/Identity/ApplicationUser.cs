using Microsoft.AspNetCore.Identity;

namespace GraduateRecords.Infrastructure.Identity;

/// <summary>
/// Local Identity user (ADR-0001 rules 4–7, ADR-0002 data model).
/// <see cref="UserName"/> is the normalized National ID and the dedicated
/// <see cref="NationalId"/> column carries the unique index. Roles, access-state
/// flags and OTP fields belong to the roles/provisioning use case and are added there.
/// </summary>
public sealed class ApplicationUser : IdentityUser
{
    public const int NationalIdMaxLength = 10;
    public const int FullNameMaxLength = 200;

    /// <summary>ASCII National ID (10 digits, leading zeros kept, always string).</summary>
    public string NationalId { get; set; } = string.Empty;

    /// <summary>Full name as received from the university/SSO; display and audit snapshot only.</summary>
    public string FullName { get; set; } = string.Empty;
}
