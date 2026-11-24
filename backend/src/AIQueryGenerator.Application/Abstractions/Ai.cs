namespace AIQueryGenerator.Application.Abstractions;

/// <summary>A request for a JSON object that must conform to <see cref="JsonSchema"/>.</summary>
public sealed record AiJsonCompletionRequest(
    string SystemPrompt,
    string UserPrompt,
    string SchemaName,
    string JsonSchema,
    double? Temperature = null,
    int? MaxOutputTokens = null);

public sealed record AiCompletionResult(string Content, string Model, int? PromptTokens, int? CompletionTokens);

/// <summary>Provider-neutral chat completion abstraction. Implementations live in Infrastructure.</summary>
public interface IAiCompletionClient
{
    string ProviderName { get; }
    string Model { get; }
    int MaxSchemaTables { get; }
    Task<AiCompletionResult> CompleteJsonAsync(AiJsonCompletionRequest request, CancellationToken cancellationToken);
}

public interface IAiClientFactory
{
    /// <summary>Returns a client for the active provider configuration or throws AI_NOT_CONFIGURED.</summary>
    Task<IAiCompletionClient> CreateAsync(CancellationToken cancellationToken);
}

public sealed record PromptTemplate(string Name, string Version, string Text)
{
    public string Id => $"{Name}.{Version}";
}

public interface IPromptProvider
{
    PromptTemplate Get(string name);
}

public static class PromptNames
{
    public const string SqlGeneration = "sql-generation";
    public const string SqlExplanation = "sql-explanation";
    public const string ChartRecommendation = "chart-recommendation";
    public const string QueryOptimization = "query-optimization";
}
