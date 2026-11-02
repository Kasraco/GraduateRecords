using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Options;

namespace GraduateRecords.Web.Authorization;

/// <summary>
/// Maps a policy name of the form <c>Permission:&lt;value&gt;</c> onto a policy
/// that requires the authenticated session to carry that exact <c>permission</c>
/// claim (ADR-0004 §10.2: an endpoint without an explicit policy is a
/// build/review failure; ADR-0017 §5 for the canonical value format). Every
/// other policy name — including the default and fallback policies — is
/// delegated to the framework provider, so authentication and the global
/// authenticated-by-default fallback are unchanged.
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    public const string PolicyPrefix = "Permission:";

    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
        {
            var permission = policyName[PolicyPrefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}
