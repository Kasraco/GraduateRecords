using Microsoft.AspNetCore.Mvc;

namespace GraduateRecords.Web.Controllers;

public sealed class HomeController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}
