using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Messaggio in una chat 1:1 (DM tra due utenti) o di sede (gruppo per clinica).
/// Esattamente uno di <c>DestinatarioUserId</c> e <c>ClinicaGroupId</c> è valorizzato.
/// </summary>
public class Messaggio : TenantEntity
{
    public string MittenteUserId { get; set; } = string.Empty;
    public string MittenteNome { get; set; } = string.Empty;

    /// <summary>Per i DM: id dell'altro utente. Null per i gruppi sede.</summary>
    public string? DestinatarioUserId { get; set; }

    /// <summary>Per i gruppi sede: id della clinica. Null per i DM.</summary>
    public string? ClinicaGroupId { get; set; }

    public string Testo { get; set; } = string.Empty;

    /// <summary>User che hanno letto questo messaggio. Per DM ne basta uno (il destinatario).</summary>
    public List<string> LettoDaUserIds { get; set; } = new();

    public bool IsDirectMessage => !string.IsNullOrEmpty(DestinatarioUserId);
    public bool IsClinicaGroup => !string.IsNullOrEmpty(ClinicaGroupId);

    /// <summary>Identificatore canonico del thread DM (id1|id2 ordinati alfabeticamente).</summary>
    public static string DmThreadKey(string a, string b)
    {
        var pair = new[] { a, b }.OrderBy(s => s, StringComparer.Ordinal).ToArray();
        return $"dm:{pair[0]}|{pair[1]}";
    }

    /// <summary>Identificatore canonico del thread di una sede.</summary>
    public static string ClinicaThreadKey(string clinicaId) => $"clinica:{clinicaId}";
}
