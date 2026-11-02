using GraduateRecords.Application.Common.Results;
using GraduateRecords.Web.Models.Admin;
using Microsoft.AspNetCore.Mvc;

namespace GraduateRecords.Web.Infrastructure;

/// <summary>
/// AGENTS §5 result mapping for the administration endpoints:
/// <c>NotFound</c> → 404, <c>Forbidden</c> → 403, and every remaining expected
/// failure (validation on a redirect flow, conflict, unavailable) → the shared
/// Persian toast plus a Post-Redirect-Get, so the browser never re-submits.
/// The message shown is the handler's own Persian message; it never contains a
/// stack trace, SQL, path or identifier (AGENTS §18).
/// </summary>
internal static class AdministrationResultMapping
{
    public const string FriendlyFallbackMessage = "خطایی رخ داده است.";

    public static IActionResult ReadFailure(
        Controller controller,
        Error? error,
        string fallbackAction,
        string fallbackController,
        object? routeValues = null) =>
        Failure(controller, error, fallbackAction, fallbackController, routeValues);

    public static IActionResult MutationFailure(
        Controller controller,
        Error? error,
        string fallbackAction,
        string fallbackController,
        object? routeValues = null) =>
        Failure(controller, error, fallbackAction, fallbackController, routeValues);

    private static IActionResult Failure(
        Controller controller,
        Error? error,
        string fallbackAction,
        string fallbackController,
        object? routeValues)
    {
        switch (error?.Kind)
        {
            case ErrorKind.NotFound:
                return new NotFoundResult();

            case ErrorKind.Forbidden:
                return new ForbidResult();

            default:
                controller.TempData[TempDataKeys.Error] =
                    string.IsNullOrEmpty(error?.Message) ? FriendlyFallbackMessage : error.Message;

                return new RedirectToActionResult(fallbackAction, fallbackController, routeValues);
        }
    }
}
