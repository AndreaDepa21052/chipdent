using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class Dottore : TenantEntity
{
    public string Nome { get; set; } = string.Empty;
    public string Cognome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public string Specializzazione { get; set; } = string.Empty;
    public string NumeroAlbo { get; set; } = string.Empty;
    public DateTime? ScadenzaAlbo { get; set; }
    public TipoContratto TipoContratto { get; set; } = TipoContratto.Collaborazione;
    public string? ClinicaPrincipaleId { get; set; }
    public bool Attivo { get; set; } = true;

    public string NomeCompleto => $"Dr. {Nome} {Cognome}".Trim();
}

public enum TipoContratto
{
    Collaborazione,
    Dipendente,
    LiberoProfessionista,
    PartTime
}
