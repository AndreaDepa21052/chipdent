using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Scheda di valutazione periodica del dipendente. Tiene traccia di periodo,
/// punteggi sintetici per area e di un giudizio finale.
/// </summary>
public class SchedaValutazione : TenantEntity
{
    public string DipendenteId { get; set; } = string.Empty;
    public string DipendenteNome { get; set; } = string.Empty;

    /// <summary>Anno/periodo a cui si riferisce la valutazione (es. "2026 H1").</summary>
    public string Periodo { get; set; } = string.Empty;

    public DateTime Data { get; set; } = DateTime.UtcNow;

    /// <summary>Nome del valutatore (responsabile/direttore).</summary>
    public string? ValutatoreNome { get; set; }

    public StatoSchedaValutazione Stato { get; set; } = StatoSchedaValutazione.Bozza;

    // Punteggi 1-5 per area (null = non valutato)
    public int? PunteggioCompetenze { get; set; }
    public int? PunteggioComportamento { get; set; }
    public int? PunteggioTeamwork { get; set; }
    public int? PunteggioPuntualita { get; set; }
    public int? PunteggioObiettivi { get; set; }

    /// <summary>Giudizio finale 1-5.</summary>
    public int? PunteggioFinale { get; set; }

    public string? Obiettivi { get; set; }
    public string? PuntiDiForza { get; set; }
    public string? AreeDiMiglioramento { get; set; }
    public string? Commenti { get; set; }

    public DateTime? DataColloquio { get; set; }
    public bool FirmaDipendente { get; set; }
    public bool FirmaValutatore { get; set; }

    public string? AllegatoNome { get; set; }
    public string? AllegatoPath { get; set; }
    public long? AllegatoSize { get; set; }

    public double? PunteggioMedio
    {
        get
        {
            var voti = new[] { PunteggioCompetenze, PunteggioComportamento, PunteggioTeamwork, PunteggioPuntualita, PunteggioObiettivi }
                .Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            return voti.Length == 0 ? null : voti.Average();
        }
    }
}

public enum StatoSchedaValutazione
{
    Bozza,
    InCorso,
    Conclusa,
    Archiviata
}
