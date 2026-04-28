using System.Security.Claims;
using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize]
[Route("dpi")]
public class DpiController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;

    public DpiController(MongoContext mongo, ITenantContext tenant)
    {
        _mongo = mongo;
        _tenant = tenant;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var tid = _tenant.TenantId!;
        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var canManage = User.IsManagement() || User.IsDirettore();

        var catalogo = await _mongo.Dpi.Find(d => d.TenantId == tid).SortBy(d => d.Tipo).ToListAsync();
        var cliniche = (await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync())
            .ToDictionary(c => c.Id, c => c.Nome);
        var dipendenti = (await _mongo.Dipendenti.Find(d => d.TenantId == tid).ToListAsync())
            .ToDictionary(d => d.Id);

        var consegneFilter = Builders<ConsegnaDpi>.Filter.Eq(x => x.TenantId, tid);
        if (!canManage)
        {
            // Staff vede solo le sue consegne
            var meUser = await _mongo.Users.Find(u => u.Id == meId && u.TenantId == tid).FirstOrDefaultAsync();
            if (meUser?.LinkedPersonType == LinkedPersonType.Dipendente && !string.IsNullOrEmpty(meUser.LinkedPersonId))
                consegneFilter &= Builders<ConsegnaDpi>.Filter.Eq(x => x.DipendenteId, meUser.LinkedPersonId);
            else
                consegneFilter &= Builders<ConsegnaDpi>.Filter.Eq(x => x.DipendenteId, "__none__");
        }
        var consegne = await _mongo.ConsegneDpi.Find(consegneFilter)
            .SortByDescending(x => x.DataConsegna).Limit(200).ToListAsync();
        var dpiLookup = catalogo.ToDictionary(d => d.Id);

        var consegneRows = consegne.Select(cn => new ConsegnaDpiRow(
            cn,
            dpiLookup.GetValueOrDefault(cn.DpiId),
            dipendenti.GetValueOrDefault(cn.DipendenteId)?.NomeCompleto ?? "—",
            cliniche.GetValueOrDefault(cn.ClinicaId, "—"))).ToList();

        var oggi = DateTime.UtcNow;
        var soon = oggi.AddDays(30);

        ViewData["Section"] = "dpi";
        return View(new DpiIndexViewModel
        {
            Catalogo = catalogo.Select(d => new DpiRow(d,
                cliniche.GetValueOrDefault(d.ClinicaId, "—"),
                consegne.Count(c => c.DpiId == d.Id))).ToList(),
            Consegne = consegneRows,
            InAttesaFirma = consegneRows.Count(c => c.Consegna.Stato == StatoConsegnaDpi.InAttesaFirma),
            InScadenza = consegneRows.Count(c => c.Consegna.ScadenzaSostituzione is not null
                                                  && c.Consegna.ScadenzaSostituzione >= oggi
                                                  && c.Consegna.ScadenzaSostituzione < soon),
            Scadute = consegneRows.Count(c => c.Consegna.ScadenzaSostituzione is not null
                                              && c.Consegna.ScadenzaSostituzione < oggi
                                              && c.Consegna.Stato != StatoConsegnaDpi.Sostituita),
            CanManage = canManage
        });
    }

    // ───── Catalogo DPI ─────

    [HttpGet("catalogo/nuovo")]
    [Authorize(Policy = Policies.RequireDirettore)]
    public async Task<IActionResult> CreateDpi()
    {
        ViewData["Section"] = "dpi"; ViewData["IsNew"] = true;
        return View("FormCatalogo", new DpiCatalogoFormViewModel
        {
            Cliniche = await ClinicheAsync()
        });
    }

    [HttpPost("catalogo/nuovo")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDpi(DpiCatalogoFormViewModel vm)
    {
        if (!ModelState.IsValid || string.IsNullOrEmpty(vm.ClinicaId))
        {
            ViewData["Section"] = "dpi"; ViewData["IsNew"] = true;
            vm.Cliniche = await ClinicheAsync();
            return View("FormCatalogo", vm);
        }
        await _mongo.Dpi.InsertOneAsync(new Dpi
        {
            TenantId = _tenant.TenantId!,
            ClinicaId = vm.ClinicaId,
            Tipo = vm.Tipo,
            Nome = vm.Nome.Trim(),
            Modello = vm.Modello,
            Codice = vm.Codice,
            IntervalloSostituzioneGiorni = vm.IntervalloSostituzioneGiorni,
            Note = vm.Note,
            Attivo = true
        });
        TempData["flash"] = "DPI aggiunto al catalogo.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("catalogo/{id}/elimina")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDpi(string id)
    {
        await _mongo.Dpi.DeleteOneAsync(d => d.Id == id && d.TenantId == _tenant.TenantId);
        TempData["flash"] = "DPI rimosso dal catalogo.";
        return RedirectToAction(nameof(Index));
    }

    // ───── Consegne ─────

    [HttpGet("consegna/nuova")]
    [Authorize(Policy = Policies.RequireDirettore)]
    public async Task<IActionResult> Consegna(string? dpiId = null, string? dipendenteId = null)
    {
        ViewData["Section"] = "dpi";
        var tid = _tenant.TenantId!;
        var dpi = await _mongo.Dpi.Find(d => d.TenantId == tid && d.Attivo).SortBy(d => d.Nome).ToListAsync();
        var dips = await _mongo.Dipendenti.Find(d => d.TenantId == tid && d.Stato != StatoDipendente.Cessato)
                                          .SortBy(d => d.Cognome).ToListAsync();
        return View(new NuovaConsegnaDpiViewModel
        {
            DpiId = dpiId ?? string.Empty,
            DipendenteId = dipendenteId ?? string.Empty,
            DpiDisponibili = dpi,
            Dipendenti = dips
        });
    }

    [HttpPost("consegna/nuova")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Consegna(NuovaConsegnaDpiViewModel vm)
    {
        var tid = _tenant.TenantId!;
        var dpi = await _mongo.Dpi.Find(d => d.Id == vm.DpiId && d.TenantId == tid).FirstOrDefaultAsync();
        var dip = await _mongo.Dipendenti.Find(d => d.Id == vm.DipendenteId && d.TenantId == tid).FirstOrDefaultAsync();

        if (dpi is null) ModelState.AddModelError(nameof(vm.DpiId), "DPI non valido.");
        if (dip is null) ModelState.AddModelError(nameof(vm.DipendenteId), "Dipendente non valido.");

        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "dpi";
            vm.DpiDisponibili = await _mongo.Dpi.Find(d => d.TenantId == tid && d.Attivo).SortBy(d => d.Nome).ToListAsync();
            vm.Dipendenti = await _mongo.Dipendenti.Find(d => d.TenantId == tid && d.Stato != StatoDipendente.Cessato).SortBy(d => d.Cognome).ToListAsync();
            return View(vm);
        }

        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var meName = User.Identity?.Name ?? "";

        var scadenza = vm.ScadenzaSostituzione;
        if (scadenza is null && dpi!.IntervalloSostituzioneGiorni is { } gg)
            scadenza = DateTime.UtcNow.AddDays(gg);

        var c = new ConsegnaDpi
        {
            TenantId = tid,
            DpiId = dpi!.Id,
            DipendenteId = dip!.Id,
            ClinicaId = dip.ClinicaId,
            DataConsegna = DateTime.UtcNow,
            Quantita = Math.Max(1, vm.Quantita),
            ScadenzaSostituzione = scadenza is null ? null : DateTime.SpecifyKind(scadenza.Value.Date, DateTimeKind.Utc),
            ConsegnaDaUserId = meId,
            ConsegnaDaNome = meName,
            Note = vm.Note,
            Stato = StatoConsegnaDpi.InAttesaFirma
        };
        await _mongo.ConsegneDpi.InsertOneAsync(c);

        TempData["flash"] = $"Consegna registrata per {dip.NomeCompleto}. In attesa di firma del dipendente.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("consegna/{id}/firma")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Firma(string id)
    {
        var tid = _tenant.TenantId!;
        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var meName = User.Identity?.Name ?? "";

        var c = await _mongo.ConsegneDpi.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (c is null) return NotFound();

        // Solo il dipendente destinatario (o un Direttore/Management che firma "per lui") può firmare
        var meUser = await _mongo.Users.Find(u => u.Id == meId && u.TenantId == tid).FirstOrDefaultAsync();
        var isOwner = meUser?.LinkedPersonType == LinkedPersonType.Dipendente && meUser.LinkedPersonId == c.DipendenteId;
        if (!isOwner && !User.IsManagement() && !User.IsDirettore()) return Forbid();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        await _mongo.ConsegneDpi.UpdateOneAsync(
            x => x.Id == id && x.TenantId == tid,
            Builders<ConsegnaDpi>.Update
                .Set(x => x.Stato, StatoConsegnaDpi.Firmata)
                .Set(x => x.FirmaIl, DateTime.UtcNow)
                .Set(x => x.FirmaIp, ip)
                .Set(x => x.FirmaUserId, meId)
                .Set(x => x.FirmaNome, meName)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        TempData["flash"] = "Firma registrata. Grazie.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("consegna/{id}/sostituisci")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sostituisci(string id)
    {
        var tid = _tenant.TenantId!;
        await _mongo.ConsegneDpi.UpdateOneAsync(
            x => x.Id == id && x.TenantId == tid,
            Builders<ConsegnaDpi>.Update
                .Set(x => x.Stato, StatoConsegnaDpi.Sostituita)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));
        TempData["flash"] = "Consegna marcata come sostituita.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("consegna/{id}/elimina")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConsegna(string id)
    {
        await _mongo.ConsegneDpi.DeleteOneAsync(x => x.Id == id && x.TenantId == _tenant.TenantId);
        TempData["flash"] = "Consegna eliminata.";
        return RedirectToAction(nameof(Index));
    }

    private Task<List<Clinica>> ClinicheAsync()
        => _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId).SortBy(c => c.Nome).ToListAsync();
}
