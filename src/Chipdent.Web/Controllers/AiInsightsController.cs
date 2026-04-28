using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Insights;
using Chipdent.Web.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chipdent.Web.Controllers;

[Authorize(Policy = Policies.RequireManagement)]
[Route("ai-insights")]
public class AiInsightsController : Controller
{
    private readonly AiInsightsEngine _engine;
    private readonly ITenantContext _tenant;

    public AiInsightsController(AiInsightsEngine engine, ITenantContext tenant)
    {
        _engine = engine;
        _tenant = tenant;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ViewData["Section"] = "ai-insights";
        var snapshot = await _engine.ComputeAsync(_tenant.TenantId!);
        return View(snapshot);
    }
}
