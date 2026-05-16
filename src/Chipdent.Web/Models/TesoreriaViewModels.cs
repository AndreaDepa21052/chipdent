using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Common;
using Chipdent.Web.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace Chipdent.Web.Models;

public class TesoreriaIndexViewModel
{
    // ── KPI ──────────────────────────────────────────────────────
    public decimal EspostoProssimi30gg { get; set; }
    public decimal ImportoScaduto { get; set; }
    public int CountScadute { get; set; }
    public decimal DaProgrammareSettimana { get; set; }
    public int CountDaProgrammareSettimana { get; set; }
    public decimal PagatoMeseCorrente { get; set; }
    public int CountFattureInApprovazione { get; set; }
    public int CountFuoriTermini { get; set; }

    // ── Tabella scadenze (filtrata) ─────────────────────────────
    public List<RigaTesoreria> Righe { get; set; } = new();

    // ── Sintesi laterali ────────────────────────────────────────
    public List<TopFornitoreRow> TopFornitori { get; set; } = new();

    // ── Filtri attivi ───────────────────────────────────────────
    public TesoreriaFilter Filtro { get; set; } = new();
    public List<(string Id, string Nome)> CliniceLookup { get; set; } = new();
    public List<(string Id, string Nome)> FornitoriLookup { get; set; } = new();

    // ── Dati per i grafici (serializzati lato view) ─────────────
    public List<SerieMese> SpesaPerCategoria12m { get; set; } = new();
    public List<SerieMese> CashOutFuturo90gg { get; set; } = new();
    public List<SerieClinica> SpesaPerSede { get; set; } = new();
    public Dictionary<string, decimal> RipartizioneStati { get; set; } = new();
}

public class RigaTesoreria
{
    /// <summary>Tolleranza in giorni oltre cui dichiarata vs attesa è considerata mismatch.</summary>
    public const int TolleranzaMismatchGiorni = 2;

    public string ScadenzaId { get; set; } = string.Empty;
    public string FatturaId { get; set; } = string.Empty;
    public DateTime DataScadenza { get; set; }
    public DateTime? DataScadenzaAttesa { get; set; }
    public string MeseCompetenza { get; set; } = string.Empty;
    public string Loc { get; set; } = "—";
    public string ClinicaId { get; set; } = string.Empty;
    public string NumeroDoc { get; set; } = string.Empty;
    public string FornitoreNome { get; set; } = string.Empty;
    public string FornitoreId { get; set; } = string.Empty;
    public decimal Imponibile { get; set; }
    public decimal Iva { get; set; }
    public decimal Totale { get; set; }
    public MetodoPagamento Metodo { get; set; }
    public StatoScadenza Stato { get; set; }
    public CategoriaSpesa Categoria { get; set; }
    public string? Note { get; set; }
    public string? Iban { get; set; }
    /// <summary>
    /// IBAN ordinante = IBAN della Società che ha ricevuto la fattura
    /// (risolto via <c>Clinica.SocietaId → Societa.Iban</c>). È il conto
    /// DA CUI verrà pagato il fornitore. <see cref="Iban"/> è quello del
    /// beneficiario (fornitore), cioè VERSO CUI parte il bonifico.
    /// </summary>
    public string? IbanOrdinante { get; set; }
    public bool FlagBM { get; set; }
    public string? FlagEM { get; set; }
    public TipoEmissioneFattura TipoEmissione { get; set; } = TipoEmissioneFattura.NonSpecificato;
    public bool BonificoMultiploCbi { get; set; }
    public bool HasAllegato { get; set; }

    /// <summary>True se la sede destinataria è la holding del gruppo (CCH).</summary>
    public bool IsHolding { get; set; }

    /// <summary>Id della scadenza padre quando questa è una rata derivata (F24/ritenute).</summary>
    public string? ScadenzaPadreId { get; set; }

    /// <summary>Origine del caricamento (Backoffice / PortaleFornitore / ImportExcel).</summary>
    public OrigineFattura Origine { get; set; } = OrigineFattura.Backoffice;

    /// <summary>Data programmata del bonifico (popolata quando Stato = Programmato o per le righe in distinta SEPA).</summary>
    public DateTime? DataProgrammata { get; set; }

