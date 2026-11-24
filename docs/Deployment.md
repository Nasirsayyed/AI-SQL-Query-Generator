# Deployment

## Topology

| Component | Hosting |
|---|---|
| API | ASP.NET Core 8 on IIS (in-process, ASP.NET Core Hosting Bundle 8.x), or Kestrel behind a reverse proxy |
| SPA | Static files on IIS (URL Rewrite module) or any static host |
| Application DB | SQL Server 2019 or later |
| Target DBs | SQL Server 2016 or later, each reached with a dedicated read-only login |

Recommended: serve the SPA and API from the **same origin** (for example `https://aiqg.contoso.com/` for the SPA and
`https://aiqg.contoso.com/api` as an IIS application or reverse-proxy path). The refresh cookie is then `SameSite=Strict`,
and no CORS is needed. If they are on different sites, set `Cors:AllowedOrigins`, build the SPA with `VITE_API_BASE_URL`,
and set `Auth:RefreshCookieSameSite=None` (HTTPS required).

## Environments

| Setting file | Use |
|---|---|
| `appsettings.json` | Defaults shared by all environments (no secrets) |
| `appsettings.Development.json` | Local development: migrations on start, demo connection seed, Swagger, `keys/` folder |
| `appsettings.UAT.json` | UAT: migrations on start, Swagger on, DPAPI-protected keys |
| `appsettings.Production.json` | Production: `Database:Initialization=None` (run the SQL script), Swagger off, DPAPI-protected keys |

Select the environment with `ASPNETCORE_ENVIRONMENT` (`Development`, `UAT` or `Production`). The frontend uses
`npm run build` (production) or `npm run build:uat` (`.env.uat`).

## Environment variables

ASP.NET Core maps `__` to `:`. **Never commit secrets.** Set them as IIS application-pool environment variables, machine
environment variables, or through a secret manager (for example the Azure Key Vault configuration provider).

| Variable | Required | Description |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | yes | `Development`, `UAT` or `Production` |
| `ConnectionStrings__ApplicationDb` | yes | Application DB connection string (login needs db_datareader + db_datawriter; DDL only if migrations run on start) |
| `Jwt__SigningKey` | yes | At least 32 random characters; the API refuses to start without it |
| `Jwt__Issuer`, `Jwt__Audience` | no | Defaults `AIQueryGenerator` / `AIQueryGenerator.Web` |
| `Jwt__AccessTokenMinutes`, `Jwt__RefreshTokenDays` | no | Defaults 15 / 7 |
| `OPENAI_API_KEY` | for AI | Default AI key reference (`Ai:Provider:ApiKeyReference = env:OPENAI_API_KEY`) |
| `AZURE_OPENAI_API_KEY` | optional | When an Azure OpenAI provider references it |
| `AIQG_SECRET_*` | optional | Target DB passwords referenced as `env:AIQG_SECRET_NAME` from connection settings |
| `Seed__SuperAdmin__Email`, `Seed__SuperAdmin__Password` | first start | Creates the first Super Admin when no users exist; remove afterwards |
| `DataProtection__KeysPath` | yes (non-dev) | Folder for the Data Protection key ring, outside the web root, persistent and backed up |
| `DataProtection__ProtectWithDpapi` | Windows | `true` encrypts the key ring with DPAPI (machine/app-pool identity) |
| `DataProtection__AzureBlobUri` | Azure / web farm | Blob URI (`https://<account>.blob.core.windows.net/<container>/aiqg-keys.xml`) storing the key ring for every instance |
| `DataProtection__KeyVaultKeyId` | Azure | Key Vault key id (`https://<vault>.vault.azure.net/keys/<name>`) that wraps the key ring |
| `KeyVault__Uri` | Azure | Loads secrets from Azure Key Vault into configuration (see below) |
| `KeyVault__ReloadMinutes` | no | Periodically reload Key Vault secrets (0 = load once at start-up) |
| `Azure__ManagedIdentityClientId` | optional | User-assigned managed identity client id; otherwise `DefaultAzureCredential` is used |
| `Cors__AllowedOrigins__0` | cross-origin only | SPA origin |
| `Auth__RefreshCookieSameSite` | cross-origin only | `None` when the SPA is on a different site |
| `ForwardedHeaders__KnownProxies__0` | behind a proxy | Reverse proxy IP so client IPs and HTTPS are trusted |
| `Execution__*`, `SqlValidation__*`, `RateLimiting__*`, `Limits__MaxRequestBodyBytes` | no | Limits (see `appsettings.json`) |
| `Swagger__Enabled` | no | Expose `/swagger` |

Frontend build variables (public, never secret): `VITE_API_BASE_URL` (empty means same origin) and `VITE_BASE_PATH`
(`/` or `/subdir/`).

> **The Data Protection key ring is critical.** It decrypts stored target passwords, AI keys and query parameters. Losing
> it makes them unreadable (re-enter the passwords). In a web farm, every instance must share the same key ring: use a
> file share (`DataProtection__KeysPath`) or Azure (`DataProtection__AzureBlobUri` plus `DataProtection__KeyVaultKeyId`).

## Azure Key Vault (optional)

Set `KeyVault__Uri` and grant the API's identity (managed identity, or whatever `DefaultAzureCredential` resolves to)
the **Key Vault Secrets User** role. Secret names map to configuration keys like this:

| Key Vault secret name | Configuration key |
|---|---|
| `Jwt--SigningKey` | `Jwt:SigningKey` |
| `ConnectionStrings--ApplicationDb` | `ConnectionStrings:ApplicationDb` |
| `Seed--SuperAdmin--Password` | `Seed:SuperAdmin:Password` |
| `OPENAI-API-KEY`, `AZURE-OPENAI-API-KEY` | `OPENAI_API_KEY`, `AZURE_OPENAI_API_KEY` |
| `AIQG-SECRET-SALES-DB` | `AIQG_SECRET_SALES_DB`, so a connection password can be `env:AIQG_SECRET_SALES_DB` |

