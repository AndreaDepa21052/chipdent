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

[Authorize]
[Route("videoassistenza")]
public class VideoassistenzaController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly INotificationPublisher _publisher;

    public VideoassistenzaController(MongoContext mongo, ITenantContext tenant, INotificationPublisher publisher)
    {
        _mongo = mongo;
        _tenant = tenant;
        _publisher = publisher;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(StatoAssistenza? filter = null)
    {
        var tid = _tenant.TenantId!;
        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var canHandle = User.IsBackoffice() || User.IsManagement();

        var f = Builders<RichiestaAssistenza>.Filter.Eq(r => r.TenantId, tid);
        // Lo Staff e il Direttore vedono solo le proprie richieste; Backoffice/Management vedono tutto.
        if (!canHandle) f &= Builders<RichiestaAssistenza>.Filter.Eq(r => r.RichiedenteUserId, meId);
        if (filter.HasValue) f &= Builders<RichiestaAssistenza>.Filter.Eq(r => r.Stato, filter.Value);

        var richieste = await _mongo.RichiesteAssistenza.Find(f)
            .SortByDescending(r => r.Priorita).ThenByDescending(r => r.CreatedAt).ToListAsync();

        var statBase = Builders<RichiestaAssistenza>.Filter.Eq(r => r.TenantId, tid);
        if (!canHandle) statBase &= Builders<RichiestaAssistenza>.Filter.Eq(r => r.RichiedenteUserId, meId);
        var inAttesa = (int)await _mongo.RichiesteAssistenza.CountDocumentsAsync(statBase & Builders<RichiestaAssistenza>.Filter.Eq(r => r.Stato, StatoAssistenza.InAttesa));
        var inCorso  = (int)await _mongo.RichiesteAssistenza.CountDocumentsAsync(statBase & Builders<RichiestaAssistenza>.Filter.Eq(r => r.Stato, StatoAssistenza.InCorso));
        var urgenti  = (int)await _mongo.RichiesteAssistenza.CountDocumentsAsync(statBase & Builders<RichiestaAssistenza>.Filter.Eq(r => r.Priorita, PrioritaAssistenza.Urgente) & Builders<RichiestaAssistenza>.Filter.In(r => r.Stato, new[] { StatoAssistenza.InAttesa, StatoAssistenza.InCorso }));

        ViewData["Section"] = "videoassistenza";
        return View(new VideoassistenzaIndexViewModel
        {
            Richieste = richieste,
            CanHandle = canHandle,
            InAttesa = inAttesa,
            InCorso = inCorso,
            Urgenti = urgenti,
            Filter = filter
        });
    }

    [HttpGet("nuova")]
    public async Task<IActionResult> Create()
    {
        var tid = _tenant.TenantId!;

        // Se Staff/Direttore con clinica, pre-compilo
        string? defaultClinicaId = null;
        if (_tenant.ClinicaIds.Count == 1) defaultClinicaId = _tenant.ClinicaIds[0];
        if (User.LinkedPersonType() == "Dipendente" && !string.IsNullOrEmpty(User.LinkedPersonId()))
        {
            var dip = await _mongo.Dipendenti.Find(d => d.Id == User.LinkedPersonId() && d.TenantId == tid).FirstOrDefaultAsync();
            if (dip is not null) defaultClinicaId = dip.ClinicaId;
        }

        var cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).SortBy(c => c.Nome).ToListAsync();

        ViewData["Section"] = "videoassistenza";
        return View(new NuovaAssistenzaViewModel
        {
            Cliniche = cliniche,
            ClinicaId = defaultClinicaId
        });
    }

    [HttpPost("nuova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NuovaAssistenzaViewModel vm)
    {
        var tid = _tenant.TenantId!;

        if (string.IsNullOrWhiteSpace(vm.Motivo))
            ModelState.AddModelError(nameof(vm.Motivo), "Motivo obbligatorio.");

        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "videoassistenza";
            vm.Cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).SortBy(c => c.Nome).ToListAsync();
            return View(vm);
        }

        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var meName = User.Identity?.Name ?? "Utente";
        var meRole = User.FindFirst(ClaimTypes.Role)?.Value ?? "";

        string? clinicaNome = null;
        if (!string.IsNullOrEmpty(vm.ClinicaId))
        {
            var c = await _mongo.Cliniche.Find(x => x.Id == vm.ClinicaId && x.TenantId == tid).FirstOrDefaultAsync();
            clinicaNome = c?.Nome;
        }

        var r = new RichiestaAssistenza
        {
            TenantId = tid,
            RichiedenteUserId = meId,
            RichiedenteNome = meName,
            RichiedenteRuolo = meRole,
            ClinicaId = vm.ClinicaId,
            ClinicaNome = clinicaNome,
            Priorita = vm.Priorita,
            Motivo = vm.Motivo.Trim(),
            Descrizione = string.IsNullOrWhiteSpace(vm.Descrizione) ? null : vm.Descrizione.Trim(),
            Stato = StatoAssistenza.InAttesa
        };
        await _mongo.RichiesteAssistenza.InsertOneAsync(r);

        // Notifica realtime: attiva il toast a tutti i Backoffice/Management online del tenant.
        await _publisher.PublishAsync(tid, "assistenza-nuova", new
        {
            id = r.Id,
            richiedente = r.RichiedenteNome,
            ruolo = r.RichiedenteRuolo,
            clinica = r.ClinicaNome,
            priorita = r.Priorita.ToString(),
            motivo = r.Motivo,
            url = Url.Action(nameof(Sala), new { id = r.Id }),
            when = DateTime.UtcNow
        });
        // Voce in feed/bell anche per le altre persone.
        await _publisher.PublishAsync(tid, "activity", new
        {
            kind = "comm",
            title = $"📞 Videoassistenza richiesta da {r.RichiedenteNome}",
            description = (clinicaNome is null ? "" : clinicaNome + " · ") + r.Motivo,
            when = DateTime.UtcNow
        });

        return RedirectToAction(nameof(Sala), new { id = r.Id });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Sala(string id)
    {
        var tid = _tenant.TenantId!;
        var r = await _mongo.RichiesteAssistenza.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (r is null) return NotFound();

        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var canHandle = User.IsBackoffice() || User.IsManagement();
        if (r.RichiedenteUserId != meId && !canHandle) return Forbid();

        ViewData["Section"] = "videoassistenza";
        return View(new SalaAssistenzaViewModel
        {
            Richiesta = r,
            CanHandle = canHandle,
            UserDisplayName = User.Identity?.Name ?? "Utente",
            UserEmail = User.FindFirst(ClaimTypes.Email)?.Value
        });
    }

    [HttpPost("{id}/rispondi")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rispondi(string id)
    {
        var tid = _tenant.TenantId!;
        var r = await _mongo.RichiesteAssistenza.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (r is null) return NotFound();

        if (r.Stato == StatoAssistenza.InAttesa)
        {
            var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            var meName = User.Identity?.Name ?? "Operatore";
            await _mongo.RichiesteAssistenza.UpdateOneAsync(
                x => x.Id == id && x.TenantId == tid,
                Builders<RichiestaAssistenza>.Update
                    .Set(x => x.Stato, StatoAssistenza.InCorso)
                    .Set(x => x.OperatoreUserId, meId)
                    .Set(x => x.OperatoreNome, meName)
                    .Set(x => x.PresaInCaricoAt, DateTime.UtcNow)
                    .Set(x => x.UpdatedAt, DateTime.UtcNow));

            // Notifica al richiedente che l'operatore sta entrando in chiamata.
            await _publisher.PublishAsync(tid, "assistenza-presa", new
            {
                id = r.Id,
                operatore = meName,
                richiedenteUserId = r.RichiedenteUserId,
                url = Url.Action(nameof(Sala), new { id = r.Id }),
                when = DateTime.UtcNow
            });
        }

        return RedirectToAction(nameof(Sala), new { id });
    }

    [HttpPost("{id}/chiudi")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Chiudi(string id, string? note = null)
    {
        var tid = _tenant.TenantId!;
        var r = await _mongo.RichiesteAssistenza.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (r is null) return NotFound();

        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var canHandle = User.IsBackoffice() || User.IsManagement();
        if (r.RichiedenteUserId != meId && !canHandle) return Forbid();

        await _mongo.RichiesteAssistenza.UpdateOneAsync(
            x => x.Id == id && x.TenantId == tid,
            Builders<RichiestaAssistenza>.Update
                .Set(x => x.Stato, StatoAssistenza.Chiusa)
                .Set(x => x.ChiusaAt, DateTime.UtcNow)
                .Set(x => x.NoteChiusura, string.IsNullOrWhiteSpace(note) ? null : note.Trim())
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        TempData["flash"] = "Sessione chiusa.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/annulla")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Annulla(string id)
    {
        var tid = _tenant.TenantId!;
        var r = await _mongo.RichiesteAssistenza.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (r is null) return NotFound();

        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        if (r.RichiedenteUserId != meId && !User.IsManagement()) return Forbid();
        if (r.Stato == StatoAssistenza.Chiusa) return RedirectToAction(nameof(Index));

        await _mongo.RichiesteAssistenza.UpdateOneAsync(
            x => x.Id == id && x.TenantId == tid,
            Builders<RichiestaAssistenza>.Update
                .Set(x => x.Stato, StatoAssistenza.Annullata)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        TempData["flash"] = "Richiesta annullata.";
        return RedirectToAction(nameof(Index));
    }
}
