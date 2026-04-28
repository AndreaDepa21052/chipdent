using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class Tenant : Entity
{
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Path relativo a wwwroot del logo (es. uploads/{tenantId}/branding/logo.png).</summary>
    public string? LogoPath { get; set; }

    /// <summary>Legacy: URL esterno o path al logo. Usare LogoPath per upload locale.</summary>
    public string? LogoUrl { get; set; }

    public string PrimaryColor { get; set; } = "#c47830";
    public bool IsActive { get; set; } = true;

    // Anagrafica legale
    public string? RagioneSociale { get; set; }
    public string? PartitaIva { get; set; }
    public string? CodiceFiscale { get; set; }
    public string? IndirizzoLegale { get; set; }
    public string? Descrizione { get; set; }

    // Configurazione operativa
    /// <summary>Fuso orario IANA (es. "Europe/Rome"). Default: Europe/Rome.</summary>
    public string FusoOrario { get; set; } = "Europe/Rome";

    // Provenienza
    public DateTime? DataAttivazione { get; set; }
    public string? CreatoDaUserId { get; set; }
}
