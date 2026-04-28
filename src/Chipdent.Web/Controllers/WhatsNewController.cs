using Chipdent.Web.Infrastructure;
using Chipdent.Web.Infrastructure.Changelog;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chipdent.Web.Controllers;

[Authorize]
[Route("novita")]
public class WhatsNewController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["Section"] = "novita";
        var groups = Changelog.Entries
            .GroupBy(e => $"{e.Version} · {e.Codename}")
            .Select(g => new WhatsNewGroup(
                Header: g.Key,
                Date: g.Max(e => e.Date),
                Entries: g.OrderBy(e => e.Category).ThenBy(e => e.Title).ToList()))
            .OrderByDescending(g => g.Date)
            .ToList();
        return View(new WhatsNewViewModel
        {
            CurrentVersion = AppVersion.Display,
            IsMvpReleased = AppVersion.IsMvpReleased,
            Groups = groups
        });
    }
}
