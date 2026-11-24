# Database Design

Two logical databases:

- **Application database** (`AIQueryGeneratorApp`): owned by the API and managed by EF Core migrations
  (`backend/src/AIQueryGenerator.Infrastructure/Persistence/Migrations`). The idempotent script is in
  `backend/database/ApplicationDatabase.sql`.
- **Target database(s)**: business data being queried. The sample is `AIQueryDemoDB`
  (`backend/database/SampleTargetDatabase.sql`).

No plaintext database password, API key, refresh token or sensitive query parameter is stored in the application database.

## Application database

```
Users ─< UserRoles >─ Roles ─< RolePermissions >─ Permissions
  │                    │
  │                    └─< DatabaseConnectionPermissions >─ DatabaseConnections ─< AllowedDatabaseObjects
  │                                                              │
  ├─< RefreshTokens                                               ├─< DatabaseSchemas ─< DatabaseTables ─< DatabaseColumns
  │                                                               │                          ├─< DatabaseIndexes
  │                                                               │                          └─< DatabaseRelationships (From/To)
  ├─< QueryHistory >──────────────────────────────────────────────┤
  ├─< QueryExecutionLogs >── QueryHistory                         │
  ├─< SavedQueries >──────────────────────────────────────────────┘
  └─< FavoriteQueries >── SavedQueries
AuditLogs (UserId nullable, no FK so records survive)    AIProviderConfigurations
```

| Table | Purpose | Key columns / constraints |
|---|---|---|
| `Users` | Accounts | `UserId` PK; `NormalizedEmail` unique; `PasswordHash` (PBKDF2); `SecurityStamp`; lockout fields; `IsActive`; `CreatedAt`/`UpdatedAt` |
| `Roles` | SuperAdmin, Admin, Developer, Analyst, Viewer | `Name` unique; `IsSystem` |
| `UserRoles` | User ↔ role | PK (`UserId`, `RoleId`); cascade from user, restrict from role |
| `Permissions` | Permission catalogue (`query.execute`, …) | `Code` unique |
| `RolePermissions` | Role ↔ permission | PK (`RoleId`, `PermissionId`) |
| `RefreshTokens` | Rotating refresh tokens | `TokenHash` unique (SHA-256); `FamilyId` index; `ExpiresAt`, `RevokedAt`, `ReplacedByTokenHash` |
| `DatabaseConnections` | Target databases | `ConnectionName` unique where not deleted; `AuthenticationType`; `EncryptedCredentialsReference` (`dp:`/`env:` only); `Status` (Untested/Healthy/Failed/Unsafe); `MaxRows` CHECK 1–100000; `CommandTimeoutSeconds` CHECK 1–600; soft delete |
| `DatabaseConnectionPermissions` | Roles allowed to query a connection | unique (`ConnectionId`, `RoleId`) |
| `AllowedDatabaseObjects` | Admin allow/deny rules (Schema/Table/View/Column), name based so they survive metadata re-sync | index (`ConnectionId`, `ObjectType`, `SchemaName`, `ObjectName`, `ColumnName`) |
| `DatabaseSchemas` | Discovered schemas | unique (`ConnectionId`, `SchemaName`) |
| `DatabaseTables` | Discovered tables/views | unique (`SchemaId`, `TableName`); `ObjectModifiedAt` (sys.objects.modify_date, for incremental refresh); `RowCountEstimate`; `Description` |
| `DatabaseColumns` | Discovered columns | unique (`TableId`, `ColumnName`); type, length, precision, scale, nullability, PK, identity, computed |
| `DatabaseRelationships` | Foreign keys (one row per column pair) | `FromTableId`/`ToTableId` FKs (restrict) |
| `DatabaseIndexes` | Index metadata | `TableId` cascade |
| `QueryHistory` | Question → generated SQL → executed SQL | `QueryId` bigint; `UserQuestion`, `GeneratedSQL`, `ExecutedSQL`, `ParametersProtected` (encrypted), `ExecutionStatus`, `ExecutionDurationMs`, `ReturnedRows`, `PromptVersion`, `AiModel`; indexes (`UserId`, `CreatedAt`), (`ConnectionId`, `CreatedAt`); soft delete |
| `SavedQueries` | Reusable/shared queries | `SQLQuery`, `Category`, `IsShared`, `ParametersProtected`, `ChartConfigJson`; index (`IsShared`, `Category`) |
| `FavoriteQueries` | Per-user favourites | unique (`UserId`, `SavedQueryId`) |
| `QueryExecutionLogs` | One row per execution attempt | `ExecutionStatus`, `ExecutionDurationMs`, `RowsReturned`, `WasTruncated`, `ErrorCode`, `SqlHash`, `TablesUsed`, `ExecutedAt`; indexes on `ExecutedAt`, (`UserId`, `ExecutedAt`), (`ConnectionId`, `ExecutedAt`) |
| `AuditLogs` | Security-relevant actions (logins, user and connection changes, permission changes, schema refresh) | `Action`, `EntityType`, `EntityId`, `Details` (JSON, no secrets), `IpAddress`, `CreatedAt` |
| `AIProviderConfigurations` | AI provider settings | `Name` unique; `ProviderType`, `BaseUrl`, `Model`, `ApiKeySecretReference` (reference only), temperature/limits, `IsActive` |

