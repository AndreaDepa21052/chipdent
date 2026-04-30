using System.Security.Claims;
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
/// Backlog di prodotto raccolto dalla rete:
/// - tutti gli utenti autenticati possono proporre richieste in agile mode e votare;
/// - solo l'Owner può promuovere a sprint, scartare o cambiare stato.
/// Le richieste sono per-tenant (ogni catena ha il proprio backlog).
/// </summary>
[Authorize]
[Route("backlog")]
public class ProductBacklogController : Controller
{
    private static readonly string[] RuoliSuggeriti =
    {
        "Staff", "Direttore di sede", "Backoffice", "Management", "Dottore", "ASO", "Igienista", "Receptionist"
    };

    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly INotificationPublisher _publisher;

    public ProductBacklogController(MongoContext mongo, ITenantContext tenant, INotificationPublisher publisher)
    {
        _mongo = mongo;
        _tenant = tenant;
        _publisher = publisher;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(StatoBacklog? filter = null, AreaBacklog? area = null, string sort = "voti")
    {
        var tid = _tenant.TenantId!;
        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

        var f = Builders<ProductBacklogItem>.Filter.Eq(x => x.TenantId, tid);
        if (filter.HasValue) f &= Builders<ProductBacklogItem>.Filter.Eq(x => x.Stato, filter.Value);
        if (area.HasValue) f &= Builders<ProductBacklogItem>.Filter.Eq(x => x.Area, area.Value);

        var raw = await _mongo.ProductBacklog.Find(f).ToListAsync();
        var items = sort switch
        {
            "recenti" => raw.OrderByDescending(x => x.CreatedAt).ToList(),
            "impatto" => raw.OrderByDescending(x => x.Impatto).ThenByDescending(x => x.VotantiUserIds.Count).ToList(),
            _ => raw.OrderByDescending(x => x.VotantiUserIds.Count).ThenByDescending(x => x.CreatedAt).ToList()
        };

        // Stat counter per banner
        var statBase = Builders<ProductBacklogItem>.Filter.Eq(x => x.TenantId, tid);
        var totaleProposte = (int)await _mongo.ProductBacklog.CountDocumentsAsync(statBase & Builders<ProductBacklogItem>.Filter.In(x => x.Stato, new[] { StatoBacklog.Proposta, StatoBacklog.InEsame }));
        var totaleInLav = (int)await _mongo.ProductBacklog.CountDocumentsAsync(statBase & Builders<ProductBacklogItem>.Filter.Eq(x => x.Stato, StatoBacklog.InLavorazione));
        var totaleCompletate = (int)await _mongo.ProductBacklog.CountDocumentsAsync(statBase & Builders<ProductBacklogItem>.Filter.Eq(x => x.Stato, StatoBacklog.Completata));
        var allItems = await _mongo.ProductBacklog.Find(statBase).ToListAsync();
        var votiTotali = allItems.Sum(x => x.VotantiUserIds.Count);

        ViewData["Section"] = "product-backlog";
        return View(new ProductBacklogIndexViewModel
        {
            Items = items.Select(i => new ProductBacklogRow(
                i,
                VotatoDaMe: !string.IsNullOrEmpty(meId) && i.VotantiUserIds.Contains(meId),
                MiaProposta: i.AutoreUserId == meId)).ToList(),
            Filter = filter,
            AreaFilter = area,
            Sort = sort,
            IsOwner = User.IsOwner(),
            CurrentUserId = meId,
            TotaleProposte = totaleProposte,
            TotaleInLavorazione = totaleInLav,
            TotaleCompletate = totaleCompletate,
            VotiTotali = votiTotali
        });
    }

    [HttpGet("nuova")]
    public IActionResult Create()
    {
        ViewData["Section"] = "product-backlog";
        var defaultRuolo = User.IsOwner() ? "Owner"
            : User.IsManagement() ? "Management"
            : User.IsDirettore() ? "Direttore di sede"
            : User.IsBackoffice() ? "Backoffice"
            : "Staff";

        return View(new NuovaProductBacklogViewModel
        {
            ComeRuolo = defaultRuolo,
            RuoliSuggeriti = RuoliSuggeriti
        });
    }

    [HttpPost("nuova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NuovaProductBacklogViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "product-backlog";
            vm.RuoliSuggeriti = RuoliSuggeriti;
            return View(vm);
        }

        var tid = _tenant.TenantId!;
        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var meName = User.Identity?.Name ?? "";
        var meRole = User.FindFirst(ClaimTypes.Role)?.Value ?? "";

        var item = new ProductBacklogItem
        {
            TenantId = tid,
            AutoreUserId = meId,
            AutoreNome = meName,
            AutoreRuolo = meRole,
            ComeRuolo = vm.ComeRuolo.Trim(),
            Vorrei = vm.Vorrei.Trim(),
            CosiChe = vm.CosiChe.Trim(),
            Categoria = vm.Categoria,
            Area = vm.Area,
            Impatto = vm.Impatto,
            Commento = string.IsNullOrWhiteSpace(vm.Commento) ? null : vm.Commento.Trim(),
            // L'autore vota automaticamente la propria proposta
            VotantiUserIds = string.IsNullOrEmpty(meId) ? new() : new() { meId },
            Stato = StatoBacklog.Proposta
        };
        await _mongo.ProductBacklog.InsertOneAsync(item);

        await _publisher.PublishAsync(tid, "activity", new
        {
            kind = "comm",
            title = $"Nuova proposta di backlog: {Truncate(vm.Vorrei, 80)}",
            description = $"da {meName} · area {vm.Area} · impatto {vm.Impatto}",
            when = DateTime.UtcNow
        });

        TempData["flash"] = "Richiesta inviata. Gli altri utenti possono ora votarla.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Details(string id)
    {
        var tid = _tenant.TenantId!;
        var item = await _mongo.ProductBacklog.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (item is null) return NotFound();

        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        ViewData["Section"] = "product-backlog";
        ViewData["Crumb"] = "Backlog di prodotto";
        return View(new ProductBacklogRow(item,
            VotatoDaMe: !string.IsNullOrEmpty(meId) && item.VotantiUserIds.Contains(meId),
            MiaProposta: item.AutoreUserId == meId));
    }

    [HttpPost("{id}/voto")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleVote(string id)
    {
        var tid = _tenant.TenantId!;
        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        if (string.IsNullOrEmpty(meId)) return Forbid();

        var item = await _mongo.ProductBacklog.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (item is null) return NotFound();

        UpdateDefinition<ProductBacklogItem> update;
        if (item.VotantiUserIds.Contains(meId))
        {
            update = Builders<ProductBacklogItem>.Update
                .Pull(x => x.VotantiUserIds, meId)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);
        }
        else
        {
            update = Builders<ProductBacklogItem>.Update
                .AddToSet(x => x.VotantiUserIds, meId)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);
        }
        await _mongo.ProductBacklog.UpdateOneAsync(x => x.Id == id && x.TenantId == tid, update);

        if (Request.Headers.TryGetValue("X-Requested-With", out var v) && v == "XMLHttpRequest")
        {
            var aggiornato = await _mongo.ProductBacklog.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
            return Json(new { ok = true, voti = aggiornato?.VotantiUserIds.Count ?? 0, votato = aggiornato?.VotantiUserIds.Contains(meId) ?? false });
        }
        return RedirectToAction(nameof(Index));
    }

    // ─── Workflow Owner ──────────────────────────────────────────────

    [HttpPost("{id}/promuovi")]
    [Authorize(Policy = Policies.RequireOwner)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Promuovi(string id, string? sprintTarget, string? notaOwner)
        => await ChangeStato(id, StatoBacklog.InLavorazione, sprintTarget, notaOwner, "promossa a backlog");

    [HttpPost("{id}/in-esame")]
    [Authorize(Policy = Policies.RequireOwner)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InEsame(string id, string? notaOwner)
        => await ChangeStato(id, StatoBacklog.InEsame, null, notaOwner, "messa in esame");

    [HttpPost("{id}/completata")]
    [Authorize(Policy = Policies.RequireOwner)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Completata(string id, string? notaOwner)
        => await ChangeStato(id, StatoBacklog.Completata, null, notaOwner, "marcata come completata");

    [HttpPost("{id}/scarta")]
    [Authorize(Policy = Policies.RequireOwner)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Scarta(string id, string? notaOwner)
        => await ChangeStato(id, StatoBacklog.Scartata, null, notaOwner, "scartata");

    [HttpPost("{id}/riapri")]
    [Authorize(Policy = Policies.RequireOwner)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Riapri(string id)
        => await ChangeStato(id, StatoBacklog.Proposta, null, null, "riaperta");

    [HttpPost("{id}/elimina")]
    [Authorize(Policy = Policies.RequireOwner)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Elimina(string id)
    {
        var tid = _tenant.TenantId!;
        await _mongo.ProductBacklog.DeleteOneAsync(x => x.Id == id && x.TenantId == tid);
        TempData["flash"] = "Richiesta eliminata.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> ChangeStato(string id, StatoBacklog nuovo, string? sprintTarget, string? notaOwner, string azione)
    {
        var tid = _tenant.TenantId!;
        var item = await _mongo.ProductBacklog.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (item is null) return NotFound();

        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var meName = User.Identity?.Name ?? "";

        var update = Builders<ProductBacklogItem>.Update
            .Set(x => x.Stato, nuovo)
            .Set(x => x.GestitaDaUserId, meId)
            .Set(x => x.GestitaDaNome, meName)
            .Set(x => x.DataDecisione, DateTime.UtcNow)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        if (!string.IsNullOrWhiteSpace(notaOwner))
            update = update.Set(x => x.NotaOwner, notaOwner.Trim());
        if (nuovo == StatoBacklog.InLavorazione && !string.IsNullOrWhiteSpace(sprintTarget))
            update = update.Set(x => x.SprintTarget, sprintTarget.Trim());

        await _mongo.ProductBacklog.UpdateOneAsync(x => x.Id == id && x.TenantId == tid, update);

        await _publisher.PublishAsync(tid, "activity", new
        {
            kind = "comm",
            title = $"Backlog: «{Truncate(item.Vorrei, 70)}» {azione}",
            description = string.IsNullOrEmpty(notaOwner) ? $"da {meName}" : $"da {meName} · {notaOwner}",
            when = DateTime.UtcNow
        });

        TempData["flash"] = $"Richiesta {azione}.";
        return RedirectToAction(nameof(Index));
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
}
