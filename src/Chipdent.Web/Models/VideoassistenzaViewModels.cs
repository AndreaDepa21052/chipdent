using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class VideoassistenzaIndexViewModel
{
    public IReadOnlyList<RichiestaAssistenza> Richieste { get; set; } = Array.Empty<RichiestaAssistenza>();
    public bool CanHandle { get; set; }
    public int InAttesa { get; set; }
    public int InCorso { get; set; }
    public int Urgenti { get; set; }
    public StatoAssistenza? Filter { get; set; }
}

public class NuovaAssistenzaViewModel
{
    public string? ClinicaId { get; set; }

    public PrioritaAssistenza Priorita { get; set; } = PrioritaAssistenza.Media;

    [Required(ErrorMessage = "Indica brevemente il motivo.")]
    [StringLength(150)]
    public string Motivo { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Descrizione { get; set; }

    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
}

public class SalaAssistenzaViewModel
{
    public RichiestaAssistenza Richiesta { get; set; } = new();
    public bool CanHandle { get; set; }
    public string UserDisplayName { get; set; } = string.Empty;
    public string? UserEmail { get; set; }
}
