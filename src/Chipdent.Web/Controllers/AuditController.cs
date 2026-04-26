using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize(Roles = Policies.StaffRoles)]
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
}
