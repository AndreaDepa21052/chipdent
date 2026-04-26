using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Hubs;
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
        var tenantId = _tenant.TenantId!;
        var tenant = await _mongo.Tenants.Find(t => t.Id == tenantId).FirstOrDefaultAsync();
        var cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tenantId).ToListAsync();

        var vm = new DashboardViewModel
        {
            TenantName = tenant?.DisplayName ?? "Chipdent",
            UserFullName = User.Identity?.Name ?? "",
            ClinicheTotali = cliniche.Count,
            ClinicheOperative = cliniche.Count(c => c.Stato == ClinicaStato.Operativa),
            DottoriAttivi = 24,
            DipendentiAttivi = 87,
            RlsInScadenza = 3,
            TurniOggi = new[]
            {
                new TurnoOggi("Dr. Marco Bianchi", "Implantologia", "Milano Centro", "08:30 — 13:00"),
                new TurnoOggi("Dr.ssa Laura Ferri", "Ortodonzia", "Milano Centro", "09:00 — 18:00"),
                new TurnoOggi("Sara Conti", "ASO", "Roma EUR", "08:00 — 14:00"),
                new TurnoOggi("Dr. Paolo Rizzo", "Endodonzia", "Roma EUR", "14:00 — 20:00")
            },
            Attivita = new[]
            {
                new AttivitaRecente(DateTime.Now.AddMinutes(-5), "Nuovo turno pubblicato", "Settimana 18 — Milano Centro", "shift"),
                new AttivitaRecente(DateTime.Now.AddHours(-2), "Documento DVR aggiornato", "Versione 3.2 — Roma EUR", "doc"),
                new AttivitaRecente(DateTime.Now.AddHours(-6), "Visita medica caricata", "Sara Conti — idoneità annuale", "rls"),
                new AttivitaRecente(DateTime.Now.AddDays(-1), "Richiesta ferie approvata", "Dr. Bianchi — 12-19 maggio", "comm")
            }
        };

        return View(vm);
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
}
