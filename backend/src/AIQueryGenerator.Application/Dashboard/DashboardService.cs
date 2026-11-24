using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Domain.Entities;
using AIQueryGenerator.Domain.Enums;
using AIQueryGenerator.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace AIQueryGenerator.Application.Dashboard;

public sealed record DashboardSummaryDto(
    int TotalQueries,
    int SuccessfulQueries,
    int FailedQueries,
    double AverageExecutionTimeMs,
    int QueriesGeneratedToday,
    int ActiveConnections,
    int TotalSavedQueries,
    bool IsGlobalScope);

public sealed record DailyTrendDto(DateOnly Date, int Total, int Succeeded, int Failed, int Generated);

public sealed record NamedCountDto(string Name, int Count);

public sealed record PerformanceDto(DateOnly Date, double AverageMs, int MaxMs, int Executions);

public sealed record ConnectionPerformanceDto(string ConnectionName, double AverageMs, int Executions);

public sealed record ExecutionPerformanceDto(IReadOnlyList<PerformanceDto> Daily, IReadOnlyList<ConnectionPerformanceDto> ByConnection);

public sealed record ActivityItemDto(long Id, string Title, string? UserName, string? ConnectionName, string Status, int? DurationMs, string? Detail, DateTime At);

public sealed record RecentActivityDto(
    IReadOnlyList<ActivityItemDto> LatestQueries,
    IReadOnlyList<ActivityItemDto> LatestFailures,
    IReadOnlyList<ActivityItemDto> MetadataRefreshes);

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<DailyTrendDto>> GetQueryTrendsAsync(int days, CancellationToken ct);
    Task<IReadOnlyList<NamedCountDto>> GetTopTablesAsync(int days, int top, CancellationToken ct);
    Task<ExecutionPerformanceDto> GetExecutionPerformanceAsync(int days, CancellationToken ct);
    Task<IReadOnlyList<NamedCountDto>> GetTopUsersAsync(int days, int top, CancellationToken ct);
    Task<IReadOnlyList<NamedCountDto>> GetCategoriesAsync(CancellationToken ct);
    Task<RecentActivityDto> GetRecentActivityAsync(CancellationToken ct);
}

/// <summary>
/// Analytics computed from real application-database records. Users with history.view_all see global figures;
/// everyone else sees figures for their own activity.
/// </summary>
public sealed class DashboardService(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock) : IDashboardService
{
    private const int MaxRowsForAggregation = 100_000;
    private bool Global => currentUser.HasPermission(Permissions.HistoryViewAll);
    private static readonly QueryExecutionStatus[] FailedStatuses = [QueryExecutionStatus.Failed, QueryExecutionStatus.TimedOut, QueryExecutionStatus.ValidationFailed];

    private IQueryable<QueryExecutionLog> Logs() =>
        Global ? db.QueryExecutionLogs.AsNoTracking() : db.QueryExecutionLogs.AsNoTracking().Where(l => l.UserId == currentUser.UserId);

