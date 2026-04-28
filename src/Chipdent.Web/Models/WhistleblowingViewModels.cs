using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class WhistleblowingPubblicaViewModel
{
    [Required, Display(Name = "Tipo violazione")]
    public TipoViolazioneWhistleblowing Tipo { get; set; } = TipoViolazioneWhistleblowing.CorruzioneEAbusi;

    [Required, StringLength(200), Display(Name = "Oggetto sintetico")]
    public string Oggetto { get; set; } = string.Empty;

    [Required, StringLength(5000), Display(Name = "Descrizione dei fatti")]
    public string Descrizione { get; set; } = string.Empty;

    [StringLength(2000), Display(Name = "Soggetti coinvolti / contesto (opzionale)")]
    public string? FattiESoggetti { get; set; }

    [Display(Name = "Sede coinvolta (opzionale)")]
    public string? ClinicaId { get; set; }

    [Display(Name = "Voglio restare anonimo")]
    public bool Anonima { get; set; } = true;

    [StringLength(120), Display(Name = "Nome (solo se NON anonimo)")]
    public string? FirmatarioNome { get; set; }

    [EmailAddress, Display(Name = "Email (solo se NON anonimo)")]
    public string? FirmatarioEmail { get; set; }

    [StringLength(60), Display(Name = "Ruolo (solo se NON anonimo)")]
    public string? FirmatarioRuolo { get; set; }

    [Required, MinLength(6), MaxLength(40)]
    [Display(Name = "Codice di accesso (lo userai per seguire il caso)")]
    public string CodiceAccesso { get; set; } = string.Empty;

    public Microsoft.AspNetCore.Http.IFormFile? Allegato { get; set; }

    public IReadOnlyList<Domain.Entities.Clinica> Cliniche { get; set; } = Array.Empty<Domain.Entities.Clinica>();
    public string? TenantSlug { get; set; }
}

public class WhistleblowingConfermaViewModel
{
    public string CodiceTracciamento { get; set; } = string.Empty;
    public string TenantNome { get; set; } = string.Empty;
}

public class WhistleblowingSeguiViewModel
{
    [Required] public string CodiceTracciamento { get; set; } = string.Empty;
    [Required] public string CodiceAccesso { get; set; } = string.Empty;
    public string? Errore { get; set; }
}

public class WhistleblowingDettaglioPubblicoViewModel
{
    public SegnalazioneWhistleblowing Segnalazione { get; set; } = new();
    public string ClinicaNome { get; set; } = string.Empty;
    public string CodiceAccessoVerificato { get; set; } = string.Empty;
}

public class WhistleblowingAdminIndexViewModel
{
    public IReadOnlyList<WhistleblowingAdminRow> Tutte { get; set; } = Array.Empty<WhistleblowingAdminRow>();
    public StatoWhistleblowing? Filter { get; set; }
    public int Aperte { get; set; }
    public int InEsame { get; set; }
}

public record WhistleblowingAdminRow(SegnalazioneWhistleblowing Segnalazione, string ClinicaNome);

public class WhistleblowingAdminDettaglioViewModel
{
    public SegnalazioneWhistleblowing Segnalazione { get; set; } = new();
    public string ClinicaNome { get; set; } = string.Empty;
}
