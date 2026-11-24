using AIQueryGenerator.Domain.Enums;

namespace AIQueryGenerator.Application.Schema;

/// <summary>Raw catalog snapshot read from a target database by the discovery service.</summary>
public sealed class DiscoveredDatabase
{
    public string DatabaseName { get; init; } = string.Empty;
    public List<DiscoveredTable> Tables { get; init; } = new();
    public List<DiscoveredForeignKey> ForeignKeys { get; init; } = new();
}

public sealed class DiscoveredTable
{
    public string Schema { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DbObjectType ObjectType { get; init; }
    public DateTime ModifiedAt { get; init; }
    public long? RowCountEstimate { get; init; }
    public string? Description { get; init; }
    public List<DiscoveredColumn> Columns { get; init; } = new();
    public List<DiscoveredIndex> Indexes { get; init; } = new();
}

public sealed class DiscoveredColumn
{
    public string Name { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public int? MaxLength { get; init; }
    public int? Precision { get; init; }
    public int? Scale { get; init; }
    public bool IsNullable { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool IsIdentity { get; init; }
    public bool IsComputed { get; init; }
    public int OrdinalPosition { get; init; }
    public string? Description { get; init; }
}

public sealed class DiscoveredIndex
{
    public string Name { get; init; } = string.Empty;
    public bool IsUnique { get; init; }
    public bool IsPrimaryKey { get; init; }
    public string Columns { get; init; } = string.Empty;
}

public sealed class DiscoveredForeignKey
{
    public string Name { get; init; } = string.Empty;
    public string FromSchema { get; init; } = string.Empty;
    public string FromTable { get; init; } = string.Empty;
    public string FromColumn { get; init; } = string.Empty;
    public string ToSchema { get; init; } = string.Empty;
    public string ToTable { get; init; } = string.Empty;
    public string ToColumn { get; init; } = string.Empty;
    public int Ordinal { get; init; }
}
