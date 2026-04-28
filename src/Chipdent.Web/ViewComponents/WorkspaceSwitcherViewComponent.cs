using System.Security.Claims;
using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.ViewComponents;

/// <summary>
/// Renderizza il workspace switcher con logo del tenant corrente e la lista
/// di altri workspace dove l'email dell'utente è Owner attivo.
/// </summary>
public class WorkspaceSwitcherViewComponent : ViewComponent
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;

    public WorkspaceSwitcherViewComponent(MongoContext mongo, ITenantContext tenant)
    {
        _mongo = mongo;
        _tenant = tenant;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var data = new WorkspaceSwitcherData();
        if (string.IsNullOrEmpty(_tenant.TenantId)) return View(data);

        data.Current = await _mongo.Tenants.Find(t => t.Id == _tenant.TenantId).FirstOrDefaultAsync();
        if (data.Current is null) return View(data);

        var meEmail = ((ClaimsPrincipal)User).FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrEmpty(meEmail)) return View(data);

        // Tenant in cui questa email è Owner attivo (escluso quello corrente)
        var ownerships = await _mongo.Users
            .Find(u => u.Email == meEmail && u.IsActive && u.Role == UserRole.Owner && u.TenantId != _tenant.TenantId)
            .ToListAsync();
        if (ownerships.Count > 0)
        {
            var tenantIds = ownerships.Select(o => o.TenantId).ToList();
            var others = await _mongo.Tenants.Find(t => tenantIds.Contains(t.Id) && t.IsActive).ToListAsync();
            data.OtherWorkspaces = others
                .OrderBy(t => t.DisplayName)
                .Select(t => new WorkspaceSwitchEntry
                {
                    Slug = t.Slug,
                    DisplayName = t.DisplayName,
                    LogoPath = t.LogoPath
                }).ToList();
        }
        return View(data);
    }
}

public class WorkspaceSwitcherData
{
    public Tenant? Current { get; set; }
    public IReadOnlyList<WorkspaceSwitchEntry> OtherWorkspaces { get; set; } = Array.Empty<WorkspaceSwitchEntry>();
}
