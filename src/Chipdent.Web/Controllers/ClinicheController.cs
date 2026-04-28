using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize(Policy = Policies.RequireBackoffice)]
[Route("cliniche")]
public class ClinicheController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;

    public ClinicheController(MongoContext mongo, ITenantContext tenant)
    {
        _mongo = mongo;
        _tenant = tenant;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? view = null)
    {
        var items = await _mongo.Cliniche
            .Find(c => c.TenantId == _tenant.TenantId)
            .SortBy(c => c.Nome)
            .ToListAsync();
        ViewData["Section"] = "cliniche";
        ViewData["ViewMode"] = view == "mappa" ? "mappa" : "lista";
        return View(items);
    }

    [HttpGet("mappa.json")]
    [Produces("application/json")]
    public async Task<IActionResult> MapData()
    {
        var items = await _mongo.Cliniche
            .Find(c => c.TenantId == _tenant.TenantId)
            .ToListAsync();
        var pins = items
            .Where(c => c.IsGeolocalized)
            .Select(c => new
            {
                id = c.Id,
                nome = c.Nome,
                citta = c.Citta,
                indirizzo = c.Indirizzo,
                stato = c.Stato.ToString(),
                lat = c.Latitudine,
                lng = c.Longitudine,
                riuniti = c.NumeroRiuniti
            });
        return Json(pins);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Details(string id)
    {
        var clinica = await Load(id);
        if (clinica is null) return NotFound();

        var dottori = await _mongo.Dottori
            .Find(d => d.TenantId == _tenant.TenantId && d.ClinicaPrincipaleId == id)
            .ToListAsync();

        var dipendenti = await _mongo.Dipendenti
            .Find(d => d.TenantId == _tenant.TenantId && d.ClinicaId == id)
            .ToListAsync();

        ViewData["Section"] = "cliniche";
        ViewData["Dottori"] = dottori;
        ViewData["Dipendenti"] = dipendenti;
        return View(clinica);
    }

    [HttpGet("nuova")]
    [Authorize(Policy = Policies.RequireManagement)]
    public IActionResult Create()
    {
        ViewData["Section"] = "cliniche";
        ViewData["IsNew"] = true;
        return View("Form", new Clinica());
    }

    [HttpPost("nuova")]
    [Authorize(Policy = Policies.RequireManagement)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Clinica model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "cliniche";
            ViewData["IsNew"] = true;
            return View("Form", model);
        }
        model.TenantId = _tenant.TenantId!;
        model.CreatedAt = DateTime.UtcNow;
        await _mongo.Cliniche.InsertOneAsync(model);
        TempData["flash"] = $"Clinica «{model.Nome}» creata.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/modifica")]
    [Authorize(Policy = Policies.RequireManagement)]
    public async Task<IActionResult> Edit(string id)
    {
        var clinica = await Load(id);
        if (clinica is null) return NotFound();
        ViewData["Section"] = "cliniche";
        ViewData["IsNew"] = false;
        return View("Form", clinica);
    }

    [HttpPost("{id}/modifica")]
    [Authorize(Policy = Policies.RequireManagement)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, Clinica model)
    {
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "cliniche";
            ViewData["IsNew"] = false;
            return View("Form", model);
        }
        var existing = await Load(id);
        if (existing is null) return NotFound();
        model.TenantId = existing.TenantId;
        model.CreatedAt = existing.CreatedAt;
        model.UpdatedAt = DateTime.UtcNow;
        await _mongo.Cliniche.ReplaceOneAsync(c => c.Id == id && c.TenantId == _tenant.TenantId, model);
        TempData["flash"] = $"Clinica «{model.Nome}» aggiornata.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id}/elimina")]
    [Authorize(Policy = Policies.RequireManagement)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        await _mongo.Cliniche.DeleteOneAsync(c => c.Id == id && c.TenantId == _tenant.TenantId);
        TempData["flash"] = "Clinica eliminata.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<Clinica?> Load(string id)
        => await _mongo.Cliniche
            .Find(c => c.Id == id && c.TenantId == _tenant.TenantId)
            .FirstOrDefaultAsync();
}
