using System.Globalization;
using System.Text;
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
[Route("report")]
public class ReportController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;

    public ReportController(MongoContext mongo, ITenantContext tenant)
    {
        _mongo = mongo;
        _tenant = tenant;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(DateTime? mese = null)
    {
        var tid = _tenant.TenantId!;
        var meseRif = (mese ?? DateTime.Today);
        var primoDelMese = new DateTime(meseRif.Year, meseRif.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var primoMeseSucc = primoDelMese.AddMonths(1);
        var dodiciMesiFa = primoDelMese.AddMonths(-12);

        var cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).SortBy(c => c.Nome).ToListAsync();
        var dipendenti = await _mongo.Dipendenti.Find(d => d.TenantId == tid).ToListAsync();
        var attivi = dipendenti.Where(d => d.Stato != StatoDipendente.Cessato).ToList();
        var contratti = await _mongo.Contratti.Find(c => c.TenantId == tid).ToListAsync();
        var turni = await _mongo.Turni.Find(t => t.TenantId == tid && t.Data >= primoDelMese && t.Data < primoMeseSucc).ToListAsync();
        var ferieApprovate = await _mongo.RichiesteFerie.Find(r => r.TenantId == tid && r.Stato == StatoRichiestaFerie.Approvata
                                                                   && r.DataInizio < primoMeseSucc && r.DataFine >= primoDelMese).ToListAsync();

        // ── Presenze ──
        var presenze = cliniche.Select(c =>
        {
            var sedeTurni = turni.Where(t => t.ClinicaId == c.Id).ToList();
            var ore = sedeTurni.Sum(t => Math.Max(0, (t.OraFine - t.OraInizio).TotalHours));
            var giorniFerieSede = ferieApprovate
                .Where(r => r.ClinicaId == c.Id)
                .Sum(r => DaysInMonth(r.DataInizio, r.DataFine, primoDelMese, primoMeseSucc));
            return new ClinicaPresenzeRow(c.Id, c.Nome, (int)Math.Round(ore), sedeTurni.Count, giorniFerieSede);
        }).ToList();

        // ── Costo personale ──
        var costoRows = cliniche.Select(c =>
        {
            var dipSede = attivi.Where(d => d.ClinicaId == c.Id).ToList();
            decimal costo = 0;
            foreach (var d in dipSede)
            {
                var ct = contratti.Where(x => x.DipendenteId == d.Id
                                              && x.DataInizio <= primoMeseSucc
                                              && (x.DataFine is null || x.DataFine >= primoDelMese))
                                  .OrderByDescending(x => x.DataInizio).FirstOrDefault();
                costo += ct?.RetribuzioneMensileLorda ?? RuoloFallback(d.Ruolo);
            }
            return new ClinicaCostoRow(c.Id, c.Nome, Math.Round(costo, 0), dipSede.Count);
        }).ToList();

        // ── Compliance index ──
        var visite = await _mongo.VisiteMediche.Find(v => v.TenantId == tid).ToListAsync();
        var corsi = await _mongo.Corsi.Find(c => c.TenantId == tid).ToListAsync();
        var docs = await _mongo.DocumentiClinica.Find(d => d.TenantId == tid).ToListAsync();

        var compliance = cliniche.Select(c =>
        {
            var dipSede = attivi.Where(d => d.ClinicaId == c.Id).Select(d => d.Id).ToHashSet();

            var visSede = visite.Where(v => dipSede.Contains(v.DipendenteId)).ToList();
            var visScadute = visSede.Count(v => v.ScadenzaIdoneita is not null && v.ScadenzaIdoneita < DateTime.UtcNow);
            var visOk = visSede.Count - visScadute;

            var corsiSede = corsi.Where(co => co.DestinatarioTipo == DestinatarioCorso.Dipendente && dipSede.Contains(co.DestinatarioId)).ToList();
            var corsiScaduti = corsiSede.Count(co => co.Scadenza is not null && co.Scadenza < DateTime.UtcNow);
            var corsiOk = corsiSede.Count - corsiScaduti;

            var docsSede = docs.Where(d => d.ClinicaId == c.Id).ToList();
            var docsScaduti = docsSede.Count(d => d.DataScadenza is not null && d.DataScadenza < DateTime.UtcNow);
            var docsOk = docsSede.Count - docsScaduti;

            var totale = visSede.Count + corsiSede.Count + docsSede.Count;
            var ok = visOk + corsiOk + docsOk;
            var punteggio = totale == 0 ? 100 : (int)Math.Round(ok * 100.0 / totale);

            return new SedeComplianceRow(c.Id, c.Nome, punteggio, visOk, visScadute, corsiOk, corsiScaduti, docsOk, docsScaduti);
        }).ToList();

        // ── Turnover periodo (12 mesi) ──
        var assunti12 = dipendenti.Count(d => d.DataAssunzione >= dodiciMesiFa);
        var cessati12 = dipendenti.Count(d => d.DataDimissioni is not null && d.DataDimissioni >= dodiciMesiFa);
        var medio = (attivi.Count + cessati12) / 2.0;
        decimal? pct = medio > 0 ? Math.Round((decimal)(cessati12 / medio) * 100m, 1) : null;

        ViewData["Section"] = "report";
        return View(new ReportIndexViewModel
        {
            Mese = primoDelMese,
            PresenzeMese = presenze,
            CostoPersonale = costoRows,
            ComplianceIndex = compliance,
            Turnover = new TurnoverPeriodo(assunti12, cessati12, pct)
        });
    }

    [HttpGet("presenze.csv")]
    public async Task<IActionResult> PresenzeCsv(DateTime? mese = null)
    {
        var vm = (await Index(mese)) as ViewResult;
        var model = vm?.Model as ReportIndexViewModel ?? new ReportIndexViewModel();
        var sb = new StringBuilder();
        sb.Append('﻿');
        sb.AppendLine($"Mese:;{model.Mese:yyyy-MM}");
        sb.AppendLine("Sede;OreLavorate;Turni;GiorniFerieApprovate");
        foreach (var r in model.PresenzeMese)
            sb.AppendLine($"{Csv(r.ClinicaNome)};{r.OreLavorate};{r.Turni};{r.GiorniFerie}");
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", $"presenze-{model.Mese:yyyy-MM}.csv");
    }

    [HttpGet("costo-personale.csv")]
    public async Task<IActionResult> CostoCsv(DateTime? mese = null)
    {
        var vm = (await Index(mese)) as ViewResult;
        var model = vm?.Model as ReportIndexViewModel ?? new ReportIndexViewModel();
        var sb = new StringBuilder();
        sb.Append('﻿');
        sb.AppendLine($"Mese:;{model.Mese:yyyy-MM}");
        sb.AppendLine("Sede;Dipendenti;CostoMensileLordo");
        foreach (var r in model.CostoPersonale)
            sb.AppendLine($"{Csv(r.ClinicaNome)};{r.Dipendenti};{r.CostoMensileLordo.ToString(CultureInfo.InvariantCulture)}");
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", $"costo-personale-{model.Mese:yyyy-MM}.csv");
    }

    [HttpGet("compliance.csv")]
    public async Task<IActionResult> ComplianceCsv()
    {
        var vm = (await Index(null)) as ViewResult;
        var model = vm?.Model as ReportIndexViewModel ?? new ReportIndexViewModel();
        var sb = new StringBuilder();
        sb.Append('﻿');
        sb.AppendLine("Sede;PunteggioCompliance;VisiteOk;VisiteScadute;CorsiOk;CorsiScaduti;DocOk;DocScaduti");
        foreach (var r in model.ComplianceIndex)
            sb.AppendLine($"{Csv(r.ClinicaNome)};{r.Punteggio100};{r.VisiteOk};{r.VisiteScadute};{r.CorsiOk};{r.CorsiScaduti};{r.DocsOk};{r.DocsScaduti}");
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", $"compliance-{DateTime.UtcNow:yyyy-MM-dd}.csv");
    }

    private static int DaysInMonth(DateTime inizio, DateTime fine, DateTime monthStart, DateTime monthEnd)
    {
        var s = inizio < monthStart ? monthStart : inizio;
        var e = fine >= monthEnd ? monthEnd.AddDays(-1) : fine;
        if (e < s) return 0;
        var d = 0;
        for (var x = s.Date; x <= e.Date; x = x.AddDays(1))
            if (x.DayOfWeek != DayOfWeek.Saturday && x.DayOfWeek != DayOfWeek.Sunday) d++;
        return d;
    }

    private static decimal RuoloFallback(RuoloDipendente r) => r switch
    {
        RuoloDipendente.ASO            => 1700m,
        RuoloDipendente.Igienista      => 2100m,
        RuoloDipendente.Segreteria     => 1800m,
        RuoloDipendente.ResponsabileSede => 2800m,
        RuoloDipendente.Amministrazione => 2200m,
        RuoloDipendente.Direzione      => 4000m,
        RuoloDipendente.Marketing      => 2300m,
        RuoloDipendente.IT             => 2600m,
        _ => 1900m
    };

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(';') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
