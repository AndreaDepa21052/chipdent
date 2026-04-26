using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize(Roles = Policies.StaffRoles)]
[Route("documentazione")]
public class DocumentazioneController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;

    public DocumentazioneController(MongoContext mongo, ITenantContext tenant)
    {
        _mongo = mongo;
        _tenant = tenant;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? clinicaId = null)
    {
        var tid = _tenant.TenantId!;
        var cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).SortBy(c => c.Nome).ToListAsync();
        var filter = Builders<DocumentoClinica>.Filter.Eq(d => d.TenantId, tid);
        if (!string.IsNullOrEmpty(clinicaId))
            filter &= Builders<DocumentoClinica>.Filter.Eq(d => d.ClinicaId, clinicaId);

        var docs = await _mongo.DocumentiClinica.Find(filter).SortBy(d => d.DataScadenza).ToListAsync();
        var gruppi = cliniche
            .Where(c => string.IsNullOrEmpty(clinicaId) || c.Id == clinicaId)
            .Select(c => new ClinicaDocumentiGroup(c.Id, c.Nome, docs.Where(d => d.ClinicaId == c.Id).ToList()))
            .Where(g => g.Documenti.Count > 0 || string.IsNullOrEmpty(clinicaId) == false)
            .ToList();

        if (string.IsNullOrEmpty(clinicaId))
        {
            gruppi = cliniche.Select(c => new ClinicaDocumentiGroup(c.Id, c.Nome, docs.Where(d => d.ClinicaId == c.Id).ToList())).ToList();
        }

        ViewData["Section"] = "documentazione";
        return View(new DocumentazioneIndexViewModel
        {
            Gruppi = gruppi,
            FilterClinicaId = clinicaId,
            Cliniche = cliniche
        });
    }

    [HttpGet("nuovo")]
    [Authorize(Policy = Policies.RequireManager)]
    public async Task<IActionResult> Create(string? clinicaId = null)
    {
        ViewData["Section"] = "documentazione"; ViewData["IsNew"] = true;
        return View("Form", new DocumentoFormViewModel
        {
            ClinicaId = clinicaId ?? string.Empty,
            Cliniche = await CliniceAsync()
        });
    }

    [HttpPost("nuovo")]
    [Authorize(Policy = Policies.RequireManager)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DocumentoFormViewModel vm)
    {
        if (!ModelState.IsValid || string.IsNullOrEmpty(vm.ClinicaId))
        {
            ModelState.AddModelError(string.Empty, "Clinica obbligatoria.");
            ViewData["Section"] = "documentazione"; ViewData["IsNew"] = true;
            vm.Cliniche = await CliniceAsync();
            return View("Form", vm);
        }
        await _mongo.DocumentiClinica.InsertOneAsync(new DocumentoClinica
        {
            TenantId = _tenant.TenantId!,
            ClinicaId = vm.ClinicaId,
            Tipo = vm.Tipo,
            Titolo = vm.Titolo,
            Numero = vm.Numero,
            DataEmissione = vm.DataEmissione,
            DataScadenza = vm.DataScadenza,
            EnteEmittente = vm.EnteEmittente,
            Note = vm.Note
        });
        TempData["flash"] = "Documento aggiunto.";
        return RedirectToAction(nameof(Index), new { clinicaId = vm.ClinicaId });
    }

    [HttpGet("{id}/modifica")]
    [Authorize(Policy = Policies.RequireManager)]
    public async Task<IActionResult> Edit(string id)
    {
        var d = await _mongo.DocumentiClinica.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (d is null) return NotFound();
        ViewData["Section"] = "documentazione"; ViewData["IsNew"] = false;
        return View("Form", new DocumentoFormViewModel
        {
            Id = d.Id, ClinicaId = d.ClinicaId, Tipo = d.Tipo, Titolo = d.Titolo,
            Numero = d.Numero, DataEmissione = d.DataEmissione, DataScadenza = d.DataScadenza,
            EnteEmittente = d.EnteEmittente, Note = d.Note,
            Cliniche = await CliniceAsync()
        });
    }

    [HttpPost("{id}/modifica")]
    [Authorize(Policy = Policies.RequireManager)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, DocumentoFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "documentazione"; ViewData["IsNew"] = false;
            vm.Cliniche = await CliniceAsync();
            return View("Form", vm);
        }
        await _mongo.DocumentiClinica.UpdateOneAsync(
            x => x.Id == id && x.TenantId == _tenant.TenantId,
            Builders<DocumentoClinica>.Update
                .Set(x => x.ClinicaId, vm.ClinicaId)
                .Set(x => x.Tipo, vm.Tipo)
                .Set(x => x.Titolo, vm.Titolo)
                .Set(x => x.Numero, vm.Numero)
                .Set(x => x.DataEmissione, vm.DataEmissione)
                .Set(x => x.DataScadenza, vm.DataScadenza)
                .Set(x => x.EnteEmittente, vm.EnteEmittente)
                .Set(x => x.Note, vm.Note)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));
        TempData["flash"] = "Documento aggiornato.";
        return RedirectToAction(nameof(Index), new { clinicaId = vm.ClinicaId });
    }

    [HttpPost("{id}/elimina")]
    [Authorize(Policy = Policies.RequireAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        await _mongo.DocumentiClinica.DeleteOneAsync(x => x.Id == id && x.TenantId == _tenant.TenantId);
        TempData["flash"] = "Documento eliminato.";
        return RedirectToAction(nameof(Index));
    }

    private Task<List<Clinica>> CliniceAsync()
        => _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId).SortBy(c => c.Nome).ToListAsync();
}
