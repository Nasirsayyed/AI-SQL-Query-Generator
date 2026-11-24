using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Domain.Entities;
using AIQueryGenerator.Shared.Contracts;
using AIQueryGenerator.Shared.Exceptions;
using AIQueryGenerator.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace AIQueryGenerator.Application.Auth;

public interface IUserService
{
    Task<PagedResult<UserListItemDto>> ListAsync(UserQuery query, CancellationToken ct);
    Task<UserListItemDto> GetAsync(int userId, CancellationToken ct);
    Task<UserListItemDto> CreateAsync(CreateUserRequest request, CancellationToken ct);
    Task<UserListItemDto> UpdateAsync(int userId, UpdateUserRequest request, CancellationToken ct);
    Task ResetPasswordAsync(int userId, ResetPasswordRequest request, CancellationToken ct);
    Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct);
    Task<IReadOnlyList<PermissionDto>> ListPermissionsAsync(CancellationToken ct);
    Task<RoleDto> UpdateRolePermissionsAsync(int roleId, UpdateRolePermissionsRequest request, CancellationToken ct);
}

/// <summary>
/// Admin-controlled user management. Only Super Admins may create, modify or grant the Admin/Super Admin roles.
/// </summary>
public sealed class UserService(
    IApplicationDbContext db,
    IPasswordHasher hasher,
    ICurrentUser currentUser,
    IAuditService audit,
    TimeProvider clock) : IUserService
{
    public async Task<PagedResult<UserListItemDto>> ListAsync(UserQuery query, CancellationToken ct)
    {
        var users = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            users = users.Where(u => u.FullName.Contains(s) || u.Email.Contains(s));
        }
        if (query.IsActive.HasValue) users = users.Where(u => u.IsActive == query.IsActive);
        if (!string.IsNullOrWhiteSpace(query.Role)) users = users.Where(u => u.UserRoles.Any(r => r.Role.Name == query.Role));

        users = (query.SortBy?.ToLowerInvariant()) switch
        {
            "email" => query.Descending ? users.OrderByDescending(u => u.Email) : users.OrderBy(u => u.Email),
            "createdat" => query.Descending ? users.OrderByDescending(u => u.CreatedAt) : users.OrderBy(u => u.CreatedAt),
            "lastloginat" => query.Descending ? users.OrderByDescending(u => u.LastLoginAt) : users.OrderBy(u => u.LastLoginAt),
            _ => query.Descending ? users.OrderByDescending(u => u.FullName) : users.OrderBy(u => u.FullName),
        };

        var total = await users.CountAsync(ct);
        var items = await users.Skip(query.Skip).Take(query.PageSize)
            .Select(u => new UserListItemDto(u.UserId, u.FullName, u.Email, u.IsActive,
                u.UserRoles.Select(r => r.Role.Name).ToList(), u.LastLoginAt, u.CreatedAt))
            .ToListAsync(ct);
        return new PagedResult<UserListItemDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<UserListItemDto> GetAsync(int userId, CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(u => u.UserId == userId)
            .Select(u => new UserListItemDto(u.UserId, u.FullName, u.Email, u.IsActive,
                u.UserRoles.Select(r => r.Role.Name).ToList(), u.LastLoginAt, u.CreatedAt))
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("User not found.");

    public async Task<UserListItemDto> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        var normalized = request.Email.Trim().ToUpperInvariant();
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct))
            throw new ConflictException("A user with this email already exists.");

        var roles = await ResolveRolesAsync(request.Roles, ct);
        EnsureCanGrant(roles.Select(r => r.Name));

        var now = clock.GetUtcNow().UtcDateTime;
        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim(),
            NormalizedEmail = normalized,
            PasswordHash = hasher.Hash(request.Password),
            IsActive = true,
        };
        foreach (var role in roles) user.UserRoles.Add(new UserRole { Role = role, AssignedAt = now });
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("user.created", "User", user.UserId.ToString(), new { user.Email, roles = request.Roles }, ct);
        return await GetAsync(user.UserId, ct);
    }

    public async Task<UserListItemDto> UpdateAsync(int userId, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).FirstOrDefaultAsync(u => u.UserId == userId, ct)
                   ?? throw new NotFoundException("User not found.");

        var existingRoles = user.UserRoles.Select(r => r.Role.Name).ToList();
        EnsureCanManage(existingRoles);

        var roles = await ResolveRolesAsync(request.Roles, ct);
        var added = roles.Select(r => r.Name).Except(existingRoles).ToList();
        var removed = existingRoles.Except(roles.Select(r => r.Name)).ToList();
        EnsureCanGrant(added.Concat(removed));

        if (userId == currentUser.UserId && (!request.IsActive || removed.Count > 0))
            throw new BusinessRuleException("You cannot deactivate yourself or remove your own roles.");

        await EnsureNotLastSuperAdminAsync(user, request, removed, ct);

        user.FullName = request.FullName.Trim();
        var securityChanged = user.IsActive != request.IsActive || added.Count > 0 || removed.Count > 0;
        user.IsActive = request.IsActive;

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var ur in user.UserRoles.Where(ur => removed.Contains(ur.Role.Name)).ToList()) user.UserRoles.Remove(ur);
        foreach (var role in roles.Where(r => added.Contains(r.Name))) user.UserRoles.Add(new UserRole { RoleId = role.RoleId, AssignedAt = now });

        if (securityChanged)
        {
            user.RotateSecurityStamp();
            foreach (var t in await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct))
                t.Revoke(now, "access_changed");
        }

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("user.updated", "User", userId.ToString(), new { request.IsActive, added, removed }, ct);
        return await GetAsync(userId, ct);
    }

    public async Task ResetPasswordAsync(int userId, ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).FirstOrDefaultAsync(u => u.UserId == userId, ct)
                   ?? throw new NotFoundException("User not found.");
        EnsureCanManage(user.UserRoles.Select(r => r.Role.Name));

        user.PasswordHash = hasher.Hash(request.NewPassword);
        user.RotateSecurityStamp();
        user.LockoutEndAt = null;
        user.FailedLoginCount = 0;
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var t in await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct))
            t.Revoke(now, "password_reset");
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("user.password_reset", "User", userId.ToString(), null, ct);
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct) =>
        await db.Roles.AsNoTracking().OrderBy(r => r.RoleId)
            .Select(r => new RoleDto(r.RoleId, r.Name, r.Description,
                r.RolePermissions.Select(p => p.Permission.Code).ToList(), r.UserRoles.Count))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PermissionDto>> ListPermissionsAsync(CancellationToken ct) =>
        await db.Permissions.AsNoTracking().OrderBy(p => p.Code).Select(p => new PermissionDto(p.Code, p.Description)).ToListAsync(ct);

    public async Task<RoleDto> UpdateRolePermissionsAsync(int roleId, UpdateRolePermissionsRequest request, CancellationToken ct)
    {
        var role = await db.Roles.Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission).FirstOrDefaultAsync(r => r.RoleId == roleId, ct)
                   ?? throw new NotFoundException("Role not found.");
        if (role.Name == Roles.SuperAdmin)
            throw new BusinessRuleException("Super Admin permissions cannot be changed.");
        EnsureCanGrant([role.Name]);
        if (request.Permissions.Contains(Permissions.AdminsManage) && !currentUser.IsInRole(Roles.SuperAdmin))
            throw new ForbiddenException("Only a Super Admin can grant administrator management.");

        var permissions = await db.Permissions.Where(p => request.Permissions.Contains(p.Code)).ToListAsync(ct);
        var unknown = request.Permissions.Except(permissions.Select(p => p.Code)).ToList();
        if (unknown.Count > 0) throw new BusinessRuleException($"Unknown permissions: {string.Join(", ", unknown)}");

        role.RolePermissions.Clear();
        foreach (var p in permissions) role.RolePermissions.Add(new RolePermission { RoleId = role.RoleId, PermissionId = p.PermissionId });

        var now = clock.GetUtcNow().UtcDateTime;
        var userIds = await db.UserRoles.Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId).ToListAsync(ct);
        foreach (var t in await db.RefreshTokens.Where(t => userIds.Contains(t.UserId) && t.RevokedAt == null).ToListAsync(ct))
            t.Revoke(now, "role_permissions_changed");

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("role.permissions_updated", "Role", roleId.ToString(), new { request.Permissions }, ct);
        return (await ListRolesAsync(ct)).First(r => r.RoleId == roleId);
    }

    private async Task<List<Role>> ResolveRolesAsync(IReadOnlyList<string> names, CancellationToken ct)
    {
        var roles = await db.Roles.Where(r => names.Contains(r.Name)).ToListAsync(ct);
        var unknown = names.Except(roles.Select(r => r.Name), StringComparer.OrdinalIgnoreCase).ToList();
        if (unknown.Count > 0) throw new BusinessRuleException($"Unknown roles: {string.Join(", ", unknown)}");
        return roles;
    }

    private void EnsureCanGrant(IEnumerable<string> roleNames)
    {
        if (roleNames.Any(Roles.Privileged.Contains) && !currentUser.HasPermission(Permissions.AdminsManage))
            throw new ForbiddenException("Only a Super Admin can grant or revoke administrator roles.");
    }

    private void EnsureCanManage(IEnumerable<string> targetRoles)
    {
        if (targetRoles.Any(Roles.Privileged.Contains) && !currentUser.HasPermission(Permissions.AdminsManage))
            throw new ForbiddenException("Only a Super Admin can manage administrator accounts.");
    }

    private async Task EnsureNotLastSuperAdminAsync(User user, UpdateUserRequest request, List<string> removed, CancellationToken ct)
    {
        var isSuper = user.UserRoles.Any(r => r.Role.Name == Roles.SuperAdmin);
        if (!isSuper || (request.IsActive && !removed.Contains(Roles.SuperAdmin))) return;
        var activeSupers = await db.UserRoles.CountAsync(ur => ur.Role.Name == Roles.SuperAdmin && ur.User.IsActive, ct);
        if (activeSupers <= 1)
            throw new BusinessRuleException("At least one active Super Admin must remain.");
    }
}
