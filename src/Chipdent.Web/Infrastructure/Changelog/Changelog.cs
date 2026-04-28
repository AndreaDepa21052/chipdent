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
