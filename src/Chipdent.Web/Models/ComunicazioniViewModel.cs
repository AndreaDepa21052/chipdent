using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class ComunicazioniInboxViewModel
{
    public IReadOnlyList<Comunicazione> Lista { get; set; } = Array.Empty<Comunicazione>();
    public Comunicazione? Selezionata { get; set; }
    public string CurrentUserId { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, string> ClinicheLookup { get; set; } = new Dictionary<string, string>();
}

public class ComunicazioneFormViewModel
{
    [Required, StringLength(150)]
    public string Oggetto { get; set; } = string.Empty;

    [Required, StringLength(2000)]
    public string Corpo { get; set; } = string.Empty;

    public CategoriaComunicazione Categoria { get; set; } = CategoriaComunicazione.Generico;

    public string? ClinicaId { get; set; }

    [Display(Name = "Richiedi conferma di lettura")]
    public bool RichiedeConferma { get; set; }

    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
}
