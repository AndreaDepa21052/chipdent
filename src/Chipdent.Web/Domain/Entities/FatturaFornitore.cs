using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Fattura passiva ricevuta da un fornitore. Una fattura genera 1+ scadenze
/// di pagamento (collezione separata <see cref="ScadenzaPagamento"/>).
/// </summary>
public class FatturaFornitore : TenantEntity
{
    public string FornitoreId { get; set; } = string.Empty;

    /// <summary>Sede destinataria della spesa (LOC nell'Excel: VAR/CCH/BUS/MI7/MI9…).</summary>
    public string ClinicaId { get; set; } = string.Empty;

    public string Numero { get; set; } = string.Empty;
    public DateTime DataEmissione { get; set; }

    /// <summary>Mese/anno di competenza economica (separato dalla data di emissione).</summary>
    public DateTime MeseCompetenza { get; set; }

    public CategoriaSpesa Categoria { get; set; } = CategoriaSpesa.AltreSpeseFisse;

    /// <summary>Imponibile (importo netto IVA esclusa).</summary>
    public decimal Imponibile { get; set; }
    public decimal Iva { get; set; }
    public decimal Totale { get; set; }

    /// <summary>Flag legacy mantenuti per fedeltà al file Excel originario.</summary>
    public string? FlagEM { get; set; }   // E/M: Emessa/Manuale
    public bool FlagBM { get; set; }      // BM: Bonifico Manuale

    public string? Note { get; set; }

    public StatoFattura Stato { get; set; } = StatoFattura.Caricata;

    /// <summary>Motivo del rifiuto (se Stato = Rifiutata).</summary>
    public string? MotivoRifiuto { get; set; }

    /// <summary>Allegato PDF/XML della fattura (path relativo a wwwroot).</summary>
    public string? AllegatoNome { get; set; }
    public string? AllegatoPath { get; set; }
    public long? AllegatoSize { get; set; }

    /// <summary>Origine del caricamento (back-office o portale fornitore).</summary>
    public OrigineFattura Origine { get; set; } = OrigineFattura.Backoffice;

    public string? CaricataDaUserId { get; set; }
    public DateTime? ApprovataIl { get; set; }
    public string? ApprovataDaUserId { get; set; }
}

public enum StatoFattura
{
    /// <summary>Caricata da fornitore o backoffice, in attesa di approvazione.</summary>
    Caricata,
    /// <summary>Validata: generate le scadenze e pronta al pagamento.</summary>
    Approvata,
    /// <summary>Rifiutata dall'owner (vedi MotivoRifiuto).</summary>
    Rifiutata
}

public enum OrigineFattura
{
    Backoffice,
    PortaleFornitore,
    ImportExcel
}
