using AIQueryGenerator.API.Infrastructure;
using AIQueryGenerator.Application.SavedQueries;
using AIQueryGenerator.Application.Sql;
using AIQueryGenerator.Shared.Contracts;
using AIQueryGenerator.Shared.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AIQueryGenerator.API.Controllers;

[Route("api/saved-queries")]
public sealed class SavedQueriesController(ISavedQueryService saved) : ApiControllerBase
{
    /// <summary>Own saved queries plus queries shared with the caller.</summary>
    [HttpGet]
    [HasPermission(Permissions.SharedReportsView)]
    public async Task<ActionResult<ApiResponse<PagedResult<SavedQueryDto>>>> List([FromQuery] SavedQueryQuery query, CancellationToken ct) =>
        Success(await saved.ListAsync(query, ct));

    [HttpGet("categories")]
    [HasPermission(Permissions.SharedReportsView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<string>>>> Categories(CancellationToken ct) => Success(await saved.ListCategoriesAsync(ct));

    [HttpGet("{id:int}")]
    [HasPermission(Permissions.SharedReportsView)]
    public async Task<ActionResult<ApiResponse<SavedQueryDto>>> Get(int id, CancellationToken ct) => Success(await saved.GetAsync(id, ct));

    [HttpPost]
    [HasPermission(Permissions.SavedQueriesManage)]
    public async Task<ActionResult<ApiResponse<SavedQueryDto>>> Create(SaveQueryRequest request, CancellationToken ct)
    {
        var created = await saved.CreateAsync(request, ct);
        return Created($"/api/saved-queries/{created.SavedQueryId}", created);
    }

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.SavedQueriesManage)]
    public async Task<ActionResult<ApiResponse<SavedQueryDto>>> Update(int id, SaveQueryRequest request, CancellationToken ct) =>
        Success(await saved.UpdateAsync(id, request, ct));

    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.SavedQueriesManage)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(int id, CancellationToken ct)
    {
        await saved.DeleteAsync(id, ct);
        return Done("Saved query deleted.");
    }

    /// <summary>Toggles the caller's favourite flag.</summary>
    [HttpPost("{id:int}/favorite")]
    [HasPermission(Permissions.SharedReportsView)]
    public async Task<ActionResult<ApiResponse<FavoriteResultDto>>> Favorite(int id, CancellationToken ct) => Success(await saved.ToggleFavoriteAsync(id, ct));

    /// <summary>Runs a saved or shared query. It is revalidated against current permissions every time.</summary>
    [HttpPost("{id:int}/run")]
    [HasPermission(Permissions.SharedReportsView)]
    [EnableRateLimiting("execute")]
    public async Task<ActionResult<ApiResponse<QueryResultDto>>> Run(int id, [FromBody] RunSavedQueryRequest? request, CancellationToken ct) =>
        Success(await saved.RunAsync(id, request ?? new RunSavedQueryRequest(), ct));
}
