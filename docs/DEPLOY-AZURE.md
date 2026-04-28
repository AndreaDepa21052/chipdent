# Deploy Chipdent su Azure App Service + MongoDB Atlas

Guida operativa per portare Chipdent dallo sviluppo locale (Docker Mongo)
al cloud (Azure App Service Linux + MongoDB Atlas).

> **⚠️ Sicurezza:** non committare mai la connection string Atlas su Git
> (con o senza password). La password va gestita esclusivamente come
> *App Setting* di Azure (encrypted at rest) o tramite Key Vault.
> Se hai esposto la password in chat / log / repo, ruotala su Atlas
> prima di procedere.

---

## 1. Configura MongoDB Atlas

### 1.1 Crea l'utente database (se non l'hai già)
1. Atlas → Database Access → Add New Database User.
2. Username: `chipdent_app` (esempio).
3. Password: usa "Autogenerate Secure Password" e copiala in un
   password manager.
4. Database User Privileges: **Read and write to any database**
   (semplifica: una stringa serve per molti tenant futuri).

### 1.2 Network Access
- Atlas → Network Access → Add IP Address.
- **Per Azure App Service**: aggiungi `0.0.0.0/0` (allow from anywhere)
  e proteggi tramite credenziali forti, oppure
- usa **Private Endpoint** (Atlas → Network Access → Private Endpoint)
  collegato alla tua VNet Azure (se hai App Service Plan ≥ S1).
  Questa è l'opzione production-grade.

### 1.3 Indici consigliati
Prima del primo deploy, lancia questi comandi su Atlas → Browse
Collections → Shell oppure tramite `mongosh`:

```javascript
use chipdent;

// Tenant scoping (la query più usata)
db.users.createIndex({ tenantId: 1, email: 1 });
db.cliniche.createIndex({ tenantId: 1, slug: 1 });
db.dipendenti.createIndex({ tenantId: 1, clinicaId: 1, stato: 1 });
db.dottori.createIndex({ tenantId: 1, attivo: 1 });
db.turni.createIndex({ tenantId: 1, data: 1, personaId: 1 });
db.timbrature.createIndex({ tenantId: 1, dipendenteId: 1, timestamp: -1 });
db.audit.createIndex({ tenantId: 1, createdAt: -1 });
db.richiesteFerie.createIndex({ tenantId: 1, dipendenteId: 1, dataInizio: 1 });
db.segnalazioni.createIndex({ tenantId: 1, stato: 1, createdAt: -1 });
db.whistleblowing.createIndex({ tenantId: 1, codiceTracciamento: 1 }, { unique: true });

// Tenant lookup per slug (login multi-workspace)
db.tenants.createIndex({ slug: 1 }, { unique: true });
```

Senza questi indici l'app funziona ma le query diventano lente con
volumi >10.000 documenti.

---

## 2. Migra i dati locali (opzionale — solo se hai dati di sviluppo da portare)

Se vuoi partire con un DB pulito (raccomandato per produzione), salta
questa sezione: il `MongoSeeder` creerà tutto al primo avvio.

### 2.1 Esporta da locale

```bash
# Trova la connection string locale dal tuo docker-compose:
#   mongodb://chipdent:chipdent@localhost:27017

mongodump \
  --uri "mongodb://chipdent:chipdent@localhost:27017" \
  --db chipdent \
  --out ./dump
```

### 2.2 Importa su Atlas

```bash
# Usa la connstring Atlas SENZA il database finale (lo selezionerà mongorestore)
ATLAS_URI="mongodb+srv://chipdent_app:LA_TUA_NUOVA_PASSWORD@cluster0.zbuamnl.mongodb.net/?retryWrites=true&w=majority&appName=Cluster0"

mongorestore \
  --uri "$ATLAS_URI" \
  --nsInclude "chipdent.*" \
  ./dump
```

Verifica su Atlas → Browse Collections che il database `chipdent` sia
popolato.

---

## 3. Crea l'Azure App Service

### 3.1 Risorse Azure
- **Resource Group**: `rg-chipdent-prod` (esempio).
- **App Service Plan**: Linux, P1V2 minimo (necessario per WebSockets
  SignalR senza buffering aggressivo). Region: West Europe.
- **App Service**: `chipdent-app` (Linux, .NET 8).

