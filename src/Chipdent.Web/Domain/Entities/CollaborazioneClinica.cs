using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Rapporto di collaborazione fra un dottore e una clinica.
/// Un dottore può collaborare con più sedi contemporaneamente; ogni rapporto
/// ha una data di inizio e (opzionale) di fine.
/// </summary>
public class CollaborazioneClinica : TenantEntity
{
    public string DottoreId { get; set; } = string.Empty;
    public string ClinicaId { get; set; } = string.Empty;
    public string ClinicaNome { get; set; } = string.Empty;

    public DateTime DataInizio { get; set; } = DateTime.UtcNow;
    public DateTime? DataFine { get; set; }

    public string? Ruolo { get; set; }
    public string? Note { get; set; }

    public bool IsAttiva => DataFine is null || DataFine.Value.Date >= DateTime.UtcNow.Date;
}
