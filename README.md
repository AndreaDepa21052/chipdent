# Chipdent

SaaS gestionale per catene di studi dentistici.

**Stack:** .NET 8 · ASP.NET Core MVC (Razor) · MongoDB · SignalR

## Quick start

```bash
# 1. MongoDB locale
docker compose up -d

# 2. Run app
dotnet run --project src/Chipdent.Web

# 3. Apri https://localhost:5001
#    Login demo: owner@chipdent.it / chipdent
```

Mongo Express disponibile su `http://localhost:8081` per ispezionare i dati.

## Struttura

```
src/Chipdent.Web/
├── Controllers/        # Account, Dashboard, Cliniche, Dottori,
│                       # Dipendenti, Turni, Users, Home
├── Domain/             # Entities (Tenant, User, Clinica, Dottore,
│                       # Dipendente, Turno, Invito) + Entity base
├── Hubs/               # SignalR (NotificationsHub, NotificationPublisher)
├── Infrastructure/
│   ├── Identity/       # BCryptPasswordHasher, Policies (RBAC)
│   ├── Mongo/          # MongoContext, MongoSeeder, MongoSettings
│   └── Tenancy/        # ITenantContext, TenantResolverMiddleware
├── Models/             # ViewModels (Login, Dashboard, Turni, Users…)
├── Views/              # Razor (_Layout, _AuthLayout, _Flash + moduli)
└── wwwroot/            # Design system (site.css), JS, logo SVG

tests/Chipdent.Web.Tests/   # xUnit (hasher, tenancy, entities)
```

## Moduli

| Modulo | Route | Capabilities |
|---|---|---|
| Dashboard | `/dashboard` | Stat aggregati live, attività SignalR |
| Cliniche | `/cliniche` | CRUD + dettaglio con team della sede |
| Dottori | `/dottori` | CRUD + alert scadenza albo |
| Dipendenti | `/dipendenti` | CRUD + ferie residue + stato |
| Turni | `/turni` | Calendario settimanale, click-to-add, navigazione settimane |
| Utenti | `/utenti` | Lista utenti, invito via token, attiva/disattiva |
| Account | `/account` | Login, logout, accept invito |

## Multi-tenancy

Shared-database / shared-collection con `TenantId` su ogni documento. Dopo il
login il claim `tenant_id` viene letto dal `TenantResolverMiddleware` e
propagato all'`ITenantContext` scoped, usato da tutti i controller per
filtrare le query.

Hub SignalR raggruppa i client per tenant (`tenant:{id}`); le notifiche
pubblicate via `INotificationPublisher.PublishAsync(tenantId, channel, payload)`
arrivano solo agli utenti del tenant corretto.

## RBAC

Quattro policy in `Infrastructure/Identity/Policies.cs`:

- `RequireOwner` — solo Owner
- `RequireAdmin` — Owner + Admin (gestione utenti, delete entità)
- `RequireManager` — Owner + Admin + Manager (turni, edit cliniche)
- `RequireHR` — + HR (anagrafiche dottori/dipendenti)

I controller decorano gli endpoint sensibili con `[Authorize(Policy = …)]`.

## Invito utenti

Workflow:
1. Admin accede a `/utenti/invita`, genera invito (email + nome + ruolo).
2. Sistema crea un `Invito` con token random URL-safe (7 giorni di validità).
3. Il link `/account/invito/{token}` mostra una pagina dove l'utente imposta
   la propria password e l'account viene creato.
4. L'invito viene marcato come usato; eventuali revoche da `/utenti`.

## Demo seed (al primo avvio)

- 1 tenant (`Confident Dental`)
- 1 owner (`owner@chipdent.it` / `chipdent`)
- 3 cliniche (Milano, Roma, Torino)
- 3 dottori (con scadenza albo per testare gli alert)
- 3 dipendenti (uno in onboarding, ferie residue diverse)

## Test

```bash
dotnet test
```
