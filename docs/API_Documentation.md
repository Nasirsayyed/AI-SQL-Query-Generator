# API Documentation

Interactive OpenAPI documentation is served at `/swagger` (enabled by `Swagger:Enabled`; on by default in Development and UAT).
Every endpoint except login, refresh, logout and `/health` requires `Authorization: Bearer <access token>`.

## Response contract

Every response, success or failure, uses the same envelope:

```json
{
  "success": true,
  "data": { },
  "message": null,
  "errorCode": null,
  "errors": [],
  "traceId": null
}
```

On failure `success` is `false`, `message` is safe to display, `errorCode` is stable, `errors` lists field or validation
issues (`{ "field": "...", "message": "..." }`), and `traceId` correlates with server logs. Stack traces, connection strings
and infrastructure details are never returned.

Paged endpoints accept `page`, `pageSize` (1–200), `search`, `sortBy`, `sortDirection` (`asc`/`desc`) and return
`{ items, page, pageSize, totalCount, totalPages }`.

## Status codes and error codes

| HTTP | errorCode | Meaning |
|---|---|---|
| 400 | `VALIDATION_FAILED` | Request validation (FluentValidation) failed |
| 401 | `UNAUTHORIZED`, `TOKEN_EXPIRED`, `INVALID_CREDENTIALS` | Missing or expired token, bad login. Expired tokens also send `X-Token-Expired: true` |
| 403 | `FORBIDDEN` | Missing permission or record-level access |
| 403 | `SQL_PERMISSION_DENIED` | SQL Server denied access (least-privilege login) |
| 404 | `NOT_FOUND` | Resource not found or not visible to the caller |
| 409 | `CONFLICT` | Duplicate name or email |
| 413 | `REQUEST_TOO_LARGE` | Body exceeds `Limits:MaxRequestBodyBytes` |
| 422 | `SQL_INVALID` | SQL failed validation at execution time; `errors` lists issues |
| 422 | `SQL_NOT_VALIDATED` | Missing, expired or mismatched validation token |
| 422 | `SQL_EXECUTION_FAILED`, `INVALID_PARAMETERS`, `CONNECTION_INACTIVE`, `BUSINESS_RULE` | Domain rule failures |
| 429 | `RATE_LIMITED`, `CONCURRENT_QUERY_LIMIT` | Rate or concurrency limit |
| 502 | `AI_INVALID_RESPONSE` | AI returned malformed, refused or truncated output |
| 503 | `AI_NOT_CONFIGURED`, `AI_RATE_LIMITED`, `AI_UNAVAILABLE`, `DATABASE_UNAVAILABLE` | Dependency unavailable |
| 504 | `AI_TIMEOUT`, `SQL_TIMEOUT` | Time limits exceeded |

## Endpoints

### Authentication — `api/auth`

| Method | Path | Permission | Notes |
|---|---|---|---|
| POST | `/api/auth/login` | anonymous (rate limited) | Body `{ email, password }` → `{ accessToken, accessTokenExpiresAt, user }`; sets the `aiqg_refresh` HttpOnly cookie |
| POST | `/api/auth/refresh` | cookie + `X-Requested-With` header | Rotates the refresh token. Reusing an old token revokes the family |
| POST | `/api/auth/logout` | cookie + `X-Requested-With` header | Revokes the token family and clears the cookie |
| GET | `/api/auth/profile` | authenticated | `{ userId, fullName, email, roles[], permissions[], lastLoginAt }` |
| POST | `/api/auth/change-password` | authenticated | `{ currentPassword, newPassword }`; revokes sessions |

### Users and roles — `users.manage`

| Method | Path | Notes |
|---|---|---|
| GET | `/api/users?search&role&isActive&page&pageSize` | Paged |
| GET | `/api/users/{id}` | |
| POST | `/api/users` | `{ fullName, email, password, roles[] }`. Only Super Admins may grant Admin or SuperAdmin |
| PUT | `/api/users/{id}` | `{ fullName, isActive, roles[] }`. At least one active Super Admin must remain |
| POST | `/api/users/{id}/reset-password` | `{ newPassword }` |
| GET | `/api/roles` | Roles with permission codes and user counts |
| GET | `/api/roles/permissions` | Permission catalogue |
| PUT | `/api/roles/{id}/permissions` | `{ permissions[] }` (Super Admin role is immutable) |

### Database connections

