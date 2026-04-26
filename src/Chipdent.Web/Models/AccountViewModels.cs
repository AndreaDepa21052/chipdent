using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class MyProfileViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    [Required, Display(Name = "Nome completo")]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "Telefono")]
    public string? Phone { get; set; }

    public UserRole Role { get; set; }
    public LinkedPersonType LinkedPersonType { get; set; }
    public string? LinkedPersonId { get; set; }
    public string? LinkedPersonName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password), Display(Name = "Password attuale")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), MinLength(8, ErrorMessage = "Minimo 8 caratteri")]
    [Display(Name = "Nuova password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "Le password non coincidono")]
    [Display(Name = "Conferma nuova password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class PreferencesViewModel
{
    [Display(Name = "Notifiche in-app")]
    public bool NotificheInApp { get; set; } = true;

    [Display(Name = "Mostra toast in tempo reale")]
    public bool MostraToast { get; set; } = true;

    [Display(Name = "Suoni di notifica")]
    public bool SuoniNotifiche { get; set; } = false;

    [Display(Name = "Digest email giornaliero")]
    public bool DigestEmail { get; set; } = true;

    [Display(Name = "Lingua")]
    public string Lingua { get; set; } = "it";

    [Display(Name = "Densità interfaccia")]
    public string Densita { get; set; } = "comoda";
}
