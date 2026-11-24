using AIQueryGenerator.API.Infrastructure;
using AIQueryGenerator.Application.History;
using AIQueryGenerator.Application.Sql;
using AIQueryGenerator.Shared.Contracts;
using AIQueryGenerator.Shared.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AIQueryGenerator.API.Controllers;

[Route("api/query-history")]
[HasPermission(Permissions.HistoryView)]
public sealed class QueryHistoryController(IQueryHistoryService history) : ApiControllerBase
{
    /// <summary>Paged history (own records; all users' records with history.view_all).</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<QueryHistoryItemDto>>>> List([FromQuery] QueryHistoryQuery query, CancellationToken ct) =>
        Success(await history.ListAsync(query, ct));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<QueryHistoryDetailDto>>> Get(long id, CancellationToken ct) => Success(await history.GetAsync(id, ct));

    [HttpDelete("{id:long}")]
    [HasPermission(Permissions.HistoryDelete)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(long id, CancellationToken ct)
    {
        await history.DeleteAsync(id, ct);
        return Done("History record deleted.");
    }

    /// <summary>Re-runs a previous query after fresh authorisation and validation.</summary>
    [HttpPost("{id:long}/rerun")]
    [HasPermission(Permissions.QueryExecute)]
    [EnableRateLimiting("execute")]
    public async Task<ActionResult<ApiResponse<QueryResultDto>>> Rerun(long id, [FromBody] RerunQueryRequest? request, CancellationToken ct) =>
        Success(await history.RerunAsync(id, request ?? new RerunQueryRequest(), ct));
}
