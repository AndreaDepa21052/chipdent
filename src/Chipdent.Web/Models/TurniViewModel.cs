using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class TurniWeekViewModel
{
    public DateTime WeekStart { get; set; }
    public DateTime WeekEnd => WeekStart.AddDays(6);
    public IReadOnlyList<DateTime> Days => Enumerable.Range(0, 7).Select(i => WeekStart.AddDays(i)).ToArray();

    public IReadOnlyList<PersonaRow> Righe { get; set; } = Array.Empty<PersonaRow>();
    public IReadOnlyDictionary<string, string> ClinicheLookup { get; set; } = new Dictionary<string, string>();
    public IReadOnlyList<TurnoTemplate> Templates { get; set; } = Array.Empty<TurnoTemplate>();
    public IReadOnlyList<ConflittoTurno> Conflitti { get; set; } = Array.Empty<ConflittoTurno>();
    public bool CanEdit { get; set; }
    public int CoperturaMinimaPerGiorno { get; set; } = 2;
}

public record ConflittoTurno(string PersonaId, string PersonaNome, DateTime Data, string Motivo);

public record PersonaRow(string Id, TipoPersona Tipo, string Nome, string Sotto, IReadOnlyList<Turno> Turni);

public class TurnoFormViewModel
{
    public string? Id { get; set; }
    public DateTime Data { get; set; } = DateTime.Today;
    public TimeSpan OraInizio { get; set; } = new(8, 30, 0);
    public TimeSpan OraFine { get; set; } = new(13, 0, 0);
    public string? ClinicaId { get; set; }
    public string? PersonaId { get; set; }
    public TipoPersona TipoPersona { get; set; } = TipoPersona.Dottore;
    public string? Note { get; set; }

    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
    public IReadOnlyList<Dottore> Dottori { get; set; } = Array.Empty<Dottore>();
    public IReadOnlyList<Dipendente> Dipendenti { get; set; } = Array.Empty<Dipendente>();
    public DateTime ReturnWeek { get; set; } = DateTime.Today;
}
