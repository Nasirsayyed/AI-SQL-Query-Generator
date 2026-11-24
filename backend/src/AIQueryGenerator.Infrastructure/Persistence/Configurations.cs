using AIQueryGenerator.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIQueryGenerator.Infrastructure.Persistence;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.HasKey(x => x.UserId);
        b.Property(x => x.FullName).HasMaxLength(150).IsRequired();
        b.Property(x => x.Email).HasMaxLength(256).IsRequired();
        b.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
        b.HasIndex(x => x.NormalizedEmail).IsUnique();
        b.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
        b.Property(x => x.SecurityStamp).HasMaxLength(64).IsRequired();
        b.Property(x => x.IsActive).HasDefaultValue(true);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("Roles");
        b.HasKey(x => x.RoleId);
        b.Property(x => x.Name).HasMaxLength(50).IsRequired();
        b.HasIndex(x => x.Name).IsUnique();
        b.Property(x => x.Description).HasMaxLength(250);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("UserRoles");
        b.HasKey(x => new { x.UserId, x.RoleId });
        b.HasOne(x => x.User).WithMany(u => u.UserRoles).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Role).WithMany(r => r.UserRoles).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.RoleId);
    }
}

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.ToTable("Permissions");
        b.HasKey(x => x.PermissionId);
        b.Property(x => x.Code).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Description).HasMaxLength(250);
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("RolePermissions");
        b.HasKey(x => new { x.RoleId, x.PermissionId });
        b.HasOne(x => x.Role).WithMany(r => r.RolePermissions).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Permission).WithMany(p => p.RolePermissions).HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.PermissionId);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens");
        b.HasKey(x => x.RefreshTokenId);
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.FamilyId);
        b.HasIndex(x => new { x.UserId, x.ExpiresAt });
        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
        b.Property(x => x.ReasonRevoked).HasMaxLength(50);
        b.Property(x => x.CreatedByIp).HasMaxLength(64);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DatabaseConnectionConfiguration : IEntityTypeConfiguration<DatabaseConnection>
{
    public void Configure(EntityTypeBuilder<DatabaseConnection> b)
    {
        b.ToTable("DatabaseConnections", t =>
        {
            t.HasCheckConstraint("CK_DatabaseConnections_MaxRows", "[MaxRows] BETWEEN 1 AND 100000");
            t.HasCheckConstraint("CK_DatabaseConnections_CommandTimeout", "[CommandTimeoutSeconds] BETWEEN 1 AND 600");
        });
        b.HasKey(x => x.ConnectionId);
        b.Property(x => x.ConnectionName).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.ConnectionName).IsUnique().HasFilter("[IsDeleted] = 0");
        b.Property(x => x.ServerName).HasMaxLength(256).IsRequired();
        b.Property(x => x.DatabaseName).HasMaxLength(128).IsRequired();
        b.Property(x => x.AuthenticationType).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Username).HasMaxLength(128);
        b.Property(x => x.EncryptedCredentialsReference).HasMaxLength(2000);
        b.Property(x => x.LastTestMessage).HasMaxLength(500);
        b.Property(x => x.MaxRows).HasDefaultValue(DatabaseConnection.DefaultMaxRows);
        b.HasIndex(x => x.IsActive);
    }
}