| Method | Path | Permission | Notes |
|---|---|---|---|
| GET | `/api/connections` | `connections.view` | Connections the caller's roles may query (admins: all) |
| GET | `/api/connections/manage` | `connections.manage` | Full details (no credentials; `hasStoredCredentials` flag) |
| GET | `/api/connections/{id}` | `connections.manage` | |
| POST | `/api/connections` | `connections.manage` | `SaveConnectionRequest` (below) |
| PUT | `/api/connections/{id}` | `connections.manage` | `password: null` keeps the stored credential; target changes reset the status to Untested |
| DELETE | `/api/connections/{id}` | `connections.manage` | Soft delete; destroys the credential reference; history kept |
| POST | `/api/connections/{id}/test` | `connections.manage` | Connectivity plus least-privilege probe → `{ success, isLeastPrivilege, message, serverVersion, loginName, privilegeWarnings[] }` |
| POST | `/api/connections/{id}/active` | `connections.manage` | `{ isActive }`. Activation requires status Healthy |
| POST | `/api/connections/{id}/refresh-schema?full=false` | `connections.manage` | Incremental (by `modify_date`) or full metadata sync |
| GET/PUT | `/api/connections/{id}/allowed-objects` | `schema.configure` | `{ rules: [{ objectType: Schema\|Table\|View\|Column, schemaName, objectName?, columnName?, accessLevel: Allow\|Deny }] }` |

```json
{
  "connectionName": "Sales DW",
  "serverName": "sql01.contoso.local,1433",
  "databaseName": "SalesDW",
  "authenticationType": "SqlPassword",
  "username": "aiqg_reader",
  "password": "env:AIQG_SECRET_SALESDW",
  "encryptConnection": true,
  "trustServerCertificate": false,
  "connectionTimeoutSeconds": 15,
  "commandTimeoutSeconds": 30,
  "maxRows": 1000,
  "allowedRoles": ["Developer", "Analyst"]
}
```

`authenticationType` is one of `SqlPassword`, `WindowsIntegrated`, `EntraManagedIdentity` or `EntraDefault`.
`serverName` must be `host`, `host\instance` or `host,port`; connection strings are rejected.

### Schema — `schema.view`

| Method | Path | Notes |
|---|---|---|
| GET | `/api/schema/{connectionId}` | Schemas → tables, relationships, refresh time. Non-admins see permitted objects only |
| GET | `/api/schema/{connectionId}/tables?schemaName=` | Table summaries |
| GET | `/api/schema/{connectionId}/tables/{schema.table}` | Columns (type, nullability, PK, FK target, permitted flag), indexes, relationships |
| GET | `/api/schema/{connectionId}/search?q=` | Schema, table and column search |
| GET | `/api/schema/{connectionId}/completions` | Permitted tables and columns for editor autocomplete |

### AI — rate limited (`RateLimiting:AiPerMinute`)

| Method | Path | Permission | Notes |
|---|---|---|---|
| POST | `/api/ai/generate-sql` | `query.generate` | `{ connectionId, question, schemaName? }` → `GenerateSqlResponse` (below). **Does not execute** |
| POST | `/api/ai/explain-sql` | `query.explain` | `{ connectionId, sql, beginnerFriendly }` → `{ summary, steps[{clause, description}], beginnerExplanation, notes[] }` |
| POST | `/api/ai/suggest-chart` | `charts.view` | `{ question, columns[{name, dataType, clrType}], rowCount }`. Column metadata only, never rows. Falls back to a heuristic when AI is unavailable (`source: "heuristic"`) |
| POST | `/api/ai/optimize-sql` | `query.explain` | `{ connectionId, sql }` → `{ summary, suggestions[], optimizedSql, optimizedSqlValidation }` |
| GET | `/api/ai/status` | authenticated | `{ configured, providerName, model }` |

```json
{
  "success": true,
  "needsClarification": false,
  "clarificationQuestion": null,
  "sqlQuery": "SELECT TOP (10) c.CustomerName, SUM(o.TotalAmount) AS TotalSales FROM ...",
  "queryTitle": "Top 10 Customers by Sales",
  "explanation": "This query calculates total sales...",
  "tablesUsed": ["dbo.Customers", "dbo.Orders"],
  "columnsUsed": ["CustomerName", "TotalAmount"],
  "parameters": [],
  "warnings": [],
  "chartSuggestion": { "recommended": true, "chartType": "bar", "xAxis": "CustomerName", "yAxis": "TotalSales", "aggregation": "none", "source": "ai" },
  "validation": { "isValid": true, "message": "...", "errors": [], "warnings": [], "referencedObjects": [], "validationToken": "CfDJ8..." },
  "queryHistoryId": 42,
  "model": "gpt-4o-mini",
  "promptVersion": "sql-generation.v1"
}
```

### SQL

