using System.Security.Claims;
using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Hubs;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize]
[Route("chat")]
public class ChatController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly IChatPublisher _publisher;

    public ChatController(MongoContext mongo, ITenantContext tenant, IChatPublisher publisher)
    {
        _mongo = mongo;
        _tenant = tenant;
        _publisher = publisher;
    }

    [HttpGet("")]
    public Task<IActionResult> Index(string? thread = null) => Render(thread);

    [HttpGet("dm/{userId}")]
    public Task<IActionResult> DirectMessage(string userId)
    {
        var me = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var key = Messaggio.DmThreadKey(me, userId);
        return Render(key);
    }

    [HttpGet("clinica/{clinicaId}")]
    public Task<IActionResult> ClinicaThread(string clinicaId)
        => Render(Messaggio.ClinicaThreadKey(clinicaId));

    [HttpPost("invia")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send([FromForm] string thread, [FromForm] string testo)
    {
        var tid = _tenant.TenantId!;
        var me = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var fullName = User.Identity?.Name ?? "";
        if (string.IsNullOrWhiteSpace(thread) || string.IsNullOrWhiteSpace(testo))
            return BadRequest();

        var msg = new Messaggio
        {
            TenantId = tid,
            MittenteUserId = me,
            MittenteNome = fullName,
            Testo = testo.Trim(),
            LettoDaUserIds = new List<string> { me }
        };

        if (thread.StartsWith("dm:"))
        {
            var parts = thread.Substring(3).Split('|');
            if (parts.Length != 2) return BadRequest();
            var other = parts[0] == me ? parts[1] : parts[0];
            msg.DestinatarioUserId = other;
            await _mongo.Messaggi.InsertOneAsync(msg);
            await _publisher.PublishDirectAsync(me, other, ToPayload(msg));
        }
        else if (thread.StartsWith("clinica:"))
        {
            var clinicaId = thread.Substring(8);
            // Check accesso: l'utente deve poter accedere alla clinica
            if (!_tenant.CanAccessClinica(clinicaId)) return Forbid();
            msg.ClinicaGroupId = clinicaId;
            await _mongo.Messaggi.InsertOneAsync(msg);
            await _publisher.PublishToClinicaAsync(clinicaId, ToPayload(msg));
        }
        else
        {
            return BadRequest("Thread non valido.");
        }

        return RedirectToAction(nameof(Index), new { thread });
    }

    [HttpPost("{thread}/letto")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(string thread)
    {
        var me = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var tid = _tenant.TenantId!;

        FilterDefinition<Messaggio> filter;
        if (thread.StartsWith("dm:"))
        {
            var parts = thread.Substring(3).Split('|');
            if (parts.Length != 2) return BadRequest();
            var other = parts[0] == me ? parts[1] : parts[0];
            filter = Builders<Messaggio>.Filter.Eq(m => m.TenantId, tid)
                     & Builders<Messaggio>.Filter.Eq(m => m.MittenteUserId, other)
                     & Builders<Messaggio>.Filter.Eq(m => m.DestinatarioUserId, me);
        }
        else if (thread.StartsWith("clinica:"))
        {
            var clinicaId = thread.Substring(8);
            filter = Builders<Messaggio>.Filter.Eq(m => m.TenantId, tid)
                     & Builders<Messaggio>.Filter.Eq(m => m.ClinicaGroupId, clinicaId);
        }
        else return BadRequest();

        await _mongo.Messaggi.UpdateManyAsync(filter,
            Builders<Messaggio>.Update.AddToSet(m => m.LettoDaUserIds, me));
        return Ok();
    }

    private async Task<IActionResult> Render(string? threadKey)
    {
        var tid = _tenant.TenantId!;
        var me = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var meName = User.Identity?.Name ?? "";

        var users = await _mongo.Users.Find(u => u.TenantId == tid && u.IsActive).ToListAsync();
        var contatti = users.Where(u => u.Id != me)
            .Select(u => new UserMini(u.Id, u.FullName, u.Role.ToString()))
            .OrderBy(u => u.FullName).ToList();

        var clinicheAll = await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync();
        var clinicheVisibili = clinicheAll
            .Where(c => _tenant.CanAccessClinica(c.Id))
            .Select(c => new ClinicaMini(c.Id, c.Nome))
            .OrderBy(c => c.Nome).ToList();

        // Costruzione thread summaries: tutti i DM in cui sono coinvolto + tutte le cliniche visibili.
        var allMine = await _mongo.Messaggi
            .Find(m => m.TenantId == tid && (m.MittenteUserId == me || m.DestinatarioUserId == me
                       || (m.ClinicaGroupId != null && clinicheVisibili.Select(c => c.Id).Contains(m.ClinicaGroupId))))
            .SortByDescending(m => m.CreatedAt)
            .ToListAsync();

        var threadGroups = new Dictionary<string, ChatThreadSummary>();

        foreach (var msg in allMine)
        {
            string key;
            string title;
            string? sub;
            bool isClinica;
            if (msg.IsClinicaGroup)
            {
                key = Messaggio.ClinicaThreadKey(msg.ClinicaGroupId!);
                var c = clinicheAll.FirstOrDefault(x => x.Id == msg.ClinicaGroupId);
                title = c?.Nome ?? "Sede";
                sub = "Sede";
                isClinica = true;
            }
            else
            {
                var other = msg.MittenteUserId == me ? (msg.DestinatarioUserId ?? "") : msg.MittenteUserId;
                key = Messaggio.DmThreadKey(me, other);
                var u = users.FirstOrDefault(x => x.Id == other);
                title = u?.FullName ?? "Utente";
                sub = u?.Role.ToString();
                isClinica = false;
            }

            if (!threadGroups.ContainsKey(key))
            {
                var nonLetti = allMine.Count(m =>
                    ((m.IsClinicaGroup && Messaggio.ClinicaThreadKey(m.ClinicaGroupId!) == key)
                     || (!m.IsClinicaGroup && Messaggio.DmThreadKey(m.MittenteUserId, m.DestinatarioUserId ?? "") == key))
                    && !m.LettoDaUserIds.Contains(me));
                threadGroups[key] = new ChatThreadSummary(key, title, sub, msg.CreatedAt, nonLetti, isClinica);
            }
        }

        var threads = threadGroups.Values
            .OrderByDescending(t => t.UltimoMsgAt)
            .ToList();

        ChatThreadSummary? active = null;
        IReadOnlyList<Messaggio> messaggi = Array.Empty<Messaggio>();

        if (!string.IsNullOrEmpty(threadKey))
        {
            active = threads.FirstOrDefault(t => t.Key == threadKey);
            // Se il thread non esiste ancora (es. nuovo DM su utente con cui non ho ancora chattato), creo un placeholder.
            if (active is null)
            {
                if (threadKey.StartsWith("dm:"))
                {
                    var parts = threadKey.Substring(3).Split('|');
                    var other = parts[0] == me ? parts[1] : parts[0];
                    var u = users.FirstOrDefault(x => x.Id == other);
                    if (u is not null)
                        active = new ChatThreadSummary(threadKey, u.FullName, u.Role.ToString(), DateTime.MinValue, 0, false);
                }
                else if (threadKey.StartsWith("clinica:"))
                {
                    var cid = threadKey.Substring(8);
                    var c = clinicheAll.FirstOrDefault(x => x.Id == cid);
                    if (c is not null && _tenant.CanAccessClinica(c.Id))
                        active = new ChatThreadSummary(threadKey, c.Nome, "Sede", DateTime.MinValue, 0, true);
                }
            }

            if (active is not null)
            {
                if (active.IsClinica)
                {
                    var cid = threadKey.Substring(8);
                    messaggi = allMine.Where(m => m.ClinicaGroupId == cid).Reverse().ToList();
                }
                else
                {
                    var parts = threadKey.Substring(3).Split('|');
                    var other = parts[0] == me ? parts[1] : parts[0];
                    messaggi = allMine.Where(m => !m.IsClinicaGroup
                                                  && ((m.MittenteUserId == me && m.DestinatarioUserId == other)
                                                      || (m.MittenteUserId == other && m.DestinatarioUserId == me)))
                                      .Reverse().ToList();
                }
            }
        }

        ViewData["Section"] = "chat";
        return View("Index", new ChatIndexViewModel
        {
            Threads = threads,
            Active = active,
            Messaggi = messaggi,
            CurrentUserId = me,
            CurrentUserName = meName,
            ContattiDisponibili = contatti,
            ClinicheDisponibili = clinicheVisibili
        });
    }

    private static object ToPayload(Messaggio m) => new
    {
        id = m.Id,
        thread = m.IsClinicaGroup
            ? Messaggio.ClinicaThreadKey(m.ClinicaGroupId!)
            : Messaggio.DmThreadKey(m.MittenteUserId, m.DestinatarioUserId ?? ""),
        mittenteId = m.MittenteUserId,
        mittenteNome = m.MittenteNome,
        testo = m.Testo,
        createdAt = m.CreatedAt
    };
}
