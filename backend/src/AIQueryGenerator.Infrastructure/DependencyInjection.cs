using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Application.Ai;
using AIQueryGenerator.Application.Sql;
using AIQueryGenerator.Infrastructure.AI;
using AIQueryGenerator.Infrastructure.Persistence;
using AIQueryGenerator.Infrastructure.Security;
using AIQueryGenerator.Infrastructure.Sql;
using AIQueryGenerator.Infrastructure.TargetDatabase;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace AIQueryGenerator.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, bool configureDatabase = true)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<SecretStoreOptions>(configuration.GetSection(SecretStoreOptions.SectionName));
        services.Configure<SqlValidationOptions>(configuration.GetSection(SqlValidationOptions.SectionName));
        services.Configure<ExecutionOptions>(configuration.GetSection(ExecutionOptions.SectionName));
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.Configure<AiProviderOptions>(configuration.GetSection(AiProviderOptions.SectionName));

        if (configureDatabase)
        {
            var connectionString = configuration.GetConnectionString("ApplicationDb")
                ?? throw new InvalidOperationException("ConnectionStrings:ApplicationDb is not configured.");
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(3);
                sql.CommandTimeout(30);
            }));
        }
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<DatabaseSeeder>();

        // Data protection keys encrypt stored secrets and parameters. In production persist them outside the web root and
        // protect them (certificate, DPAPI, or Azure Key Vault) — see docs/Deployment.md.
        var dp = services.AddDataProtection().SetApplicationName("AIQueryGenerator");
        var keysPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath)) dp.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        if (OperatingSystem.IsWindows() && configuration.GetValue<bool>("DataProtection:ProtectWithDpapi")) dp.ProtectKeysWithDpapi();
        // Azure Blob key storage / Key Vault key wrapping (web farms, Azure App Service) when configured.
        Azure.AzureIntegration.ConfigureDataProtection(dp, configuration);

        services.AddSingleton<IPasswordHasher, PasswordHasherService>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<ISecretStore, DataProtectionSecretStore>();
        services.AddSingleton<Application.Abstractions.IDataProtector, DataProtectionValueProtector>();
        services.AddSingleton<IValidationTokenService, ValidationTokenService>();
        services.AddSingleton<IQueryConcurrencyLimiter, QueryConcurrencyLimiter>();
        services.AddScoped<IAuditService, AuditService>();

        services.AddSingleton<ISqlValidator, ScriptDomSqlValidator>();
        services.AddSingleton<ISqlFormatter, ScriptDomSqlFormatter>();
        services.AddSingleton<IExecutionPlanParser, ShowplanParser>();
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddScoped<ITargetDatabaseClient, TargetDatabaseClient>();
        services.AddScoped<ISchemaDiscoveryService, SqlServerSchemaDiscoveryService>();

        services.AddSingleton<IPromptProvider, EmbeddedPromptProvider>();
        services.AddScoped<IAiClientFactory, AiClientFactory>();
        services.AddHttpClient(AiClientFactory.HttpClientName, c => c.Timeout = Timeout.InfiniteTimeSpan)
            .AddStandardResilienceHandler(o =>
            {
                o.Retry.MaxRetryAttempts = 2;
                o.Retry.Delay = TimeSpan.FromSeconds(2);
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(120);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(300);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(240);
            });

        return services;
    }
}
