using System.Diagnostics;
using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Application.Connections;
using AIQueryGenerator.Domain.Entities;
using AIQueryGenerator.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AIQueryGenerator.Application.Schema;

public interface ISchemaSyncService
{
    Task<SchemaRefreshResultDto> SynchronizeAsync(DatabaseConnection connection, bool full, CancellationToken ct);
}

/// <summary>
/// Merges a catalog snapshot from the target database into the metadata cache. Incremental refresh skips objects whose
/// sys.objects.modify_date is unchanged; a full refresh rewrites every object. Relationships are always rebuilt.
/// </summary>
public sealed class SchemaSyncService(
    IApplicationDbContext db,
    ISchemaDiscoveryService discovery,
    ISchemaCatalogService catalogService,
    TimeProvider clock) : ISchemaSyncService
{
    public async Task<SchemaRefreshResultDto> SynchronizeAsync(DatabaseConnection connection, bool full, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var snapshot = await discovery.DiscoverAsync(connection, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var connectionId = connection.ConnectionId;

        var schemas = await db.DatabaseSchemas.Where(s => s.ConnectionId == connectionId).ToListAsync(ct);
        var tables = await db.DatabaseTables.Where(t => t.ConnectionId == connectionId)
            .Include(t => t.Columns).Include(t => t.Indexes).ToListAsync(ct);

        // Relationships reference tables; drop them first and rebuild at the end.
        db.DatabaseRelationships.RemoveRange(await db.DatabaseRelationships.Where(r => r.ConnectionId == connectionId).ToListAsync(ct));

        var schemaMap = schemas.ToDictionary(s => s.SchemaName, StringComparer.OrdinalIgnoreCase);
        foreach (var name in snapshot.Tables.Select(t => t.Schema).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!schemaMap.TryGetValue(name, out var schema))
            {
                schema = new DatabaseSchema { ConnectionId = connectionId, SchemaName = name };
                db.DatabaseSchemas.Add(schema);
                schemaMap[name] = schema;
            }
            schema.LastSyncedAt = now;
        }

        var tableMap = tables.ToDictionary(t => Key(schemaOf(t), t.TableName), StringComparer.OrdinalIgnoreCase);
        string schemaOf(DatabaseTable t) => schemas.FirstOrDefault(s => s.SchemaId == t.SchemaId)?.SchemaName ?? string.Empty;

        int added = 0, updated = 0, unchanged = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolved = new Dictionary<string, DatabaseTable>(StringComparer.OrdinalIgnoreCase);

        foreach (var discovered in snapshot.Tables)
        {
            var key = Key(discovered.Schema, discovered.Name);
            seen.Add(key);

            if (!tableMap.TryGetValue(key, out var table))
            {
                table = new DatabaseTable { ConnectionId = connectionId, Schema = schemaMap[discovered.Schema], TableName = discovered.Name };
                db.DatabaseTables.Add(table);
                ApplyTable(table, discovered, now);
                added++;
            }
            else if (full || table.ObjectModifiedAt != discovered.ModifiedAt || table.ObjectType != discovered.ObjectType)
            {
                db.DatabaseColumns.RemoveRange(table.Columns);
                db.DatabaseIndexes.RemoveRange(table.Indexes);
                table.Columns.Clear();
                table.Indexes.Clear();
                ApplyTable(table, discovered, now);
                updated++;
            }
            else
            {
                table.RowCountEstimate = discovered.RowCountEstimate;
                table.LastSyncedAt = now;
                unchanged++;
            }

            resolved[key] = table;
        }

        var removedTables = tables.Where(t => !seen.Contains(Key(schemaOf(t), t.TableName))).ToList();
        db.DatabaseTables.RemoveRange(removedTables);
        var liveSchemas = snapshot.Tables.Select(t => t.Schema).ToHashSet(StringComparer.OrdinalIgnoreCase);
        db.DatabaseSchemas.RemoveRange(schemas.Where(s => !liveSchemas.Contains(s.SchemaName)));

        await db.SaveChangesAsync(ct); // assigns ids for new tables

        var relationships = 0;
        foreach (var fk in snapshot.ForeignKeys)
        {
            if (!resolved.TryGetValue(Key(fk.FromSchema, fk.FromTable), out var from) ||
                !resolved.TryGetValue(Key(fk.ToSchema, fk.ToTable), out var to)) continue;
            db.DatabaseRelationships.Add(new DatabaseRelationship
            {
                ConnectionId = connectionId,
                ForeignKeyName = fk.Name,
                FromTableId = from.TableId,
                FromColumnName = fk.FromColumn,
                ToTableId = to.TableId,
                ToColumnName = fk.ToColumn,
                OrdinalPosition = fk.Ordinal,
            });
            relationships++;
        }

        var tracked = await db.DatabaseConnections.FirstAsync(c => c.ConnectionId == connectionId, ct);
        tracked.LastSchemaRefreshAt = now;
        await db.SaveChangesAsync(ct);
        catalogService.Invalidate(connectionId);

        return new SchemaRefreshResultDto(
            liveSchemas.Count,
            snapshot.Tables.Count(t => t.ObjectType == DbObjectType.Table),
            snapshot.Tables.Count(t => t.ObjectType == DbObjectType.View),
            snapshot.Tables.Sum(t => t.Columns.Count),
            relationships, added, updated, removedTables.Count, unchanged, full, now, (int)sw.ElapsedMilliseconds);
    }

    private static void ApplyTable(DatabaseTable table, DiscoveredTable d, DateTime now)
    {
        table.ObjectType = d.ObjectType;
        table.Description = d.Description;
        table.ObjectModifiedAt = d.ModifiedAt;
        table.RowCountEstimate = d.RowCountEstimate;
        table.LastSyncedAt = now;
        foreach (var c in d.Columns)
        {
            table.Columns.Add(new DatabaseColumn
            {
                ColumnName = c.Name, DataType = c.DataType, MaxLength = c.MaxLength, Precision = c.Precision, Scale = c.Scale,
                IsNullable = c.IsNullable, IsPrimaryKey = c.IsPrimaryKey, IsIdentity = c.IsIdentity, IsComputed = c.IsComputed,
                OrdinalPosition = c.OrdinalPosition, Description = c.Description,
            });
        }
        foreach (var i in d.Indexes)
            table.Indexes.Add(new DatabaseIndex { IndexName = i.Name, IsUnique = i.IsUnique, IsPrimaryKey = i.IsPrimaryKey, Columns = i.Columns });
    }

    private static string Key(string schema, string table) => $"{schema}.{table}";
}
