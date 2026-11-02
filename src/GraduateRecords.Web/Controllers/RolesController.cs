using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Messaging;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Application.Features.Administration.Roles;
using GraduateRecords.Web.Authorization;
using GraduateRecords.Web.Infrastructure;
using GraduateRecords.Web.Middleware;
using GraduateRecords.Web.Models.Admin;
using Microsoft.AspNetCore.Mvc;

namespace GraduateRecords.Web.Controllers;

/// <summary>
/// Role membership administration and the read-only role → permission view.
/// <para>
/// Grant/revoke are gated by <c>Roles.Assign</c> (ADR-0017 §10 — one
/// capability for both directions); the read-only mapping page is gated by
/// <c>Users.Manage</c>. v1 has no permission-to-role editor and no per-user
/// permission assignment (ADR-0004 §2, ADR-0017 §6/§10), so no action here can
/// change a mapping. All rules — supported roles only, self-role-change ban,
/// last-Administrator protection — are enforced in the Application handlers.
/// </para>
/// </summary>
[Area("Admin")]
public sealed class RolesController(
    IQueryHandler<GetRolePermissionsQuery, IReadOnlyList<RolePermissionItem>> getRolePermissions,
    ICommandHandler<GrantRoleCommand, RoleChangeResult> grantRole,
    ICommandHandler<RevokeRoleCommand, RoleChangeResult> revokeRole) : Controller
{
    [HttpGet]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var result = await getRolePermissions.HandleAsync(new GetRolePermissionsQuery(), cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return AdministrationResultMapping.ReadFailure(
                this, result.Error, nameof(Index), nameof(RolesController));
        }

        return View(new RolesIndexViewModel { Roles = MapRoles(result.Value) });
    }

    [HttpPost]
    [HasPermission(Permissions.RolesAssign)]
    public async Task<IActionResult> Grant(string id, string roleName, CancellationToken cancellationToken = default)
    {
        var result = await grantRole.HandleAsync(
            new GrantRoleCommand(id, roleName, RequestContext()), cancellationToken);

        return HandleRoleChange(
            result,
            id,
            appliedMessage: "نقش با موفقیت اعطا شد.",
            unchangedMessage: "این نقش قبلاً به کاربر اعطا شده بود.");
    }

    [HttpPost]
    [HasPermission(Permissions.RolesAssign)]
    public async Task<IActionResult> Revoke(string id, string roleName, CancellationToken cancellationToken = default)
    {
        var result = await revokeRole.HandleAsync(
            new RevokeRoleCommand(id, roleName, RequestContext()), cancellationToken);

        return HandleRoleChange(
            result,
            id,
            appliedMessage: "نقش با موفقیت حذف شد.",
            unchangedMessage: "این نقش قبلاً از کاربر حذف شده بود.");
    }

    /// <summary>DTO → view-model mapping only; no rule lives here (AGENTS §4/§5).</summary>
    private static List<RolePermissionRowViewModel> MapRoles(IReadOnlyList<RolePermissionItem> items) =>
        items.Select(item => new RolePermissionRowViewModel
        {
            RoleName = item.RoleName,
            Permissions = item.Permissions,
        }).ToList();

    /// <summary>
    /// AGENTS §5 result mapping shared by both role mutations: an expected
    /// failure becomes a toast plus Post-Redirect-Get back to the user page
    /// (NotFound → 404 and Forbidden → 403 are applied by the shared mapper),
    /// and success becomes PRG with the toast stating whether the membership
    /// actually changed. No business rule is evaluated here.
    /// </summary>
    private IActionResult HandleRoleChange(
        Result<RoleChangeResult> result,
        string userId,
        string appliedMessage,
        string unchangedMessage)
    {
        if (!result.IsSuccess)
        {
            return AdministrationResultMapping.MutationFailure(
                this, result.Error, nameof(UsersController.Details), ControllerName, new { id = userId });
        }

        TempData[TempDataKeys.Success] = result.Value?.Changed == true ? appliedMessage : unchangedMessage;
        return RedirectToAction(nameof(UsersController.Details), ControllerName, new { id = userId });
    }

    /// <summary>
    /// Route segment of the user screens: the conventional route uses the
    /// controller name without its <c>Controller</c> suffix, so
    /// <c>nameof(UsersController)</c> would generate <c>/Admin/UsersController/…</c>.
    /// </summary>
    private const string ControllerName = "Users";

    /// <summary>Request observability for the audit event (never identity — that comes from ICurrentUser).</summary>
    private RequestAuditContext RequestContext() =>
        new(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            CorrelationIdMiddleware.GetCorrelationId(HttpContext));
}
