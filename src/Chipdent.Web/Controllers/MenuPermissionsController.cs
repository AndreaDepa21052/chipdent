using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Models;
using Chipdent.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chipdent.Web.Controllers;

/// <summary>
/// Pannello esclusivo del PlatformAdmin: configura, per ciascun ruolo, quali
/// sezioni della sidebar sono visibili. Le scelte sono globali (cross-tenant).
/// </summary>
[Authorize(Policy = Policies.RequirePlatformAdmin)]
[Route("amministrazione/menu-permessi")]
public class MenuPermissionsController : Controller
{
    private readonly IMenuVisibilityService _service;

    public MenuPermissionsController(IMenuVisibilityService service)
    {
        _service = service;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var hidden = await _service.GetAllAsync(HttpContext.RequestAborted);
        ViewData["Section"] = "menu-permessi";
        ViewData["Title"]   = "Permessi menu";
        return View(new MenuPermissionsViewModel
        {
            Groups = MenuCatalog.Groups,
            Roles = MenuCatalog.ConfigurableRoles,
            Hidden = hidden,
            Flash = TempData["flash"] as string ?? string.Empty
        });
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(IFormCollection form)
    {
        var allSlugs = MenuCatalog.AllSections.Select(s => s.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var role in MenuCatalog.ConfigurableRoles)
        {
            // I checkbox sono "visibile": il name del checkbox è "visible[role][slug]".
            // Se assente nel POST → la sezione è nascosta per quel ruolo.
            var visiblePrefix = $"visible[{role}][";
            var visibleForRole = form.Keys
                .Where(k => k.StartsWith(visiblePrefix, StringComparison.Ordinal) && k.EndsWith("]", StringComparison.Ordinal))
                .Select(k => k.Substring(visiblePrefix.Length, k.Length - visiblePrefix.Length - 1))
                .Where(slug => allSlugs.Contains(slug))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var hidden = allSlugs.Where(s => !visibleForRole.Contains(s)).ToList();
            await _service.SetHiddenForRoleAsync(role, hidden, HttpContext.RequestAborted);
        }

        TempData["flash"] = "Permessi menu aggiornati.";
        return RedirectToAction(nameof(Index));
    }
}