internal sealed class DatabaseConnectionPermissionConfiguration : IEntityTypeConfiguration<DatabaseConnectionPermission>
{
    public void Configure(EntityTypeBuilder<DatabaseConnectionPermission> b)
    {
        b.ToTable("DatabaseConnectionPermissions");
        b.HasKey(x => x.DatabaseConnectionPermissionId);
        b.HasIndex(x => new { x.ConnectionId, x.RoleId }).IsUnique();
        b.HasOne(x => x.Connection).WithMany(c => c.Permissions).HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AllowedDatabaseObjectConfiguration : IEntityTypeConfiguration<AllowedDatabaseObject>
{
    public void Configure(EntityTypeBuilder<AllowedDatabaseObject> b)
    {
        b.ToTable("AllowedDatabaseObjects");
        b.HasKey(x => x.AllowedDatabaseObjectId);
        b.Property(x => x.ObjectType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.AccessLevel).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.SchemaName).HasMaxLength(128).IsRequired();
        b.Property(x => x.ObjectName).HasMaxLength(128);
        b.Property(x => x.ColumnName).HasMaxLength(128);
        b.HasIndex(x => new { x.ConnectionId, x.ObjectType, x.SchemaName, x.ObjectName, x.ColumnName });
        b.HasOne(x => x.Connection).WithMany(c => c.AllowedObjects).HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DatabaseSchemaConfiguration : IEntityTypeConfiguration<DatabaseSchema>
{
    public void Configure(EntityTypeBuilder<DatabaseSchema> b)
    {
        b.ToTable("DatabaseSchemas");
        b.HasKey(x => x.SchemaId);
        b.Property(x => x.SchemaName).HasMaxLength(128).IsRequired();
        b.HasIndex(x => new { x.ConnectionId, x.SchemaName }).IsUnique();
        b.HasOne(x => x.Connection).WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DatabaseTableConfiguration : IEntityTypeConfiguration<DatabaseTable>
{
    public void Configure(EntityTypeBuilder<DatabaseTable> b)
    {
        b.ToTable("DatabaseTables");
        b.HasKey(x => x.TableId);
        b.Property(x => x.TableName).HasMaxLength(128).IsRequired();
        b.Property(x => x.ObjectType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.HasIndex(x => new { x.SchemaId, x.TableName }).IsUnique();
        b.HasIndex(x => x.ConnectionId);
        b.HasOne(x => x.Schema).WithMany(s => s.Tables).HasForeignKey(x => x.SchemaId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DatabaseColumnConfiguration : IEntityTypeConfiguration<DatabaseColumn>
{
    public void Configure(EntityTypeBuilder<DatabaseColumn> b)
    {
        b.ToTable("DatabaseColumns");
        b.HasKey(x => x.ColumnId);
        b.Property(x => x.ColumnName).HasMaxLength(128).IsRequired();
        b.Property(x => x.DataType).HasMaxLength(128).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.HasIndex(x => new { x.TableId, x.ColumnName }).IsUnique();
        b.HasOne(x => x.Table).WithMany(t => t.Columns).HasForeignKey(x => x.TableId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DatabaseRelationshipConfiguration : IEntityTypeConfiguration<DatabaseRelationship>
{
    public void Configure(EntityTypeBuilder<DatabaseRelationship> b)
    {
        b.ToTable("DatabaseRelationships");
        b.HasKey(x => x.RelationshipId);
        b.Property(x => x.ForeignKeyName).HasMaxLength(128).IsRequired();
        b.Property(x => x.FromColumnName).HasMaxLength(128).IsRequired();
        b.Property(x => x.ToColumnName).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.ConnectionId);
        // Relationships are removed explicitly before tables during synchronisation (avoids multiple cascade paths).
        b.HasOne(x => x.FromTable).WithMany().HasForeignKey(x => x.FromTableId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ToTable).WithMany().HasForeignKey(x => x.ToTableId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DatabaseIndexConfiguration : IEntityTypeConfiguration<DatabaseIndex>
{
    public void Configure(EntityTypeBuilder<DatabaseIndex> b)
    {
        b.ToTable("DatabaseIndexes");
        b.HasKey(x => x.DatabaseIndexId);
        b.Property(x => x.IndexName).HasMaxLength(128).IsRequired();
        b.Property(x => x.Columns).HasMaxLength(1000).IsRequired();
        b.HasOne(x => x.Table).WithMany(t => t.Indexes).HasForeignKey(x => x.TableId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class QueryHistoryConfiguration : IEntityTypeConfiguration<QueryHistory>
{
    public void Configure(EntityTypeBuilder<QueryHistory> b)
    {
        b.ToTable("QueryHistory");
        b.HasKey(x => x.QueryId);
        b.Property(x => x.QueryTitle).HasMaxLength(200).IsRequired();
        b.Property(x => x.UserQuestion).HasMaxLength(2000);
        b.Property(x => x.GeneratedSql).HasColumnName("GeneratedSQL");
        b.Property(x => x.ExecutedSql).HasColumnName("ExecutedSQL");
        b.Property(x => x.TablesUsed).HasMaxLength(1000);
        b.Property(x => x.PromptVersion).HasMaxLength(50);
        b.Property(x => x.AiModel).HasMaxLength(100);
        b.Property(x => x.ErrorMessage).HasMaxLength(1000);
        b.Property(x => x.ChartSuggestionJson).HasMaxLength(2000);
        b.Property(x => x.Explanation).HasMaxLength(4000);
        b.Property(x => x.ExecutionStatus).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(x => new { x.UserId, x.CreatedAt });
        b.HasIndex(x => new { x.ConnectionId, x.CreatedAt });
        b.HasIndex(x => x.ExecutionStatus);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Connection).WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SavedQueryConfiguration : IEntityTypeConfiguration<SavedQuery>
{
    public void Configure(EntityTypeBuilder<SavedQuery> b)
    {
        b.ToTable("SavedQueries");
        b.HasKey(x => x.SavedQueryId);
        b.Property(x => x.QueryTitle).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.UserQuestion).HasMaxLength(2000);
        b.Property(x => x.SqlQuery).HasColumnName("SQLQuery").IsRequired();
        b.Property(x => x.Category).HasMaxLength(100);
        b.Property(x => x.ChartConfigJson).HasMaxLength(4000);
        b.HasIndex(x => x.UserId);
        b.HasIndex(x => new { x.IsShared, x.Category });
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Connection).WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FavoriteQueryConfiguration : IEntityTypeConfiguration<FavoriteQuery>
{
    public void Configure(EntityTypeBuilder<FavoriteQuery> b)
    {
        b.ToTable("FavoriteQueries");
        b.HasKey(x => x.FavoriteQueryId);
        b.HasIndex(x => new { x.UserId, x.SavedQueryId }).IsUnique();
        b.HasOne(x => x.SavedQuery).WithMany(s => s.Favorites).HasForeignKey(x => x.SavedQueryId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class QueryExecutionLogConfiguration : IEntityTypeConfiguration<QueryExecutionLog>
{
    public void Configure(EntityTypeBuilder<QueryExecutionLog> b)
    {
        b.ToTable("QueryExecutionLogs");
        b.HasKey(x => x.ExecutionId);
        b.Property(x => x.ExecutionStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.ErrorCode).HasMaxLength(50);
        b.Property(x => x.ErrorMessage).HasMaxLength(1000);
        b.Property(x => x.SqlHash).HasMaxLength(64);
        b.Property(x => x.TablesUsed).HasMaxLength(1000);
        b.HasIndex(x => x.ExecutedAt);
        b.HasIndex(x => new { x.UserId, x.ExecutedAt });
        b.HasIndex(x => new { x.ConnectionId, x.ExecutedAt });
        b.HasIndex(x => x.SavedQueryId);
        b.HasOne(x => x.Query).WithMany().HasForeignKey(x => x.QueryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Connection).WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.HasKey(x => x.AuditLogId);
        b.Property(x => x.Action).HasMaxLength(100).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(100);
        b.Property(x => x.EntityId).HasMaxLength(100);
        b.Property(x => x.Details).HasMaxLength(4000);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.HasIndex(x => x.CreatedAt);
        b.HasIndex(x => new { x.UserId, x.CreatedAt });
        b.HasIndex(x => x.Action);
    }
}

internal sealed class AiProviderConfigurationConfiguration : IEntityTypeConfiguration<AiProviderConfiguration>
{
    public void Configure(EntityTypeBuilder<AiProviderConfiguration> b)
    {
        b.ToTable("AIProviderConfigurations");
        b.HasKey(x => x.AiProviderConfigurationId);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Name).IsUnique();
        b.Property(x => x.ProviderType).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.BaseUrl).HasMaxLength(500).IsRequired();
        b.Property(x => x.Model).HasMaxLength(100).IsRequired();
        b.Property(x => x.ApiKeySecretReference).HasMaxLength(2000).IsRequired();
    }
}
