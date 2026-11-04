using GraduateRecords.Domain.Common;
using DomainNationalId = GraduateRecords.Domain.Common.NationalId;

namespace GraduateRecords.Infrastructure.Options;

/// <summary>
/// Bootstrap Administrator configuration (ADR-0016 §2): identity information
/// only — never a password, hash, token, OIDC secret or any other credential
/// (§2.2, AGENTS §18). Both keys are empty in the repository (§2.3); real
/// values come from the environment (ADR-0006 §4.1) or user-secrets in
/// Development.
/// <para>
/// Shape validation is environment-independent: partial or present-but-invalid
/// values fail startup in <b>every</b> environment (§2.4, <c>ValidateOnStart</c>
/// convention). Whether absent configuration is fatal depends on runtime state
/// (an existing Administrator role holder) and is decided by the Application
/// bootstrap step, not here (§2.4/§14.2).
/// </para>
/// </summary>
public sealed class BootstrapAdministratorOptions
{
    public const string SectionName = "BootstrapAdministrator";

    public const string ValidationMessage =
        "BootstrapAdministrator configuration is invalid: NationalId and MobileNumber must be supplied together, " +
        "the National ID must be a valid 10-digit Iranian National ID, and the mobile number must be 09xxxxxxxxx.";

    /// <summary>Normalized 10-digit National ID of the first Administrator (ADR-0016 §2.1).</summary>
    public string NationalId { get; set; } = string.Empty;

    /// <summary>Mobile number normalized to <c>09xxxxxxxxx</c> (ADR-0016 §2.1).</summary>
    public string MobileNumber { get; set; } = string.Empty;

    /// <summary>True when both keys carry a value (an empty section is legitimate after completion, §2.4).</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(NationalId) && !string.IsNullOrWhiteSpace(MobileNumber);

    /// <summary>True when exactly one of the two keys carries a value — always an operator error (§2.4).</summary>
    public bool IsPartial =>
        string.IsNullOrWhiteSpace(NationalId) != string.IsNullOrWhiteSpace(MobileNumber);

    /// <summary>
    /// Shape rules only: both-or-neither, a checksum-valid National ID and the
    /// canonical mobile shape. Absence is valid here (state-dependent fatality
    /// is decided by the bootstrap step).
    /// </summary>
    public bool IsValid()
    {
        if (IsPartial)
        {
            return false;
        }

        if (!IsConfigured)
        {
            return true;
        }

        if (!DomainNationalId.TryParse(NationalId, out _))
        {
            return false;
        }

        var mobile = PersianTextNormalizer.ToLatinDigits(MobileNumber).Trim();
        return IsValidMobile(mobile);
    }

    private static bool IsValidMobile(string mobile)
    {
        if (mobile.Length != 11 || mobile[0] != '0' || mobile[1] != '9')
        {
            return false;
        }

        foreach (var character in mobile)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
