using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Attestato ECM (Educazione Continua in Medicina) di un dottore.
/// Ogni attestato è associato a un singolo evento formativo e contribuisce
/// al totale crediti del triennio in corso.
/// </summary>
public class AttestatoEcm : TenantEntity
{
    public string DottoreId { get; set; } = string.Empty;

    public string TitoloEvento { get; set; } = string.Empty;
    public string? Provider { get; set; }

    public DateTime DataConseguimento { get; set; } = DateTime.UtcNow;

    /// <summary>Crediti ECM assegnati dall'evento.</summary>
    public decimal CreditiEcm { get; set; }

    /// <summary>Anno (4 cifre) di riferimento dei crediti per il triennio.</summary>
    public int AnnoRiferimento { get; set; } = DateTime.UtcNow.Year;

    public ModalitaEcm Modalita { get; set; } = ModalitaEcm.Fad;

    public string? Note { get; set; }

    public string? AllegatoNome { get; set; }
    public string? AllegatoPath { get; set; }
    public long? AllegatoSize { get; set; }
}

public enum ModalitaEcm
{
    Fad,
    Residenziale,
    SulCampo,
    Mista
}
