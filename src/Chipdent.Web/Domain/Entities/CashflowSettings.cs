using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Singleton per tenant. Configurazione del modulo Cashflow:
/// saldo cassa attuale aggiornato manualmente dall'Owner, soglia di rischio liquidità
/// (sotto cui scatta l'alert sulla dashboard).
/// </summary>
public class CashflowSettings : TenantEntity
{
    /// <summary>Saldo cassa corrente (€), aggiornato manualmente.</summary>
    public decimal SaldoCassa { get; set; }

    /// <summary>Quando il saldo è stato aggiornato l'ultima volta (per warning di "stale").</summary>
    public DateTime? SaldoAggiornatoIl { get; set; }
    public string? SaldoAggiornatoDaUserId { get; set; }

    /// <summary>Soglia minima sotto cui scatta l'alert "rischio liquidità" sulla proiezione.</summary>
    public decimal SogliaRischio { get; set; } = 5_000m;

    /// <summary>Note libere (es. "saldo esclude conto vincolato")</summary>
    public string? Note { get; set; }
}

/// <summary>
/// Entrata di cassa attesa nel mese (input manuale dall'Owner).
/// Le entrate alimentano il saldo proiettato sulla dashboard cashflow.
/// </summary>
public class EntrataAttesa : TenantEntity
{
    /// <summary>Primo giorno del mese di riferimento (data di accredito stimata).</summary>
    public DateTime DataAttesa { get; set; }
    public decimal Importo { get; set; }
    public string Descrizione { get; set; } = string.Empty;

    /// <summary>Clinica che genera l'incasso (opzionale, per ripartizione).</summary>
    public string? ClinicaId { get; set; }

    public string? CreatoDaUserId { get; set; }
}
