using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize(Roles = Policies.StaffRoles)]
[Route("dipendenti")]
public class DipendentiController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;

    public DipendentiController(MongoContext mongo, ITenantContext tenant)
    {
        _mongo = mongo;
        _tenant = tenant;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var items = await _mongo.Dipendenti
            .Find(d => d.TenantId == _tenant.TenantId)
            .SortBy(d => d.Cognome)
            .ToListAsync();

        var cliniche = await CliniceLookupAsync();
        ViewData["Section"] = "dipendenti";
        ViewData["Cliniche"] = cliniche;
        return View(items);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Details(string id)
    {
        var d = await Load(id);
        if (d is null) return NotFound();
        ViewData["Section"] = "dipendenti";
        ViewData["Cliniche"] = await CliniceLookupAsync();
        return View(d);
    }

    [HttpGet("nuovo")]
    [Authorize(Policy = Policies.RequireHR)]
    public async Task<IActionResult> Create()
    {
        ViewData["Section"] = "dipendenti";
        ViewData["IsNew"] = true;
        ViewData["Cliniche"] = await CliniceListAsync();
        return View("Form", new Dipendente { DataAssunzione = DateTime.UtcNow });
    }

    [HttpPost("nuovo")]
    [Authorize(Policy = Policies.RequireHR)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Dipendente model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "dipendenti";
            ViewData["IsNew"] = true;
            ViewData["Cliniche"] = await CliniceListAsync();
            return View("Form", model);
        }
        model.TenantId = _tenant.TenantId!;
        model.CreatedAt = DateTime.UtcNow;
        await _mongo.Dipendenti.InsertOneAsync(model);
        TempData["flash"] = $"Dipendente «{model.NomeCompleto}» creato.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/modifica")]
    [Authorize(Policy = Policies.RequireHR)]
    public async Task<IActionResult> Edit(string id)
    {
        var d = await Load(id);
        if (d is null) return NotFound();
        ViewData["Section"] = "dipendenti";
        ViewData["IsNew"] = false;
        ViewData["Cliniche"] = await CliniceListAsync();
        return View("Form", d);
    }

    [HttpPost("{id}/modifica")]
    [Authorize(Policy = Policies.RequireHR)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, Dipendente model)
    {
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "dipendenti";
            ViewData["IsNew"] = false;
            ViewData["Cliniche"] = await CliniceListAsync();
            return View("Form", model);
        }
        var existing = await Load(id);
        if (existing is null) return NotFound();
        model.TenantId = existing.TenantId;
        model.CreatedAt = existing.CreatedAt;
        model.UpdatedAt = DateTime.UtcNow;
        await _mongo.Dipendenti.ReplaceOneAsync(d => d.Id == id && d.TenantId == _tenant.TenantId, model);
        TempData["flash"] = $"Dipendente «{model.NomeCompleto}» aggiornato.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id}/elimina")]
    [Authorize(Policy = Policies.RequireAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        await _mongo.Dipendenti.DeleteOneAsync(d => d.Id == id && d.TenantId == _tenant.TenantId);
        TempData["flash"] = "Dipendente eliminato.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<Dipendente?> Load(string id)
        => await _mongo.Dipendenti
            .Find(d => d.Id == id && d.TenantId == _tenant.TenantId)
            .FirstOrDefaultAsync();

    private Task<List<Clinica>> CliniceListAsync()
        => _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId).SortBy(c => c.Nome).ToListAsync();

    private async Task<Dictionary<string, string>> CliniceLookupAsync()
        => (await CliniceListAsync()).ToDictionary(c => c.Id, c => c.Nome);
}