    /// <summary>Data effettiva del pagamento (popolata quando Stato = Pagato).</summary>
    public DateTime? DataPagamento { get; set; }

    /// <summary>Id della distinta SEPA che include questa scadenza. Null = non in distinta.</summary>
    public string? DistintaSepaId { get; set; }
    public bool InDistintaSepa => !string.IsNullOrEmpty(DistintaSepaId);

    /// <summary>Quando è stata caricata la fattura sorgente.</summary>
    public DateTime CaricataIl { get; set; }

    /// <summary>Nome leggibile di chi ha caricato la fattura. Per il portale fornitori
    /// è il nome del fornitore stesso. Per il back-office è l'utente che ha creato la fattura.</summary>
    public string CaricataDaNome { get; set; } = "—";

    /// <summary>True se la data dichiarata diverge dall'attesa oltre la tolleranza.</summary>
    public bool ScadenzaFuoriTermini =>
        DataScadenzaAttesa.HasValue
        && Math.Abs((DataScadenza.Date - DataScadenzaAttesa.Value.Date).TotalDays) > TolleranzaMismatchGiorni;

    public int? GiorniDelta =>
        DataScadenzaAttesa.HasValue
            ? (int)(DataScadenza.Date - DataScadenzaAttesa.Value.Date).TotalDays
            : null;

    /// <summary>True se al fornitore manca un IBAN valido e il metodo richiede il bonifico
    /// (la scadenza non può finire in distinta SEPA finché non si completa l'anagrafica).</summary>
    public bool MancaIbanFornitore { get; set; }

    /// <summary>True se manca P.IVA / Codice Fiscale sull'anagrafica fornitore (problema fiscale).</summary>
    public bool MancaIdFiscale { get; set; }

    /// <summary>True se il fornitore è stato eliminato / non risolvibile a partire dall'id.</summary>
    public bool FornitoreSconosciuto { get; set; }

    /// <summary>Lista compatta dei problemi riscontrati su questa riga (tooltip).</summary>
    public IReadOnlyList<string> Problemi
    {
        get
        {
            var lst = new List<string>();
            if (FornitoreSconosciuto) lst.Add("Fornitore non trovato in anagrafica");
            if (MancaIbanFornitore) lst.Add("IBAN beneficiario mancante o non valido");
            if (MancaIdFiscale) lst.Add("P.IVA / Codice Fiscale fornitore mancanti");
            if (ScadenzaFuoriTermini)
            {
                var delta = GiorniDelta!.Value;
                var deltaTxt = delta > 0 ? $"+{delta} gg" : $"{delta} gg";
                lst.Add($"Scadenza dichiarata diverge dai termini contratto ({deltaTxt})");
            }
            return lst;
        }
    }

    public bool HaProblemi => Problemi.Count > 0;
}

public class TopFornitoreRow
{
    public string FornitoreId { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public decimal Esposto { get; set; }
    public int NumeroScadenze { get; set; }
}

public class TesoreriaFilter
{
    public string? FornitoreId { get; set; }
    public string? ClinicaId { get; set; }
    public CategoriaSpesa? Categoria { get; set; }
    public StatoScadenza? Stato { get; set; }
    public MetodoPagamento? Metodo { get; set; }
    public DateTime? Dal { get; set; }
    public DateTime? Al { get; set; }
    public string? Q { get; set; }
    public bool SoloFuoriTermini { get; set; }

    /// <summary>Colonna su cui ordinare la griglia (data/loc/em/fornitore/totale/met/stato/inserita).</summary>
    public string? Sort { get; set; }
    /// <summary>"asc" o "desc". Default "asc" per data, "desc" per totale.</summary>
    public string? Dir { get; set; }
}

public record SerieMese(string Mese, decimal Valore);
public record SerieClinica(string Sigla, string Nome, decimal Valore);

// ── Form: nuova/edit fattura (back-office) ──────────────────────
public class FatturaFormViewModel
{
    public string? Id { get; set; }

    [Required(ErrorMessage = "Seleziona un fornitore.")]
    public string FornitoreId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Seleziona una sede.")]
    public string ClinicaId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Numero documento obbligatorio.")]
    public string Numero { get; set; } = string.Empty;

    [Required]
    public DateTime DataEmissione { get; set; } = DateTime.UtcNow.Date;

    [Required]
    public DateTime MeseCompetenza { get; set; } = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

