# Chipdent — Claude Code Context

> Leggi questo file all'inizio di ogni sessione. Contiene tutto il contesto necessario per lavorare sul progetto in modo coerente.

---

## 🐿 Cos'è Chipdent

Chipdent è un **SaaS gestionale multi-tenant** per catene di studi dentistici italiani.

**Posizionamento:** Chipdent NON è un gestionale clinico. Non gestisce pazienti, cartelle cliniche o fatturazione — queste funzioni restano su XDENT o AlfaDocs. Chipdent è il **layer operativo-manageriale** che si affianca al gestionale clinico.

**Chipdent gestisce:**
- Turni e presenze del personale
- HR e anagrafiche (dottori, dipendenti, cliniche)
- RLS e compliance sicurezza (DVR, visite mediche, corsi obbligatori)
- Archivio documenti per sede con checklist scadenze
- Comunicazioni interne (messaggistica realtime, circolari, segnalazioni)
- Dashboard direzionale per management e direttori di sede

**Primo cliente:** Confident (catena dentale italiana, 8 sedi)

---

## 🏗️ Stack tecnologico

### Backend
- **ASP.NET Core 8** — framework principale, Minimal API + MVC ibrido
- **Razor MVC** — rendering server-side per pagine principali
- **SignalR** — comunicazione realtime (chat, notifiche, aggiornamenti turni live)
- **C# 12** — linguaggio, con nullable reference types abilitati
- **MongoDB Driver ufficiale** — accesso al database (NO ORM, query native)

### Database
- **MongoDB** — database principale, schema flessibile per-tenant
- **Azure Blob Storage** — documenti, file allegati, immagini

### Frontend (integrato in Razor)
- **Razor Views (.cshtml)** — rendering server-side principale
- **Alpine.js** — reattività leggera lato client dove serve (no SPA)
- **HTMX** — aggiornamenti parziali della pagina senza full reload (opzionale)
- **Tailwind CSS** — stile utility-first compilato con CLI
- **SignalR JS client** — per chat e notifiche realtime

### Infrastruttura Azure
- **Azure App Service** o **AKS** — hosting applicazione
- **Azure Cosmos DB per MongoDB API** — database gestito (compatibile driver MongoDB)
- **Azure Blob Storage** — storage documenti
- **Azure AD B2C** — autenticazione multi-tenant, MFA, OIDC
- **Azure SignalR Service** — scale-out SignalR in produzione
- **Azure Key Vault** — secrets management
- **Azure Monitor + App Insights** — logging, tracing, alerting
- **Azure DevOps** — CI/CD pipeline

---

## 📁 Struttura progetto

```
Chipdent/
├── CLAUDE.md                          ← questo file (root del repo)
├── docs/
│   ├── PRODUCT.md
│   ├── FEATURES.md
│   ├── ARCHITECTURE.md
│   ├── BACKLOG.md
│   ├── BUSINESS.md
│   └── PERSONAS.md
├── src/
│   └── Chipdent.Web/                  ← progetto principale ASP.NET Core 8
│       ├── Controllers/               ← MVC Controllers per aree funzionali
│       │   ├── AnagraficheController.cs
│       │   ├── TurniController.cs
│       │   ├── RlsController.cs
│       │   ├── DocumentiController.cs
│       │   └── ComunicazioniController.cs
│       ├── Hubs/
│       │   └── ChatHub.cs             ← SignalR Hub per messaggistica realtime
│       ├── Models/                    ← Domain models (POCO)
│       │   ├── Clinica.cs
│       │   ├── Dipendente.cs
│       │   ├── Dottore.cs
│       │   ├── Turno.cs
│       │   ├── VisitaMedica.cs
│       │   └── Documento.cs
│       ├── ViewModels/                ← ViewModel per Razor Views
│       ├── Views/                     ← Razor .cshtml
│       │   ├── Shared/
│       │   │   ├── _Layout.cshtml
│       │   │   └── _Sidebar.cshtml
│       │   ├── Dashboard/
│       │   ├── Anagrafiche/
│       │   ├── Turni/
│       │   ├── Rls/
│       │   ├── Documenti/
│       │   └── Comunicazioni/
│       ├── Services/                  ← Business logic layer
│       │   ├── TurniService.cs
│       │   ├── RlsService.cs
│       │   ├── NotificheService.cs
│       │   └── DocumentiService.cs
│       ├── Repositories/              ← MongoDB data access layer
│       │   ├── BaseRepository.cs
│       │   ├── ClinicaRepository.cs
│       │   └── DipendenteRepository.cs
│       ├── Middleware/
│       │   ├── TenantMiddleware.cs    ← estrae e valida tenant dal token
│       │   └── RoleAuthMiddleware.cs
│       ├── wwwroot/                   ← assets statici
│       │   ├── css/
│       │   ├── js/
│       │   └── lib/
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       └── Program.cs
├── tests/
│   ├── Chipdent.UnitTests/
│   └── Chipdent.IntegrationTests/
├── .github/ (o azure-pipelines.yml)
├── Chipdent.sln
└── README.md
```

