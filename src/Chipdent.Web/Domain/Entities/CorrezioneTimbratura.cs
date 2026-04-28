using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Richiesta di correzione di una timbratura inviata dal dipendente al direttore.
/// Workflow: Aperta → Approvata (la timbratura viene aggiornata/aggiunta) o Respinta.
/// </summary>
public class CorrezioneTimbratura : TenantEntity
{
    public string DipendenteId { get; set; } = string.Empty;
    public string DipendenteNome { get; set; } = string.Empty;

    /// <summary>Id timbratura da correggere; null se la richiesta è di aggiungerne una nuova.</summary>
    public string? TimbraturaId { get; set; }

    public TipoCorrezione Tipo { get; set; } = TipoCorrezione.Modifica;

    /// <summary>Tipo timbratura proposto (per modifica/aggiunta).</summary>
    public TipoTimbratura TipoTimbraturaProposto { get; set; }
    public DateTime TimestampProposto { get; set; }
    public bool RemotoProposto { get; set; }

    public string Motivazione { get; set; } = string.Empty;

    public StatoCorrezione Stato { get; set; } = StatoCorrezione.Aperta;
    public string? DecisoreUserId { get; set; }
    public string? DecisoreNome { get; set; }
    public DateTime? DataDecisione { get; set; }
    public string? NoteDecisore { get; set; }
}

public enum TipoCorrezione
{
    Aggiungi,
    Modifica,
    Elimina
}

public enum StatoCorrezione
{
    Aperta,
    Approvata,
    Respinta,
    Annullata
}

/// <summary>
/// Approvazione formale del timesheet mensile di un dipendente da parte del direttore.
/// Conserva uno snapshot dei totali al momento dell'approvazione per audit/paghe.
/// </summary>
public class ApprovazioneTimesheet : TenantEntity
{
    public string DipendenteId { get; set; } = string.Empty;
    public string DipendenteNome { get; set; } = string.Empty;
    /// <summary>"yyyy-MM" del mese approvato.</summary>
    public string Periodo { get; set; } = string.Empty;

    public StatoApprovazioneTimesheet Stato { get; set; } = StatoApprovazioneTimesheet.InAttesa;

    public string? DirettoreUserId { get; set; }
    public string? DirettoreNome { get; set; }
    public DateTime? ApprovatoIl { get; set; }
    public string? Note { get; set; }

    // Snapshot totali (in minuti per evitare problemi di serializzazione TimeSpan su Mongo)
    public int OreLavorateMin { get; set; }
    public int OrePianificateMin { get; set; }
    public int OrePausaMin { get; set; }
    public int SaldoOreMin { get; set; }
    public int Ritardi { get; set; }
    public int UsciteAnticipate { get; set; }
    public int GiorniLavorati { get; set; }
}

public enum StatoApprovazioneTimesheet
{
    InAttesa,
    Approvato,
    Contestato
}
