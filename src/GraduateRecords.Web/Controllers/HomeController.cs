using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GraduateRecords.Web.Controllers;

public sealed class HomeController : Controller
{
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Index() => View();
}
