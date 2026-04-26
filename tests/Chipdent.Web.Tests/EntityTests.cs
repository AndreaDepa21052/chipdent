using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Tests;

public class EntityTests
{
    [Fact]
    public void Dottore_NomeCompleto_includes_title()
    {
        var d = new Dottore { Nome = "Marco", Cognome = "Bianchi" };
        Assert.Equal("Dr. Marco Bianchi", d.NomeCompleto);
    }

    [Fact]
    public void Dipendente_NomeCompleto_no_title()
    {
        var d = new Dipendente { Nome = "Sara", Cognome = "Conti" };
        Assert.Equal("Sara Conti", d.NomeCompleto);
    }

    [Fact]
    public void Invito_is_invalid_when_used()
    {
        var i = new Invito
        {
            ScadeIl = DateTime.UtcNow.AddDays(1),
            UsatoIl = DateTime.UtcNow
        };
        Assert.False(i.IsValido);
    }

    [Fact]
    public void Invito_is_invalid_when_expired()
    {
        var i = new Invito
        {
            ScadeIl = DateTime.UtcNow.AddDays(-1)
        };
        Assert.False(i.IsValido);
    }

    [Fact]
    public void Invito_is_valid_when_fresh()
    {
        var i = new Invito
        {
            ScadeIl = DateTime.UtcNow.AddDays(7)
        };
        Assert.True(i.IsValido);
    }
}
