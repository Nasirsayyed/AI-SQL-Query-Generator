using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Domain.Entities;
using AIQueryGenerator.Shared.Exceptions;
using AIQueryGenerator.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace AIQueryGenerator.Application.Connections;

public interface IConnectionAccessService
{
    /// <summary>Returns the connection if it exists, is active, and the current user's roles are permitted to query it.</summary>
    Task<DatabaseConnection> GetQueryableConnectionAsync(int connectionId, CancellationToken ct);

    /// <summary>Returns the connection if the user may view its metadata (admins: any; others: permitted connections).</summary>
    Task<DatabaseConnection> GetViewableConnectionAsync(int connectionId, CancellationToken ct);

    Task<IReadOnlyList<int>> GetAccessibleConnectionIdsAsync(CancellationToken ct);
}

public sealed class ConnectionAccessService(IApplicationDbContext db, ICurrentUser currentUser) : IConnectionAccessService
{
    public async Task<DatabaseConnection> GetQueryableConnectionAsync(int connectionId, CancellationToken ct)
    {
        var connection = await GetViewableConnectionAsync(connectionId, ct);
        if (!connection.IsActive)
            throw new BusinessRuleException("This database connection is not active.", ErrorCodes.ConnectionInactive);
        return connection;
    }

    public async Task<DatabaseConnection> GetViewableConnectionAsync(int connectionId, CancellationToken ct)
    {
        var connection = await db.DatabaseConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ConnectionId == connectionId && !c.IsDeleted, ct)
            ?? throw new NotFoundException("Database connection not found.");

        if (!await HasRoleAccessAsync(connectionId, ct))
            throw new ForbiddenException("You do not have access to this database connection.");
        return connection;
    }

    public async Task<IReadOnlyList<int>> GetAccessibleConnectionIdsAsync(CancellationToken ct)
    {
        if (currentUser.HasPermission(Permissions.ConnectionsManage))
            return await db.DatabaseConnections.Where(c => !c.IsDeleted).Select(c => c.ConnectionId).ToListAsync(ct);

        var roles = currentUser.Roles.ToList();
        return await db.DatabaseConnectionPermissions
            .Where(p => p.CanQuery && roles.Contains(p.Role.Name) && !p.Connection.IsDeleted)
            .Select(p => p.ConnectionId).Distinct().ToListAsync(ct);
    }

    private async Task<bool> HasRoleAccessAsync(int connectionId, CancellationToken ct)
    {
        if (currentUser.HasPermission(Permissions.ConnectionsManage)) return true;
        var roles = currentUser.Roles.ToList();
        return await db.DatabaseConnectionPermissions.AnyAsync(p => p.ConnectionId == connectionId && p.CanQuery && roles.Contains(p.Role.Name), ct);
    }
}
