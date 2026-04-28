using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Richiesta di cambio turno: lo Staff propone a un collega di prendere il proprio turno.
/// Il workflow è: <see cref="StatoCambioTurno.InAttesa"/> → AccettataDaCollega → ApprovataDirettore.
/// All'approvazione finale il <c>PersonaId</c> del turno originale viene sostituito.
/// </summary>
public class RichiestaCambioTurno : TenantEntity
{
    public string TurnoId { get; set; } = string.Empty;
    public string ClinicaId { get; set; } = string.Empty;

    /// <summary>UserId del richiedente (chi cede il turno).</summary>
    public string RichiedenteUserId { get; set; } = string.Empty;
    public string RichiedenteNome { get; set; } = string.Empty;
    public TipoPersona TipoPersona { get; set; }
    public string PersonaIdRichiedente { get; set; } = string.Empty;

    /// <summary>Collega proposto come sostituto. <c>null</c> = broadcast (chiunque può accettare).</summary>
    public string? DestinatarioUserId { get; set; }
    public string? DestinatarioNome { get; set; }

    /// <summary>Quando un collega accetta in caso di broadcast, qui viene fissato il primo che accetta.</summary>
    public string? CollegaAccettanteUserId { get; set; }
    public string? CollegaAccettanteNome { get; set; }
    /// <summary>PersonaId (Dottore o Dipendente) del collega accettante, usato per swap del turno.</summary>
    public string? PersonaIdCollegaAccettante { get; set; }

    public StatoCambioTurno Stato { get; set; } = StatoCambioTurno.InAttesa;

    public string? NoteRichiesta { get; set; }
    public string? NoteCollega { get; set; }
    public string? NoteDirettore { get; set; }

    public DateTime? DataAccettazioneCollega { get; set; }
    public DateTime? DataDecisioneDirettore { get; set; }
    public string? DirettoreUserId { get; set; }
    public string? DirettoreNome { get; set; }
}

public enum StatoCambioTurno
{
    InAttesa,
    AccettataDaCollega,
    RifiutataDaCollega,
    ApprovataDirettore,
    RifiutataDirettore,
    Annullata
}
