IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE TABLE [AIProviderConfigurations] (
        [AiProviderConfigurationId] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [ProviderType] nvarchar(30) NOT NULL,
        [BaseUrl] nvarchar(500) NOT NULL,
        [Model] nvarchar(100) NOT NULL,
        [ApiKeySecretReference] nvarchar(2000) NOT NULL,
        [Temperature] float NOT NULL,
        [MaxOutputTokens] int NOT NULL,
        [TimeoutSeconds] int NOT NULL,
        [MaxSchemaTables] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_AIProviderConfigurations] PRIMARY KEY ([AiProviderConfigurationId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [AuditLogId] bigint NOT NULL IDENTITY,
        [UserId] int NULL,
        [Action] nvarchar(100) NOT NULL,
        [EntityType] nvarchar(100) NULL,
        [EntityId] nvarchar(100) NULL,
        [Details] nvarchar(4000) NULL,
        [IpAddress] nvarchar(64) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([AuditLogId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE TABLE [DatabaseConnections] (
        [ConnectionId] int NOT NULL IDENTITY,
        [ConnectionName] nvarchar(100) NOT NULL,
        [ServerName] nvarchar(256) NOT NULL,
        [DatabaseName] nvarchar(128) NOT NULL,
        [AuthenticationType] nvarchar(30) NOT NULL,
        [Username] nvarchar(128) NULL,
        [EncryptedCredentialsReference] nvarchar(2000) NULL,
        [EncryptConnection] bit NOT NULL,
        [TrustServerCertificate] bit NOT NULL,
        [ConnectionTimeoutSeconds] int NOT NULL,
        [CommandTimeoutSeconds] int NOT NULL,
        [MaxRows] int NOT NULL DEFAULT 1000,
        [IsActive] bit NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [LastTestedAt] datetime2 NULL,
        [LastTestMessage] nvarchar(500) NULL,
        [LastSchemaRefreshAt] datetime2 NULL,
        [CreatedByUserId] int NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_DatabaseConnections] PRIMARY KEY ([ConnectionId]),
        CONSTRAINT [CK_DatabaseConnections_CommandTimeout] CHECK ([CommandTimeoutSeconds] BETWEEN 1 AND 600),
        CONSTRAINT [CK_DatabaseConnections_MaxRows] CHECK ([MaxRows] BETWEEN 1 AND 100000)
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE TABLE [Permissions] (
        [PermissionId] int NOT NULL IDENTITY,
        [Code] nvarchar(100) NOT NULL,
        [Description] nvarchar(250) NULL,
        CONSTRAINT [PK_Permissions] PRIMARY KEY ([PermissionId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE TABLE [Roles] (
        [RoleId] int NOT NULL IDENTITY,
        [Name] nvarchar(50) NOT NULL,
        [Description] nvarchar(250) NULL,
        [IsSystem] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([RoleId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [UserId] int NOT NULL IDENTITY,
        [FullName] nvarchar(150) NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [NormalizedEmail] nvarchar(256) NOT NULL,
        [PasswordHash] nvarchar(500) NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [LastLoginAt] datetime2 NULL,
        [FailedLoginCount] int NOT NULL,
        [LockoutEndAt] datetime2 NULL,
        [SecurityStamp] nvarchar(64) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([UserId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE TABLE [AllowedDatabaseObjects] (
        [AllowedDatabaseObjectId] int NOT NULL IDENTITY,
        [ConnectionId] int NOT NULL,
        [ObjectType] nvarchar(20) NOT NULL,
        [SchemaName] nvarchar(128) NOT NULL,
        [ObjectName] nvarchar(128) NULL,
        [ColumnName] nvarchar(128) NULL,
        [AccessLevel] nvarchar(10) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedByUserId] int NULL,
        CONSTRAINT [PK_AllowedDatabaseObjects] PRIMARY KEY ([AllowedDatabaseObjectId]),
        CONSTRAINT [FK_AllowedDatabaseObjects_DatabaseConnections_ConnectionId] FOREIGN KEY ([ConnectionId]) REFERENCES [DatabaseConnections] ([ConnectionId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE TABLE [DatabaseSchemas] (
        [SchemaId] int NOT NULL IDENTITY,
        [ConnectionId] int NOT NULL,
        [SchemaName] nvarchar(128) NOT NULL,
        [LastSyncedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_DatabaseSchemas] PRIMARY KEY ([SchemaId]),
        CONSTRAINT [FK_DatabaseSchemas_DatabaseConnections_ConnectionId] FOREIGN KEY ([ConnectionId]) REFERENCES [DatabaseConnections] ([ConnectionId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE TABLE [DatabaseConnectionPermissions] (
        [DatabaseConnectionPermissionId] int NOT NULL IDENTITY,
        [ConnectionId] int NOT NULL,
        [RoleId] int NOT NULL,
        [CanQuery] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_DatabaseConnectionPermissions] PRIMARY KEY ([DatabaseConnectionPermissionId]),
        CONSTRAINT [FK_DatabaseConnectionPermissions_DatabaseConnections_ConnectionId] FOREIGN KEY ([ConnectionId]) REFERENCES [DatabaseConnections] ([ConnectionId]) ON DELETE CASCADE,
        CONSTRAINT [FK_DatabaseConnectionPermissions_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([RoleId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE TABLE [RolePermissions] (
        [RoleId] int NOT NULL,
        [PermissionId] int NOT NULL,
        CONSTRAINT [PK_RolePermissions] PRIMARY KEY ([RoleId], [PermissionId]),
        CONSTRAINT [FK_RolePermissions_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [Permissions] ([PermissionId]) ON DELETE CASCADE,
        CONSTRAINT [FK_RolePermissions_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([RoleId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE TABLE [QueryHistory] (
        [QueryId] bigint NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [ConnectionId] int NOT NULL,
        [QueryTitle] nvarchar(200) NOT NULL,
        [UserQuestion] nvarchar(2000) NULL,
        [GeneratedSQL] nvarchar(max) NULL,
        [ExecutedSQL] nvarchar(max) NULL,
        [ParametersProtected] nvarchar(max) NULL,
        [Explanation] nvarchar(4000) NULL,
        [TablesUsed] nvarchar(1000) NULL,
        [ChartSuggestionJson] nvarchar(2000) NULL,
        [PromptVersion] nvarchar(50) NULL,
        [AiModel] nvarchar(100) NULL,
        [ExecutionDurationMs] int NULL,
        [ReturnedRows] int NULL,
        [ExecutionStatus] nvarchar(30) NOT NULL,
        [ErrorMessage] nvarchar(1000) NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_QueryHistory] PRIMARY KEY ([QueryId]),
        CONSTRAINT [FK_QueryHistory_DatabaseConnections_ConnectionId] FOREIGN KEY ([ConnectionId]) REFERENCES [DatabaseConnections] ([ConnectionId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_QueryHistory_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE TABLE [RefreshTokens] (
        [RefreshTokenId] bigint NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [TokenHash] nvarchar(128) NOT NULL,
        [FamilyId] uniqueidentifier NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedByIp] nvarchar(64) NULL,
        [RevokedAt] datetime2 NULL,
        [ReplacedByTokenHash] nvarchar(128) NULL,
        [ReasonRevoked] nvarchar(50) NULL,
        CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([RefreshTokenId]),
        CONSTRAINT [FK_RefreshTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE TABLE [SavedQueries] (
        [SavedQueryId] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [ConnectionId] int NOT NULL,
        [QueryTitle] nvarchar(200) NOT NULL,
        [Description] nvarchar(2000) NULL,
        [UserQuestion] nvarchar(2000) NULL,
        [SQLQuery] nvarchar(max) NOT NULL,
        [Category] nvarchar(100) NULL,
        [IsShared] bit NOT NULL,
        [ParametersProtected] nvarchar(max) NULL,
        [ChartConfigJson] nvarchar(4000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_SavedQueries] PRIMARY KEY ([SavedQueryId]),
        CONSTRAINT [FK_SavedQueries_DatabaseConnections_ConnectionId] FOREIGN KEY ([ConnectionId]) REFERENCES [DatabaseConnections] ([ConnectionId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SavedQueries_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE TABLE [UserRoles] (
        [UserId] int NOT NULL,
        [RoleId] int NOT NULL,
        [AssignedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_UserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_UserRoles_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([RoleId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_UserRoles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE TABLE [DatabaseTables] (
        [TableId] int NOT NULL IDENTITY,
        [SchemaId] int NOT NULL,
        [ConnectionId] int NOT NULL,
        [TableName] nvarchar(128) NOT NULL,
        [ObjectType] nvarchar(20) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [ObjectModifiedAt] datetime2 NOT NULL,
        [RowCountEstimate] bigint NULL,
        [LastSyncedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_DatabaseTables] PRIMARY KEY ([TableId]),
        CONSTRAINT [FK_DatabaseTables_DatabaseSchemas_SchemaId] FOREIGN KEY ([SchemaId]) REFERENCES [DatabaseSchemas] ([SchemaId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE TABLE [QueryExecutionLogs] (
        [ExecutionId] bigint NOT NULL IDENTITY,
        [QueryId] bigint NULL,
        [SavedQueryId] int NULL,
        [UserId] int NOT NULL,
        [ConnectionId] int NOT NULL,
        [ExecutionStatus] nvarchar(30) NOT NULL,
        [ExecutionDurationMs] int NOT NULL,
        [RowsReturned] int NOT NULL,
        [WasTruncated] bit NOT NULL,
        [ErrorCode] nvarchar(50) NULL,
        [ErrorMessage] nvarchar(1000) NULL,
        [SqlHash] nvarchar(64) NULL,
        [TablesUsed] nvarchar(1000) NULL,
        [ExecutedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_QueryExecutionLogs] PRIMARY KEY ([ExecutionId]),
        CONSTRAINT [FK_QueryExecutionLogs_DatabaseConnections_ConnectionId] FOREIGN KEY ([ConnectionId]) REFERENCES [DatabaseConnections] ([ConnectionId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_QueryExecutionLogs_QueryHistory_QueryId] FOREIGN KEY ([QueryId]) REFERENCES [QueryHistory] ([QueryId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_QueryExecutionLogs_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE TABLE [FavoriteQueries] (
        [FavoriteQueryId] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [SavedQueryId] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_FavoriteQueries] PRIMARY KEY ([FavoriteQueryId]),
        CONSTRAINT [FK_FavoriteQueries_SavedQueries_SavedQueryId] FOREIGN KEY ([SavedQueryId]) REFERENCES [SavedQueries] ([SavedQueryId]) ON DELETE CASCADE,
        CONSTRAINT [FK_FavoriteQueries_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE TABLE [DatabaseColumns] (
        [ColumnId] int NOT NULL IDENTITY,
        [TableId] int NOT NULL,
        [ColumnName] nvarchar(128) NOT NULL,
        [DataType] nvarchar(128) NOT NULL,
        [MaxLength] int NULL,
        [Precision] int NULL,
        [Scale] int NULL,
        [IsNullable] bit NOT NULL,
        [IsPrimaryKey] bit NOT NULL,
        [IsIdentity] bit NOT NULL,
        [IsComputed] bit NOT NULL,
        [OrdinalPosition] int NOT NULL,
        [Description] nvarchar(500) NULL,
        CONSTRAINT [PK_DatabaseColumns] PRIMARY KEY ([ColumnId]),
        CONSTRAINT [FK_DatabaseColumns_DatabaseTables_TableId] FOREIGN KEY ([TableId]) REFERENCES [DatabaseTables] ([TableId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE TABLE [DatabaseIndexes] (
        [DatabaseIndexId] int NOT NULL IDENTITY,
        [TableId] int NOT NULL,
        [IndexName] nvarchar(128) NOT NULL,
        [IsUnique] bit NOT NULL,
        [IsPrimaryKey] bit NOT NULL,
        [Columns] nvarchar(1000) NOT NULL,
        CONSTRAINT [PK_DatabaseIndexes] PRIMARY KEY ([DatabaseIndexId]),
        CONSTRAINT [FK_DatabaseIndexes_DatabaseTables_TableId] FOREIGN KEY ([TableId]) REFERENCES [DatabaseTables] ([TableId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE TABLE [DatabaseRelationships] (
        [RelationshipId] int NOT NULL IDENTITY,
        [ConnectionId] int NOT NULL,
        [ForeignKeyName] nvarchar(128) NOT NULL,
        [FromTableId] int NOT NULL,
        [FromColumnName] nvarchar(128) NOT NULL,
        [ToTableId] int NOT NULL,
        [ToColumnName] nvarchar(128) NOT NULL,
        [OrdinalPosition] int NOT NULL,
        CONSTRAINT [PK_DatabaseRelationships] PRIMARY KEY ([RelationshipId]),
        CONSTRAINT [FK_DatabaseRelationships_DatabaseTables_FromTableId] FOREIGN KEY ([FromTableId]) REFERENCES [DatabaseTables] ([TableId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DatabaseRelationships_DatabaseTables_ToTableId] FOREIGN KEY ([ToTableId]) REFERENCES [DatabaseTables] ([TableId]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AIProviderConfigurations_Name] ON [AIProviderConfigurations] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AllowedDatabaseObjects_ConnectionId_ObjectType_SchemaName_ObjectName_ColumnName] ON [AllowedDatabaseObjects] ([ConnectionId], [ObjectType], [SchemaName], [ObjectName], [ColumnName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_Action] ON [AuditLogs] ([Action]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_CreatedAt] ON [AuditLogs] ([CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_UserId_CreatedAt] ON [AuditLogs] ([UserId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DatabaseColumns_TableId_ColumnName] ON [DatabaseColumns] ([TableId], [ColumnName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DatabaseConnectionPermissions_ConnectionId_RoleId] ON [DatabaseConnectionPermissions] ([ConnectionId], [RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DatabaseConnectionPermissions_RoleId] ON [DatabaseConnectionPermissions] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_DatabaseConnections_ConnectionName] ON [DatabaseConnections] ([ConnectionName]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DatabaseConnections_IsActive] ON [DatabaseConnections] ([IsActive]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DatabaseIndexes_TableId] ON [DatabaseIndexes] ([TableId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DatabaseRelationships_ConnectionId] ON [DatabaseRelationships] ([ConnectionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DatabaseRelationships_FromTableId] ON [DatabaseRelationships] ([FromTableId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DatabaseRelationships_ToTableId] ON [DatabaseRelationships] ([ToTableId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DatabaseSchemas_ConnectionId_SchemaName] ON [DatabaseSchemas] ([ConnectionId], [SchemaName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DatabaseTables_ConnectionId] ON [DatabaseTables] ([ConnectionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DatabaseTables_SchemaId_TableName] ON [DatabaseTables] ([SchemaId], [TableName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_FavoriteQueries_SavedQueryId] ON [FavoriteQueries] ([SavedQueryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_FavoriteQueries_UserId_SavedQueryId] ON [FavoriteQueries] ([UserId], [SavedQueryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Permissions_Code] ON [Permissions] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_QueryExecutionLogs_ConnectionId_ExecutedAt] ON [QueryExecutionLogs] ([ConnectionId], [ExecutedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_QueryExecutionLogs_ExecutedAt] ON [QueryExecutionLogs] ([ExecutedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_QueryExecutionLogs_QueryId] ON [QueryExecutionLogs] ([QueryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_QueryExecutionLogs_SavedQueryId] ON [QueryExecutionLogs] ([SavedQueryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_QueryExecutionLogs_UserId_ExecutedAt] ON [QueryExecutionLogs] ([UserId], [ExecutedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_QueryHistory_ConnectionId_CreatedAt] ON [QueryHistory] ([ConnectionId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_QueryHistory_ExecutionStatus] ON [QueryHistory] ([ExecutionStatus]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_QueryHistory_UserId_CreatedAt] ON [QueryHistory] ([UserId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_FamilyId] ON [RefreshTokens] ([FamilyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RefreshTokens_TokenHash] ON [RefreshTokens] ([TokenHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_UserId_ExpiresAt] ON [RefreshTokens] ([UserId], [ExpiresAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RolePermissions_PermissionId] ON [RolePermissions] ([PermissionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Roles_Name] ON [Roles] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SavedQueries_ConnectionId] ON [SavedQueries] ([ConnectionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SavedQueries_IsShared_Category] ON [SavedQueries] ([IsShared], [Category]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SavedQueries_UserId] ON [SavedQueries] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserRoles_RoleId] ON [UserRoles] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_NormalizedEmail] ON [Users] ([NormalizedEmail]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124102020_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261124102020_InitialCreate', N'8.0.11');
END;
GO

COMMIT;
GO

