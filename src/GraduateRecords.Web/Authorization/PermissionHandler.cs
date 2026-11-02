using GraduateRecords.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;

namespace GraduateRecords.Web.Authorization;

/// <summary>
/// Fail-closed evaluation of <see cref="PermissionRequirement"/> (ADR-0017 §21):
/// <list type="bullet">
/// <item>an unauthenticated principal never satisfies a requirement;</item>
/// <item>an unknown, blank or unregistered permission value is never granted —
/// the check is an ordinal match against the principal's <c>permission</c>
/// claims, which the principal factory copies from Identity role claims;</item>
/// <item>no other claim type, role name, <c>UserKind</c> or authentication
/// attribute can substitute for the missing permission.</item>
/// </list>
/// UI visibility is UX only; this handler and the attribute above it are the
/// server-side authority.
/// </summary>
public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (string.IsNullOrEmpty(requirement.Permission))
        {
            context.Fail();
            return Task.CompletedTask;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Fail();
            return Task.CompletedTask;
        }

        var granted = context.User.Claims.Any(claim =>
            string.Equals(claim.Type, PermissionClaims.Type, StringComparison.Ordinal)
            && string.Equals(claim.Value, requirement.Permission, StringComparison.Ordinal));

        if (granted)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }

        return Task.CompletedTask;
    }
}
