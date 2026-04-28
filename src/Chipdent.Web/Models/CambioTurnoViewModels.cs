using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class CambioTurnoIndexViewModel
{
    public IReadOnlyList<CambioTurnoRow> Mie { get; set; } = Array.Empty<CambioTurnoRow>();
    public IReadOnlyList<CambioTurnoRow> InArrivo { get; set; } = Array.Empty<CambioTurnoRow>();
    public IReadOnlyList<CambioTurnoRow> DaApprovare { get; set; } = Array.Empty<CambioTurnoRow>();
    public bool CanApprove { get; set; }
}

public record CambioTurnoRow(
    RichiestaCambioTurno Richiesta,
    Turno? Turno,
    string ClinicaNome,
    string DestinatarioLabel);

public class NuovaCambioTurnoViewModel
{
    [Required]
    public string TurnoId { get; set; } = string.Empty;

    [Display(Name = "Collega destinatario")]
    public string? DestinatarioUserId { get; set; }

    [Display(Name = "Note (opzionale)")]
    public string? Note { get; set; }

    public Turno? Turno { get; set; }
    public string TurnoLabel { get; set; } = string.Empty;
    public string ClinicaNome { get; set; } = string.Empty;

    public IReadOnlyList<CollegaMini> Colleghi { get; set; } = Array.Empty<CollegaMini>();
}

public record CollegaMini(string UserId, string Nome, string Ruolo);
