using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Distinta di bonifici SEPA generata in formato pain.001.001.03 e scaricata
/// dall'Owner. Conserva la lista delle scadenze incluse, il totale e l'XML
/// originale per la ri-scaricabilità e l'audit.
/// </summary>
public class DistintaPagamento : TenantEntity
{
    /// <summary>Identificativo umano della distinta (Message Id), es. "DIS-20250503-0001".</summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Etichetta libera scelta dall'utente (default = MessageId).</summary>
    public string? Etichetta { get; set; }

    /// <summary>Data di esecuzione richiesta sul bonifico (Requested Execution Date).</summary>
    public DateTime DataEsecuzione { get; set; }

    /// <summary>Snapshot dell'IBAN ordinante al momento della generazione.</summary>
    public string PagatoreIban { get; set; } = string.Empty;
    public string? PagatoreBic { get; set; }
    public string PagatoreRagioneSociale { get; set; } = string.Empty;

    public int NumeroTransazioni { get; set; }
    public decimal Totale { get; set; }

    /// <summary>Id delle scadenze incluse nella distinta.</summary>
    public List<string> ScadenzaIds { get; set; } = new();

    /// <summary>XML completo (UTF-8) della distinta.</summary>
    public string Xml { get; set; } = string.Empty;

    public string? CreatoDaUserId { get; set; }
}
