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
        Assert.False(ctx.IsClinicaScoped);
        Assert.Empty(ctx.ClinicaIds);
    }

    [Fact]
    public void Set_populates_id_and_slug()
    {
        var ctx = new TenantContext();
        ctx.Set("abc123", "confident");
        Assert.True(ctx.HasTenant);
        Assert.Equal("abc123", ctx.TenantId);
        Assert.Equal("confident", ctx.TenantSlug);
        Assert.False(ctx.IsClinicaScoped);
    }

    [Fact]
    public void Set_with_clinica_ids_marks_scoped()
    {
        var ctx = new TenantContext();
        ctx.Set("abc123", "confident", new[] { "clinica-milano", "clinica-roma" });
        Assert.True(ctx.IsClinicaScoped);
        Assert.Equal(2, ctx.ClinicaIds.Count);
        Assert.Contains("clinica-milano", ctx.ClinicaIds);
    }

    [Fact]
    public void CanAccessClinica_is_true_when_unscoped()
    {
        var ctx = new TenantContext();
        ctx.Set("t", "s");
        Assert.True(ctx.CanAccessClinica("any-id"));
        Assert.True(ctx.CanAccessClinica(null));
    }

    [Fact]
    public void CanAccessClinica_filters_when_scoped()
    {
        var ctx = new TenantContext();
        ctx.Set("t", "s", new[] { "clinica-milano" });
        Assert.True(ctx.CanAccessClinica("clinica-milano"));
        Assert.False(ctx.CanAccessClinica("clinica-roma"));
        Assert.False(ctx.CanAccessClinica(null));
    }

    [Fact]
    public void Set_dedupes_and_trims_clinica_ids()
    {
        var ctx = new TenantContext();
        ctx.Set("t", "s", new[] { "a", "b", "a", "" });
        Assert.Equal(2, ctx.ClinicaIds.Count);
    }
}
