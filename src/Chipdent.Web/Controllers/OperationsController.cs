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
/// Operations da retail per il dental: ronda sicurezza apertura/chiusura sede + inventario consumabili.
/// </summary>
[Authorize]
[Route("operations")]
public class OperationsController : Controller
{
    private static readonly string[] DefaultChecklistApertura =
    {
        "Disattivazione allarme",
        "Verifica chiavi e accessi",
        "Accensione riuniti e compressore",
        "Frigo farmaci a temperatura corretta (2-8 °C)",
        "Autoclave in pre-riscaldamento",
        "Sterilizzatrice operativa",
        "Pulizia visiva sale e sala riposo",
        "Verifica luci di emergenza"
    };

    private static readonly string[] DefaultChecklistChiusura =
    {
        "Spegnimento riuniti e compressore",
        "Pulizia e sanificazione sale",
        "Verifica frigo farmaci chiuso",
        "Spegnimento autoclave / sterilizzatrice",
        "Smaltimento rifiuti speciali",
        "Spegnimento luci e PC",
        "Chiusura accessi e finestre",
        "Inserimento allarme"
    };

    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly INotificationPublisher _publisher;

    public OperationsController(MongoContext mongo, ITenantContext tenant, INotificationPublisher publisher)
    {
        _mongo = mongo;
        _tenant = tenant;
        _publisher = publisher;
    }

    // ─── Ronda sicurezza ────────────────────────────────────────────

    [HttpGet("ronda")]
    public async Task<IActionResult> RondaIndex(string? clinicaId = null, int giorni = 30)
    {
        var tid = _tenant.TenantId!;
        var since = DateTime.UtcNow.AddDays(-giorni);
        var f = Builders<RondaSicurezza>.Filter.Eq(r => r.TenantId, tid)
              & Builders<RondaSicurezza>.Filter.Gte(r => r.DataOra, since);
        if (!string.IsNullOrEmpty(clinicaId)) f &= Builders<RondaSicurezza>.Filter.Eq(r => r.ClinicaId, clinicaId);
        if (_tenant.IsClinicaScoped) f &= Builders<RondaSicurezza>.Filter.In(r => r.ClinicaId, _tenant.ClinicaIds);

        var ronde = await _mongo.RondeSicurezza.Find(f).SortByDescending(r => r.DataOra).Limit(200).ToListAsync();
        var cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync();
        var clinicheMap = cliniche.ToDictionary(c => c.Id, c => c.Nome);

        ViewData["Section"] = "operations";
        return View(new RondaIndexViewModel
        {
            Cliniche = cliniche,
            ClinicaIdFilter = clinicaId,
            Giorni = giorni,
            Ronde = ronde.Select(r => new RondaRow(r, clinicheMap.GetValueOrDefault(r.ClinicaId, "—"))).ToList(),
            AperturaOggi = ronde.Any(r => r.Tipo == TipoRonda.Apertura && r.DataOra.Date == DateTime.UtcNow.Date),
            ChiusuraOggi = ronde.Any(r => r.Tipo == TipoRonda.Chiusura && r.DataOra.Date == DateTime.UtcNow.Date),
            AnomalieAperte = (int)await _mongo.RondeSicurezza.CountDocumentsAsync(Builders<RondaSicurezza>.Filter.Eq(r => r.TenantId, tid) & Builders<RondaSicurezza>.Filter.Eq(r => r.HaAnomalie, true) & Builders<RondaSicurezza>.Filter.Gte(r => r.DataOra, since))
        });
    }

    [HttpGet("ronda/nuova")]
    public async Task<IActionResult> RondaNuova(TipoRonda tipo = TipoRonda.Apertura, string? clinicaId = null)
    {
        var tid = _tenant.TenantId!;
        var cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).SortBy(c => c.Nome).ToListAsync();
        if (_tenant.IsClinicaScoped) cliniche = cliniche.Where(c => _tenant.ClinicaIds.Contains(c.Id)).ToList();

        var checklist = (tipo == TipoRonda.Apertura ? DefaultChecklistApertura : DefaultChecklistChiusura)
            .Select(l => new RondaItem { Label = l }).ToList();

