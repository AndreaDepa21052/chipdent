namespace Chipdent.Web.Infrastructure.Tenancy;

public interface ITenantContext
{
    string? TenantId { get; }
    string? TenantSlug { get; }
    bool HasTenant { get; }
    void Set(string tenantId, string tenantSlug);
}

public class TenantContext : ITenantContext
{
    public string? TenantId { get; private set; }
    public string? TenantSlug { get; private set; }
    public bool HasTenant => !string.IsNullOrEmpty(TenantId);

    public void Set(string tenantId, string tenantSlug)
    {
        TenantId = tenantId;
        TenantSlug = tenantSlug;
    }
}
