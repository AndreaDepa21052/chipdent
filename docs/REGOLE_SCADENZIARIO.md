# Regole di generazione dello scadenziario

> Documento canonico delle regole applicate da
> `Chipdent.Web.Infrastructure.Tesoreria.ScadenziarioGenerator`.
> **Tenere allineato a ogni modifica del generatore.**

L'`ScadenziarioGenerator` riceve in input le righe di fatture importate
(`ImportFatturaRiga`) più l'anagrafica fornitori/cliniche/dottori del tenant
e produce in output:

- `Fatture` — `FatturaFornitore` registrate
- `Scadenze` — `ScadenzaPagamento` generate
- `FornitoriNuovi` — fornitori auto-creati durante la classificazione
- `Alerts` — segnalazioni con severità Info / Warn / Err
- `FatturePagamentoManuale` — fatture per cui non è stata generata scadenza
  perché il fornitore è marcato come «pagamenti manuali» (vedi sotto)

---

## Pipeline (per ogni riga importata)

1. Classificazione fornitore
2. Match anagrafica fornitore
3. Risoluzione clinica destinataria (LOC)
4. Costruzione fattura
5. **Short-circuit** se fornitore a pagamenti manuali → tabella alert dedicata
6. Calcolo scadenze per tipologia
7. Arricchimento note con «nota secondaria automatica» della clinica
8. Regole di confronto su storico (duplicati / scostamento importo)

---

## 1. Classificazione fornitore

Tipologie operative (`TipoFornitoreOp`), in ordine di priorità:

| Tipo | Match nel nome / causale |
|---|---|
| `Invisalign` | "invisalign", "align technology" |
| `Compass` | "compass" |
| `DeutscheBank` | "deutsche bank" |
| `CartaCredito` | "amex", "american express", "carta credito" |
| `Riba` | "riba" (anche in causale) |
| `ImportoFisso` | "cristal", "infinity" |
| `Locazione` | "locazione", "immobiliare", "affitto" |
| `DirezioneSanitaria` | "direzione sanitaria", "dir. sanitaria", "dir san" |
| `Laboratorio` | "laboratorio", "odontotecnic", "lab.", "lab ortodont" |
| `Medico` | match per cognome+nome con anagrafica dottori |
| `Generico` | tutto il resto |

Se più di una tipologia matcha, si applica la prima della lista e si emette
un alert **«Catalogazione ambigua»** che riporta tutte le candidate.

---

## 2. Match anagrafica fornitore

Priorità (più stabile → meno stabile):

1. **Partita IVA** estratta dal PDF (normalizzata: rimosso prefisso "IT",
   spazi, trattini, punti)
2. **Codice fiscale** estratto dal PDF (stessa normalizzazione)
3. **Ragione sociale** dell'import (key-case insensitive)

Se nessun match: auto-creazione di un nuovo fornitore con categoria default
e termini di pagamento derivati dalla tipologia. Alert **«Catalogazione
fornitore»** per ricordare di verificare IBAN e termini.

---

## 3. Risoluzione clinica destinataria (LOC)

Ordine di priorità:

1. Sezione "CCH" → clinica holding (`IsHolding = true`)
2. Match per `Clinica.NomeAbbreviato` (campo «Nome abbreviato (LOC)»
   sulla scheda clinica) contro `riga.Sezione` o suffisso del nome fornitore
3. Tabella statica di fallback (sigle storiche: DES, VAR, GIU, COR, COM,
   MI7, MI9, MI3, MI6, SGM, BUS, BOL, BRU, CMS, CCH)

Se nessun match: alert **«LOC mancante»** (Warn) — l'operatore assegnerà
la sede a mano.

Lo stesso campo `Clinica.NomeAbbreviato` è usato in scrittura nel
campo LOC mostrato in tutte le UI dello scadenziario
(vedi `TesoreriaController.SiglaSede`). Se assente, fallback alla tabella
statica e poi ai primi 3 caratteri del nome.

---

## 4. Costruzione fattura

Tutte le fatture importate hanno:

