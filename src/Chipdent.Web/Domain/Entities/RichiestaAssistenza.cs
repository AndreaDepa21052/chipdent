using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Richiesta di video-assistenza: lo Staff o il Direttore di sede chiede aiuto al Backoffice
/// e si apre una sala Jitsi condivisa. Workflow: InAttesa → InCorso → Chiusa (o Annullata).
/// </summary>
public class RichiestaAssistenza : TenantEntity
{
    public string RichiedenteUserId { get; set; } = string.Empty;
    public string RichiedenteNome   { get; set; } = string.Empty;
    public string RichiedenteRuolo  { get; set; } = string.Empty;
    public string? ClinicaId        { get; set; }
    public string? ClinicaNome      { get; set; }

    public PrioritaAssistenza Priorita  { get; set; } = PrioritaAssistenza.Media;
    public string Motivo                 { get; set; } = string.Empty;
    public string? Descrizione           { get; set; }

    public StatoAssistenza Stato { get; set; } = StatoAssistenza.InAttesa;

    /// <summary>Nome stanza Jitsi (UUID). Generato alla creazione, lo stesso per richiedente e operatore.</summary>
    public string RoomId { get; set; } = Guid.NewGuid().ToString("N");

    public string? OperatoreUserId { get; set; }
    public string? OperatoreNome   { get; set; }
    public DateTime? PresaInCaricoAt { get; set; }
    public DateTime? ChiusaAt        { get; set; }
    public string?   NoteChiusura    { get; set; }
}

public enum PrioritaAssistenza
{
    Bassa,
    Media,
    Alta,
    Urgente
}

public enum StatoAssistenza
{
    InAttesa,
    InCorso,
    Chiusa,
    Annullata
}
