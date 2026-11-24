using System.Security.Cryptography;
using System.Text;
using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Application.Common;
using AIQueryGenerator.Application.Sql;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace AIQueryGenerator.Infrastructure.Security;

/// <summary>Authenticated, time-limited tokens binding a validation result to user, connection, SQL and parameters.</summary>
public sealed class ValidationTokenService(IDataProtectionProvider provider, IOptions<ExecutionOptions> options) : IValidationTokenService
{
    private readonly ITimeLimitedDataProtector _protector = provider.CreateProtector("AIQueryGenerator.SqlValidation.v1").ToTimeLimitedDataProtector();

    public string Issue(int userId, int connectionId, string sql, string parameterSignature) =>
        _protector.Protect(Payload(userId, connectionId, sql, parameterSignature), TimeSpan.FromMinutes(options.Value.ValidationTokenLifetimeMinutes));

    public bool Verify(string? token, int userId, int connectionId, string sql, string parameterSignature)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            var actual = _protector.Unprotect(token);
            var expected = Payload(userId, connectionId, sql, parameterSignature);
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static string Payload(int userId, int connectionId, string sql, string parameterSignature) =>
        $"{userId}|{connectionId}|{Hashing.Sha256(sql)}|{Hashing.Sha256(parameterSignature)}";
}
