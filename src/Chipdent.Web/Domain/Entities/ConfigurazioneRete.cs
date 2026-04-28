using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Soglia di copertura minima per una sede e un ruolo dipendente in un giorno tipo.
/// Il dashboard turni segnala quando una giornata scende sotto questa soglia.
/// </summary>
public class SogliaCopertura : TenantEntity
{
    public string ClinicaId { get; set; } = string.Empty;
    public RuoloDipendente Ruolo { get; set; }
    public int MinimoPerGiorno { get; set; } = 1;
    public DayOfWeek? GiornoSettimana { get; set; } // null = tutti i giorni feriali
    public bool Attiva { get; set; } = true;
}

/// <summary>
/// Categoria di documento dichiarata «obbligatoria» per una sede.
/// Il modulo Documentazione segnala in rosso le categorie senza documenti caricati.
/// </summary>
public class CategoriaDocumentoObbligatoria : TenantEntity
{
    public string ClinicaId { get; set; } = string.Empty;
    public TipoDocumento Tipo { get; set; }
    public bool Attiva { get; set; } = true;
    public string? Note { get; set; }
}

/// <summary>
/// Configurazione globale dei workflow di approvazione del tenant.
/// Usata da modulo Ferie/Comunicazioni per decidere chi approva cosa.
/// </summary>
public class WorkflowConfiguration : TenantEntity
{
    /// <summary>True se richieste ferie sopra <see cref="GiorniMaxAutoApprove"/> richiedono escalation al Management.</summary>
    public bool EscaladaFerieLunghe { get; set; } = true;
    public int GiorniMaxAutoApprove { get; set; } = 5;

    /// <summary>True se le circolari obbligatorie devono includere la conferma di lettura.</summary>
    public bool CircolariConfermaObbligatoria { get; set; } = true;

    /// <summary>True se le richieste sostituzioni urgenti notificano via email il Direttore.</summary>
    public bool NotificaSostituzioniViaEmail { get; set; } = false;

    /// <summary>Singleton: c'è al massimo un documento per tenant.</summary>
    public const string SingletonKey = "default";
    public string Key { get; set; } = SingletonKey;
}
