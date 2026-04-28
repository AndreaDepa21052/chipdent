using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class SegnalazioniIndexViewModel
{
    public IReadOnlyList<SegnalazioneRow> Segnalazioni { get; set; } = Array.Empty<SegnalazioneRow>();
    public StatoSegnalazione? Filter { get; set; }
    public TipoSegnalazione? TipoFilter { get; set; }
    public bool CanResolve { get; set; }
    public int Aperte { get; set; }
    public int InLavorazione { get; set; }
    public int Urgenti { get; set; }
}

public record SegnalazioneRow(Segnalazione Segnalazione, string ClinicaNome);

public class NuovaSegnalazioneViewModel
{
    [Required, StringLength(200)]
    public string Titolo { get; set; } = string.Empty;

    [Required, StringLength(2000)]
    public string Descrizione { get; set; } = string.Empty;

    public TipoSegnalazione Tipo { get; set; } = TipoSegnalazione.GuastoAttrezzatura;
    public PrioritaSegnalazione Priorita { get; set; } = PrioritaSegnalazione.Media;

    [Required]
    public string ClinicaId { get; set; } = string.Empty;

    public Microsoft.AspNetCore.Http.IFormFile? Allegato { get; set; }

    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
    public bool LockedClinica { get; set; }
}

public class RisoluzioneSegnalazioneViewModel
{
    [Required] public string Id { get; set; } = string.Empty;
    [StringLength(2000)] public string? Note { get; set; }
}
