using AIQueryGenerator.Domain.Enums;

namespace AIQueryGenerator.Application.Schema;

public sealed record CatalogColumn(string Name, string DataType, bool IsNullable, bool IsPrimaryKey, string? Description);

/// <summary>A base table or view known to the metadata cache, with the subset of columns the caller may use.</summary>
public sealed class CatalogTable
{
    public required int TableId { get; init; }
    public required string Schema { get; init; }
    public required string Name { get; init; }
    public required DbObjectType ObjectType { get; init; }
    public string? Description { get; init; }
    public bool IsAllowed { get; init; }

    /// <summary>All discovered columns keyed case-insensitively (SQL Server default collation semantics).</summary>
    public required IReadOnlyDictionary<string, CatalogColumn> AllColumns { get; init; }
    public required IReadOnlySet<string> AllowedColumns { get; init; }

    public string Key => AuthorizedCatalog.KeyOf(Schema, Name);
    public string QualifiedName => $"[{Schema}].[{Name}]";
    public bool HasRestrictedColumns => AllowedColumns.Count < AllColumns.Count;
}

public sealed record CatalogRelationship(string Name, string FromTable, string FromColumn, string ToTable, string ToColumn);

/// <summary>
/// The authorisation view of a connection's schema for one evaluation: every discovered object (so the validator
/// can tell "unknown" from "unauthorised") flagged with what is permitted.
/// </summary>
public sealed class AuthorizedCatalog
{
    private readonly Dictionary<string, CatalogTable> _tables;

    public AuthorizedCatalog(string databaseName, IEnumerable<CatalogTable> tables, IEnumerable<CatalogRelationship> relationships)
    {
        DatabaseName = databaseName;
        _tables = tables.ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);
        Relationships = relationships.ToList();
    }

    public string DatabaseName { get; }
    public IReadOnlyCollection<CatalogTable> Tables => _tables.Values;
    public IEnumerable<CatalogTable> AllowedTables => _tables.Values.Where(t => t.IsAllowed);
    public IReadOnlyList<CatalogRelationship> Relationships { get; }

    public static string KeyOf(string schema, string name) => $"{schema}.{name}".ToLowerInvariant();

    public CatalogTable? Find(string schema, string name) => _tables.GetValueOrDefault(KeyOf(schema, name));

    /// <summary>Relationships where both ends are permitted tables and both columns are permitted.</summary>
    public IEnumerable<CatalogRelationship> AllowedRelationships => Relationships.Where(r =>
    {
        var from = _tables.GetValueOrDefault(r.FromTable);
        var to = _tables.GetValueOrDefault(r.ToTable);
        return from is { IsAllowed: true } && to is { IsAllowed: true }
            && from.AllowedColumns.Contains(r.FromColumn) && to.AllowedColumns.Contains(r.ToColumn);
    });
}
