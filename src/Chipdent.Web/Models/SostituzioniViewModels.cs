using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class SostituzioniIndexViewModel
{
    public IReadOnlyList<SostituzioneRow> Aperte { get; set; } = Array.Empty<SostituzioneRow>();
    public IReadOnlyList<SostituzioneRow> Coperte { get; set; } = Array.Empty<SostituzioneRow>();
    public IReadOnlyList<SostituzioneRow> Escalate { get; set; } = Array.Empty<SostituzioneRow>();
    public IReadOnlyList<SostituzioneRow> InArrivo { get; set; } = Array.Empty<SostituzioneRow>();
    public bool CanManage { get; set; }
}

public record SostituzioneRow(RichiestaSostituzione Richiesta, string ClinicaNome);

public class NuovaSostituzioneViewModel
{
    [Required] public string AssenteDipendenteId { get; set; } = string.Empty;
    [Required, DataType(DataType.Date)] public DateTime Data { get; set; } = DateTime.Today;
    [Required, DataType(DataType.Time)] public TimeSpan OraInizio { get; set; } = new(9, 0, 0);
    [Required, DataType(DataType.Time)] public TimeSpan OraFine { get; set; } = new(13, 0, 0);
    public MotivoAssenza Motivo { get; set; } = MotivoAssenza.Malattia;
    public string? Descrizione { get; set; }
    public string? TurnoOrigineId { get; set; }

    public IReadOnlyList<Dipendente> Dipendenti { get; set; } = Array.Empty<Dipendente>();
}

public class SostituzioneCandidatiViewModel
{
    public RichiestaSostituzione Richiesta { get; set; } = new();
    public string ClinicaNome { get; set; } = string.Empty;
    public string AssenteNome { get; set; } = string.Empty;
    public IReadOnlyList<CandidatoSostituzione> Candidati { get; set; } = Array.Empty<CandidatoSostituzione>();
}

public record CandidatoSostituzione(
    string DipendenteId,
    string? UserId,
    string Nome,
    RuoloDipendente Ruolo,
    bool LiberoInQuelMomento,
    bool InFerie,
    string? MotivoNonDisponibile,
    int AiScore = 0,
    string? AiMotivazione = null,
    int CarichoOreSettimana = 0);
