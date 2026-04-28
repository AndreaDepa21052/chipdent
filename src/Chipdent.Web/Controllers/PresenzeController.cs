using System.Globalization;
using System.Security.Claims;
using System.Text;
using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Hubs;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Insights;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize]
[Route("presenze")]
public class PresenzeController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly INotificationPublisher _publisher;

    /// <summary>Tolleranza per considerare una timbratura "in ritardo" rispetto al turno (minuti).</summary>
    private const int ToleranceMinutes = 10;

    public PresenzeController(MongoContext mongo, ITenantContext tenant, INotificationPublisher publisher)
    {
        _mongo = mongo;
        _tenant = tenant;
        _publisher = publisher;
    }

    [HttpGet("")]
    [Authorize(Policy = Policies.RequireDirettore)]
    public async Task<IActionResult> Index(DateTime? mese = null, string? clinicaId = null)
    {
        var tid = _tenant.TenantId!;
        var meseRif = mese ?? DateTime.Today;
        var primoDelMese = new DateTime(meseRif.Year, meseRif.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var primoMeseSucc = primoDelMese.AddMonths(1);

        var cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).SortBy(c => c.Nome).ToListAsync();
        var clinicheLookup = cliniche.ToDictionary(c => c.Id, c => c.Nome);

        var dipFilter = Builders<Dipendente>.Filter.Eq(d => d.TenantId, tid)
                       & Builders<Dipendente>.Filter.Ne(d => d.Stato, StatoDipendente.Cessato);
        if (!string.IsNullOrEmpty(clinicaId))
            dipFilter &= Builders<Dipendente>.Filter.Eq(d => d.ClinicaId, clinicaId);
        var dipendenti = await _mongo.Dipendenti.Find(dipFilter).SortBy(d => d.Cognome).ToListAsync();

        var dipIds = dipendenti.Select(d => d.Id).ToList();
        var turni = await _mongo.Turni.Find(t => t.TenantId == tid && t.Data >= primoDelMese && t.Data < primoMeseSucc
                                                  && t.TipoPersona == TipoPersona.Dipendente).ToListAsync();
        var timbrature = await _mongo.Timbrature.Find(x => x.TenantId == tid && x.Timestamp >= primoDelMese && x.Timestamp < primoMeseSucc).ToListAsync();

        var righe = dipendenti.Select(d =>
        {
            var miei = turni.Where(t => t.PersonaId == d.Id && t.TipoPersona == TipoPersona.Dipendente).ToList();
            var giorniP = miei.Select(t => t.Data.Date).Distinct().Count();
            var mieTimb = timbrature.Where(x => x.DipendenteId == d.Id).ToList();

            var agg = TimbraturaCalculator.AggregaMese(d.Id, mieTimb, miei, primoDelMese, primoMeseSucc);

            return new DipendentePresenzeRow(
                d.Id, d.NomeCompleto,
                clinicheLookup.GetValueOrDefault(d.ClinicaId, "—"),
                OrePianificate: (int)Math.Round(agg.OrePianificate.TotalHours),
                OreLavorate: (int)Math.Round(agg.OreLavorate.TotalHours),
                Ritardi: agg.Ritardi,
                UsciteAnticipate: agg.UsciteAnticipate,
                GiorniLavorati: agg.GiorniLavorati,
                GiorniPianificati: giorniP,
                OrePausa: (int)Math.Round(agg.OrePausa.TotalHours),
                SaldoOre: (int)Math.Round(agg.SaldoBancaOre.TotalHours),
                GiorniInRemoto: agg.GiorniInRemoto);
        }).ToList();

        var ultimeRows = timbrature
            .OrderByDescending(t => t.Timestamp)
            .Take(20)
            .Select(t => new TimbraturaRow(t, dipendenti.FirstOrDefault(d => d.Id == t.DipendenteId)?.NomeCompleto ?? "—"))
            .ToList();

        ViewData["Section"] = "presenze";
        return View(new PresenzeIndexViewModel
        {
            Mese = primoDelMese,
            Righe = righe,
            UltimeTimbrature = ultimeRows,
            CanManage = User.IsManagement() || User.IsDirettore(),
            Cliniche = cliniche,
            FilterClinicaId = clinicaId
        });
    }

    /// <summary>Pagina kiosk timbratura PIN — accessibile senza autenticazione (può essere usata su tablet a parete).</summary>
    [AllowAnonymous]
    [HttpGet("kiosk")]
    public IActionResult Kiosk()
    {
        ViewData["Section"] = "presenze";
        return View();
    }

    [AllowAnonymous]
    [HttpPost("kiosk")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> KioskPost(string pin, string? tenantSlug = null)
    {
        if (string.IsNullOrEmpty(pin))
        {
            ViewBag.Esito = "PIN richiesto.";
            ViewBag.EsitoTipo = "errore";
            return View("Kiosk");
        }

        // Risoluzione tenant: priorità a tenantSlug nella query/form, poi al cookie auth
        string? tid = _tenant.TenantId;
        if (string.IsNullOrEmpty(tid) && !string.IsNullOrEmpty(tenantSlug))
        {
            var t = await _mongo.Tenants.Find(x => x.Slug == tenantSlug && x.IsActive).FirstOrDefaultAsync();
            tid = t?.Id;
        }
        if (string.IsNullOrEmpty(tid))
        {
            ViewBag.Esito = "Tenant non identificato. Contatta l'amministratore.";
            ViewBag.EsitoTipo = "errore";
            return View("Kiosk");
        }

        var dip = await _mongo.Dipendenti.Find(d => d.TenantId == tid && d.PinTimbratura == pin && d.Stato != StatoDipendente.Cessato).FirstOrDefaultAsync();
        if (dip is null)
        {
            ViewBag.Esito = "PIN non riconosciuto.";
            ViewBag.EsitoTipo = "errore";
            return View("Kiosk");
        }

        // Determino check-in o check-out: prendo l'ultima timbratura del giorno
        var oggi = DateTime.UtcNow.Date;
        var ultima = await _mongo.Timbrature
            .Find(x => x.TenantId == tid && x.DipendenteId == dip.Id && x.Timestamp >= oggi && x.Timestamp < oggi.AddDays(1))
            .SortByDescending(x => x.Timestamp).FirstOrDefaultAsync();
        var tipo = (ultima is null || ultima.Tipo == TipoTimbratura.CheckOut)
            ? TipoTimbratura.CheckIn
            : TipoTimbratura.CheckOut;

        await _mongo.Timbrature.InsertOneAsync(new Timbratura
        {
            TenantId = tid,
            DipendenteId = dip.Id,
            ClinicaId = dip.ClinicaId,
            Tipo = tipo,
            Timestamp = DateTime.UtcNow,
            Metodo = MetodoTimbratura.Pin
        });

        await _publisher.PublishAsync(tid, "activity", new
        {
            kind = "shift",
            title = $"{(tipo == TipoTimbratura.CheckIn ? "Entrata" : "Uscita")}: {dip.NomeCompleto}",
            description = DateTime.Now.ToString("HH:mm"),
            when = DateTime.UtcNow
        });

        ViewBag.Esito = $"✓ {(tipo == TipoTimbratura.CheckIn ? "Entrata" : "Uscita")} registrata per {dip.NomeCompleto} alle {DateTime.Now:HH:mm}";
        ViewBag.EsitoTipo = "ok";
        ViewBag.TenantSlug = tenantSlug;
        return View("Kiosk");
    }

    [HttpPost("manuale")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Manuale(TimbraManualeViewModel vm)
    {
        var tid = _tenant.TenantId!;
        var dip = await _mongo.Dipendenti.Find(d => d.Id == vm.DipendenteId && d.TenantId == tid).FirstOrDefaultAsync();
        if (dip is null) return BadRequest();
        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        await _mongo.Timbrature.InsertOneAsync(new Timbratura
        {
            TenantId = tid,
            DipendenteId = dip.Id,
            ClinicaId = dip.ClinicaId,
            Tipo = vm.Tipo,
            Timestamp = DateTime.SpecifyKind(vm.Quando, DateTimeKind.Utc),
            Metodo = MetodoTimbratura.Manuale,
            RegistrataDaUserId = meId,
            Note = vm.Note
        });
        TempData["flash"] = "Timbratura inserita manualmente.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/elimina")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminaTimbratura(string id)
    {
        await _mongo.Timbrature.DeleteOneAsync(t => t.Id == id && t.TenantId == _tenant.TenantId);
        TempData["flash"] = "Timbratura eliminata.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("pin")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPin(PinSetupViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            TempData["flash"] = "PIN non valido (4-6 cifre).";
            return RedirectToAction(nameof(Index));
        }
        var tid = _tenant.TenantId!;
        var dup = await _mongo.Dipendenti.Find(d => d.TenantId == tid && d.PinTimbratura == vm.Pin && d.Id != vm.DipendenteId).AnyAsync();
        if (dup)
        {
            TempData["flash"] = "PIN già in uso da un altro dipendente.";
            return RedirectToAction(nameof(Index));
        }
        await _mongo.Dipendenti.UpdateOneAsync(
            d => d.Id == vm.DipendenteId && d.TenantId == tid,
            Builders<Dipendente>.Update.Set(d => d.PinTimbratura, vm.Pin).Set(d => d.UpdatedAt, DateTime.UtcNow));
        TempData["flash"] = "PIN aggiornato.";
        return RedirectToAction(nameof(Index));
    }

    // ───── Correzioni timbratura ─────

    [HttpGet("correzioni")]
    [Authorize(Policy = Policies.RequireDirettore)]
    public async Task<IActionResult> Correzioni(StatoCorrezione? filter = null)
    {
        var tid = _tenant.TenantId!;
        var f = Builders<CorrezioneTimbratura>.Filter.Eq(c => c.TenantId, tid);
        if (filter.HasValue) f &= Builders<CorrezioneTimbratura>.Filter.Eq(c => c.Stato, filter.Value);
        else f &= Builders<CorrezioneTimbratura>.Filter.Eq(c => c.Stato, StatoCorrezione.Aperta);
        var items = await _mongo.CorrezioniTimbrature.Find(f).SortByDescending(c => c.CreatedAt).ToListAsync();
        ViewData["Section"] = "presenze";
        ViewData["Filter"] = filter;
        return View(items);
    }

    [HttpPost("correzioni/{id}/approva")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApprovaCorrezione(string id, string? note = null)
    {
        var tid = _tenant.TenantId!;
        var c = await _mongo.CorrezioniTimbrature.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (c is null) return NotFound();
        if (c.Stato != StatoCorrezione.Aperta)
        {
            TempData["flash"] = "Richiesta già processata.";
            return RedirectToAction(nameof(Correzioni));
        }

        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var meName = User.Identity?.Name ?? "";

        // Applica la correzione
        switch (c.Tipo)
        {
            case TipoCorrezione.Aggiungi:
                var dip = await _mongo.Dipendenti.Find(d => d.Id == c.DipendenteId).FirstOrDefaultAsync();
                if (dip is not null)
                {
                    await _mongo.Timbrature.InsertOneAsync(new Timbratura
                    {
                        TenantId = tid,
                        DipendenteId = c.DipendenteId,
                        ClinicaId = dip.ClinicaId,
                        Tipo = c.TipoTimbraturaProposto,
                        Timestamp = DateTime.SpecifyKind(c.TimestampProposto, DateTimeKind.Utc),
                        Metodo = MetodoTimbratura.Manuale,
                        Remoto = c.RemotoProposto,
                        RegistrataDaUserId = meId,
                        Note = $"Da correzione approvata: {c.Motivazione}"
                    });
                }
                break;
            case TipoCorrezione.Modifica:
                if (!string.IsNullOrEmpty(c.TimbraturaId))
                {
                    await _mongo.Timbrature.UpdateOneAsync(
                        x => x.Id == c.TimbraturaId && x.TenantId == tid,
                        Builders<Timbratura>.Update
                            .Set(x => x.Tipo, c.TipoTimbraturaProposto)
                            .Set(x => x.Timestamp, DateTime.SpecifyKind(c.TimestampProposto, DateTimeKind.Utc))
                            .Set(x => x.Remoto, c.RemotoProposto));
                }
                break;
            case TipoCorrezione.Elimina:
                if (!string.IsNullOrEmpty(c.TimbraturaId))
                {
                    await _mongo.Timbrature.DeleteOneAsync(x => x.Id == c.TimbraturaId && x.TenantId == tid);
                }
                break;
        }

        await _mongo.CorrezioniTimbrature.UpdateOneAsync(
            x => x.Id == id && x.TenantId == tid,
            Builders<CorrezioneTimbratura>.Update
                .Set(x => x.Stato, StatoCorrezione.Approvata)
                .Set(x => x.DecisoreUserId, meId)
                .Set(x => x.DecisoreNome, meName)
                .Set(x => x.DataDecisione, DateTime.UtcNow)
                .Set(x => x.NoteDecisore, note)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        TempData["flash"] = "Correzione approvata e applicata.";
        return RedirectToAction(nameof(Correzioni));
    }

    [HttpPost("correzioni/{id}/respingi")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RespingiCorrezione(string id, string? note = null)
    {
        var tid = _tenant.TenantId!;
        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var meName = User.Identity?.Name ?? "";
        await _mongo.CorrezioniTimbrature.UpdateOneAsync(
            x => x.Id == id && x.TenantId == tid && x.Stato == StatoCorrezione.Aperta,
            Builders<CorrezioneTimbratura>.Update
                .Set(x => x.Stato, StatoCorrezione.Respinta)
                .Set(x => x.DecisoreUserId, meId)
                .Set(x => x.DecisoreNome, meName)
                .Set(x => x.DataDecisione, DateTime.UtcNow)
                .Set(x => x.NoteDecisore, note)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));
        TempData["flash"] = "Richiesta respinta.";
        return RedirectToAction(nameof(Correzioni));
    }

    // ───── Approvazione timesheet mensile ─────

    [HttpPost("approva-timesheet")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApprovaTimesheet(string dipendenteId, DateTime mese, string? note = null)
    {
        var tid = _tenant.TenantId!;
        var dip = await _mongo.Dipendenti.Find(d => d.Id == dipendenteId && d.TenantId == tid).FirstOrDefaultAsync();
        if (dip is null) return NotFound();

        var primo = new DateTime(mese.Year, mese.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var fine = primo.AddMonths(1);
        var timb = await _mongo.Timbrature.Find(x => x.TenantId == tid && x.DipendenteId == dipendenteId
                                                      && x.Timestamp >= primo && x.Timestamp < fine).ToListAsync();
        var turni = await _mongo.Turni.Find(x => x.TenantId == tid && x.PersonaId == dipendenteId
                                                   && x.TipoPersona == TipoPersona.Dipendente
                                                   && x.Data >= primo && x.Data < fine).ToListAsync();
        var agg = TimbraturaCalculator.AggregaMese(dipendenteId, timb, turni, primo, fine);
        var periodo = primo.ToString("yyyy-MM");

        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var meName = User.Identity?.Name ?? "";

        // Upsert
        await _mongo.ApprovazioniTimesheet.ReplaceOneAsync(
            a => a.TenantId == tid && a.DipendenteId == dipendenteId && a.Periodo == periodo,
            new ApprovazioneTimesheet
            {
                TenantId = tid,
                DipendenteId = dipendenteId,
                DipendenteNome = dip.NomeCompleto,
                Periodo = periodo,
                Stato = StatoApprovazioneTimesheet.Approvato,
                DirettoreUserId = meId,
                DirettoreNome = meName,
                ApprovatoIl = DateTime.UtcNow,
                Note = note,
                OreLavorateMin = (int)agg.OreLavorate.TotalMinutes,
                OrePianificateMin = (int)agg.OrePianificate.TotalMinutes,
                OrePausaMin = (int)agg.OrePausa.TotalMinutes,
                SaldoOreMin = (int)agg.SaldoBancaOre.TotalMinutes,
                Ritardi = agg.Ritardi,
                UsciteAnticipate = agg.UsciteAnticipate,
                GiorniLavorati = agg.GiorniLavorati
            },
            new ReplaceOptions { IsUpsert = true });

        TempData["flash"] = $"Timesheet di {dip.NomeCompleto} per {periodo} approvato.";
        return RedirectToAction(nameof(Index), new { mese = mese.ToString("yyyy-MM-dd") });
    }

    [HttpGet("export.csv")]
    [Authorize(Policy = Policies.RequireDirettore)]
    public async Task<IActionResult> ExportCsv(DateTime? mese = null, string? clinicaId = null)
    {
        var vm = (await Index(mese, clinicaId)) as ViewResult;
        var model = vm?.Model as PresenzeIndexViewModel ?? new PresenzeIndexViewModel();
        var sb = new StringBuilder();
        sb.Append('﻿');
        sb.AppendLine($"Mese:;{model.Mese:yyyy-MM}");
        sb.AppendLine("Dipendente;Sede;OrePianificate;OreLavorate;GiorniPianificati;GiorniLavorati;Ritardi;UsciteAnticipate");
        foreach (var r in model.Righe)
        {
            sb.Append(Csv(r.Nome)).Append(';')
              .Append(Csv(r.ClinicaNome)).Append(';')
              .Append(r.OrePianificate).Append(';')
              .Append(r.OreLavorate).Append(';')
              .Append(r.GiorniPianificati).Append(';')
              .Append(r.GiorniLavorati).Append(';')
              .Append(r.Ritardi).Append(';')
              .Append(r.UsciteAnticipate)
              .AppendLine();
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", $"presenze-{model.Mese:yyyy-MM}.csv");
    }

    /// <summary>
    /// Export per consulente del lavoro / sistemi paghe (Zucchetti / TeamSystem-like):
    /// CSV con una riga per (dipendente × giorno), formato strutturato che include
    /// causale paga, ore ordinarie, ore pausa e flag remoto.
    /// </summary>
    [HttpGet("export-paghe.csv")]
    [Authorize(Policy = Policies.RequireDirettore)]
    public async Task<IActionResult> ExportPaghe(DateTime? mese = null, string? clinicaId = null)
    {
        var tid = _tenant.TenantId!;
        var meseRif = mese ?? DateTime.Today;
        var primo = new DateTime(meseRif.Year, meseRif.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var fine = primo.AddMonths(1);

        var dipFilter = Builders<Dipendente>.Filter.Eq(d => d.TenantId, tid)
                       & Builders<Dipendente>.Filter.Ne(d => d.Stato, StatoDipendente.Cessato);
        if (!string.IsNullOrEmpty(clinicaId))
            dipFilter &= Builders<Dipendente>.Filter.Eq(d => d.ClinicaId, clinicaId);
        var dipendenti = await _mongo.Dipendenti.Find(dipFilter).SortBy(d => d.Cognome).ToListAsync();
        var cliniche = (await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync())
            .ToDictionary(c => c.Id, c => c.Nome);
        var timb = await _mongo.Timbrature.Find(x => x.TenantId == tid && x.Timestamp >= primo && x.Timestamp < fine).ToListAsync();
        var turni = await _mongo.Turni.Find(t => t.TenantId == tid && t.TipoPersona == TipoPersona.Dipendente
                                                  && t.Data >= primo && t.Data < fine).ToListAsync();
        var ferie = await _mongo.RichiesteFerie.Find(r => r.TenantId == tid && r.Stato == StatoRichiestaFerie.Approvata
                                                           && r.DataInizio < fine && r.DataFine >= primo).ToListAsync();

        var sb = new StringBuilder();
        sb.Append('﻿');
        // Header pensato per import generico (Zucchetti & C. usano formati simili)
        sb.AppendLine("Periodo;CodiceDip;CognomeNome;CodiceFiscale;Sede;Data;Causale;OreOrdinarie;OrePausa;OreNetto;Remoto;Ritardo;UscitaAnticipata");

        foreach (var d in dipendenti)
        {
            var sedeNome = cliniche.GetValueOrDefault(d.ClinicaId, "—");
            for (var giorno = primo.Date; giorno < fine.Date; giorno = giorno.AddDays(1))
            {
                var tg = timb.Where(x => x.DipendenteId == d.Id && x.Timestamp.Date == giorno).ToList();
                var trg = turni.Where(x => x.PersonaId == d.Id && x.Data.Date == giorno).ToList();
                var ferieGiorno = ferie.FirstOrDefault(f => f.DipendenteId == d.Id
                                                             && f.DataInizio.Date <= giorno && f.DataFine.Date >= giorno);

                string causale;
                int oreOrdinarie = 0, orePausa = 0, oreNetto = 0;
                bool remoto = false, ritardo = false, uscita = false;

                if (ferieGiorno is not null)
                {
                    causale = ferieGiorno.Tipo switch
                    {
                        TipoAssenza.Ferie          => "FER",
                        TipoAssenza.Permesso       => "PER",
                        TipoAssenza.Malattia       => "MAL",
                        TipoAssenza.PermessoStudio => "STU",
                        _ => "ASS"
                    };
                }
                else if (tg.Count > 0)
                {
                    var giornoAgg = TimbraturaCalculator.AggregaGiorno(tg, trg);
                    oreOrdinarie = (int)Math.Round(giornoAgg.OreLavorate.TotalMinutes); // memo: minuti, vedi note
                    orePausa = (int)Math.Round(giornoAgg.OrePausa.TotalMinutes);
                    oreNetto = oreOrdinarie;
                    remoto = giornoAgg.Remoto;
                    ritardo = giornoAgg.Ritardo;
                    uscita = giornoAgg.UsciteAnticipate;
                    causale = remoto ? "SMA" : "ORD"; // SMA = smart working
                }
                else if (trg.Count > 0)
                {
                    causale = "ASG"; // turno assegnato senza timbrature → eventualmente assenza giustificabile
                }
                else
                {
                    continue; // nessun turno né timbratura: salta la riga
                }

                sb.Append(primo.ToString("yyyy-MM")).Append(';')
                  .Append(Csv(d.CodiceFiscale ?? d.Id)).Append(';')
                  .Append(Csv($"{d.Cognome} {d.Nome}".Trim())).Append(';')
                  .Append(Csv(d.CodiceFiscale)).Append(';')
                  .Append(Csv(sedeNome)).Append(';')
                  .Append(giorno.ToString("yyyy-MM-dd")).Append(';')
                  .Append(causale).Append(';')
                  // Ore in formato decimale (es. 8,50) con virgola IT per Zucchetti-friendliness
                  .Append((oreOrdinarie / 60.0).ToString("0.00", CultureInfo.GetCultureInfo("it-IT"))).Append(';')
                  .Append((orePausa / 60.0).ToString("0.00", CultureInfo.GetCultureInfo("it-IT"))).Append(';')
                  .Append((oreNetto / 60.0).ToString("0.00", CultureInfo.GetCultureInfo("it-IT"))).Append(';')
                  .Append(remoto ? "S" : "N").Append(';')
                  .Append(ritardo ? "S" : "N").Append(';')
                  .Append(uscita ? "S" : "N")
                  .AppendLine();
            }
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8",
            $"paghe-{primo:yyyy-MM}{(string.IsNullOrEmpty(clinicaId) ? "" : "-" + clinicaId)}.csv");
    }

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(';') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
