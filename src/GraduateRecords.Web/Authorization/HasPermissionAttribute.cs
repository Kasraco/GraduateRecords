using Microsoft.AspNetCore.Authorization;

namespace GraduateRecords.Web.Authorization;

/// <summary>
/// Explicit per-endpoint permission gate (ADR-0004 §10.2, ADR-0017 §21):
/// <c>[HasPermission(Permissions.UsersManage)]</c> resolves to the policy
/// <c>Permission:Users.Manage</c> built by <see cref="PermissionPolicyProvider"/>.
/// <para>Applied to a controller it gates every action on it; applied to an
/// action it gates only that action. It never grants anything by itself — the
/// principal must carry the matching <c>permission</c> claim, which comes only
/// from role membership (ADR-0017 §6).</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permission) =>
        Policy = PermissionPolicyProvider.PolicyPrefix + permission;
}