Disabled secrets are ignored. For the key ring, create a blob container and a Key Vault RSA key, and grant the identity
**Storage Blob Data Contributor** on the container and **Key Vault Crypto User** on the key. Then set
`DataProtection__AzureBlobUri` and `DataProtection__KeyVaultKeyId`. Leave `DataProtection__KeysPath` empty when using the blob.

These integrations are covered by unit tests for name mapping and wiring, but have not been exercised against a real
Azure subscription.

## Database setup

### Application database

```bash
sqlcmd -S <server> -d master -C -Q "IF DB_ID('AIQueryGeneratorApp') IS NULL CREATE DATABASE AIQueryGeneratorApp"
```

```bash
sqlcmd -S <server> -d AIQueryGeneratorApp -C -i backend/database/ApplicationDatabase.sql
```

Create a login for the API with `db_datareader` and `db_datawriter` on this database (and `db_ddladmin` only if
`Database:Initialization=Migrate`). Roles and permissions are seeded idempotently at every start-up.

### Target databases

For every business database, create a dedicated principal with **SELECT only on approved objects**:

```sql
CREATE LOGIN aiqg_reader WITH PASSWORD = '<strong password>', CHECK_POLICY = ON;
USE SalesDW;
CREATE USER aiqg_reader FOR LOGIN aiqg_reader;
GRANT SELECT ON SCHEMA::dbo TO aiqg_reader;          -- or GRANT SELECT ON specific tables/views
GRANT VIEW DEFINITION ON SCHEMA::dbo TO aiqg_reader; -- schema discovery
GRANT SHOWPLAN TO aiqg_reader;                        -- optional: estimated execution plans
DENY INSERT, UPDATE, DELETE, ALTER, EXECUTE ON SCHEMA::dbo TO aiqg_reader;
DENY SELECT ON dbo.Customers (Email) TO aiqg_reader;  -- example: sensitive column
```

Prefer Windows integrated authentication (an app-pool identity mapped to a read-only database user) or Microsoft Entra
managed identity where available; then no password is stored at all. The API's connection test rejects principals with
sysadmin, db_owner or any write permission.

The demo database: `sqlcmd -S <server> -U <admin> -C -i backend/database/SampleTargetDatabase.sql -v ReaderPassword="..."`.

## Build

```bash
cd backend && dotnet publish src/AIQueryGenerator.API -c Release -o ../publish/api
```

```bash
cd frontend/ai-query-generator && npm ci && npm run build
```

The SPA output is `frontend/ai-query-generator/dist` (it includes `web.config`).

For a sub-directory deployment of the SPA:

```bash
VITE_BASE_PATH=/aiqg/ npm run build
```

Then change the rewrite target in `dist/web.config` to `/aiqg/index.html`.

## IIS

1. Install the **ASP.NET Core 8 Hosting Bundle** and the **URL Rewrite** module.
2. **API site/application:** physical path `publish/api` (includes `web.config`), app pool *No Managed Code*, its own
   identity. Set the environment variables on the app pool (IIS 10+: Configuration Editor →
   `system.applicationHost/applicationPools` → environmentVariables) or as machine variables. Grant the identity modify
   rights on the key and log folders.
3. **SPA site:** physical path `dist`. Same-origin option: create an IIS application `/api` under the SPA site pointing to
   the API folder (the SPA `web.config` excludes `/api`, `/health` and `/swagger` from SPA fallback).
4. Bind HTTPS with a valid certificate. The API enables HSTS and HTTPS redirection outside Development.
5. Check `https://<host>/health`, then sign in, test, activate and refresh each connection, configure schema permissions,
   and configure the AI provider (**Settings → AI provider → Test active provider**).

Request size: Kestrel/IIS limits follow `Limits:MaxRequestBodyBytes`; the API `web.config` sets `maxAllowedContentLength`
to match.

## Logging and monitoring

Serilog writes to the console and rolling compact-JSON files (paths per environment in `appsettings.*.json`). Each
response's trace id is logged with the request. Ship the files to your log platform. Audit records are in the `AuditLogs`
table and visible under **Settings → Audit log**.

## Backup and restore

- **Application DB:** schedule full and log backups:

  ```sql
  BACKUP DATABASE AIQueryGeneratorApp TO DISK = N'X:\Backups\AIQueryGeneratorApp.bak' WITH COMPRESSION, CHECKSUM;
  ```

  Restore with `RESTORE DATABASE ... WITH REPLACE`, then start the API (the seeder is idempotent).
- **Data Protection keys:** back up `DataProtection:KeysPath` together with the database. A database restored without its
  matching key ring cannot decrypt stored credentials or parameters.
- **Target DBs** are owned by their business teams; this application only reads them. After a target restore or schema
  change, use **Refresh schema** (incremental) or `?full=true`.

## Security checklist

- [ ] HTTPS only; HSTS enabled; Swagger disabled in production
- [ ] `Jwt__SigningKey` random, at least 32 characters, stored outside source control
- [ ] Data Protection keys persisted, protected (DPAPI, certificate or Key Vault) and backed up
- [ ] Every target connection uses a SELECT-only principal (the connection test passes as least-privilege)
- [ ] Schema permissions configured; sensitive columns denied in the app **and** with `DENY SELECT` in SQL Server
- [ ] Rate limits and `Execution` limits reviewed
- [ ] `Seed__SuperAdmin__Password` removed after the first start
- [ ] CORS restricted to the SPA origin (or same-origin hosting)
- [ ] Logs shipped and retained; audit log reviewed
