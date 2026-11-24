using System.Text.Json;
using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Application.Connections;
using AIQueryGenerator.Application.Sql;
using AIQueryGenerator.Domain.Entities;
using AIQueryGenerator.Shared.Contracts;
using AIQueryGenerator.Shared.Exceptions;
using AIQueryGenerator.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace AIQueryGenerator.Application.SavedQueries;

public interface ISavedQueryService
{
    Task<PagedResult<SavedQueryDto>> ListAsync(SavedQueryQuery query, CancellationToken ct);
    Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken ct);
    Task<SavedQueryDto> GetAsync(int id, CancellationToken ct);
    Task<SavedQueryDto> CreateAsync(SaveQueryRequest request, CancellationToken ct);
    Task<SavedQueryDto> UpdateAsync(int id, SaveQueryRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    Task<FavoriteResultDto> ToggleFavoriteAsync(int id, CancellationToken ct);
    Task<QueryResultDto> RunAsync(int id, RunSavedQueryRequest request, CancellationToken ct);
}

public sealed class SavedQueryService(
    IApplicationDbContext db,
    IQueryExecutionPipeline pipeline,
    IConnectionAccessService access,
    IDataProtector protector,
    ICurrentUser currentUser,
    IAuditService audit) : ISavedQueryService
{
    private bool CanSeeShared => currentUser.HasPermission(Permissions.SharedReportsView);

    public async Task<PagedResult<SavedQueryDto>> ListAsync(SavedQueryQuery query, CancellationToken ct)
    {
        var uid = currentUser.UserId;
        var accessible = await access.GetAccessibleConnectionIdsAsync(ct);
        var q = db.SavedQueries.AsNoTracking().Where(s => accessible.Contains(s.ConnectionId));

        q = query.Scope?.ToLowerInvariant() switch
        {
            "mine" => q.Where(s => s.UserId == uid),
            "shared" => CanSeeShared ? q.Where(s => s.IsShared) : q.Where(_ => false),
            _ => q.Where(s => s.UserId == uid || (CanSeeShared && s.IsShared)),
        };

        if (query.FavoritesOnly) q = q.Where(s => s.Favorites.Any(f => f.UserId == uid));
        if (!string.IsNullOrWhiteSpace(query.Category)) q = q.Where(s => s.Category == query.Category);
        if (query.ConnectionId is { } cid) q = q.Where(s => s.ConnectionId == cid);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(s => s.QueryTitle.Contains(term) || (s.Description != null && s.Description.Contains(term)) || s.SqlQuery.Contains(term));
        }

        q = query.SortBy?.ToLowerInvariant() switch
        {
            "querytitle" => query.Descending ? q.OrderByDescending(s => s.QueryTitle) : q.OrderBy(s => s.QueryTitle),
            "category" => query.Descending ? q.OrderByDescending(s => s.Category) : q.OrderBy(s => s.Category),
            _ => query.Descending ? q.OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt) : q.OrderBy(s => s.UpdatedAt ?? s.CreatedAt),
        };

        var total = await q.CountAsync(ct);
        var rows = await q.Skip(query.Skip).Take(query.PageSize)
            .Include(s => s.User).Include(s => s.Connection).Include(s => s.Favorites.Where(f => f.UserId == uid))
            .ToListAsync(ct);
        return new PagedResult<SavedQueryDto>(rows.Select(ToDto).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken ct)
    {
        var uid = currentUser.UserId;
        var shared = CanSeeShared;
        return await db.SavedQueries.AsNoTracking()
            .Where(s => s.Category != null && (s.UserId == uid || (shared && s.IsShared)))
            .Select(s => s.Category!).Distinct().OrderBy(c => c).ToListAsync(ct);
    }

    public async Task<SavedQueryDto> GetAsync(int id, CancellationToken ct) => ToDto(await LoadVisibleAsync(id, ct));

    public async Task<SavedQueryDto> CreateAsync(SaveQueryRequest request, CancellationToken ct)
    {
        await EnsureSavableAsync(request, ct);
        var entity = new SavedQuery { UserId = currentUser.UserId };
        Apply(entity, request);
        db.SavedQueries.Add(entity);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("saved_query.created", "SavedQuery", entity.SavedQueryId.ToString(), new { entity.QueryTitle, entity.IsShared }, ct);
        return await GetAsync(entity.SavedQueryId, ct);
    }

    public async Task<SavedQueryDto> UpdateAsync(int id, SaveQueryRequest request, CancellationToken ct)
    {
        var entity = await db.SavedQueries.FirstOrDefaultAsync(s => s.SavedQueryId == id, ct) ?? throw new NotFoundException("Saved query not found.");
        if (entity.UserId != currentUser.UserId) throw new ForbiddenException("Only the owner can modify a saved query.");
        await EnsureSavableAsync(request, ct, entity.SqlQuery);
        Apply(entity, request);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("saved_query.updated", "SavedQuery", id.ToString(), new { entity.IsShared }, ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var entity = await db.SavedQueries.FirstOrDefaultAsync(s => s.SavedQueryId == id, ct) ?? throw new NotFoundException("Saved query not found.");
        if (entity.UserId != currentUser.UserId && !currentUser.HasPermission(Permissions.UsersManage))
            throw new ForbiddenException("Only the owner can delete a saved query.");
        db.FavoriteQueries.RemoveRange(await db.FavoriteQueries.Where(f => f.SavedQueryId == id).ToListAsync(ct));
        db.SavedQueries.Remove(entity);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("saved_query.deleted", "SavedQuery", id.ToString(), null, ct);
    }

    public async Task<FavoriteResultDto> ToggleFavoriteAsync(int id, CancellationToken ct)
    {
        await LoadVisibleAsync(id, ct);
        var existing = await db.FavoriteQueries.FirstOrDefaultAsync(f => f.SavedQueryId == id && f.UserId == currentUser.UserId, ct);
        if (existing is not null) db.FavoriteQueries.Remove(existing);
        else db.FavoriteQueries.Add(new FavoriteQuery { SavedQueryId = id, UserId = currentUser.UserId, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        return new FavoriteResultDto(id, existing is null);
    }

    public async Task<QueryResultDto> RunAsync(int id, RunSavedQueryRequest request, CancellationToken ct)
    {
        var saved = await LoadVisibleAsync(id, ct);
        // Saved SQL is untrusted: the pipeline revalidates it against current permissions on every run.
        return await pipeline.ExecuteAsync(new PipelineRequest
        {
            ConnectionId = saved.ConnectionId,
            Sql = saved.SqlQuery,
            Parameters = request.Parameters ?? ReadParameters(saved.ParametersProtected),
            SavedQueryId = saved.SavedQueryId,
            QueryTitle = saved.QueryTitle,
            UserQuestion = saved.UserQuestion,
            GeneratedSql = saved.SqlQuery,
        }, ct);
    }

    private async Task EnsureSavableAsync(SaveQueryRequest request, CancellationToken ct, string? existingSql = null)
    {
        await access.GetViewableConnectionAsync(request.ConnectionId, ct);
        if (request.IsShared && !currentUser.HasPermission(Permissions.SavedQueriesShare))
            throw new ForbiddenException("Your role cannot share queries.");

        var sqlChanged = existingSql is null || SqlQueryService.Normalize(existingSql) != SqlQueryService.Normalize(request.SqlQuery);
        if (sqlChanged && !currentUser.HasPermission(Permissions.QueryEditSql))
        {
            var generated = request.SourceQueryHistoryId is { } hid
                ? await db.QueryHistory.Where(h => h.QueryId == hid && h.UserId == currentUser.UserId).Select(h => h.GeneratedSql).FirstOrDefaultAsync(ct)
                : null;
            if (generated is null || SqlQueryService.Normalize(generated) != SqlQueryService.Normalize(request.SqlQuery))
                throw new ForbiddenException("Your role can only save unmodified AI-generated SQL.");
        }

        var validation = await pipeline.ValidateAsync(request.ConnectionId, request.SqlQuery, request.Parameters.Select(p => p.Name.TrimStart('@')).ToList(), ct);
        if (!validation.IsValid) throw new SqlValidationFailedException(validation);

        if (request.ChartConfigJson is not null)
        {
            try { using var _ = JsonDocument.Parse(request.ChartConfigJson); }
            catch (JsonException) { throw new BusinessRuleException("Chart configuration must be valid JSON."); }
        }
    }

    private void Apply(SavedQuery entity, SaveQueryRequest r)
    {
        entity.ConnectionId = r.ConnectionId;
        entity.QueryTitle = r.QueryTitle.Trim();
        entity.Description = r.Description?.Trim();
        entity.UserQuestion = r.UserQuestion?.Trim();
        entity.SqlQuery = r.SqlQuery.Trim();
        entity.Category = string.IsNullOrWhiteSpace(r.Category) ? null : r.Category.Trim();
        entity.IsShared = r.IsShared;
        entity.ParametersProtected = r.Parameters.Count > 0 ? protector.Protect(JsonSerializer.Serialize(r.Parameters)) : null;
        entity.ChartConfigJson = r.ChartConfigJson;
    }

    private async Task<SavedQuery> LoadVisibleAsync(int id, CancellationToken ct)
    {
        var uid = currentUser.UserId;
        var entity = await db.SavedQueries.AsNoTracking()
            .Include(s => s.User).Include(s => s.Connection).Include(s => s.Favorites.Where(f => f.UserId == uid))
            .FirstOrDefaultAsync(s => s.SavedQueryId == id, ct) ?? throw new NotFoundException("Saved query not found.");
        if (entity.UserId != uid && !(entity.IsShared && CanSeeShared))
            throw new NotFoundException("Saved query not found.");
        return entity;
    }

    private IReadOnlyList<QueryParameterDto> ReadParameters(string? protectedValue)
    {
        var json = protector.Unprotect(protectedValue);
        return json is null ? [] : JsonSerializer.Deserialize<List<QueryParameterDto>>(json) ?? [];
    }

    private SavedQueryDto ToDto(SavedQuery s) => new(
        s.SavedQueryId, s.QueryTitle, s.Description, s.UserQuestion, s.SqlQuery, s.Category, s.IsShared,
        s.Favorites.Any(f => f.UserId == currentUser.UserId), s.UserId == currentUser.UserId, s.User.FullName,
        s.ConnectionId, s.Connection.ConnectionName, ReadParameters(s.ParametersProtected), s.ChartConfigJson, s.CreatedAt, s.UpdatedAt);
}
