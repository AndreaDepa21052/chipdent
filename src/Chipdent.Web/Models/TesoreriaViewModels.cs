using System.ComponentModel.DataAnnotations;
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

    /// <summary>Data programmata del bonifico (popolata quando Stato = Programmato).</summary>
    public DateTime? DataProgrammata { get; set; }

    /// <summary>Data effettiva del pagamento (popolata quando Stato = Pagato).</summary>
    public DateTime? DataPagamento { get; set; }

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

    [Required(ErrorMessage = "Ragione sociale obbligatoria.")]
    public string RagioneSociale { get; set; } = string.Empty;

    public string? PartitaIva { get; set; }
    public string? CodiceFiscale { get; set; }
    public string? CodiceSdi { get; set; }
    public string? Pec { get; set; }

    [EmailAddress(ErrorMessage = "Email non valida.")]
    public string? EmailContatto { get; set; }

    public string? Telefono { get; set; }
    public string? Indirizzo { get; set; }
    public string? Iban { get; set; }
    public CategoriaSpesa CategoriaDefault { get; set; } = CategoriaSpesa.AltreSpeseFisse;
    public StatoFornitore Stato { get; set; } = StatoFornitore.Attivo;
    public string? Note { get; set; }

    // Termini di pagamento contrattuali
    [Range(0, 365, ErrorMessage = "Giorni tra 0 e 365.")]
    public int TerminiPagamentoGiorni { get; set; } = 30;
    public BasePagamento BasePagamento { get; set; } = BasePagamento.DataFattura;

    /// <summary>Se true crea anche un User di portale per questo fornitore.</summary>
    public bool AbilitaPortale { get; set; }
    public string? PortalePassword { get; set; }
    public bool HaUtentePortale { get; set; }

    /// <summary>True se questo Fornitore è la controparte fattura di un Dottore (ombra).</summary>
    public bool IsDottoreOmbra { get; set; }
}

public class FornitoriIndexViewModel
{
    public List<FornitoreRow> Fornitori { get; set; } = new();
}

public class FornitoreRow
{
    public Fornitore Fornitore { get; set; } = null!;
    public bool HaUtentePortale { get; set; }
    public decimal EspostoCorrente { get; set; }
    public int FatturePeriodoCorrente { get; set; }
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
