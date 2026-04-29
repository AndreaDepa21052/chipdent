using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

/// <summary>
/// Bacheca TV per sede: pagina full-screen pensata per uno schermo in sala riposo della clinica.
/// Accesso anonimo via tenant slug + clinicaId (no PII, no anagrafica completa). Auto-refresh realtime via SignalR
/// usando il gruppo "tenant:{tenantId}" già esistente; in fallback la pagina si auto-refresh ogni 60 secondi.
/// </summary>
[AllowAnonymous]
[Route("bacheca")]
public class BachecaController : Controller
{
    private readonly MongoContext _mongo;

    public BachecaController(MongoContext mongo) => _mongo = mongo;

    [HttpGet("{tenantSlug}/{clinicaId}")]
    public async Task<IActionResult> Index(string tenantSlug, string clinicaId)
    {
        var tenant = await _mongo.Tenants.Find(t => t.Slug == tenantSlug && t.IsActive).FirstOrDefaultAsync();
        if (tenant is null) return NotFound();

        var clinica = await _mongo.Cliniche.Find(c => c.Id == clinicaId && c.TenantId == tenant.Id).FirstOrDefaultAsync();
        if (clinica is null) return NotFound();

        var oggi = DateTime.Today;
        var domani = oggi.AddDays(1);

        // Turni di oggi su questa sede
        var turni = await _mongo.Turni
            .Find(t => t.TenantId == tenant.Id && t.ClinicaId == clinicaId && t.Data >= oggi && t.Data < domani)
            .SortBy(t => t.OraInizio).ToListAsync();

        // Risolvo nomi persone (senza esporre dati sensibili: solo nome + ruolo)
        var dottoriIds = turni.Where(t => t.TipoPersona == TipoPersona.Dottore).Select(t => t.PersonaId).Distinct().ToList();
        var dipIds     = turni.Where(t => t.TipoPersona == TipoPersona.Dipendente).Select(t => t.PersonaId).Distinct().ToList();
        var dottori = (await _mongo.Dottori.Find(d => dottoriIds.Contains(d.Id)).ToListAsync()).ToDictionary(d => d.Id);
        var dipendenti = (await _mongo.Dipendenti.Find(d => dipIds.Contains(d.Id)).ToListAsync()).ToDictionary(d => d.Id);

        var righe = turni.Select(t => new BachecaTurnoRow(
            From: t.OraInizio,
            To:   t.OraFine,
            Nome: t.TipoPersona == TipoPersona.Dottore
                ? (dottori.TryGetValue(t.PersonaId, out var d) ? $"Dr. {d.Cognome}" : "—")
                : (dipendenti.TryGetValue(t.PersonaId, out var dp) ? $"{dp.Nome} {dp.Cognome}" : "—"),
            Ruolo: t.TipoPersona == TipoPersona.Dottore
                ? (dottori.TryGetValue(t.PersonaId, out var d2) ? d2.Specializzazione : "Dottore")
                : (dipendenti.TryGetValue(t.PersonaId, out var dp2) ? dp2.Ruolo.ToString() : "Staff"))).ToList();

        // Comunicazioni recenti pubbliche (categorie sicure: Annuncio + UrgenzaOperativa) della sede o tenant-wide
        var sinceDate = DateTime.UtcNow.AddDays(-7);
        var comunicazioni = await _mongo.Comunicazioni.Find(c =>
                c.TenantId == tenant.Id
                && c.CreatedAt >= sinceDate
                && (c.ClinicaId == null || c.ClinicaId == clinicaId)
                && (c.Categoria == CategoriaComunicazione.Annuncio || c.Categoria == CategoriaComunicazione.UrgenzaOperativa))
            .SortByDescending(c => c.CreatedAt).Limit(5).ToListAsync();

        return View(new BachecaViewModel
        {
            TenantId = tenant.Id,
            TenantSlug = tenant.Slug,
            TenantNome = tenant.DisplayName,
            ClinicaId = clinicaId,
            ClinicaNome = clinica.Nome,
            ClinicaCitta = clinica.Citta,
            Turni = righe,
            Comunicazioni = comunicazioni.Select(c => new BachecaComunicazioneRow(
                Categoria: c.Categoria.ToString(),
                Oggetto: c.Oggetto,
                Anteprima: c.Corpo.Length > 200 ? c.Corpo[..200] + "…" : c.Corpo,
                Quando: c.CreatedAt)).ToList()
        });
    }
}
