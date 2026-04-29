using Chipdent.Web.Infrastructure.Insights;

namespace Chipdent.Web.Models;

public class PredizioneAssenzeViewModel
{
    public int Orizzonte { get; set; } = 7;
    public IReadOnlyList<RischioAssenzaRow> Rischi { get; set; } = Array.Empty<RischioAssenzaRow>();
    public int ScoreMedio { get; set; }
    public int Critici { get; set; }
}

public record RischioAssenzaRow(RischioAssenza Rischio, string ClinicaNome);
