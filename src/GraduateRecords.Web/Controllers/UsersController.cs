using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Messaging;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Application.Features.Administration.Users;
using GraduateRecords.Web.Authorization;
using GraduateRecords.Web.Infrastructure;
using GraduateRecords.Web.Middleware;
using GraduateRecords.Web.Models.Admin;
using Microsoft.AspNetCore.Mvc;

namespace GraduateRecords.Web.Controllers;

/// <summary>
/// Administration user account screens (ADR-0002 §2.1 creation, §8.2
/// account-data-only view). AGENTS §5: this controller only binds view models,
/// calls Application handlers and maps their <c>Result</c> — every rule
/// (capability, supported roles, self-role-change, last-Administrator) lives in
/// the handlers. Gated by <c>Users.Manage</c> (ADR-0017 §9).
/// </summary>
[Area("Admin")]
[HasPermission(Permissions.UsersManage)]
public sealed class UsersController(
    IQueryHandler<ListUsersQuery, IdentityUserPage> listUsers,
    IQueryHandler<GetUserQuery, IdentityUserDetail> getUser,
    ICommandHandler<CreateUserCommand, string> createUser,
    ICurrentUser currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? q, int page = 1, CancellationToken cancellationToken = default)
    {
        var result = await listUsers.HandleAsync(new ListUsersQuery(q, page), cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return AdministrationResultMapping.ReadFailure(
                this, result.Error, nameof(Index), nameof(UsersController));
        }

        return View(MapPage(result.Value, q));
    }

    [HttpGet]
    public async Task<IActionResult> Details(string id, CancellationToken cancellationToken = default)
    {
        var result = await getUser.HandleAsync(new GetUserQuery(id), cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return AdministrationResultMapping.ReadFailure(
                this, result.Error, nameof(Index), nameof(UsersController));
        }

        return View(MapDetail(result.Value));
    }

    [HttpGet]
    public IActionResult Create() => View(new CreateUserViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserViewModel model, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await createUser.HandleAsync(BuildCommand(model), cancellationToken);
        return HandleCreateResult(model, result);
    }

    /// <summary>DTO → view-model mapping only; no rule lives here (AGENTS §4/§5).</summary>
    private UserDetailsViewModel MapDetail(IdentityUserDetail user) => new()
    {
        Id = user.Id,
        FullName = user.FullName,
        NationalIdMasked = user.NationalIdMasked,
        Kind = user.Kind,
        Roles = user.Roles,
        AvailableRoles = RoleNames.AllSupported
            .Where(role => !user.Roles.Contains(role, StringComparer.Ordinal))
            .ToList(),
        // UX only: the server-side §20 guard rejects self-role-change anyway.
        IsOwnAccount = string.Equals(user.Id, currentUser.UserId, StringComparison.Ordinal),
    };

    /// <summary>Binds the view model into the command and carries request observability for the audit event.</summary>
    private CreateUserCommand BuildCommand(CreateUserViewModel model) =>
        new(
            model.NationalId,
            model.FullName,
            model.MobileNumber,
            model.Kind,
            model.Roles,
            new RequestAuditContext(
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(),
                CorrelationIdMiddleware.GetCorrelationId(HttpContext)));

    /// <summary>
    /// AGENTS §5 result mapping: Forbidden → 403, everything else the handler
    /// can reject (validation, duplicate National ID, unavailable) re-renders the
    /// form with its Persian message and the entered values preserved;
    /// success → Post-Redirect-Get with a toast.
    /// </summary>
    private IActionResult HandleCreateResult(CreateUserViewModel model, Result<string> result)
    {
        if (!result.IsSuccess || result.Value is null)
        {
            if (result.Error?.Kind == ErrorKind.Forbidden)
            {
                return new ForbidResult();
            }

            ModelState.AddModelError(
                string.Empty,
                result.Error?.Message ?? AdministrationResultMapping.FriendlyFallbackMessage);
            return View(model);
        }

        TempData[TempDataKeys.Success] = "کاربر جدید با موفقیت ایجاد شد.";
        return RedirectToAction(nameof(Details), new { id = result.Value });
    }

    private static UsersIndexViewModel MapPage(IdentityUserPage page, string? query) => new()
    {
        Query = query,
        Page = page.Page,
        TotalCount = page.TotalCount,
        Users = page.Users
            .Select(user => new UserRowViewModel
            {
                Id = user.Id,
                FullName = user.FullName,
                NationalIdMasked = user.NationalIdMasked,
                Kind = user.Kind,
                Roles = user.Roles,
            })
            .ToList(),
    };
}
