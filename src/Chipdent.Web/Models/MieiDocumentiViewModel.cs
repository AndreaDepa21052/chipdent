using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class MieiDocumentiViewModel
{
    public string DipendenteNome { get; set; } = string.Empty;
    public string? RuoloDipendente { get; set; }
    public string? ClinicaNome { get; set; }
    public bool HasLinkedPerson { get; set; }

    public Contratto? ContrattoAttuale { get; set; }
    public IReadOnlyList<Contratto> ContrattiStorici { get; set; } = Array.Empty<Contratto>();

    public IReadOnlyList<VisitaMedica> Visite { get; set; } = Array.Empty<VisitaMedica>();
    public IReadOnlyList<Corso> Corsi { get; set; } = Array.Empty<Corso>();
}
