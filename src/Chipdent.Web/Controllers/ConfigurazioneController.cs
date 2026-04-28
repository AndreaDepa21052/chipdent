using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize(Policy = Policies.RequireManagement)]
[Route("configurazione")]
public class ConfigurazioneController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;

    public ConfigurazioneController(MongoContext mongo, ITenantContext tenant)
    {
        _mongo = mongo;
        _tenant = tenant;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var tid = _tenant.TenantId!;
        var cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).SortBy(c => c.Nome).ToListAsync();
        var clinicheLookup = cliniche.ToDictionary(c => c.Id, c => c.Nome);
        var soglie = await _mongo.SoglieCopertura.Find(s => s.TenantId == tid).SortBy(s => s.ClinicaId).ToListAsync();
        var categorie = await _mongo.CategorieDocumentoObbligatorie.Find(c => c.TenantId == tid).ToListAsync();
        var docs = await _mongo.DocumentiClinica.Find(d => d.TenantId == tid).ToListAsync();
        var workflow = await GetOrCreateWorkflow(tid);

        var soglieRows = soglie.Select(s => new SogliaCoperturaRow(s, clinicheLookup.GetValueOrDefault(s.ClinicaId, "—"))).ToList();
        var categorieRows = categorie.Select(c =>
        {
            var docCount = docs.Count(d => d.ClinicaId == c.ClinicaId && d.Tipo == c.Tipo);
            return new CategoriaObbligatoriaRow(c, clinicheLookup.GetValueOrDefault(c.ClinicaId, "—"), docCount);
        }).ToList();

        ViewData["Section"] = "configurazione";
        return View(new ConfigurazioneIndexViewModel
        {
            Workflow = workflow,
            Soglie = soglieRows,
            CategorieObbligatorie = categorieRows,
            Cliniche = cliniche
        });
    }

    // ───── Workflow ─────

    [HttpPost("workflow")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveWorkflow(WorkflowConfigViewModel vm)
    {
        var tid = _tenant.TenantId!;
        var existing = await GetOrCreateWorkflow(tid);
        await _mongo.WorkflowConfigs.UpdateOneAsync(
            w => w.Id == existing.Id,
            Builders<WorkflowConfiguration>.Update
                .Set(w => w.EscaladaFerieLunghe, vm.EscaladaFerieLunghe)
                .Set(w => w.GiorniMaxAutoApprove, vm.GiorniMaxAutoApprove)
                .Set(w => w.CircolariConfermaObbligatoria, vm.CircolariConfermaObbligatoria)
                .Set(w => w.NotificaSostituzioniViaEmail, vm.NotificaSostituzioniViaEmail)
                .Set(w => w.UpdatedAt, DateTime.UtcNow));
        TempData["flash"] = "Configurazione workflow aggiornata.";
        return RedirectToAction(nameof(Index));
    }

    // ───── Soglie copertura ─────

    [HttpPost("soglie/nuova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddSoglia(string clinicaId, RuoloDipendente ruolo, int minimoPerGiorno, DayOfWeek? giornoSettimana)
    {
        if (string.IsNullOrEmpty(clinicaId) || minimoPerGiorno < 1)
        {
            TempData["flash"] = "Dati soglia non validi.";
            return RedirectToAction(nameof(Index));
        }
        await _mongo.SoglieCopertura.InsertOneAsync(new SogliaCopertura
        {
            TenantId = _tenant.TenantId!,
            ClinicaId = clinicaId,
            Ruolo = ruolo,
            MinimoPerGiorno = minimoPerGiorno,
            GiornoSettimana = giornoSettimana,
            Attiva = true
        });
        TempData["flash"] = "Soglia aggiunta.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("soglie/{id}/elimina")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSoglia(string id)
    {
        await _mongo.SoglieCopertura.DeleteOneAsync(s => s.Id == id && s.TenantId == _tenant.TenantId);
        TempData["flash"] = "Soglia rimossa.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("soglie/{id}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleSoglia(string id)
    {
        var s = await _mongo.SoglieCopertura.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (s is null) return NotFound();
        await _mongo.SoglieCopertura.UpdateOneAsync(
            x => x.Id == id,
            Builders<SogliaCopertura>.Update.Set(x => x.Attiva, !s.Attiva));
        return RedirectToAction(nameof(Index));
    }

    // ───── Categorie documenti obbligatori ─────

    [HttpPost("categorie/nuova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCategoria(string clinicaId, TipoDocumento tipo, string? note)
    {
        if (string.IsNullOrEmpty(clinicaId))
        {
            TempData["flash"] = "Clinica obbligatoria.";
            return RedirectToAction(nameof(Index));
        }
        var existing = await _mongo.CategorieDocumentoObbligatorie
            .Find(c => c.TenantId == _tenant.TenantId && c.ClinicaId == clinicaId && c.Tipo == tipo)
            .AnyAsync();
        if (existing)
        {
            TempData["flash"] = "Categoria già presente per questa sede.";
            return RedirectToAction(nameof(Index));
        }
        await _mongo.CategorieDocumentoObbligatorie.InsertOneAsync(new CategoriaDocumentoObbligatoria
        {
            TenantId = _tenant.TenantId!,
            ClinicaId = clinicaId,
            Tipo = tipo,
            Attiva = true,
            Note = note
        });
        TempData["flash"] = "Categoria obbligatoria aggiunta.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("categorie/{id}/elimina")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCategoria(string id)
    {
        await _mongo.CategorieDocumentoObbligatorie.DeleteOneAsync(c => c.Id == id && c.TenantId == _tenant.TenantId);
        TempData["flash"] = "Categoria rimossa.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<WorkflowConfiguration> GetOrCreateWorkflow(string tid)
    {
        var w = await _mongo.WorkflowConfigs
            .Find(x => x.TenantId == tid && x.Key == WorkflowConfiguration.SingletonKey)
            .FirstOrDefaultAsync();
        if (w is not null) return w;
        w = new WorkflowConfiguration { TenantId = tid, Key = WorkflowConfiguration.SingletonKey };
        await _mongo.WorkflowConfigs.InsertOneAsync(w);
        return w;
    }
}
