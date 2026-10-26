namespace GraduateRecords.Infrastructure.Options;

/// <summary>
/// Validated security configuration for identity and sign-in (AGENTS §13, §18;
/// ADR-0001 rules 12–13; ADR-0008 §5). All numeric security values live here and in
/// configuration, never as literals in code. Values are decided by the project owner:
/// lockout 5 attempts / 15 minutes, break-glass password length ≥ 14 with no
/// composition rules, login rate limit 10 per minute per client IP, cookie idle
/// lifetime 30 minutes sliding with an 8-hour absolute session cap.
/// </summary>
public sealed class IdentitySecuritySettings
{
    public const string SectionName = "Identity";

    /// <summary>Owner decision: the password policy never goes below 14 characters.</summary>
    public const int MinimumPasswordLength = 14;

//OLD KRB

    // public const string ValidationMessage =
    //     "Identity configuration is invalid: lockout attempts and duration must be at least 1, " +
    //     "password length must be at least 14, the cookie idle lifetime must be at least 1 minute " +
    //     "and shorter than the absolute session cap, the login rate limit must be at least 1 permit " +
    //     "per minute, and Identity:Oidc must contain a valid https Authority plus ClientId and ClientSecret.";


// Change KRB
public const string ValidationMessage =
    "Identity security configuration is invalid: lockout attempts and duration must be at least 1, " +
    "password length must be at least 14, the cookie idle lifetime must be at least 1 minute " +
    "and shorter than the absolute session cap, and the login rate limit must be at least 1 permit per minute.";
/// ----------------
    public LockoutSettings Lockout { get; set; } = new();

    public PasswordSettings Password { get; set; } = new();

    public CookieLifetimeSettings CookieLifetime { get; set; } = new();

    public LoginRateLimitSettings LoginRateLimit { get; set; } = new();

    public OidcSettings Oidc { get; set; } = new();

    public sealed class LockoutSettings
    {
        public int MaxFailedAccessAttempts { get; set; }

        public int LockoutDurationMinutes { get; set; }

        public bool AllowedForNewUsers { get; set; } = true;
    }

    public sealed class PasswordSettings
    {
        public int RequiredLength { get; set; }
    }

    public sealed class CookieLifetimeSettings
    {
        public int IdleMinutes { get; set; }

        public int AbsoluteHours { get; set; }
    }

    public sealed class LoginRateLimitSettings
    {
        public int PermitsPerMinute { get; set; }

        /// <summary>The fixed window implied by "per minute" (owner decision).</summary>
        public TimeSpan Window => TimeSpan.FromMinutes(1);
    }

    public sealed class OidcSettings
    {
        public string Authority { get; set; } = string.Empty;

        public string ClientId { get; set; } = string.Empty;

        /// <summary>Secret configuration only (ADR-0001 rule 17); never in the repository.</summary>
        public string ClientSecret { get; set; } = string.Empty;

        public bool UsePkce { get; set; } = true;
    }

    public TimeSpan AbsoluteSessionCap => TimeSpan.FromHours(CookieLifetime.AbsoluteHours);

    public TimeSpan IdleSessionLifetime => TimeSpan.FromMinutes(CookieLifetime.IdleMinutes);

//OLD -KRB
    // public bool IsValid() =>
    //     Lockout.MaxFailedAccessAttempts >= 1
    //     && Lockout.LockoutDurationMinutes >= 1
    //     && Password.RequiredLength >= MinimumPasswordLength
    //     && CookieLifetime.IdleMinutes >= 1
    //     && CookieLifetime.AbsoluteHours >= 1
    //     && AbsoluteSessionCap >= IdleSessionLifetime
    //     && LoginRateLimit.PermitsPerMinute >= 1
    //     && IsValidAuthority(Oidc.Authority)
    //     && !string.IsNullOrWhiteSpace(Oidc.ClientId)
    //     && !string.IsNullOrWhiteSpace(Oidc.ClientSecret);


// change krb
   public bool IsValid() =>
        Lockout.MaxFailedAccessAttempts >= 1
        && Lockout.LockoutDurationMinutes >= 1
        && Password.RequiredLength >= MinimumPasswordLength
        && CookieLifetime.IdleMinutes >= 1
        && CookieLifetime.AbsoluteHours >= 1
        && AbsoluteSessionCap > IdleSessionLifetime
        && LoginRateLimit.PermitsPerMinute >= 1;
        
    public bool IsOidcConfigured =>
        IsValidAuthority(Oidc.Authority)
        && !string.IsNullOrWhiteSpace(Oidc.ClientId)
        && !string.IsNullOrWhiteSpace(Oidc.ClientSecret);
// change krb

    private static bool IsValidAuthority(string authority) =>
        !string.IsNullOrWhiteSpace(authority)
        && Uri.TryCreate(authority, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;
}
