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
[Route("dashboard")]
public class DashboardController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly INotificationPublisher _publisher;

    public DashboardController(MongoContext mongo, ITenantContext tenant, INotificationPublisher publisher)
    {
        _mongo = mongo;
        _tenant = tenant;
        _publisher = publisher;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        // I fornitori non hanno accesso alla dashboard interna: vengono mandati al portale dedicato.
        if (User.IsFornitore())
            return RedirectToAction("Index", "FornitoriPortal");

        var tid = _tenant.TenantId!;
        var tenant = await _mongo.Tenants.Find(t => t.Id == tid).FirstOrDefaultAsync();

        var mode = User.IsManagement() ? DashboardMode.Management
                  : User.IsDirettore() || User.IsBackoffice() ? DashboardMode.Direttore
                  : DashboardMode.Staff;

        var vm = new DashboardViewModel
        {
            Mode = mode,
            TenantName = tenant?.DisplayName ?? "Chipdent",
            UserFullName = User.Identity?.Name ?? ""
        };

        ViewData["Section"] = "dashboard";
        return mode switch
        {
            DashboardMode.Management => View("Management", await BuildManagement(vm, tid)),
            DashboardMode.Direttore  => View("Direttore",  await BuildDirettore(vm, tid)),
            _                        => View("Staff",      await BuildStaff(vm, tid))
        };
    }

    private async Task<DashboardViewModel> BuildManagement(DashboardViewModel vm, string tid)
    {
        var cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync();
        var dottori = await _mongo.Dottori.Find(d => d.TenantId == tid && d.Attivo).ToListAsync();
        var dipendenti = await _mongo.Dipendenti.Find(d => d.TenantId == tid && d.Stato != StatoDipendente.Cessato).ToListAsync();

        var soon = DateTime.UtcNow.AddMonths(1);
        var docsInScad = await _mongo.DocumentiClinica.CountDocumentsAsync(d => d.TenantId == tid && d.DataScadenza != null && d.DataScadenza < soon);
        var visiteInScad = await _mongo.VisiteMediche.CountDocumentsAsync(v => v.TenantId == tid && v.ScadenzaIdoneita != null && v.ScadenzaIdoneita < soon);
        var ferieInAttesa = await _mongo.RichiesteFerie.CountDocumentsAsync(r => r.TenantId == tid && r.Stato == StatoRichiestaFerie.InAttesa);

        vm.ClinicheTotali = cliniche.Count;
        vm.ClinicheOperative = cliniche.Count(c => c.Stato == ClinicaStato.Operativa);
        vm.DottoriAttivi = dottori.Count;
        vm.DipendentiAttivi = dipendenti.Count;
        vm.RlsInScadenza = (int)visiteInScad;
        vm.DocumentiInScadenza = (int)docsInScad;
        vm.RichiesteFerieInAttesa = (int)ferieInAttesa;
        vm.Attivita = await BuildRecentActivity(tid);
        return vm;
    }

    private async Task<DashboardViewModel> BuildDirettore(DashboardViewModel vm, string tid)
    {
        var clinicheIds = _tenant.IsClinicaScoped ? _tenant.ClinicaIds.ToList() : null;
        var clinicheFilter = clinicheIds is null
            ? Builders<Clinica>.Filter.Eq(c => c.TenantId, tid)
            : Builders<Clinica>.Filter.Eq(c => c.TenantId, tid) & Builders<Clinica>.Filter.In(c => c.Id, clinicheIds);
        var cliniche = await _mongo.Cliniche.Find(clinicheFilter).ToListAsync();
        var clinicaIdSet = cliniche.Select(c => c.Id).ToHashSet();

        var weekStart = StartOfWeek(DateTime.Today);
        var weekEnd = weekStart.AddDays(7);

        var dipendenti = await _mongo.Dipendenti
            .Find(d => d.TenantId == tid && d.Stato != StatoDipendente.Cessato)
            .ToListAsync();
        var dipFiltrati = dipendenti.Where(d => clinicaIdSet.Contains(d.ClinicaId)).ToList();

        var turniSettimana = await _mongo.Turni
            .Find(t => t.TenantId == tid && t.Data >= weekStart && t.Data < weekEnd)
            .ToListAsync();
        var turniNelleMieCliniche = turniSettimana.Where(t => clinicaIdSet.Contains(t.ClinicaId)).ToList();

        var soon = DateTime.UtcNow.AddMonths(1);
        var docs = await _mongo.DocumentiClinica
            .Find(d => d.TenantId == tid && d.DataScadenza != null && d.DataScadenza < soon)
            .ToListAsync();

        var ferieDaApprovare = await _mongo.RichiesteFerie
            .Find(r => r.TenantId == tid && r.Stato == StatoRichiestaFerie.InAttesa)
            .SortBy(r => r.DataInizio).ToListAsync();
        var ferieFiltrate = ferieDaApprovare
            .Where(r => clinicaIdSet.Contains(r.ClinicaId))
            .Take(5)
            .ToList();

        // Turni di oggi
        var oggi = DateTime.Today;
        var turniOggi = turniNelleMieCliniche.Where(t => t.Data.Date == oggi).ToList();
        var clinicheLookup = cliniche.ToDictionary(c => c.Id, c => c.Nome);
        var personeLookup = new Dictionary<string, string>();
        foreach (var d in await _mongo.Dottori.Find(x => x.TenantId == tid).ToListAsync())
            personeLookup[d.Id] = d.NomeCompleto;
        foreach (var d in dipendenti)
            personeLookup[d.Id] = d.NomeCompleto;
        var turniOggiVm = turniOggi
            .OrderBy(t => t.OraInizio)
            .Select(t => new TurnoOggi(
                personeLookup.GetValueOrDefault(t.PersonaId, "—"),
                t.TipoPersona.ToString(),
                clinicheLookup.GetValueOrDefault(t.ClinicaId, "—"),
                $"{t.OraInizio:hh\\:mm} – {t.OraFine:hh\\:mm}"))
            .ToList();

        vm.ClinicheDelDirettore = cliniche.Select(c =>
        {
            var dipCount = dipFiltrati.Count(d => d.ClinicaId == c.Id);
            var turniCount = turniNelleMieCliniche.Count(t => t.ClinicaId == c.Id);
            var docCount = docs.Count(d => d.ClinicaId == c.Id);
            return new ClinicaSummary(c.Id, c.Nome, dipCount, turniCount, docCount);
        }).ToList();
        vm.TurniOggi = turniOggiVm;
        vm.FerieDaApprovare = ferieFiltrate;
        vm.RichiesteFerieInAttesa = ferieFiltrate.Count;
        vm.DocumentiInScadenza = docs.Count(d => clinicaIdSet.Contains(d.ClinicaId));
        vm.RlsInScadenza = (int)await _mongo.VisiteMediche.CountDocumentsAsync(v => v.TenantId == tid && v.ScadenzaIdoneita != null && v.ScadenzaIdoneita < soon);
        vm.Attivita = await BuildRecentActivity(tid);
        return vm;
    }

    private async Task<DashboardViewModel> BuildStaff(DashboardViewModel vm, string tid)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var linkedType = User.LinkedPersonType();
        var linkedId = User.LinkedPersonId();

        Dipendente? me = null;
        if (linkedType == "Dipendente" && !string.IsNullOrEmpty(linkedId))
            me = await _mongo.Dipendenti.Find(d => d.Id == linkedId && d.TenantId == tid).FirstOrDefaultAsync();

        vm.StaffNome = me?.NomeCompleto ?? vm.UserFullName;
        vm.StaffSaldoFerie = me?.GiorniFerieResidui;

        if (me is not null)
        {
            var oggi = DateTime.Today;
            var fineSett = oggi.AddDays(7);
            var turni = await _mongo.Turni
                .Find(t => t.TenantId == tid && t.PersonaId == me.Id && t.TipoPersona == TipoPersona.Dipendente
                           && t.Data >= oggi && t.Data < fineSett)
                .SortBy(t => t.Data).ThenBy(t => t.OraInizio)
                .ToListAsync();
            var clinicheLookup = (await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync())
                .ToDictionary(c => c.Id, c => c.Nome);
            vm.MieiProssimiTurni = turni.Select(t => new TurnoOggi(
                me.NomeCompleto,
                me.Ruolo.ToString(),
                clinicheLookup.GetValueOrDefault(t.ClinicaId, "—"),
                $"{t.Data:dd/MM} · {t.OraInizio:hh\\:mm}–{t.OraFine:hh\\:mm}")).ToList();

            vm.MieRichiesteInAttesa = (int)await _mongo.RichiesteFerie
                .CountDocumentsAsync(r => r.TenantId == tid && r.DipendenteId == me.Id && r.Stato == StatoRichiestaFerie.InAttesa);
        }

        vm.CircolariNonLette = (int)await _mongo.Comunicazioni
            .CountDocumentsAsync(c => c.TenantId == tid && c.Categoria == CategoriaComunicazione.Annuncio
                                      && !c.LettaDaUserIds.Contains(userId));
        vm.Attivita = await BuildRecentActivity(tid);
        return vm;
    }

    private async Task<IReadOnlyList<AttivitaRecente>> BuildRecentActivity(string tid)
    {
        var entries = await _mongo.Audit
            .Find(a => a.TenantId == tid)
            .SortByDescending(a => a.CreatedAt)
            .Limit(10)
            .ToListAsync();
        return entries.Select(e => new AttivitaRecente(
            e.CreatedAt,
            $"{e.Action} · {e.EntityType}",
            string.IsNullOrEmpty(e.EntityLabel) ? e.UserName : $"{e.EntityLabel} — {e.UserName}",
            "audit")).ToList();
    }

    [HttpPost("ping")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ping()
    {
        var tenantId = _tenant.TenantId!;
        await _publisher.PublishAsync(tenantId, "activity", new
        {
            kind = "ping",
            title = "Notifica di prova",
            description = $"Inviata da {User.Identity?.Name} alle {DateTime.Now:HH:mm:ss}",
            when = DateTime.UtcNow
        });
        return Content("ok");
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var d = date.Date;
        var diff = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return d.AddDays(-diff);
    }
}
