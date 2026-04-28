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
        var weekStart = StartOfWeek(week ?? DateTime.UtcNow.Date);
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
        var templates = await _mongo.TurniTemplate
            .Find(t => t.TenantId == _tenant.TenantId && t.Attivo)
            .SortBy(t => t.OraInizio).ToListAsync();

        // Ferie approvate sovrapposte alla settimana per conflict detection.
        var ferieAttive = await _mongo.RichiesteFerie
            .Find(r => r.TenantId == _tenant.TenantId
                       && r.Stato == StatoRichiestaFerie.Approvata
                       && r.DataInizio < weekEnd && r.DataFine >= weekStart)
            .ToListAsync();

        var linkedPersonId = User.LinkedPersonId();
        var linkedPersonType = User.LinkedPersonType();
        var canSeeAll = User.IsManagement() || User.IsDirettore() || User.IsBackoffice();
        var restrictToSelf = !canSeeAll && linkedPersonId is not null;

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

        var conflitti = DetectConflitti(righe, ferieAttive, dipendenti);

        ViewData["Section"] = "turni";
        ViewData["RestrictedView"] = restrictToSelf;
        ViewData["NoLinkedPerson"] = !canSeeAll && linkedPersonId is null;
        return View(new TurniWeekViewModel
        {
            WeekStart = weekStart,
            Righe = righe,
            ClinicheLookup = cliniche,
            Templates = templates,
            Conflitti = conflitti,
            CanEdit = User.IsManagement() || User.IsDirettore()
        });
    }

    [HttpGet("nuovo")]
    [Authorize(Policy = Policies.RequireDirettore)]
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
    [Authorize(Policy = Policies.RequireDirettore)]
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
            Data = UtcDate(vm.Data),
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
        return RedirectToAction(nameof(Index), new { week = vm.ReturnWeek.ToString("yyyy-MM-dd") });
    }

    [HttpGet("{id}/modifica")]
    [Authorize(Policy = Policies.RequireDirettore)]
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
    [Authorize(Policy = Policies.RequireDirettore)]
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
            .Set(t => t.Data, UtcDate(vm.Data))
            .Set(t => t.OraInizio, vm.OraInizio)
            .Set(t => t.OraFine, vm.OraFine)
            .Set(t => t.ClinicaId, vm.ClinicaId!)
            .Set(t => t.PersonaId, vm.PersonaId!)
            .Set(t => t.TipoPersona, vm.TipoPersona)
            .Set(t => t.Note, vm.Note)
            .Set(t => t.UpdatedAt, DateTime.UtcNow);
        await _mongo.Turni.UpdateOneAsync(t => t.Id == id && t.TenantId == _tenant.TenantId, update);

        TempData["flash"] = "Turno aggiornato.";
        return RedirectToAction(nameof(Index), new { week = vm.ReturnWeek.ToString("yyyy-MM-dd") });
    }

    [HttpPost("{id}/elimina")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id, DateTime? week = null)
    {
        await _mongo.Turni.DeleteOneAsync(t => t.Id == id && t.TenantId == _tenant.TenantId);
        TempData["flash"] = "Turno eliminato.";
        return RedirectToAction(nameof(Index), new { week = week?.ToString("yyyy-MM-dd") });
    }

    /// <summary>Drag&drop: sposta un turno a una nuova data/persona via AJAX.</summary>
    [HttpPost("{id}/sposta")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(string id, [FromForm] DateTime data, [FromForm] string? personaId, [FromForm] TipoPersona? tipo)
    {
        if (string.IsNullOrEmpty(id))
            return BadRequest("Id turno mancante.");

        var existing = await _mongo.Turni.Find(t => t.Id == id && t.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (existing is null)
            return NotFound($"Turno {id} non trovato.");

        var update = Builders<Turno>.Update
            .Set(t => t.Data, UtcDate(data))
            .Set(t => t.UpdatedAt, DateTime.UtcNow);
        if (!string.IsNullOrEmpty(personaId))
        {
            update = update.Set(t => t.PersonaId, personaId);
            if (tipo.HasValue) update = update.Set(t => t.TipoPersona, tipo.Value);
        }
        await _mongo.Turni.UpdateOneAsync(t => t.Id == id && t.TenantId == _tenant.TenantId, update);
        return Json(new { ok = true });
    }

    /// <summary>Crea un turno rapido da template, in una cella del calendario.</summary>
    [HttpPost("rapido")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Quick([FromForm] DateTime data, [FromForm] string personaId, [FromForm] TipoPersona tipo, [FromForm] string templateId, [FromForm] string clinicaId)
    {
        var tpl = await _mongo.TurniTemplate.Find(t => t.Id == templateId && t.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (tpl is null) return BadRequest("Template non valido.");

        var turno = new Turno
        {
            TenantId = _tenant.TenantId!,
            Data = UtcDate(data),
            OraInizio = tpl.OraInizio,
            OraFine = tpl.OraFine,
            ClinicaId = clinicaId,
            PersonaId = personaId,
            TipoPersona = tipo,
            Note = $"Da template «{tpl.Nome}»"
        };
        await _mongo.Turni.InsertOneAsync(turno);
        return Json(new { ok = true, id = turno.Id });
    }

    /// <summary>Copia tutti i turni di una settimana nella settimana successiva.</summary>
    [HttpPost("copia-settimana")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CopiaSettimana([FromForm] DateTime week)
    {
        var src = StartOfWeek(week);
        var srcEnd = src.AddDays(7);
        var dest = src.AddDays(7);

        var sourceTurni = await _mongo.Turni
            .Find(t => t.TenantId == _tenant.TenantId && t.Data >= src && t.Data < srcEnd)
            .ToListAsync();

        if (sourceTurni.Count == 0)
        {
            TempData["flash"] = "Nessun turno da copiare nella settimana di origine.";
            return RedirectToAction(nameof(Index), new { week = dest.ToString("yyyy-MM-dd") });
        }

        var copies = sourceTurni.Select(t => new Turno
        {
            TenantId = t.TenantId,
            Data = UtcDate(t.Data.AddDays(7)),
            OraInizio = t.OraInizio,
            OraFine = t.OraFine,
            ClinicaId = t.ClinicaId,
            PersonaId = t.PersonaId,
            TipoPersona = t.TipoPersona,
            Note = t.Note
        }).ToList();
        await _mongo.Turni.InsertManyAsync(copies);

        TempData["flash"] = $"Copiati {copies.Count} turni nella settimana del {dest:dd/MM}.";
        return RedirectToAction(nameof(Index), new { week = dest.ToString("yyyy-MM-dd") });
    }

    // ───── Templates ─────

    [HttpGet("template")]
    [Authorize(Policy = Policies.RequireDirettore)]
    public async Task<IActionResult> Templates()
    {
        var items = await _mongo.TurniTemplate
            .Find(t => t.TenantId == _tenant.TenantId)
            .SortBy(t => t.OraInizio).ToListAsync();
        ViewData["Section"] = "turni";
        return View(items);
    }

    [HttpPost("template/nuovo")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTemplate(string nome, TimeSpan oraInizio, TimeSpan oraFine, string? coloreHex)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            TempData["flash"] = "Nome richiesto.";
            return RedirectToAction(nameof(Templates));
        }
        await _mongo.TurniTemplate.InsertOneAsync(new TurnoTemplate
        {
            TenantId = _tenant.TenantId!,
            Nome = nome.Trim(),
            OraInizio = oraInizio,
            OraFine = oraFine,
            ColoreHex = string.IsNullOrWhiteSpace(coloreHex) ? null : coloreHex,
            Attivo = true
        });
        TempData["flash"] = "Template creato.";
        return RedirectToAction(nameof(Templates));
    }

    [HttpPost("template/{id}/elimina")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTemplate(string id)
    {
        await _mongo.TurniTemplate.DeleteOneAsync(t => t.Id == id && t.TenantId == _tenant.TenantId);
        TempData["flash"] = "Template eliminato.";
        return RedirectToAction(nameof(Templates));
    }

    // ───── Internals ─────

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

    private static List<ConflittoTurno> DetectConflitti(
        IEnumerable<PersonaRow> righe,
        IReadOnlyList<RichiestaFerie> ferie,
        IReadOnlyList<Dipendente> dipendenti)
    {
        var risultato = new List<ConflittoTurno>();
        foreach (var riga in righe)
        {
            // Sovrapposizione orari nello stesso giorno
            var byDay = riga.Turni.GroupBy(t => t.Data.Date);
            foreach (var grp in byDay)
            {
                var ordered = grp.OrderBy(t => t.OraInizio).ToList();
                for (var i = 1; i < ordered.Count; i++)
                {
                    if (ordered[i].OraInizio < ordered[i - 1].OraFine)
                    {
                        risultato.Add(new ConflittoTurno(riga.Id, riga.Nome, grp.Key,
                            $"Sovrapposizione orari {ordered[i - 1].OraInizio:hh\\:mm}-{ordered[i - 1].OraFine:hh\\:mm} con {ordered[i].OraInizio:hh\\:mm}-{ordered[i].OraFine:hh\\:mm}"));
                    }
                }
            }

            // Turni assegnati durante ferie approvate (solo dipendenti)
            if (riga.Tipo == TipoPersona.Dipendente)
            {
                var ferieDip = ferie.Where(f => f.DipendenteId == riga.Id).ToList();
                foreach (var t in riga.Turni)
                {
                    var match = ferieDip.FirstOrDefault(f => t.Data.Date >= f.DataInizio.Date && t.Data.Date <= f.DataFine.Date);
                    if (match is not null)
                    {
                        risultato.Add(new ConflittoTurno(riga.Id, riga.Nome, t.Data.Date,
                            $"Turno assegnato durante ferie approvate ({match.Tipo})"));
                    }
                }
            }
        }
        return risultato;
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var d = date.Date;
        var diff = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return DateTime.SpecifyKind(d.AddDays(-diff), DateTimeKind.Utc);
    }

    /// <summary>Normalizza una data in UTC midnight per evitare shift di fuso orario su MongoDB.</summary>
    private static DateTime UtcDate(DateTime d) => DateTime.SpecifyKind(d.Date, DateTimeKind.Utc);
}
