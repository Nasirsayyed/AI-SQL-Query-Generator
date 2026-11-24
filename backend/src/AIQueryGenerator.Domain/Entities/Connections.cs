using AIQueryGenerator.Domain.Common;
using AIQueryGenerator.Domain.Enums;

namespace AIQueryGenerator.Domain.Entities;

/// <summary>
/// A configured target SQL Server database. Connection strings are always composed server-side from these
/// discrete fields; arbitrary client-supplied connection strings are never accepted.
/// </summary>
public class DatabaseConnection : AuditableEntity, ISoftDelete
{
    public const int DefaultMaxRows = 1000;

    public int ConnectionId { get; set; }
    public string ConnectionName { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public SqlAuthenticationType AuthenticationType { get; set; }
    public string? Username { get; set; }

    /// <summary>Reference to the secret (e.g. "dp:..." protected payload or "env:NAME"). Never plaintext.</summary>
    public string? EncryptedCredentialsReference { get; set; }

    public bool EncryptConnection { get; set; } = true;
    public bool TrustServerCertificate { get; set; }
    public int ConnectionTimeoutSeconds { get; set; } = 15;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public int MaxRows { get; set; } = DefaultMaxRows;
    public bool IsActive { get; set; }
    public ConnectionStatus Status { get; set; } = ConnectionStatus.Untested;
    public DateTime? LastTestedAt { get; set; }
    public string? LastTestMessage { get; set; }
    public DateTime? LastSchemaRefreshAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public ICollection<DatabaseConnectionPermission> Permissions { get; set; } = new List<DatabaseConnectionPermission>();
    public ICollection<AllowedDatabaseObject> AllowedObjects { get; set; } = new List<AllowedDatabaseObject>();

    /// <summary>A connection may only be activated after a successful, least-privilege-verified test.</summary>
    public void Activate()
    {
        if (Status != ConnectionStatus.Healthy)
            throw new DomainException("The connection must pass a connection test before it can be activated.");
        IsActive = true;
    }

    public void Deactivate() => IsActive = false;

    /// <summary>Any change to the connection target or credentials requires a re-test.</summary>
    public void MarkUntested()
    {
        Status = ConnectionStatus.Untested;
        IsActive = false;
    }
}

/// <summary>Grants a role access to query a connection.</summary>
public class DatabaseConnectionPermission
{
    public int DatabaseConnectionPermissionId { get; set; }
    public int ConnectionId { get; set; }
    public int RoleId { get; set; }
    public bool CanQuery { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public DatabaseConnection Connection { get; set; } = null!;
    public Role Role { get; set; } = null!;
}

/// <summary>
/// Admin-defined allow/deny rule for schema objects. Rules are name based so they survive metadata re-synchronisation.
/// Resolution: a table/view is permitted when it has an Allow rule, or its schema has an Allow rule and the table has
/// no Deny rule. A column is permitted when its table is permitted and the column has no Deny rule.
/// </summary>
public class AllowedDatabaseObject
{
    public int AllowedDatabaseObjectId { get; set; }
    public int ConnectionId { get; set; }
    public DbObjectType ObjectType { get; set; }
    public string SchemaName { get; set; } = string.Empty;
    public string? ObjectName { get; set; }
    public string? ColumnName { get; set; }
    public ObjectAccessLevel AccessLevel { get; set; } = ObjectAccessLevel.Allow;
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }

    public DatabaseConnection Connection { get; set; } = null!;
}
