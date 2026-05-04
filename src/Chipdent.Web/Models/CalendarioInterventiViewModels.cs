using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

/// <summary>Vista d'insieme: una sezione per ogni tipologia di intervento, riga per ogni clinica
/// (replica della struttura del file "Calendario interventi.xlsx").</summary>
public class CalendarioInterventiViewModel
{
    public List<SezioneIntervento> Sezioni { get; set; } = new();
    public List<Clinica> Cliniche { get; set; } = new();

    /// <summary>Intervento "in scadenza nei prossimi N giorni" — usato per badge KPI in alto.</summary>
    public int Imminenti { get; set; }
    public int Scaduti { get; set; }
    public int Totali { get; set; }
}

public class SezioneIntervento
{
    public TipoIntervento Tipo { get; set; }
    public string Titolo { get; set; } = string.Empty;
    public string FornitoreDefault { get; set; } = string.Empty;
    public string Frequenza { get; set; } = string.Empty;
    public string Icona { get; set; } = "🛠️";
    public List<RigaIntervento> Righe { get; set; } = new();
}

public class RigaIntervento
{
    public string ClinicaId { get; set; } = string.Empty;
    public string ClinicaNome { get; set; } = string.Empty;

    public InterventoClinica? Intervento { get; set; }
}
