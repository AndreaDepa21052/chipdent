# Chipdent — Architettura Tecnica

## Stack

| Layer | Tecnologia | Note |
|-------|-----------|------|
| Web framework | ASP.NET Core 8 | Minimal API + Razor MVC |
| UI server-side | Razor MVC (.cshtml) | Rendering principale |
| UI reattività | Alpine.js + HTMX | Interattività leggera, no SPA |
| Realtime | SignalR + Azure SignalR Service | Chat, notifiche, aggiornamenti live |
| Stile | Tailwind CSS | Compilato con CLI |
| Database | MongoDB | Azure Cosmos DB API MongoDB in produzione |
| File storage | Azure Blob Storage | Documenti, allegati |
| Autenticazione | Azure AD B2C | OIDC, MFA, multi-tenant |
| Hosting | Azure App Service / AKS | |
| Secrets | Azure Key Vault | |
| Observability | Azure Monitor + App Insights | OpenTelemetry |
| CI/CD | Azure DevOps | |

---

## Multi-tenancy

### Strategia: database-per-tenant su MongoDB

Ogni tenant (catena dentale cliente) ha il proprio database MongoDB isolato:

```
MongoDB Atlas / Cosmos DB
├── chipdent_confident       ← database tenant Confident
│   ├── cliniche
│   ├── dipendenti
│   ├── dottori
│   ├── turni
│   ├── visite_mediche
│   ├── corsi_sicurezza
│   ├── documenti
│   └── messaggi
├── chipdent_dentalgroup     ← database tenant DentalGroup
│   └── ...
└── chipdent_system          ← database di sistema (no tenant data)
    ├── tenants
    └── audit_log
```

### Flusso autenticazione → tenant

```
1. Utente login → Azure AD B2C
2. B2C emette JWT con claims: sub, email, role, tenant_id, clinica_id
3. TenantMiddleware legge tenant_id dal JWT
4. Popola ITenantContext nel DI container (scoped)
5. Tutti i Repository ricevono ITenantContext via DI
6. BaseRepository usa GetDatabase("chipdent_{tenantId}")
```

### ITenantContext

```csharp
public interface ITenantContext
{
    string TenantId { get; }
    string UserId { get; }
    string UserRole { get; }
    string? ClinicaId { get; }  // null per NETWORK_ADMIN
}
```

---

## Architettura a layer

```
┌─────────────────────────────────────────────┐
│           Razor Views (.cshtml)             │  ← Presentazione
│         Alpine.js + HTMX + SignalR JS       │
└──────────────────┬──────────────────────────┘
                   │
┌──────────────────▼──────────────────────────┐
│            MVC Controllers                  │  ← Routing + Auth
│      [Authorize(Roles = "...")] attribute   │
└──────────────────┬──────────────────────────┘
                   │
┌──────────────────▼──────────────────────────┐
│              Services                       │  ← Business Logic
│   TurniService, RlsService, NotificheService│
└──────────────────┬──────────────────────────┘
                   │
┌──────────────────▼──────────────────────────┐
│            Repositories                     │  ← Data Access
│   BaseRepository<T> → MongoDB collections   │
└──────────────────┬──────────────────────────┘
                   │
┌──────────────────▼──────────────────────────┐
│   MongoDB (chipdent_{tenantId})             │  ← Persistenza
│   Azure Blob Storage (documenti)            │
└─────────────────────────────────────────────┘
```

---

## SignalR — Architettura realtime

### Hub unico con gruppi

```csharp
public class ChipdentHub : Hub
{
    // Gruppi SignalR:
    // "tenant_{tenantId}"       → tutto il tenant (circolari globali)
    // "clinica_{clinicaId}"     → tutti gli utenti di una sede
    // "user_{userId}"           → messaggio diretto
    // "compliance"              → alert RLS a direttori + admin
    // "turni_{clinicaId}"       → aggiornamenti turni realtime per sede
}
```

### Scale-out in produzione

In produzione si usa **Azure SignalR Service** come backplane:

```csharp
// Program.cs
builder.Services.AddSignalR()
    .AddAzureSignalR(builder.Configuration["Azure:SignalR:ConnectionString"]);
```

Questo permette di scalare a più istanze App Service senza perdere connessioni.

### Eventi principali