Enums are stored as strings for readability. Every timestamp is UTC.

### Permission resolution (`AllowedDatabaseObjects`)

- A table/view is permitted when it has an **Allow** rule, or its schema has an **Allow** rule and the table has no **Deny** rule.
  A schema **Deny** blocks everything in it.
- A column is permitted when its table is permitted and the column has no **Deny** rule.
- With no rules, nothing is permitted (secure default).

## Sample target database: `AIQueryDemoDB`

| Table | Columns |
|---|---|
| `Customers` | CustomerId PK, CustomerName, Email (unique; restricted in the demo), City, Region, CreatedDate |
| `Products` | ProductId PK, ProductName (unique), Category, UnitPrice (CHECK ≥ 0), ReorderLevel |
| `Warehouses` | WarehouseId PK, WarehouseName (unique), City |
| `Inventory` | InventoryId PK, ProductId FK, WarehouseId FK, AvailableQuantity, ReservedQuantity, LastUpdated; unique (ProductId, WarehouseId) |
| `Orders` | OrderId PK, CustomerId FK, OrderDate, OrderStatus (CHECK Pending/Processing/Shipped/Delivered/Cancelled), TotalAmount, WarehouseId FK, **ShippedDate** (added so processing time can be measured) |
| `OrderDetails` | OrderDetailId PK, OrderId FK, ProductId FK, Quantity (CHECK > 0), UnitPrice, LineTotal (persisted computed) |
| `Payments` | PaymentId PK, OrderId FK, PaymentDate, PaymentAmount, PaymentStatus (CHECK Completed/Pending/Failed/Refunded) |
| `vwMonthlySales` (view) | OrderYear, OrderMonth, OrderCount, Revenue |

Seed data: 250 customers in 15 cities and 5 regions (20 never order), 40 products in 7 categories, 5 warehouses, about 150
inventory rows (several below reorder level), 6,000 orders across about 15 months (20 placed within the last hour), about
15,000 order lines, and payments including failures, retries and refunds. All dates are relative to `GETDATE()`.

The script also creates the `aiqg_reader` login (password from the `ReaderPassword` sqlcmd variable) with SELECT,
VIEW DEFINITION and SHOWPLAN only; explicit DENY of INSERT/UPDATE/DELETE/ALTER/EXECUTE; and `DENY SELECT` on
`Customers(Email)`.

## Migrations

```bash
cd backend
dotnet ef migrations add <Name> -p src/AIQueryGenerator.Infrastructure -s src/AIQueryGenerator.Infrastructure -o Persistence/Migrations
dotnet ef migrations script --idempotent -p src/AIQueryGenerator.Infrastructure -o database/ApplicationDatabase.sql
```
