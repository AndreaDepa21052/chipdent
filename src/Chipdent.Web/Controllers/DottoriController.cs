using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize(Roles = Policies.StaffRoles)]
[Route("dottori")]
public class DottoriController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;

    public DottoriController(MongoContext mongo, ITenantContext tenant)
    {
        _mongo = mongo;
        _tenant = tenant;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var items = await _mongo.Dottori
            .Find(d => d.TenantId == _tenant.TenantId)
            .SortBy(d => d.Cognome)
            .ToListAsync();

        var cliniche = await CliniceLookupAsync();
        ViewData["Section"] = "dottori";
        ViewData["Cliniche"] = cliniche;
        return View(items);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Details(string id)
    {
        var d = await Load(id);
        if (d is null) return NotFound();
        var cliniche = await CliniceLookupAsync();
        ViewData["Section"] = "dottori";
        ViewData["Cliniche"] = cliniche;
        return View(d);
    }

    [HttpGet("nuovo")]
    [Authorize(Policy = Policies.RequireHR)]
    public async Task<IActionResult> Create()
    {
        ViewData["Section"] = "dottori";
        ViewData["IsNew"] = true;
        ViewData["Cliniche"] = await CliniceListAsync();
        return View("Form", new Dottore());
    }

    [HttpPost("nuovo")]
    [Authorize(Policy = Policies.RequireHR)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Dottore model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "dottori";
            ViewData["IsNew"] = true;
            ViewData["Cliniche"] = await CliniceListAsync();
            return View("Form", model);
        }
        model.TenantId = _tenant.TenantId!;
        model.CreatedAt = DateTime.UtcNow;
        await _mongo.Dottori.InsertOneAsync(model);
        TempData["flash"] = $"Dottore «{model.NomeCompleto}» creato.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/modifica")]
    [Authorize(Policy = Policies.RequireHR)]
    public async Task<IActionResult> Edit(string id)
    {
        var d = await Load(id);
        if (d is null) return NotFound();
        ViewData["Section"] = "dottori";
        ViewData["IsNew"] = false;
        ViewData["Cliniche"] = await CliniceListAsync();
        return View("Form", d);
    }

    [HttpPost("{id}/modifica")]
    [Authorize(Policy = Policies.RequireHR)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, Dottore model)
    {
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "dottori";
            ViewData["IsNew"] = false;
            ViewData["Cliniche"] = await CliniceListAsync();
            return View("Form", model);
        }
        var existing = await Load(id);
        if (existing is null) return NotFound();
        model.TenantId = existing.TenantId;
        model.CreatedAt = existing.CreatedAt;
        model.UpdatedAt = DateTime.UtcNow;
        await _mongo.Dottori.ReplaceOneAsync(d => d.Id == id && d.TenantId == _tenant.TenantId, model);
        TempData["flash"] = $"Dottore «{model.NomeCompleto}» aggiornato.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id}/elimina")]
    [Authorize(Policy = Policies.RequireAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        await _mongo.Dottori.DeleteOneAsync(d => d.Id == id && d.TenantId == _tenant.TenantId);
        TempData["flash"] = "Dottore eliminato.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<Dottore?> Load(string id)
        => await _mongo.Dottori
            .Find(d => d.Id == id && d.TenantId == _tenant.TenantId)
            .FirstOrDefaultAsync();

    private Task<List<Clinica>> CliniceListAsync()
        => _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId).SortBy(c => c.Nome).ToListAsync();

    private async Task<Dictionary<string, string>> CliniceLookupAsync()
        => (await CliniceListAsync()).ToDictionary(c => c.Id, c => c.Nome);
}
