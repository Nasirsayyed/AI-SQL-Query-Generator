using System.Text.Json;
using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Domain.Entities;
using AIQueryGenerator.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace AIQueryGenerator.Infrastructure.Security;

/// <summary>Persists audit records. Callers must never pass secrets, passwords or result data in details.</summary>
public sealed class AuditService(ApplicationDbContext db, ICurrentUser currentUser, ILogger<AuditService> logger) : IAuditService
{
    public async Task LogAsync(string action, string? entityType = null, string? entityId = null, object? details = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = details is null ? null : JsonSerializer.Serialize(details);
            db.AuditLogs.Add(new AuditLog
            {
                UserId = currentUser.IsAuthenticated ? currentUser.UserId : null,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Details = json is { Length: > 4000 } ? json[..4000] : json,
                IpAddress = currentUser.IpAddress,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to write audit record {Action}", action);
        }
    }
}
