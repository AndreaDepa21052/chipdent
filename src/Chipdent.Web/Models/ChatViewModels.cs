using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class ChatIndexViewModel
{
    public IReadOnlyList<ChatThreadSummary> Threads { get; set; } = Array.Empty<ChatThreadSummary>();
    public ChatThreadSummary? Active { get; set; }
    public IReadOnlyList<Messaggio> Messaggi { get; set; } = Array.Empty<Messaggio>();
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserName { get; set; } = string.Empty;
    public IReadOnlyList<UserMini> ContattiDisponibili { get; set; } = Array.Empty<UserMini>();
    public IReadOnlyList<ClinicaMini> ClinicheDisponibili { get; set; } = Array.Empty<ClinicaMini>();
}

public record ChatThreadSummary(string Key, string Title, string? Sottotitolo, DateTime UltimoMsgAt, int NonLetti, bool IsClinica);
public record UserMini(string Id, string FullName, string Role);
public record ClinicaMini(string Id, string Nome);
