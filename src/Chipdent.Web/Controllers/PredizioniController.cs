using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Insights;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize(Policy = Policies.RequireDirettore)]
[Route("predizioni")]
public class PredizioniController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly AssenzePredictor _predictor;

    public PredizioniController(MongoContext mongo, ITenantContext tenant, AssenzePredictor predictor)
    {
        _mongo = mongo;
        _tenant = tenant;
        _predictor = predictor;
    }

    [HttpGet("assenze")]
    public async Task<IActionResult> Assenze(int orizzonte = 7)
    {
        var tid = _tenant.TenantId!;
        orizzonte = Math.Clamp(orizzonte, 1, 30);
        var oggi = DateTime.Today;
        var scope = _tenant.IsClinicaScoped ? _tenant.ClinicaIds : null;
        var rischi = await _predictor.CalcolaAsync(tid, oggi, orizzonte, scope);

        var clinicheList = await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync();
        var clinicheMap = clinicheList.ToDictionary(c => c.Id, c => c.Nome);

        ViewData["Section"] = "predizioni";
        return View(new PredizioneAssenzeViewModel
        {
            Orizzonte = orizzonte,
            Rischi = rischi.Select(r => new RischioAssenzaRow(r, clinicheMap.GetValueOrDefault(r.ClinicaId, "—"))).ToList(),
            ScoreMedio = rischi.Count == 0 ? 0 : (int)rischi.Average(r => r.Score),
            Critici = rischi.Count(r => r.Score >= 60)
        });
    }
}
