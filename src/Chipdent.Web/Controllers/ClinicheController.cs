using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Rls;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize(Policy = Policies.RequireBackoffice)]
[Route("cliniche")]
public class ClinicheController : Controller
{
    private const long MaxAllegatoBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllegatoEstensioniAmmesse = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".png", ".jpg", ".jpeg", ".doc", ".docx" };

    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly Chipdent.Web.Infrastructure.Storage.IFileStorage _storage;

    public ClinicheController(MongoContext mongo, ITenantContext tenant, Chipdent.Web.Infrastructure.Storage.IFileStorage storage)
    {
        _mongo = mongo;
        _tenant = tenant;
        _storage = storage;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? view = null)
    {
        var items = await _mongo.Cliniche
            .Find(c => c.TenantId == _tenant.TenantId)
            .SortBy(c => c.Nome)
            .ToListAsync();

        var societa = await _mongo.Societa
            .Find(s => s.TenantId == _tenant.TenantId)
            .ToListAsync();
        var societaMap = societa.ToDictionary(s => s.Id, s => s);

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
        ViewData["SocietaMap"] = societaMap;
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

        var mysteryClient = await _mongo.MysteryClient
            .Find(m => m.TenantId == _tenant.TenantId && m.ClinicaId == id)
            .SortByDescending(m => m.DataVisita)
            .ToListAsync();

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
        ViewData["MysteryClient"] = mysteryClient;
        return View(clinica);
    }

    // ─────────────────────────────────────────────────────────────
    //  MYSTERY CLIENT: storico visite con scheda di valutazione
    // ─────────────────────────────────────────────────────────────
    [HttpPost("{id}/mystery/nuova")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxAllegatoBytes)]
    public async Task<IActionResult> NuovaVisitaMystery(string id, DateTime dataVisita, CanaleMystery canale,
        string? codiceMystery, double? punteggioComplessivo,
        int? accoglienza, int? cortesia, int? competenza, int? ambiente, int? followUp,
        string? puntiDiForza, string? areeDiMiglioramento, string? azioniCorrettive, string? note,
        IFormFile? allegato)
    {
        var clinica = await Load(id);
        if (clinica is null) return NotFound();

        var visita = new VisitaMysteryClient
        {
            TenantId = _tenant.TenantId!,
            ClinicaId = id,
            DataVisita = DateTime.SpecifyKind(dataVisita.Date, DateTimeKind.Utc),
            Canale = canale,
            CodiceMystery = codiceMystery,
            PunteggioComplessivo = punteggioComplessivo,
            PunteggioAccoglienza = accoglienza,
            PunteggioCortesia = cortesia,
            PunteggioCompetenza = competenza,
            PunteggioAmbiente = ambiente,
            PunteggioFollowUp = followUp,
            PuntiDiForza = puntiDiForza,
            AreeDiMiglioramento = areeDiMiglioramento,
            AzioniCorrettive = azioniCorrettive,
            Note = note
        };

        if (allegato is { Length: > 0 })
        {
            if (allegato.Length > MaxAllegatoBytes)
            {
                TempData["flash"] = $"File troppo grande (max {MaxAllegatoBytes / (1024 * 1024)}MB).";
                return RedirectToAction(nameof(Details), new { id, tab = "mystery" });
            }
            var ext = Path.GetExtension(allegato.FileName).ToLowerInvariant();
            if (!AllegatoEstensioniAmmesse.Contains(ext))
            {
                TempData["flash"] = $"Estensione non consentita: {ext}";
                return RedirectToAction(nameof(Details), new { id, tab = "mystery" });
            }
            await using var stream = allegato.OpenReadStream();
            var stored = await _storage.SaveAsync(_tenant.TenantId!, "mystery-client", allegato.FileName, stream, allegato.ContentType);
            visita.AllegatoNome = allegato.FileName;
            visita.AllegatoPath = stored.RelativePath;
            visita.AllegatoSize = stored.SizeBytes;
        }

        await _mongo.MysteryClient.InsertOneAsync(visita);
        TempData["flash"] = "Visita Mystery Client registrata.";
        return RedirectToAction(nameof(Details), new { id, tab = "mystery" });
    }

    [HttpPost("{id}/mystery/{visitaId}/elimina")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminaVisitaMystery(string id, string visitaId)
    {
        await _mongo.MysteryClient.DeleteOneAsync(v => v.Id == visitaId && v.TenantId == _tenant.TenantId && v.ClinicaId == id);
        TempData["flash"] = "Visita rimossa.";
        return RedirectToAction(nameof(Details), new { id, tab = "mystery" });
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
    public async Task<IActionResult> Create()
    {
        ViewData["Section"] = "cliniche";
        ViewData["IsNew"] = true;
        await PopulateSocietaAsync();
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
            await PopulateSocietaAsync();
            return View("Form", model);
        }
        model.TenantId = _tenant.TenantId!;
        model.CreatedAt = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(model.SocietaId)) model.SocietaId = null;
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
        await PopulateSocietaAsync();
        return View("Form", clinica);
    }

    /// <summary>Restituisce la modale di modifica rapida della clinica (partial).</summary>
    [HttpGet("{id}/edit-modal")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    public async Task<IActionResult> EditModal(string id)
    {
        var clinica = await Load(id);
        if (clinica is null) return NotFound();

        var tid = _tenant.TenantId;
        var oggi = DateTime.UtcNow.Date;
        var soglia30 = oggi.AddDays(30);

        var interventi = await _mongo.InterventiClinica
            .Find(i => i.TenantId == tid && i.ClinicaId == id && i.ProssimaScadenza != null && i.ProssimaScadenza <= soglia30)
            .ToListAsync();
        var scaduti = interventi.Count(i => i.ProssimaScadenza!.Value.Date < oggi);
        var imminenti = interventi.Count(i => i.ProssimaScadenza!.Value.Date >= oggi && i.ProssimaScadenza.Value.Date <= soglia30);

        var dottoriCount = (int)await _mongo.Dottori
            .CountDocumentsAsync(d => d.TenantId == tid && d.ClinicaPrincipaleId == id);
        var dipendentiCount = (int)await _mongo.Dipendenti
            .CountDocumentsAsync(d => d.TenantId == tid && d.ClinicaId == id);

        var societa = await _mongo.Societa
            .Find(s => s.TenantId == tid)
            .SortBy(s => s.Nome)
            .ToListAsync();
        ViewData["Societa"] = societa;

        var vm = new ClinicaEditModalViewModel
        {
            Clinica = clinica,
            InterventiScaduti = scaduti,
            InterventiImminenti = imminenti,
            Dottori = dottoriCount,
            Dipendenti = dipendentiCount
        };

        var calendarioHref = Url.Action("Index", "CalendarioInterventi");

        if (scaduti > 0)
        {
            vm.Critiche.Add(new ClinicaCriticita(
                $"{scaduti} interventi scaduti", "⛔",
                Href: calendarioHref,
                Tooltip: "Apri il Calendario interventi per gestire le scadenze già scadute"));
        }
        if (imminenti > 0)
        {
            vm.Avvisi.Add(new ClinicaCriticita(
                $"{imminenti} in scadenza ≤ 30 gg", "⏰",
                Href: calendarioHref,
                Tooltip: "Apri il Calendario interventi per le scadenze imminenti"));
        }

        // Campi anagrafici — criticità sulla scheda. I "critici" sono campi
        // richiesti (Nome/Citta/Indirizzo). Gli altri sono "avvisi" raccomandati.
        if (string.IsNullOrWhiteSpace(clinica.Nome))
            vm.Critiche.Add(new ClinicaCriticita("Nome clinica", "🏷", "fld-Nome", "sec-identita"));
        if (string.IsNullOrWhiteSpace(clinica.Citta))
            vm.Critiche.Add(new ClinicaCriticita("Città", "🏙", "fld-Citta", "sec-sede"));
        if (string.IsNullOrWhiteSpace(clinica.Indirizzo))
            vm.Critiche.Add(new ClinicaCriticita("Indirizzo", "📍", "fld-Indirizzo", "sec-sede"));

        if (string.IsNullOrWhiteSpace(clinica.Telefono))
            vm.Avvisi.Add(new ClinicaCriticita("Telefono", "📞", "fld-Telefono", "sec-contatti"));
        if (string.IsNullOrWhiteSpace(clinica.Email))
            vm.Avvisi.Add(new ClinicaCriticita("Email", "✉️", "fld-Email", "sec-contatti"));
        if (!clinica.IsGeolocalized)
            vm.Avvisi.Add(new ClinicaCriticita("Coordinate GPS", "🗺", "fld-Latitudine", "sec-sede"));
        if (clinica.NumeroRiuniti <= 0)
            vm.Avvisi.Add(new ClinicaCriticita("Numero riuniti", "🦷", "fld-NumeroRiuniti", "sec-identita"));
        if (!clinica.OrganicoTarget.HasValue || clinica.OrganicoTarget.Value <= 0)
            vm.Avvisi.Add(new ClinicaCriticita("Organico target", "👥", "fld-OrganicoTarget", "sec-identita"));

        // Completezza: 9 voci anagrafiche pesate pari + assenza interventi scaduti.
        var voci = new[]
        {
            !string.IsNullOrWhiteSpace(clinica.Nome),
            !string.IsNullOrWhiteSpace(clinica.Citta),
            !string.IsNullOrWhiteSpace(clinica.Indirizzo),
            !string.IsNullOrWhiteSpace(clinica.Telefono),
            !string.IsNullOrWhiteSpace(clinica.Email),
            clinica.IsGeolocalized,
            clinica.NumeroRiuniti > 0,
            clinica.OrganicoTarget.HasValue && clinica.OrganicoTarget.Value > 0,
            scaduti == 0
        };
        vm.Completezza = (int)Math.Round(voci.Count(x => x) * 100.0 / voci.Length);

        return PartialView("_EditModal", vm);
    }

    [HttpPost("{id}/modifica")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, Clinica model,
        [FromHeader(Name = "X-Edit-Modal")] string? modal)
    {
        var isModal = modal == "1";
        if (id != model.Id)
        {
            if (isModal) return BadRequest(new { errors = new { Id = new[] { "Id non coerente." } } });
            return BadRequest();
        }
        if (!ModelState.IsValid)
        {
            if (isModal)
            {
                var errors = ModelState
                    .Where(e => e.Value!.Errors.Count > 0)
                    .ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray());
                return BadRequest(new { errors });
            }
            ViewData["Section"] = "cliniche";
            ViewData["IsNew"] = false;
            await PopulateSocietaAsync();
            return View("Form", model);
        }
        var existing = await Load(id);
        if (existing is null) return NotFound();
        model.TenantId = existing.TenantId;
        model.CreatedAt = existing.CreatedAt;
        model.UpdatedAt = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(model.SocietaId)) model.SocietaId = null;
        await _mongo.Cliniche.ReplaceOneAsync(c => c.Id == id && c.TenantId == _tenant.TenantId, model);
        TempData["flash"] = $"Clinica «{model.Nome}» aggiornata.";
        if (isModal) return Ok(new { ok = true });
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

    private async Task PopulateSocietaAsync()
    {
        var societa = await _mongo.Societa
            .Find(s => s.TenantId == _tenant.TenantId)
            .SortBy(s => s.Nome)
            .ToListAsync();
        ViewData["Societa"] = societa;
    }
}
