using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Header (batch) di un caricamento di fatture passive da file (XLSX a 2 sheet,
/// oppure 1-2 CSV "CCH/Ident"). I batch sono <b>append-only</b>: non vengono mai
/// cancellati né modificati dopo l'inserimento, per garantire un log completo
/// delle importazioni.
/// </summary>
public class ImportFatturePassiveBatch : TenantEntity
{
    public DateTime DataCaricamento { get; set; } = DateTime.UtcNow;

    public string CaricatoDaUserId { get; set; } = string.Empty;
    public string CaricatoDaNome { get; set; } = string.Empty;

    /// <summary>Tipologia del caricamento: pacchetto Excel multi-sheet oppure CSV singoli.</summary>
    public TipoImportFatture Tipo { get; set; }

    /// <summary>Files contenuti nel batch (1 per CSV, N per i sheet di un XLSX).</summary>
    public List<ImportFatturaFile> Files { get; set; } = new();

    public int TotaleRighe { get; set; }
    public int RigheValide { get; set; }
    public int RigheConErrore { get; set; }

    public string? Note { get; set; }
}

public enum TipoImportFatture
{
    Xlsx = 0,
    Csv = 1
}

/// <summary>
/// Singolo file logico contenuto in un batch (sheet di XLSX o file CSV).
/// </summary>
public class ImportFatturaFile
{
    public string NomeFile { get; set; } = string.Empty;

    /// <summary>Etichetta della sezione: "CCH" o "Ident" (oppure "Sconosciuta").</summary>
    public string Sezione { get; set; } = string.Empty;

    public long DimensioneByte { get; set; }
    public string? ChecksumSha256 { get; set; }

    public int RigheTotali { get; set; }
    public int RigheValide { get; set; }
    public int RigheConErrore { get; set; }
}

/// <summary>
/// Riga di fattura passiva importata (snapshot del file caricato). Append-only:
/// non viene mai cancellata né modificata. Le righe restano consultabili dal
/// dettaglio del batch anche se in futuro le fatture verranno spostate nel
/// modello operativo (collection <c>fatture</c>).
/// </summary>
public class ImportFatturaRiga : TenantEntity
{
    public string BatchId { get; set; } = string.Empty;
    public string NomeFile { get; set; } = string.Empty;
    public string Sezione { get; set; } = string.Empty;
    public int NumeroRiga { get; set; }

    public string? GestioniCollegate { get; set; }
    public string? AttIva { get; set; }
    public string? RegimeIva { get; set; }
    public string? SezioneRegistro { get; set; }
    public string? Protocollo { get; set; }

    public DateTime? DataRegistrazione { get; set; }
    public string? Numero { get; set; }
    public DateTime? DataDocumento { get; set; }
    public DateTime? DataRicezione { get; set; }

    public string? Fornitore { get; set; }
    public string? TipoDocumento { get; set; }

    public decimal? TotaleDocumento { get; set; }
    public decimal? Iva { get; set; }
    public decimal? NettoAPagare { get; set; }
    public decimal? Ritenuta { get; set; }

    public string? Valuta { get; set; }
    public string? Causale { get; set; }
    public string? Allegati { get; set; }

    public List<string> Errori { get; set; } = new();
    public bool HaErrori => Errori.Count > 0;
}
