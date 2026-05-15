using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize(Policy = Policies.RequireBackoffice)]
[Route("societa")]
public class SocietaController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;

    public SocietaController(MongoContext mongo, ITenantContext tenant)
    {
        _mongo = mongo;
        _tenant = tenant;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var items = await _mongo.Societa
            .Find(s => s.TenantId == _tenant.TenantId)
            .SortBy(s => s.Nome)
            .ToListAsync();

        // Conteggio cliniche per società per la griglia.
        var cliniche = await _mongo.Cliniche
            .Find(c => c.TenantId == _tenant.TenantId)
            .ToListAsync();
        var clinichePerSocieta = cliniche
            .Where(c => !string.IsNullOrEmpty(c.SocietaId))
            .GroupBy(c => c.SocietaId!)
            .ToDictionary(g => g.Key, g => g.Count());

        ViewData["Section"] = "societa";
        ViewData["ClinicheCount"] = clinichePerSocieta;
        return View(items);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Details(string id)
    {
        var societa = await Load(id);
        if (societa is null) return NotFound();

        var cliniche = await _mongo.Cliniche
            .Find(c => c.TenantId == _tenant.TenantId && c.SocietaId == id)
            .SortBy(c => c.Nome)
            .ToListAsync();

        ViewData["Section"] = "societa";
        ViewData["Cliniche"] = cliniche;
        return View(societa);
    }

    [HttpGet("nuova")]
    [Authorize(Policy = Policies.RequireManagement)]
    public IActionResult Create()
    {
        ViewData["Section"] = "societa";
        ViewData["IsNew"] = true;
        return View("Form", new Societa());
    }

    [HttpPost("nuova")]
    [Authorize(Policy = Policies.RequireManagement)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Societa model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "societa";
            ViewData["IsNew"] = true;
            return View("Form", model);
        }
        model.TenantId = _tenant.TenantId!;
        model.CreatedAt = DateTime.UtcNow;
        await _mongo.Societa.InsertOneAsync(model);
        TempData["flash"] = $"Società «{model.Nome}» creata.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/modifica")]
    public async Task<IActionResult> Edit(string id)
    {
        var societa = await Load(id);
        if (societa is null) return NotFound();
        ViewData["Section"] = "societa";
        ViewData["IsNew"] = false;
        return View("Form", societa);
    }

    [HttpPost("{id}/modifica")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, Societa model)
    {
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "societa";
            ViewData["IsNew"] = false;
            return View("Form", model);
        }
        var existing = await Load(id);
        if (existing is null) return NotFound();
        model.TenantId = existing.TenantId;
        model.CreatedAt = existing.CreatedAt;
        model.UpdatedAt = DateTime.UtcNow;
        await _mongo.Societa.ReplaceOneAsync(s => s.Id == id && s.TenantId == _tenant.TenantId, model);
        TempData["flash"] = $"Società «{model.Nome}» aggiornata.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id}/elimina")]
    [Authorize(Policy = Policies.RequireManagement)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        // Le cliniche legate restano: il riferimento si "azzera" lato UI
        // perché il SocietaId punta a un id non più presente.
        var clinicheLegate = (int)await _mongo.Cliniche.CountDocumentsAsync(
            c => c.TenantId == _tenant.TenantId && c.SocietaId == id);
        if (clinicheLegate > 0)
        {
            await _mongo.Cliniche.UpdateManyAsync(
                c => c.TenantId == _tenant.TenantId && c.SocietaId == id,
                Builders<Clinica>.Update.Set(c => c.SocietaId, null).Set(c => c.UpdatedAt, DateTime.UtcNow));
        }
        await _mongo.Societa.DeleteOneAsync(s => s.Id == id && s.TenantId == _tenant.TenantId);
        TempData["flash"] = clinicheLegate > 0
            ? $"Società eliminata. {clinicheLegate} cliniche svincolate."
            : "Società eliminata.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<Societa?> Load(string id)
        => await _mongo.Societa
            .Find(s => s.Id == id && s.TenantId == _tenant.TenantId)
            .FirstOrDefaultAsync();
}
