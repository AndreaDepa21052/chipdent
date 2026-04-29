using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Hubs;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

/// <summary>
/// Feedback NPS pazienti.
/// - Endpoint pubblico anonimo /feedback/{tenantSlug}/{clinicaId}: form a 11 button con commento facoltativo.
/// - Dashboard /feedback (aggregata per sede/dottore) accessibile solo a Management/Direttore.
/// </summary>
public class FeedbackController : Controller
{
    private static readonly string[] ParoleCritiche =
    {
        "dolore", "scarso", "pessimo", "lamentela", "reclamo", "scortese", "maleducato",
        "sporco", "attesa", "ritardo", "non torno", "non tornerò", "denuncia"
    };

    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly INotificationPublisher _publisher;

    public FeedbackController(MongoContext mongo, ITenantContext tenant, INotificationPublisher publisher)
    {
        _mongo = mongo;
        _tenant = tenant;
        _publisher = publisher;
    }

    // ─── Form pubblico anonimo ───────────────────────────────────────

    [AllowAnonymous]
    [HttpGet("/feedback/{tenantSlug}/{clinicaId}")]
    public async Task<IActionResult> Submit(string tenantSlug, string clinicaId)
    {
        var t = await _mongo.Tenants.Find(x => x.Slug == tenantSlug && x.IsActive).FirstOrDefaultAsync();
        if (t is null) return NotFound();
        var c = await _mongo.Cliniche.Find(x => x.Id == clinicaId && x.TenantId == t.Id).FirstOrDefaultAsync();
        if (c is null) return NotFound();

        var dottori = await _mongo.Dottori.Find(d => d.TenantId == t.Id && d.Attivo).SortBy(d => d.Cognome).ToListAsync();

        return View(new FeedbackSubmitViewModel
        {
            TenantId = t.Id,
            TenantSlug = t.Slug,
            TenantNome = t.DisplayName,
            ClinicaId = clinicaId,
            ClinicaNome = c.Nome,
            Dottori = dottori
        });
    }

    [AllowAnonymous]
    [HttpPost("/feedback/{tenantSlug}/{clinicaId}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(string tenantSlug, string clinicaId, int score, string? dottoreId, string? commento)
    {
        var t = await _mongo.Tenants.Find(x => x.Slug == tenantSlug && x.IsActive).FirstOrDefaultAsync();
        if (t is null) return NotFound();
        var c = await _mongo.Cliniche.Find(x => x.Id == clinicaId && x.TenantId == t.Id).FirstOrDefaultAsync();
        if (c is null) return NotFound();
        if (score < 0 || score > 10) return BadRequest("Punteggio non valido.");

        var commentoTrim = string.IsNullOrWhiteSpace(commento) ? null : commento.Trim();
        var critico = score <= 6 || (commentoTrim is not null && ParoleCritiche.Any(k => commentoTrim.Contains(k, StringComparison.OrdinalIgnoreCase)));

        var fb = new FeedbackPaziente
        {
            TenantId = t.Id,
            ClinicaId = clinicaId,
            DottoreId = string.IsNullOrEmpty(dottoreId) ? null : dottoreId,
            Score = score,
            Commento = commentoTrim,
            DaApprofondire = critico
        };
        await _mongo.FeedbackPazienti.InsertOneAsync(fb);

        // Notifica realtime al management/direttore solo se feedback critico, per follow-up immediato.
        if (critico)
        {
            await _publisher.PublishAsync(t.Id, "activity", new
            {
                kind = "comm",
                title = $"⚠️ Feedback critico ricevuto · {c.Nome}",
                description = $"Voto {score}/10" + (commentoTrim is null ? "" : $" · «{(commentoTrim.Length > 80 ? commentoTrim[..80] + "…" : commentoTrim)}»"),
                when = DateTime.UtcNow
            });
        }

        return View("Grazie", new FeedbackGrazieViewModel { ClinicaNome = c.Nome, Score = score });
    }

    // ─── Dashboard riservata ─────────────────────────────────────────

    [Authorize(Policy = Policies.RequireDirettore)]
    [HttpGet("feedback")]
    public async Task<IActionResult> Index(string? clinicaId = null, int giorni = 90)
    {
        var tid = _tenant.TenantId!;
        var since = DateTime.UtcNow.AddDays(-Math.Clamp(giorni, 7, 365));

        var f = Builders<FeedbackPaziente>.Filter.Eq(x => x.TenantId, tid)
              & Builders<FeedbackPaziente>.Filter.Gte(x => x.CreatedAt, since);
        if (!string.IsNullOrEmpty(clinicaId)) f &= Builders<FeedbackPaziente>.Filter.Eq(x => x.ClinicaId, clinicaId);

        // Direttore vede solo le proprie sedi
        if (_tenant.IsClinicaScoped) f &= Builders<FeedbackPaziente>.Filter.In(x => x.ClinicaId, _tenant.ClinicaIds);

        var feedbacks = await _mongo.FeedbackPazienti.Find(f).SortByDescending(x => x.CreatedAt).ToListAsync();

        var clinicheList = await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync();
        var clinicheMap = clinicheList.ToDictionary(c => c.Id, c => c.Nome);
        var dottoriMap = (await _mongo.Dottori.Find(d => d.TenantId == tid).ToListAsync()).ToDictionary(d => d.Id, d => $"Dr. {d.Cognome}");

        // NPS = (%promotori - %detrattori), dove promotore = 9-10, detrattore = 0-6, passivo = 7-8.
        var nps = NpsScore(feedbacks);

        // NPS per sede
        var perSede = feedbacks.GroupBy(x => x.ClinicaId)
            .Select(g => new FeedbackPerEntita(
                Id: g.Key,
                Nome: clinicheMap.GetValueOrDefault(g.Key, "—"),
                Totale: g.Count(),
                Nps: NpsScore(g.ToList()),
                Media: g.Average(x => (double)x.Score)))
            .OrderByDescending(x => x.Nps).ToList();

        var perDottore = feedbacks.Where(x => !string.IsNullOrEmpty(x.DottoreId))
            .GroupBy(x => x.DottoreId!)
            .Select(g => new FeedbackPerEntita(
                Id: g.Key,
                Nome: dottoriMap.GetValueOrDefault(g.Key, "—"),
                Totale: g.Count(),
                Nps: NpsScore(g.ToList()),
                Media: g.Average(x => (double)x.Score)))
            .OrderByDescending(x => x.Nps).ToList();

        ViewData["Section"] = "feedback";
        return View(new FeedbackDashboardViewModel
        {
            Cliniche = clinicheList,
            ClinicaIdFilter = clinicaId,
            Giorni = giorni,
            Feedbacks = feedbacks.Select(fb => new FeedbackRow(fb,
                clinicheMap.GetValueOrDefault(fb.ClinicaId, "—"),
                fb.DottoreId is null ? null : dottoriMap.GetValueOrDefault(fb.DottoreId))).ToList(),
            NpsTotale = nps,
            MediaTotale = feedbacks.Count == 0 ? 0 : feedbacks.Average(x => (double)x.Score),
            CountTotale = feedbacks.Count,
            CountCritici = feedbacks.Count(x => x.DaApprofondire),
            PerSede = perSede,
            PerDottore = perDottore
        });
    }

    private static int NpsScore(IReadOnlyCollection<FeedbackPaziente> items)
    {
        if (items.Count == 0) return 0;
        var promotori = items.Count(x => x.Score >= 9);
        var detrattori = items.Count(x => x.Score <= 6);
        return (int)Math.Round(100.0 * (promotori - detrattori) / items.Count);
    }
}
