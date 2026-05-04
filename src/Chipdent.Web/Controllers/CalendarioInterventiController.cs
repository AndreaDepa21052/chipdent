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

    public static readonly (TipoIntervento Tipo, string Titolo, string Icona, string Fornitore, string Frequenza)[] SezioniDef =
    {
        (TipoIntervento.RegistroAntincendio,         "Registro antincendio",                "🧯", "CVZ Antincendi S.r.l.",                "a 6 mesi"),
        (TipoIntervento.PuliziaFiltriCondizionatori, "Pulizia filtri condizionatori",       "❄️", "Manutentore condizionatori",           "a 6 mesi"),
        (TipoIntervento.MessaATerra,                 "Messa a terra (biennale)",            "⚡", "Motta Impianti",                       "biennale"),
        (TipoIntervento.ImpiantoElettricoAnnuale,    "Impianto elettrico (annuale)",        "🔌", "Motta Impianti",                       "annuale"),
        (TipoIntervento.ElettromedicaliBiennale,     "Elettromedicali (biennale)",          "🩺", "Motta Impianti",                       "biennale"),
        (TipoIntervento.Radiografico,                "Radiografico",                        "🦷", "Esperto Qualificato Radioprotezione",  "annuale"),
        (TipoIntervento.BombolaOssigeno,             "Bombola ossigeno",                    "🫧", "SOL Group",                            "—"),
        (TipoIntervento.Nolomedical,                 "Nolomedical",                         "📦", "Nolomedical s.r.l.",                   "annuale"),
        (TipoIntervento.EcologiaAmbienteContratto,   "Ecologia Ambiente — contratto",       "♻️", "Ecologia Ambiente",                    "biennale"),
        (TipoIntervento.EcologiaAmbienteRentri,      "RENTRI + App FIR digitale",           "📋", "Ecologia Ambiente / RENTRI",           "annuale")
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
                FornitoreDefault = def.Fornitore,
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
        ViewData["Titolo"] = def.Titolo ?? tipo.ToString();
        ViewData["FornitoreDefault"] = def.Fornitore ?? "";
        ViewData["FrequenzaDefault"] = def.Frequenza ?? "";

        return PartialView("_Modale", iv ?? new InterventoClinica
        {
            TenantId = tid,
            ClinicaId = clinicaId,
            Tipo = tipo,
            Fornitore = def.Fornitore ?? "",
            Frequenza = def.Frequenza
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
