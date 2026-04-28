# Chipdent — Mappa Funzionale

## Ruoli utente

| Ruolo | Codice | Accesso |
|-------|--------|---------|
| Management catena | `NETWORK_ADMIN` | Tutte le sedi del tenant |
| Direttore sede | `CLINIC_DIRECTOR` | Solo la propria clinica |
| Staff operativo | `STAFF` | Solo dati personali + sede assegnata |
| Super Admin | `SUPER_ADMIN` | Solo team Chipdent (no dati tenant) |

---

## Modulo 1 — Dashboard & Controllo

### 1.1 Dashboard esecutiva (NETWORK_ADMIN)
Vista d'insieme dell'intera catena in tempo reale.

**Feature:**
- KPI aggregati: sedi attive, dottori, dipendenti totali, alert aperti
- Stato per sede: semaforo verde/giallo/rosso con drill-down
- Alert critici prioritizzati (RLS scadute, documenti mancanti, copertura turni)
- Attività recenti in timeline
- Digest notifiche configurabile (email giornaliera / push)

**Priorità:** MVP

---

### 1.2 Dashboard di sede (CLINIC_DIRECTOR)
Vista operativa quotidiana per la propria clinica.

**Feature:**
- Turni di oggi con stato per persona
- Ferie e assenze della settimana
- Alert RLS della sede (semafori per dipendente)
- Documenti mancanti o in scadenza
- Segnalazioni aperte

**Priorità:** MVP

---

### 1.3 App personale (STAFF)
Interfaccia semplificata per il dipendente.

**Feature:**
- Prossimo turno in evidenza
- Turni della settimana corrente
- Stato richieste ferie
- Saldo ferie residue
- Notifiche non lette

**Priorità:** MVP

---

## Modulo 2 — Anagrafiche

### 2.1 Cliniche
**Feature:**
- CRUD completo con dati anagrafici (ragione sociale, P.IVA, indirizzo, contatti)
- Numero riuniti e sale
- Referente operativo
- Stato attiva / inattiva / in apertura
- Storico modifiche

**Accesso:** NETWORK_ADMIN (CRUD), CLINIC_DIRECTOR (sola lettura propria sede)
**Priorità:** MVP

---

### 2.2 Dottori
**Feature:**
- CRUD con dati professionali
- Numero iscrizione Albo con data scadenza e alert
- Specializzazioni e abilitazioni
- Tipo rapporto: dipendente / libero professionista
- P.IVA e regime fiscale se autonomo
- Sede/i di appartenenza
- ECM: crediti formativi obbligatori con scadenza annuale

**Accesso:** NETWORK_ADMIN (CRUD), CLINIC_DIRECTOR (lettura propria sede)
**Priorità:** MVP

---

### 2.3 Dipendenti
**Feature:**
- CRUD con dati personali (nome, CF, documento identità)
- Tipo contratto (TD/TI/stage/part-time), CCNL, livello
- Sede di appartenenza
- Data assunzione e storico sedi
- Saldo ferie e permessi residui
- Note HR riservate (solo NETWORK_ADMIN)
- Stato: attivo / in malattia / in ferie / onboarding / cessato

**Accesso:** NETWORK_ADMIN (CRUD), CLINIC_DIRECTOR (lettura + note sede), STAFF (solo propri dati)
**Priorità:** MVP

---

### 2.4 Contratti e scadenze
**Feature:**
- Caricamento contratto firmato (PDF su Azure Blob)
- Data inizio, fine, tipo
- Alert rinnovo: 90 / 30 / 7 giorni prima della scadenza
- Storico contratti per dipendente
- Export lista contratti in scadenza per consulente del lavoro

**Accesso:** NETWORK_ADMIN (CRUD), CLINIC_DIRECTOR (lettura)
**Priorità:** Sprint 2

---

## Modulo 3 — Turni & HR Operativo

### 3.1 Pianificazione turni
Editor visivo per il direttore di sede.

**Feature:**
- Griglia settimanale: righe = persone, colonne = giorni
- Fasce orarie: Mattina (08–14), Pomeriggio (14–20), Personalizzata
- Drag & drop per spostare turni
- Template turni riutilizzabili (salva settimana tipo)
- Copia settimana precedente con un click
- Conflict detection: doppio turno stesso giorno, ferie sovrapposte
- Indicatore copertura minima per ruolo (configurabile da NETWORK_ADMIN)
- Vista mese per pianificazione a lungo termine
- Aggiornamento realtime via SignalR per tutti gli utenti della sede

**Accesso:** CLINIC_DIRECTOR (CRUD propria sede), NETWORK_ADMIN (lettura tutte le sedi)
**Priorità:** MVP

---

### 3.2 Gestione ferie e permessi
Workflow completo dalla richiesta all'approvazione.

**Feature:**
- STAFF: seleziona date, tipo (ferie/permesso/ROL), note opzionali
- Sistema verifica: saldo sufficiente, sovrapposizioni con altri
- CLINIC_DIRECTOR: riceve notifica SignalR + email
- CLINIC_DIRECTOR: approva o rifiuta con motivazione
- STAFF: riceve notifica risposta (SignalR + push)
- Calendario ferie sede: vista mensile chi è assente quando
- Storico richieste per dipendente
- Integrazione automatica con pianificazione turni (segna come assenza)