        ViewData["Section"] = "operations";
        return View(new RondaFormViewModel
        {
            Tipo = tipo,
            ClinicaId = clinicaId ?? cliniche.FirstOrDefault()?.Id,
            Cliniche = cliniche,
            Items = checklist
        });
    }

    [HttpPost("ronda/nuova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RondaNuova(RondaFormViewModel vm)
    {
        var tid = _tenant.TenantId!;
        if (string.IsNullOrEmpty(vm.ClinicaId)) ModelState.AddModelError(nameof(vm.ClinicaId), "Sede obbligatoria.");
        if (vm.Items.Count == 0) ModelState.AddModelError("", "Checklist vuota.");
        if (!ModelState.IsValid)
        {
            vm.Cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync();
            return View(vm);
        }

        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var meName = User.Identity?.Name ?? "Utente";
        var anomalie = vm.Items.Any(i => !i.Ok);
        var ronda = new RondaSicurezza
        {
            TenantId = tid,
            ClinicaId = vm.ClinicaId!,
            Tipo = vm.Tipo,
            DataOra = DateTime.UtcNow,
            EseguitaDaUserId = meId,
            EseguitaDaNome = meName,
            Items = vm.Items,
            HaAnomalie = anomalie,
            Note = string.IsNullOrWhiteSpace(vm.Note) ? null : vm.Note.Trim()
        };
        await _mongo.RondeSicurezza.InsertOneAsync(ronda);

        if (anomalie)
        {
            await _publisher.PublishAsync(tid, "activity", new
            {
                kind = "comm",
                title = $"⚠️ Ronda {vm.Tipo.ToString().ToLower()} con anomalie",
                description = $"{meName} · {vm.Items.Count(i => !i.Ok)} item KO",
                when = DateTime.UtcNow
            });
        }

        TempData["flash"] = anomalie ? "⚠️ Ronda salvata con anomalie segnalate." : "✓ Ronda completata.";
        return RedirectToAction(nameof(RondaIndex));
    }

    // ─── Inventario consumabili ─────────────────────────────────────

    [HttpGet("inventario")]
    public async Task<IActionResult> InventarioIndex(string? clinicaId = null)
    {
        var tid = _tenant.TenantId!;
        var f = Builders<Consumabile>.Filter.Eq(x => x.TenantId, tid) & Builders<Consumabile>.Filter.Eq(x => x.Attivo, true);
        if (!string.IsNullOrEmpty(clinicaId)) f &= Builders<Consumabile>.Filter.Eq(x => x.ClinicaId, clinicaId);
        if (_tenant.IsClinicaScoped) f &= Builders<Consumabile>.Filter.In(x => x.ClinicaId, _tenant.ClinicaIds);

        var consumabili = await _mongo.Consumabili.Find(f).SortBy(x => x.Nome).ToListAsync();
        var cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync();
        var clinicheMap = cliniche.ToDictionary(c => c.Id, c => c.Nome);

        ViewData["Section"] = "operations";
        return View(new InventarioIndexViewModel
        {
            Cliniche = cliniche,
            ClinicaIdFilter = clinicaId,
            Items = consumabili.Select(c => new InventarioRow(c, clinicheMap.GetValueOrDefault(c.ClinicaId, "—"))).ToList(),
            SottoSoglia = consumabili.Count(c => c.GiacenzaCorrente <= c.SogliaMinima)
        });
    }

    [HttpGet("inventario/nuovo")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    public async Task<IActionResult> ConsumabileNuovo()
    {
        var tid = _tenant.TenantId!;
        var cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).SortBy(c => c.Nome).ToListAsync();
        ViewData["Section"] = "operations";
        return View(new ConsumabileFormViewModel { Cliniche = cliniche });
    }

    [HttpPost("inventario/nuovo")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConsumabileNuovo(ConsumabileFormViewModel vm)
    {
        var tid = _tenant.TenantId!;
        if (!ModelState.IsValid)
        {
            vm.Cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync();
            return View(vm);
        }
        await _mongo.Consumabili.InsertOneAsync(new Consumabile
        {
            TenantId = tid,
            ClinicaId = vm.ClinicaId,
            Nome = vm.Nome.Trim(),
            Categoria = vm.Categoria,
            UnitaMisura = vm.UnitaMisura,
            GiacenzaCorrente = vm.GiacenzaCorrente,
            SogliaMinima = vm.SogliaMinima,
            Fornitore = vm.Fornitore,
            CodiceFornitore = vm.CodiceFornitore
        });
        TempData["flash"] = "✓ Consumabile aggiunto.";
        return RedirectToAction(nameof(InventarioIndex));
    }

    [HttpPost("inventario/{id}/movimento")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Movimento(string id, TipoMovimento tipo, int quantita, string? motivo = null)
    {
        var tid = _tenant.TenantId!;
        if (quantita <= 0) { TempData["flash"] = "Quantità deve essere > 0."; return RedirectToAction(nameof(InventarioIndex)); }
        var c = await _mongo.Consumabili.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (c is null) return NotFound();

        var delta = tipo switch
        {
            TipoMovimento.Carico => quantita,
            TipoMovimento.Scarico => -quantita,
            TipoMovimento.Rettifica => quantita - c.GiacenzaCorrente,
            _ => 0
        };
        var nuovaGiacenza = Math.Max(0, c.GiacenzaCorrente + delta);

        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        await _mongo.MovimentiConsumabili.InsertOneAsync(new MovimentoConsumabile
        {
            TenantId = tid,
            ConsumabileId = c.Id,
            ClinicaId = c.ClinicaId,
            Tipo = tipo,
            Quantita = Math.Abs(delta),
            Motivo = motivo,
            EseguitoDaUserId = meId
        });

        await _mongo.Consumabili.UpdateOneAsync(x => x.Id == id && x.TenantId == tid,
            Builders<Consumabile>.Update
                .Set(x => x.GiacenzaCorrente, nuovaGiacenza)
                .Set(x => x.UltimoMovimentoAt, DateTime.UtcNow)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        // Alert se sotto soglia
        if (nuovaGiacenza <= c.SogliaMinima && c.GiacenzaCorrente > c.SogliaMinima)
        {
            await _publisher.PublishAsync(tid, "activity", new
            {
                kind = "comm",
                title = $"📦 Riordinare: {c.Nome}",
                description = $"Giacenza scesa a {nuovaGiacenza} (soglia {c.SogliaMinima})",
                when = DateTime.UtcNow
            });
        }

        TempData["flash"] = $"✓ {tipo} di {Math.Abs(delta)}. Nuova giacenza: {nuovaGiacenza}.";
        return RedirectToAction(nameof(InventarioIndex));
    }
}
