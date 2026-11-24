# Architecture

## Overview

```
┌──────────────────────────── Browser (React SPA) ─────────────────────────────┐
│ Monaco SQL editor · MUI Data Grid · ECharts · TanStack Query · JWT in memory │
└───────────────┬──────────────────────────────────────────────────────────────┘
                │ HTTPS · Bearer access token · HttpOnly refresh cookie
┌───────────────▼────────────── ASP.NET Core 8 API ───────────────────────────┐
│ API layer        Controllers (thin) · JWT auth · permission policies          │
│                  rate limiting · request limits · exception → ApiResponse     │
│ Application      Auth/Users · Connections · Schema · AI · SQL pipeline ·      │
│                  History · Saved queries · Dashboard · Settings · Audit       │
│ Domain           Entities · enums · invariants (no framework dependencies)    │
│ Infrastructure   EF Core (app DB) · SqlClient/Dapper (target DB) · ScriptDom  │
│                  validator · OpenAI client · Data Protection · JWT · Serilog  │
└──────┬───────────────────────────────┬───────────────────────────┬──────────┘
       │ EF Core                        │ SqlClient (read-only login) │ HTTPS
┌──────▼─────────────┐       ┌──────────▼──────────────┐   ┌──────────▼─────────┐
│ Application DB     │       │ Target business DB(s)   │   │ AI provider        │
│ users, roles, meta-│       │ e.g. AIQueryDemoDB      │   │ OpenAI / Azure /   │
│ data cache, history│       │ SELECT-only principal   │   │ compatible         │
└────────────────────┘       └─────────────────────────┘   └────────────────────┘
```

The application database and target databases are separate. The API never stores target credentials in plain text and
never sends row data to the AI provider.

## Clean Architecture layers

| Project | Responsibility | Depends on |
|---|---|---|
| `Domain` | Entities (`User`, `DatabaseConnection`, `QueryHistory`, …), enums, invariants (lockout, activation requires a healthy test, refresh-token revocation) | Shared |
| `Shared` | `ApiResponse<T>`, paging contracts, `AppException` hierarchy and error codes, role/permission catalogue | — |
| `Application` | Use cases and DTOs, FluentValidation validators, abstractions (`ISqlValidator`, `ITargetDatabaseClient`, `IAiCompletionClient`, `ISecretStore`, …), the query execution pipeline, schema context builder, chart policy | Domain, Shared |
| `Infrastructure` | EF Core `ApplicationDbContext`, migrations, seeding; ScriptDom validator and formatter; SqlClient target client, catalog discovery, showplan parser; OpenAI-compatible client and versioned prompts; JWT, PBKDF2 hashing, Data Protection secret store, validation tokens, concurrency limiter, audit | Application |
| `API` | Controllers, permission policy provider, validation filter, exception and security-header middleware, rate limiter, CORS, Swagger, health checks | Infrastructure |

Controllers contain no business logic: each action calls one application service.

## Key flows

### Natural language → SQL (`POST /api/ai/generate-sql`)

1. `ConnectionAccessService` checks that the connection exists, is active, and that one of the caller's roles is granted it.
2. `SchemaCatalogService` builds an `AuthorizedCatalog` (cached for 10 minutes and invalidated on sync or rule change) from
   the metadata cache plus the admin allow/deny rules.
3. `SchemaContextBuilder` picks the most relevant **permitted** tables (keyword scoring plus foreign-key neighbours, capped by
   `MaxSchemaTables`) and renders compact DDL-like text. Descriptions are sanitised and labelled as data.
4. The prompt includes the database engine, current date, restrictions, the permitted schema, and the question wrapped in
   `<question>` tags and marked untrusted. The system prompt is the versioned `sql-generation.v1.md`.
5. `OpenAiCompatibleClient` calls Chat Completions with `response_format = json_schema` (strict). The response is parsed
   by `AiResponseParser`; non-JSON or wrapped output is rejected (`AI_INVALID_RESPONSE`).
6. The SQL is validated. If invalid, the model gets one repair attempt with the validator errors.
7. A `QueryHistory` row is written (`Generated`, `ClarificationRequested` or `ValidationFailed`). A validation token is
   returned only for valid SQL. **Nothing is executed.**

### Validate → Execute (`POST /api/sql/validate`, `POST /api/sql/execute`)

```
validate:  parse → allowlist walk → object/column authorisation → limits → parameters → token(user, conn, sha256(sql), sha256(params))
execute:   verify token → (Analyst: SQL must equal its AI generation) → QueryExecutionPipeline
pipeline:  connection access → bind & type parameters → validate AGAIN → concurrency slot →
           read-only execution (rolled-back transaction, MaxRows+1 read, timeout, cancellation) →
           history + execution log → result
```

History re-runs and saved-query runs enter the same pipeline (without a token), so every execution is re-authorised and
re-validated against current permissions.

## SQL security model (defence in depth)

