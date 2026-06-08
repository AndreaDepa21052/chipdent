using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class UsersIndexViewModel
{
    public IReadOnlyList<User> Users { get; set; } = Array.Empty<User>();
    public IReadOnlyList<Invito> InvitiAttivi { get; set; } = Array.Empty<Invito>();
    public IReadOnlyDictionary<string, string> PersonaLookup { get; set; } = new Dictionary<string, string>();
    public string CurrentUserId { get; set; } = string.Empty;
}

public class UserEditViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    [Required, Display(Name = "Nome completo")]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "Ruolo")]
    public UserRole Role { get; set; } = UserRole.Staff;

    [Display(Name = "Livello di accesso")]
    public AccessLevel AccessLevel { get; set; } = AccessLevel.LetturaScrittura;

    [Display(Name = "Cliniche assegnate")]
    public List<string> ClinicaIds { get; set; } = new();

    [Display(Name = "Tipo persona collegata")]
    public LinkedPersonType LinkedPersonType { get; set; } = LinkedPersonType.None;

    [Display(Name = "Persona collegata")]
    public string? LinkedPersonId { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsCurrent { get; set; }

    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
    public IReadOnlyList<Dottore> Dottori { get; set; } = Array.Empty<Dottore>();
    public IReadOnlyList<Dipendente> Dipendenti { get; set; } = Array.Empty<Dipendente>();
}

/// <summary>
/// Pagina "Accessi": elenco utenti a sinistra + editor delle sezioni per l'utente
/// selezionato a destra. <see cref="Editor"/> è null finché non si seleziona un utente.
/// </summary>
public class UserSectionAccessViewModel
{
    public IReadOnlyList<UserAccessRow> Users { get; set; } = Array.Empty<UserAccessRow>();
    public UserSectionEditorViewModel? Editor { get; set; }
}

public record UserAccessRow(
    string Id,
    string FullName,
    string Email,
    UserRole Role,
    bool HasOverride,
    bool IsCurrent,
    bool IsActive);

/// <summary>
/// Stato dell'editor delle sezioni per un singolo utente.
/// </summary>
public class UserSectionEditorViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Staff;
    public bool IsCurrent { get; set; }

    /// <summary>Se true l'utente ha un set personalizzato; altrimenti eredita dal ruolo.</summary>
    public bool HasOverride { get; set; }

    public IReadOnlyList<Chipdent.Web.Services.MenuCatalog.Group> Groups { get; set; } =
        Array.Empty<Chipdent.Web.Services.MenuCatalog.Group>();

    /// <summary>Slug effettivamente accessibili (checkbox spuntati).</summary>
    public HashSet<string> Allowed { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Slug consentiti dal ruolo: gli unici configurabili (gli altri sono disabilitati).</summary>
    public HashSet<string> RoleAvailable { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsConfigurable(string slug) => RoleAvailable.Contains(slug);
    public bool IsAllowed(string slug) => Allowed.Contains(slug);
}

public class InviteUserViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, Display(Name = "Nome completo")]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "Ruolo")]
    public UserRole Ruolo { get; set; } = UserRole.Staff;

    [Display(Name = "Livello di accesso")]
    public AccessLevel AccessLevel { get; set; } = AccessLevel.LetturaScrittura;

    [Display(Name = "Cliniche assegnate")]
    public List<string> ClinicaIds { get; set; } = new();

    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
}

public class AcceptInviteViewModel
{
    [Required]
    public string Token { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;

    [Required, MinLength(8, ErrorMessage = "Minimo 8 caratteri")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Le password non coincidono")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
