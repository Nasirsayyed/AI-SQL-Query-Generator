using System.Text.Json.Serialization;

namespace AIQueryGenerator.Application.Sql;

public sealed record QueryParameterDto(string Name, SqlParameterType Type, string? Value, string? Description = null);

public sealed record ValidateSqlRequest(int ConnectionId, string Sql, IReadOnlyList<QueryParameterDto>? Parameters = null);

public sealed record SqlValidationResponse(
    bool IsValid,
    string Message,
    IReadOnlyList<SqlValidationIssue> Errors,
    IReadOnlyList<SqlValidationIssue> Warnings,
    IReadOnlyList<string> ReferencedObjects,
    IReadOnlyList<string> ReferencedParameters,
    string? ValidationToken,
    DateTime ValidatedAt);

public sealed record ExecuteSqlRequest
{
    public int ConnectionId { get; init; }
    public string Sql { get; init; } = string.Empty;
    public IReadOnlyList<QueryParameterDto> Parameters { get; init; } = [];

    /// <summary>Token from POST /api/sql/validate for this exact SQL and parameter set.</summary>
    public string? ValidationToken { get; init; }

    /// <summary>History record of the AI generation this execution belongs to, if any.</summary>
    public long? QueryHistoryId { get; init; }
    public string? QueryTitle { get; init; }
}

public sealed record QueryResultDto(
    long? QueryHistoryId,
    IReadOnlyList<ResultColumn> Columns,
    IReadOnlyList<object?[]> Rows,
    int RowCount,
    bool IsTruncated,
    int MaxRows,
    int DurationMs,
    DateTime ExecutedAt,
    int ConnectionId,
    string ConnectionName,
    IReadOnlyList<string> Warnings);

public sealed record FormatSqlRequest(string Sql);

public sealed record FormatSqlResponse(string Sql);

public sealed record ExplainPlanRequest(int ConnectionId, string Sql, IReadOnlyList<QueryParameterDto>? Parameters = null);

public sealed record ExplainPlanResponse(ExecutionPlanSummary Summary, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PlanXml);

/// <summary>Execution limits configured by administrators (appsettings "Execution").</summary>
public sealed class ExecutionOptions
{
    public const string SectionName = "Execution";

    /// <summary>Hard ceiling for per-connection MaxRows.</summary>
    public int MaxRowsCeiling { get; set; } = 10_000;
    public int MaxCommandTimeoutSeconds { get; set; } = 120;
    public int MaxCellLength { get; set; } = 4_000;
    public bool RequireValidationToken { get; set; } = true;
    public int ValidationTokenLifetimeMinutes { get; set; } = 30;
    public int MaxConcurrentQueriesPerUser { get; set; } = 2;
    public int MaxConcurrentQueriesGlobal { get; set; } = 20;
    public int MaxParameterValueLength { get; set; } = 4_000;
}
