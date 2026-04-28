using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class ReportIndexViewModel
{
    public DateTime Mese { get; set; } = DateTime.Today;
    public IReadOnlyList<ClinicaPresenzeRow> PresenzeMese { get; set; } = Array.Empty<ClinicaPresenzeRow>();
    public IReadOnlyList<ClinicaCostoRow> CostoPersonale { get; set; } = Array.Empty<ClinicaCostoRow>();
    public IReadOnlyList<SedeComplianceRow> ComplianceIndex { get; set; } = Array.Empty<SedeComplianceRow>();
    public TurnoverPeriodo Turnover { get; set; } = new(0, 0, null);
}

public record ClinicaPresenzeRow(string ClinicaId, string ClinicaNome, int OreLavorate, int Turni, int GiorniFerie);
public record ClinicaCostoRow(string ClinicaId, string ClinicaNome, decimal CostoMensileLordo, int Dipendenti);
public record SedeComplianceRow(string ClinicaId, string ClinicaNome, int Punteggio100, int VisiteOk, int VisiteScadute, int CorsiOk, int CorsiScaduti, int DocsOk, int DocsScaduti);
public record TurnoverPeriodo(int Assunti, int Cessati, decimal? Percentuale);

public class FormazioneIndexViewModel
{
    public IReadOnlyList<DottoreEcmRow> Dottori { get; set; } = Array.Empty<DottoreEcmRow>();
    public int InRegola { get; set; }
    public int InRitardo { get; set; }
    public int Critici { get; set; }
}

public record DottoreEcmRow(Dottore Dottore, int CreditiAcquisiti, int CreditiRichiesti, int? AnnoFineTriennio, EcmStato Stato);

public enum EcmStato { InRegola, InRitardo, Critico, NonConfigurato }
