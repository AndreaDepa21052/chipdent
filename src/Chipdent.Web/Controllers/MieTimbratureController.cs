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
[Route("mie-timbrature")]
public class MieTimbratureController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly INotificationPublisher _publisher;

    public MieTimbratureController(MongoContext mongo, ITenantContext tenant, INotificationPublisher publisher)
    {
        _mongo = mongo;
        _tenant = tenant;
        _publisher = publisher;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(DateTime? mese = null)
    {
        var tid = _tenant.TenantId!;
        ViewData["Section"] = "mie-timbrature";

        var (dipendente, clinica) = await ResolveLinkedDipendenteAsync(tid);
        if (dipendente is null)
        {
            return View(new MieTimbratureViewModel { HasLinkedDipendente = false });
        }

        var meseRif = mese ?? DateTime.Today;
        var primo = new DateTime(meseRif.Year, meseRif.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var fine = primo.AddMonths(1);

        var timbrature = await _mongo.Timbrature
            .Find(t => t.TenantId == tid && t.DipendenteId == dipendente.Id
                       && t.Timestamp >= primo && t.Timestamp < fine)
            .SortBy(t => t.Timestamp).ToListAsync();
        var turni = await _mongo.Turni
            .Find(t => t.TenantId == tid && t.PersonaId == dipendente.Id && t.TipoPersona == TipoPersona.Dipendente
                       && t.Data >= primo && t.Data < fine)
            .ToListAsync();

        var aggregato = TimbraturaCalculator.AggregaMese(dipendente.Id, timbrature, turni, primo, fine);

        var giorni = new List<TimbraturaGiorno>();
        for (var d = primo; d < fine; d = d.AddDays(1))
        {
            var tg = timbrature.Where(x => x.Timestamp.Date == d).OrderBy(x => x.Timestamp).ToList();
            var trg = turni.Where(x => x.Data.Date == d).ToList();
            if (tg.Count == 0 && trg.Count == 0) continue;
            giorni.Add(new TimbraturaGiorno(d, TimbraturaCalculator.AggregaGiorno(tg, trg), tg, trg));
        }
        giorni = giorni.OrderByDescending(g => g.Data).ToList();

        var stato = await ComputeStatoCorrenteAsync(tid, dipendente.Id);

        ViewData["Section"] = "mie-timbrature";
        return View(new MieTimbratureViewModel
        {
            HasLinkedDipendente = true,
            DipendenteNome = dipendente.NomeCompleto,
            ClinicaNome = clinica?.Nome ?? "—",
            Mese = primo,
            Aggregato = aggregato,
            Giorni = giorni,
            StatoCorrente = stato
        });
    }

    [HttpPost("punch")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Punch(TipoTimbratura tipo, bool remoto = false)
    {
        var tid = _tenant.TenantId!;
        var (dip, _) = await ResolveLinkedDipendenteAsync(tid);
        if (dip is null) return Forbid();

        // Validazione stato: non si può fare CheckIn se sei già al lavoro, ecc.
        var stato = await ComputeStatoCorrenteAsync(tid, dip.Id);
        var permesso = (tipo, stato) switch
        {
            (TipoTimbratura.CheckIn,    StatoTimbraturaCorrente.Fuori)     => true,
            (TipoTimbratura.CheckOut,   StatoTimbraturaCorrente.AlLavoro)  => true,
            (TipoTimbratura.CheckOut,   StatoTimbraturaCorrente.InPausa)   => true,
            (TipoTimbratura.PauseStart, StatoTimbraturaCorrente.AlLavoro)  => true,
            (TipoTimbratura.PauseEnd,   StatoTimbraturaCorrente.InPausa)   => true,
            _ => false
        };
        if (!permesso)
        {
            TempData["flash"] = $"Azione «{tipo}» non valida con stato corrente «{stato}».";
            return RedirectToAction(nameof(Index));
        }

        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var t = new Timbratura
        {
            TenantId = tid,
            DipendenteId = dip.Id,
            ClinicaId = dip.ClinicaId,
            Tipo = tipo,
            Timestamp = DateTime.UtcNow,
            Metodo = MetodoTimbratura.Web,
            Remoto = remoto,
            RegistrataDaUserId = meId
        };
        await _mongo.Timbrature.InsertOneAsync(t);

        var label = tipo switch
        {
            TipoTimbratura.CheckIn    => remoto ? "Inizio (remoto)" : "Inizio",
            TipoTimbratura.CheckOut   => "Fine",
            TipoTimbratura.PauseStart => "Pausa",
            TipoTimbratura.PauseEnd   => "Ripresa",
            _ => tipo.ToString()
        };
        await _publisher.PublishAsync(tid, "activity", new
        {
            kind = "shift",
            title = $"{label}: {dip.NomeCompleto}",
            description = DateTime.Now.ToString("HH:mm") + (remoto ? " · da remoto" : ""),
            when = DateTime.UtcNow
        });

        TempData["flash"] = $"✓ {label} alle {DateTime.Now:HH:mm}.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<(Dipendente? Dip, Clinica? Clinica)> ResolveLinkedDipendenteAsync(string tid)
    {
        if (User.LinkedPersonType() != "Dipendente") return (null, null);
        var linkedId = User.LinkedPersonId();
        if (string.IsNullOrEmpty(linkedId)) return (null, null);

        var dip = await _mongo.Dipendenti.Find(d => d.Id == linkedId && d.TenantId == tid).FirstOrDefaultAsync();
        if (dip is null) return (null, null);
        var clinica = await _mongo.Cliniche.Find(c => c.Id == dip.ClinicaId && c.TenantId == tid).FirstOrDefaultAsync();
        return (dip, clinica);
    }

    private async Task<StatoTimbraturaCorrente> ComputeStatoCorrenteAsync(string tid, string dipendenteId)
    {
        var oggi = DateTime.UtcNow.Date;
        var domani = oggi.AddDays(1);
        var oggi24h = await _mongo.Timbrature
            .Find(t => t.TenantId == tid && t.DipendenteId == dipendenteId
                       && t.Timestamp >= oggi && t.Timestamp < domani)
            .SortByDescending(t => t.Timestamp)
            .Limit(1)
            .FirstOrDefaultAsync();
        if (oggi24h is null) return StatoTimbraturaCorrente.Fuori;
        return oggi24h.Tipo switch
        {
            TipoTimbratura.CheckIn    => StatoTimbraturaCorrente.AlLavoro,
            TipoTimbratura.PauseEnd   => StatoTimbraturaCorrente.AlLavoro,
            TipoTimbratura.PauseStart => StatoTimbraturaCorrente.InPausa,
            TipoTimbratura.CheckOut   => StatoTimbraturaCorrente.Fuori,
            _ => StatoTimbraturaCorrente.Fuori
        };
    }
}
