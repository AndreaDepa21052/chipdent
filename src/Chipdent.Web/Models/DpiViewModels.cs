using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class DpiIndexViewModel
{
    public IReadOnlyList<DpiRow> Catalogo { get; set; } = Array.Empty<DpiRow>();
    public IReadOnlyList<ConsegnaDpiRow> Consegne { get; set; } = Array.Empty<ConsegnaDpiRow>();
    public int InAttesaFirma { get; set; }
    public int InScadenza { get; set; }
    public int Scadute { get; set; }
    public bool CanManage { get; set; }
}

public record DpiRow(Dpi Dpi, string ClinicaNome, int ConsegneCount);
public record ConsegnaDpiRow(ConsegnaDpi Consegna, Dpi? Dpi, string DipendenteNome, string ClinicaNome);

public class DpiCatalogoFormViewModel
{
    public string? Id { get; set; }

    [Required]
    public string ClinicaId { get; set; } = string.Empty;

    public TipoDpi Tipo { get; set; } = TipoDpi.Mascherina;

    [Required, StringLength(120)]
    public string Nome { get; set; } = string.Empty;

    public string? Modello { get; set; }
    public string? Codice { get; set; }

    [Display(Name = "Intervallo sostituzione (giorni)")]
    public int? IntervalloSostituzioneGiorni { get; set; }

    public string? Note { get; set; }
    public bool Attivo { get; set; } = true;

    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
}

public class NuovaConsegnaDpiViewModel
{
    [Required] public string DpiId { get; set; } = string.Empty;
    [Required] public string DipendenteId { get; set; } = string.Empty;
    public int Quantita { get; set; } = 1;
    [DataType(DataType.Date)] public DateTime? ScadenzaSostituzione { get; set; }
    public string? Note { get; set; }

    public IReadOnlyList<Dpi> DpiDisponibili { get; set; } = Array.Empty<Dpi>();
    public IReadOnlyList<Dipendente> Dipendenti { get; set; } = Array.Empty<Dipendente>();
}
