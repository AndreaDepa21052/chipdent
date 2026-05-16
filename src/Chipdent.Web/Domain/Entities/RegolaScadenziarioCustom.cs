using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Regola "custom" dello scadenziario, configurata dall'utente. Ogni regola
/// è composta da:
///   - un blocco <b>condizioni</b> (campi opzionali in AND) che selezionano
///     a quali scadenze applicarla;
///   - un blocco <b>azione</b> tipizzata che descrive cosa fare quando le
///     condizioni matchano (cambiare giorno scadenza, anticipare, cambiare
///     metodo, aggiungere nota, segnalare alert);
///   - un campo <see cref="Testo"/> in linguaggio naturale usato come
///     <b>documentazione leggibile</b> (mostrato in UI), non come input
///     interpretato.
///
/// <para>
/// Le regole vengono caricate da <see cref="Tesoreria.ScadenziarioGenerator"/>
/// e applicate dopo le regole built-in. Vengono ordinate per
/// <see cref="Priorita"/> crescente (numero più basso → applicata prima);
/// regole successive possono sovrascrivere l'effetto delle precedenti.
/// Le regole inattive vengono ignorate dal motore ma restano visibili in UI.
/// </para>
/// </summary>
public class RegolaScadenziarioCustom : TenantEntity
{
    /// <summary>Titolo breve mostrato in elenco e negli alert generati dal motore.</summary>
    public string Titolo { get; set; } = string.Empty;

    /// <summary>Descrizione in italiano della regola (documentazione per
    /// l'operatore). Non viene interpretata: la logica esecutiva sta nei
    /// campi strutturati.</summary>
    public string Testo { get; set; } = string.Empty;

    /// <summary>True = la regola viene applicata dal motore. False = archiviata.</summary>
    public bool Attiva { get; set; } = true;

    /// <summary>Ordine di applicazione (più basso prima). Default 100.</summary>
    public int Priorita { get; set; } = 100;

    // ── Condizioni (AND) ────────────────────────────────────────
    /// <summary>Match se la ragione sociale del fornitore contiene questo testo (case-insensitive).
    /// Vuoto = non filtra per fornitore.</summary>
    public string? FornitoreNomeContiene { get; set; }

    /// <summary>Match per id fornitore specifico (priorità sul nome).</summary>
    public string? FornitoreId { get; set; }

    /// <summary>Match per clinica destinataria (LOC). Vuoto = tutte le sedi.</summary>
    public string? ClinicaId { get; set; }

    /// <summary>Match per categoria di spesa. Null = qualunque.</summary>
    public CategoriaSpesa? Categoria { get; set; }

    /// <summary>Importo minimo (incluso) della scadenza per applicare la regola.</summary>
    public decimal? ImportoMinimo { get; set; }

    /// <summary>Importo massimo (incluso) della scadenza per applicare la regola.</summary>
    public decimal? ImportoMassimo { get; set; }

    // ── Azione ──────────────────────────────────────────────────
    public TipoAzioneRegola Azione { get; set; } = TipoAzioneRegola.SoloPromemoria;

    /// <summary>Parametro principale dell'azione. Semantica per tipo:
    /// <list type="bullet">
    /// <item><c>ImpostaGiornoMese</c>: numero giorno 1–31</item>
    /// <item><c>AnticipaGiorni</c> / <c>PosticipaGiorni</c>: numero di giorni</item>
    /// <item><c>ImpostaMetodoPagamento</c>: "Bonifico", "Rid", "Riba", "CartaCredito", "Bonifico", "Assegno", "Contanti", "Compensazione", "Altro"</item>
    /// <item><c>AggiungiNota</c>: testo da concatenare a <c>Scadenza.Note</c></item>
    /// <item><c>SegnalaAlert</c>: messaggio dell'alert che verrà mostrato in preview</item>
    /// <item><c>SoloPromemoria</c>: ignorato</item>
    /// </list>
    /// </summary>
    public string? Parametro1 { get; set; }

    /// <summary>Parametro accessorio (riservato a future azioni — al momento non usato).</summary>
    public string? Parametro2 { get; set; }

    // ── Audit ───────────────────────────────────────────────────
    public string CreataDaUserId { get; set; } = string.Empty;
    public string CreataDaNome { get; set; } = string.Empty;
}

/// <summary>
/// Tipo di azione applicata dal motore quando le condizioni di una
/// <see cref="RegolaScadenziarioCustom"/> matchano una scadenza.
/// </summary>
public enum TipoAzioneRegola
{
    /// <summary>Nessuna modifica: la regola viene solo mostrata come
    /// promemoria all'operatore (negli alert/preview).</summary>
    SoloPromemoria = 0,

    /// <summary>Sposta la data scadenza al giorno N (1-31) del mese corrente
    /// (o successivo se il giorno è già passato rispetto alla scadenza
    /// calcolata).</summary>
    ImpostaGiornoMese = 10,

    /// <summary>Anticipa la data scadenza di N giorni.</summary>
    AnticipaGiorni = 20,

    /// <summary>Posticipa la data scadenza di N giorni.</summary>
    PosticipaGiorni = 21,

    /// <summary>Forza il metodo di pagamento (Bonifico, Rid, Riba, …).</summary>
    ImpostaMetodoPagamento = 30,

    /// <summary>Aggiunge il parametro come nota libera alla scadenza
    /// (concatenata con " · " alle note esistenti).</summary>
    AggiungiNota = 40,

    /// <summary>Genera un alert custom nel preview di generazione (Warn).</summary>
    SegnalaAlert = 50
}