**Accesso:** STAFF (richiesta), CLINIC_DIRECTOR (approvazione), NETWORK_ADMIN (vista aggregata)
**Priorità:** MVP

---

### 3.3 Cambio e scambio turni
**Feature:**
- STAFF: richiede di cedere un turno a un collega specifico o broadcast alla sede
- Collega: riceve notifica e accetta/rifiuta
- CLINIC_DIRECTOR: approvazione finale obbligatoria
- Tutto tracciato con timestamp e motivazioni
- Notifiche SignalR a ogni step

**Accesso:** STAFF (richiesta/risposta), CLINIC_DIRECTOR (approvazione)
**Priorità:** Sprint 2

---

### 3.4 Gestione sostituzioni urgenti
**Feature:**
- CLINIC_DIRECTOR segnala assenza improvvisa
- Sistema mostra lista disponibili per quel turno (non in ferie, non già assegnati)
- Notifica push ai disponibili
- Primo che accetta → turno assegnato, altri notificati
- Tracciamento straordinari generati
- Escalation a NETWORK_ADMIN se non risolta entro X ore

**Accesso:** CLINIC_DIRECTOR
**Priorità:** Sprint 2

---

### 3.5 Presenze e timbrature
**Feature:**
- Check-in via QR code univoco per sede (generato dal sistema)
- Check-out con QR o manuale
- Ore lavorate vs pianificate per dipendente
- Segnalazione ritardi e uscite anticipate
- Report presenze mensile per dipendente (export per consulente paghe)
- Integrazione futura con sistemi paghe (Zucchetti)

**Accesso:** STAFF (check-in/out), CLINIC_DIRECTOR (gestione + report), NETWORK_ADMIN (aggregato)
**Priorità:** Sprint 3

---

## Modulo 4 — RLS & Compliance

### 4.1 Visite mediche
Sorveglianza sanitaria obbligatoria per tutti i dipendenti.

**Feature:**
- Data ultima visita e data scadenza per ogni dipendente
- Alert automatico: 90 / 30 / 7 / 0 giorni prima della scadenza
- Alert via SignalR a CLINIC_DIRECTOR + NETWORK_ADMIN
- Pianificazione visita futura con data e medico competente
- Upload referto medico (PDF su Blob)
- Idoneità: idoneo / idoneo con limitazioni / non idoneo
- Campo limitazioni e prescrizioni
- Vista aggregata per sede: semaforo per ogni dipendente
- Job schedulato: scan giornaliero scadenze → pubblica alert

**Accesso:** CLINIC_DIRECTOR (CRUD sede), NETWORK_ADMIN (vista aggregata), STAFF (propri dati)
**Priorità:** MVP

---

### 4.2 Corsi sicurezza
Tracciamento corsi obbligatori per ruolo e sede.

**Feature:**
- Tipi corso configurabili: antincendio, primo soccorso, sicurezza generale, HACCP
- Corsi obbligatori per ruolo (configurabili da NETWORK_ADMIN)
- Stato completamento per dipendente: completato / in scadenza / scaduto / mai fatto
- Data completamento e data prossimo rinnovo (periodicità configurabile)
- Upload attestato completamento (PDF su Blob)
- Progress bar per corso: X/N dipendenti completati per sede
- Alert automatici come visite mediche

**Accesso:** CLINIC_DIRECTOR (CRUD sede), NETWORK_ADMIN (aggregato catena)
**Priorità:** MVP

---

### 4.3 DVR — Documento di Valutazione dei Rischi
**Feature:**
- Versione corrente con numero versione, data redazione, data revisione prevista
- RSPP assegnato (nome, società, contatti)
- Upload PDF documento (Azure Blob con versioning)
- Storico versioni precedenti consultabile
- Workflow revisione: bozza → revisione RSPP → approvazione direttore → archiviazione
- Alert scadenza revisione periodica
- Vista aggregata per catena: stato DVR di ogni sede

**Accesso:** CLINIC_DIRECTOR (CRUD sede), NETWORK_ADMIN (aggregato + approvazione)
**Priorità:** MVP

---

### 4.4 DPI — Dispositivi di Protezione Individuale
**Feature:**
- Registro consegna DPI per dipendente (guanti, maschere, occhiali, ecc.)
- Firma digitale ricezione (click to sign)
- Scadenza e pianificazione sostituzione periodica
- Inventario DPI disponibili in sede
- Report per ispezione

**Accesso:** CLINIC_DIRECTOR
**Priorità:** Sprint 2

---

## Modulo 5 — Documentazione di Sede

### 5.1 Archivio documenti
Repository centralizzato per ogni sede.

