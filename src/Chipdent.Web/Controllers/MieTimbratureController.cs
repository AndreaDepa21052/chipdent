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
    private readonly Chipdent.Web.Infrastructure.Storage.IFileStorage _storage;

    public MieTimbratureController(MongoContext mongo, ITenantContext tenant, INotificationPublisher publisher,
        Chipdent.Web.Infrastructure.Storage.IFileStorage storage)
    {
        _mongo = mongo;
        _tenant = tenant;
        _publisher = publisher;
        _storage = storage;
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

        var periodo = primo.ToString("yyyy-MM");
        var correzioni = await _mongo.CorrezioniTimbrature
            .Find(c => c.TenantId == tid && c.DipendenteId == dipendente.Id
                       && c.TimestampProposto >= primo && c.TimestampProposto < fine)
            .SortByDescending(c => c.CreatedAt).ToListAsync();
        var approvazione = await _mongo.ApprovazioniTimesheet
            .Find(a => a.TenantId == tid && a.DipendenteId == dipendente.Id && a.Periodo == periodo)
            .FirstOrDefaultAsync();

        ViewData["Section"] = "mie-timbrature";
        return View(new MieTimbratureViewModel
        {
            HasLinkedDipendente = true,
            DipendenteNome = dipendente.NomeCompleto,
            ClinicaNome = clinica?.Nome ?? "—",
            Mese = primo,
            Aggregato = aggregato,
            Giorni = giorni,
            StatoCorrente = stato,
            MieCorrezioni = correzioni,
            CorrezioniPendenti = correzioni.Count(c => c.Stato == StatoCorrezione.Aperta),
            ApprovazioneMese = approvazione
        });
    }

    [HttpPost("punch")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> Punch(TipoTimbratura tipo, bool remoto = false,
        double? latitudine = null, double? longitudine = null, Microsoft.AspNetCore.Http.IFormFile? selfie = null)
    {
        var tid = _tenant.TenantId!;
        var (dip, clinica) = await ResolveLinkedDipendenteAsync(tid);
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

        // Geofencing: confronto coordinate device con sede. In smart-working il check è bypassato.
        var tenant = await _mongo.Tenants.Find(x => x.Id == tid).FirstOrDefaultAsync();
        var raggio = tenant?.RaggioGeofencingMetri ?? 200;
        double? distanza = null;
        var fuoriArea = false;
        if (!remoto && latitudine.HasValue && longitudine.HasValue && clinica is not null && clinica.IsGeolocalized)
        {
            distanza = Geo.HaversineMetri(latitudine.Value, longitudine.Value, clinica.Latitudine!.Value, clinica.Longitudine!.Value);
            if (raggio > 0 && distanza > raggio) fuoriArea = true;
        }

        // Selfie facoltativo (audit, non visibile in UI standard).
        string? selfiePath = null;
        if (selfie is { Length: > 0 } && selfie.Length <= 5 * 1024 * 1024)
        {
            var ext = Path.GetExtension(selfie.FileName).ToLowerInvariant();
            if (ext is ".jpg" or ".jpeg" or ".png" or ".webp")
            {
                await using var s = selfie.OpenReadStream();
                var stored = await _storage.SaveAsync(tid, "timbrature", $"{dip.Id}_{DateTime.UtcNow:yyyyMMddHHmmss}{ext}", s, selfie.ContentType);
                selfiePath = stored.RelativePath;
            }
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
            RegistrataDaUserId = meId,
            Latitudine = latitudine,
            Longitudine = longitudine,
            DistanzaMetri = distanza,
            FuoriArea = fuoriArea,
            SelfiePath = selfiePath
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
        var fuoriAreaTxt = fuoriArea ? $" · ⚠️ fuori area ({distanza:F0}m)" : "";
        await _publisher.PublishAsync(tid, "activity", new
        {
            kind = "shift",
            title = $"{label}: {dip.NomeCompleto}",
            description = DateTime.Now.ToString("HH:mm") + (remoto ? " · da remoto" : "") + fuoriAreaTxt,
            when = DateTime.UtcNow
        });

        TempData["flash"] = fuoriArea
            ? $"⚠️ {label} alle {DateTime.Now:HH:mm} · fuori dal raggio della sede ({distanza:F0}m). La timbratura è stata registrata e segnalata al direttore."
            : $"✓ {label} alle {DateTime.Now:HH:mm}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("correzione")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RichiediCorrezione(TipoCorrezione tipo, string? timbraturaId,
        TipoTimbratura tipoTimbratura, DateTime quando, bool remoto, string motivazione)
    {
        var tid = _tenant.TenantId!;
        var (dip, _) = await ResolveLinkedDipendenteAsync(tid);
        if (dip is null) return Forbid();
        if (string.IsNullOrWhiteSpace(motivazione))
        {
            TempData["flash"] = "Motivazione obbligatoria.";
            return RedirectToAction(nameof(Index));
        }
        await _mongo.CorrezioniTimbrature.InsertOneAsync(new CorrezioneTimbratura
        {
            TenantId = tid,
            DipendenteId = dip.Id,
            DipendenteNome = dip.NomeCompleto,
            TimbraturaId = timbraturaId,
            Tipo = tipo,
            TipoTimbraturaProposto = tipoTimbratura,
            TimestampProposto = DateTime.SpecifyKind(quando, DateTimeKind.Utc),
            RemotoProposto = remoto,
            Motivazione = motivazione.Trim(),
            Stato = StatoCorrezione.Aperta
        });
        await _publisher.PublishAsync(tid, "activity", new
        {
            kind = "shift",
            title = $"Richiesta correzione timbratura: {dip.NomeCompleto}",
            description = $"{tipo} · {quando:dd/MM HH:mm}",
            when = DateTime.UtcNow
        });
        TempData["flash"] = "Richiesta inviata al direttore.";
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
