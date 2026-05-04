using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

/// <summary>
/// "Calendario interventi" — vista riepilogativa cross-clinica delle manutenzioni
/// e contratti ricorrenti (registro antincendio, controlli elettrici, radiografico,
/// bombole O₂, smaltimento rifiuti, RENTRI…). Replica la struttura del file Excel
/// fornito da Confident: una sezione per tipologia × una riga per clinica.
/// </summary>
[Authorize(Policy = Policies.RequireBackoffice)]
[Route("calendario-interventi")]
public class CalendarioInterventiController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;

    public CalendarioInterventiController(MongoContext mongo, ITenantContext tenant)
    {
        _mongo = mongo;
        _tenant = tenant;
    }

    public record FornitoreInfo(string RagioneSociale, string? Indirizzo = null, string? Telefono = null,
        string? Cellulare = null, string? Fax = null, string? Email = null, string? EmailSecondaria = null,
        string? Pec = null, string? Note = null);

    public record SezioneDefinizione(TipoIntervento Tipo, string Titolo, string Icona, string Frequenza, FornitoreInfo Fornitore);

    /// <summary>
    /// Anagrafica statica delle sezioni del calendario interventi.
    /// I fornitori sono presi dalle anagrafiche presenti sul foglio xls fornito da Confident:
    /// dove non era indicato (NOLOMEDICAL, manutenzione condizionatori, radiografico, bombole O₂)
    /// si è messo un placeholder con il nome del fornitore di riferimento, modificabile dal back-office.
    /// </summary>
    public static readonly SezioneDefinizione[] SezioniDef =
    {
        new(TipoIntervento.RegistroAntincendio,         "Registro antincendio",          "🧯", "a 6 mesi",
            new FornitoreInfo("CVZ Antincendi S.r.l.",
                Indirizzo: "Busto Arsizio – Via Volterra 14",
                Telefono:  "+39 0331 678220",
                Fax:       "+39 0331 322252",
                Email:     "info@cvzantincendi.it",
                Pec:       "nuovacavazzana@legalmail.it")),

        new(TipoIntervento.PuliziaFiltriCondizionatori, "Pulizia filtri condizionatori", "❄️", "a 6 mesi",
            new FornitoreInfo("Manutentore condizionatori",
                Note: "Fornitore non indicato sul foglio xls — da completare in anagrafica.")),

        new(TipoIntervento.MessaATerra,                 "Messa a terra (biennale)",      "⚡", "biennale",
            new FornitoreInfo("V.E.M. Srl (sempre tramite Ing. Motta)",
                Indirizzo: "Via Bellini 5 – Scanzorosciate (BG)",
                Telefono:  "035 027 06 21",
                Email:     "info@vemverifiche.com",
                EmailSecondaria: "impiantielettrici@vemverifiche.com")),

        new(TipoIntervento.ImpiantoElettricoAnnuale,    "Impianto elettrico (annuale)",  "🔌", "annuale",
            new FornitoreInfo("Ing. Andrea Motta",
                Indirizzo: "Via Medici del Vascello 23 sc. 4 – Milano",
                Cellulare: "335 524 00 15",
                Email:     "info@andreamotta.it",
                EmailSecondaria: "andrea.motta@fastwebnet.it")),

        new(TipoIntervento.ElettromedicaliBiennale,     "Elettromedicali (biennale)",    "🩺", "biennale",
            new FornitoreInfo("Ing. Andrea Motta",
                Indirizzo: "Via Medici del Vascello 23 sc. 4 – Milano",
                Cellulare: "335 524 00 15",
                Email:     "info@andreamotta.it",
                EmailSecondaria: "andrea.motta@fastwebnet.it")),

        new(TipoIntervento.Radiografico,                "Radiografico",                  "🦷", "annuale",
            new FornitoreInfo("Esperto Qualificato Radioprotezione",
                Note: "Fornitore non indicato sul foglio xls — da completare in anagrafica.")),

        new(TipoIntervento.BombolaOssigeno,             "Bombola ossigeno",              "🫧", "—",
            new FornitoreInfo("SOL Group",
                Note: "Fornitore non indicato sul foglio xls — da completare in anagrafica.")),

        new(TipoIntervento.Nolomedical,                 "Nolomedical",                   "📦", "annuale",
            new FornitoreInfo("Nolomedical s.r.l.",
                Note: "Contatti non indicati sul foglio xls — da completare in anagrafica.")),

        new(TipoIntervento.EcologiaAmbienteContratto,   "Ecologia Ambiente — contratto (dal 01/01/2026)", "♻️", "biennale",
            new FornitoreInfo("Ecologia Ambiente",
                Note: "Disdetta via PEC entro 90 gg dalla scadenza (ago-2028).")),

        new(TipoIntervento.EcologiaAmbienteContrattoStorico, "Ecologia Ambiente — contratto storico (fino 31/12/2025)", "📚", "storico",
            new FornitoreInfo("Ecologia Ambiente",
                Note: "Contratti precedenti al rinnovo del 01/01/2026 — mantenuti come storico.")),

        new(TipoIntervento.EcologiaAmbienteRentri,      "Iscrizione RENTRI + App FIR",   "📋", "annuale",
            new FornitoreInfo("Ecologia Ambiente / RENTRI",
                Note: "Rinnovo pagamento entro il 30/4 di ogni anno."))
    };

    [HttpGet("")]
    public async Task<IActionResult> Index(TipoIntervento? tipo = null)
    {
        var tid = _tenant.TenantId!;
        var cliniche = await _mongo.Cliniche
            .Find(c => c.TenantId == tid)
            .SortBy(c => c.Nome)
            .ToListAsync();

        // Direttori vedono solo le proprie sedi; backoffice/management vedono tutto.
        if (_tenant.ClinicaIds.Count > 0)
        {
            cliniche = cliniche.Where(c => _tenant.ClinicaIds.Contains(c.Id)).ToList();
        }

        var interventi = await _mongo.InterventiClinica
            .Find(i => i.TenantId == tid)
            .ToListAsync();

        var oggi = DateTime.UtcNow.Date;
        var sezioni = new List<SezioneIntervento>();
        foreach (var def in SezioniDef)
        {
            if (tipo.HasValue && tipo.Value != def.Tipo) continue;
            var sez = new SezioneIntervento
            {
                Tipo = def.Tipo,
                Titolo = def.Titolo,
                Icona = def.Icona,
                FornitoreDefault = def.Fornitore.RagioneSociale,
                FornitoreInfo = def.Fornitore,
                Frequenza = def.Frequenza
            };
            foreach (var c in cliniche)
            {
                var iv = interventi.FirstOrDefault(x => x.ClinicaId == c.Id && x.Tipo == def.Tipo);
                sez.Righe.Add(new RigaIntervento
                {
                    ClinicaId = c.Id,
                    ClinicaNome = c.Nome,
                    Intervento = iv
                });
            }
            sezioni.Add(sez);
        }

        var inter30 = oggi.AddDays(30);
        var vm = new CalendarioInterventiViewModel
        {
            Sezioni = sezioni,
            Cliniche = cliniche,
            Totali = interventi.Count,
            Scaduti = interventi.Count(i => i.ProssimaScadenza.HasValue && i.ProssimaScadenza.Value.Date < oggi),
            Imminenti = interventi.Count(i => i.ProssimaScadenza.HasValue && i.ProssimaScadenza.Value.Date >= oggi && i.ProssimaScadenza.Value.Date <= inter30)
        };

        ViewData["Section"] = "calendario-interventi";
        ViewData["TipoFiltro"] = tipo;
        return View(vm);
    }

    /// <summary>
    /// Carica la riga (clinica × tipologia) per la modale di edit.
    /// Crea una bozza vuota se non esiste ancora un record.
    /// </summary>
    [HttpGet("modale")]
    public async Task<IActionResult> Modale(string clinicaId, TipoIntervento tipo)
    {
        var tid = _tenant.TenantId!;
        var clinica = await _mongo.Cliniche
            .Find(c => c.Id == clinicaId && c.TenantId == tid)
            .FirstOrDefaultAsync();
        if (clinica is null) return NotFound();
        if (_tenant.ClinicaIds.Count > 0 && !_tenant.CanAccessClinica(clinicaId)) return Forbid();

        var iv = await _mongo.InterventiClinica
            .Find(x => x.TenantId == tid && x.ClinicaId == clinicaId && x.Tipo == tipo)
            .FirstOrDefaultAsync();

        var def = SezioniDef.FirstOrDefault(s => s.Tipo == tipo);

        ViewData["Clinica"] = clinica;
        ViewData["Tipo"] = tipo;
        ViewData["Titolo"] = def?.Titolo ?? tipo.ToString();
        ViewData["FornitoreDefault"] = def?.Fornitore.RagioneSociale ?? "";
        ViewData["FrequenzaDefault"] = def?.Frequenza ?? "";
        ViewData["FornitoreInfo"] = def?.Fornitore;

        return PartialView("_Modale", iv ?? new InterventoClinica
        {
            TenantId = tid,
            ClinicaId = clinicaId,
            Tipo = tipo,
            Fornitore = def?.Fornitore.RagioneSociale ?? "",
            Frequenza = def?.Frequenza
        });
    }

    [HttpPost("salva")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Salva(string clinicaId, TipoIntervento tipo, string? fornitore, string? frequenza,
        DateTime? dataUltimoIntervento, DateTime? prossimaScadenza, bool archiviatoFaldoneAts, string? note,
        [FromForm] Dictionary<string, string>? dettagli)
    {
        var tid = _tenant.TenantId!;
        if (_tenant.ClinicaIds.Count > 0 && !_tenant.CanAccessClinica(clinicaId)) return Forbid();

        var existing = await _mongo.InterventiClinica
            .Find(x => x.TenantId == tid && x.ClinicaId == clinicaId && x.Tipo == tipo)
            .FirstOrDefaultAsync();

        DateTime? ToUtc(DateTime? d) => d.HasValue ? DateTime.SpecifyKind(d.Value.Date, DateTimeKind.Utc) : null;
        var puliti = (dettagli ?? new())
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Trim());

        if (existing is null)
        {
            await _mongo.InterventiClinica.InsertOneAsync(new InterventoClinica
            {
                TenantId = tid,
                ClinicaId = clinicaId,
                Tipo = tipo,
                Fornitore = fornitore ?? "",
                Frequenza = frequenza,
                DataUltimoIntervento = ToUtc(dataUltimoIntervento),
                ProssimaScadenza = ToUtc(prossimaScadenza),
                ArchiviatoFaldoneAts = archiviatoFaldoneAts,
                Note = note,
                Dettagli = puliti
            });
        }
        else
        {
            await _mongo.InterventiClinica.UpdateOneAsync(
                x => x.Id == existing.Id,
                Builders<InterventoClinica>.Update
                    .Set(x => x.Fornitore, fornitore ?? "")
                    .Set(x => x.Frequenza, frequenza)
                    .Set(x => x.DataUltimoIntervento, ToUtc(dataUltimoIntervento))
                    .Set(x => x.ProssimaScadenza, ToUtc(prossimaScadenza))
                    .Set(x => x.ArchiviatoFaldoneAts, archiviatoFaldoneAts)
                    .Set(x => x.Note, note)
                    .Set(x => x.Dettagli, puliti)
                    .Set(x => x.UpdatedAt, DateTime.UtcNow));
        }

        TempData["flash"] = "Intervento salvato.";

        // Ritorno contestualizzato: se la richiesta arriva dal dettaglio clinica, torno lì,
        // altrimenti rimango nel calendario filtrato sulla tipologia appena editata.
        var returnTo = Request.Form["returnTo"].ToString();
        if (returnTo == "clinica")
        {
            return RedirectToAction("Details", "Cliniche", new { id = clinicaId });
        }
        return RedirectToAction(nameof(Index), new { tipo });
    }

    [HttpPost("elimina")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Elimina(string clinicaId, TipoIntervento tipo, string? returnTo = null)
    {
        var tid = _tenant.TenantId!;
        if (_tenant.ClinicaIds.Count > 0 && !_tenant.CanAccessClinica(clinicaId)) return Forbid();
        await _mongo.InterventiClinica.DeleteOneAsync(x => x.TenantId == tid && x.ClinicaId == clinicaId && x.Tipo == tipo);
        TempData["flash"] = "Riga rimossa dal calendario.";
        if (returnTo == "clinica")
            return RedirectToAction("Details", "Cliniche", new { id = clinicaId });
        return RedirectToAction(nameof(Index), new { tipo });
    }
}
