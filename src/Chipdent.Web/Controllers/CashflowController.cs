using System.Security.Claims;
using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Cashflow;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

/// <summary>
/// Cashflow — dashboard predittiva del flusso di cassa basata sullo scadenziario.
/// Owner + Management + Backoffice possono consultarla; le configurazioni (saldo, soglia,
/// entrate attese) sono modificabili dagli stessi ruoli.
/// </summary>
[Authorize(Policy = Policies.RequireTesoreria)]
[Route("tesoreria/cashflow")]
public class CashflowController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly CashflowForecaster _forecaster;

    public CashflowController(MongoContext mongo, ITenantContext tenant, CashflowForecaster forecaster)
    {
        _mongo = mongo;
        _tenant = tenant;
        _forecaster = forecaster;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var tid = _tenant.TenantId!;
        var forecast = await _forecaster.BuildAsync(tid);
        var entrate = await _mongo.EntrateAttese
            .Find(e => e.TenantId == tid)
            .SortBy(e => e.DataAttesa).ToListAsync();
        ViewData["Section"] = "tesoreria-cashflow";
        return View(new CashflowDashboardViewModel
        {
            Forecast = forecast,
            EntratePerOrizzonte = entrate
        });
    }

    // ── Settings: saldo + soglia ─────────────────────────────────
    [HttpGet("settings")]
    public async Task<IActionResult> Settings()
    {
        var tid = _tenant.TenantId!;
        var s = await _mongo.CashflowSettings.Find(x => x.TenantId == tid).FirstOrDefaultAsync()
                ?? new CashflowSettings { TenantId = tid };
        ViewData["Section"] = "tesoreria-cashflow";
        return View(new CashflowSettingsFormViewModel
        {
            SaldoCassa = s.SaldoCassa,
            SogliaRischio = s.SogliaRischio,
            Note = s.Note,
            SaldoAggiornatoIl = s.SaldoAggiornatoIl
        });
    }

    [HttpPost("settings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(CashflowSettingsFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "tesoreria-cashflow";
            return View(vm);
        }
        var tid = _tenant.TenantId!;
        var existing = await _mongo.CashflowSettings.Find(x => x.TenantId == tid).FirstOrDefaultAsync();
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (existing is null)
        {
            await _mongo.CashflowSettings.InsertOneAsync(new CashflowSettings
            {
                TenantId = tid,
                SaldoCassa = vm.SaldoCassa,
                SogliaRischio = vm.SogliaRischio,
                Note = vm.Note,
                SaldoAggiornatoIl = DateTime.UtcNow,
                SaldoAggiornatoDaUserId = userId
            });
        }
        else
        {
            await _mongo.CashflowSettings.UpdateOneAsync(x => x.Id == existing.Id,
                Builders<CashflowSettings>.Update
                    .Set(x => x.SaldoCassa, vm.SaldoCassa)
                    .Set(x => x.SogliaRischio, vm.SogliaRischio)
                    .Set(x => x.Note, vm.Note)
                    .Set(x => x.SaldoAggiornatoIl, DateTime.UtcNow)
                    .Set(x => x.SaldoAggiornatoDaUserId, userId)
                    .Set(x => x.UpdatedAt, DateTime.UtcNow));
        }
        TempData["flash"] = "Configurazione cashflow aggiornata.";
        return RedirectToAction(nameof(Index));
    }

    // ── Entrate attese: CRUD inline ───────────────────────────────
    [HttpPost("entrate/nuova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NuovaEntrata(EntrataAttesaFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            TempData["flash"] = "Dati entrata non validi.";
            return RedirectToAction(nameof(Index));
        }
        await _mongo.EntrateAttese.InsertOneAsync(new EntrataAttesa
        {
            TenantId = _tenant.TenantId!,
            DataAttesa = DateTime.SpecifyKind(vm.DataAttesa.Date, DateTimeKind.Utc),
            Importo = vm.Importo,
            Descrizione = vm.Descrizione.Trim(),
            ClinicaId = string.IsNullOrEmpty(vm.ClinicaId) ? null : vm.ClinicaId,
            CreatoDaUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        });
        TempData["flash"] = "Entrata attesa registrata.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("entrate/{id}/elimina")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminaEntrata(string id)
    {
        await _mongo.EntrateAttese.DeleteOneAsync(e => e.Id == id && e.TenantId == _tenant.TenantId);
        TempData["flash"] = "Entrata rimossa.";
        return RedirectToAction(nameof(Index));
    }
}
