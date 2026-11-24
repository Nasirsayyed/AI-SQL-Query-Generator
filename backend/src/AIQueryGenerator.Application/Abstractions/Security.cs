using AIQueryGenerator.Domain.Entities;

namespace AIQueryGenerator.Application.Abstractions;

/// <summary>The authenticated caller, resolved from the validated JWT.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    int UserId { get; }
    string? Email { get; }
    IReadOnlyCollection<string> Roles { get; }
    IReadOnlyCollection<string> Permissions { get; }
    string? IpAddress { get; }
    bool HasPermission(string permission);
    bool IsInRole(string role);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password, out bool needsRehash);
}

public sealed record AccessToken(string Token, DateTime ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions);
    string GenerateRefreshToken();
    string HashToken(string token);
    TimeSpan RefreshTokenLifetime { get; }
}

/// <summary>
/// Resolves secret references to secret values. Supported reference schemes are implementation-defined
/// (for example "dp:" for data-protection encrypted payloads and "env:" for environment/secret-manager values).
/// </summary>
public interface ISecretStore
{
    /// <summary>Encrypts a secret and returns an opaque reference suitable for persisting.</summary>
    string Protect(string secret);
    string? Resolve(string? reference);
    bool IsReference(string? value);
}

/// <summary>Encrypts small values (e.g. query parameters) for storage at rest.</summary>
public interface IDataProtector
{
    string Protect(string plaintext);
    string? Unprotect(string? protectedValue);
}

/// <summary>
/// Issues and verifies short-lived tokens that bind a successful validation to the exact SQL text,
/// parameters, connection and user. Execution of editor SQL requires a matching token.
/// </summary>
public interface IValidationTokenService
{
    string Issue(int userId, int connectionId, string sql, string parameterSignature);
    bool Verify(string? token, int userId, int connectionId, string sql, string parameterSignature);
}

public interface IQueryConcurrencyLimiter
{
    /// <summary>Acquires an execution slot for the user or throws when limits are exceeded.</summary>
    Task<IDisposable> AcquireAsync(int userId, CancellationToken cancellationToken);
}

public interface IAuditService
{
    Task LogAsync(string action, string? entityType = null, string? entityId = null, object? details = null, CancellationToken cancellationToken = default);
}
