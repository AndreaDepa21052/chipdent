using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Template di turno riutilizzabile (es. "Mattina 8-13", "Pomeriggio 14-19").
/// Permette di creare turni in serie con un solo click dal calendario.
/// </summary>
public class TurnoTemplate : TenantEntity
{
    public string Nome { get; set; } = string.Empty;
    public TimeSpan OraInizio { get; set; }
    public TimeSpan OraFine { get; set; }
    public string? ColoreHex { get; set; }
    public bool Attivo { get; set; } = true;
}
