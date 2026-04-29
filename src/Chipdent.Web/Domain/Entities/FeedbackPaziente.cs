using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Feedback NPS lasciato anonimamente da un paziente a fine visita, scansionando il QR esposto in studio.
/// Nessun PII salvato (neanche IP): solo punteggio, sede, dottore opzionale, commento testuale.
/// La dashboard aggrega per sede/dottore con calcolo NPS standard (% promotori - % detrattori).
/// </summary>
public class FeedbackPaziente : TenantEntity
{
    public string ClinicaId { get; set; } = string.Empty;

    /// <summary>Dottore selezionato dal paziente (facoltativo).</summary>
    public string? DottoreId { get; set; }

    /// <summary>Punteggio 0-10 stile NPS classico.</summary>
    public int Score { get; set; }

    /// <summary>Commento libero del paziente (facoltativo).</summary>
    public string? Commento { get; set; }

    /// <summary>True se il punteggio è ≤ 6 e/o commento contiene parole chiave critiche → flag al direttore.</summary>
    public bool DaApprofondire { get; set; }
}
