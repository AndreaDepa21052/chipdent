using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize(Roles = Policies.StaffRoles)]
[Route("rls")]
public class RlsController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;

    public RlsController(MongoContext mongo, ITenantContext tenant)
    {
        _mongo = mongo;
        _tenant = tenant;
    }

    // ---------- Overview ----------

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var tid = _tenant.TenantId!;
        var soon = DateTime.UtcNow.AddMonths(3);
        var now = DateTime.UtcNow;

        var visite = await _mongo.VisiteMediche.Find(v => v.TenantId == tid).ToListAsync();
        var corsi = await _mongo.Corsi.Find(c => c.TenantId == tid).ToListAsync();
        var dvr = await _mongo.DVRs.Find(d => d.TenantId == tid).ToListAsync();
        var dipLookup = (await _mongo.Dipendenti.Find(d => d.TenantId == tid).ToListAsync())
            .ToDictionary(d => d.Id, d => d.NomeCompleto);
        var dotLookup = (await _mongo.Dottori.Find(d => d.TenantId == tid).ToListAsync())
            .ToDictionary(d => d.Id, d => d.NomeCompleto);
        var clinLookup = (await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync())
            .ToDictionary(c => c.Id, c => c.Nome);

        var alerts = new List<RlsAlertItem>();
        foreach (var v in visite.Where(v => v.ScadenzaIdoneita is not null && v.ScadenzaIdoneita < soon))
        {
            var name = dipLookup.GetValueOrDefault(v.DipendenteId, "—");
            var sev = v.ScadenzaIdoneita < now ? "danger" : "warning";
            alerts.Add(new RlsAlertItem("visita", $"Idoneità {name}", $"scade il {v.ScadenzaIdoneita:dd/MM/yyyy}", v.ScadenzaIdoneita, sev));
        }
        foreach (var c in corsi.Where(c => c.Scadenza is not null && c.Scadenza < soon))
        {
            var name = c.DestinatarioTipo switch
            {
                DestinatarioCorso.Dottore => dotLookup.GetValueOrDefault(c.DestinatarioId, "—"),
                DestinatarioCorso.Dipendente => dipLookup.GetValueOrDefault(c.DestinatarioId, "—"),
                _ => clinLookup.GetValueOrDefault(c.DestinatarioId, "—")
            };
            var sev = c.Scadenza < now ? "danger" : "warning";
            alerts.Add(new RlsAlertItem("corso", $"{c.Tipo} — {name}", $"scade il {c.Scadenza:dd/MM/yyyy}", c.Scadenza, sev));
        }
        foreach (var d in dvr.Where(d => d.ProssimaRevisione is not null && d.ProssimaRevisione < soon))
        {
            var name = clinLookup.GetValueOrDefault(d.ClinicaId, "—");
            var sev = d.ProssimaRevisione < now ? "danger" : "warning";
            alerts.Add(new RlsAlertItem("dvr", $"DVR {name} v{d.Versione}", $"revisione entro {d.ProssimaRevisione:dd/MM/yyyy}", d.ProssimaRevisione, sev));
        }

        ViewData["Section"] = "rls";
        return View(new RlsOverviewViewModel
        {
            VisiteScadenzaVicina = visite.Count(v => v.ScadenzaIdoneita >= now && v.ScadenzaIdoneita < soon),
            VisiteScadute = visite.Count(v => v.ScadenzaIdoneita < now),
            CorsiScadenzaVicina = corsi.Count(c => c.Scadenza >= now && c.Scadenza < soon),
            CorsiScaduti = corsi.Count(c => c.Scadenza < now),
            DvrInScadenza = dvr.Count(d => d.ProssimaRevisione is not null && d.ProssimaRevisione < soon),
            DvrDaApprovare = dvr.Count(d => d.Stato == StatoDVR.Bozza || d.Stato == StatoDVR.DaRivedere),
            Alerts = alerts.OrderBy(a => a.Quando).ToList()
        });
    }

    // ---------- Visite mediche ----------

    [HttpGet("visite")]
    public async Task<IActionResult> Visite()
    {
        var tid = _tenant.TenantId!;
        var visite = await _mongo.VisiteMediche.Find(v => v.TenantId == tid).SortByDescending(v => v.Data).ToListAsync();
        var dipLookup = (await _mongo.Dipendenti.Find(d => d.TenantId == tid).ToListAsync())
            .ToDictionary(d => d.Id, d => d.NomeCompleto);
        ViewData["Section"] = "rls";
        ViewData["Tab"] = "visite";
        ViewData["Dipendenti"] = dipLookup;
        return View(visite);
    }

    [HttpGet("visite/nuova")]
    [Authorize(Policy = Policies.RequireHR)]
    public async Task<IActionResult> CreateVisita()
    {
        ViewData["Section"] = "rls";
        ViewData["Tab"] = "visite";
        ViewData["IsNew"] = true;
        var vm = new VisitaFormViewModel { Dipendenti = await DipendentiAsync() };
        return View("VisitaForm", vm);
    }

    [HttpPost("visite/nuova")]
    [Authorize(Policy = Policies.RequireHR)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateVisita(VisitaFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "rls"; ViewData["Tab"] = "visite"; ViewData["IsNew"] = true;
            vm.Dipendenti = await DipendentiAsync();
            return View("VisitaForm", vm);
        }
        await _mongo.VisiteMediche.InsertOneAsync(new VisitaMedica
        {
            TenantId = _tenant.TenantId!,
            DipendenteId = vm.DipendenteId,
            Data = vm.Data.Date,
            Esito = vm.Esito,
            ScadenzaIdoneita = vm.ScadenzaIdoneita,
            Note = vm.Note
        });
        TempData["flash"] = "Visita medica registrata.";
        return RedirectToAction(nameof(Visite));
    }

    [HttpGet("visite/{id}/modifica")]
    [Authorize(Policy = Policies.RequireHR)]
    public async Task<IActionResult> EditVisita(string id)
    {
        var v = await _mongo.VisiteMediche.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (v is null) return NotFound();
        ViewData["Section"] = "rls"; ViewData["Tab"] = "visite"; ViewData["IsNew"] = false;
        var vm = new VisitaFormViewModel
        {
            Id = v.Id, DipendenteId = v.DipendenteId, Data = v.Data, Esito = v.Esito,
            ScadenzaIdoneita = v.ScadenzaIdoneita, Note = v.Note,
            Dipendenti = await DipendentiAsync()
        };
        return View("VisitaForm", vm);
    }

    [HttpPost("visite/{id}/modifica")]
    [Authorize(Policy = Policies.RequireHR)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditVisita(string id, VisitaFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "rls"; ViewData["Tab"] = "visite"; ViewData["IsNew"] = false;
            vm.Dipendenti = await DipendentiAsync();
            return View("VisitaForm", vm);
        }
        await _mongo.VisiteMediche.UpdateOneAsync(
            x => x.Id == id && x.TenantId == _tenant.TenantId,
            Builders<VisitaMedica>.Update
                .Set(x => x.DipendenteId, vm.DipendenteId)
                .Set(x => x.Data, vm.Data.Date)
                .Set(x => x.Esito, vm.Esito)
                .Set(x => x.ScadenzaIdoneita, vm.ScadenzaIdoneita)
                .Set(x => x.Note, vm.Note)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));
        TempData["flash"] = "Visita aggiornata.";
        return RedirectToAction(nameof(Visite));
    }

    [HttpPost("visite/{id}/elimina")]
    [Authorize(Policy = Policies.RequireAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteVisita(string id)
    {
        await _mongo.VisiteMediche.DeleteOneAsync(x => x.Id == id && x.TenantId == _tenant.TenantId);
        TempData["flash"] = "Visita eliminata.";
        return RedirectToAction(nameof(Visite));
    }

    // ---------- Corsi ----------

    [HttpGet("corsi")]
    public async Task<IActionResult> Corsi()
    {
        var tid = _tenant.TenantId!;
        var corsi = await _mongo.Corsi.Find(c => c.TenantId == tid).SortByDescending(c => c.DataConseguimento).ToListAsync();
        ViewData["Dipendenti"] = (await _mongo.Dipendenti.Find(d => d.TenantId == tid).ToListAsync()).ToDictionary(d => d.Id, d => d.NomeCompleto);
        ViewData["Dottori"] = (await _mongo.Dottori.Find(d => d.TenantId == tid).ToListAsync()).ToDictionary(d => d.Id, d => d.NomeCompleto);
        ViewData["Cliniche"] = (await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync()).ToDictionary(c => c.Id, c => c.Nome);
        ViewData["Section"] = "rls"; ViewData["Tab"] = "corsi";
        return View(corsi);
    }

    [HttpGet("corsi/nuovo")]
    [Authorize(Policy = Policies.RequireHR)]
    public async Task<IActionResult> CreateCorso()
    {
        ViewData["Section"] = "rls"; ViewData["Tab"] = "corsi"; ViewData["IsNew"] = true;
        return View("CorsoForm", await HydrateCorso(new CorsoFormViewModel()));
    }

    [HttpPost("corsi/nuovo")]
    [Authorize(Policy = Policies.RequireHR)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCorso(CorsoFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "rls"; ViewData["Tab"] = "corsi"; ViewData["IsNew"] = true;
            return View("CorsoForm", await HydrateCorso(vm));
        }
        await _mongo.Corsi.InsertOneAsync(new Corso
        {
            TenantId = _tenant.TenantId!,
            DestinatarioId = vm.DestinatarioId,
            DestinatarioTipo = vm.DestinatarioTipo,
            Tipo = vm.Tipo,
            DataConseguimento = vm.DataConseguimento.Date,
            Scadenza = vm.Scadenza,
            Note = vm.Note
        });
        TempData["flash"] = "Corso registrato.";
        return RedirectToAction(nameof(Corsi));
    }

    [HttpGet("corsi/{id}/modifica")]
    [Authorize(Policy = Policies.RequireHR)]
    public async Task<IActionResult> EditCorso(string id)
    {
        var c = await _mongo.Corsi.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (c is null) return NotFound();
        ViewData["Section"] = "rls"; ViewData["Tab"] = "corsi"; ViewData["IsNew"] = false;
        var vm = new CorsoFormViewModel
        {
            Id = c.Id, DestinatarioId = c.DestinatarioId, DestinatarioTipo = c.DestinatarioTipo,
            Tipo = c.Tipo, DataConseguimento = c.DataConseguimento, Scadenza = c.Scadenza, Note = c.Note
        };
        return View("CorsoForm", await HydrateCorso(vm));
    }

    [HttpPost("corsi/{id}/modifica")]
    [Authorize(Policy = Policies.RequireHR)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCorso(string id, CorsoFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "rls"; ViewData["Tab"] = "corsi"; ViewData["IsNew"] = false;
            return View("CorsoForm", await HydrateCorso(vm));
        }
        await _mongo.Corsi.UpdateOneAsync(
            x => x.Id == id && x.TenantId == _tenant.TenantId,
            Builders<Corso>.Update
                .Set(x => x.DestinatarioId, vm.DestinatarioId)
                .Set(x => x.DestinatarioTipo, vm.DestinatarioTipo)
                .Set(x => x.Tipo, vm.Tipo)
                .Set(x => x.DataConseguimento, vm.DataConseguimento.Date)
                .Set(x => x.Scadenza, vm.Scadenza)
                .Set(x => x.Note, vm.Note)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));
        TempData["flash"] = "Corso aggiornato.";
        return RedirectToAction(nameof(Corsi));
    }

    [HttpPost("corsi/{id}/elimina")]
    [Authorize(Policy = Policies.RequireAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCorso(string id)
    {
        await _mongo.Corsi.DeleteOneAsync(x => x.Id == id && x.TenantId == _tenant.TenantId);
        TempData["flash"] = "Corso eliminato.";
        return RedirectToAction(nameof(Corsi));
    }

    // ---------- DVR ----------

    [HttpGet("dvr")]
    public async Task<IActionResult> Dvr()
    {
        var tid = _tenant.TenantId!;
        var dvrs = await _mongo.DVRs.Find(d => d.TenantId == tid).SortByDescending(d => d.DataApprovazione).ToListAsync();
        ViewData["Cliniche"] = (await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync()).ToDictionary(c => c.Id, c => c.Nome);
        ViewData["Section"] = "rls"; ViewData["Tab"] = "dvr";
        return View(dvrs);
    }

    [HttpGet("dvr/nuovo")]
    [Authorize(Policy = Policies.RequireManager)]
    public async Task<IActionResult> CreateDvr()
    {
        ViewData["Section"] = "rls"; ViewData["Tab"] = "dvr"; ViewData["IsNew"] = true;
        return View("DvrForm", new DvrFormViewModel { Cliniche = await CliniceAsync() });
    }

    [HttpPost("dvr/nuovo")]
    [Authorize(Policy = Policies.RequireManager)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDvr(DvrFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "rls"; ViewData["Tab"] = "dvr"; ViewData["IsNew"] = true;
            vm.Cliniche = await CliniceAsync();
            return View("DvrForm", vm);
        }
        await _mongo.DVRs.InsertOneAsync(new DVR
        {
            TenantId = _tenant.TenantId!,
            ClinicaId = vm.ClinicaId,
            Versione = vm.Versione,
            DataApprovazione = vm.DataApprovazione.Date,
            ProssimaRevisione = vm.ProssimaRevisione,
            Stato = vm.Stato,
            Note = vm.Note
        });
        TempData["flash"] = "DVR creato.";
        return RedirectToAction(nameof(Dvr));
    }

    [HttpGet("dvr/{id}/modifica")]
    [Authorize(Policy = Policies.RequireManager)]
    public async Task<IActionResult> EditDvr(string id)
    {
        var d = await _mongo.DVRs.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (d is null) return NotFound();
        ViewData["Section"] = "rls"; ViewData["Tab"] = "dvr"; ViewData["IsNew"] = false;
        return View("DvrForm", new DvrFormViewModel
        {
            Id = d.Id, ClinicaId = d.ClinicaId, Versione = d.Versione,
            DataApprovazione = d.DataApprovazione, ProssimaRevisione = d.ProssimaRevisione,
            Stato = d.Stato, Note = d.Note,
            Cliniche = await CliniceAsync()
        });
    }

    [HttpPost("dvr/{id}/modifica")]
    [Authorize(Policy = Policies.RequireManager)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditDvr(string id, DvrFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "rls"; ViewData["Tab"] = "dvr"; ViewData["IsNew"] = false;
            vm.Cliniche = await CliniceAsync();
            return View("DvrForm", vm);
        }
        await _mongo.DVRs.UpdateOneAsync(
            x => x.Id == id && x.TenantId == _tenant.TenantId,
            Builders<DVR>.Update
                .Set(x => x.ClinicaId, vm.ClinicaId)
                .Set(x => x.Versione, vm.Versione)
                .Set(x => x.DataApprovazione, vm.DataApprovazione.Date)
                .Set(x => x.ProssimaRevisione, vm.ProssimaRevisione)
                .Set(x => x.Stato, vm.Stato)
                .Set(x => x.Note, vm.Note)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));
        TempData["flash"] = "DVR aggiornato.";
        return RedirectToAction(nameof(Dvr));
    }

    [HttpPost("dvr/{id}/elimina")]
    [Authorize(Policy = Policies.RequireAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDvr(string id)
    {
        await _mongo.DVRs.DeleteOneAsync(x => x.Id == id && x.TenantId == _tenant.TenantId);
        TempData["flash"] = "DVR eliminato.";
        return RedirectToAction(nameof(Dvr));
    }

    // ---------- helpers ----------

    private Task<List<Dipendente>> DipendentiAsync()
        => _mongo.Dipendenti.Find(d => d.TenantId == _tenant.TenantId).SortBy(d => d.Cognome).ToListAsync();

    private Task<List<Clinica>> CliniceAsync()
        => _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId).SortBy(c => c.Nome).ToListAsync();

    private async Task<CorsoFormViewModel> HydrateCorso(CorsoFormViewModel vm)
    {
        var tid = _tenant.TenantId!;
        vm.Dottori = await _mongo.Dottori.Find(d => d.TenantId == tid).SortBy(d => d.Cognome).ToListAsync();
        vm.Dipendenti = await DipendentiAsync();
        vm.Cliniche = await CliniceAsync();
        return vm;
    }
}
