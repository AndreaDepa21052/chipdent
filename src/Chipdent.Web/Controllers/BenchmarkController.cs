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
[Route("benchmark")]
public class BenchmarkController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;

    public BenchmarkController(MongoContext mongo, ITenantContext tenant)
    {
        _mongo = mongo;
        _tenant = tenant;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var tid = _tenant.TenantId!;
        var oggi = DateTime.UtcNow.Date;
        var settimanaInizio = StartOfWeek(oggi);
        var settimanaFine = settimanaInizio.AddDays(7);
        var trentaGg = oggi.AddDays(-30);

        var cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).SortBy(c => c.Nome).ToListAsync();
        var dipendenti = await _mongo.Dipendenti.Find(d => d.TenantId == tid && d.Stato != StatoDipendente.Cessato).ToListAsync();
        var contratti = await _mongo.Contratti.Find(c => c.TenantId == tid).ToListAsync();
        var visite = await _mongo.VisiteMediche.Find(v => v.TenantId == tid).ToListAsync();
        var corsi = await _mongo.Corsi.Find(c => c.TenantId == tid && c.DestinatarioTipo == DestinatarioCorso.Dipendente).ToListAsync();
        var docs = await _mongo.DocumentiClinica.Find(d => d.TenantId == tid).ToListAsync();
        var turniSettimana = await _mongo.Turni.Find(t => t.TenantId == tid && t.Data >= settimanaInizio && t.Data < settimanaFine).ToListAsync();
        var ferieAttese = await _mongo.RichiesteFerie.Find(r => r.TenantId == tid && r.Stato == StatoRichiestaFerie.InAttesa).ToListAsync();
        var segnAperte = await _mongo.Segnalazioni.Find(s => s.TenantId == tid
            && (s.Stato == StatoSegnalazione.Aperta || s.Stato == StatoSegnalazione.InLavorazione)).ToListAsync();
        var timbrature = await _mongo.Timbrature.Find(t => t.TenantId == tid && t.Timestamp >= trentaGg && t.Tipo == TipoTimbratura.CheckIn).ToListAsync();
        var turniRif30 = await _mongo.Turni.Find(t => t.TenantId == tid && t.Data >= trentaGg && t.Data <= oggi).ToListAsync();

        var sedi = new List<SedeKpiSet>();
        foreach (var c in cliniche)
        {
            var dipSede = dipendenti.Where(d => d.ClinicaId == c.Id).ToList();
            var dipIds = dipSede.Select(d => d.Id).ToHashSet();

            // Costo
            var costo = 0m;
            foreach (var d in dipSede)
            {
                var ct = contratti.Where(x => x.DipendenteId == d.Id
                                              && x.DataInizio <= oggi
                                              && (x.DataFine is null || x.DataFine >= oggi))
                                  .OrderByDescending(x => x.DataInizio).FirstOrDefault();
                costo += ct?.RetribuzioneMensileLorda ?? RuoloFallback(d.Ruolo);
            }

            // Compliance index
            var visSede = visite.Where(v => dipIds.Contains(v.DipendenteId)).ToList();
            var corsiSede = corsi.Where(co => dipIds.Contains(co.DestinatarioId)).ToList();
            var docsSede = docs.Where(d => d.ClinicaId == c.Id).ToList();
            var ok = visSede.Count(v => v.ScadenzaIdoneita is null || v.ScadenzaIdoneita >= oggi)
                   + corsiSede.Count(co => co.Scadenza is null || co.Scadenza >= oggi)
                   + docsSede.Count(d => d.DataScadenza is null || d.DataScadenza >= oggi);
            var totale = visSede.Count + corsiSede.Count + docsSede.Count;
            var compliance = totale == 0 ? 100 : (int)Math.Round(ok * 100.0 / totale);

            // Turni settimana
            var turniSede = turniSettimana.Count(t => t.ClinicaId == c.Id);

            // Ferie pendenti
            var ferieSede = ferieAttese.Count(r => r.ClinicaId == c.Id);

            // Segnalazioni aperte
            var segSede = segnAperte.Count(s => s.ClinicaId == c.Id);

            // Ritardi 30 giorni: timbrature dopo l'inizio turno per dipendenti della sede
            var ritardi = 0;
            foreach (var t in timbrature.Where(x => dipIds.Contains(x.DipendenteId)))
            {
                var turno = turniRif30.FirstOrDefault(tu => tu.PersonaId == t.DipendenteId && tu.Data.Date == t.Timestamp.Date);
                if (turno is null) continue;
                var atteso = turno.Data.Date.Add(turno.OraInizio);
                if (t.Timestamp > atteso.AddMinutes(10)) ritardi++;
            }

            // Overall score (0-100): media pesata
            // 30% compliance · 25% (effettivi/target) · 20% inverso costo · 15% inverso ritardi · 10% inverso segnalazioni
            var coperturaTargetPct = c.OrganicoTarget is { } t1 && t1 > 0
                ? Math.Min(100, (int)Math.Round(dipSede.Count * 100.0 / t1))
                : 100;

            sedi.Add(new SedeKpiSet(
                ClinicaId: c.Id,
                ClinicaNome: c.Nome,
                Citta: c.Citta,
                Effettivi: dipSede.Count,
                Target: c.OrganicoTarget ?? 0,
                CostoMensile: Math.Round(costo, 0),
                CompliancePercento: compliance,
                TurniSettimana: turniSede,
                RichiesteFerieAttese: ferieSede,
                SegnalazioniAperte: segSede,
                RitardiUltimi30g: ritardi,
                OverallScore: 0));
        }

        // Calcola overall score relativo (rispetto alla media del tenant)
        if (sedi.Any())
        {
            var avgCosto = sedi.Average(s => (double)s.CostoMensile);
            var avgCompl = sedi.Average(s => s.CompliancePercento);
            var avgRit = sedi.Average(s => s.RitardiUltimi30g);
            var avgSeg = sedi.Average(s => s.SegnalazioniAperte);

            sedi = sedi.Select(s =>
            {
                var coperturaPct = s.Target > 0 ? Math.Min(100, (int)Math.Round(s.Effettivi * 100.0 / s.Target)) : 100;
                var costoNorm = avgCosto > 0 ? (int)Math.Round(50 + (avgCosto - (double)s.CostoMensile) / avgCosto * 50) : 50;
                var ritNorm = avgRit > 0 ? (int)Math.Round(50 + (avgRit - s.RitardiUltimi30g) / Math.Max(1, avgRit) * 50) : 50;
                var segNorm = avgSeg > 0 ? (int)Math.Round(50 + (avgSeg - s.SegnalazioniAperte) / Math.Max(1, avgSeg) * 50) : 50;
                var overall = (int)Math.Round(0.30 * s.CompliancePercento
                                              + 0.25 * coperturaPct
                                              + 0.20 * costoNorm
                                              + 0.15 * ritNorm
                                              + 0.10 * segNorm);
                overall = Math.Max(0, Math.Min(100, overall));
                return s with { OverallScore = overall };
            }).ToList();
        }

        var benchmarks = new KpiBenchmarks
        {
            MediaCostoMensile = sedi.Any() ? Math.Round(sedi.Average(s => s.CostoMensile), 0) : 0,
            MediaCompliance = sedi.Any() ? (decimal)Math.Round(sedi.Average(s => s.CompliancePercento), 1) : 0,
            MediaCoperturaTarget = sedi.Any(s => s.Target > 0)
                ? (decimal)Math.Round(sedi.Where(s => s.Target > 0).Average(s => s.Effettivi * 100.0 / s.Target), 1)
                : 0,
            MediaRitardi = sedi.Any() ? (decimal)Math.Round(sedi.Average(s => s.RitardiUltimi30g), 1) : 0
        };

        var top = sedi.OrderByDescending(s => s.OverallScore).Take(3)
            .Select(s => new SedeRanking(s.ClinicaNome, s.OverallScore, RankReason(s, leader: true))).ToList();
        var bottom = sedi.OrderBy(s => s.OverallScore).Take(3)
            .Select(s => new SedeRanking(s.ClinicaNome, s.OverallScore, RankReason(s, leader: false))).ToList();

        ViewData["Section"] = "benchmark";
        return View(new BenchmarkViewModel
        {
            Sedi = sedi.OrderByDescending(s => s.OverallScore).ToList(),
            Benchmarks = benchmarks,
            Top = top,
            Bottom = bottom,
            CalcolatoIl = DateTime.UtcNow
        });
    }

    [HttpGet("export.csv")]
    public async Task<IActionResult> ExportCsv()
    {
        var vm = (await Index()) as ViewResult;
        var model = vm?.Model as BenchmarkViewModel ?? new BenchmarkViewModel();
        var sb = new StringBuilder();
        sb.Append('﻿');
        sb.AppendLine("Sede;Citta;Effettivi;Target;CostoMensile;Compliance%;TurniSettimana;FeriePendenti;SegnalazioniAperte;Ritardi30g;OverallScore");
        foreach (var s in model.Sedi)
        {
            sb.Append(Csv(s.ClinicaNome)).Append(';')
              .Append(Csv(s.Citta)).Append(';')
              .Append(s.Effettivi).Append(';')
              .Append(s.Target).Append(';')
              .Append(s.CostoMensile.ToString(CultureInfo.InvariantCulture)).Append(';')
              .Append(s.CompliancePercento).Append(';')
              .Append(s.TurniSettimana).Append(';')
              .Append(s.RichiesteFerieAttese).Append(';')
              .Append(s.SegnalazioniAperte).Append(';')
              .Append(s.RitardiUltimi30g).Append(';')
              .Append(s.OverallScore)
              .AppendLine();
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", $"benchmark-sedi-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private static string RankReason(SedeKpiSet s, bool leader)
    {
        if (leader)
        {
            if (s.CompliancePercento >= 95) return $"compliance al {s.CompliancePercento}%";
            if (s.RitardiUltimi30g <= 1) return "ritardi quasi assenti";
            if (s.SegnalazioniAperte == 0) return "nessuna segnalazione aperta";
            if (s.Target > 0 && s.Effettivi >= s.Target) return "organico in linea col target";
            return "performance equilibrata sui KPI";
        }
        if (s.CompliancePercento < 70) return $"compliance al {s.CompliancePercento}%";
        if (s.RitardiUltimi30g >= 5) return $"{s.RitardiUltimi30g} ritardi negli ultimi 30g";
        if (s.SegnalazioniAperte >= 3) return $"{s.SegnalazioniAperte} segnalazioni aperte";
        if (s.Target > 0 && s.Effettivi < s.Target) return $"sotto target organico (-{s.Target - s.Effettivi})";
        return "su più KPI sotto media";
    }

    private static decimal RuoloFallback(RuoloDipendente r) => r switch
    {
        RuoloDipendente.ASO => 1700m,
        RuoloDipendente.Igienista => 2100m,
        RuoloDipendente.Segreteria => 1800m,
        RuoloDipendente.ResponsabileSede => 2800m,
        RuoloDipendente.Amministrazione => 2200m,
        RuoloDipendente.Direzione => 4000m,
        RuoloDipendente.Marketing => 2300m,
        RuoloDipendente.IT => 2600m,
        _ => 1900m
    };

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(';') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var d = date.Date;
        var diff = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return DateTime.SpecifyKind(d.AddDays(-diff), DateTimeKind.Utc);
    }
}
