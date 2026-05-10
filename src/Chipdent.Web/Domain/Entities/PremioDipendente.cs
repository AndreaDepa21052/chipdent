using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Storico premi/bonus/welfare erogati al dipendente.
/// Una entry per ogni erogazione. Il valore può essere monetario o convenzionale
/// (per benefit non monetari come buoni pasto, formazione, ecc.).
/// </summary>
public class PremioDipendente : TenantEntity
{
    public string DipendenteId { get; set; } = string.Empty;
    public string DipendenteNome { get; set; } = string.Empty;

    public TipoPremio Tipo { get; set; } = TipoPremio.Bonus;

    /// <summary>Etichetta libera (es. "Bonus produttività Q4", "Welfare Edenred 600€").</summary>
    public string Descrizione { get; set; } = string.Empty;

    /// <summary>Data di erogazione / riferimento.</summary>
    public DateTime Data { get; set; } = DateTime.UtcNow;

    /// <summary>Importo lordo €. Null per benefit non monetari.</summary>
    public decimal? Importo { get; set; }

    public string? Motivazione { get; set; }
    public string? Note { get; set; }

    public string? AllegatoNome { get; set; }
    public string? AllegatoPath { get; set; }
    public long? AllegatoSize { get; set; }
}

public enum TipoPremio
{
    Bonus,
    Premio,
    Welfare,
    BuonoPasto,
    Formazione,
    Aumento,
    Altro
}