    public CategoriaSpesa Categoria { get; set; } = CategoriaSpesa.AltreSpeseFisse;

    [Range(0, double.MaxValue, ErrorMessage = "Imponibile non valido.")]
    public decimal Imponibile { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "IVA non valida.")]
    public decimal Iva { get; set; }

    public string? Note { get; set; }
    public string? FlagEM { get; set; }
    public bool FlagBM { get; set; }

    public IFormFile? Allegato { get; set; }
    public string? AllegatoNomeAttuale { get; set; }
    public string? AllegatoPathAttuale { get; set; }

    // Scadenza (creata automaticamente in fase di approvazione, ma raccolta qui)
    public DateTime DataScadenza { get; set; } = DateTime.UtcNow.Date.AddDays(30);
    public MetodoPagamento Metodo { get; set; } = MetodoPagamento.Bonifico;

    // Lookups
    public List<Fornitore> Fornitori { get; set; } = new();
    public List<Clinica> Cliniche { get; set; } = new();

    public decimal Totale => Imponibile + Iva;
}

// ── Form: nuovo/edit fornitore (back-office) ────────────────────
public class FornitoreFormViewModel
{
    public string? Id { get; set; }

    /// <summary>Codice anagrafico interno. Auto-generato (F####) per i nuovi inserimenti
    /// se lasciato vuoto.</summary>
    public string? Codice { get; set; }

    [Required(ErrorMessage = "Ragione sociale obbligatoria.")]
    public string RagioneSociale { get; set; } = string.Empty;

    /// <summary>Ragione sociale usata sulla distinta/bonifico. Se vuota viene copiata da RagioneSociale.</summary>
    public string? RagioneSocialePagamento { get; set; }

    public string? PartitaIva { get; set; }
    public string? CodiceFiscale { get; set; }
    public string? CodiceSdi { get; set; }
    public string? Pec { get; set; }

    [EmailAddress(ErrorMessage = "Email non valida.")]
    public string? EmailContatto { get; set; }

    public string? Telefono { get; set; }
    public string? Indirizzo { get; set; }
    public string? Localita { get; set; }
    public string? Provincia { get; set; }
    public string? CodicePostale { get; set; }
    public string? Iban { get; set; }
    public CategoriaSpesa CategoriaDefault { get; set; } = CategoriaSpesa.AltreSpeseFisse;

    /// <summary>Categoria di spesa secondaria (opzionale).</summary>
    public CategoriaSpesa? CategoriaSecondaria { get; set; }

    public StatoFornitore Stato { get; set; } = StatoFornitore.Attivo;
    public string? Note { get; set; }

    /// <summary>Quando true, appende "<c>Note · NotaSecondaria</c>" del fornitore
    /// alla Scadenza.Note in fase di generazione.</summary>
    public bool AggiungiNotaSecondariaAutomaticamente { get; set; }

    /// <summary>Testo della nota secondaria del fornitore.</summary>
    public string? NotaSecondaria { get; set; }

    // Termini di pagamento contrattuali
    [Range(0, 365, ErrorMessage = "Giorni tra 0 e 365.")]
    public int TerminiPagamentoGiorni { get; set; } = 30;
    public BasePagamento BasePagamento { get; set; } = BasePagamento.DataFattura;

    /// <summary>Modalità di pagamento (in UI): prima pagamento poi fattura
    /// (<see cref="EmissioneFattura.DopoIlPagamento"/>) o prima fattura poi pagamento
    /// (<see cref="EmissioneFattura.PrimaDelPagamento"/>).</summary>
    public EmissioneFattura EmissioneFattura { get; set; } = EmissioneFattura.Nd;

    /// <summary>Pagamento ricorrente (canoni, abbonamenti, rate) — flag sì/no.</summary>
    public bool PagamentoRicorrente { get; set; }

    /// <summary>
    /// Quando true, lo <c>ScadenziarioGenerator</c> non emette scadenze automatiche
    /// per le fatture di questo fornitore: vengono solo registrate e segnalate
    /// nella tabella alert "pagamenti manuali".
    /// </summary>
    public bool PagamentiManuali { get; set; }

    /// <summary>Id della clinica di riferimento del fornitore (sede principale, legacy).
    /// Mantenuto in sync con il primo elemento non-TUTTE di <see cref="SediRiferimentoIds"/>.</summary>
    public string? SedeRiferimentoId { get; set; }

