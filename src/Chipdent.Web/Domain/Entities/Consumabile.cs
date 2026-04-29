using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Consumabile gestito a magazzino di sede: guanti, mascherine, anestetici, materiale di consumo.
/// Quando la giacenza scende sotto la soglia minima, il sistema genera un alert riordino al Backoffice.
/// </summary>
public class Consumabile : TenantEntity
{
    public string ClinicaId { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string? Categoria { get; set; }
    public string? UnitaMisura { get; set; }   // pz, conf, ml, ecc.
    public int GiacenzaCorrente { get; set; }
    public int SogliaMinima { get; set; }
    public string? Fornitore { get; set; }
    public string? CodiceFornitore { get; set; }
    public bool Attivo { get; set; } = true;
    public DateTime? UltimoMovimentoAt { get; set; }
}

/// <summary>Movimento manuale (carico/scarico) sulla giacenza di un consumabile.</summary>
public class MovimentoConsumabile : TenantEntity
{
    public string ConsumabileId { get; set; } = string.Empty;
    public string ClinicaId { get; set; } = string.Empty;
    public TipoMovimento Tipo { get; set; }
    public int Quantita { get; set; }
    public string? Motivo { get; set; }
    public string EseguitoDaUserId { get; set; } = string.Empty;
}

public enum TipoMovimento
{
    Carico,    // arrivo merce / inventario
    Scarico,   // utilizzo
    Rettifica  // riconciliazione manuale (può essere positiva o negativa)
}