```bash
# CLI (in alternativa al Portal)
az group create -n rg-chipdent-prod -l westeurope

az appservice plan create \
  -g rg-chipdent-prod -n plan-chipdent \
  --is-linux --sku P1V2

az webapp create \
  -g rg-chipdent-prod -n chipdent-app \
  --plan plan-chipdent \
  --runtime "DOTNETCORE:8.0"
```

### 3.2 App Settings (variabili d'ambiente)

In Azure Portal → App Service `chipdent-app` → Configuration →
Application settings, aggiungi:

| Nome | Valore | Note |
|------|--------|------|
| `Mongo__ConnectionString` | `mongodb+srv://chipdent_app:NUOVA_PASSWORD@cluster0.zbuamnl.mongodb.net/?retryWrites=true&w=majority&appName=Cluster0` | Connstring Atlas |
| `Mongo__Database` | `chipdent` | Nome database |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Disabilita developer exception page |
| `WEBSITES_PORT` | `8080` | Porta in ascolto Kestrel |

> Nota: il doppio `__` nel nome (es. `Mongo__ConnectionString`) viene
> mappato da .NET su `Mongo:ConnectionString` nell'IConfiguration.

### 3.3 Abilita WebSockets (necessario per SignalR)

App Service `chipdent-app` → Configuration → General settings →
**Web sockets: On**.

### 3.4 Always On (raccomandato)

Stessa pagina, **Always On: On**. Evita che l'app si addormenti dopo
20 minuti di inattività (non disponibile sui plan Free/Shared).

---

## 4. Storage degli upload locali

Chipdent salva i file caricati (logo, allegati documenti, contratti,
DPI, ecc.) sotto `wwwroot/uploads/{tenantId}/...` come da
`LocalFileStorage.cs`.

**⚠️ Limite di Azure App Service**: il filesystem dell'App Service
**non è persistente** durante deploy o scale-out. Ci sono 3 opzioni:

### 4.1 (Quick) Mountare Azure File Share su `wwwroot/uploads`
1. Crea uno Storage Account + File Share.
2. App Service → Configuration → Path mappings → New Azure Storage Mount:
   - Name: `uploads`
   - Storage type: Azure Files
   - Mount path: `/home/site/wwwroot/uploads`
   - Storage account + share del passo precedente.
3. Riavvia l'App Service.

### 4.2 (Production) Migrare a Azure Blob Storage
Sostituisci `LocalFileStorage` con un `AzureBlobFileStorage`
(implementa `IFileStorage`, già astratto). Resta out-of-scope per il
primo deploy ma è il path consigliato a regime.

### 4.3 (Quick & dirty) `WEBSITE_LOCAL_CACHE_OPTION = Always`
Persistente fra restart ma **non fra deploy**. Solo per test.

---

## 5. Pubblica il codice

### 5.1 Da Visual Studio
- Right-click sul progetto `Chipdent.Web` → Publish → Azure → App
  Service Linux → seleziona `chipdent-app` → Finish → Publish.

### 5.2 Da CLI (più pulito per CI/CD)

```bash
cd src/Chipdent.Web

# Pubblica self-contained false (più leggero)
dotnet publish -c Release -o ./publish

# Zip
cd publish && zip -r ../chipdent.zip . && cd ..

# Deploy
az webapp deploy \
  -g rg-chipdent-prod -n chipdent-app \
  --src-path ./chipdent.zip \
  --type zip
```

### 5.3 GitHub Actions (raccomandato a regime)
Aggiungi un workflow `.github/workflows/azure-deploy.yml` con
trigger su push su `main`. Bozza:

```yaml
name: Deploy
on:
  push:
    branches: [main]
jobs:
  build-and-deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - run: dotnet publish src/Chipdent.Web -c Release -o ./publish
      - uses: azure/webapps-deploy@v3
        with:
          app-name: chipdent-app
          slot-name: production
          package: ./publish
          publish-profile: ${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE }}
```

Recupera il publish profile da App Service → Get publish profile e
incollalo come secret `AZURE_WEBAPP_PUBLISH_PROFILE` su GitHub.

---

## 6. Verifica post-deploy