- `TipoEmissione = Elettronica` (l'import Zucchetti = SDI per definizione)
- `Stato = Approvata`
- `Origine = ImportExcel`
- Imponibile / IVA / Totale: per le fatture **CCH** (holding) IVA esplicita,
  per le **controllate** solo lordo (IVA = 0).

Nota di credito: identificata da `Totale < 0` o `TipoDocumento` che contiene
"NC" / "credito". L'importo viene forzato in segno negativo.

---

## 5. Regola «Pagamenti manuali»

> Trigger: `Fornitore.PagamentiManuali = true` sull'anagrafica fornitore.

Quando il flag è attivo:

- La fattura viene comunque **registrata** in `Output.Fatture`.
- **Nessuna scadenza** viene generata (skip totale del calcolo).
- La fattura entra nella lista `Output.FatturePagamentoManuale` (mostrata
  come tabella alert dedicata nell'anteprima «Genera scadenziario»).
- Viene emesso un alert **«Pagamento manuale»** (Warn).

La regola **vince su tutte le altre** (note di credito, carta di credito,
ritenute incluse): se il flag è acceso, lo scadenziario automatico è
disabilitato per quel fornitore. L'operatore calcola e dispone il
pagamento a mano.

Toggle UI: sezione termini di pagamento sulla scheda fornitore (riquadro
in ambra "Per questo fornitore non generare scadenze automaticamente").

---

## 6. Calcolo scadenze per tipologia

### Nota di credito Compass / Deutsche Bank
- Una scadenza con importo negativo, metodo RID, stato **Pagato** alla
  data fattura.
- Note: "NC Compass/DB · RID compensativa"

### Nota di credito generica
- Una scadenza con importo negativo, metodo Bonifico, scadenza al fine
  mese fattura.
- Alert **«Nota di credito»** per ricordare l'abbinamento alla fattura
  originale.

### Carta di credito
- Una scadenza con stato **Pagato** alla data fattura, metodo CC.

### Bonifico / RID / RIBA (generici)

| Tipologia | Termini | Base di calcolo |
|---|---|---|
| Invisalign | 150 gg | Data fattura |
| Medico / Direzione sanitaria / Laboratorio | 60 gg | Fine mese di **competenza** |
| Generico / Locazione / ImportoFisso / Riba / CC / Compass / DB | 30 gg | Fine mese fattura (default fornitore) |

**Mese di competenza** (medici / laboratori / direzione sanitaria):
priorità a `riga.MeseCompetenza`+`riga.AnnoCompetenza` (estratti dal PDF) →
parsing della causale ("Compenso ott 2025") → fallback alla data fattura
(con alert).

**Snap bonifico**: i bonifici sono programmati solo al **giorno 10** o
al **30/31** del mese. La scadenza calcolata viene riallineata al giorno
più vicino tra i 4 candidati (giorno 1 prec., 10 mese, fine mese, 10 mese
successivo). Per Invisalign si arrotonda per **difetto** (anticipa il
pagamento). Quando lo snap modifica la data viene emesso un alert
**«Snap bonifico»** (Info).

**RID / RIBA**: data scadenza presa dal PDF se disponibile
(`riga.DataScadenzaPdf`), altrimenti calcolata dai termini e segnalata
con alert **«RID/RIBA da verificare»** (Info).

**IBAN beneficiario**: priorità a IBAN del PDF della singola fattura →
anagrafica fornitore → cache della fattura precedente dello stesso
fornitore. Se manca IBAN e il metodo è Bonifico: alert **«IBAN mancante»**
(Err).

### Ritenuta d'acconto

Quando `riga.Ritenuta > 0`:
- Una scadenza principale di importo **netto** (totale − ritenuta).
- Una scadenza secondaria con la sola ritenuta, scadenza al **16 del mese
  successivo** al bonifico, agganciata via `ScadenzaPadreId` alla
  principale. Note: "Ritenuta d'acconto · F24 16 mese successivo".

---

## 7. Regola «Nota secondaria automatica»

> Trigger: `Clinica.AggiungiNotaSecondariaAutomaticamente = true` sulla
> clinica destinataria.

Se attivo e `Clinica.NotaSecondariaAutomatica` non vuota, il testo viene
**appeso** al campo `Scadenza.Note` di tutte le scadenze generate per
quella fattura (separatore " · "). Si applica anche alla rata ritenuta.

Toggle UI: sezione «Note» sulla scheda clinica (riquadro in ambra
"Aggiungi nota secondaria automaticamente").

---

## 8. Regole di confronto sullo storico

Costruito incrementalmente in ordine cronologico fattura per fornitore.

- **ImportoFisso / Locazione**: alert **«Scostamento importo»** se l'importo
  diverge dall'ultima fattura dello stesso fornitore (canone atteso fisso).
- **Altri fornitori**: alert **«Possibile doppione»** se lo stesso importo
  è già presente entro 60 gg.

I fornitori a pagamento manuale contribuiscono comunque alla storia
importi (per non perdere il segnale di duplicati / scostamento).

---

## Tipologia degli alert emessi

| Regola | Severità | Significato |
|---|---|---|
| Catalogazione ambigua | Warn | Più tipologie matchano lo stesso fornitore |
| Catalogazione fornitore | Info | Nuovo fornitore auto-creato — verificare IBAN/termini |
| LOC mancante | Warn | Impossibile determinare la sede destinataria |
| Parcella | Warn | Causale contiene "parcella" — possibile doppione |
| Nota di credito | Warn | Verificare abbinamento alla fattura originale |
| Pagamento manuale | Warn | Fornitore a pagamento manuale — nessuna scadenza generata |
| Mese di competenza assente | Warn | Scadenza medica calcolata sul mese fattura |
| Snap bonifico | Info | Data scadenza riallineata al 10 o 30/31 |
| RID da verificare | Info | Data scadenza RID non presente in PDF |
| RIBA da verificare | Info | Data scadenza RIBA non presente in PDF |
| IBAN mancante | Err | Bonifico senza IBAN — anagrafica da completare |
| Scostamento importo | Warn | Canone/fisso diverso dal precedente |
| Possibile doppione | Warn | Stesso importo entro 60 gg dello stesso fornitore |
