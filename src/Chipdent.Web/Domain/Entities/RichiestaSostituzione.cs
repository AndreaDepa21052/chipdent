using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Richiesta di sostituzione urgente per un'assenza imprevista.
/// Workflow: Aperta → SostitutoProposto (notificato) → Coperta (sostituto accetta)
/// oppure EscaladaAlMgmt se nessuno copre. Annullata se l'assenza rientra.
/// </summary>
public class RichiestaSostituzione : TenantEntity
{
    public string ClinicaId { get; set; } = string.Empty;

    /// <summary>Dipendente assente.</summary>
    public string AssenteDipendenteId { get; set; } = string.Empty;
    public string AssenteNome { get; set; } = string.Empty;
    public RuoloDipendente RuoloRichiesto { get; set; }

    public DateTime Data { get; set; }
    public TimeSpan OraInizio { get; set; }
    public TimeSpan OraFine { get; set; }
    public string? TurnoOrigineId { get; set; }

    public MotivoAssenza Motivo { get; set; } = MotivoAssenza.Malattia;
    public string? Descrizione { get; set; }

    public StatoSostituzione Stato { get; set; } = StatoSostituzione.Aperta;

    /// <summary>Sostituto designato (al momento dell'apertura o dopo).</summary>
    public string? SostitutoDipendenteId { get; set; }
    public string? SostitutoUserId { get; set; }
    public string? SostitutoNome { get; set; }

    public DateTime? DataNotificaSostituto { get; set; }
    public DateTime? DataAccettazione { get; set; }
    public DateTime? DataChiusura { get; set; }
    public string? NoteSostituto { get; set; }

    public string CreatoDaUserId { get; set; } = string.Empty;
    public string CreatoDaNome { get; set; } = string.Empty;

    /// <summary>True se è scattata l'escalation al Management (nessun sostituto).</summary>
    public bool Escalata { get; set; }
    public DateTime? DataEscalation { get; set; }
}

public enum MotivoAssenza
{
    Malattia,
    Infortunio,
    Lutto,
    EmergenzaFamiliare,
    Permesso,
    Altro
}

public enum StatoSostituzione
{
    Aperta,
    SostitutoProposto,
    Coperta,
    EscaladaAlMgmt,
    Annullata
}