---

## 🔐 Multi-tenancy — REGOLA FONDAMENTALE

Il sistema usa **database-per-tenant** su MongoDB: ogni tenant ha il proprio database separato (o collection con prefisso tenant).

**Ogni operazione al database DEVE:**
1. Ricevere il `TenantId` dal token autenticato via `ITenantContext`
2. Usare il database/collection corretta del tenant
3. MAI accedere a dati di un altro tenant

```csharp
// ✅ CORRETTO — usa sempre il repository con tenant context
public class ClinicaRepository : BaseRepository<Clinica>
{
    public ClinicaRepository(ITenantContext tenant, IMongoClientFactory factory)
        : base(tenant, factory, "cliniche") { }
}

// BaseRepository ottiene automaticamente il DB del tenant corrente
protected IMongoCollection<T> GetCollection()
{
    var db = _factory.GetClient().GetDatabase($"chipdent_{_tenant.TenantId}");
    return db.GetCollection<T>(_collectionName);
}

// ❌ MAI hardcodare il database o usare un db condiviso senza tenantId
var db = client.GetDatabase("chipdent_shared"); // SBAGLIATO
```

**`ITenantContext`** viene popolato dal `TenantMiddleware` che legge il claim `tenant_id` dal JWT AD B2C.

---

## 👤 Ruoli utente (RBAC)

I ruoli sono allineati alla **mappa funzionale** (`docs/chipdent-features.html`).

```csharp
public enum UserRole
{
    Staff      = 0,   // operatività (receptionist, ASO, igienista)
    Backoffice = 10,  // anagrafiche/compliance cross-sede (ex HR)
    Direttore  = 20,  // responsabile di sede, scope ClinicaIds
    Management = 30,  // CEO/COO/HR Director/CFO — vista direzionale completa
    Owner      = 99   // ruolo tecnico (last-owner-guard del workspace)
}
```

**Policy disponibili** (`Infrastructure/Identity/Policies.cs`):

| Policy             | Include                                            |
|--------------------|----------------------------------------------------|
| `RequireOwner`     | Owner                                              |
| `RequireManagement`| Owner + Management                                 |
| `RequireDirettore` | Owner + Management + Direttore                     |
| `RequireBackoffice`| Owner + Management + Backoffice + Direttore        |

Direttore e Backoffice sono **fratelli peer**: il primo opera scope-clinica, il secondo cross-sede. Non si includono a vicenda nelle policy operative (es. Backoffice non gestisce turni; Direttore vede le proprie cliniche soltanto).

**Uso nei controller:**
```csharp
[Authorize(Policy = Policies.RequireDirettore)]
public async Task<IActionResult> ApprovaFerie(string id) { ... }

[Authorize(Policy = Policies.RequireBackoffice)]
public async Task<IActionResult> ListaDottori() { ... }
```

**Scope per clinica:** `User.ClinicaIds` (vuoto = visibilità tenant-wide).
Il claim `clinica_ids` è propagato dal cookie auth e letto in `ITenantContext.ClinicaIds`.

```csharp
// scope check programmatico
if (!_tenantContext.CanAccessClinica(turno.ClinicaId))
    return Forbid();
```

**Helper su `ClaimsPrincipal`:**
```csharp
User.IsManagement()        // Owner | Management
User.IsDirettore()         // solo Direttore
User.IsBackoffice()        // solo Backoffice
User.IsStaff()             // solo Staff
User.CanApprove()          // Management + Direttore (ferie, sostituzioni)
User.CanSeeAnagrafiche()   // Management + Direttore + Backoffice
```

---

## 📡 SignalR — Hub e gruppi

```csharp
// ChatHub.cs
public class ChatHub : Hub
{
    // Gruppi usati:
    // "tenant_{tenantId}"          → broadcast a tutto il tenant
    // "clinica_{clinicaId}"        → broadcast a una sede
    // "user_{userId}"              → messaggio diretto
    // "compliance_alerts"          → alert RLS a direttori e admin

    public async Task JoinClinica(string clinicaId)
    {
        // verificare che l'utente appartenga a quella clinica
        await Groups.AddToGroupAsync(Context.ConnectionId, $"clinica_{clinicaId}");
    }

    public async Task SendMessage(string destinatario, string testo)
    {
        // salvare su MongoDB, poi inviare via SignalR
    }
}
```

