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
[Route("turni")]
public class TurniController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly INotificationPublisher _publisher;

    public TurniController(MongoContext mongo, ITenantContext tenant, INotificationPublisher publisher)
    {
        _mongo = mongo;
        _tenant = tenant;
        _publisher = publisher;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(DateTime? week = null)
    {
        var weekStart = StartOfWeek(week ?? DateTime.Today);
        var weekEnd = weekStart.AddDays(7);

        var dottori = await _mongo.Dottori
            .Find(d => d.TenantId == _tenant.TenantId && d.Attivo)
            .SortBy(d => d.Cognome).ToListAsync();
        var dipendenti = await _mongo.Dipendenti
            .Find(d => d.TenantId == _tenant.TenantId && d.Stato != StatoDipendente.Cessato)
            .SortBy(d => d.Cognome).ToListAsync();
        var turni = await _mongo.Turni
            .Find(t => t.TenantId == _tenant.TenantId && t.Data >= weekStart && t.Data < weekEnd)
            .ToListAsync();
        var cliniche = (await _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId).ToListAsync())
            .ToDictionary(c => c.Id, c => c.Nome);

        var linkedPersonId = User.LinkedPersonId();
        var linkedPersonType = User.LinkedPersonType();
        var restrictToSelf = !User.IsFullAccess()
                             && !User.IsInRole("Manager")
                             && !User.IsInRole("HR")
                             && linkedPersonId is not null;

        var righe = new List<PersonaRow>();
        foreach (var d in dottori)
        {
            if (restrictToSelf && (linkedPersonType != "Dottore" || d.Id != linkedPersonId)) continue;
            var miei = turni.Where(t => t.TipoPersona == TipoPersona.Dottore && t.PersonaId == d.Id).ToList();
            righe.Add(new PersonaRow(d.Id, TipoPersona.Dottore, d.NomeCompleto, d.Specializzazione, miei));
        }
        foreach (var p in dipendenti)
        {
            if (restrictToSelf && (linkedPersonType != "Dipendente" || p.Id != linkedPersonId)) continue;
            var miei = turni.Where(t => t.TipoPersona == TipoPersona.Dipendente && t.PersonaId == p.Id).ToList();
            righe.Add(new PersonaRow(p.Id, TipoPersona.Dipendente, p.NomeCompleto, p.Ruolo.ToString(), miei));
        }

        ViewData["Section"] = "turni";
        ViewData["RestrictedView"] = restrictToSelf;
        ViewData["NoLinkedPerson"] = !User.IsFullAccess() && !User.IsInRole("Manager") && !User.IsInRole("HR") && linkedPersonId is null;
        return View(new TurniWeekViewModel
        {
            WeekStart = weekStart,
            Righe = righe,
            ClinicheLookup = cliniche
        });
    }

    [HttpGet("nuovo")]
    [Authorize(Policy = Policies.RequireManager)]
    public async Task<IActionResult> Create(DateTime? data, string? personaId, TipoPersona tipo = TipoPersona.Dottore, DateTime? week = null)
    {
        ViewData["Section"] = "turni";
        ViewData["IsNew"] = true;
        var vm = new TurnoFormViewModel
        {
            Data = data ?? DateTime.Today,
            PersonaId = personaId,
            TipoPersona = tipo,
            ReturnWeek = week ?? StartOfWeek(data ?? DateTime.Today)
        };
        await Hydrate(vm);
        return View("Form", vm);
    }

    [HttpPost("nuovo")]
    [Authorize(Policy = Policies.RequireManager)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TurnoFormViewModel vm)
    {
        if (!ModelState.IsValid || string.IsNullOrEmpty(vm.PersonaId) || string.IsNullOrEmpty(vm.ClinicaId))
        {
            ModelState.AddModelError(string.Empty, "Persona e clinica sono obbligatorie.");
            ViewData["Section"] = "turni";
            ViewData["IsNew"] = true;
            await Hydrate(vm);
            return View("Form", vm);
        }
        var turno = new Turno
        {
            TenantId = _tenant.TenantId!,
            Data = vm.Data.Date,
            OraInizio = vm.OraInizio,
            OraFine = vm.OraFine,
            ClinicaId = vm.ClinicaId,
            PersonaId = vm.PersonaId,
            TipoPersona = vm.TipoPersona,
            Note = vm.Note
        };
        await _mongo.Turni.InsertOneAsync(turno);

        await _publisher.PublishAsync(_tenant.TenantId!, "activity", new
        {
            kind = "shift",
            title = "Turno aggiunto",
            description = $"{turno.Data:dd/MM} · {turno.OraInizio:hh\\:mm}–{turno.OraFine:hh\\:mm}",
            when = DateTime.UtcNow
        });

        TempData["flash"] = "Turno creato.";
        return RedirectToAction(nameof(Index), new { week = vm.ReturnWeek });
    }

    [HttpGet("{id}/modifica")]
    [Authorize(Policy = Policies.RequireManager)]
    public async Task<IActionResult> Edit(string id, DateTime? week = null)
    {
        var t = await _mongo.Turni
            .Find(x => x.Id == id && x.TenantId == _tenant.TenantId)
            .FirstOrDefaultAsync();
        if (t is null) return NotFound();

        ViewData["Section"] = "turni";
        ViewData["IsNew"] = false;
        var vm = new TurnoFormViewModel
        {
            Id = t.Id,
            Data = t.Data,
            OraInizio = t.OraInizio,
            OraFine = t.OraFine,
            ClinicaId = t.ClinicaId,
            PersonaId = t.PersonaId,
            TipoPersona = t.TipoPersona,
            Note = t.Note,
            ReturnWeek = week ?? StartOfWeek(t.Data)
        };
        await Hydrate(vm);
        return View("Form", vm);
    }

    [HttpPost("{id}/modifica")]
    [Authorize(Policy = Policies.RequireManager)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, TurnoFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "turni";
            ViewData["IsNew"] = false;
            await Hydrate(vm);
            return View("Form", vm);
        }

        var update = Builders<Turno>.Update
            .Set(t => t.Data, vm.Data.Date)
            .Set(t => t.OraInizio, vm.OraInizio)
            .Set(t => t.OraFine, vm.OraFine)
            .Set(t => t.ClinicaId, vm.ClinicaId!)
            .Set(t => t.PersonaId, vm.PersonaId!)
            .Set(t => t.TipoPersona, vm.TipoPersona)
            .Set(t => t.Note, vm.Note)
            .Set(t => t.UpdatedAt, DateTime.UtcNow);
        await _mongo.Turni.UpdateOneAsync(t => t.Id == id && t.TenantId == _tenant.TenantId, update);

        TempData["flash"] = "Turno aggiornato.";
        return RedirectToAction(nameof(Index), new { week = vm.ReturnWeek });
    }

    [HttpPost("{id}/elimina")]
    [Authorize(Policy = Policies.RequireManager)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id, DateTime? week = null)
    {
        await _mongo.Turni.DeleteOneAsync(t => t.Id == id && t.TenantId == _tenant.TenantId);
        TempData["flash"] = "Turno eliminato.";
        return RedirectToAction(nameof(Index), new { week });
    }

    private async Task Hydrate(TurnoFormViewModel vm)
    {
        vm.Cliniche = await _mongo.Cliniche
            .Find(c => c.TenantId == _tenant.TenantId)
            .SortBy(c => c.Nome).ToListAsync();
        vm.Dottori = await _mongo.Dottori
            .Find(d => d.TenantId == _tenant.TenantId && d.Attivo)
            .SortBy(d => d.Cognome).ToListAsync();
        vm.Dipendenti = await _mongo.Dipendenti
            .Find(d => d.TenantId == _tenant.TenantId && d.Stato != StatoDipendente.Cessato)
            .SortBy(d => d.Cognome).ToListAsync();
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var d = date.Date;
        var diff = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return d.AddDays(-diff);
    }
}
