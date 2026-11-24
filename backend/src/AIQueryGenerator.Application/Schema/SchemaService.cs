using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Application.Connections;
using AIQueryGenerator.Shared.Exceptions;
using AIQueryGenerator.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace AIQueryGenerator.Application.Schema;

public interface ISchemaService
{
    Task<SchemaOverviewDto> GetOverviewAsync(int connectionId, CancellationToken ct);
    Task<IReadOnlyList<TableSummaryDto>> GetTablesAsync(int connectionId, string? schema, CancellationToken ct);
    Task<TableDetailDto> GetTableAsync(int connectionId, string tableName, CancellationToken ct);
    Task<IReadOnlyList<SchemaSearchResultDto>> SearchAsync(int connectionId, string query, CancellationToken ct);
    Task<IReadOnlyList<CompletionTableDto>> GetCompletionsAsync(int connectionId, CancellationToken ct);
}

/// <summary>
/// Read access to cached metadata. Users with schema.configure see every object with its permission flag;
/// all other users see only permitted objects and columns.
/// </summary>
public sealed class SchemaService(
    IApplicationDbContext db,
    IConnectionAccessService access,
    ISchemaCatalogService catalogs,
    ICurrentUser currentUser) : ISchemaService
{
    private bool CanConfigure => currentUser.HasPermission(Permissions.SchemaConfigure);

    public async Task<SchemaOverviewDto> GetOverviewAsync(int connectionId, CancellationToken ct)
    {
        var connection = await access.GetViewableConnectionAsync(connectionId, ct);
        var catalog = await catalogs.GetCatalogAsync(connectionId, ct);
        var rules = new AccessRuleEvaluator(await db.AllowedDatabaseObjects.AsNoTracking().Where(a => a.ConnectionId == connectionId).ToListAsync(ct));

        var visible = catalog.Tables.Where(t => CanConfigure || t.IsAllowed).ToList();
        var schemas = visible.GroupBy(t => t.Schema, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key)
            .Select(g => new SchemaNodeDto(g.Key, rules.IsSchemaAllowed(g.Key),
                g.OrderBy(t => t.ObjectType).ThenBy(t => t.Name).Select(ToSummary).ToList()))
            .ToList();

        var relationships = (CanConfigure ? catalog.Relationships : catalog.AllowedRelationships)
            .Select(r => ToRelationshipDto(catalog, r)).ToList();

        return new SchemaOverviewDto(connectionId, connection.DatabaseName, connection.LastSchemaRefreshAt, CanConfigure, schemas, relationships);
    }

    public async Task<IReadOnlyList<TableSummaryDto>> GetTablesAsync(int connectionId, string? schema, CancellationToken ct)
    {
        await access.GetViewableConnectionAsync(connectionId, ct);
        var catalog = await catalogs.GetCatalogAsync(connectionId, ct);
        return catalog.Tables
            .Where(t => CanConfigure || t.IsAllowed)
            .Where(t => string.IsNullOrWhiteSpace(schema) || string.Equals(t.Schema, schema, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Schema).ThenBy(t => t.Name)
            .Select(ToSummary).ToList();
    }

    public async Task<TableDetailDto> GetTableAsync(int connectionId, string tableName, CancellationToken ct)
    {
        await access.GetViewableConnectionAsync(connectionId, ct);
        var catalog = await catalogs.GetCatalogAsync(connectionId, ct);

        var parts = tableName.Split('.', 2);
        var table = parts.Length == 2
            ? catalog.Find(parts[0], parts[1])
            : catalog.Find("dbo", tableName) ?? catalog.Tables.FirstOrDefault(t => string.Equals(t.Name, tableName, StringComparison.OrdinalIgnoreCase));

        if (table is null || (!table.IsAllowed && !CanConfigure))
            throw new NotFoundException("Table not found.");

        var entity = await db.DatabaseTables.AsNoTracking().Include(t => t.Columns).Include(t => t.Indexes)
            .FirstAsync(t => t.TableId == table.TableId, ct);

        var related = (CanConfigure ? catalog.Relationships : catalog.AllowedRelationships)
            .Where(r => r.FromTable == table.Key || r.ToTable == table.Key).ToList();
        var outgoing = related.Where(r => r.FromTable == table.Key)
            .GroupBy(r => r.FromColumn, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => Display(catalog, g.First().ToTable) + "." + g.First().ToColumn, StringComparer.OrdinalIgnoreCase);

        var columns = entity.Columns.OrderBy(c => c.OrdinalPosition)
            .Where(c => CanConfigure || table.AllowedColumns.Contains(c.ColumnName))
            .Select(c => new ColumnDto(c.ColumnName, SchemaCatalogService.FormatType(c), c.IsNullable, c.IsPrimaryKey, c.IsIdentity,
                c.IsComputed, table.AllowedColumns.Contains(c.ColumnName), c.OrdinalPosition, c.Description, outgoing.GetValueOrDefault(c.ColumnName)))
            .ToList();

        var indexes = entity.Indexes.Select(i => new IndexDto(i.IndexName, i.IsUnique, i.IsPrimaryKey, i.Columns)).ToList();

        return new TableDetailDto(table.TableId, table.Schema, table.Name, table.ObjectType, table.IsAllowed, entity.RowCountEstimate,
            table.Description, columns, indexes, related.Select(r => ToRelationshipDto(catalog, r)).ToList(), entity.LastSyncedAt);
    }

    public async Task<IReadOnlyList<SchemaSearchResultDto>> SearchAsync(int connectionId, string query, CancellationToken ct)
    {
        await access.GetViewableConnectionAsync(connectionId, ct);
        var catalog = await catalogs.GetCatalogAsync(connectionId, ct);
        var q = query.Trim();
        if (q.Length == 0) return [];

        var results = new List<SchemaSearchResultDto>();
        foreach (var t in catalog.Tables.Where(t => CanConfigure || t.IsAllowed))
        {
            if (t.Schema.Contains(q, StringComparison.OrdinalIgnoreCase) || t.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                results.Add(new SchemaSearchResultDto(t.ObjectType.ToString(), t.Schema, t.Name, null, null, t.IsAllowed));

            foreach (var c in t.AllColumns.Values.Where(c => c.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
            {
                var allowed = t.AllowedColumns.Contains(c.Name);
                if (allowed || CanConfigure)
                    results.Add(new SchemaSearchResultDto("Column", t.Schema, t.Name, c.Name, c.DataType, allowed));
            }
        }

        return results.OrderBy(r => r.Kind == "Column").ThenBy(r => r.TableName).ThenBy(r => r.ColumnName).Take(200).ToList();
    }

    public async Task<IReadOnlyList<CompletionTableDto>> GetCompletionsAsync(int connectionId, CancellationToken ct)
    {
        await access.GetViewableConnectionAsync(connectionId, ct);
        var catalog = await catalogs.GetCatalogAsync(connectionId, ct);
        return catalog.AllowedTables.OrderBy(t => t.Schema).ThenBy(t => t.Name)
            .Select(t => new CompletionTableDto(t.Schema, t.Name, t.ObjectType,
                t.AllColumns.Values.Where(c => t.AllowedColumns.Contains(c.Name)).Select(c => new CompletionColumnDto(c.Name, c.DataType)).ToList()))
            .ToList();
    }

    private static TableSummaryDto ToSummary(CatalogTable t) =>
        new(t.TableId, t.Schema, t.Name, t.ObjectType, t.IsAllowed, t.AllColumns.Count, t.AllowedColumns.Count, t.RowCountEstimate, t.Description);

    private static RelationshipDto ToRelationshipDto(AuthorizedCatalog catalog, CatalogRelationship r) =>
        new(r.Name, Display(catalog, r.FromTable), r.FromColumn, Display(catalog, r.ToTable), r.ToColumn);

    private static string Display(AuthorizedCatalog catalog, string key)
    {
        var parts = key.Split('.', 2);
        var t = catalog.Find(parts[0], parts[1]);
        return t is null ? key : $"{t.Schema}.{t.Name}";
    }
}
