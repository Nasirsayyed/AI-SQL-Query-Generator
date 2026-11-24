using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Application.Schema;
using AIQueryGenerator.Domain.Common;
using AIQueryGenerator.Domain.Entities;
using AIQueryGenerator.Domain.Enums;
using AIQueryGenerator.Shared.Exceptions;
using AIQueryGenerator.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace AIQueryGenerator.Application.Connections;

public interface IConnectionService
{
    Task<IReadOnlyList<ConnectionDto>> ListAsync(CancellationToken ct);
    Task<IReadOnlyList<ConnectionSummaryDto>> ListAccessibleAsync(CancellationToken ct);
    Task<ConnectionDto> GetAsync(int id, CancellationToken ct);
    Task<ConnectionDto> CreateAsync(SaveConnectionRequest request, CancellationToken ct);
    Task<ConnectionDto> UpdateAsync(int id, SaveConnectionRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    Task<ConnectionTestResultDto> TestAsync(int id, CancellationToken ct);
    Task<ConnectionDto> SetActiveAsync(int id, bool isActive, CancellationToken ct);
    Task<SchemaRefreshResultDto> RefreshSchemaAsync(int id, bool full, CancellationToken ct);
    Task<IReadOnlyList<AllowedObjectRuleDto>> GetAllowedObjectsAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<AllowedObjectRuleDto>> UpdateAllowedObjectsAsync(int id, UpdateAllowedObjectsRequest request, CancellationToken ct);
}

/// <summary>Admin-only management of target database connections.</summary>
public sealed class ConnectionService(
    IApplicationDbContext db,
    ISecretStore secrets,
    ITargetDatabaseClient target,
    ISchemaSyncService schemaSync,
    ISchemaCatalogService catalogs,
    IConnectionAccessService access,
    ICurrentUser currentUser,
    IAuditService audit,
    TimeProvider clock) : IConnectionService
{
    public async Task<IReadOnlyList<ConnectionDto>> ListAsync(CancellationToken ct)
    {
        var list = await Query().OrderBy(c => c.ConnectionName).ToListAsync(ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<ConnectionSummaryDto>> ListAccessibleAsync(CancellationToken ct)
    {
        var ids = await access.GetAccessibleConnectionIdsAsync(ct);
        var isAdmin = currentUser.HasPermission(Permissions.ConnectionsManage);
        return await db.DatabaseConnections.AsNoTracking()
            .Where(c => ids.Contains(c.ConnectionId) && (isAdmin || c.IsActive))
            .OrderBy(c => c.ConnectionName)
            .Select(c => new ConnectionSummaryDto(c.ConnectionId, c.ConnectionName, c.DatabaseName, c.IsActive, c.Status, c.LastSchemaRefreshAt))
            .ToListAsync(ct);
    }

    public async Task<ConnectionDto> GetAsync(int id, CancellationToken ct) => ToDto(await LoadAsync(id, ct, tracking: false));

    public async Task<ConnectionDto> CreateAsync(SaveConnectionRequest request, CancellationToken ct)
    {
        await EnsureUniqueNameAsync(request.ConnectionName, null, ct);
        var connection = new DatabaseConnection { CreatedByUserId = currentUser.UserId };
        Apply(connection, request, isNew: true);
        await ApplyRolesAsync(connection, request.AllowedRoles, ct);
        db.DatabaseConnections.Add(connection);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("connection.created", "DatabaseConnection", connection.ConnectionId.ToString(),
            new { connection.ConnectionName, connection.ServerName, connection.DatabaseName, connection.AuthenticationType }, ct);
        return await GetAsync(connection.ConnectionId, ct);
    }

    public async Task<ConnectionDto> UpdateAsync(int id, SaveConnectionRequest request, CancellationToken ct)
    {
        var connection = await LoadAsync(id, ct);
        await EnsureUniqueNameAsync(request.ConnectionName, id, ct);

        var targetChanged = connection.ServerName != request.ServerName.Trim()
            || connection.DatabaseName != request.DatabaseName.Trim()
            || connection.AuthenticationType != request.AuthenticationType
            || connection.Username != request.Username?.Trim()
            || request.Password is not null
            || connection.EncryptConnection != request.EncryptConnection
            || connection.TrustServerCertificate != request.TrustServerCertificate;

        Apply(connection, request, isNew: false);
        if (targetChanged) connection.MarkUntested();

        db.DatabaseConnectionPermissions.RemoveRange(connection.Permissions);
        connection.Permissions.Clear();
        await ApplyRolesAsync(connection, request.AllowedRoles, ct);
        await db.SaveChangesAsync(ct);
        catalogs.Invalidate(id);
        await audit.LogAsync("connection.updated", "DatabaseConnection", id.ToString(), new { targetChanged, request.AllowedRoles }, ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var connection = await LoadAsync(id, ct);
        connection.IsDeleted = true;
        connection.DeletedAt = clock.GetUtcNow().UtcDateTime;
        connection.IsActive = false;
        connection.EncryptedCredentialsReference = null;
        connection.ConnectionName = $"{connection.ConnectionName} (deleted {connection.ConnectionId})".Truncate100();
        db.DatabaseConnectionPermissions.RemoveRange(connection.Permissions);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("connection.deleted", "DatabaseConnection", id.ToString(), null, ct);
    }

    public async Task<ConnectionTestResultDto> TestAsync(int id, CancellationToken ct)
    {
        var connection = await LoadAsync(id, ct);
        var result = await target.TestConnectionAsync(connection, ct);
        var now = clock.GetUtcNow().UtcDateTime;

        connection.LastTestedAt = now;
        connection.LastTestMessage = result.Message.Length > 500 ? result.Message[..500] : result.Message;
        connection.Status = !result.Success ? ConnectionStatus.Failed : result.IsLeastPrivilege ? ConnectionStatus.Healthy : ConnectionStatus.Unsafe;
        if (connection.Status != ConnectionStatus.Healthy) connection.Deactivate();

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("connection.tested", "DatabaseConnection", id.ToString(), new { result.Success, result.IsLeastPrivilege, status = connection.Status.ToString() }, ct);
        return new ConnectionTestResultDto(result.Success, result.IsLeastPrivilege, result.Message, result.ServerVersion, result.LoginName, result.PrivilegeWarnings, now);
    }

    public async Task<ConnectionDto> SetActiveAsync(int id, bool isActive, CancellationToken ct)
    {
        var connection = await LoadAsync(id, ct);
        if (isActive)
        {
            try { connection.Activate(); }
            catch (DomainException ex) { throw new BusinessRuleException(ex.Message); }
        }
        else connection.Deactivate();

        await db.SaveChangesAsync(ct);
        await audit.LogAsync(isActive ? "connection.activated" : "connection.deactivated", "DatabaseConnection", id.ToString(), null, ct);
        return await GetAsync(id, ct);
    }

    public async Task<SchemaRefreshResultDto> RefreshSchemaAsync(int id, bool full, CancellationToken ct)
    {
        var connection = await LoadAsync(id, ct);
        if (connection.Status != ConnectionStatus.Healthy)
            throw new BusinessRuleException("Test the connection successfully before refreshing its schema.");
        var result = await schemaSync.SynchronizeAsync(connection, full, ct);
        await audit.LogAsync("schema.refreshed", "DatabaseConnection", id.ToString(), result, ct);
        return result;
    }

    public async Task<IReadOnlyList<AllowedObjectRuleDto>> GetAllowedObjectsAsync(int id, CancellationToken ct)
    {
        await LoadAsync(id, ct, tracking: false);
        return await db.AllowedDatabaseObjects.AsNoTracking().Where(a => a.ConnectionId == id)
            .OrderBy(a => a.SchemaName).ThenBy(a => a.ObjectName).ThenBy(a => a.ColumnName)
            .Select(a => new AllowedObjectRuleDto(a.ObjectType, a.SchemaName, a.ObjectName, a.ColumnName, a.AccessLevel))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AllowedObjectRuleDto>> UpdateAllowedObjectsAsync(int id, UpdateAllowedObjectsRequest request, CancellationToken ct)
    {
        if (!currentUser.HasPermission(Permissions.SchemaConfigure))
            throw new ForbiddenException();
        await LoadAsync(id, ct, tracking: false);

        var existing = await db.AllowedDatabaseObjects.Where(a => a.ConnectionId == id).ToListAsync(ct);
        db.AllowedDatabaseObjects.RemoveRange(existing);

        var now = clock.GetUtcNow().UtcDateTime;
        var distinct = request.Rules
            .Select(r => r with
            {
                ObjectType = r.ObjectType,
                ObjectName = r.ObjectType == DbObjectType.Schema ? null : r.ObjectName,
                ColumnName = r.ObjectType == DbObjectType.Column ? r.ColumnName : null,
            })
            .DistinctBy(r => (r.ObjectType, r.SchemaName.ToLowerInvariant(), r.ObjectName?.ToLowerInvariant(), r.ColumnName?.ToLowerInvariant()));

        foreach (var rule in distinct)
        {
            db.AllowedDatabaseObjects.Add(new AllowedDatabaseObject
            {
                ConnectionId = id,
                ObjectType = rule.ObjectType,
                SchemaName = rule.SchemaName.Trim(),
                ObjectName = rule.ObjectName?.Trim(),
                ColumnName = rule.ColumnName?.Trim(),
                AccessLevel = rule.AccessLevel,
                CreatedAt = now,
                CreatedByUserId = currentUser.UserId,
            });
        }

        await db.SaveChangesAsync(ct);
        catalogs.Invalidate(id);
        await audit.LogAsync("schema.permissions_updated", "DatabaseConnection", id.ToString(), new { rules = request.Rules.Count }, ct);
        return await GetAllowedObjectsAsync(id, ct);
    }

    private IQueryable<DatabaseConnection> Query() =>
        db.DatabaseConnections.AsNoTracking().Include(c => c.Permissions).ThenInclude(p => p.Role).Where(c => !c.IsDeleted);

    private async Task<DatabaseConnection> LoadAsync(int id, CancellationToken ct, bool tracking = true)
    {
        var query = db.DatabaseConnections.Include(c => c.Permissions).ThenInclude(p => p.Role).Where(c => c.ConnectionId == id && !c.IsDeleted);
        if (!tracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Database connection not found.");
    }

    private async Task EnsureUniqueNameAsync(string name, int? exceptId, CancellationToken ct)
    {
        var trimmed = name.Trim();
        if (await db.DatabaseConnections.AnyAsync(c => c.ConnectionName == trimmed && c.ConnectionId != exceptId && !c.IsDeleted, ct))
            throw new ConflictException("A connection with this name already exists.");
    }

    private void Apply(DatabaseConnection c, SaveConnectionRequest r, bool isNew)
    {
        c.ConnectionName = r.ConnectionName.Trim();
        c.ServerName = r.ServerName.Trim();
        c.DatabaseName = r.DatabaseName.Trim();
        c.AuthenticationType = r.AuthenticationType;
        c.EncryptConnection = r.EncryptConnection;
        c.TrustServerCertificate = r.TrustServerCertificate;
        c.ConnectionTimeoutSeconds = r.ConnectionTimeoutSeconds;
        c.CommandTimeoutSeconds = r.CommandTimeoutSeconds;
        c.MaxRows = r.MaxRows;

        if (r.AuthenticationType == SqlAuthenticationType.SqlPassword)
        {
            c.Username = r.Username?.Trim();
            if (r.Password is not null)
                c.EncryptedCredentialsReference = secrets.IsReference(r.Password) ? r.Password.Trim() : secrets.Protect(r.Password);
            else if (isNew)
                throw new BusinessRuleException("A password or secret reference is required for SQL authentication.");
        }
        else
        {
            c.Username = null;
            c.EncryptedCredentialsReference = null;
        }
    }

    private async Task ApplyRolesAsync(DatabaseConnection connection, IReadOnlyList<string> roleNames, CancellationToken ct)
    {
        var roles = await db.Roles.Where(r => roleNames.Contains(r.Name)).ToListAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var role in roles)
            connection.Permissions.Add(new DatabaseConnectionPermission { RoleId = role.RoleId, CanQuery = true, CreatedAt = now });
    }

    private static ConnectionDto ToDto(DatabaseConnection c) => new(
        c.ConnectionId, c.ConnectionName, c.ServerName, c.DatabaseName, c.AuthenticationType, c.Username,
        !string.IsNullOrEmpty(c.EncryptedCredentialsReference), c.EncryptConnection, c.TrustServerCertificate,
        c.ConnectionTimeoutSeconds, c.CommandTimeoutSeconds, c.MaxRows, c.IsActive, c.Status, c.LastTestedAt, c.LastTestMessage,
        c.LastSchemaRefreshAt, c.Permissions.Where(p => p.CanQuery).Select(p => p.Role?.Name ?? string.Empty).Where(n => n.Length > 0).ToList(),
        c.CreatedAt, c.UpdatedAt);
}

internal static class StringExtensions
{
    public static string Truncate100(this string s) => s.Length <= 100 ? s : s[..100];
}
