namespace GraduateRecords.Domain.Auditing;

/// <summary>
/// AGENTS §14: audit action names are constants, never free text.
/// Phase 1A Use Case 1 covers the authentication events listed as the minimum
/// in ADR-0001 and, since ADR-0015, the local credential path. The distinct
/// <c>LocalSignIn*</c> actions make University SSO and Local credential
/// authentication distinguishable in the audit trail (ADR-0015 §16); break-glass
/// authentication remains a separate, not-yet-implemented mechanism whose events
/// will carry their own distinct actions. Further actions arrive with their own
/// slices.
/// </summary>
public static class AuditActions
{
    public const string AuthenticationSignInSucceeded = "Authentication.SignInSucceeded";
    public const string AuthenticationSignInFailed = "Authentication.SignInFailed";
    public const string AuthenticationInvalidNationalId = "Authentication.InvalidNationalId";
    public const string AuthenticationUnknownUserDenied = "Authentication.UnknownUserDenied";
    public const string AuthenticationSignedOut = "Authentication.SignedOut";

    /// <summary>Successful local National ID + password sign-in (ADR-0015 §16).</summary>
    public const string AuthenticationLocalSignInSucceeded = "Authentication.LocalSignInSucceeded";

    /// <summary>Failed local National ID + password sign-in, audited by category only (ADR-0015 §8, §16).</summary>
    public const string AuthenticationLocalSignInFailed = "Authentication.LocalSignInFailed";
}