**Nel client Razor (.cshtml):**
```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/chat")
    .withAutomaticReconnect()
    .build();

connection.on("NuovoMessaggio", (msg) => { /* aggiorna UI */ });
connection.on("AlertCompliance", (alert) => { /* mostra notifica */ });
```

---

## 🍃 MongoDB — Convenzioni

### Naming
- Database: `chipdent_{tenantId}` (es. `chipdent_confident`)
- Collections: snake_case plurale (es. `visite_mediche`, `turni`, `dipendenti`)

### Document base (tutti i documenti ereditano)
```csharp
public abstract class BaseDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = null!;   // userId
    public bool IsDeleted { get; set; } = false;      // soft delete SEMPRE
}
```

### Indici obbligatori
Creare sempre indice su `IsDeleted` e sui campi usati in filtri frequenti:
```csharp
var indexKeys = Builders<Dipendente>.IndexKeys
    .Ascending(d => d.IsDeleted)
    .Ascending(d => d.ClinicaId);
await collection.Indexes.CreateOneAsync(new CreateIndexModel<Dipendente>(indexKeys));
```

### Soft delete — MAI cancellare documenti
```csharp
// ✅ CORRETTO
await _repo.SoftDeleteAsync(id, userId);

// ❌ MAI
await collection.DeleteOneAsync(d => d.Id == id);
```

---

## 🎨 Razor Views — Convenzioni

### Layout
- `_Layout.cshtml` contiene sidebar, topbar e wrapper principale
- La sidebar evidenzia la sezione attiva via `ViewBag.ActiveSection`
- Colori brand: marrone `#3b1e0c`, ambra `#c47830`, crema `#f5e8d0`

### Partial views
Usare partial views per componenti riusabili:
```csharp
// Nel controller
return PartialView("_TurnoCard", viewModel);

// Nella view con HTMX
<div hx-get="/turni/card/@turno.Id" hx-trigger="load"></div>
```

### ViewModels — sempre tipizzati
```csharp
// ✅ CORRETTO
@model TurniViewModel

// ❌ MAI usare ViewBag per dati complessi
ViewBag.Turni = lista; // non fare
```

---

## ⚙️ Convenzioni generali C#

- **Nullable reference types:** `enable` in tutti i progetti
- **Record** per DTO immutabili, **class** per domain models
- **Result pattern** per operazioni che possono fallire (no exception flow)
- **Dependency Injection** per tutti i servizi — no static classes
- **async/await** ovunque — no `.Result` o `.Wait()`
- Logging via `ILogger<T>` — no `Console.WriteLine`

```csharp
// Result pattern esempio
public record Result<T>(T? Value, string? Error, bool IsSuccess)
{
    public static Result<T> Ok(T value) => new(value, null, true);
    public static Result<T> Fail(string error) => new(default, error, false);
}
```

---

## 🚀 Priorità sviluppo attuale

**Fase MVP — completare nell'ordine:**

1. `Program.cs` — setup MongoDB, AD B2C, SignalR, middleware tenant
2. `TenantMiddleware` + `ITenantContext`
3. Autenticazione AD B2C + claims ruolo
4. `BaseRepository<T>` con operazioni CRUD base
5. Anagrafica: Cliniche → Dottori → Dipendenti
6. Dashboard per ruolo (NetworkAdmin + ClinicDirector)
7. Pianificazione turni (editor visivo Razor + HTMX)
8. Workflow ferie (richiesta staff → approvazione direttore)
9. RLS: visite mediche + corsi sicurezza + DVR
10. Archivio documenti (upload Azure Blob + metadati MongoDB)
11. SignalR ChatHub + messaggistica
12. Circolari con conferma lettura

**Vedi `/docs/BACKLOG.md` per story points e dettaglio.**

---

## ⚠️ Cose da NON fare

- Non accedere a MongoDB senza passare per il repository del tenant
- Non usare `ViewBag` per dati strutturati — sempre ViewModel tipizzato
- Non fare hard delete — sempre soft delete con `IsDeleted = true`
- Non chiamare `.Result` o `.Wait()` su Task — sempre `await`
- Non hardcodare connection string — sempre da `IConfiguration` / Key Vault
- Non disabilitare nullable reference types
- Non aggiungere logica di business nei Controller — delegare ai Service
- Non esporre `ObjectId` MongoDB direttamente nelle URL — usare sempre string
