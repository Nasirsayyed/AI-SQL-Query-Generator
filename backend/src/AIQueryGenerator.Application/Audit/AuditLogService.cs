using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AIQueryGenerator.Application.Audit;

public sealed class AuditLogQuery : PagedRequest
{
    public string? Action { get; set; }
    public int? UserId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public sealed record AuditLogDto(long AuditLogId, int? UserId, string? UserName, string Action, string? EntityType, string? EntityId, string? Details, string? IpAddress, DateTime CreatedAt);

public interface IAuditLogService
{
    Task<PagedResult<AuditLogDto>> ListAsync(AuditLogQuery query, CancellationToken ct);
}

public sealed class AuditLogService(IApplicationDbContext db) : IAuditLogService
{
    public async Task<PagedResult<AuditLogDto>> ListAsync(AuditLogQuery query, CancellationToken ct)
    {
        var q = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Action)) q = q.Where(a => a.Action.StartsWith(query.Action));
        if (query.UserId is { } uid) q = q.Where(a => a.UserId == uid);
        if (query.From is { } from) q = q.Where(a => a.CreatedAt >= from);
        if (query.To is { } to) q = q.Where(a => a.CreatedAt < to.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(query.Search)) q = q.Where(a => a.Action.Contains(query.Search) || (a.Details != null && a.Details.Contains(query.Search)));

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.AuditLogId).Skip(query.Skip).Take(query.PageSize)
            .GroupJoin(db.Users, a => a.UserId, u => u.UserId, (a, users) => new { a, users })
            .SelectMany(x => x.users.DefaultIfEmpty(), (x, u) => new AuditLogDto(x.a.AuditLogId, x.a.UserId, u != null ? u.FullName : null,
                x.a.Action, x.a.EntityType, x.a.EntityId, x.a.Details, x.a.IpAddress, x.a.CreatedAt))
            .ToListAsync(ct);
        return new PagedResult<AuditLogDto>(items, query.Page, query.PageSize, total);
    }
}
