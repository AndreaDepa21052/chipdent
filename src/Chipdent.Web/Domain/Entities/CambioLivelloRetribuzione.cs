using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Storico dei cambi di livello contrattuale e/o di retribuzione del dipendente.
/// Ogni record cattura il prima→dopo e contribuisce al report mensile dei movimenti.
/// </summary>
public class CambioLivelloRetribuzione : TenantEntity
{
    public string DipendenteId { get; set; } = string.Empty;
    public string DipendenteNome { get; set; } = string.Empty;
    public string ClinicaId { get; set; } = string.Empty;

    public DateTime DataEffetto { get; set; } = DateTime.UtcNow;

    public string? LivelloDa { get; set; }
    public string? LivelloA { get; set; }

    public decimal? RetribuzioneDa { get; set; }
    public decimal? RetribuzioneA { get; set; }

    public TipoCambioLivello Tipo { get; set; } = TipoCambioLivello.AumentoLivello;
    public string? Motivo { get; set; }
    public string? Note { get; set; }

    public string DecisoDaUserId { get; set; } = string.Empty;
    public string DecisoDaNome { get; set; } = string.Empty;
}

public enum TipoCambioLivello
{
    AumentoLivello,
    AumentoRetributivo,
    AumentoLivelloERetributivo,
    Altro
}
