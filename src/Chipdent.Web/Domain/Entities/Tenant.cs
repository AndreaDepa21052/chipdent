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

    /// <summary>Raggio (metri) entro cui la timbratura web è considerata "in area"; 0 disabilita il check.</summary>
    public int RaggioGeofencingMetri { get; set; } = 200;

    /// <summary>True per richiedere il selfie alla timbratura web (audit). Default false.</summary>
    public bool SelfieTimbraturaRichiesto { get; set; } = false;

    // Provenienza
    public DateTime? DataAttivazione { get; set; }
    public string? CreatoDaUserId { get; set; }

    // Dati bancari ordinante (Tesoreria → distinte SEPA)
    /// <summary>IBAN del conto pagatore usato come "ordinante" nelle distinte SEPA.</summary>
    public string? PagatoreIban { get; set; }
    /// <summary>BIC/SWIFT del conto pagatore. Opzionale: la maggior parte delle banche lo deriva dall'IBAN.</summary>
    public string? PagatoreBic { get; set; }
    /// <summary>Ragione sociale stampata nella distinta come "Initiating Party". Se null, fallback a <see cref="RagioneSociale"/> o <see cref="DisplayName"/>.</summary>
    public string? PagatoreRagioneSociale { get; set; }
    /// <summary>Codice fiscale dell'ordinante (alcune banche lo richiedono come OrgId nella distinta).</summary>
    public string? PagatoreCodiceFiscale { get; set; }

    /// <summary>Chiavi delle migrazioni one-shot già eseguite su questo tenant. Usato dai
    /// seeder di tipo "wipe & reseed" per evitare di rigirare l'operazione su startup
    /// successivi (vedi <see cref="Chipdent.Web.Infrastructure.Mongo.WipeAnagraficaSeeder"/>).</summary>
    public List<string> MigrazioniApplicate { get; set; } = new();
}
