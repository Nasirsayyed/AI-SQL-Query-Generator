using AIQueryGenerator.Domain.Enums;

namespace AIQueryGenerator.Application.Schema;

public sealed record SchemaOverviewDto(
    int ConnectionId,
    string DatabaseName,
    DateTime? LastSchemaRefreshAt,
    bool CanConfigure,
    IReadOnlyList<SchemaNodeDto> Schemas,
    IReadOnlyList<RelationshipDto> Relationships);

public sealed record SchemaNodeDto(string SchemaName, bool IsAllowed, IReadOnlyList<TableSummaryDto> Tables);

public sealed record TableSummaryDto(
    int TableId,
    string SchemaName,
    string TableName,
    DbObjectType ObjectType,
    bool IsAllowed,
    int ColumnCount,
    int AllowedColumnCount,
    long? RowCountEstimate,
    string? Description);

public sealed record ColumnDto(
    string ColumnName,
    string DataType,
    bool IsNullable,
    bool IsPrimaryKey,
    bool IsIdentity,
    bool IsComputed,
    bool IsAllowed,
    int OrdinalPosition,
    string? Description,
    string? References);

public sealed record IndexDto(string IndexName, bool IsUnique, bool IsPrimaryKey, string Columns);

public sealed record RelationshipDto(string Name, string FromTable, string FromColumn, string ToTable, string ToColumn);

public sealed record TableDetailDto(
    int TableId,
    string SchemaName,
    string TableName,
    DbObjectType ObjectType,
    bool IsAllowed,
    long? RowCountEstimate,
    string? Description,
    IReadOnlyList<ColumnDto> Columns,
    IReadOnlyList<IndexDto> Indexes,
    IReadOnlyList<RelationshipDto> Relationships,
    DateTime LastSyncedAt);

public sealed record SchemaSearchResultDto(string Kind, string SchemaName, string TableName, string? ColumnName, string? DataType, bool IsAllowed);

/// <summary>Column/table names for editor autocomplete (permitted objects only).</summary>
public sealed record CompletionTableDto(string SchemaName, string TableName, DbObjectType ObjectType, IReadOnlyList<CompletionColumnDto> Columns);

public sealed record CompletionColumnDto(string Name, string DataType);