    /// <summary>Sedi di riferimento del fornitore (multi-select). Può contenere il sentinel
    /// "TUTTE" (<see cref="FornitoreSedi.Tutte"/>) o una lista di id clinica.</summary>
    public List<string> SediRiferimentoIds { get; set; } = new();

    /// <summary>Cliniche disponibili per la tendina "Sede di riferimento".</summary>
    public List<Clinica> Cliniche { get; set; } = new();

    /// <summary>Se true crea anche un User di portale per questo fornitore.</summary>
    public bool AbilitaPortale { get; set; }
    public string? PortalePassword { get; set; }
    public bool HaUtentePortale { get; set; }

    /// <summary>True se questo Fornitore è la controparte fattura di un Dottore (ombra).</summary>
    public bool IsDottoreOmbra { get; set; }
}

/// <summary>
/// Dati per il widget riusabile di selezione multipla "Sedi di riferimento" (chip + dropdown).
/// </summary>
public class SediRiferimentoSelectViewModel
{
    /// <summary>Id selezionati (può contenere il sentinel <c>FornitoreSedi.Tutte</c>).</summary>
    public List<string> Selected { get; set; } = new();

    /// <summary>Cliniche disponibili nella tendina.</summary>
    public List<Clinica> Cliniche { get; set; } = new();

    /// <summary>Nome del campo da postare (default: <c>SediRiferimentoIds</c>).</summary>
    public string? FieldName { get; set; }
}

/// <summary>
/// Modale di modifica rapida del fornitore: form principale + striscia verticale
/// a sinistra con le criticità anagrafiche/SEPA da risolvere. Speculare al pattern
/// usato per la scheda dottore.
/// </summary>
public class FornitoreEditModalViewModel
{
    public Fornitore Fornitore { get; set; } = null!;
    public FornitoreFormViewModel Form { get; set; } = null!;
    public FornitoreCompletezza.Esito Completezza { get; set; } =
        new(0, false, Array.Empty<string>(), Array.Empty<string>());
    public decimal EspostoCorrente { get; set; }
    public int FatturePeriodoCorrente { get; set; }
    public bool HaUtentePortale { get; set; }
}

public class FornitoriIndexViewModel
{
    public List<FornitoreRow> Fornitori { get; set; } = new();

    /// <summary>Cliniche del tenant — popolate la tendina "Sede di riferimento" nella griglia.</summary>
    public List<Clinica> Cliniche { get; set; } = new();
}

public class FornitoreRow
{
    public Fornitore Fornitore { get; set; } = null!;
    public bool HaUtentePortale { get; set; }
    public decimal EspostoCorrente { get; set; }
    public int FatturePeriodoCorrente { get; set; }

    /// <summary>Esito dei controlli di completezza anagrafica + dati SEPA.</summary>
    public FornitoreCompletezza.Esito Completezza { get; set; } =
        new(0, false, Array.Empty<string>(), Array.Empty<string>());
}

// ── Azione: programma/segna pagato ──────────────────────────────
public class PagamentoAzioneViewModel
{
    [Required]
    public string ScadenzaId { get; set; } = string.Empty;

    public DateTime? DataPagamento { get; set; }
    public DateTime? DataProgrammata { get; set; }
    public string? RiferimentoPagamento { get; set; }
}

// ── Dati bancari ordinante (per distinte SEPA) ──────────────────
public class DatiBancariFormViewModel
{
    [Required(ErrorMessage = "L'IBAN è obbligatorio per generare distinte SEPA.")]
    public string PagatoreIban { get; set; } = string.Empty;

    public string? PagatoreBic { get; set; }

    [Required(ErrorMessage = "La ragione sociale è obbligatoria.")]
    public string PagatoreRagioneSociale { get; set; } = string.Empty;

    public string? PagatoreCodiceFiscale { get; set; }

    public bool IsConfigured { get; set; }
}

// ── Storico distinte SEPA ───────────────────────────────────────
public class DistinteIndexViewModel
{
    public List<Chipdent.Web.Domain.Entities.DistintaPagamento> Distinte { get; set; } = new();
}

