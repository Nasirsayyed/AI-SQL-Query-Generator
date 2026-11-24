using Azure.Core;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace AIQueryGenerator.Infrastructure.Azure;

/// <summary>
/// Optional Microsoft Azure integration, enabled only by configuration:
/// <list type="bullet">
/// <item><c>KeyVault:Uri</c> loads secrets from Azure Key Vault into configuration (see <see cref="AppKeyVaultSecretManager"/>).</item>
/// <item><c>DataProtection:AzureBlobUri</c> persists the Data Protection key ring to a blob (shared by every API instance).</item>
/// <item><c>DataProtection:KeyVaultKeyId</c> wraps the key ring with a Key Vault key.</item>
/// <item><c>Azure:ManagedIdentityClientId</c> selects a user-assigned managed identity; otherwise
/// <see cref="DefaultAzureCredential"/> is used (managed identity, environment, Azure CLI, …).</item>
/// </list>
/// </summary>
public static class AzureIntegration
{
    public static TokenCredential CreateCredential(IConfiguration configuration)
    {
        var clientId = configuration["Azure:ManagedIdentityClientId"];
        return new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId,
            ExcludeInteractiveBrowserCredential = true,
        });
    }

    /// <summary>Adds Key Vault as a configuration source when <c>KeyVault:Uri</c> is set. Returns whether it was added.</summary>
    public static bool AddKeyVaultIfConfigured(IConfigurationBuilder builder, IConfiguration current)
    {
        var uri = current["KeyVault:Uri"];
        if (string.IsNullOrWhiteSpace(uri)) return false;

        var reload = current.GetValue<int?>("KeyVault:ReloadMinutes");
        builder.AddAzureKeyVault(
            new SecretClient(new Uri(uri), CreateCredential(current)),
            new AzureKeyVaultConfigurationOptions
            {
                Manager = new AppKeyVaultSecretManager(),
                ReloadInterval = reload is > 0 ? TimeSpan.FromMinutes(reload.Value) : null,
            });
        return true;
    }

    /// <summary>Applies the Azure Data Protection settings that are configured. Returns what was enabled.</summary>
    public static (bool Blob, bool KeyVault) ConfigureDataProtection(IDataProtectionBuilder builder, IConfiguration configuration)
    {
        var blobUri = configuration["DataProtection:AzureBlobUri"];
        var keyId = configuration["DataProtection:KeyVaultKeyId"];
        if (string.IsNullOrWhiteSpace(blobUri) && string.IsNullOrWhiteSpace(keyId)) return (false, false);

        var credential = CreateCredential(configuration);
        if (!string.IsNullOrWhiteSpace(blobUri)) builder.PersistKeysToAzureBlobStorage(new Uri(blobUri), credential);
        if (!string.IsNullOrWhiteSpace(keyId)) builder.ProtectKeysWithAzureKeyVault(new Uri(keyId), credential);
        return (!string.IsNullOrWhiteSpace(blobUri), !string.IsNullOrWhiteSpace(keyId));
    }
}

/// <summary>
/// Maps Key Vault secret names (letters, digits and hyphens only) to configuration keys:
/// <list type="bullet">
/// <item><c>Jwt--SigningKey</c> → <c>Jwt:SigningKey</c>; <c>ConnectionStrings--ApplicationDb</c> → <c>ConnectionStrings:ApplicationDb</c>.</item>
/// <item>Secret-reference names: <c>AIQG-SECRET-SALES-DB</c> → <c>AIQG_SECRET_SALES_DB</c>,
/// <c>OPENAI-API-KEY</c> → <c>OPENAI_API_KEY</c>, <c>AZURE-OPENAI-API-KEY</c> → <c>AZURE_OPENAI_API_KEY</c>, so connection
/// settings can use <c>env:AIQG_SECRET_SALES_DB</c>.</item>
/// </list>
/// Disabled secrets are skipped.
/// </summary>
public sealed class AppKeyVaultSecretManager : KeyVaultSecretManager
{
    private static readonly string[] UnderscorePrefixes = ["AIQG-SECRET-", "OPENAI-API-KEY", "AZURE-OPENAI-API-KEY"];

    public override bool Load(SecretProperties secret) => secret.Enabled != false;

    public override string GetKey(KeyVaultSecret secret) => MapName(secret.Name);

    public static string MapName(string name) =>
        UnderscorePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            ? name.Replace('-', '_').ToUpperInvariant()
            : name.Replace("--", ConfigurationPath.KeyDelimiter);
}
