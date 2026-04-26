namespace Chipdent.Web.Models;

public class DashboardViewModel
{
    public string TenantName { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public int ClinicheTotali { get; set; }
    public int ClinicheOperative { get; set; }
    public int DottoriAttivi { get; set; }
    public int DipendentiAttivi { get; set; }
    public int RlsInScadenza { get; set; }
    public IReadOnlyList<TurnoOggi> TurniOggi { get; set; } = Array.Empty<TurnoOggi>();
    public IReadOnlyList<AttivitaRecente> Attivita { get; set; } = Array.Empty<AttivitaRecente>();
}

public record TurnoOggi(string Persona, string Ruolo, string Clinica, string Orario);

public record AttivitaRecente(DateTime When, string Title, string Description, string Kind);
