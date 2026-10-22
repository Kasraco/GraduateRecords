namespace GraduateRecords.Domain.Auditing;

/// <summary>
/// AGENTS §14: audit action names are constants, never free text.
/// Phase 1A Use Case 1 covers only the authentication events listed as the
/// minimum in ADR-0001. Further actions arrive with their own slices.
/// </summary>
public static class AuditActions
{
    public const string AuthenticationSignInSucceeded = "Authentication.SignInSucceeded";
    public const string AuthenticationSignInFailed = "Authentication.SignInFailed";
    public const string AuthenticationInvalidNationalId = "Authentication.InvalidNationalId";
    public const string AuthenticationUnknownUserDenied = "Authentication.UnknownUserDenied";
    public const string AuthenticationSignedOut = "Authentication.SignedOut";
}
