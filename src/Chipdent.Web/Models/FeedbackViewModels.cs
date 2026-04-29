using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class FeedbackSubmitViewModel
{
    public string TenantId { get; set; } = string.Empty;
    public string TenantSlug { get; set; } = string.Empty;
    public string TenantNome { get; set; } = string.Empty;
    public string ClinicaId { get; set; } = string.Empty;
    public string ClinicaNome { get; set; } = string.Empty;
    public IReadOnlyList<Dottore> Dottori { get; set; } = Array.Empty<Dottore>();
}

public class FeedbackGrazieViewModel
{
    public string ClinicaNome { get; set; } = string.Empty;
    public int Score { get; set; }
}

public class FeedbackDashboardViewModel
{
    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
    public string? ClinicaIdFilter { get; set; }
    public int Giorni { get; set; } = 90;
    public IReadOnlyList<FeedbackRow> Feedbacks { get; set; } = Array.Empty<FeedbackRow>();
    public int NpsTotale { get; set; }
    public double MediaTotale { get; set; }
    public int CountTotale { get; set; }
    public int CountCritici { get; set; }
    public IReadOnlyList<FeedbackPerEntita> PerSede { get; set; } = Array.Empty<FeedbackPerEntita>();
    public IReadOnlyList<FeedbackPerEntita> PerDottore { get; set; } = Array.Empty<FeedbackPerEntita>();
}

public record FeedbackRow(FeedbackPaziente Feedback, string ClinicaNome, string? DottoreNome);

public record FeedbackPerEntita(string Id, string Nome, int Totale, int Nps, double Media);
