using Chipdent.Web.Infrastructure.Tenancy;

namespace Chipdent.Web.Tests;

public class TenantContextTests
{
    [Fact]
    public void Default_is_empty()
    {
        var ctx = new TenantContext();
        Assert.False(ctx.HasTenant);
        Assert.Null(ctx.TenantId);
        Assert.Null(ctx.TenantSlug);
    }

    [Fact]
    public void Set_populates_id_and_slug()
    {
        var ctx = new TenantContext();
        ctx.Set("abc123", "confident");
        Assert.True(ctx.HasTenant);
        Assert.Equal("abc123", ctx.TenantId);
        Assert.Equal("confident", ctx.TenantSlug);
    }
}
