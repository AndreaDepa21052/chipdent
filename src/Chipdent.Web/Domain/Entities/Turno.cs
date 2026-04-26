using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class Turno : TenantEntity
{
    public DateTime Data { get; set; }
    public TimeSpan OraInizio { get; set; }
    public TimeSpan OraFine { get; set; }
    public string ClinicaId { get; set; } = string.Empty;
    public string PersonaId { get; set; } = string.Empty;
    public TipoPersona TipoPersona { get; set; }
    public string? Note { get; set; }
}

public enum TipoPersona
{
    Dottore,
    Dipendente
}
