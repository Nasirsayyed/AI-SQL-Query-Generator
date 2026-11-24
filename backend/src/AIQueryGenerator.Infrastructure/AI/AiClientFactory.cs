using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Domain.Enums;
using AIQueryGenerator.Infrastructure.Persistence;
using AIQueryGenerator.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIQueryGenerator.Infrastructure.AI;

/// <summary>Fallback provider settings from configuration ("Ai:Provider") used when no database configuration is active.</summary>
public sealed class AiProviderOptions
{
    public const string SectionName = "Ai:Provider";

    public string Name { get; set; } = "OpenAI (configuration)";
    public AiProviderType ProviderType { get; set; } = AiProviderType.OpenAI;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>Secret reference for the key; defaults to the OPENAI_API_KEY environment variable.</summary>
    public string ApiKeyReference { get; set; } = "env:OPENAI_API_KEY";
    public double Temperature { get; set; }
    public int MaxOutputTokens { get; set; } = 2000;
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxSchemaTables { get; set; } = 40;
}

public sealed class AiClientFactory(
    ApplicationDbContext db,
    IHttpClientFactory httpClients,
    ISecretStore secrets,
    IOptions<AiProviderOptions> fallback,
    ILoggerFactory loggers) : IAiClientFactory
{
    public const string HttpClientName = "ai-provider";

    public async Task<IAiCompletionClient> CreateAsync(CancellationToken cancellationToken)
    {
        var active = await db.AiProviderConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.IsActive, cancellationToken);
        AiProviderSettings settings;
        if (active is not null)
        {
            settings = new AiProviderSettings(active.Name, active.ProviderType, active.BaseUrl, active.Model,
                secrets.Resolve(active.ApiKeySecretReference) ?? string.Empty, active.Temperature, active.MaxOutputTokens,
                active.TimeoutSeconds, active.MaxSchemaTables);
        }
        else
        {
            var o = fallback.Value;
            settings = new AiProviderSettings(o.Name, o.ProviderType, o.BaseUrl, o.Model, secrets.Resolve(o.ApiKeyReference) ?? string.Empty,
                o.Temperature, o.MaxOutputTokens, o.TimeoutSeconds, o.MaxSchemaTables);
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new ExternalServiceException("The AI provider is not configured. An administrator must supply an API key in AI settings or the OPENAI_API_KEY environment variable.", ErrorCodes.AiNotConfigured, 503);

        return new OpenAiCompatibleClient(httpClients.CreateClient(HttpClientName), settings, loggers.CreateLogger<OpenAiCompatibleClient>());
    }
}
