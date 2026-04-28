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

[Authorize(Policy = Policies.RequireBackoffice)]
[Route("formazione")]
public class FormazioneController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;

    public FormazioneController(MongoContext mongo, ITenantContext tenant)
    {
        _mongo = mongo;
        _tenant = tenant;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var tid = _tenant.TenantId!;
        var dottori = await _mongo.Dottori.Find(d => d.TenantId == tid && d.Attivo)
                                          .SortBy(d => d.Cognome).ToListAsync();
        var rows = dottori.Select(d => new DottoreEcmRow(
            d,
            d.CreditiEcmTriennio,
            d.CreditiEcmRichiestiTriennio,
            d.AnnoFineTriennioEcm,
            ComputeStato(d))).ToList();

        ViewData["Section"] = "formazione";
        return View(new FormazioneIndexViewModel
        {
            Dottori = rows,
            InRegola = rows.Count(r => r.Stato == EcmStato.InRegola),
            InRitardo = rows.Count(r => r.Stato == EcmStato.InRitardo),
            Critici = rows.Count(r => r.Stato == EcmStato.Critico)
        });
    }

    [HttpPost("{id}/aggiorna")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateEcm(string id, int crediti, int? annoFine, int? richiesti)
    {
        var update = Builders<Dottore>.Update
            .Set(x => x.CreditiEcmTriennio, Math.Max(0, crediti))
            .Set(x => x.AnnoFineTriennioEcm, annoFine)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);
        if (richiesti.HasValue && richiesti.Value > 0)
            update = update.Set(x => x.CreditiEcmRichiestiTriennio, richiesti.Value);
        await _mongo.Dottori.UpdateOneAsync(d => d.Id == id && d.TenantId == _tenant.TenantId, update);
        TempData["flash"] = "Dati ECM aggiornati.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("ecm.csv")]
    public async Task<IActionResult> ExportCsv()
    {
        var tid = _tenant.TenantId!;
        var dottori = await _mongo.Dottori.Find(d => d.TenantId == tid)
                                          .SortBy(d => d.Cognome).ToListAsync();
        var sb = new StringBuilder();
        sb.Append('﻿');
        sb.AppendLine("Cognome;Nome;Albo;ScadenzaAlbo;CreditiEcm;CreditiRichiesti;AnnoFineTriennio;Stato");
        foreach (var d in dottori)
        {
            sb.Append(Csv(d.Cognome)).Append(';')
              .Append(Csv(d.Nome)).Append(';')
              .Append(Csv(d.NumeroAlbo)).Append(';')
              .Append(d.ScadenzaAlbo?.ToString("yyyy-MM-dd") ?? "").Append(';')
              .Append(d.CreditiEcmTriennio).Append(';')
              .Append(d.CreditiEcmRichiestiTriennio).Append(';')
              .Append(d.AnnoFineTriennioEcm?.ToString(CultureInfo.InvariantCulture) ?? "").Append(';')
              .Append(ComputeStato(d))
              .AppendLine();
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", $"ecm-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private static EcmStato ComputeStato(Dottore d)
    {
        if (d.AnnoFineTriennioEcm is null) return EcmStato.NonConfigurato;

        var oggi = DateTime.UtcNow;
        var inizioTriennio = new DateTime(d.AnnoFineTriennioEcm.Value - 2, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var fineTriennio = new DateTime(d.AnnoFineTriennioEcm.Value, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        var totaleGiorni = Math.Max(1, (fineTriennio - inizioTriennio).TotalDays);
        var trascorsi = Math.Max(0, Math.Min(totaleGiorni, (oggi - inizioTriennio).TotalDays));
        var attesoOra = d.CreditiEcmRichiestiTriennio * (trascorsi / totaleGiorni);

        if (d.CreditiEcmTriennio >= d.CreditiEcmRichiestiTriennio) return EcmStato.InRegola;
        if (d.CreditiEcmTriennio >= attesoOra) return EcmStato.InRegola;
        // < 50% di quanto atteso a questo punto del triennio = critico
        if (d.CreditiEcmTriennio < attesoOra * 0.5) return EcmStato.Critico;
        return EcmStato.InRitardo;
    }

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(';') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
