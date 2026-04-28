using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class WorkspaceImpostazioniViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    [Required, StringLength(120), Display(Name = "Nome workspace")]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(500), Display(Name = "Descrizione")]
    public string? Descrizione { get; set; }

    [StringLength(7), RegularExpression(@"^#[0-9a-fA-F]{6}$", ErrorMessage = "Formato colore non valido (es. #c47830)")]
    [Display(Name = "Colore primario (hex)")]
    public string PrimaryColor { get; set; } = "#c47830";

    [StringLength(150), Display(Name = "Ragione sociale")]
    public string? RagioneSociale { get; set; }

    [StringLength(20), Display(Name = "P.IVA")]
    public string? PartitaIva { get; set; }

    [StringLength(20), Display(Name = "Codice fiscale")]
    public string? CodiceFiscale { get; set; }

    [StringLength(250), Display(Name = "Indirizzo legale")]
    public string? IndirizzoLegale { get; set; }

    [Display(Name = "Fuso orario IANA")]
    public string FusoOrario { get; set; } = "Europe/Rome";

    public string? LogoPath { get; set; }
    public Microsoft.AspNetCore.Http.IFormFile? LogoFile { get; set; }
    public bool RimuoviLogo { get; set; }

    public DateTime? DataAttivazione { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class NuovoWorkspaceViewModel
{
    [Required, StringLength(60)]
    [RegularExpression(@"^[a-z0-9][a-z0-9-]{1,58}[a-z0-9]$",
        ErrorMessage = "Slug: solo minuscole, numeri e trattini, 3-60 caratteri, niente trattini iniziali/finali.")]
    public string Slug { get; set; } = string.Empty;

    [Required, StringLength(120), Display(Name = "Nome workspace")]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(7), RegularExpression(@"^#[0-9a-fA-F]{6}$")]
    [Display(Name = "Colore primario")]
    public string PrimaryColor { get; set; } = "#c47830";

    [StringLength(500), Display(Name = "Descrizione (opzionale)")]
    public string? Descrizione { get; set; }
}

public class WorkspaceSwitchEntry
{
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? LogoPath { get; set; }
    public bool IsCurrent { get; set; }
}
