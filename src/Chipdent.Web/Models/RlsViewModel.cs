using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class RlsOverviewViewModel
{
    public int VisiteScadenzaVicina { get; set; }
    public int VisiteScadute { get; set; }
    public int CorsiScadenzaVicina { get; set; }
    public int CorsiScaduti { get; set; }
    public int DvrInScadenza { get; set; }
    public int DvrDaApprovare { get; set; }
    public IReadOnlyList<RlsAlertItem> Alerts { get; set; } = Array.Empty<RlsAlertItem>();
}

public record RlsAlertItem(string Kind, string Title, string Subtitle, DateTime? Quando, string Severity);

public class VisitaFormViewModel
{
    public string? Id { get; set; }
    public string DipendenteId { get; set; } = string.Empty;
    public DateTime Data { get; set; } = DateTime.Today;
    public EsitoVisita Esito { get; set; } = EsitoVisita.Idoneo;
    public DateTime? ScadenzaIdoneita { get; set; } = DateTime.Today.AddYears(1);
    public string? Note { get; set; }

    public IReadOnlyList<Dipendente> Dipendenti { get; set; } = Array.Empty<Dipendente>();
}

public class CorsoFormViewModel
{
    public string? Id { get; set; }
    public string DestinatarioId { get; set; } = string.Empty;
    public DestinatarioCorso DestinatarioTipo { get; set; } = DestinatarioCorso.Dipendente;
    public TipoCorso Tipo { get; set; } = TipoCorso.Antincendio;
    public DateTime DataConseguimento { get; set; } = DateTime.Today;
    public DateTime? Scadenza { get; set; } = DateTime.Today.AddYears(3);
    public string? Note { get; set; }

    public IReadOnlyList<Dottore> Dottori { get; set; } = Array.Empty<Dottore>();
    public IReadOnlyList<Dipendente> Dipendenti { get; set; } = Array.Empty<Dipendente>();
    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
}

public class DvrFormViewModel
{
    public string? Id { get; set; }
    public string ClinicaId { get; set; } = string.Empty;
    public string Versione { get; set; } = "1.0";
    public DateTime DataApprovazione { get; set; } = DateTime.Today;
    public DateTime? ProssimaRevisione { get; set; } = DateTime.Today.AddYears(1);
    public StatoDVR Stato { get; set; } = StatoDVR.Bozza;
    public string? Note { get; set; }

    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
}