**Feature:**
- Categorie predefinite: Contratto affitto, CPI Vigili del Fuoco, Autorizzazione sanitaria, Cert. impianto elettrico, Cert. impianto termico, Registro rifiuti, Altro
- Checklist obbligatori per sede: documenti mancanti evidenziati in rosso
- Upload PDF con metadati: nome, categoria, data scadenza, note
- Versioning: ogni upload crea nuova versione, storico consultabile
- Alert scadenza: 90 / 30 / 7 giorni prima
- Download e anteprima inline (PDF viewer)
- SAS URL temporanei per accesso sicuro (1h TTL)
- Audit log: chi ha caricato/scaricato/modificato e quando

**Accesso:** CLINIC_DIRECTOR (CRUD sede), NETWORK_ADMIN (tutte le sedi + export)
**Priorità:** MVP

---

### 5.2 Documenti personali dipendente
Fascicolo digitale personale accessibile anche dal dipendente.

**Feature:**
- Contratto di lavoro firmato
- Attestati corsi completati
- Referto visita medica
- Documenti caricati da HR
- Download in autonomia dal dipendente (SAS URL)
- Nessuna modifica possibile da STAFF (solo lettura)

**Accesso:** STAFF (lettura propri), CLINIC_DIRECTOR (CRUD dipendenti sede), NETWORK_ADMIN (CRUD tutti)
**Priorità:** Sprint 2

---

## Modulo 6 — Comunicazioni

### 6.1 Messaggistica interna
Chat realtime via SignalR.

**Feature:**
- Messaggi diretti 1:1 tra utenti della stessa sede
- Gruppo sede: canale per tutti gli utenti di una clinica
- Allegati (immagini, PDF, max 10MB)
- Indicatore di lettura (read receipt)
- Notifiche push per messaggi non letti
- Storico messaggi persistente su MongoDB
- Ricerca nel testo dei messaggi

**Accesso:** Tutti gli utenti (limitato alla propria sede per STAFF e CLINIC_DIRECTOR)
**Priorità:** MVP

---

### 6.2 Circolari ufficiali
Comunicazioni formali con conferma lettura obbligatoria.

**Feature:**
- NETWORK_ADMIN o CLINIC_DIRECTOR pubblica circolare
- Target: tutto il tenant / sede specifica / ruolo specifico
- Allegati opzionali
- Conferma lettura obbligatoria (click "Ho letto e compreso")
- Tracciamento chi ha letto e quando
- Badge notifica per circolari non lette
- NETWORK_ADMIN vede % lettura per circolare
- Archivio circolari passate sempre consultabile
- Notifica SignalR + push al momento della pubblicazione

**Accesso:** Ricezione tutti, Invio NETWORK_ADMIN (a tutti) e CLINIC_DIRECTOR (solo propria sede)
**Priorità:** MVP

---

### 6.3 Segnalazioni operative
Canale formale per problemi operativi e di sicurezza.

**Feature:**
- STAFF segnala: guasto attrezzatura, problema sicurezza, manutenzione, altro
- Assegnazione automatica al CLINIC_DIRECTOR
- Stato: aperta / in lavorazione / risolta / chiusa
- Commenti e aggiornamenti sul ticket
- Escalation a NETWORK_ADMIN dopo X giorni senza risoluzione
- Notifiche SignalR ad ogni cambio stato
- Dashboard segnalazioni aperte per sede

**Accesso:** STAFF (apre), CLINIC_DIRECTOR (gestisce), NETWORK_ADMIN (vista aggregata)
**Priorità:** Sprint 2

---

## Matrice accessi

| Modulo / Feature | NETWORK_ADMIN | CLINIC_DIRECTOR | STAFF |
|------------------|:---:|:---:|:---:|
| Dashboard catena | ✅ | ❌ | ❌ |
| Dashboard sede | 👁 | ✅ | ❌ |
| App personale | ❌ | ❌ | ✅ |
| Anagrafica cliniche | ✅ | 👁 | ❌ |
| Anagrafica dottori | ✅ | ✅ | ❌ |
| Anagrafica dipendenti | ✅ | ✅ | 👁 |
| Contratti | ✅ | 👁 | ❌ |
| Pianificazione turni | 👁 | ✅ | ❌ |
| Visualizza turni propri | ❌ | ✅ | ✅ |
| Richiesta ferie | ❌ | ❌ | ✅ |
| Approvazione ferie | ✅ | ✅ | ❌ |
| Cambio turno | ❌ | ✅ | ✅ |
| Presenze & timbrature | 👁 | ✅ | ✅ |
| Visite mediche | ✅ | ✅ | 👁 |
| Corsi sicurezza | ✅ | ✅ | 👁 |
| DVR | ✅ | ✅ | ❌ |
| DPI | ❌ | ✅ | 👁 |
| Archivio documenti sede | ✅ | ✅ | ❌ |
| Documenti personali | ✅ | ✅ | 👁 |
| Messaggistica | ✅ | ✅ | ✅ |
| Invio circolari | ✅ | ✅* | ❌ |
| Ricezione circolari | ✅ | ✅ | ✅ |
| Segnalazioni | 👁 | ✅ | ✅ |
| Report & analytics | ✅ | 👁 | ❌ |

*CLINIC_DIRECTOR può inviare circolari solo alla propria sede

**Legenda:** ✅ Accesso completo · 👁 Sola lettura / parziale · ❌ Non accessibile
