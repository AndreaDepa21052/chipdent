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
[Route("comunicazioni")]
public class ComunicazioniController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly INotificationPublisher _publisher;

    public ComunicazioniController(MongoContext mongo, ITenantContext tenant, INotificationPublisher publisher)
    {
        _mongo = mongo;
        _tenant = tenant;
        _publisher = publisher;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? id = null, CategoriaComunicazione? categoria = null)
    {
        var tid = _tenant.TenantId!;
        var filter = Builders<Comunicazione>.Filter.Eq(c => c.TenantId, tid);
        if (categoria is not null)
            filter &= Builders<Comunicazione>.Filter.Eq(c => c.Categoria, categoria.Value);

        var lista = await _mongo.Comunicazioni
            .Find(filter)
            .SortByDescending(c => c.CreatedAt)
            .ToListAsync();

        Comunicazione? selezionata = null;
        if (!string.IsNullOrEmpty(id))
        {
            selezionata = lista.FirstOrDefault(c => c.Id == id);
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (selezionata is not null && !string.IsNullOrEmpty(userId) && !selezionata.LettaDaUserIds.Contains(userId))
            {
                await _mongo.Comunicazioni.UpdateOneAsync(
                    c => c.Id == id,
                    Builders<Comunicazione>.Update.AddToSet(c => c.LettaDaUserIds, userId));
                selezionata.LettaDaUserIds.Add(userId);
            }
        }
        else
        {
            selezionata = lista.FirstOrDefault();
        }

        var cliniche = (await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync())
            .ToDictionary(c => c.Id, c => c.Nome);

        ViewData["Section"] = "comunicazioni";
        ViewData["Categoria"] = categoria;
        return View(new ComunicazioniInboxViewModel
        {
            Lista = lista,
            Selezionata = selezionata,
            CurrentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty,
            ClinicheLookup = cliniche
        });
    }

    [HttpGet("nuova")]
    public async Task<IActionResult> Create()
    {
        ViewData["Section"] = "comunicazioni";
        return View(new ComunicazioneFormViewModel
        {
            Cliniche = await _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId).SortBy(c => c.Nome).ToListAsync()
        });
    }

    [HttpPost("nuova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ComunicazioneFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "comunicazioni";
            vm.Cliniche = await _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId).SortBy(c => c.Nome).ToListAsync();
            return View(vm);
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        var fullName = User.Identity?.Name ?? "";

        // Snapshot del numero di destinatari attivi per percentuale di letture.
        var totDestinatari = (int)await _mongo.Users
            .CountDocumentsAsync(u => u.TenantId == _tenant.TenantId && u.IsActive && u.Id != userId);

        var comm = new Comunicazione
        {
            TenantId = _tenant.TenantId!,
            MittenteUserId = userId,
            MittenteNome = fullName,
            Categoria = vm.Categoria,
            Oggetto = vm.Oggetto,
            Corpo = vm.Corpo,
            ClinicaId = string.IsNullOrEmpty(vm.ClinicaId) ? null : vm.ClinicaId,
            RichiedeConferma = vm.RichiedeConferma,
            TotaleDestinatari = Math.Max(1, totDestinatari),
            Stato = vm.Categoria is CategoriaComunicazione.RichiestaFerie or CategoriaComunicazione.RichiestaPermesso
                ? StatoRichiesta.InAttesa : StatoRichiesta.NonApplicabile,
            LettaDaUserIds = new List<string> { userId }
        };
        await _mongo.Comunicazioni.InsertOneAsync(comm);

        await _publisher.PublishAsync(_tenant.TenantId!, "comunicazione", new
        {
            id = comm.Id,
            oggetto = comm.Oggetto,
            mittente = comm.MittenteNome,
            categoria = comm.Categoria.ToString(),
            corpo = comm.Corpo.Length > 140 ? comm.Corpo[..140] + "…" : comm.Corpo,
            createdAt = comm.CreatedAt,
            mittenteUserId = comm.MittenteUserId
        });

        await _publisher.PublishAsync(_tenant.TenantId!, "activity", new
        {
            kind = "comm",
            title = $"Nuova comunicazione: {comm.Oggetto}",
            description = $"da {comm.MittenteNome}",
            when = DateTime.UtcNow
        });

        return RedirectToAction(nameof(Index), new { id = comm.Id });
    }

    [HttpPost("{id}/approva")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approva(string id) => await SetStato(id, StatoRichiesta.Approvata);

    [HttpPost("{id}/rifiuta")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rifiuta(string id) => await SetStato(id, StatoRichiesta.Rifiutata);

    [HttpPost("{id}/elimina")]
    [Authorize(Policy = Policies.RequireManagement)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        await _mongo.Comunicazioni.DeleteOneAsync(c => c.Id == id && c.TenantId == _tenant.TenantId);
        TempData["flash"] = "Comunicazione eliminata.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Conferma esplicita di lettura di una circolare (l'apertura sola non basta se RichiedeConferma=true).</summary>
    [HttpPost("{id}/conferma-lettura")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Conferma(string id)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        if (string.IsNullOrEmpty(userId)) return Forbid();

        await _mongo.Comunicazioni.UpdateOneAsync(
            c => c.Id == id && c.TenantId == _tenant.TenantId,
            Builders<Comunicazione>.Update.AddToSet(c => c.LettaDaUserIds, userId));

        TempData["flash"] = "Lettura confermata.";
        return RedirectToAction(nameof(Index), new { id });
    }

    private async Task<IActionResult> SetStato(string id, StatoRichiesta stato)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        await _mongo.Comunicazioni.UpdateOneAsync(
            c => c.Id == id && c.TenantId == _tenant.TenantId,
            Builders<Comunicazione>.Update
                .Set(c => c.Stato, stato)
                .Set(c => c.GestitaDaUserId, userId)
                .Set(c => c.GestitaIl, DateTime.UtcNow));

        var comm = await _mongo.Comunicazioni.Find(c => c.Id == id).FirstOrDefaultAsync();
        if (comm is not null)
        {
            await _publisher.PublishAsync(_tenant.TenantId!, "comunicazione-update", new
            {
                id = comm.Id,
                stato = stato.ToString(),
                gestitaDa = User.Identity?.Name ?? ""
            });
        }

        TempData["flash"] = stato == StatoRichiesta.Approvata ? "Richiesta approvata." : "Richiesta rifiutata.";
        return RedirectToAction(nameof(Index), new { id });
    }
}
