using AIQueryGenerator.Domain.Enums;

namespace AIQueryGenerator.Application.Connections;

/// <summary>Connection details safe for clients: credentials are never included.</summary>
public sealed record ConnectionDto(
    int ConnectionId,
    string ConnectionName,
    string ServerName,
    string DatabaseName,
    SqlAuthenticationType AuthenticationType,
    string? Username,
    bool HasStoredCredentials,
    bool EncryptConnection,
    bool TrustServerCertificate,
    int ConnectionTimeoutSeconds,
    int CommandTimeoutSeconds,
    int MaxRows,
    bool IsActive,
    ConnectionStatus Status,
    DateTime? LastTestedAt,
    string? LastTestMessage,
    DateTime? LastSchemaRefreshAt,
    IReadOnlyList<string> AllowedRoles,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>Minimal connection info for non-admin users selecting a database.</summary>
public sealed record ConnectionSummaryDto(int ConnectionId, string ConnectionName, string DatabaseName, bool IsActive, ConnectionStatus Status, DateTime? LastSchemaRefreshAt);

public sealed record SaveConnectionRequest
{
    public string ConnectionName { get; init; } = string.Empty;
    public string ServerName { get; init; } = string.Empty;
    public string DatabaseName { get; init; } = string.Empty;
    public SqlAuthenticationType AuthenticationType { get; init; } = SqlAuthenticationType.SqlPassword;
    public string? Username { get; init; }

    /// <summary>
    /// Write-only. A plaintext password (encrypted immediately) or a secret reference such as "env:TARGET_DB_PASSWORD".
    /// On update, null keeps the stored credential.
    /// </summary>
    public string? Password { get; init; }
    public bool EncryptConnection { get; init; } = true;
    public bool TrustServerCertificate { get; init; }
    public int ConnectionTimeoutSeconds { get; init; } = 15;
    public int CommandTimeoutSeconds { get; init; } = 30;
    public int MaxRows { get; init; } = 1000;
    public IReadOnlyList<string> AllowedRoles { get; init; } = [];
}

public sealed record ConnectionTestResultDto(
    bool Success,
    bool IsLeastPrivilege,
    string Message,
    string? ServerVersion,
    string? LoginName,
    IReadOnlyList<string> PrivilegeWarnings,
    DateTime TestedAt);

public sealed record SetConnectionActiveRequest(bool IsActive);

public sealed record AllowedObjectRuleDto(DbObjectType ObjectType, string SchemaName, string? ObjectName, string? ColumnName, ObjectAccessLevel AccessLevel);

public sealed record UpdateAllowedObjectsRequest(IReadOnlyList<AllowedObjectRuleDto> Rules);

public sealed record SchemaRefreshResultDto(
    int Schemas,
    int Tables,
    int Views,
    int Columns,
    int Relationships,
    int TablesAdded,
    int TablesUpdated,
    int TablesRemoved,
    int TablesUnchanged,
    bool FullRefresh,
    DateTime RefreshedAt,
    int DurationMs);
