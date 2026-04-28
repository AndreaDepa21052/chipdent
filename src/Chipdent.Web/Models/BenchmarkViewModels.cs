namespace Chipdent.Web.Models;

public class BenchmarkViewModel
{
    public IReadOnlyList<SedeKpiSet> Sedi { get; set; } = Array.Empty<SedeKpiSet>();
    public KpiBenchmarks Benchmarks { get; set; } = new();
    public IReadOnlyList<SedeRanking> Top { get; set; } = Array.Empty<SedeRanking>();
    public IReadOnlyList<SedeRanking> Bottom { get; set; } = Array.Empty<SedeRanking>();
    public DateTime CalcolatoIl { get; set; }
}

public record SedeKpiSet(
    string ClinicaId,
    string ClinicaNome,
    string Citta,
    int Effettivi,
    int Target,
    decimal CostoMensile,
    int CompliancePercento,
    int TurniSettimana,
    int RichiesteFerieAttese,
    int SegnalazioniAperte,
    int RitardiUltimi30g,
    int OverallScore);

public class KpiBenchmarks
{
    public decimal MediaCostoMensile { get; set; }
    public decimal MediaCompliance { get; set; }
    public decimal MediaCoperturaTarget { get; set; }
    public decimal MediaRitardi { get; set; }
}

public record SedeRanking(string ClinicaNome, int Score, string MotivazionePrincipale);
