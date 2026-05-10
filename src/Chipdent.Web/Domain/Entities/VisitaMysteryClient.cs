using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Visita Mystery Client su una clinica. Conserva la scheda di valutazione,
/// il punteggio complessivo e l'eventuale report allegato.
/// </summary>
public class VisitaMysteryClient : TenantEntity
{
    public string ClinicaId { get; set; } = string.Empty;

    public DateTime DataVisita { get; set; } = DateTime.UtcNow;

    /// <summary>Codice/etichetta del Mystery Client (es. "MC-2026-04" o nome agenzia).</summary>
    public string? CodiceMystery { get; set; }

    /// <summary>Tipo di canale/contatto (telefono, presenza, web).</summary>
    public CanaleMystery Canale { get; set; } = CanaleMystery.Presenza;

    // Punteggi 1-5 per area
    public int? PunteggioAccoglienza { get; set; }
    public int? PunteggioCortesia { get; set; }
    public int? PunteggioCompetenza { get; set; }
    public int? PunteggioAmbiente { get; set; }
    public int? PunteggioFollowUp { get; set; }

    /// <summary>Punteggio complessivo (1-100 oppure 1-5 a discrezione del fornitore MC).</summary>
    public double? PunteggioComplessivo { get; set; }

    public string? PuntiDiForza { get; set; }
    public string? AreeDiMiglioramento { get; set; }
    public string? AzioniCorrettive { get; set; }
    public string? Note { get; set; }

    public string? AllegatoNome { get; set; }
    public string? AllegatoPath { get; set; }
    public long? AllegatoSize { get; set; }
}

public enum CanaleMystery
{
    Presenza,
    Telefono,
    Web,
    Email,
    Misto
}