| Layer | Control |
|---|---|
| 1. Prompt | Versioned system prompt with strict rules. The schema contains only permitted objects. **Not relied on for security.** |
| 2. Structured output | Strict JSON schema; the parsed response is validated again |
| 3. AST validation | ScriptDom `TSql160Parser`. Exactly one batch with one `SelectStatement`. Every syntax node must be in an explicit allowlist of node types (CTEs, joins, subqueries, window functions, CASE, CAST and so on). Dangerous nodes produce specific errors: OPENROWSET/OPENQUERY/OPENDATASOURCE/OPENXML/OPENJSON, `@@` variables, NEXT VALUE FOR, table-valued and user-defined functions, FOR XML/JSON, PIVOT, TABLESAMPLE, query hints, SELECT INTO. Functions must be in an approved built-in list; `xp_`/`sp_` names are always rejected. Comments are tokens, not nodes, so they cannot hide statements |
| 4. Object authorisation | Every table/view must exist in the metadata cache and be permitted. System schemas (`sys`, `INFORMATION_SCHEMA`) and cross-database or linked-server names are rejected. Columns are resolved conservatively through aliases; a reference that could reach a restricted column is rejected, and so is `SELECT *` over a table with restricted columns |
| 5. Limits | Max length, joins, output columns, syntax nodes, subquery depth and parameters |
| 6. Validation token | Time-limited Data Protection token binding user, connection, SQL hash and parameter hash; editing SQL invalidates it |
| 7. Execution | Parameterised `SqlCommand` (no string concatenation of values), `ApplicationIntent=ReadOnly`, transaction always rolled back, `LOCK_TIMEOUT`, command timeout, MaxRows+1 streaming with `Cancel()`, per-user and global concurrency limits, rate limiting |
| 8. Database principal | Connections cannot be activated unless the login passes a least-privilege probe: not sysadmin/serveradmin/securityadmin, not db_owner/ddladmin/datawriter/securityadmin, no CONTROL/ALTER/INSERT/UPDATE/DELETE/CREATE TABLE, and no write permission on any table. SQL Server enforces SELECT-only access, plus column DENYs, independently of the app |

## Authentication and authorisation

- **Access token:** 15-minute JWT (HS256) with `sub`, roles and permission claims. Held in browser memory only.
- **Refresh token:** 64 random bytes, SHA-256 hashed at rest, delivered in an `HttpOnly; SameSite=Strict; Secure` cookie
  scoped to `/api/auth`. Rotated on every use. Reusing a rotated token revokes the whole token family. Refresh and logout
  require an `X-Requested-With` header (CSRF mitigation).
- **Passwords:** ASP.NET Core Identity PBKDF2 hasher, strength rules, and a 15-minute lockout after 5 failures.
- **Authorisation:** `[HasPermission("…")]` policies backed by a dynamic policy provider. Services perform record-level
  checks (ownership, sharing, connection grants, admin-management rules). Role, permission, password or activation changes
  revoke refresh tokens.

## Secrets

- Target passwords and AI keys are stored only as **references**: `dp:<payload>` (ASP.NET Core Data Protection, keys
  outside the DB and protectable with DPAPI, certificates or Azure Key Vault) or `env:NAME`. `env:` references are limited
  to configured prefixes (`AIQG_SECRET_*`, `OPENAI_API_KEY`, `AZURE_OPENAI_API_KEY`) so a connection cannot be pointed at an
  arbitrary server to exfiltrate, say, the JWT key.
- Stored query parameters are encrypted with Data Protection.
- Connection strings are built with `SqlConnectionStringBuilder` from validated fields; user-supplied connection strings
  are never accepted.

## AI prompts

Versioned embedded resources in `Infrastructure/AI/Prompts`: `sql-generation.v1`, `sql-explanation.v1`,
`chart-recommendation.v1`, `query-optimization.v1`. The highest version is used unless `Ai:PromptVersions:<name>` pins
one. The prompt id and model are stored with each history record.

## Frontend

- `api/`: Axios client (bearer token, single-flight refresh on 401, normalised `ApiError`) and typed endpoint wrappers
- `auth/`: in-memory token store, `AuthProvider` (session restore from the refresh cookie), route guards
- `features/*`: one folder per page (assistant, dashboard, history, saved, connections, schema, users, settings)
- `features/assistant/useQueryWorkbench`: generate → edit → validate → execute state machine. Execution is enabled only
  while the current SQL and parameter signature match the last successful validation
- `components/`: Monaco `SqlEditor` (self-hosted, SQL only, lazy-loaded, autocomplete from permitted schema), `ResultsGrid`,
  `ResultChart` (validated declarative ECharts options; never AI-supplied code), shared UI
- `utils/export`: CSV/Excel with spreadsheet formula-injection neutralisation

## Observability

Serilog request logging and structured application logs (console plus rolling JSON files). Technical details (SqlException
numbers, provider errors) are logged server-side. Clients receive user-safe messages, stable error codes and a trace id.
`/health` checks the application database.
