using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class OnboardingStateViewModel
{
    public int Step { get; set; }
    public string TenantNome { get; set; } = string.Empty;
    public bool HasLogo { get; set; }
    public bool HasClinica { get; set; }
    public bool HasInvito { get; set; }
    public bool HasTemplate { get; set; }
    public int Completati => (HasLogo ? 1 : 0) + (HasClinica ? 1 : 0) + (HasInvito ? 1 : 0) + (HasTemplate ? 1 : 0);

    public OnboardingBrandingForm Branding { get; set; } = new();
    public OnboardingClinicaForm Clinica { get; set; } = new();
    public OnboardingInvitoForm Invito { get; set; } = new();
    public OnboardingTemplateForm Template { get; set; } = new();
}

public class OnboardingBrandingForm
{
    [Required, StringLength(120)] public string DisplayName { get; set; } = string.Empty;
    [StringLength(500)] public string? Descrizione { get; set; }
    [RegularExpression(@"^#[0-9a-fA-F]{6}$")] public string PrimaryColor { get; set; } = "#c47830";
    public Microsoft.AspNetCore.Http.IFormFile? Logo { get; set; }
}

public class OnboardingClinicaForm
{
    [Required, StringLength(120)] public string Nome { get; set; } = string.Empty;
    [Required, StringLength(80)] public string Citta { get; set; } = string.Empty;
    [Required, StringLength(200)] public string Indirizzo { get; set; } = string.Empty;
    [Range(1, 50)] public int NumeroRiuniti { get; set; } = 4;
    [Range(0, 200)] public int? OrganicoTarget { get; set; } = 6;
}

public class OnboardingInvitoForm
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required, StringLength(120)] public string FullName { get; set; } = string.Empty;
    public UserRole Ruolo { get; set; } = UserRole.Direttore;
}

public class OnboardingTemplateForm
{
    [Required, StringLength(60)] public string Nome { get; set; } = "Mattina";
    public TimeSpan OraInizio { get; set; } = new(9, 0, 0);
    public TimeSpan OraFine { get; set; } = new(13, 0, 0);
    public string ColoreHex { get; set; } = "#c47830";
}
