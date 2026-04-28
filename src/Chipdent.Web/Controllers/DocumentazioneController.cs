using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Storage;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize(Policy = Policies.RequireBackoffice)]
[Route("documentazione")]
public class DocumentazioneController : Controller
{
    private const long MaxUploadBytes = 50 * 1024 * 1024; // 50MB
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".doc", ".docx", ".xls", ".xlsx", ".odt", ".txt" };

    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly IFileStorage _storage;

    public DocumentazioneController(MongoContext mongo, ITenantContext tenant, IFileStorage storage)
    {
        _mongo = mongo;
        _tenant = tenant;
        _storage = storage;
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
    [Authorize(Policy = Policies.RequireDirettore)]
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
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> Create(DocumentoFormViewModel vm)
    {
        if (!ModelState.IsValid || string.IsNullOrEmpty(vm.ClinicaId))
        {
            ModelState.AddModelError(string.Empty, "Clinica obbligatoria.");
            ViewData["Section"] = "documentazione"; ViewData["IsNew"] = true;
            vm.Cliniche = await CliniceAsync();
            return View("Form", vm);
        }

        var doc = new DocumentoClinica
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
        };

        if (vm.Allegato is { Length: > 0 })
        {
            var err = await TryAttachAsync(doc, vm.Allegato);
            if (err is not null)
            {
                ModelState.AddModelError(nameof(vm.Allegato), err);
                ViewData["Section"] = "documentazione"; ViewData["IsNew"] = true;
                vm.Cliniche = await CliniceAsync();
                return View("Form", vm);
            }
        }

        await _mongo.DocumentiClinica.InsertOneAsync(doc);
        TempData["flash"] = "Documento aggiunto.";
        return RedirectToAction(nameof(Index), new { clinicaId = vm.ClinicaId });
    }

    [HttpGet("{id}/modifica")]
    [Authorize(Policy = Policies.RequireDirettore)]
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
            AllegatoNomeAttuale = d.AllegatoNome,
            AllegatoPathAttuale = d.AllegatoPath,
            Cliniche = await CliniceAsync()
        });
    }

    [HttpPost("{id}/modifica")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> Edit(string id, DocumentoFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "documentazione"; ViewData["IsNew"] = false;
            vm.Cliniche = await CliniceAsync();
            return View("Form", vm);
        }

        var existing = await _mongo.DocumentiClinica.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (existing is null) return NotFound();

        var update = Builders<DocumentoClinica>.Update
            .Set(x => x.ClinicaId, vm.ClinicaId)
            .Set(x => x.Tipo, vm.Tipo)
            .Set(x => x.Titolo, vm.Titolo)
            .Set(x => x.Numero, vm.Numero)
            .Set(x => x.DataEmissione, vm.DataEmissione)
            .Set(x => x.DataScadenza, vm.DataScadenza)
            .Set(x => x.EnteEmittente, vm.EnteEmittente)
            .Set(x => x.Note, vm.Note)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        if (vm.Allegato is { Length: > 0 })
        {
            var doc = new DocumentoClinica();
            var err = await TryAttachAsync(doc, vm.Allegato);
            if (err is not null)
            {
                ModelState.AddModelError(nameof(vm.Allegato), err);
                ViewData["Section"] = "documentazione"; ViewData["IsNew"] = false;
                vm.Cliniche = await CliniceAsync();
                vm.AllegatoNomeAttuale = existing.AllegatoNome;
                vm.AllegatoPathAttuale = existing.AllegatoPath;
                return View("Form", vm);
            }
            // Se c'era un allegato precedente, lo elimina dal disco
            if (!string.IsNullOrEmpty(existing.AllegatoPath))
                await _storage.DeleteAsync(_tenant.TenantId!, existing.AllegatoPath);
            update = update
                .Set(x => x.AllegatoNome, doc.AllegatoNome)
                .Set(x => x.AllegatoPath, doc.AllegatoPath)
                .Set(x => x.AllegatoSize, doc.AllegatoSize);
        }

        await _mongo.DocumentiClinica.UpdateOneAsync(x => x.Id == id && x.TenantId == _tenant.TenantId, update);
        TempData["flash"] = "Documento aggiornato.";
        return RedirectToAction(nameof(Index), new { clinicaId = vm.ClinicaId });
    }

    [HttpPost("{id}/elimina")]
    [Authorize(Policy = Policies.RequireManagement)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var existing = await _mongo.DocumentiClinica.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (existing is not null && !string.IsNullOrEmpty(existing.AllegatoPath))
            await _storage.DeleteAsync(_tenant.TenantId!, existing.AllegatoPath);
        await _mongo.DocumentiClinica.DeleteOneAsync(x => x.Id == id && x.TenantId == _tenant.TenantId);
        TempData["flash"] = "Documento eliminato.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/scarica")]
    public async Task<IActionResult> Download(string id)
    {
        var d = await _mongo.DocumentiClinica.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (d is null || string.IsNullOrEmpty(d.AllegatoPath)) return NotFound();
        var abs = Path.Combine(HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootPath, d.AllegatoPath);
        if (!System.IO.File.Exists(abs)) return NotFound();
        var ext = Path.GetExtension(d.AllegatoNome ?? "").ToLowerInvariant();
        var mime = ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
        return PhysicalFile(abs, mime, d.AllegatoNome ?? "documento");
    }

    private async Task<string?> TryAttachAsync(DocumentoClinica target, Microsoft.AspNetCore.Http.IFormFile file)
    {
        if (file.Length > MaxUploadBytes) return $"File troppo grande (max {MaxUploadBytes / (1024 * 1024)}MB).";
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext)) return $"Estensione non consentita: {ext}";

        await using var stream = file.OpenReadStream();
        var stored = await _storage.SaveAsync(_tenant.TenantId!, "documenti", file.FileName, stream, file.ContentType);
        target.AllegatoNome = file.FileName;
        target.AllegatoPath = stored.RelativePath;
        target.AllegatoSize = stored.SizeBytes;
        return null;
    }

    private Task<List<Clinica>> CliniceAsync()
        => _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId).SortBy(c => c.Nome).ToListAsync();
}
