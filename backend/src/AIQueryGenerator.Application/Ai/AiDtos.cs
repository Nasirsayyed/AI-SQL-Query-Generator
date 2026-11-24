using AIQueryGenerator.Application.Sql;

namespace AIQueryGenerator.Application.Ai;

public sealed record GenerateSqlRequest(int ConnectionId, string Question, string? SchemaName = null);

public sealed record ChartSuggestionDto(
    bool Recommended,
    string? ChartType,
    string? XAxis,
    string? YAxis,
    string? Aggregation,
    string? Title,
    string? Reason,
    string Source);

public sealed record GenerateSqlResponse(
    bool Success,
    bool NeedsClarification,
    string? ClarificationQuestion,
    string? SqlQuery,
    string QueryTitle,
    string Explanation,
    IReadOnlyList<string> TablesUsed,
    IReadOnlyList<string> ColumnsUsed,
    IReadOnlyList<QueryParameterDto> Parameters,
    IReadOnlyList<string> Warnings,
    ChartSuggestionDto? ChartSuggestion,
    SqlValidationResponse? Validation,
    long? QueryHistoryId,
    string Model,
    string PromptVersion);

public sealed record ExplainSqlRequest(int ConnectionId, string Sql, bool BeginnerFriendly = true);

public sealed record ExplanationStepDto(string Clause, string Description);

public sealed record ExplainSqlResponse(
    string Summary,
    IReadOnlyList<ExplanationStepDto> Steps,
    string? BeginnerExplanation,
    IReadOnlyList<string> Notes,
    string Model,
    string PromptVersion);

public sealed record SuggestChartRequest(string? Question, IReadOnlyList<ResultColumn> Columns, int RowCount);

public sealed record OptimizeSqlRequest(int ConnectionId, string Sql);

public sealed record OptimizationSuggestionDto(string Title, string Description, string Severity);

public sealed record OptimizeSqlResponse(
    string Summary,
    IReadOnlyList<OptimizationSuggestionDto> Suggestions,
    string? OptimizedSql,
    SqlValidationResponse? OptimizedSqlValidation,
    string Model,
    string PromptVersion);

/// <summary>AI behaviour settings (appsettings "Ai").</summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public int MaxQuestionLength { get; set; } = 2000;

    /// <summary>How many times the model may repair SQL that fails backend validation.</summary>
    public int MaxRepairAttempts { get; set; } = 1;
}
