namespace Chipdent.Web.Infrastructure.Changelog;

/// <summary>
/// Registro statico delle novità funzionali rilasciate, dall'MVP in poi.
/// Una entry per ogni feature/modulo significativo. Ordine: dal più recente al più vecchio.
/// </summary>
public static class Changelog
{
    public static readonly IReadOnlyList<ChangelogEntry> Entries = new List<ChangelogEntry>
    {
        // ───────────────────────────────────────────────────────────────────
        // 🪟 v1.500.0 — Tornavento Multi-Workspace
        // ───────────────────────────────────────────────────────────────────
        new(
            Version: "v1.500.0",
            Codename: "Tornavento Multi-Workspace",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Foundation,
            Title: "Impostazioni workspace + creazione nuovo workspace",
            Description: "Le voci «Impostazioni workspace» e «Nuovo workspace» nel selettore in alto a sinistra ora sono operative. La pagina /workspace/impostazioni (Management) permette di personalizzare nome, logo (upload PNG/JPG/SVG), colore primario, anagrafica legale (ragione sociale, P.IVA, CF, indirizzo) e fuso orario. Solo l'Owner può creare un secondo workspace via /workspace/nuovo: lo slug è univoco, il nuovo Owner è clonato con la stessa email e password."),

        new(
            Version: "v1.500.0",
            Codename: "Tornavento Multi-Workspace",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Foundation,
            Title: "Switcher workspace dinamico + login multi-tenant",
            Description: "Il workspace switcher mostra il logo del tenant corrente e lista dinamicamente gli altri workspace dove la tua email è Owner attivo, con un click che fa logout e redirect al login con tenantSlug pre-selezionato. La pagina di login gestisce il caso «email presente in più tenant»: se ce n'è solo uno funziona come prima; se ce ne sono più di uno mostra un picker workspace inline."),

        // ───────────────────────────────────────────────────────────────────
        // ✨ v1.400.0 — Tornavento Intelligence
        // ───────────────────────────────────────────────────────────────────
        new(
            Version: "v1.400.0",
            Codename: "Tornavento Intelligence",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Dashboard,
            Title: "✨ AI Insights — risk turnover, forecast, anomalie, smart staffing",
            Description: "Nuova pagina /ai-insights (Management) con previsioni e raccomandazioni generate dal motore Chipdent Intelligence: risk score turnover per dipendente con fattori esplicabili, forecast organico a 3 mesi con confidenza, anomaly detection sui ritardi (oltre 2σ dalla media personale), smart staffing che confronta la pianificazione futura con la baseline storica per sede×ruolo. Motore deterministico basato su euristiche multifattoriali — nessun dato esce dal tenant, nessun LLM coinvolto."),

        new(
            Version: "v1.400.0",
            Codename: "Tornavento Intelligence",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Turni,
            Title: "AI score sui candidati sostituzione",
            Description: "Quando il Direttore cerca un sostituto, ogni candidato riceve un punteggio AI 0-100 calcolato su carico ore della settimana, storico di accettazioni, affinità con l'orario tipico. I migliori candidati sono evidenziati in viola e suggeriti per primi."),

        // ───────────────────────────────────────────────────────────────────
        // 🌟 v1.300.0 — Tornavento Completo  (chiusura mappa funzionale)
        // ───────────────────────────────────────────────────────────────────
        new(
            Version: "v1.300.0",
            Codename: "Tornavento Completo",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Foundation,
            Title: "🌟 v1.300.0 «Tornavento Completo» — mappa funzionale al 100%",
            Description: "Chiusi gli ultimi tre moduli del livello Direttore di sede: sostituzioni urgenti, DPI con firma digitale, presenze con kiosk PIN. Tutta la mappa funzionale Chipdent (Management + Direttore + Staff) è ora live, salvo gli item esplicitamente etichettati come Roadmap futura (AI insights, integrazioni esterne, whistleblowing, multi-lingua, white-label)."),

        new(
            Version: "v1.300.0",
            Codename: "Tornavento Completo",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Turni,
            Title: "Sostituzioni urgenti",
            Description: "Workflow per assenze improvvise: il Direttore apre la richiesta indicando assente + data + motivo, il sistema mostra i candidati disponibili (stessa sede, stesso ruolo, esclude chi è in ferie o ha turni sovrapposti). Il sostituto designato accetta o rifiuta; alla conferma il sistema riassegna automaticamente il PersonaId del turno collegato. Pulsante 🚨 Escala al Management se nessuno copre."),

        new(
            Version: "v1.300.0",
            Codename: "Tornavento Completo",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Compliance,
            Title: "DPI con firma digitale",
            Description: "Catalogo DPI per sede (mascherine, guanti, camici, occhiali, ecc.) con intervallo sostituzione configurabile. Consegna nominale al dipendente con firma click che registra timestamp + IP + utente; calcolo automatico della scadenza. Counter dedicati per «da firmare», «in scadenza 30g», «scadute»."),

        new(
            Version: "v1.300.0",
            Codename: "Tornavento Completo",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Turni,
            Title: "Presenze & timbrature con kiosk PIN",
            Description: "Pagina kiosk a tutto schermo (`/presenze/kiosk`) per tablet di sede: il dipendente digita il PIN personale (4-6 cifre) e timbra entrata/uscita; il sistema decide automaticamente quale dei due in base all'ultima timbratura del giorno. Report mensile per il Direttore: ore lavorate vs pianificate, ritardi e uscite anticipate (tolleranza ±10 min), giorni effettivi vs giorni pianificati. Inserimento manuale + export CSV."),

        // ───────────────────────────────────────────────────────────────────
        // 👥 v1.200.0 — Tornavento+Staff
        // ───────────────────────────────────────────────────────────────────
        new(
            Version: "v1.200.0",
            Codename: "Tornavento+Staff",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Foundation,
            Title: "👥 v1.200.0 «Tornavento+Staff» — chiusura del livello Staff",
            Description: "Completati gli ultimi tre moduli Staff della mappa funzionale: cambio turno con workflow Staff↔collega↔Direttore, fascicolo personale «I miei documenti», segnalazioni operative con ticket di sede."),

        new(
            Version: "v1.200.0",
            Codename: "Tornavento+Staff",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Turni,
            Title: "Cambio turno con scambio fra colleghi",
            Description: "Lo Staff cede un turno a un collega specifico o in broadcast; quando un collega accetta, il Direttore approva e il sistema esegue lo swap del PersonaId del turno. Vista a tre sezioni (mie / in arrivo / da approvare) con pulsanti contestuali, dal calendario è disponibile un'azione ↔ direttamente sul turno proprio."),

        new(
            Version: "v1.200.0",
            Codename: "Tornavento+Staff",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Documenti,
            Title: "Fascicolo personale «I miei documenti»",
            Description: "Pagina personale dove ogni dipendente trova il proprio contratto attuale (e lo storico), gli attestati dei corsi obbligatori, le idoneità mediche con stato di scadenza. Download scoped al LinkedPersonId — un dipendente non può accedere agli allegati di un collega. VisitaMedica e Corso ricevono campi allegato dedicati."),

        new(
            Version: "v1.200.0",
            Codename: "Tornavento+Staff",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Comunicazioni,
            Title: "Segnalazioni operative con workflow di sede",
            Description: "Ticket di sede per guasti attrezzatura, problemi sicurezza, IT, approvvigionamento, igiene. Priorità Bassa→Urgente, allegato (foto/video/doc). Workflow: Aperta → InLavorazione (Direttore prende in carico) → Risolta (con nota di chiusura). Filtri stato e tipologia, contatori urgenti in evidenza."),

        // ───────────────────────────────────────────────────────────────────
        // 🏢 v1.100.0 — Tornavento+Management
        // ───────────────────────────────────────────────────────────────────
        new(
            Version: "v1.100.0",
            Codename: "Tornavento+Management",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Foundation,
            Title: "🏢 v1.100.0 «Tornavento+Management» — chiusura del livello Management",
            Description: "Tutti i moduli Management della mappa funzionale sono ora implementati: configurazione di rete, contratti & scadenze, headcount & organico con analytics, report & analytics esportabili, formazione & ECM."),

        new(
            Version: "v1.100.0",
            Codename: "Tornavento+Management",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Dashboard,
            Title: "Report & analytics con export CSV e stampa PDF",
            Description: "Vista Report mensile per il Management: presenze e turni per sede, costo personale aggregato (con totale catena), indice compliance per sede (visite + corsi + documenti), turnover 12 mesi. Export CSV per ogni tabella e CSS print-friendly per stampa/PDF dal browser."),

        new(
            Version: "v1.100.0",
            Codename: "Tornavento+Management",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Anagrafiche,
            Title: "Headcount & organico con grafici",
            Description: "Modulo Headcount con KPI organico, distribuzione per sede e ruolo, trend assunzioni/cessazioni 12 mesi, effettivi vs target per sede (con barra di progresso), stima costo mensile (da contratti reali + euristiche CCNL). Grafici Chart.js."),

        new(
            Version: "v1.100.0",
            Codename: "Tornavento+Management",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Anagrafiche,
            Title: "Contratti dipendenti con alert scadenze",
            Description: "Nuovo modulo Contratti: tipo, livello CCNL, retribuzione, date inizio/fine, allegato firmato (PDF). Alert automatici a 90/30/7 giorni dalla scadenza con tab di filtro dedicate. Export CSV per consulente del lavoro."),

        new(
            Version: "v1.100.0",
            Codename: "Tornavento+Management",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Compliance,
            Title: "Formazione & ECM dottori",
            Description: "Tracciamento crediti ECM dei medici (default 150 nel triennio AGENAS). Stato calcolato automaticamente come «in regola / in ritardo / critico» rispetto al passo previsto. Aggiornamento crediti inline in tabella, export CSV."),

        new(
            Version: "v1.100.0",
            Codename: "Tornavento+Management",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Foundation,
            Title: "Configurazione rete centralizzata",
            Description: "Pagina Configurazione (solo Management) per definire: workflow approvazioni (escalation ferie lunghe, conferma circolari), categorie documento obbligatorie per sede (con stato caricato/mancante), soglie copertura minima per sede×ruolo×giorno. Le soglie alimentano i warning del calendario turni."),

        // ───────────────────────────────────────────────────────────────────
        // 🌳 v1.000.0 — Tornavento  (MVP chiuso)
        // ───────────────────────────────────────────────────────────────────
        new(
            Version: "v1.000.0",
            Codename: "Tornavento",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Foundation,
            Title: "🌳 v1.000.0 «Tornavento» — chiusura dell'MVP",
            Description: "Prima release stabile di Chipdent. Tutti i moduli MVP della mappa funzionale sono implementati: anagrafiche multi-sede, turni con drag&drop e conflict detection, ferie con saldo automatico, RLS (visite/corsi/DVR), documentazione con upload, chat realtime, circolari con conferma lettura, dashboard differenziate per ruolo, notifiche live + digest email."),

        new(
            Version: "v1.000.0",
            Codename: "Tornavento",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Dashboard,
            Title: "Dashboard differenziate per ruolo",
            Description: "Ogni ruolo vede una home dedicata: Management → KPI cross-sede e azioni di governo; Direttore → turni di oggi, ferie da approvare, scadenze documenti delle proprie sedi; Staff → app personale con saldo ferie, prossimi turni e circolari non lette."),

        new(
            Version: "v1.000.0",
            Codename: "Tornavento",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Documenti,
            Title: "Upload allegati documenti (locale)",
            Description: "Caricamento di PDF, immagini e office sotto wwwroot/uploads/{tenant}/, con validazione mime/size. Documenti di clinica linkati a un file scaricabile. Astrazione IFileStorage pronta per migrare in futuro su Azure Blob senza toccare i controller."),

        new(
            Version: "v1.000.0",
            Codename: "Tornavento",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Notifiche,
            Title: "Notifiche browser + digest email giornaliero",
            Description: "Le notifiche realtime SignalR ora si appoggiano alla Web Notifications API per i toast di sistema fuori dalla scheda. Background service «DigestEmailService» invia ogni mattina alle 7 un riepilogo a chi ha attivo il digest (scadenze docs, RLS, richieste in attesa)."),

        // ───────────────────────────────────────────────────────────────────
        // Pre-Tornavento (work-in-progress verso v1.000.0)
        // ───────────────────────────────────────────────────────────────────
        new(
            Version: "v0.900.0",
            Codename: "pre-Tornavento",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Turni,
            Title: "Turni: drag & drop, template, conflict detection, copia settimana",
            Description: "Editor turni potenziato con drag & drop fra celle, template orari riutilizzabili (con colori), copia settimana intera, rilevamento conflitti (sovrapposizioni e turni durante ferie approvate), avviso copertura minima per giorno."),

        new(
            Version: "v0.900.0",
            Codename: "pre-Tornavento",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Comunicazioni,
            Title: "Conferma di lettura sulle circolari",
            Description: "Le circolari ora possono richiedere una conferma di lettura esplicita dei destinatari. Tracciamento percentuale di lettura su totale destinatari snapshottato all'invio."),

        new(
            Version: "v0.900.0",
            Codename: "pre-Tornavento",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Comunicazioni,
            Title: "Chat realtime 1:1 e di sede",
            Description: "Modulo chat con messaggi diretti utente↔utente e gruppi per sede. SignalR (ChatHub) per consegna live. Inbox unica con badge messaggi non letti."),

        new(
            Version: "v0.900.0",
            Codename: "pre-Tornavento",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Ferie,
            Title: "Workflow ferie completo",
            Description: "Lo Staff invia richieste di ferie/permesso/malattia, il Direttore approva o rifiuta. Saldo giorni residui aggiornato automaticamente all'approvazione e ripristinato in caso di annullamento. Calcolo giorni lavorativi (lun-ven) automatico."),

        new(
            Version: "v0.900.0",
            Codename: "pre-Tornavento",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Foundation,
            Title: "Refactor RBAC + mappa funzionale",
            Description: "Ruoli allineati alla mappa funzionale: Owner/Management/Direttore/Backoffice/Staff. Direttore con scope multi-clinica. Footer con link alla mappa funzionale."),

        new(
            Version: "v0.900.0",
            Codename: "pre-Tornavento",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Anagrafiche,
            Title: "Mappa geografica delle cliniche",
            Description: "Nuova vista «mappa» nell'elenco cliniche con marker interattivi (Leaflet/OpenStreetMap), pin colorati per stato sede."),

        new(
            Version: "v0.900.0",
            Codename: "pre-Tornavento",
            Date: new DateTime(2026, 4, 28),
            Category: ChangelogCategory.Foundation,
            Title: "Pagina What's New",
            Description: "Cronologia pubblica delle funzionalità rilasciate, raggiungibile dal menu utente.")
    };
}

public record ChangelogEntry(
    string Version,
    string Codename,
    DateTime Date,
    ChangelogCategory Category,
    string Title,
    string Description);

public enum ChangelogCategory
{
    Foundation,
    Anagrafiche,
    Turni,
    Ferie,
    Compliance,
    Documenti,
    Comunicazioni,
    Dashboard,
    Notifiche
}
