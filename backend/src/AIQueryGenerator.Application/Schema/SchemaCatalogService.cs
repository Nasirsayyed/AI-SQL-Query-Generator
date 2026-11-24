using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Domain.Entities;
using AIQueryGenerator.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AIQueryGenerator.Application.Schema;

public interface ISchemaCatalogService
{
    /// <summary>Builds the authorisation catalog for a connection from cached metadata and admin allow/deny rules.</summary>
    Task<AuthorizedCatalog> GetCatalogAsync(int connectionId, CancellationToken ct);
    void Invalidate(int connectionId);
}

public sealed class SchemaCatalogService(IApplicationDbContext db, IMemoryCache cache) : ISchemaCatalogService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public async Task<AuthorizedCatalog> GetCatalogAsync(int connectionId, CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey(connectionId), out AuthorizedCatalog? cached) && cached is not null)
            return cached;

        var connection = await db.DatabaseConnections.AsNoTracking().FirstAsync(c => c.ConnectionId == connectionId, ct);
        var tables = await db.DatabaseTables.AsNoTracking().Where(t => t.ConnectionId == connectionId)
            .Include(t => t.Schema).Include(t => t.Columns).ToListAsync(ct);
        var rules = await db.AllowedDatabaseObjects.AsNoTracking().Where(a => a.ConnectionId == connectionId).ToListAsync(ct);
        var relationships = await db.DatabaseRelationships.AsNoTracking().Where(r => r.ConnectionId == connectionId)
            .Include(r => r.FromTable).ThenInclude(t => t.Schema)
            .Include(r => r.ToTable).ThenInclude(t => t.Schema)
            .ToListAsync(ct);

        var evaluator = new AccessRuleEvaluator(rules);
        var catalogTables = tables.Select(t =>
        {
            var allowed = evaluator.IsTableAllowed(t.Schema.SchemaName, t.TableName);
            return new CatalogTable
            {
                TableId = t.TableId,
                Schema = t.Schema.SchemaName,
                Name = t.TableName,
                ObjectType = t.ObjectType,
                Description = t.Description,
                IsAllowed = allowed,
                RowCountEstimate = t.RowCountEstimate,
                AllColumns = t.Columns.OrderBy(c => c.OrdinalPosition).ToDictionary(
                    c => c.ColumnName,
                    c => new CatalogColumn(c.ColumnName, FormatType(c), c.IsNullable, c.IsPrimaryKey, c.Description),
                    StringComparer.OrdinalIgnoreCase),
                AllowedColumns = allowed
                    ? new HashSet<string>(t.Columns.Where(c => evaluator.IsColumnAllowed(t.Schema.SchemaName, t.TableName, c.ColumnName)).Select(c => c.ColumnName), StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            };
        }).ToList();

        var catalogRelationships = relationships.Select(r => new CatalogRelationship(
            r.ForeignKeyName,
            AuthorizedCatalog.KeyOf(r.FromTable.Schema.SchemaName, r.FromTable.TableName), r.FromColumnName,
            AuthorizedCatalog.KeyOf(r.ToTable.Schema.SchemaName, r.ToTable.TableName), r.ToColumnName));

        var catalog = new AuthorizedCatalog(connection.DatabaseName, catalogTables, catalogRelationships);
        cache.Set(CacheKey(connectionId), catalog, CacheDuration);
        return catalog;
    }

    public void Invalidate(int connectionId) => cache.Remove(CacheKey(connectionId));

    private static string CacheKey(int connectionId) => $"catalog:{connectionId}";

    public static string FormatType(DatabaseColumn c)
    {
        var type = c.DataType.ToLowerInvariant();
        return type switch
        {
            "varchar" or "char" or "varbinary" or "binary" => $"{type}({(c.MaxLength is -1 ? "max" : c.MaxLength?.ToString() ?? "")})",
            "nvarchar" or "nchar" => $"{type}({(c.MaxLength is -1 ? "max" : c.MaxLength is null ? "" : (c.MaxLength / 2).ToString())})",
            "decimal" or "numeric" => $"{type}({c.Precision},{c.Scale})",
            "datetime2" or "time" or "datetimeoffset" when c.Scale is not null and not 7 => $"{type}({c.Scale})",
            _ => type,
        };
    }
}

/// <summary>Evaluates name-based allow/deny rules (see <see cref="AllowedDatabaseObject"/>).</summary>
public sealed class AccessRuleEvaluator
{
    private readonly HashSet<string> _allowedSchemas;
    private readonly HashSet<string> _deniedSchemas;
    private readonly HashSet<string> _allowedTables;
    private readonly HashSet<string> _deniedTables;
    private readonly HashSet<string> _deniedColumns;

    public AccessRuleEvaluator(IEnumerable<AllowedDatabaseObject> rules)
    {
        var list = rules.ToList();
        static string T(string s, string? o) => $"{s}.{o}".ToLowerInvariant();
        bool Is(AllowedDatabaseObject r, ObjectAccessLevel level, params DbObjectType[] types) => r.AccessLevel == level && types.Contains(r.ObjectType);

        _allowedSchemas = list.Where(r => Is(r, ObjectAccessLevel.Allow, DbObjectType.Schema)).Select(r => r.SchemaName.ToLowerInvariant()).ToHashSet();
        _deniedSchemas = list.Where(r => Is(r, ObjectAccessLevel.Deny, DbObjectType.Schema)).Select(r => r.SchemaName.ToLowerInvariant()).ToHashSet();
        _allowedTables = list.Where(r => Is(r, ObjectAccessLevel.Allow, DbObjectType.Table, DbObjectType.View)).Select(r => T(r.SchemaName, r.ObjectName)).ToHashSet();
        _deniedTables = list.Where(r => Is(r, ObjectAccessLevel.Deny, DbObjectType.Table, DbObjectType.View)).Select(r => T(r.SchemaName, r.ObjectName)).ToHashSet();
        _deniedColumns = list.Where(r => Is(r, ObjectAccessLevel.Deny, DbObjectType.Column)).Select(r => $"{T(r.SchemaName, r.ObjectName)}.{r.ColumnName}".ToLowerInvariant()).ToHashSet();
    }

    public bool IsSchemaAllowed(string schema) => _allowedSchemas.Contains(schema.ToLowerInvariant());

    public bool IsTableAllowed(string schema, string table)
    {
        var key = $"{schema}.{table}".ToLowerInvariant();
        if (_deniedTables.Contains(key) || _deniedSchemas.Contains(schema.ToLowerInvariant())) return false;
        return _allowedTables.Contains(key) || _allowedSchemas.Contains(schema.ToLowerInvariant());
    }

    public bool IsColumnAllowed(string schema, string table, string column) =>
        IsTableAllowed(schema, table) && !_deniedColumns.Contains($"{schema}.{table}.{column}".ToLowerInvariant());
}
