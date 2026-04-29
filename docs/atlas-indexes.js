// Indici MongoDB consigliati per Chipdent.
// Esegui questo script una volta su Atlas (Browse Collections → Shell)
// oppure con `mongosh "<URI>" --file docs/atlas-indexes.js`.
//
// Idempotente: createIndex non rilancia se l'indice esiste già con la stessa specifica.

const dbName = 'chipdent';
db = db.getSiblingDB(dbName);

print(`Creazione indici su ${dbName}…`);

// Tenant lookup
db.tenants.createIndex({ slug: 1 }, { unique: true });

// Multi-tenancy: filtri primari su tenantId in quasi ogni query
db.users.createIndex({ tenantId: 1, email: 1 });
db.users.createIndex({ email: 1 }); // per login multi-workspace
db.cliniche.createIndex({ tenantId: 1 });
db.dottori.createIndex({ tenantId: 1, attivo: 1 });
db.dipendenti.createIndex({ tenantId: 1, clinicaId: 1, stato: 1 });
db.inviti.createIndex({ token: 1 }, { unique: true });

// Turni: range query per settimana + filtro persona
db.turni.createIndex({ tenantId: 1, data: 1, personaId: 1 });
db.turni.createIndex({ tenantId: 1, clinicaId: 1, data: 1 });

// Timbrature: filtro per dipendente + range temporale
db.timbrature.createIndex({ tenantId: 1, dipendenteId: 1, timestamp: -1 });
db.timbrature.createIndex({ tenantId: 1, timestamp: -1 });

// Ferie e workflow
db.richiesteFerie.createIndex({ tenantId: 1, dipendenteId: 1, dataInizio: 1 });
db.richiesteFerie.createIndex({ tenantId: 1, stato: 1 });
db.richiesteCambioTurno.createIndex({ tenantId: 1, stato: 1, createdAt: -1 });
db.sostituzioni.createIndex({ tenantId: 1, stato: 1, data: 1 });
db.correzioniTimbrature.createIndex({ tenantId: 1, stato: 1, createdAt: -1 });
db.approvazioniTimesheet.createIndex({ tenantId: 1, dipendenteId: 1, periodo: 1 }, { unique: true });

// Comunicazioni / chat / notifiche
db.comunicazioni.createIndex({ tenantId: 1, createdAt: -1 });
db.messaggi.createIndex({ tenantId: 1, destinatarioUserId: 1, createdAt: -1 });
db.messaggi.createIndex({ tenantId: 1, clinicaGroupId: 1, createdAt: -1 });
db.audit.createIndex({ tenantId: 1, createdAt: -1 });

// RLS / Compliance
db.visiteMediche.createIndex({ tenantId: 1, dipendenteId: 1, scadenzaIdoneita: 1 });
db.corsi.createIndex({ tenantId: 1, destinatarioId: 1, scadenza: 1 });
db.dvrs.createIndex({ tenantId: 1, clinicaId: 1, prossimaRevisione: 1 });
db.documentiClinica.createIndex({ tenantId: 1, clinicaId: 1, dataScadenza: 1 });
db.dpi.createIndex({ tenantId: 1, clinicaId: 1, attivo: 1 });
db.consegneDpi.createIndex({ tenantId: 1, dipendenteId: 1, scadenzaSostituzione: 1 });

// Contratti
db.contratti.createIndex({ tenantId: 1, dipendenteId: 1, dataInizio: -1 });

// Segnalazioni operative
db.segnalazioni.createIndex({ tenantId: 1, stato: 1, createdAt: -1 });

// Whistleblowing — codice tracciamento è chiave di accesso pubblica
db.whistleblowing.createIndex({ codiceTracciamento: 1 }, { unique: true });
db.whistleblowing.createIndex({ tenantId: 1, stato: 1 });

// Videoassistenza — coda Backoffice e dettaglio per richiedente
db.richiesteAssistenza.createIndex({ tenantId: 1, stato: 1, priorita: -1, createdAt: -1 });
db.richiesteAssistenza.createIndex({ tenantId: 1, richiedenteUserId: 1, createdAt: -1 });

// Configurazione
db.soglieCopertura.createIndex({ tenantId: 1, clinicaId: 1, ruolo: 1 });
db.categorieDocumentoObbligatorie.createIndex({ tenantId: 1, clinicaId: 1, tipo: 1 });
db.workflowConfigs.createIndex({ tenantId: 1, key: 1 }, { unique: true });
db.turniTemplate.createIndex({ tenantId: 1, attivo: 1 });

print('✓ Indici creati. Lista finale:');
db.getCollectionNames().forEach(function (name) {
    const idxs = db.getCollection(name).getIndexes().map(i => i.name);
    print(`  ${name}: ${idxs.join(', ')}`);
});