1. **DNS / HTTPS**: `chipdent-app.azurewebsites.net` ha già HTTPS
   automatico con cert managed da Microsoft. Per dominio custom (es.
   `app.chipdent.it`):
   - App Service → Custom domains → Add custom domain.
   - App Service Managed Certificate (gratis) per il TLS.

2. **Login**: vai a `https://chipdent-app.azurewebsites.net` →
   redirect a `/account/login`. La prima volta il MongoSeeder esegue
   se il DB è vuoto (crea Confident, owner@chipdent.it / chipdent,
   3 cliniche demo, ecc.).

3. **SignalR live**: dopo login, vai in dashboard, click "Test
   notifica live". Il bell deve popolarsi entro 1s. Se no, controlla
   che WebSockets sia On (passo 3.3).

4. **Upload file**: vai in `/cliniche/{id}/modifica` → carica un logo.
   Se ricevi 500 controlla che `wwwroot/uploads` sia scrivibile (vedi
   sezione 4).

5. **Logs**: App Service → Log stream per vedere stdout/stderr in
   tempo reale. `_log.LogInformation` e `LogWarning` sono visibili.

---

## 7. Pulizia di sviluppo da committare prima del deploy

Verifica che questi siano corretti per produzione:

### 7.1 `appsettings.json` (resta in repo, è il fallback)

Già OK così com'è — i valori sensibili (connstring) vengono
sovrascritti dalle App Settings di Azure.

### 7.2 `Program.cs` cookie auth in HTTPS

In produzione i cookie devono essere `Secure`. Non hai bisogno di
modificare nulla: ASP.NET Core 8 lo fa automaticamente quando
l'app è dietro HTTPS (Azure App Service è sempre HTTPS).

### 7.3 Email digest (opzionale)

`LogOnlyEmailSender` in produzione **non manda davvero email** — solo
log. Quando vorrai email reali sostituisci con un provider:

```csharp
// Program.cs
builder.Services.AddSingleton<IEmailSender, SendGridEmailSender>();
```

con un'implementazione che usa SendGrid / Postmark / Azure
Communication Services. Out of scope per il primo deploy.

---

## 8. Smoke test finale

```bash
# Sostituisci con il tuo URL
URL="https://chipdent-app.azurewebsites.net"

# Health: devono rispondere 200
curl -I "$URL/account/login"
curl -I "$URL/whistleblowing"
curl -I "$URL/presenze/kiosk"
curl -I "$URL/sw.js"
curl -I "$URL/site.webmanifest"
```

Se tutti rispondono con `200 OK` (o `302` per le redirect
post-autenticazione), il deploy è andato.

---

## 9. Checklist sintetica

- [ ] Password Atlas ruotata se l'avevi esposta in chat/log/repo
- [ ] User Atlas creato con permesso readWrite
- [ ] Network Access Atlas: 0.0.0.0/0 o Private Endpoint
- [ ] Indici creati (sezione 1.3)
- [ ] App Service Plan Linux P1V2 + WebApp .NET 8
- [ ] App Settings: `Mongo__ConnectionString`, `Mongo__Database`, `ASPNETCORE_ENVIRONMENT=Production`
- [ ] WebSockets: On
- [ ] Always On: On
- [ ] Mount Azure Files su `/home/site/wwwroot/uploads`
- [ ] Deploy effettuato (CLI o GitHub Actions)
- [ ] Smoke test passato
- [ ] Custom domain + cert HTTPS configurato (opzionale ma raccomandato)

---

## 10. Backup e disaster recovery

Atlas free/shared fa **snapshot ogni 6h** e li conserva 2 giorni. Per
SLA seri usa M10+ con Continuous Cloud Backup (point-in-time restore).

App Service: `Backups` → schedule daily, retain 30 days. Backup
include solo i file dell'app (codice + uploads), non il database
(che è già su Atlas).

---

## Note finali

Il `MongoSeeder` è idempotente: in produzione fallirà silenziosamente
i seed delle entità già esistenti, quindi non causa problemi. Però
crea sempre `owner@chipdent.it` con password `chipdent` se non esiste:
**al primo login cambia la password** dalla pagina Profilo →
Cambia password.

Per dubbi sulla migrazione XDENT, paghe, integrations: l'export CSV
in `/presenze/export-paghe.csv` è già compatibile Zucchetti. Le
integrazioni API live richiedono accordi commerciali con i vendor —
fuori dallo scope tecnico.
