using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class User : TenantEntity
{
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public UserRole Role { get; set; } = UserRole.Staff;

    /// <summary>
    /// Cliniche assegnate all'utente. Vuoto = visibilità su tutte le sedi del tenant
    /// (tipico di Management/Owner). Per i Direttori contiene 1+ cliniche.
    /// </summary>
    public List<string> ClinicaIds { get; set; } = new();

    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public LinkedPersonType LinkedPersonType { get; set; } = LinkedPersonType.None;
    public string? LinkedPersonId { get; set; }
    public UserPreferences Preferences { get; set; } = new();
}

public class UserPreferences
{
    public bool DigestEmail { get; set; } = true;
    public bool NotificheInApp { get; set; } = true;
    public bool SuoniNotifiche { get; set; } = false;
    public bool MostraToast { get; set; } = true;
    public string Lingua { get; set; } = "it";
    public string Densita { get; set; } = "comoda";   // comoda | compatta
}

/// <summary>
/// Ruoli applicativi allineati alla mappa funzionale Chipdent.
/// I valori numerici sono stabili per la persistenza Mongo.
/// </summary>
public enum UserRole
{
    /// <summary>Utenti esterni (fornitori) con accesso al solo portale /fornitori.</summary>
    Fornitore = -10,
    Staff = 0,
    Backoffice = 10,
    Direttore = 20,
    Management = 30,
    Owner = 99
}

public enum LinkedPersonType
{
    None,
    Dottore,
    Dipendente,
    Fornitore
}
