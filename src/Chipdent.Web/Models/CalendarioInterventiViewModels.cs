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

    /// <summary>Lista degli interventi più urgenti (scaduti + ≤60gg), già ordinata.
    /// Alimenta il pannello proattivo "Azioni urgenti".</summary>
    public List<AzioneUrgente> Urgenti { get; set; } = new();
}

/// <summary>Riga del pannello proattivo: scadenza imminente o già scaduta su cui il backoffice deve agire.</summary>
public class AzioneUrgente
{
    public InterventoClinica Intervento { get; set; } = null!;
    public string ClinicaNome { get; set; } = string.Empty;
    public string TitoloSezione { get; set; } = string.Empty;
    public string IconaSezione { get; set; } = "🛠️";
    public Chipdent.Web.Controllers.CalendarioInterventiController.FornitoreInfo? Fornitore { get; set; }
    public int GiorniAllaScadenza { get; set; }
    public bool IsScaduto => GiorniAllaScadenza < 0;

    /// <summary>Costruisce un mailto: pre-compilato verso il fornitore (oggetto + corpo standard).</summary>
    public string MailtoUrl()
    {
        var dest = Fornitore?.Email ?? "";
        var scad = Intervento.ProssimaScadenza?.ToString("dd/MM/yyyy") ?? "—";
        var oggetto = Uri.EscapeDataString($"Confident · {TitoloSezione} sede {ClinicaNome} — scadenza {scad}");
        var corpo = Uri.EscapeDataString(
            $"Buongiorno,\n\nin riferimento alla manutenzione \"{TitoloSezione}\" della nostra sede {ClinicaNome}, " +
            $"in scadenza il {scad}, vi chiediamo cortesemente di concordare una data utile per l'intervento.\n\n" +
            $"Restiamo in attesa di un vostro riscontro.\n\nCordiali saluti,\nBackoffice Confident");
        return $"mailto:{dest}?subject={oggetto}&body={corpo}";
    }
}

public class SezioneIntervento
{
    public TipoIntervento Tipo { get; set; }
    public string Titolo { get; set; } = string.Empty;
    public string FornitoreDefault { get; set; } = string.Empty;
    public string Frequenza { get; set; } = string.Empty;
    public string Icona { get; set; } = "🛠️";
    public Chipdent.Web.Controllers.CalendarioInterventiController.FornitoreInfo? FornitoreInfo { get; set; }
    public List<RigaIntervento> Righe { get; set; } = new();
}

public class RigaIntervento
{
    public string ClinicaId { get; set; } = string.Empty;
    public string ClinicaNome { get; set; } = string.Empty;

    public InterventoClinica? Intervento { get; set; }
}
