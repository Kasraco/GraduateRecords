namespace GraduateRecords.Application.Common.Identity;

/// <summary>
/// Display-only masking for identity values that reach a screen or an audit
/// payload (AGENTS §14/§16, ADR-0004 §5.3, DESIGN §17: <c>۰۰۱****۸۹</c>).
/// <para>
/// Phase 1A seeds no <c>students.view-national-id</c> capability (ADR-0017 §8),
/// so no screen can ever show an unmasked National ID: the value is masked at
/// the port boundary, before it leaves Infrastructure.
/// </para>
/// </summary>
public static class IdentityDisplayMask
{
    private const string Mask = "****";

    /// <summary>
    /// <c>0012345679</c> → <c>001****67</c>: the first three and last two digits
    /// stay visible for identification, everything between them is replaced.
    /// Values that are not a 10-digit identifier are fully replaced, so a
    /// malformed stored value can never leak through.
    /// </summary>
    public static string NationalId(string? nationalId)
    {
        if (string.IsNullOrEmpty(nationalId) || nationalId.Length != 10)
        {
            return Mask;
        }

        foreach (var c in nationalId)
        {
            if (c is < '0' or > '9')
            {
                return Mask;
            }
        }

        return nationalId[..3] + Mask + nationalId[^2..];
    }
}
