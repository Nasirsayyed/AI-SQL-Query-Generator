using AIQueryGenerator.Infrastructure.Azure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AIQueryGenerator.UnitTests.Azure;

public class AzureIntegrationTests
{
    [Theory]
    [InlineData("Jwt--SigningKey", "Jwt:SigningKey")]
    [InlineData("ConnectionStrings--ApplicationDb", "ConnectionStrings:ApplicationDb")]
    [InlineData("Seed--SuperAdmin--Password", "Seed:SuperAdmin:Password")]
    [InlineData("AIQG-SECRET-SALES-DB", "AIQG_SECRET_SALES_DB")]
    [InlineData("aiqg-secret-demo-db-password", "AIQG_SECRET_DEMO_DB_PASSWORD")]
    [InlineData("OPENAI-API-KEY", "OPENAI_API_KEY")]
    [InlineData("AZURE-OPENAI-API-KEY", "AZURE_OPENAI_API_KEY")]
    public void Maps_key_vault_names_to_configuration_keys(string secretName, string key) =>
        Assert.Equal(key, AppKeyVaultSecretManager.MapName(secretName));

    private static (KeyManagementOptions Options, (bool Blob, bool KeyVault) Enabled) Configure(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        var enabled = AzureIntegration.ConfigureDataProtection(services.AddDataProtection(), configuration);
        using var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value, enabled);
    }

    [Fact]
    public void Data_protection_is_unchanged_without_azure_settings()
    {
        var (options, enabled) = Configure([]);
        Assert.Equal((false, false), enabled);
        Assert.Null(options.XmlRepository);
        Assert.Null(options.XmlEncryptor);
    }

    [Fact]
    public void Blob_storage_and_key_vault_wrapping_are_configured_without_contacting_azure()
    {
        var (options, enabled) = Configure(new()
        {
            ["DataProtection:AzureBlobUri"] = "https://contoso.blob.core.windows.net/keys/aiqg-keys.xml",
            ["DataProtection:KeyVaultKeyId"] = "https://contoso.vault.azure.net/keys/aiqg-dataprotection",
            ["Azure:ManagedIdentityClientId"] = "00000000-0000-0000-0000-000000000001",
        });
        Assert.Equal((true, true), enabled);
        Assert.Contains("Blob", options.XmlRepository!.GetType().Name);
        Assert.Contains("KeyVault", options.XmlEncryptor!.GetType().Name);
    }

    [Fact]
    public void Key_vault_configuration_source_is_only_added_when_configured()
    {
        var builder = new ConfigurationBuilder();
        Assert.False(AzureIntegration.AddKeyVaultIfConfigured(builder, new ConfigurationBuilder().Build()));
        Assert.Empty(builder.Sources);
    }
}
