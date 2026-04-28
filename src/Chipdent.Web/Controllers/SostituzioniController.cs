using System.Security.Claims;
using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Hubs;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Insights;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize]
[Route("sostituzioni")]
public class SostituzioniController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly INotificationPublisher _publisher;
    private readonly AiInsightsEngine _ai;

    public SostituzioniController(MongoContext mongo, ITenantContext tenant, INotificationPublisher publisher, AiInsightsEngine ai)
    {
        _mongo = mongo;
        _tenant = tenant;
        _ai = ai;
        _publisher = publisher;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var tid = _tenant.TenantId!;
        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var canManage = User.IsManagement() || User.IsDirettore();

        var all = await _mongo.Sostituzioni.Find(s => s.TenantId == tid)
                                           .SortByDescending(s => s.Data)
                                           .ToListAsync();
        var cliniche = (await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync())
            .ToDictionary(c => c.Id, c => c.Nome);

        SostituzioneRow Map(RichiestaSostituzione s) => new(s, cliniche.GetValueOrDefault(s.ClinicaId, "—"));

        var aperte = all.Where(s => s.Stato == StatoSostituzione.Aperta || s.Stato == StatoSostituzione.SostitutoProposto).Select(Map).ToList();
        var coperte = all.Where(s => s.Stato == StatoSostituzione.Coperta).Take(20).Select(Map).ToList();
        var escalate = all.Where(s => s.Stato == StatoSostituzione.EscaladaAlMgmt).Select(Map).ToList();
        var inArrivo = all.Where(s => s.SostitutoUserId == meId && s.Stato == StatoSostituzione.SostitutoProposto).Select(Map).ToList();

        ViewData["Section"] = "sostituzioni";
        return View(new SostituzioniIndexViewModel
        {
            Aperte = aperte,
            Coperte = coperte,
            Escalate = escalate,
            InArrivo = inArrivo,
            CanManage = canManage
        });
    }

    [HttpGet("nuova")]
    [Authorize(Policy = Policies.RequireDirettore)]
    public async Task<IActionResult> Create(string? dipendenteId = null, string? turnoId = null)
    {
        var tid = _tenant.TenantId!;
        var dipendenti = await _mongo.Dipendenti
            .Find(d => d.TenantId == tid && d.Stato != StatoDipendente.Cessato)
            .SortBy(d => d.Cognome).ToListAsync();

        var vm = new NuovaSostituzioneViewModel { Dipendenti = dipendenti };

        if (!string.IsNullOrEmpty(dipendenteId)) vm.AssenteDipendenteId = dipendenteId;

        if (!string.IsNullOrEmpty(turnoId))
        {
            var t = await _mongo.Turni.Find(x => x.Id == turnoId && x.TenantId == tid).FirstOrDefaultAsync();
            if (t is not null && t.TipoPersona == TipoPersona.Dipendente)
            {
                vm.AssenteDipendenteId = t.PersonaId;
                vm.Data = t.Data;
                vm.OraInizio = t.OraInizio;
                vm.OraFine = t.OraFine;
                vm.TurnoOrigineId = t.Id;
            }
        }

        ViewData["Section"] = "sostituzioni";
        return View(vm);
    }

    [HttpPost("nuova")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NuovaSostituzioneViewModel vm)
    {
        var tid = _tenant.TenantId!;
        if (vm.OraFine <= vm.OraInizio)
            ModelState.AddModelError(nameof(vm.OraFine), "L'ora di fine deve essere successiva all'inizio.");

        var assente = await _mongo.Dipendenti.Find(d => d.Id == vm.AssenteDipendenteId && d.TenantId == tid).FirstOrDefaultAsync();
        if (assente is null)
            ModelState.AddModelError(nameof(vm.AssenteDipendenteId), "Dipendente non valido.");

        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "sostituzioni";
            vm.Dipendenti = await _mongo.Dipendenti
                .Find(d => d.TenantId == tid && d.Stato != StatoDipendente.Cessato)
                .SortBy(d => d.Cognome).ToListAsync();
            return View(vm);
        }

        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var meName = User.Identity?.Name ?? "";
        var s = new RichiestaSostituzione
        {
            TenantId = tid,
            ClinicaId = assente!.ClinicaId,
            AssenteDipendenteId = assente.Id,
            AssenteNome = assente.NomeCompleto,
            RuoloRichiesto = assente.Ruolo,
            Data = DateTime.SpecifyKind(vm.Data.Date, DateTimeKind.Utc),
            OraInizio = vm.OraInizio,
            OraFine = vm.OraFine,
            Motivo = vm.Motivo,
            Descrizione = vm.Descrizione,
            TurnoOrigineId = vm.TurnoOrigineId,
            CreatoDaUserId = meId,
            CreatoDaNome = meName,
            Stato = StatoSostituzione.Aperta
        };
        await _mongo.Sostituzioni.InsertOneAsync(s);

        await _publisher.PublishAsync(tid, "activity", new
        {
            kind = "shift",
            title = $"Sostituzione richiesta: {assente.NomeCompleto}",
            description = $"{vm.Data:dd/MM} {vm.OraInizio:hh\\:mm}–{vm.OraFine:hh\\:mm} · {vm.Motivo}",
            when = DateTime.UtcNow
        });

        TempData["flash"] = "Richiesta aperta. Cerca un sostituto fra i candidati disponibili.";
        return RedirectToAction(nameof(Candidati), new { id = s.Id });
    }

    [HttpGet("{id}/candidati")]
    [Authorize(Policy = Policies.RequireDirettore)]
    public async Task<IActionResult> Candidati(string id)
    {
        var tid = _tenant.TenantId!;
        var s = await _mongo.Sostituzioni.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (s is null) return NotFound();

        var cliniche = (await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync())
            .ToDictionary(c => c.Id, c => c.Nome);

        // Candidati = dipendenti stessa sede, stesso ruolo, escluso l'assente
        var candidatiBase = await _mongo.Dipendenti
            .Find(d => d.TenantId == tid
                       && d.ClinicaId == s.ClinicaId
                       && d.Ruolo == s.RuoloRichiesto
                       && d.Id != s.AssenteDipendenteId
                       && d.Stato != StatoDipendente.Cessato)
            .ToListAsync();

        // Carica turni e ferie del giorno per filtrare disponibilità
        var dayStart = s.Data.Date;
        var dayEnd = dayStart.AddDays(1);
        var turniGiorno = await _mongo.Turni
            .Find(t => t.TenantId == tid && t.Data >= dayStart && t.Data < dayEnd && t.TipoPersona == TipoPersona.Dipendente)
            .ToListAsync();
        var ferieAttive = await _mongo.RichiesteFerie
            .Find(r => r.TenantId == tid && r.Stato == StatoRichiestaFerie.Approvata
                       && r.DataInizio <= dayStart && r.DataFine >= dayStart)
            .ToListAsync();

        // AI score per tutti i candidati (anche quelli non disponibili, per sapere chi sarebbe stato il migliore)
        var aiScores = await _ai.ScoreCandidatiAsync(tid,
            candidatiBase.Select(x => x.Id).ToList(),
            s.Data, s.OraInizio, s.OraFine);

        var candidati = new List<CandidatoSostituzione>();
        foreach (var c in candidatiBase)
        {
            var inFerie = ferieAttive.Any(f => f.DipendenteId == c.Id);
            var sovrapposto = turniGiorno.Any(t => t.PersonaId == c.Id
                                                   && Overlap(t.OraInizio, t.OraFine, s.OraInizio, s.OraFine));
            string? motivo = inFerie ? "in ferie" : sovrapposto ? "ha già un turno sovrapposto" : null;

            var u = await _mongo.Users.Find(x => x.TenantId == tid && x.LinkedPersonId == c.Id && x.LinkedPersonType == LinkedPersonType.Dipendente && x.IsActive).FirstOrDefaultAsync();
            var ai = aiScores.GetValueOrDefault(c.Id);
            candidati.Add(new CandidatoSostituzione(
                DipendenteId: c.Id,
                UserId: u?.Id,
                Nome: c.NomeCompleto,
                Ruolo: c.Ruolo,
                LiberoInQuelMomento: !sovrapposto,
                InFerie: inFerie,
                MotivoNonDisponibile: motivo,
                AiScore: ai?.Score ?? 0,
                AiMotivazione: ai?.Motivazione,
                CarichoOreSettimana: ai?.CarichoOreSettimana ?? 0));
        }
        // Ordino: prima disponibili, poi per AI score discendente
        candidati = candidati.OrderByDescending(x => x.MotivoNonDisponibile is null)
                             .ThenByDescending(x => x.AiScore)
                             .ThenBy(x => x.Nome).ToList();

        ViewData["Section"] = "sostituzioni";
        return View(new SostituzioneCandidatiViewModel
        {
            Richiesta = s,
            ClinicaNome = cliniche.GetValueOrDefault(s.ClinicaId, "—"),
            AssenteNome = s.AssenteNome,
            Candidati = candidati
        });
    }

    [HttpPost("{id}/proponi")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Proponi(string id, string dipendenteId)
    {
        var tid = _tenant.TenantId!;
        var s = await _mongo.Sostituzioni.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (s is null) return NotFound();

        var dip = await _mongo.Dipendenti.Find(d => d.Id == dipendenteId && d.TenantId == tid).FirstOrDefaultAsync();
        if (dip is null) return BadRequest();
        var u = await _mongo.Users.Find(x => x.TenantId == tid && x.LinkedPersonId == dipendenteId && x.LinkedPersonType == LinkedPersonType.Dipendente).FirstOrDefaultAsync();

        await _mongo.Sostituzioni.UpdateOneAsync(
            x => x.Id == id && x.TenantId == tid,
            Builders<RichiestaSostituzione>.Update
                .Set(x => x.SostitutoDipendenteId, dipendenteId)
                .Set(x => x.SostitutoUserId, u?.Id)
                .Set(x => x.SostitutoNome, dip.NomeCompleto)
                .Set(x => x.Stato, StatoSostituzione.SostitutoProposto)
                .Set(x => x.DataNotificaSostituto, DateTime.UtcNow)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        await _publisher.PublishAsync(tid, "activity", new
        {
            kind = "shift",
            title = $"Sostituzione proposta a {dip.NomeCompleto}",
            description = $"{s.AssenteNome} · {s.Data:dd/MM} {s.OraInizio:hh\\:mm}",
            when = DateTime.UtcNow
        });

        TempData["flash"] = $"{dip.NomeCompleto} è stato notificato. Attende accettazione.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/accetta")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accetta(string id, string? note = null)
    {
        var tid = _tenant.TenantId!;
        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var s = await _mongo.Sostituzioni.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (s is null) return NotFound();
        if (s.SostitutoUserId != meId && !User.IsManagement() && !User.IsDirettore()) return Forbid();
        if (s.Stato != StatoSostituzione.SostitutoProposto)
        {
            TempData["flash"] = "Richiesta non in stato accettabile.";
            return RedirectToAction(nameof(Index));
        }

        // Se c'è un turno collegato, lo riassegno al sostituto
        if (!string.IsNullOrEmpty(s.TurnoOrigineId) && !string.IsNullOrEmpty(s.SostitutoDipendenteId))
        {
            await _mongo.Turni.UpdateOneAsync(
                t => t.Id == s.TurnoOrigineId && t.TenantId == tid,
                Builders<Turno>.Update.Set(t => t.PersonaId, s.SostitutoDipendenteId).Set(t => t.UpdatedAt, DateTime.UtcNow));
        }

        await _mongo.Sostituzioni.UpdateOneAsync(
            x => x.Id == id && x.TenantId == tid,
            Builders<RichiestaSostituzione>.Update
                .Set(x => x.Stato, StatoSostituzione.Coperta)
                .Set(x => x.DataAccettazione, DateTime.UtcNow)
                .Set(x => x.DataChiusura, DateTime.UtcNow)
                .Set(x => x.NoteSostituto, note)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        TempData["flash"] = "Sostituzione coperta. Grazie!";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/rifiuta")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rifiuta(string id, string? note = null)
    {
        var tid = _tenant.TenantId!;
        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var s = await _mongo.Sostituzioni.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (s is null) return NotFound();
        if (s.SostitutoUserId != meId && !User.IsManagement() && !User.IsDirettore()) return Forbid();
        if (s.Stato != StatoSostituzione.SostitutoProposto)
        {
            TempData["flash"] = "Richiesta non in stato rifiutabile.";
            return RedirectToAction(nameof(Index));
        }

        // Torna in stato Aperta (Direttore può proporre un altro)
        await _mongo.Sostituzioni.UpdateOneAsync(
            x => x.Id == id && x.TenantId == tid,
            Builders<RichiestaSostituzione>.Update
                .Set(x => x.Stato, StatoSostituzione.Aperta)
                .Set(x => x.SostitutoDipendenteId, (string?)null)
                .Set(x => x.SostitutoUserId, (string?)null)
                .Set(x => x.SostitutoNome, (string?)null)
                .Set(x => x.NoteSostituto, note)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        TempData["flash"] = "Hai declinato. La richiesta è tornata aperta per un altro candidato.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/escala")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Escala(string id)
    {
        var tid = _tenant.TenantId!;
        var s = await _mongo.Sostituzioni.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (s is null) return NotFound();

        await _mongo.Sostituzioni.UpdateOneAsync(
            x => x.Id == id && x.TenantId == tid,
            Builders<RichiestaSostituzione>.Update
                .Set(x => x.Stato, StatoSostituzione.EscaladaAlMgmt)
                .Set(x => x.Escalata, true)
                .Set(x => x.DataEscalation, DateTime.UtcNow)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        await _publisher.PublishAsync(tid, "activity", new
        {
            kind = "shift",
            title = "🚨 Sostituzione escalata al Management",
            description = $"{s.AssenteNome} · {s.Data:dd/MM} {s.OraInizio:hh\\:mm}",
            when = DateTime.UtcNow
        });

        TempData["flash"] = "Richiesta escalata al Management.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/annulla")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Annulla(string id)
    {
        var tid = _tenant.TenantId!;
        await _mongo.Sostituzioni.UpdateOneAsync(
            x => x.Id == id && x.TenantId == tid,
            Builders<RichiestaSostituzione>.Update
                .Set(x => x.Stato, StatoSostituzione.Annullata)
                .Set(x => x.DataChiusura, DateTime.UtcNow)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));
        TempData["flash"] = "Richiesta annullata.";
        return RedirectToAction(nameof(Index));
    }

    private static bool Overlap(TimeSpan a1, TimeSpan a2, TimeSpan b1, TimeSpan b2)
        => a1 < b2 && b1 < a2;
}
