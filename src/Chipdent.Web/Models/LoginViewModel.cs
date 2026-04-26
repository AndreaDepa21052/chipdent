using System.ComponentModel.DataAnnotations;

namespace Chipdent.Web.Models;

public class LoginViewModel
{
    [Required(ErrorMessage = "Email obbligatoria")]
    [EmailAddress(ErrorMessage = "Email non valida")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password obbligatoria")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Ricordami")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
    public string? Error { get; set; }
}
