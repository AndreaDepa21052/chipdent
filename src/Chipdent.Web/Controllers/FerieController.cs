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
[Route("ferie")]
public class FerieController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly INotificationPublisher _publisher;

    public FerieController(MongoContext mongo, ITenantContext tenant, INotificationPublisher publisher)
    {
        _mongo = mongo;
        _tenant = tenant;
        _publisher = publisher;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(StatoRichiestaFerie? filter = null)
    {
        var tid = _tenant.TenantId!;
        var query = _mongo.RichiesteFerie.Find(r => r.TenantId == tid);
        if (filter.HasValue) query = _mongo.RichiesteFerie.Find(r => r.TenantId == tid && r.Stato == filter.Value);

        var richieste = await query.SortByDescending(r => r.CreatedAt).ToListAsync();
        var dipendenti = (await _mongo.Dipendenti.Find(d => d.TenantId == tid).ToListAsync())
            .ToDictionary(d => d.Id);
        var cliniche = (await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync())
            .ToDictionary(c => c.Id, c => c.Nome);
        var users = (await _mongo.Users.Find(u => u.TenantId == tid).ToListAsync())
            .ToDictionary(u => u.Id, u => u.FullName);

        RichiestaFerieRow Map(RichiestaFerie r) => new(
            r,
            dipendenti.TryGetValue(r.DipendenteId, out var d) ? d.NomeCompleto : "—",
            cliniche.TryGetValue(r.ClinicaId, out var cn) ? cn : "—",
            r.DecisoreUserId is not null && users.TryGetValue(r.DecisoreUserId, out var dn) ? dn : null);

        var canApprove = User.IsManagement() || User.IsDirettore();
        var myLinkedType = User.LinkedPersonType();
        var myLinkedId = User.LinkedPersonId();
        var myDipendenteId = myLinkedType == "Dipendente" ? myLinkedId : null;

        IReadOnlyList<RichiestaFerieRow> mie = Array.Empty<RichiestaFerieRow>();
        if (myDipendenteId is not null)
        {
            mie = richieste
                .Where(r => r.DipendenteId == myDipendenteId)
                .Select(Map).ToList();
        }

        var visible = canApprove
            ? richieste.Select(Map).ToList()
            : (myDipendenteId is not null ? mie : Array.Empty<RichiestaFerieRow>());

        int? mieiResidui = null;
        if (myDipendenteId is not null && dipendenti.TryGetValue(myDipendenteId, out var meAsDip))
            mieiResidui = meAsDip.GiorniFerieResidui;

        ViewData["Section"] = "ferie";
        return View(new FerieIndexViewModel
        {
            Richieste = visible,
            Mie = mie,
            CanApprove = canApprove,
            CanRequest = myDipendenteId is not null || canApprove,
            MyDipendenteId = myDipendenteId,
            MieiGiorniResidui = mieiResidui,
            Filter = filter
        });
    }

    [HttpGet("nuova")]
    public async Task<IActionResult> Create()
    {
        var tid = _tenant.TenantId!;
        var dipendenti = await _mongo.Dipendenti
            .Find(d => d.TenantId == tid && d.Stato != StatoDipendente.Cessato)
            .SortBy(d => d.Cognome).ToListAsync();

        var canApprove = User.IsManagement() || User.IsDirettore();
        var myDipendenteId = User.LinkedPersonType() == "Dipendente" ? User.LinkedPersonId() : null;

        var vm = new NuovaRichiestaFerieViewModel();
        if (canApprove)
        {
            vm.DipendentiSelezionabili = dipendenti;
        }
        else if (myDipendenteId is not null)
        {
            var me = dipendenti.FirstOrDefault(d => d.Id == myDipendenteId);
            if (me is null) return Forbid();
            vm.DipendenteId = me.Id;
            vm.DipendentiSelezionabili = new[] { me };
            vm.LockedDipendente = true;
        }
        else
        {
            return Forbid();
        }

        ViewData["Section"] = "ferie";
        return View(vm);
    }

    [HttpPost("nuova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NuovaRichiestaFerieViewModel vm)
    {
        var tid = _tenant.TenantId!;
        var dipendente = await _mongo.Dipendenti.Find(d => d.Id == vm.DipendenteId && d.TenantId == tid).FirstOrDefaultAsync();
        if (dipendente is null)
        {
            ModelState.AddModelError(nameof(vm.DipendenteId), "Dipendente non valido.");
        }
        else if (vm.DataFine < vm.DataInizio)
        {
            ModelState.AddModelError(nameof(vm.DataFine), "La data di fine deve essere uguale o successiva alla data di inizio.");
        }

        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "ferie";
            vm.DipendentiSelezionabili = await _mongo.Dipendenti
                .Find(d => d.TenantId == tid && d.Stato != StatoDipendente.Cessato)
                .SortBy(d => d.Cognome).ToListAsync();
            return View(vm);
        }

        var giorni = ContaGiorniLavorativi(vm.DataInizio, vm.DataFine);
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

        var richiesta = new RichiestaFerie
        {
            TenantId = tid,
            DipendenteId = dipendente!.Id,
            RichiedenteUserId = userId,
            ClinicaId = dipendente.ClinicaId,
            Tipo = vm.Tipo,
            DataInizio = vm.DataInizio.Date,
            DataFine = vm.DataFine.Date,
            GiorniRichiesti = giorni,
            NoteRichiesta = vm.Note,
            Stato = StatoRichiestaFerie.InAttesa
        };
        await _mongo.RichiesteFerie.InsertOneAsync(richiesta);

        await _publisher.PublishAsync(tid, "activity", new
        {
            kind = "ferie",
            title = $"Nuova richiesta {vm.Tipo}",
            description = $"{dipendente.NomeCompleto} · {vm.DataInizio:dd/MM} → {vm.DataFine:dd/MM} ({giorni}g)",
            when = DateTime.UtcNow
        });

        TempData["flash"] = $"Richiesta inviata ({giorni} giorni lavorativi).";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/approva")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(string id, string? note = null)
        => await SetDecision(id, StatoRichiestaFerie.Approvata, note);

    [HttpPost("{id}/rifiuta")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(string id, string? note = null)
        => await SetDecision(id, StatoRichiestaFerie.Rifiutata, note);

    [HttpPost("{id}/annulla")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(string id)
    {
        var tid = _tenant.TenantId!;
        var r = await _mongo.RichiesteFerie.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (r is null) return NotFound();

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        var canCancel = r.RichiedenteUserId == userId || User.IsManagement() || User.IsDirettore();
        if (!canCancel) return Forbid();

        // Se era già approvata e il saldo era stato applicato, lo restituiamo.
        if (r.Stato == StatoRichiestaFerie.Approvata && r.SaldoApplicato && r.Tipo == TipoAssenza.Ferie)
        {
            await _mongo.Dipendenti.UpdateOneAsync(
                d => d.Id == r.DipendenteId && d.TenantId == tid,
                Builders<Dipendente>.Update.Inc(d => d.GiorniFerieResidui, r.GiorniRichiesti));
        }

        await _mongo.RichiesteFerie.UpdateOneAsync(
            x => x.Id == id && x.TenantId == tid,
            Builders<RichiestaFerie>.Update
                .Set(x => x.Stato, StatoRichiestaFerie.Annullata)
                .Set(x => x.SaldoApplicato, false)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        TempData["flash"] = "Richiesta annullata.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> SetDecision(string id, StatoRichiestaFerie nuovoStato, string? note)
    {
        if (!(User.IsManagement() || User.IsDirettore())) return Forbid();

        var tid = _tenant.TenantId!;
        var r = await _mongo.RichiesteFerie.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (r is null) return NotFound();
        if (r.Stato != StatoRichiestaFerie.InAttesa)
        {
            TempData["flash"] = "La richiesta è già stata processata.";
            return RedirectToAction(nameof(Index));
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        var apply = nuovoStato == StatoRichiestaFerie.Approvata && r.Tipo == TipoAssenza.Ferie;

        await _mongo.RichiesteFerie.UpdateOneAsync(
            x => x.Id == id && x.TenantId == tid,
            Builders<RichiestaFerie>.Update
                .Set(x => x.Stato, nuovoStato)
                .Set(x => x.DecisoreUserId, userId)
                .Set(x => x.DecisoreIl, DateTime.UtcNow)
                .Set(x => x.NoteDecisore, note)
                .Set(x => x.SaldoApplicato, apply)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        if (apply)
        {
            await _mongo.Dipendenti.UpdateOneAsync(
                d => d.Id == r.DipendenteId && d.TenantId == tid,
                Builders<Dipendente>.Update.Inc(d => d.GiorniFerieResidui, -r.GiorniRichiesti));
        }

        await _publisher.PublishAsync(tid, "activity", new
        {
            kind = "ferie",
            title = nuovoStato == StatoRichiestaFerie.Approvata ? "Ferie approvate" : "Ferie rifiutate",
            description = $"{r.DataInizio:dd/MM} → {r.DataFine:dd/MM}",
            when = DateTime.UtcNow
        });

        TempData["flash"] = nuovoStato == StatoRichiestaFerie.Approvata
            ? $"Approvata. Saldo aggiornato di -{r.GiorniRichiesti}g."
            : "Richiesta rifiutata.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Conta i giorni lavorativi (lun-ven) inclusi gli estremi.</summary>
    public static int ContaGiorniLavorativi(DateTime inizio, DateTime fine)
    {
        if (fine < inizio) return 0;
        var days = 0;
        for (var d = inizio.Date; d <= fine.Date; d = d.AddDays(1))
        {
            if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                days++;
        }
        return days;
    }
}
