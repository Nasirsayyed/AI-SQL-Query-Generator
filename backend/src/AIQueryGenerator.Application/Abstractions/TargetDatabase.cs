using AIQueryGenerator.Application.Schema;
using AIQueryGenerator.Application.Sql;
using AIQueryGenerator.Domain.Entities;

namespace AIQueryGenerator.Application.Abstractions;

public sealed record ConnectionTestResult(
    bool Success,
    bool IsLeastPrivilege,
    string Message,
    string? ServerVersion,
    string? LoginName,
    IReadOnlyList<string> PrivilegeWarnings);

public sealed record QueryExecutionRequest(
    DatabaseConnection Connection,
    string Sql,
    IReadOnlyList<BoundParameter> Parameters,
    int MaxRows,
    int CommandTimeoutSeconds,
    int MaxCellLength);

public sealed record BoundParameter(string Name, SqlParameterType Type, object? Value);

/// <summary>Read-only access to a target business database. Implementations must only run validated SQL.</summary>
public interface ITargetDatabaseClient
{
    Task<ConnectionTestResult> TestConnectionAsync(DatabaseConnection connection, CancellationToken cancellationToken);
    Task<QueryResultData> ExecuteReadOnlyAsync(QueryExecutionRequest request, CancellationToken cancellationToken);
    Task<string> GetEstimatedPlanXmlAsync(QueryExecutionRequest request, CancellationToken cancellationToken);
}

public interface ISchemaDiscoveryService
{
    Task<DiscoveredDatabase> DiscoverAsync(DatabaseConnection connection, CancellationToken cancellationToken);
}

public interface ISqlValidator
{
    SqlValidationResult Validate(string sql, SqlValidationContext context);
}

public sealed record SqlFormatResult(string Sql, int CommentsRemoved);

public interface ISqlFormatter
{
    /// <summary>
    /// Re-generates the SQL text from its syntax tree. Comments cannot be preserved; the number of comments that the
    /// formatted text drops is reported so callers can confirm first. Throws when the SQL cannot be parsed.
    /// </summary>
    SqlFormatResult Format(string sql);
}

/// <summary>Summarises an estimated execution plan XML document.</summary>
public interface IExecutionPlanParser
{
    ExecutionPlanSummary Parse(string planXml);
}
