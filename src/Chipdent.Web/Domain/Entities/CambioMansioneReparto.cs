using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Storico dei cambi di ruolo/mansione/reparto del dipendente.
/// Ogni record cattura il prima→dopo dei campi anagrafici professionali
/// (Ruolo, MansioneSpecifica, Reparto) e contribuisce al report mensile dei movimenti.
/// </summary>
public class CambioMansioneReparto : TenantEntity
{
    public string DipendenteId { get; set; } = string.Empty;
    public string DipendenteNome { get; set; } = string.Empty;
    public string ClinicaId { get; set; } = string.Empty;

    public DateTime DataEffetto { get; set; } = DateTime.UtcNow;

    public RuoloDipendente? RuoloDa { get; set; }
    public RuoloDipendente? RuoloA { get; set; }

    public string? MansioneSpecificaDa { get; set; }
    public string? MansioneSpecificaA { get; set; }

    public string? RepartoDa { get; set; }
    public string? RepartoA { get; set; }

    public string? Motivo { get; set; }
    public string? Note { get; set; }

    public string DecisoDaUserId { get; set; } = string.Empty;
    public string DecisoDaNome { get; set; } = string.Empty;
}
