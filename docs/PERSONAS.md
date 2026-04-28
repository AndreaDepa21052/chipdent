# Chipdent — Personas

## Persona 1 — Management Centrale
**Ruolo:** CEO / COO / HR Director  
**Codice:** `NETWORK_ADMIN`  
**Accesso:** Tutte le sedi del tenant

### Chi è
Marco, 48 anni, COO di Confident. Gestisce 8 sedi in 5 città. Passa metà del tempo in riunioni con i direttori di sede, l'altra metà a rincorrere informazioni sparse. Ha bisogno di sapere in 30 secondi se c'è qualcosa che non va da qualche parte — senza dover chiamare nessuno.

### Pain point
- Nessuna visibilità in tempo reale sullo stato delle sedi
- Scopre i problemi RLS solo quando sono già scaduti
- Deve chiedere a ogni direttore lo stesso report ogni mese
- Non sa quante persone lavorano nella catena e con che contratto

### Job to be done
> "Voglio aprire una pagina la mattina e sapere subito se c'è qualcosa che richiede la mia attenzione oggi."

### Feature chiave
- Dashboard esecutiva con KPI aggregati
- Alert prioritizzati via SignalR (RLS scadute, documenti mancanti, sedi sotto copertura)
- Report mensile export-ready per board
- Headcount e costo personale per sede

### Workflow tipico
1. Mattina: apre dashboard → vede 2 alert (DVR scaduto Torino, dipendente senza visita medica Roma)
2. Delega al direttore con nota → tracciato nel sistema
3. Fine mese: esporta report HR per CFO in 2 click

---

## Persona 2 — Direttore di Sede
**Ruolo:** Responsabile di clinica, coordinatore medico  
**Codice:** `CLINIC_DIRECTOR`  
**Accesso:** Solo la propria clinica (lettura/scrittura completa)

### Chi è
Lucia, 38 anni, direttrice della sede di Milano Centro. Ha 4 dottori e 9 tra assistenti, igieniste e receptionist. Pianifica i turni ogni domenica sera su Excel, riceve richieste di ferie via WhatsApp, scopre i problemi RLS quando arriva un'ispezione.

### Pain point
- Pianificare i turni richiede 2–3 ore ogni settimana
- Le richieste di ferie via WhatsApp si perdono
- Non sa mai quando scade la visita medica di qualcuno
- I documenti della sede sono in una cartella condivisa che nessuno aggiorna

### Job to be done
> "Voglio pianificare i turni in 20 minuti, sapere sempre chi è in regola con la sicurezza, e trovare i documenti subito quando arriva un'ispezione."

### Feature chiave
- Editor turni visivo con Razor + HTMX
- Workflow ferie con notifica SignalR
- Dashboard RLS con semafori per ogni dipendente
- Archivio documenti con checklist e alert scadenze

### Workflow tipico
1. Domenica: apre pianificazione turni, copia da template e aggiusta (15 min)
2. Lunedì: riceve notifica SignalR per richiesta ferie → vede calendario → approva
3. Alert: visita medica Dr. Ferrari scade tra 14 giorni → pianifica
4. Venerdì: carica CPI rinnovato → sistema aggiorna scadenza

---

## Persona 3 — Staff Operativo
**Ruolo:** Receptionist, assistente, igienista, coordinatore  
**Codice:** `STAFF`  
**Accesso:** Solo dati personali + informazioni della propria sede

### Chi è
Carla, 29 anni, igienista a Milano Centro. Ha turni fissi ma a volte li scambia con la collega. Vuole sapere quando lavora, richiedere ferie senza mandare WhatsApp e trovare i suoi documenti quando servono.

### Pain point
- Non sa mai il turno senza chiamare qualcuno
- Le richieste di ferie via WhatsApp restano senza risposta
- Non trova il suo contratto o l'attestato antincendio
- Scopre le circolari aziendali quando il direttore gliele dice a voce

### Job to be done
> "Voglio vedere i miei turni, richiedere ferie dal telefono e ricevere una risposta — senza dover chiamare nessuno."

### Feature chiave
- Vista turni personali (mobile-friendly, Razor responsive)
- Richiesta ferie con stato in tempo reale via SignalR
- Scambio turno con collega
- Sezione "i miei documenti"
- Ricezione notifiche circolari

### Workflow tipico
1. Mattina: apre app → vede turni della settimana
2. Richiede 3 giorni di ferie → direttore riceve notifica → Carla riceve conferma SignalR
3. Riceve circolare nuovo protocollo → legge → conferma lettura
4. Scambia turno con collega in 3 passaggi → direttore approva

---

## Matrice accessi rapida

| Feature | NETWORK_ADMIN | CLINIC_DIRECTOR | STAFF |
|---------|:---:|:---:|:---:|
| Dashboard catena | SI | NO | NO |
| Dashboard sede | LETTURA | SI | NO |
| Tutte le cliniche | SI | SOLO SUA | NO |
| Tutti i dipendenti | SI | SOLO SEDE | NO |
| Propri dati | SI | SI | SI |
| Pianificazione turni | LETTURA | SI | NO |
| Turni personali | NO | SI | LETTURA |
| Approvazione ferie | SI | SI | NO |
| Richiesta ferie | NO | NO | SI |
| Cambio turno | NO | APPROVA | RICHIEDE |
| RLS gestione | SI | SI | NO |
| RLS propri dati | NO | NO | LETTURA |
| Documenti sede | SI | SI | NO |
| Documenti personali | SI | SI | LETTURA |
| Invio circolari | SI | SOLO SUA SEDE | NO |
| Ricezione circolari | SI | SI | SI |
| Messaggistica | SI | SI | SI |
| Report export | SI | LETTURA | NO |
| Configurazione sistema | SI | NO | NO |
