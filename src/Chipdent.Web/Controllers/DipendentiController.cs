using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Audit;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize(Policy = Policies.RequireBackoffice)]
[Route("dipendenti")]
public class DipendentiController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;

    public DipendentiController(MongoContext mongo, ITenantContext tenant, IAuditService audit)
    {
        _mongo = mongo;
        _tenant = tenant;
        _audit = audit;
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
    public async Task<IActionResult> Details(string id, string tab = "anagrafica")
    {
        var d = await Load(id);
        if (d is null) return NotFound();

        var cliniche = await CliniceListAsync();
        var clinicaNome = cliniche.FirstOrDefault(c => c.Id == d.ClinicaId)?.Nome;

        string? managerNome = null;
        if (!string.IsNullOrEmpty(d.ManagerId))
        {
            var mgr = await _mongo.Dipendenti.Find(x => x.Id == d.ManagerId && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
            managerNome = mgr?.NomeCompleto;
        }

        var storico = await _mongo.Trasferimenti
            .Find(t => t.TenantId == _tenant.TenantId && t.PersonaId == id && t.PersonaTipo == TipoPersona.Dipendente)
            .SortByDescending(t => t.DataEffetto)
            .ToListAsync();

        var audit = await _mongo.Audit
            .Find(a => a.TenantId == _tenant.TenantId && a.EntityType == "Dipendente" && a.EntityId == id)
            .SortByDescending(a => a.CreatedAt)
            .Limit(50)
            .ToListAsync();

        ViewData["Section"] = "dipendenti";
        return View("Profile", new DipendenteProfileViewModel
        {
            Dipendente = d,
            ClinicaCorrenteNome = clinicaNome,
            ManagerNome = managerNome,
            Storico = storico,
            Audit = audit,
            Cliniche = cliniche,
            Tab = tab
        });
    }

    [HttpGet("nuovo")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    public async Task<IActionResult> Create()
    {
        ViewData["Section"] = "dipendenti";
        ViewData["IsNew"] = true;
        ViewData["Cliniche"] = await CliniceListAsync();
        ViewData["Managers"] = await ManagerCandidatesAsync();
        return View("Form", new Dipendente { DataAssunzione = DateTime.Today });
    }

    [HttpPost("nuovo")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Dipendente model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "dipendenti";
            ViewData["IsNew"] = true;
            ViewData["Cliniche"] = await CliniceListAsync();
            ViewData["Managers"] = await ManagerCandidatesAsync();
            return View("Form", model);
        }
        model.TenantId = _tenant.TenantId!;
        model.CreatedAt = DateTime.UtcNow;
        await _mongo.Dipendenti.InsertOneAsync(model);

        await _audit.LogAsync("Dipendente", model.Id, model.NomeCompleto, AuditAction.Created, actor: User);

        if (!string.IsNullOrEmpty(model.ClinicaId))
        {
            var cliniche = await CliniceListAsync();
            var clinica = cliniche.FirstOrDefault(c => c.Id == model.ClinicaId);
            await CreateTransferAsync(model.Id, TipoPersona.Dipendente, model.NomeCompleto,
                null, null, clinica?.Id ?? "", clinica?.Nome ?? "",
                MotivoTrasferimento.Riorganizzazione, "Assegnazione iniziale", model.DataAssunzione);
        }

        TempData["flash"] = $"Dipendente «{model.NomeCompleto}» creato.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/modifica")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    public async Task<IActionResult> Edit(string id)
    {
        var d = await Load(id);
        if (d is null) return NotFound();
        ViewData["Section"] = "dipendenti";
        ViewData["IsNew"] = false;
        ViewData["Cliniche"] = await CliniceListAsync();
        ViewData["Managers"] = await ManagerCandidatesAsync(excludeId: id);
        return View("Form", d);
    }

    [HttpPost("{id}/modifica")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, Dipendente model)
    {
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "dipendenti";
            ViewData["IsNew"] = false;
            ViewData["Cliniche"] = await CliniceListAsync();
            ViewData["Managers"] = await ManagerCandidatesAsync(excludeId: id);
            return View("Form", model);
        }
        var existing = await Load(id);
        if (existing is null) return NotFound();
        model.TenantId = existing.TenantId;
        model.CreatedAt = existing.CreatedAt;
        model.UpdatedAt = DateTime.UtcNow;
        await _mongo.Dipendenti.ReplaceOneAsync(d => d.Id == id && d.TenantId == _tenant.TenantId, model);

        await _audit.LogDiffAsync(existing, model, "Dipendente", model.NomeCompleto,
            AuditAction.Updated, User, ignoreFields: nameof(Dipendente.UpdatedAt));

        if (existing.ClinicaId != model.ClinicaId)
        {
            var cliniche = await CliniceListAsync();
            var fromC = cliniche.FirstOrDefault(c => c.Id == existing.ClinicaId);
            var toC = cliniche.FirstOrDefault(c => c.Id == model.ClinicaId);
            await CreateTransferAsync(model.Id, TipoPersona.Dipendente, model.NomeCompleto,
                fromC?.Id, fromC?.Nome, toC?.Id ?? "", toC?.Nome ?? "",
                MotivoTrasferimento.Riorganizzazione, "Aggiornamento sede", DateTime.Today);
        }

        TempData["flash"] = $"Dipendente «{model.NomeCompleto}» aggiornato.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id}/elimina")]
    [Authorize(Policy = Policies.RequireManagement)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var existing = await Load(id);
        if (existing is null) return NotFound();
        await _mongo.Dipendenti.DeleteOneAsync(d => d.Id == id && d.TenantId == _tenant.TenantId);
        await _audit.LogAsync("Dipendente", id, existing.NomeCompleto, AuditAction.Deleted, actor: User);
        TempData["flash"] = "Dipendente eliminato.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/trasferisci")]
    [Authorize(Policy = Policies.RequireDirettore)]
    public async Task<IActionResult> Transfer(string id)
    {
        var d = await Load(id);
        if (d is null) return NotFound();
        var cliniche = await CliniceListAsync();
        var current = cliniche.FirstOrDefault(c => c.Id == d.ClinicaId);
        ViewData["Section"] = "dipendenti";
        return View("Transfer", new TransferViewModel
        {
            PersonaId = d.Id,
            PersonaTipo = TipoPersona.Dipendente,
            PersonaNome = d.NomeCompleto,
            ClinicaAttualeId = current?.Id,
            ClinicaAttualeNome = current?.Nome,
            Cliniche = cliniche
        });
    }

    [HttpPost("{id}/trasferisci")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Transfer(string id, TransferViewModel vm)
    {
        var d = await Load(id);
        if (d is null) return NotFound();
        if (!ModelState.IsValid)
        {
            vm.PersonaNome = d.NomeCompleto;
            vm.Cliniche = await CliniceListAsync();
            ViewData["Section"] = "dipendenti";
            return View("Transfer", vm);
        }
        if (vm.ClinicaAId == d.ClinicaId)
        {
            ModelState.AddModelError(nameof(vm.ClinicaAId), "Il dipendente è già assegnato a questa sede.");
            vm.PersonaNome = d.NomeCompleto;
            vm.Cliniche = await CliniceListAsync();
            ViewData["Section"] = "dipendenti";
            return View("Transfer", vm);
        }

        var cliniche = await CliniceListAsync();
        var fromC = cliniche.FirstOrDefault(c => c.Id == d.ClinicaId);
        var toC = cliniche.FirstOrDefault(c => c.Id == vm.ClinicaAId);

        await _mongo.Dipendenti.UpdateOneAsync(
            x => x.Id == id && x.TenantId == _tenant.TenantId,
            Builders<Dipendente>.Update
                .Set(x => x.ClinicaId, vm.ClinicaAId)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        await CreateTransferAsync(id, TipoPersona.Dipendente, d.NomeCompleto,
            fromC?.Id, fromC?.Nome, toC?.Id ?? "", toC?.Nome ?? "",
            vm.Motivo, vm.Note, vm.DataEffetto);

        await _audit.LogAsync("Dipendente", id, d.NomeCompleto, AuditAction.Transferred,
            new[] { new FieldChange { Field = "ClinicaId",
                                       OldValue = fromC?.Nome ?? "—",
                                       NewValue = toC?.Nome ?? "—" } },
            note: vm.Note, actor: User);

        TempData["flash"] = $"{d.NomeCompleto} trasferito a {toC?.Nome}.";
        return RedirectToAction(nameof(Details), new { id, tab = "storico" });
    }

    [HttpGet("{id}/dimetti")]
    [Authorize(Policy = Policies.RequireDirettore)]
    public async Task<IActionResult> Dismiss(string id)
    {
        var d = await Load(id);
        if (d is null) return NotFound();
        ViewData["Section"] = "dipendenti";
        return View("Dismiss", new DismissViewModel
        {
            PersonaId = d.Id,
            PersonaTipo = TipoPersona.Dipendente,
            PersonaNome = d.NomeCompleto
        });
    }

    [HttpPost("{id}/dimetti")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dismiss(string id, DismissViewModel vm)
    {
        var d = await Load(id);
        if (d is null) return NotFound();
        if (!ModelState.IsValid)
        {
            vm.PersonaNome = d.NomeCompleto;
            ViewData["Section"] = "dipendenti";
            return View("Dismiss", vm);
        }
        await _mongo.Dipendenti.UpdateOneAsync(
            x => x.Id == id && x.TenantId == _tenant.TenantId,
            Builders<Dipendente>.Update
                .Set(x => x.Stato, StatoDipendente.Cessato)
                .Set(x => x.DataDimissioni, vm.DataDimissioni)
                .Set(x => x.MotivoDimissioni, vm.Motivo)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        await _audit.LogAsync("Dipendente", id, d.NomeCompleto, AuditAction.Dismissed,
            new[] { new FieldChange { Field = "DataDimissioni", OldValue = "—", NewValue = vm.DataDimissioni.ToString("yyyy-MM-dd") } },
            note: vm.Motivo, actor: User);

        TempData["flash"] = $"{d.NomeCompleto} dimesso il {vm.DataDimissioni:dd/MM/yyyy}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id}/riattiva")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivate(string id)
    {
        var d = await Load(id);
        if (d is null) return NotFound();
        await _mongo.Dipendenti.UpdateOneAsync(
            x => x.Id == id && x.TenantId == _tenant.TenantId,
            Builders<Dipendente>.Update
                .Set(x => x.Stato, StatoDipendente.Attivo)
                .Set(x => x.DataDimissioni, (DateTime?)null)
                .Set(x => x.MotivoDimissioni, (string?)null)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));
        await _audit.LogAsync("Dipendente", id, d.NomeCompleto, AuditAction.Reactivated, actor: User);
        TempData["flash"] = $"{d.NomeCompleto} riattivato.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task CreateTransferAsync(string personaId, TipoPersona tipo, string personaNome,
                                           string? fromId, string? fromName,
                                           string toId, string toName,
                                           MotivoTrasferimento motivo, string? note, DateTime data)
    {
        var trasferimento = new Trasferimento
        {
            TenantId = _tenant.TenantId!,
            PersonaId = personaId,
            PersonaTipo = tipo,
            PersonaNome = personaNome,
            ClinicaDaId = fromId,
            ClinicaDaNome = fromName,
            ClinicaAId = toId,
            ClinicaANome = toName,
            DataEffetto = data,
            Motivo = motivo,
            Note = note,
            DecisoDaUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty,
            DecisoDaNome = User.Identity?.Name ?? "system"
        };
        await _mongo.Trasferimenti.InsertOneAsync(trasferimento);
    }

    private async Task<Dipendente?> Load(string id)
        => await _mongo.Dipendenti
            .Find(d => d.Id == id && d.TenantId == _tenant.TenantId)
            .FirstOrDefaultAsync();

    private Task<List<Clinica>> CliniceListAsync()
        => _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId).SortBy(c => c.Nome).ToListAsync();

    private async Task<Dictionary<string, string>> CliniceLookupAsync()
        => (await CliniceListAsync()).ToDictionary(c => c.Id, c => c.Nome);

    private async Task<List<Dipendente>> ManagerCandidatesAsync(string? excludeId = null)
        => await _mongo.Dipendenti
            .Find(d => d.TenantId == _tenant.TenantId
                       && d.Stato != StatoDipendente.Cessato
                       && (excludeId == null || d.Id != excludeId))
            .SortBy(d => d.Cognome).ToListAsync();
}
