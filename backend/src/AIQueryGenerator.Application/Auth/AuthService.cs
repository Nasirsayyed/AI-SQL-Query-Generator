using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Domain.Entities;
using AIQueryGenerator.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIQueryGenerator.Application.Auth;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken ct);
    Task LogoutAsync(string? refreshToken, CancellationToken ct);
    Task<UserProfileDto> GetProfileAsync(CancellationToken ct);
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct);
}

public sealed class AuthService(
    IApplicationDbContext db,
    IPasswordHasher hasher,
    ITokenService tokens,
    ICurrentUser currentUser,
    IAuditService audit,
    TimeProvider clock,
    ILogger<AuthService> logger) : IAuthService
{
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var normalized = request.Email.Trim().ToUpperInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);

        if (user is null)
        {
            hasher.Hash(request.Password); // equalise timing for unknown accounts
            await audit.LogAsync("auth.login_failed", "User", null, new { reason = "unknown_email" }, ct);
            throw new UnauthorizedAppException(InvalidCredentialsMessage, ErrorCodes.InvalidCredentials);
        }

        if (user.IsLockedOut(now))
            throw new UnauthorizedAppException("The account is temporarily locked because of repeated failed logins. Try again later.", ErrorCodes.InvalidCredentials);

        if (!hasher.Verify(user.PasswordHash, request.Password, out var needsRehash))
        {
            user.RegisterFailedLogin(now);
            await db.SaveChangesAsync(ct);
            await audit.LogAsync("auth.login_failed", "User", user.UserId.ToString(), new { reason = "bad_password" }, ct);
            throw new UnauthorizedAppException(InvalidCredentialsMessage, ErrorCodes.InvalidCredentials);
        }

        if (!user.IsActive)
            throw new UnauthorizedAppException("This account has been deactivated.", ErrorCodes.InvalidCredentials);

        if (needsRehash) user.PasswordHash = hasher.Hash(request.Password);
        user.RegisterSuccessfulLogin(now);

        var result = await IssueTokensAsync(user, Guid.NewGuid(), now, ct);
        await audit.LogAsync("auth.login", "User", user.UserId.ToString(), null, ct);
        return result;
    }

    public async Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new UnauthorizedAppException("Your session has expired. Please sign in again.", ErrorCodes.TokenExpired);

        var now = clock.GetUtcNow().UtcDateTime;
        var hash = tokens.HashToken(refreshToken);
        var stored = await db.RefreshTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null)
            throw new UnauthorizedAppException("Your session has expired. Please sign in again.", ErrorCodes.TokenExpired);

        if (stored.RevokedAt is not null)
        {
            // A rotated token was presented again: treat as theft and revoke the whole token family.
            logger.LogWarning("Refresh token reuse detected for user {UserId}, family {FamilyId}", stored.UserId, stored.FamilyId);
            var family = await db.RefreshTokens.Where(t => t.FamilyId == stored.FamilyId && t.RevokedAt == null).ToListAsync(ct);
            foreach (var t in family) t.Revoke(now, "reuse_detected");
            await db.SaveChangesAsync(ct);
            await audit.LogAsync("auth.refresh_reuse_detected", "User", stored.UserId.ToString(), null, ct);
            throw new UnauthorizedAppException("Your session is no longer valid. Please sign in again.", ErrorCodes.TokenExpired);
        }

        if (!stored.IsActive(now) || !stored.User.IsActive)
            throw new UnauthorizedAppException("Your session has expired. Please sign in again.", ErrorCodes.TokenExpired);

        var result = await IssueTokensAsync(stored.User, stored.FamilyId, now, ct, stored);
        return result;
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var hash = tokens.HashToken(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null) return;

        var now = clock.GetUtcNow().UtcDateTime;
        var family = await db.RefreshTokens.Where(t => t.FamilyId == stored.FamilyId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in family) t.Revoke(now, "logout");
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("auth.logout", "User", stored.UserId.ToString(), null, ct);
    }

    public async Task<UserProfileDto> GetProfileAsync(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == currentUser.UserId, ct)
                   ?? throw new UnauthorizedAppException();
        var (roles, permissions) = await LoadAccessAsync(user.UserId, ct);
        return ToProfile(user, roles, permissions);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == currentUser.UserId, ct) ?? throw new UnauthorizedAppException();
        if (!hasher.Verify(user.PasswordHash, request.CurrentPassword, out _))
            throw new BusinessRuleException("The current password is incorrect.", ErrorCodes.InvalidCredentials);

        user.PasswordHash = hasher.Hash(request.NewPassword);
        user.RotateSecurityStamp();
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var t in await db.RefreshTokens.Where(t => t.UserId == user.UserId && t.RevokedAt == null).ToListAsync(ct))
            t.Revoke(now, "password_changed");
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("auth.password_changed", "User", user.UserId.ToString(), null, ct);
    }

    private async Task<AuthResult> IssueTokensAsync(User user, Guid familyId, DateTime now, CancellationToken ct, RefreshToken? replacing = null)
    {
        var (roles, permissions) = await LoadAccessAsync(user.UserId, ct);
        var access = tokens.CreateAccessToken(user, roles, permissions);
        var rawRefresh = tokens.GenerateRefreshToken();
        var refreshHash = tokens.HashToken(rawRefresh);
        var refresh = new RefreshToken
        {
            UserId = user.UserId,
            TokenHash = refreshHash,
            FamilyId = familyId,
            CreatedAt = now,
            ExpiresAt = now.Add(tokens.RefreshTokenLifetime),
            CreatedByIp = currentUser.IpAddress,
        };
        replacing?.Revoke(now, "rotated", refreshHash);
        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync(ct);

        return new AuthResult(access.Token, access.ExpiresAt, rawRefresh, refresh.ExpiresAt, ToProfile(user, roles, permissions));
    }

    private async Task<(List<string> Roles, List<string> Permissions)> LoadAccessAsync(int userId, CancellationToken ct)
    {
        var roles = await db.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.Role.Name).ToListAsync(ct);
        var permissions = await db.UserRoles.Where(ur => ur.UserId == userId)
            .SelectMany(ur => ur.Role.RolePermissions.Select(rp => rp.Permission.Code))
            .Distinct().ToListAsync(ct);
        return (roles, permissions.Order().ToList());
    }

    private static UserProfileDto ToProfile(User user, List<string> roles, List<string> permissions) =>
        new(user.UserId, user.FullName, user.Email, roles, permissions, user.LastLoginAt);
}
