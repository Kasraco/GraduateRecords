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

    // --- Phase 1A Use Case 3: user/role administration (ADR-0017 §22 names the
    // conceptual events; the constants are added in the implementing slice).
    // These are best-effort structured security events in Phase 1A — the durable
    // append-only AuditLog arrives with Phase 1C (ADR-0016 §12.4, ADR-0017 §22).
    // Never any credential material, token or unmasked National ID (AGENTS §14).

    /// <summary>Administrator created a local user account (ADR-0002 §2.1/§9).</summary>
    public const string AuthorizationUserCreated = "Authorization.UserCreated";

    /// <summary>A supported role was granted to a user (ADR-0002 §9, ADR-0017 §10/§22).</summary>
    public const string AuthorizationRoleGranted = "Authorization.RoleGranted";

    /// <summary>A supported role was revoked from a user (ADR-0002 §9, ADR-0017 §10/§22).</summary>
    public const string AuthorizationRoleRevoked = "Authorization.RoleRevoked";

    // --- Phase 1A Bootstrap slice (ADR-0016 §12.1 names the conceptual events;
    // the constants are added in this implementing slice). Best-effort
    // structured security events in Phase 1A — the durable append-only AuditLog
    // arrives with Phase 1C (ADR-0016 §12.4). Payloads are ids and outcome
    // only: never a National ID, a mobile number or any credential (AGENTS §14).

    /// <summary>
    /// The first Administrator identity was provisioned at startup, or the
    /// provisioning was refused/failed (same event, <c>Result</c> distinguishes
    /// Success/Denied/Failed — ADR-0016 §12.2). Actor is absent: bootstrap runs
    /// without a session (ADR-0016 §12.2).
    /// </summary>
    public const string BootstrapAdministratorProvisioned = "Bootstrap.AdministratorProvisioned";

    /// <summary>
    /// The missing Administrator role was established on an already-existing
    /// bootstrap identity (ADR-0016 §12.1, resume path of §4.5).
    /// </summary>
    public const string BootstrapAdministratorRoleEstablished = "Bootstrap.AdministratorRoleEstablished";
}
