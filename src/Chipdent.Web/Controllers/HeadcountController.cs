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
[Route("headcount")]
public class HeadcountController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;

    public HeadcountController(MongoContext mongo, ITenantContext tenant)
    {
        _mongo = mongo;
        _tenant = tenant;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var tid = _tenant.TenantId!;
        var dipendenti = await _mongo.Dipendenti.Find(d => d.TenantId == tid).ToListAsync();
        var cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).SortBy(c => c.Nome).ToListAsync();
        var contratti = await _mongo.Contratti.Find(c => c.TenantId == tid).ToListAsync();

        var attivi = dipendenti.Where(d => d.Stato != StatoDipendente.Cessato).ToList();
        var oggi = DateTime.UtcNow;
        var dodiciMesiFa = oggi.AddMonths(-12);

        var assunti12m = dipendenti.Count(d => d.DataAssunzione >= dodiciMesiFa);
        var cessati12m = dipendenti.Count(d => d.DataDimissioni is not null && d.DataDimissioni >= dodiciMesiFa);
        var medio = (attivi.Count + cessati12m) / 2.0;
        var turnover = medio > 0 ? Math.Round((decimal)(cessati12m / medio) * 100m, 1) : (decimal?)null;

        // Stima costo: somma delle retribuzioni mensili dei contratti attivi del personale attivo,
        // o in fallback un'euristica per ruolo (CCNL grezzo) se manca il dato del contratto.
        var costoMensile = 0m;
        var attiviIds = attivi.Select(a => a.Id).ToHashSet();
        var contrattiAttiviPerDip = contratti
            .Where(c => attiviIds.Contains(c.DipendenteId)
                        && c.DataInizio <= oggi
                        && (c.DataFine is null || c.DataFine >= oggi))
            .GroupBy(c => c.DipendenteId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.DataInizio).First());

        foreach (var d in attivi)
        {
            if (contrattiAttiviPerDip.TryGetValue(d.Id, out var ct) && ct.RetribuzioneMensileLorda is { } r)
            {
                costoMensile += r;
            }
            else
            {
                // Stima euristica per ruolo (lordo mensile, indicativo).
                costoMensile += d.Ruolo switch
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
            }
        }

        var perSede = cliniche.Select(c =>
        {
            var attiviSede = attivi.Where(d => d.ClinicaId == c.Id).ToList();
            return new HeadcountSede(c.Id, c.Nome,
                Effettivi: attiviSede.Count(d => d.Stato == StatoDipendente.Attivo || d.Stato == StatoDipendente.InFerie || d.Stato == StatoDipendente.InMalattia),
                Target: c.OrganicoTarget ?? 0,
                Onboarding: attiviSede.Count(d => d.Stato == StatoDipendente.Onboarding));
        }).ToList();

        var perRuolo = attivi
            .GroupBy(d => d.Ruolo)
            .Select(g => new HeadcountRuolo(g.Key, g.Count()))
            .OrderByDescending(x => x.Conteggio).ToList();

        var perContratto = attivi
            .GroupBy(d => d.TipoContratto)
            .Select(g => new HeadcountContratto(g.Key, g.Count()))
            .OrderByDescending(x => x.Conteggio).ToList();

        // Trend ultimi 12 mesi: per ogni mese conta assunti/cessati e organico cumulato a fine mese.
        var trend = new List<HeadcountTrendMese>();
        var organicoAlMomento = dipendenti.Count(d =>
            d.DataAssunzione <= dodiciMesiFa
            && (d.DataDimissioni is null || d.DataDimissioni > dodiciMesiFa));
        for (var i = 11; i >= 0; i--)
        {
            var mese = new DateTime(oggi.Year, oggi.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-i);
            var fineMese = mese.AddMonths(1);
            var assuntiMese = dipendenti.Count(d => d.DataAssunzione >= mese && d.DataAssunzione < fineMese);
            var cessatiMese = dipendenti.Count(d => d.DataDimissioni is not null && d.DataDimissioni >= mese && d.DataDimissioni < fineMese);
            organicoAlMomento += assuntiMese - cessatiMese;
            trend.Add(new HeadcountTrendMese(mese, assuntiMese, cessatiMese, organicoAlMomento));
        }

        var target = cliniche
            .Where(c => c.OrganicoTarget.HasValue)
            .Select(c =>
            {
                var eff = attivi.Count(d => d.ClinicaId == c.Id && d.Stato != StatoDipendente.Cessato);
                return new TargetSede(c.Id, c.Nome, eff, c.OrganicoTarget!.Value);
            })
            .OrderBy(t => t.ClinicaNome).ToList();

        ViewData["Section"] = "headcount";
        return View(new HeadcountViewModel
        {
            OrganicoTotale = attivi.Count,
            InOnboarding = attivi.Count(d => d.Stato == StatoDipendente.Onboarding),
            Cessati12m = cessati12m,
            Assunti12m = assunti12m,
            TurnoverPercentuale = turnover,
            CostoStimatoMensile = Math.Round(costoMensile, 0),
            PerSede = perSede,
            PerRuolo = perRuolo,
            PerTipoContratto = perContratto,
            Trend12Mesi = trend,
            Target = target
        });
    }

    [HttpGet("export.csv")]
    public async Task<IActionResult> ExportCsv()
    {
        var tid = _tenant.TenantId!;
        var dipendenti = await _mongo.Dipendenti.Find(d => d.TenantId == tid).ToListAsync();
        var cliniche = (await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync())
            .ToDictionary(c => c.Id, c => c.Nome);

        var sb = new StringBuilder();
        sb.Append('﻿');
        sb.AppendLine("Cognome;Nome;Email;CodiceFiscale;Sede;Ruolo;TipoContratto;Stato;DataAssunzione;DataDimissioni;FerieResidui");
        foreach (var d in dipendenti.OrderBy(x => x.Cognome).ThenBy(x => x.Nome))
        {
            sb.Append(Csv(d.Cognome)).Append(';')
              .Append(Csv(d.Nome)).Append(';')
              .Append(Csv(d.Email)).Append(';')
              .Append(Csv(d.CodiceFiscale)).Append(';')
              .Append(Csv(cliniche.GetValueOrDefault(d.ClinicaId, "—"))).Append(';')
              .Append(d.Ruolo).Append(';')
              .Append(d.TipoContratto).Append(';')
              .Append(d.Stato).Append(';')
              .Append(d.DataAssunzione.ToString("yyyy-MM-dd")).Append(';')
              .Append(d.DataDimissioni?.ToString("yyyy-MM-dd") ?? "").Append(';')
              .Append(d.GiorniFerieResidui.ToString(CultureInfo.InvariantCulture))
              .AppendLine();
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", $"organico-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(';') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
