using System.Text.Json;
using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Application.Ai;
using AIQueryGenerator.Application.Sql;
using AIQueryGenerator.Domain.Entities;
using AIQueryGenerator.Shared.Contracts;
using AIQueryGenerator.Shared.Exceptions;
using AIQueryGenerator.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace AIQueryGenerator.Application.History;

public interface IQueryHistoryService
{
    Task<PagedResult<QueryHistoryItemDto>> ListAsync(QueryHistoryQuery query, CancellationToken ct);
    Task<QueryHistoryDetailDto> GetAsync(long id, CancellationToken ct);
    Task DeleteAsync(long id, CancellationToken ct);
    Task<QueryResultDto> RerunAsync(long id, RerunQueryRequest request, CancellationToken ct);
}

public sealed class QueryHistoryService(
    IApplicationDbContext db,
    IQueryExecutionPipeline pipeline,
    IDataProtector protector,
    ICurrentUser currentUser,
    TimeProvider clock) : IQueryHistoryService
{
    private bool CanViewAll => currentUser.HasPermission(Permissions.HistoryViewAll);

    public async Task<PagedResult<QueryHistoryItemDto>> ListAsync(QueryHistoryQuery query, CancellationToken ct)
    {
        var q = db.QueryHistory.AsNoTracking().Where(h => !h.IsDeleted);
        if (!CanViewAll) q = q.Where(h => h.UserId == currentUser.UserId);
        else if (query.UserId is { } uid) q = q.Where(h => h.UserId == uid);

        if (query.ConnectionId is { } cid) q = q.Where(h => h.ConnectionId == cid);
        if (query.Status is { } st) q = q.Where(h => h.ExecutionStatus == st);
        if (query.From is { } from) q = q.Where(h => h.CreatedAt >= from);
        if (query.To is { } to) q = q.Where(h => h.CreatedAt < to.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(h => h.QueryTitle.Contains(s) || (h.UserQuestion != null && h.UserQuestion.Contains(s)) ||
                             (h.ExecutedSql != null && h.ExecutedSql.Contains(s)) || (h.GeneratedSql != null && h.GeneratedSql.Contains(s)));
        }

        q = query.SortBy?.ToLowerInvariant() switch
        {
            "querytitle" => query.Descending ? q.OrderByDescending(h => h.QueryTitle) : q.OrderBy(h => h.QueryTitle),
            "executiondurationms" => query.Descending ? q.OrderByDescending(h => h.ExecutionDurationMs) : q.OrderBy(h => h.ExecutionDurationMs),
            "returnedrows" => query.Descending ? q.OrderByDescending(h => h.ReturnedRows) : q.OrderBy(h => h.ReturnedRows),
            _ => query.Descending ? q.OrderByDescending(h => h.QueryId) : q.OrderBy(h => h.QueryId),
        };

        var total = await q.CountAsync(ct);
        var items = await q.Skip(query.Skip).Take(query.PageSize).Select(Projection).ToListAsync(ct);
        return new PagedResult<QueryHistoryItemDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<QueryHistoryDetailDto> GetAsync(long id, CancellationToken ct)
    {
        var history = await LoadVisibleAsync(id, ct);
        var item = await db.QueryHistory.AsNoTracking().Where(h => h.QueryId == id).Select(Projection).FirstAsync(ct);
        var isOwner = history.UserId == currentUser.UserId;

        return new QueryHistoryDetailDto(
            item,
            history.Explanation,
            history.TablesUsed?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [],
            history.ChartSuggestionJson is null ? null : JsonSerializer.Deserialize<ChartSuggestionDto>(history.ChartSuggestionJson),
            isOwner ? ReadParameters(history.ParametersProtected) : [],
            history.PromptVersion,
            history.AiModel,
            history.ErrorMessage,
            isOwner);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        var history = await db.QueryHistory.FirstOrDefaultAsync(h => h.QueryId == id && !h.IsDeleted, ct)
                      ?? throw new NotFoundException("Query history record not found.");
        if (history.UserId != currentUser.UserId && !CanViewAll)
            throw new NotFoundException("Query history record not found.");

        history.IsDeleted = true;
        history.DeletedAt = clock.GetUtcNow().UtcDateTime;
        history.ParametersProtected = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task<QueryResultDto> RerunAsync(long id, RerunQueryRequest request, CancellationToken ct)
    {
        var history = await LoadVisibleAsync(id, ct);
        if (history.UserId != currentUser.UserId)
            throw new ForbiddenException("Only the owner of a history record can re-run it.");

        var sql = history.ExecutedSql ?? history.GeneratedSql ?? throw new BusinessRuleException("This history record has no SQL to run.");
        if (!currentUser.HasPermission(Permissions.QueryEditSql) &&
            (history.GeneratedSql is null || SqlQueryService.Normalize(history.GeneratedSql) != SqlQueryService.Normalize(sql)))
            throw new ForbiddenException("Your role can only re-run unmodified AI-generated SQL.");

        // Fresh authorisation and validation happen inside the pipeline.
        return await pipeline.ExecuteAsync(new PipelineRequest
        {
            ConnectionId = history.ConnectionId,
            Sql = sql,
            Parameters = request.Parameters ?? ReadParameters(history.ParametersProtected),
            QueryHistoryId = history.QueryId,
            QueryTitle = history.QueryTitle,
            UserQuestion = history.UserQuestion,
            GeneratedSql = history.GeneratedSql,
        }, ct);
    }

    private async Task<QueryHistory> LoadVisibleAsync(long id, CancellationToken ct)
    {
        var history = await db.QueryHistory.AsNoTracking().FirstOrDefaultAsync(h => h.QueryId == id && !h.IsDeleted, ct)
                      ?? throw new NotFoundException("Query history record not found.");
        if (history.UserId != currentUser.UserId && !CanViewAll)
            throw new NotFoundException("Query history record not found.");
        return history;
    }

    private IReadOnlyList<QueryParameterDto> ReadParameters(string? protectedValue)
    {
        var json = protector.Unprotect(protectedValue);
        return json is null ? [] : JsonSerializer.Deserialize<List<QueryParameterDto>>(json) ?? [];
    }

    private static readonly System.Linq.Expressions.Expression<Func<QueryHistory, QueryHistoryItemDto>> Projection = h =>
        new QueryHistoryItemDto(h.QueryId, h.QueryTitle, h.UserQuestion, h.GeneratedSql, h.ExecutedSql, h.ConnectionId,
            h.Connection.ConnectionName, h.UserId, h.User.FullName, h.ExecutionDurationMs, h.ReturnedRows, h.ExecutionStatus,
            h.ParametersProtected != null, h.CreatedAt, h.UpdatedAt);
}
