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
    /// Livello di accesso trasversale al ruolo: «sola lettura» limita l'utente alla
    /// consultazione, «lettura e scrittura» abilita le azioni di modifica consentite
    /// dal suo ruolo. Default = LetturaScrittura (comportamento storico).
    /// </summary>
    public AccessLevel AccessLevel { get; set; } = AccessLevel.LetturaScrittura;

    /// <summary>
    /// Cliniche assegnate all'utente. Vuoto = visibilità su tutte le sedi del tenant
    /// (tipico di Management/Owner). Per i Direttori contiene 1+ cliniche.
    /// </summary>
    public List<string> ClinicaIds { get; set; } = new();

    /// <summary>
    /// Quando true, l'utente usa un set personalizzato di sezioni visibili
    /// (<see cref="VisibleSections"/>) invece di ereditare la visibilità del ruolo.
    /// L'override può solo restringere le sezioni già consentite dal ruolo.
    /// </summary>
    public bool HasSectionOverride { get; set; } = false;

    /// <summary>
    /// Slug delle sezioni a cui l'utente può accedere quando
    /// <see cref="HasSectionOverride"/> è attivo. Ignorato altrimenti.
    /// </summary>
    public List<string> VisibleSections { get; set; } = new();

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
    Owner = 99,
    /// <summary>
    /// Amministratore di piattaforma: vede tutti i menu e gestisce le visibilità
    /// dei menu per ciascun ruolo. Sopra a Owner.
    /// </summary>
    PlatformAdmin = 100
}

public enum LinkedPersonType
{
    None,
    Dottore,
    Dipendente,
    Fornitore
}

/// <summary>
/// Livello di accesso assegnabile a un utente, indipendente dal ruolo.
/// I valori numerici sono stabili per la persistenza Mongo.
/// </summary>
public enum AccessLevel
{
    /// <summary>L'utente può solo consultare i dati: nessuna azione di scrittura.</summary>
    SolaLettura = 0,

    /// <summary>L'utente può creare/modificare secondo quanto consentito dal suo ruolo.</summary>
    LetturaScrittura = 10
}
