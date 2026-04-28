using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class ContrattiIndexViewModel
{
    public IReadOnlyList<ContrattoRow> Contratti { get; set; } = Array.Empty<ContrattoRow>();
    public StatoContratto? Filter { get; set; }
    public int TotaliPerStato(StatoContratto s) => Contratti.Count(c => c.Contratto.StatoCalcolato == s);
}

public record ContrattoRow(Contratto Contratto, string DipendenteNome, string ClinicaNome, string RuoloDipendente);

public class ContrattoFormViewModel
{
    public string? Id { get; set; }

    [Required, Display(Name = "Dipendente")]
    public string DipendenteId { get; set; } = string.Empty;

    [Display(Name = "Tipo contratto")]
    public TipoContrattoLavoro Tipo { get; set; } = TipoContrattoLavoro.TempoIndeterminato;

    [Display(Name = "Livello / inquadramento")]
    public string? Livello { get; set; }

    [Display(Name = "Retribuzione mensile lorda (€)")]
    public decimal? RetribuzioneMensileLorda { get; set; }

    [Required, Display(Name = "Data inizio"), DataType(DataType.Date)]
    public DateTime DataInizio { get; set; } = DateTime.Today;

    [Display(Name = "Data fine"), DataType(DataType.Date)]
    public DateTime? DataFine { get; set; }

    public string? Note { get; set; }

    public string? AllegatoNomeAttuale { get; set; }
    public string? AllegatoPathAttuale { get; set; }
    public Microsoft.AspNetCore.Http.IFormFile? Allegato { get; set; }

    public IReadOnlyList<Dipendente> Dipendenti { get; set; } = Array.Empty<Dipendente>();
    public bool LockedDipendente { get; set; }
}
