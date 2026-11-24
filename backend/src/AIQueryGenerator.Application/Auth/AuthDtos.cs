namespace AIQueryGenerator.Application.Auth;

public sealed record LoginRequest(string Email, string Password);

public sealed record UserProfileDto(
    int UserId,
    string FullName,
    string Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    DateTime? LastLoginAt);

/// <summary>Result of login/refresh. The refresh token is delivered to the browser as an HttpOnly cookie only.</summary>
public sealed record AuthResult(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserProfileDto User);

public sealed record AuthResponse(string AccessToken, DateTime AccessTokenExpiresAt, UserProfileDto User);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record UserListItemDto(
    int UserId,
    string FullName,
    string Email,
    bool IsActive,
    IReadOnlyList<string> Roles,
    DateTime? LastLoginAt,
    DateTime CreatedAt);

public sealed class UserQuery : Shared.Contracts.PagedRequest
{
    public string? Role { get; init; }
    public bool? IsActive { get; init; }
}

public sealed record CreateUserRequest(string FullName, string Email, string Password, IReadOnlyList<string> Roles);

public sealed record UpdateUserRequest(string FullName, bool IsActive, IReadOnlyList<string> Roles);

public sealed record ResetPasswordRequest(string NewPassword);

public sealed record RoleDto(int RoleId, string Name, string? Description, IReadOnlyList<string> Permissions, int UserCount);

public sealed record PermissionDto(string Code, string? Description);

public sealed record UpdateRolePermissionsRequest(IReadOnlyList<string> Permissions);
