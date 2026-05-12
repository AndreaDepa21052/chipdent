using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Proposta di aggiornamento dell'anagrafica fornitore generata dal parser PDF
/// in fase di import fatture. Non scrive mai direttamente sull'anagrafica:
/// il Backoffice rivede ogni proposta e la approva o la scarta.
/// Una proposta = una coppia (Fornitore, Campo) per batch. Più valori diversi
/// per lo stesso campo generano proposte distinte (varianti).
/// </summary>
public class PropostaAnagraficaFornitore : TenantEntity
{
    /// <summary>Id del batch di import che ha generato la proposta.</summary>
    public string BatchId { get; set; } = string.Empty;

    /// <summary>Numero pagina PDF dove il dato è stato letto (per audit/debug).</summary>
    public int? PaginaPdf { get; set; }

    /// <summary>
    /// Id del fornitore esistente in anagrafica (se trovato per match nome / P.IVA).
    /// Null = fornitore nuovo (sarà creato all'approvazione, se non già censito altrimenti).
    /// </summary>
    public string? FornitoreId { get; set; }

    /// <summary>Ragione sociale come letta dal PDF.</summary>
    public string RagioneSocialePdf { get; set; } = string.Empty;

    /// <summary>Campo dell'anagrafica oggetto della proposta (snake-case del nome property).</summary>
    public string Campo { get; set; } = string.Empty;

    /// <summary>Valore attualmente in anagrafica (snapshot al momento della proposta).</summary>
    public string? ValoreAttuale { get; set; }

    /// <summary>Valore proposto dal PDF.</summary>
    public string? ValoreProposto { get; set; }

    /// <summary>Numero/i fattura PDF da cui è stato estratto il valore (riferimento).</summary>
    public string? FatturaRiferimento { get; set; }

    public StatoPropostaAnagrafica Stato { get; set; } = StatoPropostaAnagrafica.InAttesa;

    public DateTime? DecisaIl { get; set; }
    public string? DecisaDaUserId { get; set; }
    public string? DecisaDaNome { get; set; }
    public string? NotaDecisione { get; set; }
}

public enum StatoPropostaAnagrafica
{
    InAttesa,
    Approvata,
    Rifiutata
}
