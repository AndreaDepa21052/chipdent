using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Cashflow;

namespace Chipdent.Web.Models;

public class CashflowDashboardViewModel
{
    public CashflowForecast Forecast { get; set; } = null!;
    public List<EntrataAttesa> EntratePerOrizzonte { get; set; } = new();
}

public class CashflowSettingsFormViewModel
{
    [Range(0, double.MaxValue, ErrorMessage = "Saldo non valido.")]
    public decimal SaldoCassa { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Soglia non valida.")]
    public decimal SogliaRischio { get; set; } = 5_000m;

    public string? Note { get; set; }
    public DateTime? SaldoAggiornatoIl { get; set; }
}

public class EntrataAttesaFormViewModel
{
    public string? Id { get; set; }

    [Required]
    public DateTime DataAttesa { get; set; } = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

    [Range(0.01, double.MaxValue, ErrorMessage = "Importo deve essere > 0.")]
    public decimal Importo { get; set; }

    [Required(ErrorMessage = "Descrizione obbligatoria.")]
    public string Descrizione { get; set; } = string.Empty;

    public string? ClinicaId { get; set; }
}
