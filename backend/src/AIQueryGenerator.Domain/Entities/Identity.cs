using AIQueryGenerator.Domain.Common;

namespace AIQueryGenerator.Domain.Entities;

public class User : AuditableEntity
{
    public const int MaxFailedLogins = 5;

    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndAt { get; set; }

    /// <summary>Incremented when credentials/roles change to invalidate outstanding refresh tokens.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

    public bool IsLockedOut(DateTime utcNow) => LockoutEndAt.HasValue && LockoutEndAt.Value > utcNow;

    /// <summary>Records a failed login and applies a temporary lockout after repeated failures.</summary>
    public void RegisterFailedLogin(DateTime utcNow)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= MaxFailedLogins)
        {
            LockoutEndAt = utcNow.AddMinutes(15);
            FailedLoginCount = 0;
        }
    }

    public void RegisterSuccessfulLogin(DateTime utcNow)
    {
        FailedLoginCount = 0;
        LockoutEndAt = null;
        LastLoginAt = utcNow;
    }

    public void RotateSecurityStamp() => SecurityStamp = Guid.NewGuid().ToString("N");
}

public class Role
{
    public int RoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class UserRole
{
    public int UserId { get; set; }
    public int RoleId { get; set; }
    public DateTime AssignedAt { get; set; }

    public User User { get; set; } = null!;
    public Role Role { get; set; } = null!;
}

public class Permission
{
    public int PermissionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class RolePermission
{
    public int RoleId { get; set; }
    public int PermissionId { get; set; }

    public Role Role { get; set; } = null!;
    public Permission Permission { get; set; } = null!;
}

public class RefreshToken
{
    public long RefreshTokenId { get; set; }
    public int UserId { get; set; }

    /// <summary>SHA-256 hash of the opaque token. The raw token is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>All tokens descending from one login share a family; reuse of a rotated token revokes the family.</summary>
    public Guid FamilyId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? ReasonRevoked { get; set; }

    public User User { get; set; } = null!;

    public bool IsActive(DateTime utcNow) => RevokedAt is null && ExpiresAt > utcNow;

    public void Revoke(DateTime utcNow, string reason, string? replacedBy = null)
    {
        RevokedAt ??= utcNow;
        ReasonRevoked ??= reason;
        ReplacedByTokenHash ??= replacedBy;
    }
}
