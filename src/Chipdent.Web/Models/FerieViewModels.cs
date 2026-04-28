using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class FerieIndexViewModel
{
    public IReadOnlyList<RichiestaFerieRow> Richieste { get; set; } = Array.Empty<RichiestaFerieRow>();
    public IReadOnlyList<RichiestaFerieRow> Mie { get; set; } = Array.Empty<RichiestaFerieRow>();
    public bool CanApprove { get; set; }
    public bool CanRequest { get; set; }
    public string? MyDipendenteId { get; set; }
    public int? MieiGiorniResidui { get; set; }
    public StatoRichiestaFerie? Filter { get; set; }
}

public record RichiestaFerieRow(
    RichiestaFerie Richiesta,
    string DipendenteNome,
    string ClinicaNome,
    string? DecisoreNome);

public class NuovaRichiestaFerieViewModel
{
    [Required]
    public string DipendenteId { get; set; } = string.Empty;

    [Display(Name = "Tipo assenza")]
    public TipoAssenza Tipo { get; set; } = TipoAssenza.Ferie;

    [Required, Display(Name = "Dal")]
    [DataType(DataType.Date)]
    public DateTime DataInizio { get; set; } = DateTime.Today;

    [Required, Display(Name = "Al")]
    [DataType(DataType.Date)]
    public DateTime DataFine { get; set; } = DateTime.Today;

    [Display(Name = "Note (opzionale)")]
    public string? Note { get; set; }

    public IReadOnlyList<Dipendente> DipendentiSelezionabili { get; set; } = Array.Empty<Dipendente>();
    public bool LockedDipendente { get; set; }
}

public class DecisioneFerieViewModel
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Display(Name = "Nota (opzionale)")]
    public string? Note { get; set; }
}
