using AIQueryGenerator.API.Infrastructure;
using AIQueryGenerator.Application.Dashboard;
using AIQueryGenerator.Shared.Contracts;
using AIQueryGenerator.Shared.Exceptions;
using AIQueryGenerator.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace AIQueryGenerator.API.Controllers;

[Route("api/dashboard")]
[HasPermission(Permissions.DashboardView)]
public sealed class DashboardController(IDashboardService dashboard, Application.Abstractions.ICurrentUser user) : ApiControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<DashboardSummaryDto>>> Summary(CancellationToken ct) => Success(await dashboard.GetSummaryAsync(ct));

    [HttpGet("query-trends")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DailyTrendDto>>>> Trends([FromQuery] int days = 30, CancellationToken ct = default) =>
        Success(await dashboard.GetQueryTrendsAsync(days, ct));

    [HttpGet("top-tables")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<NamedCountDto>>>> TopTables([FromQuery] int days = 30, [FromQuery] int top = 10, CancellationToken ct = default) =>
        Success(await dashboard.GetTopTablesAsync(days, top, ct));

    [HttpGet("execution-performance")]
    public async Task<ActionResult<ApiResponse<ExecutionPerformanceDto>>> Performance([FromQuery] int days = 30, CancellationToken ct = default) =>
        Success(await dashboard.GetExecutionPerformanceAsync(days, ct));

    /// <summary>Most active users (requires history.view_all).</summary>
    [HttpGet("top-users")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<NamedCountDto>>>> TopUsers([FromQuery] int days = 30, [FromQuery] int top = 10, CancellationToken ct = default)
    {
        if (!user.HasPermission(Permissions.HistoryViewAll)) throw new ForbiddenException();
        return Success(await dashboard.GetTopUsersAsync(days, top, ct));
    }

    [HttpGet("categories")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<NamedCountDto>>>> Categories(CancellationToken ct) => Success(await dashboard.GetCategoriesAsync(ct));

    [HttpGet("recent-activity")]
    public async Task<ActionResult<ApiResponse<RecentActivityDto>>> RecentActivity(CancellationToken ct) => Success(await dashboard.GetRecentActivityAsync(ct));
}
