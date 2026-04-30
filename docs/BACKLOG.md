# Chipdent — Product Backlog

> 1 Story Point (SP) ≈ 1 giorno di lavoro per uno sviluppatore senior .NET
> Sprint = 2 settimane

**Legenda stato:**
- ✅ Completata
- 🟡 In corso / parzialmente implementata
- ⬜ Da fare

---

## MVP — Sprint 1 (settimane 1–2)
**Obiettivo:** infrastruttura, auth, anagrafiche base
**Avanzamento sprint: 96 % (44 / 46 SP)**

| # | Stato | User Story | SP | Note |
|---|-------|-----------|-----|------|
| 1 | ✅ | Setup soluzione .NET 8, struttura cartelle, Tailwind, pipeline Azure DevOps | 3 | |
| 2 | ✅ | Configurazione MongoDB + BaseRepository + soft delete | 3 | |
| 3 | 🟡 | Integrazione Azure AD B2C + JWT middleware | 5 | Auth a cookie già attiva, AD B2C ancora da collegare. Claims tenant_id, role, clinica_ids già propagati |
| 4 | ✅ | TenantMiddleware + ITenantContext (Scoped DI) | 2 | |
| 5 | ✅ | RBAC: attributi Authorize Roles + policy custom | 2 | |
| 6 | ✅ | _Layout.cshtml + sidebar Chipdent con navigazione per ruolo | 3 | |
| 7 | ✅ | Anagrafica Cliniche — CRUD completo (Razor + Controller + Repository) | 5 | |
| 8 | ✅ | Anagrafica Dottori — CRUD + campo iscrizione albo + alert scadenza | 5 | |
| 9 | ✅ | Anagrafica Dipendenti — CRUD + stato + contratto base | 5 | |
| 10 | ✅ | Dashboard NETWORK_ADMIN — KPI aggregati (conti da MongoDB) | 5 | |
| 11 | ✅ | Dashboard CLINIC_DIRECTOR — turni oggi + alert sede | 5 | |
| 12 | ✅ | Centro notifiche in-app (badge + lista notifiche) | 3 | |
| **Totale** | | | **46 SP** | ~3 settimane 1 dev |

---

## MVP — Sprint 2 (settimane 3–4)
**Obiettivo:** turni, ferie, RLS base
**Avanzamento sprint: 100 % (48 / 48 SP)**

| # | Stato | User Story | SP | Note |
|---|-------|-----------|-----|------|
| 13 | ✅ | Pianificazione turni — griglia visiva Razor (settimana x persone) | 8 | HTMX per aggiornamenti parziali |
| 14 | ✅ | Pianificazione turni — salva/modifica turno singolo | 5 | |
| 15 | ✅ | Pianificazione turni — template riutilizzabili | 3 | |
| 16 | ✅ | Pianificazione turni — conflict detection (doppi turni, ferie) | 3 | |
| 17 | ✅ | Richiesta ferie da STAFF — form + salvataggio MongoDB | 3 | |
| 18 | ✅ | Approvazione ferie CLINIC_DIRECTOR — approva/rifiuta con nota | 3 | |
| 19 | ✅ | SignalR Hub setup + connessione client JS | 3 | NotificationsHub + ChatHub mappati |
| 20 | ✅ | SignalR: notifica realtime ferie approvata/rifiutata | 2 | |
| 21 | ✅ | Visite mediche — CRUD + data scadenza + upload referto Blob | 5 | |
| 22 | ✅ | Corsi sicurezza — CRUD + stato per dipendente + attestato | 5 | |
| 23 | ✅ | DVR — upload PDF + versioning + data revisione | 5 | |
| 24 | ✅ | Background Service: scan scadenze RLS ogni notte + alert | 3 | DigestEmailService + TimbraturaWatchdog |
| **Totale** | | | **48 SP** | ~3 settimane 1 dev |

---

## Sprint 3 (settimane 5–6)
**Obiettivo:** documenti, comunicazioni, SignalR completo
**Avanzamento sprint: 100 % (39 / 39 SP)**

| # | Stato | User Story | SP | Note |
|---|-------|-----------|-----|------|
| 25 | ✅ | Archivio documenti sede — CRUD + categorie + checklist | 5 | |
| 26 | ✅ | Upload documenti Azure Blob + SAS URL temporanei | 5 | LocalFileStorage attivo, switch ad Azure Blob da effettuare in deploy |
| 27 | ✅ | Alert documenti mancanti o in scadenza | 3 | Scadenziario |
| 28 | ✅ | Audit log documenti (chi carica/scarica/modifica) | 3 | AuditService |
| 29 | ✅ | Messaggistica interna — modello dati MongoDB + UI Razor | 5 | |
| 30 | ✅ | SignalR: invio/ricezione messaggi realtime | 5 | |
| 31 | ✅ | Circolari — pubblicazione con target (tenant/sede/ruolo) | 3 | |
| 32 | ✅ | Circolari — conferma lettura + tracciamento percentuale | 3 | |
| 33 | ✅ | SignalR: notifica nuova circolare a tutti i target | 2 | |
| 34 | ✅ | App personale STAFF — turni + saldo ferie + richieste | 5 | MieiDocumenti, MieTimbrature |
| **Totale** | | | **39 SP** | ~2.5 settimane 1 dev |

