using AIQueryGenerator.Application.Schema;
using AIQueryGenerator.Application.Sql;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AIQueryGenerator.Infrastructure.Sql;

/// <summary>
/// Verifies that every referenced table/view and column is permitted for the caller. Resolution is deliberately
/// conservative: when an alias or unqualified column could refer to a restricted column of any referenced object, the
/// query is rejected. This can reject rare valid queries but never accepts access to a restricted column.
/// </summary>
internal sealed class ObjectAuthorizer(AuthorizedCatalog catalog, SqlValidationOptions options, QueryWalker walker, SqlValidationResult result)
{
    private readonly Dictionary<string, List<CatalogTable>> _exposedNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CatalogTable> _referencedTables = new();

    public void Authorize()
    {
        foreach (var reference in walker.TableReferences)
            ResolveTable(reference);

        if (result.Errors.Count > 0) return;

        foreach (var star in walker.StarExpressions)
            CheckStar(star);

        foreach (var column in walker.ColumnReferences)
            CheckColumn(column);

        result.ReferencedObjects.AddRange(_referencedTables.Select(t => $"{t.Schema}.{t.Name}").Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private void ResolveTable(NamedTableReference reference)
    {
        var name = reference.SchemaObject;
        var baseName = name.BaseIdentifier?.Value ?? string.Empty;

        if (name.ServerIdentifier is not null)
        {
            Error(SqlValidationCodes.CrossDatabase, $"Linked-server access ({name.ServerIdentifier.Value}) is not allowed.", reference);
            return;
        }

        if (name.DatabaseIdentifier is not null &&
            !string.Equals(name.DatabaseIdentifier.Value, catalog.DatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            Error(SqlValidationCodes.CrossDatabase, $"Cross-database access ({name.DatabaseIdentifier.Value}) is not allowed.", reference);
            return;
        }

        // Unqualified references to a CTE name resolve to the CTE.
        if (name.SchemaIdentifier is null && name.DatabaseIdentifier is null && walker.CteNames.Contains(baseName))
        {
            Expose(reference.Alias?.Value ?? baseName, null);
            return;
        }

        var schema = name.SchemaIdentifier?.Value ?? "dbo";
        if (!options.AllowSystemCatalogs && options.SystemSchemas.Contains(schema, StringComparer.OrdinalIgnoreCase))
        {
            Error(SqlValidationCodes.SystemCatalog, $"Access to system catalog '{schema}.{baseName}' is not allowed.", reference);
            return;
        }

        var table = catalog.Find(schema, baseName);
        if (table is null)
        {
            Error(SqlValidationCodes.UnknownObject, $"Unknown table or view '{schema}.{baseName}'. Only objects from the discovered schema may be used.", reference);
            return;
        }

        if (!table.IsAllowed)
        {
            Error(SqlValidationCodes.UnauthorizedObject, $"You are not authorised to query '{table.Schema}.{table.Name}'.", reference);
            return;
        }

        _referencedTables.Add(table);
        Expose(reference.Alias?.Value ?? table.Name, table);
        if (reference.Alias is null)
            Expose($"{table.Schema}.{table.Name}", table);
    }

    private void Expose(string name, CatalogTable? table)
    {
        if (!_exposedNames.TryGetValue(name, out var list))
            _exposedNames[name] = list = new List<CatalogTable>();
        if (table is not null) list.Add(table);
    }

    private void CheckStar(SelectStarExpression star)
    {
        IEnumerable<CatalogTable> targets = _referencedTables;
        if (star.Qualifier is { Identifiers.Count: > 0 } qualifier)
        {
            var key = string.Join('.', qualifier.Identifiers.TakeLast(2).Select(i => i.Value));
            targets = _exposedNames.TryGetValue(key, out var list)
                ? list
                : _exposedNames.GetValueOrDefault(qualifier.Identifiers[^1].Value) ?? new List<CatalogTable>();
        }

        foreach (var table in targets.Where(t => t.HasRestrictedColumns).DistinctBy(t => t.Key))
            Error(SqlValidationCodes.StarExposesRestricted,
                $"SELECT * cannot be used on '{table.Schema}.{table.Name}' because it contains restricted columns. List the permitted columns explicitly.", star);
    }

    private void CheckColumn(ColumnReferenceExpression column)
    {
        var parts = column.MultiPartIdentifier?.Identifiers;
        if (parts is null || parts.Count == 0) return;

        var columnName = parts[^1].Value;
        if (parts.Count >= 4 && !string.Equals(parts[0].Value, catalog.DatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            Error(SqlValidationCodes.CrossDatabase, "Cross-database column references are not allowed.", column);
            return;
        }

        if (parts.Count == 1)
        {
            CheckUnqualified(columnName, column);
            return;
        }

        var qualifier = parts.Count == 2 ? parts[0].Value : $"{parts[^3].Value}.{parts[^2].Value}";
        if (!_exposedNames.TryGetValue(qualifier, out var targets))
        {
            if (walker.VirtualTableAliases.Contains(qualifier)) return;
            Error(SqlValidationCodes.UnknownObject, $"The qualifier '{qualifier}' in '{qualifier}.{columnName}' does not match any table or alias in the query.", column);
            return;
        }

        if (targets.Count == 0) return; // CTE / derived table: its columns are validated where defined.

        var allowedSomewhere = false;
        foreach (var table in targets)
        {
            if (table.AllowedColumns.Contains(columnName)) { allowedSomewhere = true; continue; }
            if (table.AllColumns.ContainsKey(columnName))
            {
                Error(SqlValidationCodes.UnauthorizedColumn, $"You are not authorised to use column '{table.Name}.{columnName}'.", column);
                return;
            }
        }

        if (!allowedSomewhere && !walker.VirtualTableAliases.Contains(qualifier))
        {
            Error(SqlValidationCodes.UnknownColumn, $"Unknown column '{qualifier}.{columnName}'.", column);
            return;
        }

        result.ReferencedColumns.Add($"{qualifier}.{columnName}");
    }

    private void CheckUnqualified(string columnName, ColumnReferenceExpression column)
    {
        var restricted = _referencedTables.FirstOrDefault(t => t.AllColumns.ContainsKey(columnName) && !t.AllowedColumns.Contains(columnName));
        if (restricted is not null)
        {
            Error(SqlValidationCodes.UnauthorizedColumn, $"You are not authorised to use column '{restricted.Name}.{columnName}'.", column);
            return;
        }

        var known = _referencedTables.Any(t => t.AllowedColumns.Contains(columnName)) || walker.DefinedColumnNames.Contains(columnName);
        if (!known)
        {
            Error(SqlValidationCodes.UnknownColumn, $"Unknown column '{columnName}'. It does not exist in the referenced tables.", column);
            return;
        }

        result.ReferencedColumns.Add(columnName);
    }

    private void Error(string code, string message, TSqlFragment node)
    {
        if (!result.Errors.Any(e => e.Message == message))
            result.Error(code, message, node.StartLine, node.StartColumn);
    }
}
