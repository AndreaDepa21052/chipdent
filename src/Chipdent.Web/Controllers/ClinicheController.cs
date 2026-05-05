using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Rls;
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

        // Alert dal Calendario interventi per ogni clinica: scaduti + in scadenza ≤ 30 gg.
        // Pre-computati qui per evitare N query dalla view.
        var oggi = DateTime.UtcNow.Date;
        var soglia30 = oggi.AddDays(30);
        var interventiCriticI = await _mongo.InterventiClinica
            .Find(i => i.TenantId == _tenant.TenantId && i.ProssimaScadenza != null && i.ProssimaScadenza <= soglia30)
            .ToListAsync();
        var alertMap = interventiCriticI
            .GroupBy(i => i.ClinicaId)
            .ToDictionary(g => g.Key, g => new ClinicaAlerts
            {
                Scaduti = g.Count(i => i.ProssimaScadenza!.Value.Date < oggi),
                Imminenti = g.Count(i => i.ProssimaScadenza!.Value.Date >= oggi && i.ProssimaScadenza.Value.Date <= soglia30)
            });

        ViewData["Section"] = "cliniche";
        ViewData["ViewMode"] = view == "mappa" ? "mappa" : "lista";
        ViewData["Alerts"] = alertMap;
        return View(items);
    }

    public class ClinicaAlerts
    {
        public int Scaduti { get; set; }
        public int Imminenti { get; set; }
        public int Totale => Scaduti + Imminenti;
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

        var rentri = await _mongo.Rentri
            .Find(r => r.TenantId == _tenant.TenantId && r.ClinicaId == id)
            .FirstOrDefaultAsync();

        var protocolli = await _mongo.ProtocolliClinica
            .Find(p => p.TenantId == _tenant.TenantId && p.ClinicaId == id)
            .SortByDescending(p => p.DataAdozione).ToListAsync();

        var interventi = await _mongo.InterventiClinica
            .Find(i => i.TenantId == _tenant.TenantId && i.ClinicaId == id)
            .ToListAsync();

        // ── RLS / sicurezza per la sede ─────────────────────────────
        var dipendentiIds = dipendenti.Select(d => d.Id).ToHashSet();
        var dottoriIds = dottori.Select(d => d.Id).ToHashSet();
        var corsi = await _mongo.Corsi
            .Find(c => c.TenantId == _tenant.TenantId
                && ((c.DestinatarioTipo == DestinatarioCorso.Dipendente && dipendentiIds.Contains(c.DestinatarioId))
                    || (c.DestinatarioTipo == DestinatarioCorso.Dottore && dottoriIds.Contains(c.DestinatarioId))
                    || (c.DestinatarioTipo == DestinatarioCorso.Clinica && c.DestinatarioId == id)))
            .ToListAsync();
        var visiteSede = await _mongo.VisiteMediche
            .Find(v => v.TenantId == _tenant.TenantId && dipendentiIds.Contains(v.DipendenteId))
            .ToListAsync();
        var dvrSede = await _mongo.DVRs
            .Find(d => d.TenantId == _tenant.TenantId && d.ClinicaId == id)
            .SortByDescending(d => d.DataApprovazione)
            .ToListAsync();

        var allCliniche = await _mongo.Cliniche
            .Find(c => c.TenantId == _tenant.TenantId).ToListAsync();
        var now = DateTime.UtcNow;
        var soon = now.AddMonths(3);
        var nomineSede = RlsAggregator.Nomine(
            corsi,
            dipendenti.ToDictionary(d => d.Id),
            dottori.ToDictionary(d => d.Id),
            allCliniche.ToDictionary(c => c.Id),
            now, soon, clinicaFilter: id);
        var corsiSede = RlsAggregator.CorsiInScadenzaPerTipo(
            corsi,
            dipendenti.ToDictionary(d => d.Id),
            dottori.ToDictionary(d => d.Id),
            allCliniche.ToDictionary(c => c.Id),
            now, soon, clinicaFilter: id);

        ViewData["Section"] = "cliniche";
        ViewData["Dottori"] = dottori;
        ViewData["Dipendenti"] = dipendenti;
        ViewData["Rentri"] = rentri;
        ViewData["Protocolli"] = protocolli;
        ViewData["Interventi"] = interventi;
        ViewData["NomineRls"] = nomineSede;
        ViewData["CorsiRls"] = corsiSede;
        ViewData["VisiteRls"] = visiteSede;
        ViewData["DvrRls"] = dvrSede;
        return View(clinica);
    }

    // ─────────────────────────────────────────────────────────────
    //  RENTRI: una iscrizione per clinica (upsert)
    // ─────────────────────────────────────────────────────────────
    [HttpPost("{id}/rentri")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SalvaRentri(string id, DateTime? dataAttivazione, string? username, string? password, string? numeroIscrizione, string? note)
    {
        var existing = await _mongo.Rentri.Find(r => r.TenantId == _tenant.TenantId && r.ClinicaId == id).FirstOrDefaultAsync();
        if (existing is null)
        {
            await _mongo.Rentri.InsertOneAsync(new IscrizioneRentri
            {
                TenantId = _tenant.TenantId!,
                ClinicaId = id,
                DataAttivazione = dataAttivazione.HasValue ? DateTime.SpecifyKind(dataAttivazione.Value.Date, DateTimeKind.Utc) : null,
                Username = username,
                Password = password,
                NumeroIscrizione = numeroIscrizione,
                Note = note
            });
        }
        else
        {
            await _mongo.Rentri.UpdateOneAsync(r => r.Id == existing.Id,
                Builders<IscrizioneRentri>.Update
                    .Set(r => r.DataAttivazione, dataAttivazione.HasValue ? DateTime.SpecifyKind(dataAttivazione.Value.Date, DateTimeKind.Utc) : (DateTime?)null)
                    .Set(r => r.Username, username)
                    .Set(r => r.Password, password)
                    .Set(r => r.NumeroIscrizione, numeroIscrizione)
                    .Set(r => r.Note, note)
                    .Set(r => r.UpdatedAt, DateTime.UtcNow));
        }
        TempData["flash"] = "Iscrizione RENTRI salvata.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ─────────────────────────────────────────────────────────────
    //  PROTOCOLLI per clinica
    // ─────────────────────────────────────────────────────────────
    [HttpPost("{id}/protocolli/nuovo")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NuovoProtocollo(string id, TipoProtocollo tipo, DateTime? dataAdozione, DateTime? prossimaRevisione, string? versione, string? note)
    {
        await _mongo.ProtocolliClinica.InsertOneAsync(new ProtocolloClinica
        {
            TenantId = _tenant.TenantId!,
            ClinicaId = id,
            Tipo = tipo,
            Attivo = true,
            DataAdozione = dataAdozione.HasValue ? DateTime.SpecifyKind(dataAdozione.Value.Date, DateTimeKind.Utc) : null,
            ProssimaRevisione = prossimaRevisione.HasValue ? DateTime.SpecifyKind(prossimaRevisione.Value.Date, DateTimeKind.Utc) : null,
            Versione = versione,
            Note = note
        });
        TempData["flash"] = "Protocollo aggiunto.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id}/protocolli/{protocolloId}/toggle")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleProtocollo(string id, string protocolloId)
    {
        var p = await _mongo.ProtocolliClinica.Find(x => x.Id == protocolloId && x.TenantId == _tenant.TenantId && x.ClinicaId == id).FirstOrDefaultAsync();
        if (p is null) return NotFound();
        await _mongo.ProtocolliClinica.UpdateOneAsync(
            x => x.Id == protocolloId,
            Builders<ProtocolloClinica>.Update
                .Set(x => x.Attivo, !p.Attivo)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id}/protocolli/{protocolloId}/elimina")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminaProtocollo(string id, string protocolloId)
    {
        await _mongo.ProtocolliClinica.DeleteOneAsync(p => p.Id == protocolloId && p.TenantId == _tenant.TenantId && p.ClinicaId == id);
        TempData["flash"] = "Protocollo rimosso.";
        return RedirectToAction(nameof(Details), new { id });
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
    [Authorize(Policy = Policies.RequireBackoffice)]
    public async Task<IActionResult> Edit(string id)
    {
        var clinica = await Load(id);
        if (clinica is null) return NotFound();
        ViewData["Section"] = "cliniche";
        ViewData["IsNew"] = false;
        return View("Form", clinica);
    }

    [HttpPost("{id}/modifica")]
    [Authorize(Policy = Policies.RequireBackoffice)]
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