// ── Import fatture passive (CSV CCH/Ident + XLSX) ────────────────
public class ImportFattureIndexViewModel
{
    public List<ImportFattureBatchRow> Batches { get; set; } = new();
    public int TotaleBatch { get; set; }
    public int TotaleRighe { get; set; }
    public int TotaleRigheConErrore { get; set; }
    public DateTime? UltimoCaricamento { get; set; }
    /// <summary>Numero di proposte anagrafica in attesa di decisione (badge).</summary>
    public int ProposteAnagraficaInAttesa { get; set; }
}

public class ImportFattureBatchRow
{
    public string Id { get; set; } = string.Empty;
    public DateTime DataCaricamento { get; set; }
    public string CaricatoDa { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty; // Xlsx | Csv
    public List<Chipdent.Web.Domain.Entities.ImportFatturaFile> Files { get; set; } = new();
    public int TotaleRighe { get; set; }
    public int RigheValide { get; set; }
    public int RigheConErrore { get; set; }
    public string? Note { get; set; }
}

public class ImportFattureDettaglioViewModel
{
    public Chipdent.Web.Domain.Entities.ImportFatturePassiveBatch Batch { get; set; } = null!;
    public string CaricatoDaNome { get; set; } = string.Empty;
    /// <summary>Righe raggruppate per file (sheet o csv).</summary>
    public List<ImportFattureFileGroup> Files { get; set; } = new();
}

public class ImportFattureFileGroup
{
    public Chipdent.Web.Domain.Entities.ImportFatturaFile Header { get; set; } = null!;
    public List<Chipdent.Web.Domain.Entities.ImportFatturaRiga> Righe { get; set; } = new();
    public decimal TotaleDocumenti => Righe.Sum(r => r.TotaleDocumento ?? 0);
    public decimal TotaleNetto => Righe.Sum(r => r.NettoAPagare ?? 0);
    public decimal TotaleIva => Righe.Sum(r => r.Iva ?? 0);
}

// ── Genera scadenziario (dry-run + apply) ────────────────────────────
public class GeneraScadenziarioViewModel
{
    public int RigheImportateTotali { get; set; }
    public int RigheElaborate { get; set; }
    public int RigheSaltate { get; set; }
    public int FattureGenerate { get; set; }
    public int ScadenzeGenerate { get; set; }
    public int FornitoriNuovi { get; set; }

    public int ScadenzeAttuali { get; set; }
    public int FattureAttuali { get; set; }
    public int ScadenzePagateAttuali { get; set; }

    public List<AlertRow> Alerts { get; set; } = new();
    public List<AnteprimaScadenza> Anteprima { get; set; } = new();

    /// <summary>
    /// Fatture per le quali NON è stata generata scadenza perché il fornitore
    /// è marcato come «pagamenti manuali». L'operatore deve calcolare e
    /// disporre il pagamento a mano.
    /// </summary>
    public List<FatturaPagamentoManualeRow> FatturePagamentoManuale { get; set; } = new();

    public bool IsApplied { get; set; }

    public Dictionary<string, int> AlertsPerCategoria { get; set; } = new();
    public Dictionary<string, int> AlertsPerSeverita { get; set; } = new();
}

public class FatturaPagamentoManualeRow
{
    public string Fornitore { get; set; } = string.Empty;
    public string? NumeroDoc { get; set; }
    public DateTime DataDocumento { get; set; }
    public decimal Totale { get; set; }
    public string? Loc { get; set; }
    public int RigaSorgente { get; set; }
}

public class AlertRow
{
    public string Severita { get; set; } = "info"; // info | warn | err
    public string Regola { get; set; } = string.Empty;
    public string Messaggio { get; set; } = string.Empty;
    public string? Fornitore { get; set; }
    public string? NumeroDoc { get; set; }
    public DateTime? Data { get; set; }
    public int Riga { get; set; }
}

public class AnteprimaScadenza
{
    public string Fornitore { get; set; } = string.Empty;
    public string NumeroDoc { get; set; } = string.Empty;
    public DateTime DataDoc { get; set; }
    public DateTime DataScadenza { get; set; }
    public decimal Importo { get; set; }
    public string Metodo { get; set; } = string.Empty;
    public string Stato { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;
    public string LOC { get; set; } = "—";
    public string? Iban { get; set; }
    public string? Note { get; set; }
}
