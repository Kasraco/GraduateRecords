using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GraduateRecords.Infrastructure.Identity;

/// <summary>
/// Builds the local session principal (ADR-0001 rule 18, ADR-0015 §3).
/// <para>
/// UC2.2: derives from the role-aware <see cref="UserClaimsPrincipalFactory{TUser, TRole}"/>,
/// so the framework itself copies, on every principal build, (a) one canonical
/// role-name claim per actual <c>AspNetUserRoles</c> membership and (b) every
/// Identity role claim from <c>AspNetRoleClaims</c> — the persisted
/// <c>permission</c> claims of ADR-0004/ADR-0017 §6. No custom role/permission
/// loading code exists here; only the display claim below is added by this class.
/// </para>
/// <para>
/// The Name claim is the UserName, which is the National ID, so audit and UI
/// must never render it; the full name is the display snapshot
/// (AGENTS §14 <c>ActorDisplayName</c>). The factory runs on every security-stamp
/// validation (ADR-0008 §5.3), so claims always reflect the current stored
/// roles, role claims and name. Nothing is derived from UserKind,
/// authentication method or any other session attribute (ADR-0017 §1/§4).
/// </para>
/// </summary>
public sealed class ApplicationUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>(userManager, roleManager, optionsAccessor)
{
    /// <summary>
    /// Display-name claim type (AGENTS §14 <c>ActorDisplayName</c>). The value
    /// is defined once in Application (<see cref="Application.Common.Security.SessionClaims"/>
    /// — Web's <c>ICurrentUser</c> must read the same string without referencing
    /// this assembly) and aliased here so existing call sites and tests keep
    /// working unchanged.
    /// </summary>
    public const string FullNameClaimType = Application.Common.Security.SessionClaims.FullName;

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(FullNameClaimType, user.FullName));
        return identity;
    }
}
