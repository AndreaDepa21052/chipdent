using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Contratto di lavoro firmato di un dipendente.
/// Lo storico contratti è la lista di tutti i Contratto per dipendente
/// ordinati per DataInizio (il più recente è il "corrente" se non scaduto).
/// </summary>
public class Contratto : TenantEntity
{
    public string DipendenteId { get; set; } = string.Empty;
    public TipoContrattoLavoro Tipo { get; set; } = TipoContrattoLavoro.TempoIndeterminato;
    public string? Livello { get; set; }
    public decimal? RetribuzioneMensileLorda { get; set; }
    public DateTime DataInizio { get; set; }

    /// <summary>Null per Tempo Indeterminato. Valorizzata per TD/Stage/Apprendistato.</summary>
    public DateTime? DataFine { get; set; }

    public string? Note { get; set; }

    /// <summary>File del contratto firmato (path relativo a wwwroot).</summary>
    public string? AllegatoNome { get; set; }
    public string? AllegatoPath { get; set; }
    public long? AllegatoSize { get; set; }

    public StatoContratto StatoCalcolato
    {
        get
        {
            if (DataFine is null) return StatoContratto.Attivo;
            var oggi = DateTime.UtcNow.Date;
            if (DataFine.Value.Date < oggi) return StatoContratto.Scaduto;
            if (DataFine.Value.Date < oggi.AddDays(7)) return StatoContratto.Critico;       // ≤ 7g
            if (DataFine.Value.Date < oggi.AddDays(30)) return StatoContratto.InScadenza;   // ≤ 30g
            if (DataFine.Value.Date < oggi.AddDays(90)) return StatoContratto.PreScadenza;  // ≤ 90g
            return StatoContratto.Attivo;
        }
    }
}

public enum TipoContrattoLavoro
{
    TempoIndeterminato,
    TempoDeterminato,
    Apprendistato,
    Stage,
    Collaborazione,
    PartitaIVA,
    Altro
}

public enum StatoContratto
{
    Attivo,
    PreScadenza,    // ≤ 90 giorni
    InScadenza,     // ≤ 30 giorni
    Critico,        // ≤ 7 giorni
    Scaduto
}
