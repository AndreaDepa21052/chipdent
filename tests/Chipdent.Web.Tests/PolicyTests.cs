using System.Security.Claims;
using Chipdent.Web.Infrastructure.Identity;

namespace Chipdent.Web.Tests;

public class PolicyTests
{
    private static ClaimsPrincipal Principal(string role) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "test"));

    [Fact]
    public void Owner_is_management()
    {
        Assert.True(Principal(Policies.Names.Owner).IsManagement());
        Assert.True(Principal(Policies.Names.Owner).IsOwner());
    }

    [Fact]
    public void Management_is_management_but_not_owner()
    {
        var p = Principal(Policies.Names.Management);
        Assert.True(p.IsManagement());
        Assert.False(p.IsOwner());
    }

    [Fact]
    public void Direttore_is_not_management_nor_backoffice()
    {
        var p = Principal(Policies.Names.Direttore);
        Assert.True(p.IsDirettore());
        Assert.False(p.IsManagement());
        Assert.False(p.IsBackoffice());
    }

    [Fact]
    public void Backoffice_is_not_direttore_nor_management()
    {
        var p = Principal(Policies.Names.Backoffice);
        Assert.True(p.IsBackoffice());
        Assert.False(p.IsDirettore());
        Assert.False(p.IsManagement());
    }

    [Fact]
    public void Staff_is_only_staff()
    {
        var p = Principal(Policies.Names.Staff);
        Assert.True(p.IsStaff());
        Assert.False(p.IsBackoffice());
        Assert.False(p.IsDirettore());
        Assert.False(p.IsManagement());
    }

    [Fact]
    public void CanApprove_is_management_or_direttore()
    {
        Assert.True(Principal(Policies.Names.Owner).CanApprove());
        Assert.True(Principal(Policies.Names.Management).CanApprove());
        Assert.True(Principal(Policies.Names.Direttore).CanApprove());
        Assert.False(Principal(Policies.Names.Backoffice).CanApprove());
        Assert.False(Principal(Policies.Names.Staff).CanApprove());
    }

    [Fact]
    public void CanSeeAnagrafiche_excludes_staff()
    {
        Assert.True(Principal(Policies.Names.Backoffice).CanSeeAnagrafiche());
        Assert.True(Principal(Policies.Names.Direttore).CanSeeAnagrafiche());
        Assert.True(Principal(Policies.Names.Management).CanSeeAnagrafiche());
        Assert.False(Principal(Policies.Names.Staff).CanSeeAnagrafiche());
    }
}
