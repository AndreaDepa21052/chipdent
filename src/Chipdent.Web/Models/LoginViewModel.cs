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

    /// <summary>Slug del tenant (opzionale): usato quando la stessa email esiste in più workspace.</summary>
    public string? TenantSlug { get; set; }

    /// <summary>Lista di workspace disponibili per l'email digitata, popolata server-side se >1.</summary>
    public IReadOnlyList<(string Slug, string DisplayName)> Workspaces { get; set; } = Array.Empty<(string, string)>();
}