    private IQueryable<QueryHistory> History() =>
        Global ? db.QueryHistory.AsNoTracking() : db.QueryHistory.AsNoTracking().Where(h => h.UserId == currentUser.UserId);

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct)
    {
        var today = clock.GetUtcNow().UtcDateTime.Date;
        var logs = Logs();
        var total = await logs.CountAsync(ct);
        var succeeded = await logs.CountAsync(l => l.ExecutionStatus == QueryExecutionStatus.Succeeded, ct);
        var failed = await logs.CountAsync(l => FailedStatuses.Contains(l.ExecutionStatus), ct);
        var avg = await logs.Where(l => l.ExecutionStatus == QueryExecutionStatus.Succeeded)
            .Select(l => (double?)l.ExecutionDurationMs).AverageAsync(ct) ?? 0;
        var generatedToday = await History().CountAsync(h => h.CreatedAt >= today && h.GeneratedSql != null, ct);
        var activeConnections = await db.DatabaseConnections.CountAsync(c => c.IsActive && !c.IsDeleted, ct);
        var saved = Global ? await db.SavedQueries.CountAsync(ct) : await db.SavedQueries.CountAsync(s => s.UserId == currentUser.UserId, ct);

        return new DashboardSummaryDto(total, succeeded, failed, Math.Round(avg, 1), generatedToday, activeConnections, saved, Global);
    }

    public async Task<IReadOnlyList<DailyTrendDto>> GetQueryTrendsAsync(int days, CancellationToken ct)
    {
        var (from, range) = Window(days);
        var logs = await Logs().Where(l => l.ExecutedAt >= from).OrderByDescending(l => l.ExecutionId)
            .Take(MaxRowsForAggregation).Select(l => new { l.ExecutedAt, l.ExecutionStatus }).ToListAsync(ct);
        var generated = await History().Where(h => h.CreatedAt >= from && h.GeneratedSql != null)
            .Take(MaxRowsForAggregation).Select(h => h.CreatedAt).ToListAsync(ct);

        var byDay = logs.GroupBy(l => DateOnly.FromDateTime(l.ExecutedAt)).ToDictionary(g => g.Key);
        var genByDay = generated.GroupBy(d => DateOnly.FromDateTime(d)).ToDictionary(g => g.Key, g => g.Count());

        return range.Select(d =>
        {
            byDay.TryGetValue(d, out var g);
            return new DailyTrendDto(d,
                g?.Count() ?? 0,
                g?.Count(x => x.ExecutionStatus == QueryExecutionStatus.Succeeded) ?? 0,
                g?.Count(x => FailedStatuses.Contains(x.ExecutionStatus)) ?? 0,
                genByDay.GetValueOrDefault(d));
        }).ToList();
    }

    public async Task<IReadOnlyList<NamedCountDto>> GetTopTablesAsync(int days, int top, CancellationToken ct)
    {
        var (from, _) = Window(days);
        var tables = await Logs().Where(l => l.ExecutedAt >= from && l.TablesUsed != null && l.ExecutionStatus == QueryExecutionStatus.Succeeded)
            .OrderByDescending(l => l.ExecutionId).Take(MaxRowsForAggregation).Select(l => l.TablesUsed!).ToListAsync(ct);
        return tables.SelectMany(t => t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => new NamedCountDto(g.Key, g.Count()))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Name).Take(Math.Clamp(top, 1, 50)).ToList();
    }

    public async Task<ExecutionPerformanceDto> GetExecutionPerformanceAsync(int days, CancellationToken ct)
    {
        var (from, range) = Window(days);
        var logs = await Logs().Where(l => l.ExecutedAt >= from && l.ExecutionStatus == QueryExecutionStatus.Succeeded)
            .OrderByDescending(l => l.ExecutionId).Take(MaxRowsForAggregation)
            .Select(l => new { l.ExecutedAt, l.ExecutionDurationMs, l.Connection.ConnectionName }).ToListAsync(ct);

        var byDay = logs.GroupBy(l => DateOnly.FromDateTime(l.ExecutedAt)).ToDictionary(g => g.Key);
        var daily = range.Select(d => byDay.TryGetValue(d, out var g)
            ? new PerformanceDto(d, Math.Round(g.Average(x => x.ExecutionDurationMs), 1), g.Max(x => x.ExecutionDurationMs), g.Count())
            : new PerformanceDto(d, 0, 0, 0)).ToList();
        var byConnection = logs.GroupBy(l => l.ConnectionName)
            .Select(g => new ConnectionPerformanceDto(g.Key, Math.Round(g.Average(x => x.ExecutionDurationMs), 1), g.Count()))
            .OrderByDescending(x => x.Executions).ToList();
        return new ExecutionPerformanceDto(daily, byConnection);
    }

    public async Task<IReadOnlyList<NamedCountDto>> GetTopUsersAsync(int days, int top, CancellationToken ct)
    {
        var (from, _) = Window(days);
        var rows = await Logs().Where(l => l.ExecutedAt >= from)
            .GroupBy(l => l.User.FullName)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(Math.Clamp(top, 1, 50)).ToListAsync(ct);
        return rows.Select(r => new NamedCountDto(r.Name, r.Count)).ToList();
    }

    public async Task<IReadOnlyList<NamedCountDto>> GetCategoriesAsync(CancellationToken ct)
    {
        // Popularity = executions of saved queries in each category, plus the number of saved queries in it.
        var runs = await Logs().Where(l => l.SavedQueryId != null)
            .Join(db.SavedQueries, l => l.SavedQueryId, s => s.SavedQueryId, (l, s) => s.Category ?? "Uncategorised")
            .GroupBy(c => c).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var saved = await (Global ? db.SavedQueries : db.SavedQueries.Where(s => s.UserId == currentUser.UserId || s.IsShared))
            .GroupBy(s => s.Category ?? "Uncategorised").Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);

        return runs.Concat(saved).GroupBy(x => x.Key)
            .Select(g => new NamedCountDto(g.Key, g.Sum(x => x.Count)))
            .OrderByDescending(x => x.Count).Take(10).ToList();
    }

    public async Task<RecentActivityDto> GetRecentActivityAsync(CancellationToken ct)
    {
        var latest = await History().Where(h => !h.IsDeleted).OrderByDescending(h => h.QueryId).Take(8)
            .Select(h => new ActivityItemDto(h.QueryId, h.QueryTitle, h.User.FullName, h.Connection.ConnectionName,
                h.ExecutionStatus.ToString(), h.ExecutionDurationMs, h.UserQuestion, h.UpdatedAt ?? h.CreatedAt))
            .ToListAsync(ct);

        var failures = await Logs().Where(l => FailedStatuses.Contains(l.ExecutionStatus)).OrderByDescending(l => l.ExecutionId).Take(8)
            .Select(l => new ActivityItemDto(l.ExecutionId, l.Query != null ? l.Query.QueryTitle : "Query", l.User.FullName,
                l.Connection.ConnectionName, l.ExecutionStatus.ToString(), l.ExecutionDurationMs, l.ErrorMessage, l.ExecutedAt))
            .ToListAsync(ct);

        var refreshes = await db.DatabaseConnections.AsNoTracking().Where(c => c.LastSchemaRefreshAt != null && !c.IsDeleted)
            .OrderByDescending(c => c.LastSchemaRefreshAt).Take(8)
            .Select(c => new ActivityItemDto(c.ConnectionId, c.ConnectionName, null, c.DatabaseName, c.Status.ToString(), null, null, c.LastSchemaRefreshAt!.Value))
            .ToListAsync(ct);

        return new RecentActivityDto(latest, failures, refreshes);
    }

    private (DateTime From, List<DateOnly> Range) Window(int days)
    {
        days = Math.Clamp(days, 1, 90);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var start = today.AddDays(-(days - 1));
        return (start.ToDateTime(TimeOnly.MinValue), Enumerable.Range(0, days).Select(start.AddDays).ToList());
    }
}
