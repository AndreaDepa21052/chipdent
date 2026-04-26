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

Mongo Express è disponibile su `http://localhost:8081` per ispezionare i dati.

## Struttura

```
src/Chipdent.Web/
├── Controllers/        # MVC controllers (Home, Account, Dashboard)
├── Domain/             # Entities (Tenant, User, Clinica, …)
├── Hubs/               # SignalR (NotificationsHub, NotificationPublisher)
├── Infrastructure/
│   ├── Identity/       # PasswordHasher (BCrypt)
│   ├── Mongo/          # MongoContext, MongoSeeder
│   └── Tenancy/        # ITenantContext, TenantResolverMiddleware
├── Models/             # ViewModels
├── Views/              # Razor (_Layout, _AuthLayout, Account, Dashboard)
└── wwwroot/            # CSS design system, JS, logo SVG
```

## Multi-tenancy

Modello shared-database / shared-collection con `TenantId` per riga, isolato a
livello di middleware (claim `tenant_id` dopo login) e propagato in
`ITenantContext` per tutte le query.

Hub SignalR raggruppa i client per tenant (`tenant:{id}`) — le notifiche
pubblicate via `INotificationPublisher` arrivano solo agli utenti del
tenant corretto.

## Demo seed

Al primo avvio Mongo viene popolato con:
- 1 tenant (`Confident Dental`)
- 1 utente owner (`owner@chipdent.it` / `chipdent`)
- 3 cliniche di esempio (Milano, Roma, Torino)
