using System.Security.Claims;
using Chipdent.Web.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Tenancy;

public class TenantResolverMiddleware
{
    public const string TenantIdClaim = "tenant_id";
    public const string TenantSlugClaim = "tenant_slug";
    public const string LinkedPersonTypeClaim = "linked_person_type";
    public const string LinkedPersonIdClaim = "linked_person_id";

    private readonly RequestDelegate _next;

    public TenantResolverMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, MongoContext mongo)
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var tenantId = user.FindFirst(TenantIdClaim)?.Value;
            var tenantSlug = user.FindFirst(TenantSlugClaim)?.Value;
            if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(tenantSlug))
            {
                tenantContext.Set(tenantId, tenantSlug);
            }
        }
        else if (context.Request.Headers.TryGetValue("X-Tenant", out var headerSlug))
        {
            var slug = headerSlug.ToString();
            var tenant = await mongo.Tenants.Find(t => t.Slug == slug && t.IsActive).FirstOrDefaultAsync();
            if (tenant is not null)
            {
                tenantContext.Set(tenant.Id, tenant.Slug);
            }
        }

        await _next(context);
    }
}
