namespace AIQueryGenerator.Application.Ai;

/// <summary>Raw structured output of the SQL generation prompt. Treated as untrusted input.</summary>
internal sealed class SqlGenerationOutput
{
    public bool Success { get; set; }
    public bool NeedsClarification { get; set; }
    public string? ClarificationQuestion { get; set; }
    public string? SqlQuery { get; set; }
    public string? QueryTitle { get; set; }
    public string? Explanation { get; set; }
    public List<string>? TablesUsed { get; set; }
    public List<string>? ColumnsUsed { get; set; }
    public List<AiParameter>? Parameters { get; set; }
    public List<string>? Warnings { get; set; }
    public AiChart? ChartSuggestion { get; set; }
}

internal sealed class AiParameter
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Value { get; set; }
    public string? Description { get; set; }
}

internal sealed class AiChart
{
    public bool Recommended { get; set; }
    public string? ChartType { get; set; }
    public string? XAxis { get; set; }
    public string? YAxis { get; set; }
    public string? Aggregation { get; set; }
    public string? Title { get; set; }
    public string? Reason { get; set; }
}

internal sealed class ExplanationOutput
{
    public string? Summary { get; set; }
    public List<ExplanationStep>? Steps { get; set; }
    public string? BeginnerExplanation { get; set; }
    public List<string>? Notes { get; set; }
}

internal sealed class ExplanationStep
{
    public string? Clause { get; set; }
    public string? Description { get; set; }
}

internal sealed class OptimizationOutput
{
    public string? Summary { get; set; }
    public List<OptimizationSuggestion>? Suggestions { get; set; }
    public string? OptimizedSql { get; set; }
}

internal sealed class OptimizationSuggestion
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Severity { get; set; }
}
