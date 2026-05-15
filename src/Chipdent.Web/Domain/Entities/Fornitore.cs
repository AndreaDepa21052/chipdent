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

    /// <summary>Ragione sociale alternativa usata in distinta/bonifico (beneficiario reale).
    /// Default = RagioneSociale alla creazione; può divergere quando il pagatore è una società
    /// diversa dal soggetto che emette fattura (es. mandato di pagamento, gruppo, holding).</summary>
    public string RagioneSocialePagamento { get; set; } = string.Empty;

    /// <summary>Nome da mostrare nello scadenziario / distinta come beneficiario del pagamento:
    /// <see cref="RagioneSocialePagamento"/> se valorizzata, altrimenti fallback alla
    /// <see cref="RagioneSociale"/> della fattura. Non persistita.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
    public string NomePerPagamento => string.IsNullOrWhiteSpace(RagioneSocialePagamento)
        ? RagioneSociale
        : RagioneSocialePagamento;

    public string? PartitaIva { get; set; }
    public string? CodiceFiscale { get; set; }
    public string? CodiceSdi { get; set; }
    public string? Pec { get; set; }
    public string? EmailContatto { get; set; }
    public string? Telefono { get; set; }
    public string? Indirizzo { get; set; }

    /// <summary>Località / comune della sede legale del beneficiario.</summary>
    public string? Localita { get; set; }

    /// <summary>Sigla provincia (es. "MI") della sede legale del beneficiario.</summary>
    public string? Provincia { get; set; }

    /// <summary>CAP della sede legale del beneficiario.</summary>
    public string? CodicePostale { get; set; }

    public string? Iban { get; set; }

    /// <summary>Categoria di spesa primaria — usata come hint quando il fornitore carica fatture.</summary>
    public CategoriaSpesa CategoriaDefault { get; set; } = CategoriaSpesa.AltreSpeseFisse;

    /// <summary>Categoria di spesa secondaria opzionale (fornitori che operano su più voci di spesa,
    /// es. service che fattura sia manutenzione sia materiali). Null se non applicabile.</summary>
    public CategoriaSpesa? CategoriaSecondaria { get; set; }

    public StatoFornitore Stato { get; set; } = StatoFornitore.Attivo;
    public string? Note { get; set; }

    // Termini di pagamento da contratto / accordi commerciali ─────────────
    /// <summary>Numero di giorni dalla base di pagamento alla scadenza (es. 30, 60).</summary>
    public int TerminiPagamentoGiorni { get; set; } = 30;

    /// <summary>Schema di calcolo della scadenza attesa.</summary>
    public BasePagamento BasePagamento { get; set; } = BasePagamento.DataFattura;

    /// <summary>Momento di emissione della fattura rispetto al pagamento. Nel form anagrafica
    /// è esposto come "Modalità di pagamento" con due opzioni: prima pagamento poi fattura
    /// (<see cref="EmissioneFattura.DopoIlPagamento"/>) oppure prima fattura poi pagamento
    /// (<see cref="EmissioneFattura.PrimaDelPagamento"/>). <see cref="EmissioneFattura.Nd"/>
    /// resta come valore di compatibilità per anagrafiche storiche non ancora classificate.</summary>
    public EmissioneFattura EmissioneFattura { get; set; } = EmissioneFattura.Nd;

    /// <summary>Indica che il fornitore emette pagamenti ricorrenti (es. canoni mensili, rate
    /// di leasing, abbonamenti) anziché fatture spot. Usato in scadenziario/cashflow per
    /// segnalare le posizioni che si ripetono periodo dopo periodo.</summary>
    public bool PagamentoRicorrente { get; set; }

    /// <summary>Id della <see cref="Clinica"/> di riferimento del fornitore (sede a cui è
    /// principalmente associato il rapporto). Null = non assegnata. Per le anagrafiche
    /// storiche viene popolata via backfill alla sede DESIO.</summary>
    public string? SedeRiferimentoId { get; set; }

    // Link al Dottore (un Dottore è anche un Fornitore quando è collaboratore/libero
    // professionista — riusa il modulo Tesoreria per gestire i suoi pagamenti).
    /// <summary>Id del Dottore se questo Fornitore è la "controparte fattura" di un dottore.</summary>
    public string? DottoreId { get; set; }

    /// <summary>Soft-delete: il record resta su DB (fatture/scadenze storiche puntano qui) ma
    /// non viene mostrato nella griglia anagrafica. Settato dalla cancellazione utente.</summary>
    public bool IsDeleted { get; set; }
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
    Cessato,
    /// <summary>Stato canonico "non più operativo". I valori legacy Sospeso/Cessato vengono trattati
    /// come dismessi in UI per i tenant che usano il modello binario Attivo/Dismesso.</summary>
    Dismesso
}

/// <summary>
/// Momento in cui il fornitore emette la fattura rispetto al pagamento.
/// </summary>
public enum EmissioneFattura
{
    /// <summary>Fattura emessa prima del pagamento.</summary>
    PrimaDelPagamento,
    /// <summary>Fattura emessa dopo il pagamento.</summary>
    DopoIlPagamento,
    /// <summary>Non disponibile / non specificato.</summary>
    Nd
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
