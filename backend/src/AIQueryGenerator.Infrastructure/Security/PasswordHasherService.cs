using AIQueryGenerator.Application.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace AIQueryGenerator.Infrastructure.Security;

/// <summary>PBKDF2 (HMAC-SHA512, 100k iterations) via ASP.NET Core Identity's hasher (V3 format).</summary>
public sealed class PasswordHasherService : IPasswordHasher
{
    private static readonly PasswordHasher<object> Hasher = new();
    private static readonly object Subject = new();

    public string Hash(string password) => Hasher.HashPassword(Subject, password);

    public bool Verify(string hash, string password, out bool needsRehash)
    {
        needsRehash = false;
        if (string.IsNullOrEmpty(hash)) return false;
        try
        {
            var result = Hasher.VerifyHashedPassword(Subject, hash, password);
            needsRehash = result == PasswordVerificationResult.SuccessRehashNeeded;
            return result != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
