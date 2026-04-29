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
/// Scadenziario unificato: aggrega TUTTE le scadenze del tenant in un'unica vista cross-modulo.
/// Calcola un "score di urgenza" per ordinamento (impatto × prossimità) — niente più scadenze sparse
/// in 8 pagine diverse, una sola lista da bonificare.
/// </summary>
[Authorize(Policy = Policies.RequireBackoffice)]
[Route("scadenziario")]
public class ScadenziarioController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;

    public ScadenziarioController(MongoContext mongo, ITenantContext tenant)
    {
        _mongo = mongo;
        _tenant = tenant;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(int orizzonte = 90)
    {
        var tid = _tenant.TenantId!;
        orizzonte = Math.Clamp(orizzonte, 30, 365);
        var oggi = DateTime.UtcNow.Date;
        var limite = oggi.AddDays(orizzonte);

        var scadenze = new List<RigaScadenza>();

        // ── Visite mediche (idoneità lavoratori) ──
        var visite = await _mongo.VisiteMediche.Find(v => v.TenantId == tid && v.ScadenzaIdoneita != null && v.ScadenzaIdoneita <= limite).ToListAsync();
        var dipMap = (await _mongo.Dipendenti.Find(d => d.TenantId == tid).ToListAsync()).ToDictionary(d => d.Id, d => $"{d.Nome} {d.Cognome}");
        foreach (var v in visite)
        {
            scadenze.Add(new RigaScadenza("Visita medica", dipMap.GetValueOrDefault(v.DipendenteId, "—"), v.ScadenzaIdoneita!.Value, ImpattoScadenza.Alto, "/rls/visite"));
        }

        // ── Corsi sicurezza/ECM ──
        var corsi = await _mongo.Corsi.Find(c => c.TenantId == tid && c.Scadenza != null && c.Scadenza <= limite).ToListAsync();
        var dottMap = (await _mongo.Dottori.Find(d => d.TenantId == tid).ToListAsync()).ToDictionary(d => d.Id, d => $"Dr. {d.Cognome}");
        foreach (var c in corsi)
        {
            var nome = c.DestinatarioTipo == DestinatarioCorso.Dottore
                ? dottMap.GetValueOrDefault(c.DestinatarioId, "—")
                : c.DestinatarioTipo == DestinatarioCorso.Dipendente ? dipMap.GetValueOrDefault(c.DestinatarioId, "—") : "Clinica";
            scadenze.Add(new RigaScadenza($"Corso {c.Tipo}", nome, c.Scadenza!.Value, ImpattoScadenza.Medio, "/rls/corsi"));
        }

        // ── DVR ──
        var dvrs = await _mongo.DVRs.Find(d => d.TenantId == tid && d.ProssimaRevisione != null && d.ProssimaRevisione <= limite).ToListAsync();
        var clinicheMap = (await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync()).ToDictionary(c => c.Id, c => c.Nome);
        foreach (var d in dvrs)
        {
            scadenze.Add(new RigaScadenza("DVR", clinicheMap.GetValueOrDefault(d.ClinicaId, "—"), d.ProssimaRevisione!.Value, ImpattoScadenza.Critico, "/rls/dvr"));
        }

        // ── Documenti clinica (DURC, agibilità, polizze, ecc.) ──
        var docs = await _mongo.DocumentiClinica.Find(d => d.TenantId == tid && d.DataScadenza != null && d.DataScadenza <= limite).ToListAsync();
        foreach (var d in docs)
        {
            scadenze.Add(new RigaScadenza($"Documento {d.Tipo}", clinicheMap.GetValueOrDefault(d.ClinicaId, "—"), d.DataScadenza!.Value, ImpattoScadenza.Alto, "/documentazione"));
        }

        // ── Albo dottori ──
        var dottoriScad = await _mongo.Dottori.Find(d => d.TenantId == tid && d.ScadenzaAlbo != null && d.ScadenzaAlbo <= limite).ToListAsync();
        foreach (var d in dottoriScad)
        {
            scadenze.Add(new RigaScadenza("Iscrizione albo", $"Dr. {d.Cognome} {d.Nome}", d.ScadenzaAlbo!.Value, ImpattoScadenza.Critico, $"/dottori/{d.Id}"));
        }

        // ── Contratti dipendenti ──
        var contratti = await _mongo.Contratti.Find(c => c.TenantId == tid && c.DataFine != null && c.DataFine <= limite).ToListAsync();
        foreach (var c in contratti)
        {
            scadenze.Add(new RigaScadenza($"Contratto {c.Tipo}", dipMap.GetValueOrDefault(c.DipendenteId, "—"), c.DataFine!.Value, ImpattoScadenza.Alto, $"/contratti"));
        }

        // ── Score impatto × urgenza ──
        // urgenza: scaduto = 1.5, ≤7gg = 1.2, ≤30gg = 1.0, ≤60gg = 0.7, ≤90gg = 0.5
        // impatto: Critico = 100, Alto = 70, Medio = 40, Basso = 20
        scadenze = scadenze.Select(s => s with
        {
            ScoreUrgenza = (int)Math.Round(ImpattoBase(s.Impatto) * UrgenzaCoeff(s.Data, oggi))
        }).OrderByDescending(s => s.ScoreUrgenza).ThenBy(s => s.Data).ToList();

        ViewData["Section"] = "scadenziario";
        return View(new ScadenziarioViewModel
        {
            Scadenze = scadenze,
            Orizzonte = orizzonte,
            Scadute = scadenze.Count(s => s.Data < oggi),
            Imminenti = scadenze.Count(s => s.Data >= oggi && s.Data <= oggi.AddDays(30)),
            Critiche = scadenze.Count(s => s.Impatto == ImpattoScadenza.Critico)
        });
    }

    private static int ImpattoBase(ImpattoScadenza i) => i switch
    {
        ImpattoScadenza.Critico => 100,
        ImpattoScadenza.Alto    => 70,
        ImpattoScadenza.Medio   => 40,
        _ => 20
    };

    private static double UrgenzaCoeff(DateTime data, DateTime oggi)
    {
        var gg = (data.Date - oggi).TotalDays;
        return gg switch
        {
            <  0 => 1.5,
            <= 7 => 1.2,
            <= 30 => 1.0,
            <= 60 => 0.7,
            _    => 0.5
        };
    }
}
