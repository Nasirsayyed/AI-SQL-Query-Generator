using AIQueryGenerator.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AIQueryGenerator.Infrastructure.Security;

public sealed class SecretStoreOptions
{
    public const string SectionName = "Secrets";

    /// <summary>
    /// Environment/configuration keys that "env:" references may resolve. Restricting the namespace prevents an
    /// administrator from pointing a connection at an arbitrary server with, for example, "env:Jwt__SigningKey".
    /// </summary>
    public string[] AllowedReferencePrefixes { get; set; } = ["AIQG_SECRET_", "OPENAI_API_KEY", "AZURE_OPENAI_API_KEY"];
}

/// <summary>
/// Secret references:
///   dp:&lt;payload&gt;  – encrypted with the ASP.NET Core Data Protection key ring (keys stored outside the database,
///                    optionally protected with a certificate, DPAPI or Azure Key Vault).
///   env:&lt;NAME&gt;    – resolved from configuration/environment, which can be backed by a secret manager such as
///                    Azure Key Vault through the configuration provider.
/// </summary>
public sealed class DataProtectionSecretStore(IDataProtectionProvider provider, IConfiguration configuration, IOptions<SecretStoreOptions> options) : ISecretStore
{
    private const string DpPrefix = "dp:";
    private const string EnvPrefix = "env:";
    private readonly Microsoft.AspNetCore.DataProtection.IDataProtector _protector = provider.CreateProtector("AIQueryGenerator.Secrets.v1");

    public string Protect(string secret) => DpPrefix + _protector.Protect(secret);

    public bool IsReference(string? value) =>
        value is not null && (value.StartsWith(DpPrefix, StringComparison.Ordinal) || value.StartsWith(EnvPrefix, StringComparison.Ordinal));

    public string? Resolve(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;
        if (reference.StartsWith(DpPrefix, StringComparison.Ordinal))
        {
            try { return _protector.Unprotect(reference[DpPrefix.Length..]); }
            catch (System.Security.Cryptography.CryptographicException) { return null; }
        }

        if (reference.StartsWith(EnvPrefix, StringComparison.Ordinal))
        {
            var name = reference[EnvPrefix.Length..].Trim();
            if (!options.Value.AllowedReferencePrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
                return null;
            return configuration[name] ?? Environment.GetEnvironmentVariable(name);
        }

        return null;
    }
}

/// <summary>Encrypts small values such as stored query parameters.</summary>
public sealed class DataProtectionValueProtector(IDataProtectionProvider provider) : Application.Abstractions.IDataProtector
{
    private readonly Microsoft.AspNetCore.DataProtection.IDataProtector _protector = provider.CreateProtector("AIQueryGenerator.Values.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return null;
        try { return _protector.Unprotect(protectedValue); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }
}
