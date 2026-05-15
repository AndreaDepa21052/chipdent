using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Audit;
using Chipdent.Web.Infrastructure.Compliance;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Sepa;
using Chipdent.Web.Infrastructure.Storage;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize(Policy = Policies.RequireBackoffice)]
[Route("dottori")]
public class DottoriController : Controller
{
    private const long MaxAllegatoBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllegatoEstensioniAmmesse = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".png", ".jpg", ".jpeg", ".doc", ".docx" };

    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;
    private readonly FornitoreOmbraService _ombra;
    private readonly IFileStorage _storage;

    public DottoriController(MongoContext mongo, ITenantContext tenant, IAuditService audit, FornitoreOmbraService ombra, IFileStorage storage)
    {
        _mongo = mongo;
        _tenant = tenant;
        _audit = audit;
        _ombra = ombra;
        _storage = storage;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var dottori = await _mongo.Dottori
            .Find(d => d.TenantId == _tenant.TenantId)
            .SortBy(d => d.Cognome)
            .ToListAsync();

        var collaborazioni = await _mongo.CollaborazioniDottori
            .Find(c => c.TenantId == _tenant.TenantId)
            .ToListAsync();
        var collabByDottore = collaborazioni.GroupBy(c => c.DottoreId).ToDictionary(g => g.Key, g => (IReadOnlyList<CollaborazioneClinica>)g.ToList());

        var documenti = await _mongo.DocumentiDottore
            .Find(d => d.TenantId == _tenant.TenantId)
            .ToListAsync();
        var documentiByDottore = documenti.GroupBy(d => d.DottoreId).ToDictionary(g => g.Key, g => g.ToList());

        var attestati = await _mongo.AttestatiEcm
            .Find(a => a.TenantId == _tenant.TenantId)
            .ToListAsync();
        var attestatiByDottore = attestati.GroupBy(a => a.DottoreId).ToDictionary(g => g.Key, g => g.ToList());

        var oggi = DateTime.UtcNow;
        var items = dottori.Select(d =>
        {
            var docs = documentiByDottore.GetValueOrDefault(d.Id) ?? new List<DocumentoDottore>();
            var atts = attestatiByDottore.GetValueOrDefault(d.Id) ?? new List<AttestatoEcm>();
            var alerts = DottoreAlertCalculator.Calcola(d, docs, atts, oggi);
            return new DottoreListItem
            {
                Dottore = d,
                Collaborazioni = collabByDottore.GetValueOrDefault(d.Id) ?? Array.Empty<CollaborazioneClinica>(),
                AlertCritici = alerts.Count(a => a.Livello == AlertLivello.Critico),
                AlertAvvisi = alerts.Count(a => a.Livello == AlertLivello.Avviso)
            };
        }).ToList();

        ViewData["Section"] = "dottori";
        return View(items);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Details(string id, string tab = "anagrafica")
    {
        var d = await Load(id);
        if (d is null) return NotFound();

        var cliniche = await CliniceListAsync();
        var clinicaNome = d.ClinicaPrincipaleId is null
            ? null
            : cliniche.FirstOrDefault(c => c.Id == d.ClinicaPrincipaleId)?.Nome;

        var storico = await _mongo.Trasferimenti
            .Find(t => t.TenantId == _tenant.TenantId && t.PersonaId == id && t.PersonaTipo == TipoPersona.Dottore)
            .SortByDescending(t => t.DataEffetto)
            .ToListAsync();

        var audit = await _mongo.Audit
            .Find(a => a.TenantId == _tenant.TenantId && a.EntityType == "Dottore" && a.EntityId == id)
            .SortByDescending(a => a.CreatedAt)
            .Limit(50)
            .ToListAsync();

        // Snapshot Tesoreria: se il dottore ha un Fornitore-ombra (collaborazione/lib. prof.)
        // mostriamo un mini-riepilogo + link rapido alla scheda Tesoreria.
        DottoreTesoreriaSnapshot? tesoreria = null;
        var fornitoreOmbra = await _mongo.Fornitori
            .Find(f => f.TenantId == _tenant.TenantId && f.DottoreId == id)
            .FirstOrDefaultAsync();
        if (fornitoreOmbra is not null)
        {
            var oggi = DateTime.UtcNow.Date;
            var anno = oggi.Year;
            var scadenze = await _mongo.ScadenzePagamento
                .Find(s => s.TenantId == _tenant.TenantId && s.FornitoreId == fornitoreOmbra.Id)
                .ToListAsync();
            var fattureAnno = await _mongo.Fatture
                .Find(f => f.TenantId == _tenant.TenantId && f.FornitoreId == fornitoreOmbra.Id && f.DataEmissione.Year == anno)
                .ToListAsync();

            var aperte = scadenze.Where(s =>
                s.Stato == StatoScadenza.DaPagare || s.Stato == StatoScadenza.Programmato).ToList();

            tesoreria = new DottoreTesoreriaSnapshot
            {
                FornitoreId = fornitoreOmbra.Id,
                EspostoAperto = aperte.Sum(s => s.Importo),
                FatturatoYTD = fattureAnno.Where(f => f.Stato != StatoFattura.Rifiutata).Sum(f => f.Totale),
                FattureInApprovazione = fattureAnno.Count(f => f.Stato == StatoFattura.Caricata),
                ScadenzeAperte = aperte.Count,
                ScadenzeScadute = aperte.Count(s => s.Stato == StatoScadenza.DaPagare && s.DataScadenza < oggi)
            };
        }

        var collaborazioni = await _mongo.CollaborazioniDottori
            .Find(c => c.TenantId == _tenant.TenantId && c.DottoreId == id)
            .SortByDescending(c => c.DataInizio)
            .ToListAsync();

        var documenti = await _mongo.DocumentiDottore
            .Find(x => x.TenantId == _tenant.TenantId && x.DottoreId == id)
            .SortBy(x => x.Tipo).ThenBy(x => x.Scadenza)
            .ToListAsync();

        var attestatiEcm = await _mongo.AttestatiEcm
            .Find(a => a.TenantId == _tenant.TenantId && a.DottoreId == id)
            .SortByDescending(a => a.DataConseguimento)
            .ToListAsync();

        var alerts = DottoreAlertCalculator.Calcola(d, documenti, attestatiEcm, DateTime.UtcNow);

        ViewData["Section"] = "dottori";
        return View("Profile", new DottoreProfileViewModel
        {
            Dottore = d,
            ClinicaPrincipaleNome = clinicaNome,
            Storico = storico,
            Audit = audit,
            Cliniche = cliniche,
            Tab = tab,
            Tesoreria = tesoreria,
            Collaborazioni = collaborazioni,
            Documenti = documenti,
            AttestatiEcm = attestatiEcm,
            Alerts = alerts
        });
    }

    [HttpGet("nuovo")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    public async Task<IActionResult> Create()
    {
        ViewData["Section"] = "dottori";
        ViewData["IsNew"] = true;
        ViewData["Cliniche"] = await CliniceListAsync();
        return View("Form", new Dottore { DataAssunzione = DateTime.Today });
    }

    [HttpPost("nuovo")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Dottore model)
    {
        ModelState.Remove(nameof(Dottore.NumeroAlbo));
        ModelState.Remove(nameof(Dottore.Specializzazione));
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "dottori";
            ViewData["IsNew"] = true;
            ViewData["Cliniche"] = await CliniceListAsync();
            return View("Form", model);
        }
        model.TenantId = _tenant.TenantId!;
        model.CreatedAt = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(model.Codice))
        {
            model.Codice = await GenerateCodiceDottoreAsync();
        }
        await _mongo.Dottori.InsertOneAsync(model);

        await _audit.LogAsync("Dottore", model.Id, model.NomeCompleto, AuditAction.Created, actor: User);
        await _ombra.EnsureForDottoreAsync(model);

        if (!string.IsNullOrEmpty(model.ClinicaPrincipaleId))
        {
            var cliniche = await CliniceListAsync();
            var clinica = cliniche.FirstOrDefault(c => c.Id == model.ClinicaPrincipaleId);
            await CreateTransferAsync(model.Id, TipoPersona.Dottore, model.NomeCompleto,
                null, null, clinica?.Id ?? "", clinica?.Nome ?? "",
                MotivoTrasferimento.Riorganizzazione, $"Assegnazione iniziale", model.DataAssunzione);
        }

        TempData["flash"] = $"Dottore «{model.NomeCompleto}» creato.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/modifica")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    public async Task<IActionResult> Edit(string id)
    {
        var d = await Load(id);
        if (d is null) return NotFound();
        ViewData["Section"] = "dottori";
        ViewData["IsNew"] = false;
        ViewData["Cliniche"] = await CliniceListAsync();
        return View("Form", d);
    }

    /// <summary>Restituisce il partial della modale di modifica rapida con dati e alert.</summary>
    [HttpGet("{id}/edit-modal")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    public async Task<IActionResult> EditModal(string id)
    {
        var d = await Load(id);
        if (d is null) return NotFound();

        var documenti = await _mongo.DocumentiDottore
            .Find(x => x.TenantId == _tenant.TenantId && x.DottoreId == id).ToListAsync();
        var attestati = await _mongo.AttestatiEcm
            .Find(a => a.TenantId == _tenant.TenantId && a.DottoreId == id).ToListAsync();
        var alerts = DottoreAlertCalculator.Calcola(d, documenti, attestati, DateTime.UtcNow);
        var cliniche = await CliniceListAsync();

        return PartialView("_EditModal", new DottoreEditModalViewModel
        {
            Dottore = d,
            Cliniche = cliniche,
            Alerts = alerts
        });
    }

    [HttpPost("{id}/modifica")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, Dottore model, [FromHeader(Name = "X-Edit-Modal")] string? modal)
    {
        if (id != model.Id) return BadRequest();
        var isModal = modal == "1";
        ModelState.Remove(nameof(Dottore.NumeroAlbo));
        ModelState.Remove(nameof(Dottore.Specializzazione));
        if (!ModelState.IsValid)
        {
            if (isModal)
            {
                var errors = ModelState
                    .Where(e => e.Value!.Errors.Count > 0)
                    .ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray());
                return BadRequest(new { errors });
            }
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

        await _audit.LogDiffAsync(existing, model, "Dottore", model.NomeCompleto,
            AuditAction.Updated, User, ignoreFields: nameof(Dottore.UpdatedAt));
        await _ombra.EnsureForDottoreAsync(model);

        if (existing.ClinicaPrincipaleId != model.ClinicaPrincipaleId)
        {
            var cliniche = await CliniceListAsync();
            var fromC = cliniche.FirstOrDefault(c => c.Id == existing.ClinicaPrincipaleId);
            var toC = cliniche.FirstOrDefault(c => c.Id == model.ClinicaPrincipaleId);
            await CreateTransferAsync(model.Id, TipoPersona.Dottore, model.NomeCompleto,
                fromC?.Id, fromC?.Nome, toC?.Id ?? "", toC?.Nome ?? "",
                MotivoTrasferimento.Riorganizzazione, "Aggiornamento sede principale", DateTime.Today);
        }

        if (isModal) return Json(new { ok = true, name = model.NomeCompleto });

        TempData["flash"] = $"Dottore «{model.NomeCompleto}» aggiornato.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id}/elimina")]
    [Authorize(Policy = Policies.RequireManagement)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var existing = await Load(id);
        if (existing is null) return NotFound();
        await _mongo.Dottori.DeleteOneAsync(d => d.Id == id && d.TenantId == _tenant.TenantId);
        await _audit.LogAsync("Dottore", id, existing.NomeCompleto, AuditAction.Deleted, actor: User);
        TempData["flash"] = "Dottore eliminato.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/trasferisci")]
    [Authorize(Policy = Policies.RequireDirettore)]
    public async Task<IActionResult> Transfer(string id)
    {
        var d = await Load(id);
        if (d is null) return NotFound();
        var cliniche = await CliniceListAsync();
        var current = cliniche.FirstOrDefault(c => c.Id == d.ClinicaPrincipaleId);
        ViewData["Section"] = "dottori";
        return View("Transfer", new TransferViewModel
        {
            PersonaId = d.Id,
            PersonaTipo = TipoPersona.Dottore,
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
            ViewData["Section"] = "dottori";
            return View("Transfer", vm);
        }
        if (vm.ClinicaAId == d.ClinicaPrincipaleId)
        {
            ModelState.AddModelError(nameof(vm.ClinicaAId), "Il dottore è già assegnato a questa sede.");
            vm.PersonaNome = d.NomeCompleto;
            vm.Cliniche = await CliniceListAsync();
            ViewData["Section"] = "dottori";
            return View("Transfer", vm);
        }

        var cliniche = await CliniceListAsync();
        var fromC = cliniche.FirstOrDefault(c => c.Id == d.ClinicaPrincipaleId);
        var toC = cliniche.FirstOrDefault(c => c.Id == vm.ClinicaAId);

        await _mongo.Dottori.UpdateOneAsync(
            x => x.Id == id && x.TenantId == _tenant.TenantId,
            Builders<Dottore>.Update
                .Set(x => x.ClinicaPrincipaleId, vm.ClinicaAId)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        await CreateTransferAsync(id, TipoPersona.Dottore, d.NomeCompleto,
            fromC?.Id, fromC?.Nome, toC?.Id ?? "", toC?.Nome ?? "",
            vm.Motivo, vm.Note, vm.DataEffetto);

        await _audit.LogAsync("Dottore", id, d.NomeCompleto, AuditAction.Transferred,
            new[] { new FieldChange { Field = "ClinicaPrincipaleId",
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
        ViewData["Section"] = "dottori";
        return View("Dismiss", new DismissViewModel
        {
            PersonaId = d.Id,
            PersonaTipo = TipoPersona.Dottore,
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
            ViewData["Section"] = "dottori";
            return View("Dismiss", vm);
        }
        await _mongo.Dottori.UpdateOneAsync(
            x => x.Id == id && x.TenantId == _tenant.TenantId,
            Builders<Dottore>.Update
                .Set(x => x.Attivo, false)
                .Set(x => x.DataDimissioni, vm.DataDimissioni)
                .Set(x => x.MotivoDimissioni, vm.Motivo)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        await _audit.LogAsync("Dottore", id, d.NomeCompleto, AuditAction.Dismissed,
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
        await _mongo.Dottori.UpdateOneAsync(
            x => x.Id == id && x.TenantId == _tenant.TenantId,
            Builders<Dottore>.Update
                .Set(x => x.Attivo, true)
                .Set(x => x.DataDimissioni, (DateTime?)null)
                .Set(x => x.MotivoDimissioni, (string?)null)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));
        await _audit.LogAsync("Dottore", id, d.NomeCompleto, AuditAction.Reactivated, actor: User);
        TempData["flash"] = $"{d.NomeCompleto} riattivato.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ─────────────────────────────────────────────────────────────
    //  COLLABORAZIONI con cliniche
    // ─────────────────────────────────────────────────────────────
    [HttpPost("{id}/collaborazioni/upsert")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpsertCollaborazione(string id, string? collabId,
        string clinicaId, DateTime dataInizio, DateTime? dataFine, string? ruolo, string? note)
    {
        var d = await Load(id);
        if (d is null) return NotFound();
        if (string.IsNullOrWhiteSpace(clinicaId))
        {
            TempData["flash"] = "Seleziona una clinica.";
            return RedirectToAction(nameof(Details), new { id, tab = "cliniche" });
        }

        var cliniche = await CliniceListAsync();
        var clinica = cliniche.FirstOrDefault(c => c.Id == clinicaId);
        if (clinica is null) return BadRequest();

        if (!string.IsNullOrEmpty(collabId))
        {
            var update = Builders<CollaborazioneClinica>.Update
                .Set(x => x.ClinicaId, clinica.Id)
                .Set(x => x.ClinicaNome, clinica.Nome)
                .Set(x => x.DataInizio, DateTime.SpecifyKind(dataInizio.Date, DateTimeKind.Utc))
                .Set(x => x.DataFine, dataFine.HasValue ? DateTime.SpecifyKind(dataFine.Value.Date, DateTimeKind.Utc) : null)
                .Set(x => x.Ruolo, ruolo)
                .Set(x => x.Note, note)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);
            await _mongo.CollaborazioniDottori.UpdateOneAsync(
                c => c.Id == collabId && c.TenantId == _tenant.TenantId && c.DottoreId == id, update);
            TempData["flash"] = "Collaborazione aggiornata.";
        }
        else
        {
            var collab = new CollaborazioneClinica
            {
                TenantId = _tenant.TenantId!,
                DottoreId = id,
                ClinicaId = clinica.Id,
                ClinicaNome = clinica.Nome,
                DataInizio = DateTime.SpecifyKind(dataInizio.Date, DateTimeKind.Utc),
                DataFine = dataFine.HasValue ? DateTime.SpecifyKind(dataFine.Value.Date, DateTimeKind.Utc) : null,
                Ruolo = ruolo,
                Note = note
            };
            await _mongo.CollaborazioniDottori.InsertOneAsync(collab);
            TempData["flash"] = "Collaborazione registrata.";
        }

        await _audit.LogAsync("Dottore", id, d.NomeCompleto, AuditAction.Updated, actor: User);
        return RedirectToAction(nameof(Details), new { id, tab = "cliniche" });
    }

    [HttpPost("{id}/collaborazioni/{collabId}/elimina")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminaCollaborazione(string id, string collabId)
    {
        await _mongo.CollaborazioniDottori.DeleteOneAsync(
            c => c.Id == collabId && c.TenantId == _tenant.TenantId && c.DottoreId == id);
        TempData["flash"] = "Collaborazione rimossa.";
        return RedirectToAction(nameof(Details), new { id, tab = "cliniche" });
    }

    // ─────────────────────────────────────────────────────────────
    //  DOCUMENTI fascicolo dottore (RC professionale e altri)
    // ─────────────────────────────────────────────────────────────
    [HttpPost("{id}/documenti/upsert")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxAllegatoBytes)]
    public async Task<IActionResult> UpsertDocumento(string id, string? docId, TipoDocumentoDottore tipo,
        string? etichettaLibera, string? numeroDocumento, string? compagnia,
        DateTime? dataEmissione, DateTime? scadenza, string? note, IFormFile? allegato)
    {
        var d = await Load(id);
        if (d is null) return NotFound();

        var doc = !string.IsNullOrEmpty(docId)
            ? await _mongo.DocumentiDottore.Find(x => x.Id == docId && x.TenantId == _tenant.TenantId && x.DottoreId == id).FirstOrDefaultAsync()
            : null;

        var isNew = doc is null;
        doc ??= new DocumentoDottore
        {
            TenantId = _tenant.TenantId!,
            DottoreId = id
        };

        doc.Tipo = tipo;
        doc.EtichettaLibera = etichettaLibera;
        doc.NumeroDocumento = numeroDocumento;
        doc.Compagnia = compagnia;
        doc.DataEmissione = dataEmissione.HasValue ? DateTime.SpecifyKind(dataEmissione.Value.Date, DateTimeKind.Utc) : null;
        doc.Scadenza = scadenza.HasValue ? DateTime.SpecifyKind(scadenza.Value.Date, DateTimeKind.Utc) : null;
        doc.Note = note;
        doc.UpdatedAt = DateTime.UtcNow;

        if (allegato is { Length: > 0 })
        {
            var err = await TryAttachAsync(allegato, "documenti-dottore",
                (path, name, size) => { doc.AllegatoNome = name; doc.AllegatoPath = path; doc.AllegatoSize = size; });
            if (err is not null)
            {
                TempData["flash"] = err;
                return RedirectToAction(nameof(Details), new { id, tab = "documenti" });
            }
        }

        if (isNew)
            await _mongo.DocumentiDottore.InsertOneAsync(doc);
        else
            await _mongo.DocumentiDottore.ReplaceOneAsync(x => x.Id == doc.Id, doc);

        await _audit.LogAsync("Dottore", id, d.NomeCompleto, AuditAction.Updated, actor: User);
        TempData["flash"] = "Documento aggiornato.";
        return RedirectToAction(nameof(Details), new { id, tab = "documenti" });
    }

    [HttpPost("{id}/documenti/{docId}/elimina")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminaDocumento(string id, string docId)
    {
        var doc = await _mongo.DocumentiDottore
            .Find(x => x.Id == docId && x.TenantId == _tenant.TenantId && x.DottoreId == id)
            .FirstOrDefaultAsync();
        if (doc is not null && !string.IsNullOrEmpty(doc.AllegatoPath))
        {
            await _storage.DeleteAsync(_tenant.TenantId!, doc.AllegatoPath);
        }
        await _mongo.DocumentiDottore.DeleteOneAsync(x => x.Id == docId && x.TenantId == _tenant.TenantId && x.DottoreId == id);
        TempData["flash"] = "Documento rimosso.";
        return RedirectToAction(nameof(Details), new { id, tab = "documenti" });
    }

    // ─────────────────────────────────────────────────────────────
    //  ECM — attestati e crediti
    // ─────────────────────────────────────────────────────────────
    [HttpPost("{id}/ecm/upsert")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxAllegatoBytes)]
    public async Task<IActionResult> UpsertEcm(string id, string? ecmId,
        string titoloEvento, string? provider, DateTime dataConseguimento,
        decimal creditiEcm, int annoRiferimento, ModalitaEcm modalita,
        string? note, IFormFile? allegato)
    {
        var d = await Load(id);
        if (d is null) return NotFound();

        if (string.IsNullOrWhiteSpace(titoloEvento))
        {
            TempData["flash"] = "Indica il titolo dell'evento ECM.";
            return RedirectToAction(nameof(Details), new { id, tab = "ecm" });
        }

        var att = !string.IsNullOrEmpty(ecmId)
            ? await _mongo.AttestatiEcm.Find(x => x.Id == ecmId && x.TenantId == _tenant.TenantId && x.DottoreId == id).FirstOrDefaultAsync()
            : null;

        var isNew = att is null;
        att ??= new AttestatoEcm
        {
            TenantId = _tenant.TenantId!,
            DottoreId = id
        };

        att.TitoloEvento = titoloEvento.Trim();
        att.Provider = provider;
        att.DataConseguimento = DateTime.SpecifyKind(dataConseguimento.Date, DateTimeKind.Utc);
        att.CreditiEcm = creditiEcm;
        att.AnnoRiferimento = annoRiferimento;
        att.Modalita = modalita;
        att.Note = note;
        att.UpdatedAt = DateTime.UtcNow;

        if (allegato is { Length: > 0 })
        {
            var err = await TryAttachAsync(allegato, "ecm",
                (path, name, size) => { att.AllegatoNome = name; att.AllegatoPath = path; att.AllegatoSize = size; });
            if (err is not null)
            {
                TempData["flash"] = err;
                return RedirectToAction(nameof(Details), new { id, tab = "ecm" });
            }
        }

        if (isNew)
            await _mongo.AttestatiEcm.InsertOneAsync(att);
        else
            await _mongo.AttestatiEcm.ReplaceOneAsync(x => x.Id == att.Id, att);

        // Tieni allineato il totale crediti triennio sul Dottore (somma anni N-2..N attorno alla data più recente).
        await SincronizzaCreditiEcmAsync(id);

        await _audit.LogAsync("Dottore", id, d.NomeCompleto, AuditAction.Updated, actor: User);
        TempData["flash"] = "Attestato ECM salvato.";
        return RedirectToAction(nameof(Details), new { id, tab = "ecm" });
    }

    [HttpPost("{id}/ecm/{ecmId}/elimina")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminaEcm(string id, string ecmId)
    {
        var att = await _mongo.AttestatiEcm
            .Find(x => x.Id == ecmId && x.TenantId == _tenant.TenantId && x.DottoreId == id)
            .FirstOrDefaultAsync();
        if (att is not null && !string.IsNullOrEmpty(att.AllegatoPath))
        {
            await _storage.DeleteAsync(_tenant.TenantId!, att.AllegatoPath);
        }
        await _mongo.AttestatiEcm.DeleteOneAsync(x => x.Id == ecmId && x.TenantId == _tenant.TenantId && x.DottoreId == id);
        await SincronizzaCreditiEcmAsync(id);
        TempData["flash"] = "Attestato ECM rimosso.";
        return RedirectToAction(nameof(Details), new { id, tab = "ecm" });
    }

    private async Task SincronizzaCreditiEcmAsync(string dottoreId)
    {
        var dottore = await Load(dottoreId);
        if (dottore is null) return;
        if (dottore.AnnoFineTriennioEcm is not int annoFine || annoFine <= 0) return;
        var annoInizio = annoFine - 2;
        var attestati = await _mongo.AttestatiEcm
            .Find(a => a.TenantId == _tenant.TenantId && a.DottoreId == dottoreId
                       && a.AnnoRiferimento >= annoInizio && a.AnnoRiferimento <= annoFine)
            .ToListAsync();
        var totale = (int)Math.Round(attestati.Sum(a => a.CreditiEcm));
        await _mongo.Dottori.UpdateOneAsync(
            d => d.Id == dottoreId && d.TenantId == _tenant.TenantId,
            Builders<Dottore>.Update
                .Set(d => d.CreditiEcmTriennio, totale)
                .Set(d => d.UpdatedAt, DateTime.UtcNow));
    }

    private async Task<string?> TryAttachAsync(IFormFile file, string folder, Action<string, string, long> apply)
    {
        if (file.Length > MaxAllegatoBytes) return $"File troppo grande (max {MaxAllegatoBytes / (1024 * 1024)}MB).";
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllegatoEstensioniAmmesse.Contains(ext)) return $"Estensione non consentita: {ext}";

        await using var stream = file.OpenReadStream();
        var stored = await _storage.SaveAsync(_tenant.TenantId!, folder, file.FileName, stream, file.ContentType);
        apply(stored.RelativePath, file.FileName, stored.SizeBytes);
        return null;
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

    private async Task<Dottore?> Load(string id)
        => await _mongo.Dottori
            .Find(d => d.Id == id && d.TenantId == _tenant.TenantId)
            .FirstOrDefaultAsync();

    private Task<List<Clinica>> CliniceListAsync()
        => _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId).SortBy(c => c.Nome).ToListAsync();

    private async Task<Dictionary<string, string>> CliniceLookupAsync()
        => (await CliniceListAsync()).ToDictionary(c => c.Id, c => c.Nome);

    /// <summary>Genera il prossimo codice dottore disponibile nel formato D####.</summary>
    private async Task<string> GenerateCodiceDottoreAsync()
    {
        var tid = _tenant.TenantId!;
        var esistenti = await _mongo.Dottori
            .Find(d => d.TenantId == tid && d.Codice != null && d.Codice != "")
            .Project(d => d.Codice)
            .ToListAsync();
        var maxNum = 0;
        foreach (var c in esistenti)
        {
            if (string.IsNullOrEmpty(c) || c[0] != 'D') continue;
            if (int.TryParse(c.AsSpan(1), out var n) && n > maxNum) maxNum = n;
        }
        return $"D{maxNum + 1:D4}";
    }
}
