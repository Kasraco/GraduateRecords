using Microsoft.AspNetCore.Mvc;

namespace GraduateRecords.Web.Controllers;

/// <summary>
/// Application landing page. It carries no <c>[AllowAnonymous]</c>: the global
/// authenticated-by-default fallback policy (AGENTS §13) protects it, so an
/// anonymous request is challenged (browser → <c>/Account/Login</c>) or answered
/// with 401 for non-browser clients.
/// </summary>
public sealed class HomeController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}
