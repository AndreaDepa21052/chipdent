using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class RichiestaFerie : TenantEntity
{
    /// <summary>Dipendente per cui è richiesta l'assenza.</summary>
    public string DipendenteId { get; set; } = string.Empty;

    /// <summary>User che ha creato la richiesta (può differire dal dipendente per richieste fatte dal Direttore).</summary>
    public string RichiedenteUserId { get; set; } = string.Empty;

    public string ClinicaId { get; set; } = string.Empty;

    public TipoAssenza Tipo { get; set; } = TipoAssenza.Ferie;

    public DateTime DataInizio { get; set; }
    public DateTime DataFine { get; set; }

    /// <summary>Giorni lavorativi calcolati (lun-ven, esclusi i weekend).</summary>
    public int GiorniRichiesti { get; set; }

    public string? NoteRichiesta { get; set; }

    public StatoRichiestaFerie Stato { get; set; } = StatoRichiestaFerie.InAttesa;

    public string? DecisoreUserId { get; set; }
    public DateTime? DecisoreIl { get; set; }
    public string? NoteDecisore { get; set; }

    /// <summary>True se al momento dell'approvazione il saldo del dipendente è stato già decrementato.</summary>
    public bool SaldoApplicato { get; set; }
}

public enum TipoAssenza
{
    Ferie,
    Permesso,
    Malattia,
    PermessoStudio,
    Altro
}

public enum StatoRichiestaFerie
{
    InAttesa,
    Approvata,
    Rifiutata,
    Annullata
}
