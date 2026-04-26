using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class UsersIndexViewModel
{
    public IReadOnlyList<User> Users { get; set; } = Array.Empty<User>();
    public IReadOnlyList<Invito> InvitiAttivi { get; set; } = Array.Empty<Invito>();
}

public class InviteUserViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, Display(Name = "Nome completo")]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "Ruolo")]
    public UserRole Ruolo { get; set; } = UserRole.Operatore;
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
