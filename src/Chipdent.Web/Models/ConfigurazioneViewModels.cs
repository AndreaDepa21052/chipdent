using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class ConfigurazioneIndexViewModel
{
    public WorkflowConfiguration Workflow { get; set; } = new();
    public IReadOnlyList<SogliaCoperturaRow> Soglie { get; set; } = Array.Empty<SogliaCoperturaRow>();
    public IReadOnlyList<CategoriaObbligatoriaRow> CategorieObbligatorie { get; set; } = Array.Empty<CategoriaObbligatoriaRow>();
    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
}

public record SogliaCoperturaRow(SogliaCopertura Soglia, string ClinicaNome);
public record CategoriaObbligatoriaRow(CategoriaDocumentoObbligatoria Categoria, string ClinicaNome, int DocumentiCaricati);

public class WorkflowConfigViewModel
{
    [Display(Name = "Escalation ferie lunghe al Management")]
    public bool EscaladaFerieLunghe { get; set; } = true;

    [Range(1, 60), Display(Name = "Giorni max auto-approvabili dal Direttore")]
    public int GiorniMaxAutoApprove { get; set; } = 5;

    [Display(Name = "Conferma lettura obbligatoria sulle circolari")]
    public bool CircolariConfermaObbligatoria { get; set; } = true;

    [Display(Name = "Notifica sostituzioni urgenti via email")]
    public bool NotificaSostituzioniViaEmail { get; set; }
}
