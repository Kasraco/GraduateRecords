using System.Security.Claims;
using GraduateRecords.Application.Common.Security;
using Microsoft.AspNetCore.Http;

namespace GraduateRecords.Web.Authorization;

/// <summary>
/// <see cref="ICurrentUser"/> backed by the authenticated session principal
/// (ARCHITECTURE §15: identity reaches a handler only through
/// <c>ICurrentUser</c>, never through request data).
/// <para>
/// Reads exactly three things: the stable user id claim, the display-name
/// snapshot claim and the role/permission claims the principal factory produced
/// from Identity. It deliberately exposes no <c>UserKind</c> and no
/// authentication-method information, so no handler can base an authorization
/// decision on either (ADR-0017 §1, §4, §21).
/// </para>
/// </summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string? UserId => IsAuthenticated
        ? Principal!.FindFirstValue(ClaimTypes.NameIdentifier)
        : null;

    public string? DisplayName => IsAuthenticated
        ? Principal!.FindFirstValue(SessionClaims.FullName)
        : null;

    public IReadOnlyCollection<string> Roles => IsAuthenticated
        ? Principal!.Claims
            .Where(claim => string.Equals(claim.Type, ClaimTypes.Role, StringComparison.Ordinal))
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray()
        : Array.Empty<string>();

    public IReadOnlyCollection<string> Permissions => IsAuthenticated
        ? Principal!.Claims
            .Where(claim => string.Equals(claim.Type, PermissionClaims.Type, StringComparison.Ordinal))
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray()
        : Array.Empty<string>();

    /// <summary>Fail-closed: no session, no permission (ADR-0017 §21).</summary>
    public bool HasPermission(string permission) =>
        IsAuthenticated && !string.IsNullOrEmpty(permission)
        && Permissions.Contains(permission, StringComparer.Ordinal);

    /// <summary>Derived from role names only — never from kind or auth path.</summary>
    public StaffScope StaffScope => ScopeExtensions.FromRoles(Roles);
}
