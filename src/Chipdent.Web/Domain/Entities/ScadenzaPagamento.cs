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

    /// <summary>Snapshot della scadenza attesa calcolata dai termini di pagamento del fornitore
    /// al momento della creazione/approvazione. Usata per evidenziare mismatch in scadenziario.
    /// Null = nessun calcolo possibile (es. fornitore senza termini configurati).</summary>
    public DateTime? DataScadenzaAttesa { get; set; }

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

    /// <summary>Snapshot dell'IBAN ordinante usato per il bonifico (clinica o tenant).</summary>
    public string? IbanOrdinanteUsato { get; set; }

    /// <summary>
    /// Riferimento alla scadenza "padre" quando questa è una rata derivata.
    /// Tipicamente le righe F24 (ritenute, IVA) sono figlie del compenso o della fattura
    /// che le ha generate. La UI le mostra rientranti sotto la padre. Null = scadenza autonoma.
    /// </summary>
    public string? ScadenzaPadreId { get; set; }
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
    /// <summary>Bonifico già inserito in distinta o programmato (stato interno UX).</summary>
    Programmato,
    /// <summary>Pagato e quietanzato.</summary>
    Pagato,
    /// <summary>Scaduto e non pagato (stato interno UX, derivato da DaPagare + data).</summary>
    Insoluto,
    /// <summary>Annullato (stato interno UX, es. nota credito).</summary>
    Annullato,
    /// <summary>Insolvenza definitiva: pagamento mai effettuato e non più dovuto
    /// (stralcio, transazione a zero, fornitore fallito). Confident lo chiama "Mai pagato".</summary>
    MaiPagato
}