| Evento client | Trigger | Destinatari |
|---------------|---------|-------------|
| `NuovoMessaggio` | Messaggio inviato | user_{destinatario} |
| `AlertCompliance` | Scadenza RLS imminente | clinica_{clinicaId} |
| `AggiornamentoTurni` | Turno modificato | turni_{clinicaId} |
| `NuovaCircolare` | Circolare pubblicata | tenant_{tenantId} |
| `FerieApprovata` | Approvazione ferie | user_{richiedente} |
| `NuovaSegnalazione` | Segnalazione aperta | clinica_{clinicaId} |

---

## MongoDB — Schema principali

### Dipendente
```json
{
  "_id": "ObjectId",
  "nome": "Carla",
  "cognome": "Esposito",
  "email": "c.esposito@confident.it",
  "ruolo": "IGIENISTA",
  "clinicaId": "ObjectId",
  "contratto": {
    "tipo": "FULL_TIME",
    "dataInizio": "2025-05-14",
    "dataFine": null,
    "ccnl": "COMMERCIO"
  },
  "ferieResidueGiorni": 25,
  "createdAt": "ISODate",
  "updatedAt": "ISODate",
  "createdBy": "userId",
  "isDeleted": false
}
```

### Turno
```json
{
  "_id": "ObjectId",
  "clinicaId": "ObjectId",
  "dipendenteId": "ObjectId",
  "data": "ISODate",
  "fasciaOraria": "MATTINA",
  "oraInizio": "08:00",
  "oraFine": "14:00",
  "stato": "CONFERMATO",
  "note": "",
  "createdAt": "ISODate",
  "updatedAt": "ISODate",
  "createdBy": "userId",
  "isDeleted": false
}
```

### VisitaMedica
```json
{
  "_id": "ObjectId",
  "dipendenteId": "ObjectId",
  "clinicaId": "ObjectId",
  "dataUltimaVisita": "ISODate",
  "dataScadenza": "ISODate",
  "idoneita": "IDONEO",
  "limitazioni": "",
  "refertoBlobId": "string",
  "statoAlert": "OK",
  "createdAt": "ISODate",
  "updatedAt": "ISODate",
  "createdBy": "userId",
  "isDeleted": false
}
```

### Documento
```json
{
  "_id": "ObjectId",
  "clinicaId": "ObjectId",
  "categoria": "CPI",
  "nome": "Certificato Prevenzione Incendi",
  "blobPath": "confident/documenti/clinica-mi/cpi-2025.pdf",
  "dimensioneBytes": 1843200,
  "dataScadenza": "ISODate",
  "statoAlert": "SCADE_PRESTO",
  "versione": 2,
  "versioni": [
    { "blobPath": "...", "caricatoDa": "userId", "dataCaricamento": "ISODate" }
  ],
  "createdAt": "ISODate",
  "updatedAt": "ISODate",
  "createdBy": "userId",
  "isDeleted": false
}
```

---

## Azure Blob Storage — Struttura

```
chipdent-{tenantId}/
├── documenti/
│   └── {clinicaId}/
│       ├── cpi-2025.pdf
│       ├── autorizzazione-sanitaria.pdf
│       └── contratto-affitto.pdf
├── dipendenti/
│   └── {dipendenteId}/
│       ├── contratto.pdf
│       └── attestato-antincendio.pdf
└── temp/                          ← file upload temporanei (TTL 24h)
```

Accesso ai file sempre via **SAS URL** temporanei (scadenza 1h), mai URL pubblici.

---

## Indici MongoDB consigliati

```csharp
// Per ogni collection — indice base su isDeleted + clinicaId
{ isDeleted: 1, clinicaId: 1 }

// Turni — ricerca per data e dipendente
{ clinicaId: 1, data: 1, isDeleted: 1 }
{ dipendenteId: 1, data: 1, isDeleted: 1 }

// Visite mediche — alert scadenze
{ clinicaId: 1, dataScadenza: 1, isDeleted: 1 }

// Messaggi — conversazione tra utenti
{ "partecipanti": 1, createdAt: -1 }

// Documenti — per clinica e categoria
{ clinicaId: 1, categoria: 1, isDeleted: 1 }
```

---

## Sicurezza

- **Autenticazione:** PIN Locale, Login Locale, Azure AD B2C, JWT Bearer token
- **Autorizzazione:** Policy-based + Role-based (`[Authorize(Roles = ...)]`)
- **Isolamento dati:** Database separato per tenant, mai query cross-tenant
- **File:** SAS URL con TTL breve, mai link pubblici
- **Secrets:** Azure Key Vault, mai in appsettings.json committato
- **HTTPS:** obbligatorio, redirect automatico
- **GDPR:** soft delete, audit log, nessun dato personale in log applicativi
