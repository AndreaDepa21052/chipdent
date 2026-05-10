using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Fornitore (passivo) della catena. Anagrafica usata da Tesoreria per associare
/// fatture e scadenze. Quando ha un User collegato (Role = Fornitore) può accedere
/// al portale /fornitori per consultare le proprie scadenze e caricare fatture.
/// </summary>
public class Fornitore : TenantEntity
{
    /// <summary>Codice anagrafico interno, univoco per tenant. Es. "F0001" per i fornitori
    /// aziendali o "D0001" per i dottori (Fornitore-ombra collegato al Dottore).</summary>
    public string? Codice { get; set; }

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

    // Termini di pagamento da contratto / accordi commerciali ─────────────
    /// <summary>Numero di giorni dalla base di pagamento alla scadenza (es. 30, 60).</summary>
    public int TerminiPagamentoGiorni { get; set; } = 30;

    /// <summary>Schema di calcolo della scadenza attesa.</summary>
    public BasePagamento BasePagamento { get; set; } = BasePagamento.DataFattura;

    // Link al Dottore (un Dottore è anche un Fornitore quando è collaboratore/libero
    // professionista — riusa il modulo Tesoreria per gestire i suoi pagamenti).
    /// <summary>Id del Dottore se questo Fornitore è la "controparte fattura" di un dottore.</summary>
    public string? DottoreId { get; set; }
}

/// <summary>
/// Base di calcolo per la data di scadenza attesa di una fattura.
/// </summary>
public enum BasePagamento
{
    /// <summary>"30 gg D.F." — scadenza = data fattura + giorni.</summary>
    DataFattura,
    /// <summary>"30 gg F.M." — scadenza = ultimo giorno del mese fattura + giorni.</summary>
    FineMeseFattura,
    /// <summary>"60 gg F.M.S." — scadenza = ultimo giorno del mese SUCCESSIVO + giorni.</summary>
    FineMeseSuccessivo
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
    // ── Voci legacy generiche (mantenute per compatibilità) ──
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
    Altro,

    // ── 32 categorie chiuse del file scadenziario di Confident ──
    EnergiaElettrica,
    Locazione,
    SpeseCondominiali,
    Assicurazione,
    Leasing,
    Software,
    It,
    NoleggioIt,
    Laboratorio,
    MaterialeMedico,
    ServizioPulizia,
    Royalties,
    CanoneMarketing,
    EntranceFee,
    DueDiligence,
    FinanziamentiPassivi,
    OneriFinanziari,
    FondoInvestimento,
    ImposteTasse,
    DirezioneSanitaria,
    Medici,
    CompensoAmministratore,
    CompensoConsigliere,
    CostiPersonale,
    CostiInizioAttivita,
    RimborsoAmministratore,
    AltriRicaviVari,
    Dividendi
}
