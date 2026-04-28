using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize]
[Route("audit")]
public class AuditController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;

    public AuditController(MongoContext mongo, ITenantContext tenant)
    {
        _mongo = mongo;
        _tenant = tenant;
    }

    [Authorize(Policy = Policies.RequireBackoffice)]
    [HttpGet("")]
    public async Task<IActionResult> Index(string? entityType = null, string? user = null,
                                           AuditAction? action = null, int page = 1)
    {
        const int pageSize = 50;
        var filter = Builders<AuditEntry>.Filter.Eq(a => a.TenantId, _tenant.TenantId);
        if (!string.IsNullOrEmpty(entityType))
            filter &= Builders<AuditEntry>.Filter.Eq(a => a.EntityType, entityType);
        if (!string.IsNullOrEmpty(user))
            filter &= Builders<AuditEntry>.Filter.Regex(a => a.UserName, new MongoDB.Bson.BsonRegularExpression(user, "i"));
        if (action is not null)
            filter &= Builders<AuditEntry>.Filter.Eq(a => a.Action, action.Value);

        var total = await _mongo.Audit.CountDocumentsAsync(filter);
        var entries = await _mongo.Audit
            .Find(filter)
            .SortByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        ViewData["Section"] = "audit";
        ViewData["Total"] = total;
        ViewData["Page"] = page;
        ViewData["PageSize"] = pageSize;
        ViewData["EntityType"] = entityType;
        ViewData["UserFilter"] = user;
        ViewData["ActionFilter"] = action;
        return View(entries);
    }

    /// <summary>
    /// Ultime ~20 voci audit del tenant, formato JSON minimale per la campanella notifiche.
    /// Accessibile a chiunque sia autenticato sul tenant (gli eventi sono già scoped per tenant).
    /// </summary>
    [HttpGet("recent.json")]
    [Produces("application/json")]
    public async Task<IActionResult> Recent(int limit = 20)
    {
        if (string.IsNullOrEmpty(_tenant.TenantId)) return Json(Array.Empty<object>());
        limit = Math.Clamp(limit, 1, 50);
        var entries = await _mongo.Audit
            .Find(a => a.TenantId == _tenant.TenantId)
            .SortByDescending(a => a.CreatedAt)
            .Limit(limit)
            .ToListAsync();
        var payload = entries.Select(e => new
        {
            kind = "audit",
            action = e.Action.ToString(),
            entityType = e.EntityType,
            entityLabel = e.EntityLabel,
            user = e.UserName,
            when = e.CreatedAt
        });
        return Json(payload);
    }
}
