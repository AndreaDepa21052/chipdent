using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Protocollo operativo adottato da una clinica (sicurezza, legionella, ecc.).
/// Storicizzato per versioni: ad ogni revisione si crea un nuovo record.
/// </summary>
public class ProtocolloClinica : TenantEntity
{
    public string ClinicaId { get; set; } = string.Empty;
    public TipoProtocollo Tipo { get; set; }
    public bool Attivo { get; set; } = true;
    public DateTime? DataAdozione { get; set; }
    public DateTime? ProssimaRevisione { get; set; }

    public string? Versione { get; set; }
    public string? Note { get; set; }

    public string? AllegatoNome { get; set; }
    public string? AllegatoPath { get; set; }
    public long? AllegatoSize { get; set; }
}

public enum TipoProtocollo
{
    Sicurezza,
    Legionella,
    SterilizzazioneStrumenti,
    GestioneRifiutiSpeciali,
    Altro
}
