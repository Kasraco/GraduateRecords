using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GraduateRecords.Infrastructure.Identity;

/// <summary>
/// Adds the <see cref="FullNameClaimType"/> claim to the local session principal.
/// ADR-0001 rule 18: the Name claim is the UserName, which is the National ID, so
/// audit and UI must never render it; the full name is the display snapshot
/// (AGENTS §14 <c>ActorDisplayName</c>). The factory runs on every security-stamp
/// validation, so the claim always reflects the current stored name.
/// </summary>
public sealed class ApplicationUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<ApplicationUser>(userManager, optionsAccessor)
{
    public const string FullNameClaimType = "full_name";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(FullNameClaimType, user.FullName));
        return identity;
    }
}
