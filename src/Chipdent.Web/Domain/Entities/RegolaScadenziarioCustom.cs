using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Regola "custom" dello scadenziario, scritta dall'utente in linguaggio
/// conversazionale (italiano). Le regole vengono salvate per tenant e
/// mostrate in:
///   - tab «Regole» della tesoreria (per consultazione e manutenzione);
///   - banner della pagina «Genera scadenziario» (promemoria all'operatore).
///
/// <para>
/// <b>Stato dell'arte sull'auto-applicazione</b>: il parsing in linguaggio
/// naturale → modifiche al motore <c>ScadenziarioGenerator</c> richiede un
/// LLM (es. Claude API). Al momento le regole custom sono memorizzate e
/// presentate all'operatore come promemoria; l'applicazione automatica
/// arriverà in un secondo step (vedi <c>Stato</c>).
/// </para>
/// </summary>
public class RegolaScadenziarioCustom : TenantEntity
{
    /// <summary>Titolo breve della regola (es. "Compenso medici: 90 gg invece di 60").</summary>
    public string Titolo { get; set; } = string.Empty;

    /// <summary>Testo libero, conversazionale. Es: "Se il fornitore è Mario
    /// Rossi e la clinica è MI7, il bonifico va al 15 del mese successivo".</summary>
    public string Testo { get; set; } = string.Empty;

    /// <summary>True = regola attiva (mostrata e applicata). False = archiviata
    /// (resta visibile in elenco ma marcata come disattivata, non applicata).</summary>
    public bool Attiva { get; set; } = true;

    /// <summary>Stato di interpretazione della regola da parte del motore.</summary>
    public StatoRegola Stato { get; set; } = StatoRegola.DaInterpretare;

    /// <summary>Note interne / spiegazione dell'interpretazione, dopo che
    /// il motore (LLM) avrà tradotto il testo conversazionale in azioni.</summary>
    public string? NotaInterpretazione { get; set; }

    public string CreataDaUserId { get; set; } = string.Empty;
    public string CreataDaNome { get; set; } = string.Empty;
}

public enum StatoRegola
{
    /// <summary>La regola è stata appena inserita e non è ancora stata
    /// interpretata: viene SOLO mostrata come promemoria all'operatore.</summary>
    DaInterpretare = 0,
    /// <summary>La regola è stata interpretata dal motore (LLM) e tradotta
    /// in azioni applicabili dal generatore di scadenziario.</summary>
    Interpretata = 10,
    /// <summary>L'interpretazione è andata in errore (testo ambiguo,
    /// contraddittorio con regole esistenti, …). Resta come promemoria.</summary>
    Errore = 20
}
