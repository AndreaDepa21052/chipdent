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
[Route("cambio-turno")]
public class CambioTurnoController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly INotificationPublisher _publisher;

    public CambioTurnoController(MongoContext mongo, ITenantContext tenant, INotificationPublisher publisher)
    {
        _mongo = mongo;
        _tenant = tenant;
        _publisher = publisher;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var tid = _tenant.TenantId!;
        var me = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var canApprove = User.IsManagement() || User.IsDirettore();

        var allReq = await _mongo.RichiesteCambioTurno
            .Find(r => r.TenantId == tid)
            .SortByDescending(r => r.CreatedAt)
            .ToListAsync();

        var turni = (await _mongo.Turni.Find(t => t.TenantId == tid
                                                  && allReq.Select(r => r.TurnoId).ToList().Contains(t.Id))
                                       .ToListAsync())
            .ToDictionary(t => t.Id);
        var cliniche = (await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync())
            .ToDictionary(c => c.Id, c => c.Nome);

        CambioTurnoRow Map(RichiestaCambioTurno r) => new(
            r,
            turni.GetValueOrDefault(r.TurnoId),
            cliniche.GetValueOrDefault(r.ClinicaId, "—"),
            r.DestinatarioUserId is null ? $"📢 broadcast a colleghi {cliniche.GetValueOrDefault(r.ClinicaId, "")}" : (r.DestinatarioNome ?? "—"));

        var mie = allReq.Where(r => r.RichiedenteUserId == me).Select(Map).ToList();
        var inArrivo = allReq.Where(r => r.RichiedenteUserId != me
                                         && r.Stato == StatoCambioTurno.InAttesa
                                         && (r.DestinatarioUserId == me || r.DestinatarioUserId is null))
                             .Select(Map).ToList();
        IReadOnlyList<CambioTurnoRow> daApprovare = canApprove
            ? allReq.Where(r => r.Stato == StatoCambioTurno.AccettataDaCollega).Select(Map).ToList()
            : Array.Empty<CambioTurnoRow>();

        ViewData["Section"] = "cambio-turno";
        return View(new CambioTurnoIndexViewModel
        {
            Mie = mie,
            InArrivo = inArrivo,
            DaApprovare = daApprovare,
            CanApprove = canApprove
        });
    }

    [HttpGet("nuova/{turnoId}")]
    public async Task<IActionResult> Create(string turnoId)
    {
        var tid = _tenant.TenantId!;
        var t = await _mongo.Turni.Find(x => x.Id == turnoId && x.TenantId == tid).FirstOrDefaultAsync();
        if (t is null) return NotFound();

        // Verifica che il turno sia "mio": il LinkedPersonId dell'utente deve coincidere col PersonaId del turno
        var linkedType = User.LinkedPersonType();
        var linkedId = User.LinkedPersonId();
        var myShift = (linkedType == "Dottore" && t.TipoPersona == TipoPersona.Dottore && t.PersonaId == linkedId)
                   || (linkedType == "Dipendente" && t.TipoPersona == TipoPersona.Dipendente && t.PersonaId == linkedId);
        if (!myShift && !User.IsManagement() && !User.IsDirettore())
            return Forbid();

        var cliniche = (await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync())
            .ToDictionary(c => c.Id, c => c.Nome);

        // Colleghi: stesso tipo persona (dottore/dipendente), stessa clinica, escluso il richiedente
        var colleghi = new List<CollegaMini>();
        if (t.TipoPersona == TipoPersona.Dottore)
        {
            var dottori = await _mongo.Dottori.Find(d => d.TenantId == tid && d.Attivo && d.Id != t.PersonaId).ToListAsync();
            foreach (var d in dottori)
            {
                var u = await _mongo.Users.Find(x => x.TenantId == tid && x.LinkedPersonId == d.Id && x.LinkedPersonType == LinkedPersonType.Dottore && x.IsActive).FirstOrDefaultAsync();
                if (u is not null) colleghi.Add(new CollegaMini(u.Id, d.NomeCompleto, d.Specializzazione));
            }
        }
        else
        {
            var dips = await _mongo.Dipendenti.Find(d => d.TenantId == tid && d.Stato != StatoDipendente.Cessato && d.ClinicaId == t.ClinicaId && d.Id != t.PersonaId).ToListAsync();
            foreach (var d in dips)
            {
                var u = await _mongo.Users.Find(x => x.TenantId == tid && x.LinkedPersonId == d.Id && x.LinkedPersonType == LinkedPersonType.Dipendente && x.IsActive).FirstOrDefaultAsync();
                if (u is not null) colleghi.Add(new CollegaMini(u.Id, d.NomeCompleto, d.Ruolo.ToString()));
            }
        }

        ViewData["Section"] = "cambio-turno";
        return View(new NuovaCambioTurnoViewModel
        {
            TurnoId = t.Id,
            Turno = t,
            TurnoLabel = $"{t.Data:dd/MM} · {t.OraInizio:hh\\:mm}–{t.OraFine:hh\\:mm}",
            ClinicaNome = cliniche.GetValueOrDefault(t.ClinicaId, "—"),
            Colleghi = colleghi
        });
    }

    [HttpPost("nuova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NuovaCambioTurnoViewModel vm)
    {
        var tid = _tenant.TenantId!;
        var t = await _mongo.Turni.Find(x => x.Id == vm.TurnoId && x.TenantId == tid).FirstOrDefaultAsync();
        if (t is null) return NotFound();

        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var meName = User.Identity?.Name ?? "";

        string? destNome = null;
        if (!string.IsNullOrEmpty(vm.DestinatarioUserId))
        {
            var u = await _mongo.Users.Find(x => x.Id == vm.DestinatarioUserId && x.TenantId == tid).FirstOrDefaultAsync();
            destNome = u?.FullName;
        }

        var richiesta = new RichiestaCambioTurno
        {
            TenantId = tid,
            TurnoId = t.Id,
            ClinicaId = t.ClinicaId,
            RichiedenteUserId = meId,
            RichiedenteNome = meName,
            TipoPersona = t.TipoPersona,
            PersonaIdRichiedente = t.PersonaId,
            DestinatarioUserId = string.IsNullOrEmpty(vm.DestinatarioUserId) ? null : vm.DestinatarioUserId,
            DestinatarioNome = destNome,
            Stato = StatoCambioTurno.InAttesa,
            NoteRichiesta = vm.Note
        };
        await _mongo.RichiesteCambioTurno.InsertOneAsync(richiesta);

        await _publisher.PublishAsync(tid, "activity", new
        {
            kind = "shift",
            title = "Cambio turno richiesto",
            description = $"{meName} · {t.Data:dd/MM} {t.OraInizio:hh\\:mm}",
            when = DateTime.UtcNow
        });

        TempData["flash"] = "Richiesta inviata.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/accetta")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accetta(string id)
        => await CollegaDecide(id, accept: true);

    [HttpPost("{id}/rifiuta")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rifiuta(string id)
        => await CollegaDecide(id, accept: false);

    private async Task<IActionResult> CollegaDecide(string id, bool accept)
    {
        var tid = _tenant.TenantId!;
        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var meName = User.Identity?.Name ?? "";

        var r = await _mongo.RichiesteCambioTurno.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (r is null) return NotFound();
        if (r.Stato != StatoCambioTurno.InAttesa)
        {
            TempData["flash"] = "La richiesta è già stata processata.";
            return RedirectToAction(nameof(Index));
        }
        if (r.DestinatarioUserId is not null && r.DestinatarioUserId != meId)
            return Forbid();

        // Trovo il PersonaId del collega che accetta (dal suo LinkedPersonId)
        var meUser = await _mongo.Users.Find(x => x.Id == meId && x.TenantId == tid).FirstOrDefaultAsync();
        if (meUser is null || string.IsNullOrEmpty(meUser.LinkedPersonId))
        {
            TempData["flash"] = "Il tuo account non è collegato a un dottore o dipendente.";
            return RedirectToAction(nameof(Index));
        }

        if (accept)
        {
            await _mongo.RichiesteCambioTurno.UpdateOneAsync(
                x => x.Id == id && x.TenantId == tid,
                Builders<RichiestaCambioTurno>.Update
                    .Set(x => x.Stato, StatoCambioTurno.AccettataDaCollega)
                    .Set(x => x.CollegaAccettanteUserId, meId)
                    .Set(x => x.CollegaAccettanteNome, meName)
                    .Set(x => x.PersonaIdCollegaAccettante, meUser.LinkedPersonId)
                    .Set(x => x.DataAccettazioneCollega, DateTime.UtcNow)
                    .Set(x => x.UpdatedAt, DateTime.UtcNow));
            TempData["flash"] = "Richiesta accettata. Ora attende l'approvazione del Direttore.";
        }
        else
        {
            // Se è broadcast, il rifiuto di un collega non chiude la richiesta
            if (r.DestinatarioUserId is null)
            {
                TempData["flash"] = "Hai rifiutato la richiesta. Resta aperta per altri colleghi.";
                return RedirectToAction(nameof(Index));
            }
            await _mongo.RichiesteCambioTurno.UpdateOneAsync(
                x => x.Id == id && x.TenantId == tid,
                Builders<RichiestaCambioTurno>.Update
                    .Set(x => x.Stato, StatoCambioTurno.RifiutataDaCollega)
                    .Set(x => x.UpdatedAt, DateTime.UtcNow));
            TempData["flash"] = "Richiesta rifiutata.";
        }

        await _publisher.PublishAsync(tid, "activity", new
        {
            kind = "shift",
            title = accept ? "Cambio turno accettato dal collega" : "Cambio turno rifiutato",
            description = $"{meName} → {r.RichiedenteNome}",
            when = DateTime.UtcNow
        });
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/approva")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approva(string id, string? note = null)
        => await DirettoreDecide(id, approve: true, note);

    [HttpPost("{id}/non-approva")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NonApprova(string id, string? note = null)
        => await DirettoreDecide(id, approve: false, note);

    private async Task<IActionResult> DirettoreDecide(string id, bool approve, string? note)
    {
        if (!(User.IsManagement() || User.IsDirettore())) return Forbid();
        var tid = _tenant.TenantId!;
        var r = await _mongo.RichiesteCambioTurno.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (r is null) return NotFound();
        if (r.Stato != StatoCambioTurno.AccettataDaCollega)
        {
            TempData["flash"] = "La richiesta non è in stato approvabile.";
            return RedirectToAction(nameof(Index));
        }

        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var meName = User.Identity?.Name ?? "";

        if (approve)
        {
            // Swap effettivo: cambio PersonaId del turno originale a quello del collega
            if (string.IsNullOrEmpty(r.PersonaIdCollegaAccettante))
            {
                TempData["flash"] = "Errore: collega accettante senza PersonaId.";
                return RedirectToAction(nameof(Index));
            }
            await _mongo.Turni.UpdateOneAsync(
                t => t.Id == r.TurnoId && t.TenantId == tid,
                Builders<Turno>.Update
                    .Set(t => t.PersonaId, r.PersonaIdCollegaAccettante)
                    .Set(t => t.UpdatedAt, DateTime.UtcNow));
        }

        await _mongo.RichiesteCambioTurno.UpdateOneAsync(
            x => x.Id == id && x.TenantId == tid,
            Builders<RichiestaCambioTurno>.Update
                .Set(x => x.Stato, approve ? StatoCambioTurno.ApprovataDirettore : StatoCambioTurno.RifiutataDirettore)
                .Set(x => x.DirettoreUserId, meId)
                .Set(x => x.DirettoreNome, meName)
                .Set(x => x.DataDecisioneDirettore, DateTime.UtcNow)
                .Set(x => x.NoteDirettore, note)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        await _publisher.PublishAsync(tid, "activity", new
        {
            kind = "shift",
            title = approve ? "Cambio turno approvato" : "Cambio turno respinto",
            description = $"{r.RichiedenteNome} ↔ {r.CollegaAccettanteNome}",
            when = DateTime.UtcNow
        });

        TempData["flash"] = approve ? "Approvato. Turno riassegnato." : "Cambio respinto.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/annulla")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Annulla(string id)
    {
        var tid = _tenant.TenantId!;
        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var r = await _mongo.RichiesteCambioTurno.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (r is null) return NotFound();
        if (r.RichiedenteUserId != meId && !User.IsManagement() && !User.IsDirettore()) return Forbid();
        if (r.Stato == StatoCambioTurno.ApprovataDirettore)
        {
            TempData["flash"] = "Una richiesta già approvata non può essere annullata da qui.";
            return RedirectToAction(nameof(Index));
        }
        await _mongo.RichiesteCambioTurno.UpdateOneAsync(
            x => x.Id == id && x.TenantId == tid,
            Builders<RichiestaCambioTurno>.Update
                .Set(x => x.Stato, StatoCambioTurno.Annullata)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));
        TempData["flash"] = "Richiesta annullata.";
        return RedirectToAction(nameof(Index));
    }
}
