using Microsoft.AspNetCore.Identity;

namespace GraduateRecords.Infrastructure.Identity;

/// <summary>
/// Applies the decided security values to <see cref="IdentityOptions"/> (AGENTS §13).
/// The break-glass password policy is length-only (minimum 14 characters, no
/// composition rules) per the owner decision on ADR-0001 rule 12. Local password
/// login stays disabled for students and staff (ADR-0001 rule 11); these options
/// govern any password operation, including the future break-glass account.
/// </summary>
public static class IdentityOptionsSetup
{
    public static void Apply(IdentityOptions options, Options.IdentitySecuritySettings settings)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);

        options.Lockout.AllowedForNewUsers = settings.Lockout.AllowedForNewUsers;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(settings.Lockout.LockoutDurationMinutes);
        options.Lockout.MaxFailedAccessAttempts = settings.Lockout.MaxFailedAccessAttempts;

        options.Password.RequiredLength = settings.Password.RequiredLength;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1; // Repeats of the same character are still rejected by length.

        options.SignIn.RequireConfirmedEmail = false;
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = false;
    }
}
