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

public class PermissionsMatrixViewModel
{
    public IReadOnlyList<PermissionRow> Rows { get; set; } = Array.Empty<PermissionRow>();
    public IReadOnlyList<UserRole> Roles { get; set; } = Array.Empty<UserRole>();
    public IReadOnlyDictionary<UserRole, int> CountByRole { get; set; } = new Dictionary<UserRole, int>();
}

public record PermissionRow(string Modulo, string Azione, IReadOnlyDictionary<UserRole, bool> Allowed);

public class InviteUserViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, Display(Name = "Nome completo")]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "Ruolo")]
    public UserRole Ruolo { get; set; } = UserRole.Staff;

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
