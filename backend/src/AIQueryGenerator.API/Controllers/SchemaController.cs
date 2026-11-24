using AIQueryGenerator.API.Infrastructure;
using AIQueryGenerator.Application.Schema;
using AIQueryGenerator.Shared.Contracts;
using AIQueryGenerator.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace AIQueryGenerator.API.Controllers;

/// <summary>Cached schema metadata, filtered to permitted objects for non-administrators.</summary>
[Route("api/schema/{connectionId:int}")]
[HasPermission(Permissions.SchemaView)]
public sealed class SchemaController(ISchemaService schema) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<SchemaOverviewDto>>> Overview(int connectionId, CancellationToken ct) =>
        Success(await schema.GetOverviewAsync(connectionId, ct));

    [HttpGet("tables")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TableSummaryDto>>>> Tables(int connectionId, [FromQuery] string? schemaName, CancellationToken ct) =>
        Success(await schema.GetTablesAsync(connectionId, schemaName, ct));

    /// <summary>Table detail. tableName may be "schema.table" or a bare table name.</summary>
    [HttpGet("tables/{tableName}")]
    public async Task<ActionResult<ApiResponse<TableDetailDto>>> Table(int connectionId, string tableName, CancellationToken ct) =>
        Success(await schema.GetTableAsync(connectionId, tableName, ct));

    [HttpGet("search")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SchemaSearchResultDto>>>> Search(int connectionId, [FromQuery] string q, CancellationToken ct) =>
        Success(await schema.SearchAsync(connectionId, q ?? string.Empty, ct));

    /// <summary>Permitted tables and columns for SQL editor autocomplete.</summary>
    [HttpGet("completions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CompletionTableDto>>>> Completions(int connectionId, CancellationToken ct) =>
        Success(await schema.GetCompletionsAsync(connectionId, ct));
}
