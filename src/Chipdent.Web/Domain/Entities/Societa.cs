using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Società (persona giuridica) del gruppo. Una società può avere N cliniche.
/// I dati anagrafici provengono dalla visura camerale; l'IBAN è quello su cui
/// arriveranno i pagamenti delle scadenze associate alla società.
/// </summary>
public class Societa : TenantEntity
{
    /// <summary>Nome breve usato per le UI (es. "Ident Cormano").</summary>
    public string Nome { get; set; } = string.Empty;

    /// <summary>Denominazione completa da visura (es. "IDENT CORMANO SRL").</summary>
    public string RagioneSociale { get; set; } = string.Empty;

    public string? CodiceFiscale { get; set; }
    public string? PartitaIva { get; set; }

    /// <summary>Numero REA completo di provincia (es. "VA - 386325").</summary>
    public string? NumeroRea { get; set; }

    public string? FormaGiuridica { get; set; }
    public DateTime? DataCostituzione { get; set; }
    public decimal? CapitaleSociale { get; set; }
    public string? CodiceAteco { get; set; }

    // Sede legale
    public string? IndirizzoSedeLegale { get; set; }
    public string? ComuneSedeLegale { get; set; }
    public string? ProvinciaSedeLegale { get; set; }
    public string? CapSedeLegale { get; set; }

    // Sede operativa / unità locale (dove la società opera effettivamente,
    // tipicamente l'indirizzo della clinica). Distinta dalla sede legale,
    // che per tutto il gruppo Confident è centralizzata a Gallarate.
    public string? IndirizzoSedeOperativa { get; set; }
    public string? ComuneSedeOperativa { get; set; }
    public string? ProvinciaSedeOperativa { get; set; }
    public string? CapSedeOperativa { get; set; }

    // Contatti
    public string? Pec { get; set; }
    public string? Email { get; set; }
    public string? Telefono { get; set; }

    /// <summary>
    /// IBAN su cui verranno disposti i pagamenti delle scadenze associate
    /// a questa società. Usato dal motore di tesoreria per generare le distinte SEPA.
    /// </summary>
    public string? Iban { get; set; }
    public string? Bic { get; set; }

    /// <summary>
    /// True per la società "holding" del gruppo (es. CCH). Marker informativo:
    /// non cambia il comportamento di tesoreria, serve come hint nella UI.
    /// </summary>
    public bool IsHolding { get; set; }

    public string? Note { get; set; }
}
