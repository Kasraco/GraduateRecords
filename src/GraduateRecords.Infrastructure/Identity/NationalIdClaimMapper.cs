using System.Security.Claims;
using GraduateRecords.Domain.Common;

namespace GraduateRecords.Infrastructure.Identity;

/// <summary>
/// ADR-0001 rule 5: the single component that maps the SSO userinfo field
/// <c>id</c> to a National ID value. It normalizes Persian/Arabic digits to Latin
/// and keeps the value as a string; checksum validation happens in the Domain
/// value object used by the sign-in use case.
/// </summary>
public static class NationalIdClaimMapper
{
    public const string ClaimType = "id";

    /// <summary>Returns the normalized National ID claim, or null when missing/blank.</summary>
    public static string? Map(ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return null;
        }

        var raw = principal.FindFirstValue(ClaimType);
        return string.IsNullOrWhiteSpace(raw) ? null : PersianTextNormalizer.NormalizeIdentifier(raw);
    }
}
