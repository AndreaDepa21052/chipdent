using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Hubs;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Insights;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize(Policy = Policies.RequireDirettore)]
[Route("ottimizzazione-turni")]
public class OttimizzazioneController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly TurniOptimizer _optimizer;
    private readonly INotificationPublisher _publisher;

    public OttimizzazioneController(MongoContext mongo, ITenantContext tenant, TurniOptimizer optimizer, INotificationPublisher publisher)
    {
        _mongo = mongo;
        _tenant = tenant;
        _optimizer = optimizer;
        _publisher = publisher;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(DateTime? week = null)
    {
        var weekStart = StartOfWeek((week ?? DateTime.Today).AddDays(7));
        var proposal = await _optimizer.ProposeAsync(_tenant.TenantId!, weekStart);
        ViewData["Section"] = "ottimizzazione";
        return View(proposal);
    }

    [HttpPost("applica")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Applica(DateTime weekStart)
    {
        var tid = _tenant.TenantId!;
        var proposal = await _optimizer.ProposeAsync(tid, StartOfWeek(weekStart));

        if (proposal.Turni.Count == 0)
        {
            TempData["flash"] = "Nessun turno da creare: la copertura è già soddisfatta.";
            return RedirectToAction(nameof(Index), new { week = proposal.WeekStart.ToString("yyyy-MM-dd") });
        }

        var turni = proposal.Turni.Select(p => new Turno
        {
            TenantId = tid,
            Data = DateTime.SpecifyKind(p.Data.Date, DateTimeKind.Utc),
            OraInizio = p.OraInizio,
            OraFine = p.OraFine,
            ClinicaId = p.ClinicaId,
            PersonaId = p.DipendenteId,
            TipoPersona = TipoPersona.Dipendente,
            Note = "✨ Generato da Ottimizzazione AI"
        }).ToList();

        await _mongo.Turni.InsertManyAsync(turni);

        await _publisher.PublishAsync(tid, "activity", new
        {
            kind = "shift",
            title = $"✨ Pianificazione AI applicata · {turni.Count} turni",
            description = $"Settimana {proposal.WeekStart:dd/MM}",
            when = DateTime.UtcNow
        });

        TempData["flash"] = $"✓ {turni.Count} turni applicati alla settimana del {proposal.WeekStart:dd/MM}.";
        return RedirectToAction("Index", "Turni", new { week = proposal.WeekStart.ToString("yyyy-MM-dd") });
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var d = date.Date;
        var diff = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return DateTime.SpecifyKind(d.AddDays(-diff), DateTimeKind.Utc);
    }
}
