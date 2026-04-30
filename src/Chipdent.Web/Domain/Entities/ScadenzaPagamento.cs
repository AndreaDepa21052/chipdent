using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Singola rata/scadenza di pagamento collegata a una <see cref="FatturaFornitore"/>.
/// Una fattura può generare più scadenze (tipico per RID/RIBA o pagamenti rateizzati).
/// </summary>
public class ScadenzaPagamento : TenantEntity
{
    public string FatturaId { get; set; } = string.Empty;
    public string FornitoreId { get; set; } = string.Empty;
    public string ClinicaId { get; set; } = string.Empty;
    public CategoriaSpesa Categoria { get; set; } = CategoriaSpesa.AltreSpeseFisse;

    public DateTime DataScadenza { get; set; }
    public decimal Importo { get; set; }

    public MetodoPagamento Metodo { get; set; } = MetodoPagamento.Bonifico;
    public StatoScadenza Stato { get; set; } = StatoScadenza.DaPagare;

    /// <summary>IBAN snapshot (storicizzato anche se l'anagrafica cambia).</summary>
    public string? Iban { get; set; }

    /// <summary>Data programmata del bonifico (Stato = Programmato).</summary>
    public DateTime? DataProgrammata { get; set; }

    /// <summary>Data effettiva del pagamento (Stato = Pagato).</summary>
    public DateTime? DataPagamento { get; set; }

    /// <summary>Riferimento bonifico/CRO/contabile per la riconciliazione.</summary>
    public string? RiferimentoPagamento { get; set; }

    public string? Note { get; set; }
}

public enum MetodoPagamento
{
    Bonifico,
    Rid,
    Riba,
    CartaCredito,
    Contanti,
    Assegno,
    Compensazione,
    Altro
}

public enum StatoScadenza
{
    /// <summary>Da pagare nel futuro.</summary>
    DaPagare,
    /// <summary>Bonifico già inserito in distinta o programmato.</summary>
    Programmato,
    /// <summary>Pagato e quietanzato.</summary>
    Pagato,
    /// <summary>Scaduto e non pagato (gestito a parte come stato "rosso").</summary>
    Insoluto,
    /// <summary>Annullato (es. fattura stornata o nota credito).</summary>
    Annullato
}
