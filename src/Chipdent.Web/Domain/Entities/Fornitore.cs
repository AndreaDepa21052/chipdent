using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Fornitore (passivo) della catena. Anagrafica usata da Tesoreria per associare
/// fatture e scadenze. Quando ha un User collegato (Role = Fornitore) può accedere
/// al portale /fornitori per consultare le proprie scadenze e caricare fatture.
/// </summary>
public class Fornitore : TenantEntity
{
    public string RagioneSociale { get; set; } = string.Empty;
    public string? PartitaIva { get; set; }
    public string? CodiceFiscale { get; set; }
    public string? CodiceSdi { get; set; }
    public string? Pec { get; set; }
    public string? EmailContatto { get; set; }
    public string? Telefono { get; set; }
    public string? Indirizzo { get; set; }
    public string? Iban { get; set; }

    /// <summary>Categoria di spesa di default usata come hint quando il fornitore carica fatture.</summary>
    public CategoriaSpesa CategoriaDefault { get; set; } = CategoriaSpesa.AltreSpeseFisse;

    public StatoFornitore Stato { get; set; } = StatoFornitore.Attivo;
    public string? Note { get; set; }
}

public enum StatoFornitore
{
    Attivo,
    Sospeso,
    Cessato
}

/// <summary>
/// Tipo / categoria di spesa di una fattura. Allineata al file Excel
/// del cliente (TIPO: ACQUA, ENERGIA, ALTRE SPESE FISSE, …).
/// </summary>
public enum CategoriaSpesa
{
    Acqua,
    Energia,
    Gas,
    Telefonia,
    Affitto,
    Cancelleria,
    MaterialiClinici,
    Manutenzione,
    Pulizie,
    Consulenze,
    Marketing,
    Trasporti,
    AltreSpeseFisse,
    Altro
}
