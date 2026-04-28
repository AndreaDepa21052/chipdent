using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public enum DashboardMode
{
    Management,
    Direttore,
    Staff
}

public class DashboardViewModel
{
    public DashboardMode Mode { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;

    // Management
    public int ClinicheTotali { get; set; }
    public int ClinicheOperative { get; set; }
    public int DottoriAttivi { get; set; }
    public int DipendentiAttivi { get; set; }
    public int RlsInScadenza { get; set; }
    public int DocumentiInScadenza { get; set; }
    public int RichiesteFerieInAttesa { get; set; }

    // Direttore
    public IReadOnlyList<ClinicaSummary> ClinicheDelDirettore { get; set; } = Array.Empty<ClinicaSummary>();
    public IReadOnlyList<TurnoOggi> TurniOggi { get; set; } = Array.Empty<TurnoOggi>();
    public IReadOnlyList<RichiestaFerie> FerieDaApprovare { get; set; } = Array.Empty<RichiestaFerie>();
    public int ConflittiTurniSettimana { get; set; }

    // Staff
    public string? StaffNome { get; set; }
    public int? StaffSaldoFerie { get; set; }
    public IReadOnlyList<TurnoOggi> MieiProssimiTurni { get; set; } = Array.Empty<TurnoOggi>();
    public int MieRichiesteInAttesa { get; set; }
    public int CircolariNonLette { get; set; }

    // Comune
    public IReadOnlyList<AttivitaRecente> Attivita { get; set; } = Array.Empty<AttivitaRecente>();
}

public record ClinicaSummary(string Id, string Nome, int DipendentiAttivi, int TurniSettimana, int DocumentiScadutiOPresto);
public record TurnoOggi(string Persona, string Ruolo, string Clinica, string Orario);
public record AttivitaRecente(DateTime When, string Title, string Description, string Kind);
