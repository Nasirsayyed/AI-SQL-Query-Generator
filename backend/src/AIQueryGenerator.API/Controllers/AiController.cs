using AIQueryGenerator.API.Infrastructure;
using AIQueryGenerator.Application.Ai;
using AIQueryGenerator.Application.Settings;
using AIQueryGenerator.Shared.Contracts;
using AIQueryGenerator.Shared.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AIQueryGenerator.API.Controllers;

/// <summary>AI endpoints. These generate text only — nothing here executes SQL.</summary>
[Route("api/ai")]
[EnableRateLimiting("ai")]
public sealed class AiController(IAiSqlService ai, IAiSettingsService settings) : ApiControllerBase
{
    /// <summary>Converts a natural-language question into a validated (not executed) read-only SQL query.</summary>
    [HttpPost("generate-sql")]
    [HasPermission(Permissions.QueryGenerate)]
    public async Task<ActionResult<ApiResponse<GenerateSqlResponse>>> GenerateSql(GenerateSqlRequest request, CancellationToken ct) =>
        Success(await ai.GenerateSqlAsync(request, ct));

    /// <summary>Explains submitted SQL step by step without executing it.</summary>
    [HttpPost("explain-sql")]
    [HasPermission(Permissions.QueryExplain)]
    public async Task<ActionResult<ApiResponse<ExplainSqlResponse>>> ExplainSql(ExplainSqlRequest request, CancellationToken ct) =>
        Success(await ai.ExplainSqlAsync(request, ct));

    /// <summary>Recommends a chart from result column metadata (no row data is sent to the AI).</summary>
    [HttpPost("suggest-chart")]
    [HasPermission(Permissions.ChartsView)]
    public async Task<ActionResult<ApiResponse<ChartSuggestionDto>>> SuggestChart(SuggestChartRequest request, CancellationToken ct) =>
        Success(await ai.SuggestChartAsync(request, ct));

    /// <summary>Suggests performance improvements for a query.</summary>
    [HttpPost("optimize-sql")]
    [HasPermission(Permissions.QueryExplain)]
    public async Task<ActionResult<ApiResponse<OptimizeSqlResponse>>> OptimizeSql(OptimizeSqlRequest request, CancellationToken ct) =>
        Success(await ai.OptimizeSqlAsync(request, ct));

    /// <summary>Whether an AI provider is configured, and which model is active.</summary>
    [HttpGet("status")]
    public async Task<ActionResult<ApiResponse<AiStatusDto>>> Status(CancellationToken ct) => Success(await settings.GetStatusAsync(ct));
}
