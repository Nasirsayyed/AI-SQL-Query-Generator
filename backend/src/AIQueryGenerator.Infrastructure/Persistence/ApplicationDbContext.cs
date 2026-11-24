using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Domain.Common;
using AIQueryGenerator.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIQueryGenerator.Infrastructure.Persistence;

/// <summary>EF Core context for the application database (users, connections, metadata cache, history, audit).</summary>
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, TimeProvider clock)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<DatabaseConnection> DatabaseConnections => Set<DatabaseConnection>();
    public DbSet<DatabaseConnectionPermission> DatabaseConnectionPermissions => Set<DatabaseConnectionPermission>();
    public DbSet<AllowedDatabaseObject> AllowedDatabaseObjects => Set<AllowedDatabaseObject>();
    public DbSet<DatabaseSchema> DatabaseSchemas => Set<DatabaseSchema>();
    public DbSet<DatabaseTable> DatabaseTables => Set<DatabaseTable>();
    public DbSet<DatabaseColumn> DatabaseColumns => Set<DatabaseColumn>();
    public DbSet<DatabaseRelationship> DatabaseRelationships => Set<DatabaseRelationship>();
    public DbSet<DatabaseIndex> DatabaseIndexes => Set<DatabaseIndex>();
    public DbSet<QueryHistory> QueryHistory => Set<QueryHistory>();
    public DbSet<SavedQuery> SavedQueries => Set<SavedQuery>();
    public DbSet<FavoriteQuery> FavoriteQueries => Set<FavoriteQuery>();
    public DbSet<QueryExecutionLog> QueryExecutionLogs => Set<QueryExecutionLog>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AiProviderConfiguration> AiProviderConfigurations => Set<AiProviderConfiguration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
            if (entry.State == EntityState.Modified) entry.Entity.UpdatedAt = now;
        }
        foreach (var entry in ChangeTracker.Entries<Role>().Where(e => e.State == EntityState.Added && e.Entity.CreatedAt == default))
            entry.Entity.CreatedAt = now;
        foreach (var entry in ChangeTracker.Entries<AuditLog>().Where(e => e.State == EntityState.Added && e.Entity.CreatedAt == default))
            entry.Entity.CreatedAt = now;
        return base.SaveChangesAsync(cancellationToken);
    }
}
