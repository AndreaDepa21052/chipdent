using System.Diagnostics;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Chipdent.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        if (User?.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Dashboard");
        }
        return RedirectToAction("Login", "Account");
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
