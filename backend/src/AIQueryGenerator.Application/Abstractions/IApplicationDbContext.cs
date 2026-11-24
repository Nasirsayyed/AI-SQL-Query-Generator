using AIQueryGenerator.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIQueryGenerator.Application.Abstractions;

/// <summary>Unit of work over the application database (not the target business database).</summary>
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<DatabaseConnection> DatabaseConnections { get; }
    DbSet<DatabaseConnectionPermission> DatabaseConnectionPermissions { get; }
    DbSet<AllowedDatabaseObject> AllowedDatabaseObjects { get; }
    DbSet<DatabaseSchema> DatabaseSchemas { get; }
    DbSet<DatabaseTable> DatabaseTables { get; }
    DbSet<DatabaseColumn> DatabaseColumns { get; }
    DbSet<DatabaseRelationship> DatabaseRelationships { get; }
    DbSet<DatabaseIndex> DatabaseIndexes { get; }
    DbSet<QueryHistory> QueryHistory { get; }
    DbSet<SavedQuery> SavedQueries { get; }
    DbSet<FavoriteQuery> FavoriteQueries { get; }
    DbSet<QueryExecutionLog> QueryExecutionLogs { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<AiProviderConfiguration> AiProviderConfigurations { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