---

## Sprint 4 (settimane 7–8)
**Obiettivo:** completamento, UAT, go-live
**Avanzamento sprint: 71 % (30 / 42 SP)**

| # | Stato | User Story | SP | Note |
|---|-------|-----------|-----|------|
| 35 | ✅ | Cambio e scambio turni (richiesta + collega accetta + direttore approva) | 5 | |
| 36 | ✅ | Gestione sostituzioni urgenti (assenza improvvisa + lista disponibili) | 5 | |
| 37 | ✅ | Segnalazioni operative (ticket da STAFF, gestito da CLINIC_DIRECTOR) | 5 | |
| 38 | ✅ | Contratti dipendenti — upload + scadenza + alert | 3 | |
| 39 | ✅ | DPI — registro consegna + firma click + scadenza | 3 | |
| 40 | ✅ | Documenti personali dipendente — fascicolo digitale | 3 | |
| 41 | ✅ | Report export presenze, RLS, organico (CSV/PDF) | 5 | |
| 42 | 🟡 | Provisioning nuovo tenant automatizzato | 3 | OnboardingController presente, seed AD B2C ancora manuale |
| 43 | 🟡 | Testing UAT con team Confident + bug fix | 5 | In corso |
| 44 | ⬜ | Migrazione dati da Excel a MongoDB (import script) | 5 | Da pianificare con cliente |
| **Totale** | | | **42 SP** | ~3 settimane 1 dev |

---

## Sprint 5–6 (mese 3)
**Obiettivo:** maturità prodotto
**Avanzamento sprint: 81 % (38 / 47 SP)**

| # | Stato | User Story | SP | Note |
|---|-------|-----------|-----|------|
| 45 | ✅ | Presenze e timbrature QR code (check-in/out) | 8 | Presenze + Timbrature + correzioni |
| 46 | 🟡 | Firma digitale documenti (click to sign con timestamp) | 8 | Firma click attiva su DPI; estensione a DocumentoClinica TBD |
| 47 | ✅ | Formazione ECM dottori — crediti + scadenza annuale | 5 | |
| 48 | ✅ | Headcount e analytics organico con grafici | 8 | HeadcountController |
| 49 | ✅ | PWA mobile (manifest + service worker su Razor) | 8 | manifest.webmanifest + sw.js attivi |
| 50 | ✅ | Configurazione rete centralizzata (soglie, template, ruoli) | 5 | ConfigurazioneController + WorkflowConfigs |
| 51 | ⬜ | Mappa geografica sedi | 5 | Leaflet.js — non avviata |
| **Totale** | | | **47 SP** | ~3 settimane 1 dev |

---

## Roadmap futura (anno 2)

| Stato | Feature | Note |
|-------|---------|------|
| ⬜ | Integrazione API XDENT | Sync anagrafiche dottori bidirezionale |
| ✅ | AI insights HR predittivo | AiInsightsEngine + AssenzePredictor attivi |
| ✅ | Ottimizzazione turni AI | TurniOptimizer attivo |
| ⬜ | Integrazione sistema paghe | Export per Zucchetti, TeamSystem |
| ✅ | Whistleblowing anonimo | D.Lgs. 24/2023 — WhistleblowingController |
| ⬜ | Multi-lingua EN/ES | Espansione internazionale |
| ⬜ | White-label altri verticali | Fisioterapia, oculistica, poliambulatori |

---

## Riepilogo

| Fase | Sprint | SP | Completati | % | Durata (1 dev senior) |
|------|--------|----|------------|---|----------------------|
| MVP core | 1–2 | 94 | 92 | 98 % | ~6 settimane |
| Completamento | 3–4 | 81 | 69 | 85 % | ~5 settimane |
| Maturità | 5–6 | 47 | 38 | 81 % | ~3 settimane |
| **Go-live completo** | | **222 SP** | **199 SP** | **90 %** | **~3.5 mesi** |

Con 2 sviluppatori senior .NET: **go-live in ~7 settimane**.

> **Nota:** dalla v1.4 il backlog è anche manutenibile via portale alla sezione **Backlog di prodotto** (Operatività → Backlog).
> Lì lo Staff può proporre nuove user story in formato agile («Come Staff vorrei che…»), tutta la rete vota le richieste e l'Owner decide cosa promuovere a sprint.
