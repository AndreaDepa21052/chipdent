using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class HeadcountViewModel
{
    public int OrganicoTotale { get; set; }
    public int InOnboarding { get; set; }
    public int Cessati12m { get; set; }
    public int Assunti12m { get; set; }
    public decimal? TurnoverPercentuale { get; set; }
    public decimal? CostoStimatoMensile { get; set; }

    public IReadOnlyList<HeadcountSede> PerSede { get; set; } = Array.Empty<HeadcountSede>();
    public IReadOnlyList<HeadcountRuolo> PerRuolo { get; set; } = Array.Empty<HeadcountRuolo>();
    public IReadOnlyList<HeadcountContratto> PerTipoContratto { get; set; } = Array.Empty<HeadcountContratto>();
    public IReadOnlyList<HeadcountTrendMese> Trend12Mesi { get; set; } = Array.Empty<HeadcountTrendMese>();
    public IReadOnlyList<TargetSede> Target { get; set; } = Array.Empty<TargetSede>();
}

public record HeadcountSede(string ClinicaId, string ClinicaNome, int Effettivi, int Target, int Onboarding);
public record HeadcountRuolo(RuoloDipendente Ruolo, int Conteggio);
public record HeadcountContratto(TipoContratto Tipo, int Conteggio);
public record HeadcountTrendMese(DateTime Mese, int Assunti, int Cessati, int OrganicoFineMese);
public record TargetSede(string ClinicaId, string ClinicaNome, int Effettivi, int Target);
