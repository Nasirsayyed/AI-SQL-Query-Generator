using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Application.Schema;
using AIQueryGenerator.Domain.Entities;
using AIQueryGenerator.Domain.Enums;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AIQueryGenerator.Infrastructure.TargetDatabase;

/// <summary>
/// Reads schema metadata (never row data) from SQL Server catalog views: sys.tables, sys.views, sys.schemas,
/// sys.columns, sys.types, sys.foreign_keys, sys.foreign_key_columns, sys.indexes and INFORMATION_SCHEMA.TABLES.
/// Only objects visible to the read-only principal are returned.
/// </summary>
public sealed class SqlServerSchemaDiscoveryService(ISqlConnectionFactory factory, ILogger<SqlServerSchemaDiscoveryService> logger) : ISchemaDiscoveryService
{
    private const string ObjectsSql = """
        SELECT s.name AS SchemaName, o.name AS ObjectName, o.ObjectType, o.modify_date AS ModifiedAt,
               CAST(ep.value AS nvarchar(1000)) AS Description,
               (SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id = o.object_id AND p.index_id IN (0, 1)) AS RowCountEstimate
        FROM (
            SELECT t.object_id, t.schema_id, t.name, t.modify_date, 'U' AS ObjectType FROM sys.tables t WHERE t.is_ms_shipped = 0
            UNION ALL
            SELECT v.object_id, v.schema_id, v.name, v.modify_date, 'V' FROM sys.views v WHERE v.is_ms_shipped = 0
        ) o
        JOIN sys.schemas s ON s.schema_id = o.schema_id
        JOIN INFORMATION_SCHEMA.TABLES ist ON ist.TABLE_SCHEMA = s.name AND ist.TABLE_NAME = o.name
        LEFT JOIN sys.extended_properties ep ON ep.class = 1 AND ep.major_id = o.object_id AND ep.minor_id = 0 AND ep.name = 'MS_Description'
        WHERE s.name NOT IN ('sys', 'INFORMATION_SCHEMA', 'guest')
        ORDER BY s.name, o.name;
        """;

    private const string ColumnsSql = """
        SELECT s.name AS SchemaName, o.name AS ObjectName, c.name AS ColumnName, TYPE_NAME(c.system_type_id) AS DataType,
               CAST(c.max_length AS int) AS MaxLength, CAST(c.precision AS int) AS Precision, CAST(c.scale AS int) AS Scale,
               c.is_nullable AS IsNullable, c.is_identity AS IsIdentity, c.is_computed AS IsComputed, c.column_id AS OrdinalPosition,
               CAST(ep.value AS nvarchar(500)) AS Description,
               CAST(CASE WHEN EXISTS (
                   SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                   WHERE i.is_primary_key = 1 AND ic.object_id = c.object_id AND ic.column_id = c.column_id) THEN 1 ELSE 0 END AS bit) AS IsPrimaryKey
        FROM sys.columns c
        JOIN sys.objects o ON o.object_id = c.object_id AND o.type IN ('U', 'V') AND o.is_ms_shipped = 0
        JOIN sys.schemas s ON s.schema_id = o.schema_id
        LEFT JOIN sys.extended_properties ep ON ep.class = 1 AND ep.major_id = c.object_id AND ep.minor_id = c.column_id AND ep.name = 'MS_Description'
        WHERE s.name NOT IN ('sys', 'INFORMATION_SCHEMA', 'guest')
        ORDER BY s.name, o.name, c.column_id;
        """;

    private const string ForeignKeysSql = """
        SELECT fk.name AS Name, ps.name AS FromSchema, po.name AS FromTable, pc.name AS FromColumn,
               rs.name AS ToSchema, ro.name AS ToTable, rc.name AS ToColumn, fkc.constraint_column_id AS Ordinal
        FROM sys.foreign_keys fk
        JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        JOIN sys.objects po ON po.object_id = fkc.parent_object_id
        JOIN sys.schemas ps ON ps.schema_id = po.schema_id
        JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        JOIN sys.objects ro ON ro.object_id = fkc.referenced_object_id
        JOIN sys.schemas rs ON rs.schema_id = ro.schema_id
        JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        ORDER BY fk.name, fkc.constraint_column_id;
        """;

