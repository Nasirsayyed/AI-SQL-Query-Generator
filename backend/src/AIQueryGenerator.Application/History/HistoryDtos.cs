using AIQueryGenerator.Application.Ai;
using AIQueryGenerator.Application.Sql;
using AIQueryGenerator.Domain.Enums;
using AIQueryGenerator.Shared.Contracts;

namespace AIQueryGenerator.Application.History;

public sealed class QueryHistoryQuery : PagedRequest
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int? UserId { get; set; }
    public int? ConnectionId { get; set; }
    public QueryExecutionStatus? Status { get; set; }
}

public sealed record QueryHistoryItemDto(
    long QueryId,
    string QueryTitle,
    string? UserQuestion,
    string? GeneratedSql,
    string? ExecutedSql,
    int ConnectionId,
    string ConnectionName,
    int UserId,
    string UserName,
    int? ExecutionDurationMs,
    int? ReturnedRows,
    QueryExecutionStatus ExecutionStatus,
    bool HasParameters,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record QueryHistoryDetailDto(
    QueryHistoryItemDto Item,
    string? Explanation,
    IReadOnlyList<string> TablesUsed,
    ChartSuggestionDto? ChartSuggestion,
    IReadOnlyList<QueryParameterDto> Parameters,
    string? PromptVersion,
    string? AiModel,
    string? ErrorMessage,
    bool IsOwner);

public sealed record RerunQueryRequest(IReadOnlyList<QueryParameterDto>? Parameters = null);
