namespace GraduateRecords.Web.Models;

/// <summary>
/// Login choice page model (ADR-0015 §10, AGENTS 13.5: the user explicitly
/// chooses between University SSO and local Staff/Administrator login).
/// <see cref="NationalId"/> and <see cref="Password"/> belong to the local
/// credential form; the password is never persisted or re-rendered after a
/// failed attempt.
/// </summary>
public sealed class LocalLoginViewModel
{
    /// <summary>Whether the university SSO option can be offered on this environment.</summary>
    public bool SsoAvailable { get; set; }

    /// <summary>Entered National ID (accepted as Persian or ASCII digits; display only after a failure).</summary>
    public string? NationalId { get; set; }

    public string? Password { get; set; }

    /// <summary>Host-relative return URL; never honoured when absolute (open-redirect protection).</summary>
    public string? ReturnUrl { get; set; }
}
