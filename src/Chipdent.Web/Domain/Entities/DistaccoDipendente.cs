using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Distacco temporaneo di un dipendente presso una clinica diversa dalla sede di
/// assegnazione principale. Storicizzato: ogni distacco è un record separato con
/// inizio e (opzionale) fine. Quello "corrente" è il record più recente con
/// DataFine null o ≥ oggi.
/// </summary>
public class DistaccoDipendente : TenantEntity
{
    public string DipendenteId { get; set; } = string.Empty;
    /// <summary>Sede di destinazione del distacco (la sede principale resta su Dipendente.ClinicaId).</summary>
    public string ClinicaDistaccoId { get; set; } = string.Empty;
    public DateTime DataInizio { get; set; }
    public DateTime? DataFine { get; set; }
    public string? Motivo { get; set; }
    public string? Note { get; set; }

    /// <summary>Calcolato: durata effettiva in mesi (su DataFine o sulla data odierna se in corso).</summary>
    public int DurataMesi
    {
        get
        {
            var fine = DataFine ?? DateTime.UtcNow;
            return Math.Max(0, (int)Math.Floor((fine - DataInizio).TotalDays / 30.44));
        }
    }

    public bool IsInCorso => DataFine is null || DataFine.Value.Date >= DateTime.UtcNow.Date;
}