    private const string IndexesSql = """
        SELECT s.name AS SchemaName, o.name AS ObjectName, i.name AS IndexName, i.is_unique AS IsUnique, i.is_primary_key AS IsPrimaryKey,
               STUFF((SELECT ', ' + c.name
                      FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                      WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
                      ORDER BY ic.key_ordinal FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '') AS Columns
        FROM sys.indexes i
        JOIN sys.objects o ON o.object_id = i.object_id AND o.type IN ('U', 'V') AND o.is_ms_shipped = 0
        JOIN sys.schemas s ON s.schema_id = o.schema_id
        WHERE i.name IS NOT NULL AND i.is_hypothetical = 0
        ORDER BY s.name, o.name, i.name;
        """;

    public async Task<DiscoveredDatabase> DiscoverAsync(DatabaseConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var sql = factory.Create(connection);
            await sql.OpenAsync(cancellationToken);
            CommandDefinition Cmd(string text) => new(text, commandTimeout: 120, cancellationToken: cancellationToken);

            var objects = (await sql.QueryAsync<ObjectRow>(Cmd(ObjectsSql))).ToList();
            var columns = (await sql.QueryAsync<ColumnRow>(Cmd(ColumnsSql))).ToLookup(c => (c.SchemaName, c.ObjectName));
            var indexes = (await sql.QueryAsync<IndexRow>(Cmd(IndexesSql))).ToLookup(i => (i.SchemaName, i.ObjectName));
            var fks = (await sql.QueryAsync<DiscoveredForeignKey>(Cmd(ForeignKeysSql))).ToList();

            return new DiscoveredDatabase
            {
                DatabaseName = connection.DatabaseName,
                Tables = objects.Select(o => new DiscoveredTable
                {
                    Schema = o.SchemaName,
                    Name = o.ObjectName,
                    ObjectType = o.ObjectType == "V" ? DbObjectType.View : DbObjectType.Table,
                    ModifiedAt = o.ModifiedAt,
                    Description = o.Description,
                    RowCountEstimate = o.RowCountEstimate,
                    Columns = columns[(o.SchemaName, o.ObjectName)].Select(c => new DiscoveredColumn
                    {
                        Name = c.ColumnName, DataType = c.DataType, MaxLength = c.MaxLength, Precision = c.Precision, Scale = c.Scale,
                        IsNullable = c.IsNullable, IsIdentity = c.IsIdentity, IsComputed = c.IsComputed, IsPrimaryKey = c.IsPrimaryKey,
                        OrdinalPosition = c.OrdinalPosition, Description = c.Description,
                    }).ToList(),
                    Indexes = indexes[(o.SchemaName, o.ObjectName)].Select(i => new DiscoveredIndex
                    {
                        Name = i.IndexName, IsUnique = i.IsUnique, IsPrimaryKey = i.IsPrimaryKey, Columns = i.Columns ?? string.Empty,
                    }).ToList(),
                }).ToList(),
                ForeignKeys = fks,
            };
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "Schema discovery failed for connection {ConnectionId} (error {Number})", connection.ConnectionId, ex.Number);
            throw SqlErrorMapper.Map(ex, 120);
        }
    }

    private sealed record ObjectRow(string SchemaName, string ObjectName, string ObjectType, DateTime ModifiedAt, string? Description, long? RowCountEstimate);

    private sealed class ColumnRow
    {
        public string SchemaName { get; init; } = "";
        public string ObjectName { get; init; } = "";
        public string ColumnName { get; init; } = "";
        public string DataType { get; init; } = "";
        public int? MaxLength { get; init; }
        public int? Precision { get; init; }
        public int? Scale { get; init; }
        public bool IsNullable { get; init; }
        public bool IsIdentity { get; init; }
        public bool IsComputed { get; init; }
        public int OrdinalPosition { get; init; }
        public string? Description { get; init; }
        public bool IsPrimaryKey { get; init; }
    }

    private sealed class IndexRow
    {
        public string SchemaName { get; init; } = "";
        public string ObjectName { get; init; } = "";
        public string IndexName { get; init; } = "";
        public bool IsUnique { get; init; }
        public bool IsPrimaryKey { get; init; }
        public string? Columns { get; init; }
    }
}
