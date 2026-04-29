using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

// ── Ronda ──

public class RondaIndexViewModel
{
    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
    public string? ClinicaIdFilter { get; set; }
    public int Giorni { get; set; } = 30;
    public IReadOnlyList<RondaRow> Ronde { get; set; } = Array.Empty<RondaRow>();
    public bool AperturaOggi { get; set; }
    public bool ChiusuraOggi { get; set; }
    public int AnomalieAperte { get; set; }
}

public record RondaRow(RondaSicurezza Ronda, string ClinicaNome);

public class RondaFormViewModel
{
    public TipoRonda Tipo { get; set; } = TipoRonda.Apertura;

    [Required]
    public string? ClinicaId { get; set; }

    public List<RondaItem> Items { get; set; } = new();
    public string? Note { get; set; }
    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
}

// ── Inventario ──

public class InventarioIndexViewModel
{
    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
    public string? ClinicaIdFilter { get; set; }
    public IReadOnlyList<InventarioRow> Items { get; set; } = Array.Empty<InventarioRow>();
    public int SottoSoglia { get; set; }
}

public record InventarioRow(Consumabile Consumabile, string ClinicaNome);

public class ConsumabileFormViewModel
{
    [Required, StringLength(150)]
    public string Nome { get; set; } = string.Empty;

    [Required]
    public string ClinicaId { get; set; } = string.Empty;

    public string? Categoria { get; set; }
    public string? UnitaMisura { get; set; } = "pz";

    [Range(0, int.MaxValue)]
    public int GiacenzaCorrente { get; set; } = 0;

    [Range(0, int.MaxValue)]
    public int SogliaMinima { get; set; } = 0;

    public string? Fornitore { get; set; }
    public string? CodiceFornitore { get; set; }

    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
}
