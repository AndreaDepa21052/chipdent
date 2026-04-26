using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class Dipendente : TenantEntity
{
    public string Nome { get; set; } = string.Empty;
    public string Cognome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public RuoloDipendente Ruolo { get; set; } = RuoloDipendente.ASO;
    public string ClinicaId { get; set; } = string.Empty;
    public TipoContratto TipoContratto { get; set; } = TipoContratto.Dipendente;
    public DateTime DataAssunzione { get; set; } = DateTime.UtcNow;
    public int GiorniFerieResidui { get; set; } = 26;
    public StatoDipendente Stato { get; set; } = StatoDipendente.Attivo;

    public string NomeCompleto => $"{Nome} {Cognome}".Trim();
}

public enum RuoloDipendente
{
    ASO,
    Igienista,
    Segreteria,
    ResponsabileSede,
    Amministrazione
}

public enum StatoDipendente
{
    Attivo,
    Onboarding,
    InMalattia,
    InFerie,
    Sospeso,
    Cessato
}