| Method | Path | Permission | Notes |
|---|---|---|---|
| POST | `/api/sql/validate` | `query.execute` | `{ connectionId, sql, parameters[] }` → `{ isValid, message, errors[{code, message, line, column}], warnings[], referencedObjects[], referencedParameters[], validationToken }` |
| POST | `/api/sql/execute` | `query.execute` (rate limited) | `{ connectionId, sql, parameters[], validationToken, queryHistoryId?, queryTitle? }` → `QueryResult`. Re-validates; users without `query.edit_sql` may only run SQL identical to their AI generation |
| POST | `/api/sql/format` | `query.generate` | `{ sql }` → `{ sql }` (regenerated from the AST; comments dropped) |
| POST | `/api/sql/explain-plan` | `query.edit_sql` | Estimated plan via `SET SHOWPLAN_XML ON` (not executed) → `{ summary{estimatedTotalCost, estimatedRows, topOperators[], warnings[], missingIndexes[]}, planXml }` |

Parameters: `{ name, type: String|Int|BigInt|Decimal|Bool|Date|DateTime|Guid, value: string|null }`. Values are parsed
with the invariant culture and bound as typed `SqlParameter`s.

`QueryResult`:

```json
{
  "queryHistoryId": 42,
  "columns": [{ "name": "CustomerName", "dataType": "nvarchar", "clrType": "String" }],
  "rows": [["Olivia Smith", 98000.5]],
  "rowCount": 1,
  "isTruncated": false,
  "maxRows": 1000,
  "durationMs": 38,
  "executedAt": "2026-11-24T10:55:00Z",
  "connectionId": 1,
  "connectionName": "AIQueryDemoDB",
  "warnings": []
}
```

Big integers above 2^53 and decimals with more than 15 significant digits are returned as strings. Long text is truncated
to `Execution:MaxCellLength`.

### Query history — `history.view`

| Method | Path | Notes |
|---|---|---|
| GET | `/api/query-history?search&from&to&userId&connectionId&status&page&pageSize&sortBy&sortDirection` | Own records; all users with `history.view_all` |
| GET | `/api/query-history/{id}` | Detail; decrypted parameters for the owner only |
| DELETE | `/api/query-history/{id}` | `history.delete`; soft delete, erases stored parameters |
| POST | `/api/query-history/{id}/rerun` | `query.execute`; owner only; fresh authorisation and validation. Body `{ parameters? }` |

### Saved queries

| Method | Path | Permission | Notes |
|---|---|---|---|
| GET | `/api/saved-queries?scope=mine\|shared\|all&category&favoritesOnly&search` | `shared_reports.view` | |
| GET | `/api/saved-queries/categories` | `shared_reports.view` | |
| GET | `/api/saved-queries/{id}` | `shared_reports.view` | Owner or shared |
| POST/PUT | `/api/saved-queries[/{id}]` | `saved_queries.manage` | SQL must validate; `isShared` needs `saved_queries.share`; roles without editing must pass `sourceQueryHistoryId` and the SQL must equal that generation |
| DELETE | `/api/saved-queries/{id}` | `saved_queries.manage` | Owner (or user admin) |
| POST | `/api/saved-queries/{id}/favorite` | `shared_reports.view` | Toggles; returns `{ isFavorite }` |
| POST | `/api/saved-queries/{id}/run` | `shared_reports.view` | Re-validated on every run |

### Dashboard — `dashboard.view`

Scope is global for `history.view_all`, otherwise the caller's own activity.

| Method | Path | Returns |
|---|---|---|
| GET | `/api/dashboard/summary` | totals, successes, failures, average execution ms, generated today, active connections |
| GET | `/api/dashboard/query-trends?days=30` | daily totals, succeeded, failed, generated |
| GET | `/api/dashboard/top-tables?days=30&top=10` | most used tables |
| GET | `/api/dashboard/execution-performance?days=30` | daily average and maximum ms; per connection |
| GET | `/api/dashboard/top-users` | most active users (`history.view_all`) |
| GET | `/api/dashboard/categories` | popular saved-query categories |
| GET | `/api/dashboard/recent-activity` | latest queries, failures, metadata refreshes |

### Settings and audit

| Method | Path | Permission |
|---|---|---|
| GET/POST | `/api/settings/ai` | `ai_settings.manage` |
| PUT/DELETE | `/api/settings/ai/{id}` | `ai_settings.manage` |
| POST | `/api/settings/ai/{id}/activate` | `ai_settings.manage` |
| POST | `/api/settings/ai/test` | `ai_settings.manage` |
| GET | `/api/audit-logs?action&userId&from&to` | `audit.view` |
| GET | `/health` | anonymous |
