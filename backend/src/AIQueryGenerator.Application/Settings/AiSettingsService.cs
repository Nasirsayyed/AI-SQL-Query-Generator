using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Domain.Entities;
using AIQueryGenerator.Domain.Enums;
using AIQueryGenerator.Shared.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AIQueryGenerator.Application.Settings;

public sealed record AiProviderConfigurationDto(
    int AiProviderConfigurationId,
    string Name,
    AiProviderType ProviderType,
    string BaseUrl,
    string Model,
    string? ApiKeySecretReference,
    bool HasApiKey,
    double Temperature,
    int MaxOutputTokens,
    int TimeoutSeconds,
    int MaxSchemaTables,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record SaveAiProviderConfigurationRequest
{
    public string Name { get; init; } = string.Empty;
    public AiProviderType ProviderType { get; init; } = AiProviderType.OpenAI;
    public string BaseUrl { get; init; } = "https://api.openai.com/v1";
    public string Model { get; init; } = "gpt-4o-mini";

    /// <summary>Write-only. A secret reference ("env:OPENAI_API_KEY") or a raw key, which is encrypted before storage.</summary>
    public string? ApiKey { get; init; }
    public double Temperature { get; init; }
    public int MaxOutputTokens { get; init; } = 2000;
    public int TimeoutSeconds { get; init; } = 60;
    public int MaxSchemaTables { get; init; } = 40;
}

public sealed record AiProviderTestResultDto(bool Success, string Message, string? Model, int? LatencyMs);

public sealed class SaveAiProviderConfigurationRequestValidator : AbstractValidator<SaveAiProviderConfigurationRequest>
{
    public SaveAiProviderConfigurationRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ProviderType).IsInEnum();
        RuleFor(x => x.BaseUrl).NotEmpty().MaximumLength(500)
            .Must(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .WithMessage("Base URL must be an absolute HTTPS URL.");
        RuleFor(x => x.Model).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ApiKey).MaximumLength(1000);
        RuleFor(x => x.Temperature).InclusiveBetween(0, 1);
        RuleFor(x => x.MaxOutputTokens).InclusiveBetween(256, 16000);
        RuleFor(x => x.TimeoutSeconds).InclusiveBetween(5, 300);
        RuleFor(x => x.MaxSchemaTables).InclusiveBetween(1, 200);
    }
}

public interface IAiSettingsService
{
    Task<IReadOnlyList<AiProviderConfigurationDto>> ListAsync(CancellationToken ct);
    Task<AiProviderConfigurationDto> CreateAsync(SaveAiProviderConfigurationRequest request, CancellationToken ct);
    Task<AiProviderConfigurationDto> UpdateAsync(int id, SaveAiProviderConfigurationRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    Task<AiProviderConfigurationDto> ActivateAsync(int id, CancellationToken ct);
    Task<AiProviderTestResultDto> TestAsync(CancellationToken ct);
    Task<AiStatusDto> GetStatusAsync(CancellationToken ct);
}

public sealed record AiStatusDto(bool Configured, string? ProviderName, string? Model);

public sealed class AiSettingsService(
    IApplicationDbContext db,
    ISecretStore secrets,
    IAiClientFactory clients,
    IAuditService audit) : IAiSettingsService
{
    public async Task<IReadOnlyList<AiProviderConfigurationDto>> ListAsync(CancellationToken ct) =>
        (await db.AiProviderConfigurations.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<AiProviderConfigurationDto> CreateAsync(SaveAiProviderConfigurationRequest request, CancellationToken ct)
    {
        var entity = new AiProviderConfiguration();
        Apply(entity, request);
        if (string.IsNullOrWhiteSpace(request.ApiKey)) entity.ApiKeySecretReference = "env:OPENAI_API_KEY";
        db.AiProviderConfigurations.Add(entity);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("ai_settings.created", "AiProviderConfiguration", entity.AiProviderConfigurationId.ToString(), new { entity.Name, entity.Model }, ct);
        return ToDto(entity);
    }

    public async Task<AiProviderConfigurationDto> UpdateAsync(int id, SaveAiProviderConfigurationRequest request, CancellationToken ct)
    {
        var entity = await db.AiProviderConfigurations.FirstOrDefaultAsync(c => c.AiProviderConfigurationId == id, ct)
                     ?? throw new NotFoundException("AI provider configuration not found.");
        Apply(entity, request);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("ai_settings.updated", "AiProviderConfiguration", id.ToString(), new { entity.Name, entity.Model, apiKeyChanged = request.ApiKey is not null }, ct);
        return ToDto(entity);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var entity = await db.AiProviderConfigurations.FirstOrDefaultAsync(c => c.AiProviderConfigurationId == id, ct)
                     ?? throw new NotFoundException("AI provider configuration not found.");
        db.AiProviderConfigurations.Remove(entity);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("ai_settings.deleted", "AiProviderConfiguration", id.ToString(), null, ct);
    }

    public async Task<AiProviderConfigurationDto> ActivateAsync(int id, CancellationToken ct)
    {
        var all = await db.AiProviderConfigurations.ToListAsync(ct);
        var target = all.FirstOrDefault(c => c.AiProviderConfigurationId == id) ?? throw new NotFoundException("AI provider configuration not found.");
        foreach (var c in all) c.IsActive = c == target;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("ai_settings.activated", "AiProviderConfiguration", id.ToString(), null, ct);
        return ToDto(target);
    }

    public async Task<AiProviderTestResultDto> TestAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var client = await clients.CreateAsync(ct);
            var result = await client.CompleteJsonAsync(new AiJsonCompletionRequest(
                "You are a connectivity test. Reply with the requested JSON only.",
                "Return {\"ok\": true}.",
                "connectivity_test",
                """{"type":"object","additionalProperties":false,"required":["ok"],"properties":{"ok":{"type":"boolean"}}}""",
                0, 256), ct);
            return new AiProviderTestResultDto(true, "The AI provider responded successfully.", result.Model, (int)sw.ElapsedMilliseconds);
        }
        catch (AppException ex)
        {
            return new AiProviderTestResultDto(false, ex.Message, null, (int)sw.ElapsedMilliseconds);
        }
    }

    public async Task<AiStatusDto> GetStatusAsync(CancellationToken ct)
    {
        try
        {
            var client = await clients.CreateAsync(ct);
            return new AiStatusDto(true, client.ProviderName, client.Model);
        }
        catch (AppException)
        {
            return new AiStatusDto(false, null, null);
        }
    }

    private void Apply(AiProviderConfiguration entity, SaveAiProviderConfigurationRequest r)
    {
        entity.Name = r.Name.Trim();
        entity.ProviderType = r.ProviderType;
        entity.BaseUrl = r.BaseUrl.Trim().TrimEnd('/');
        entity.Model = r.Model.Trim();
        entity.Temperature = r.Temperature;
        entity.MaxOutputTokens = r.MaxOutputTokens;
        entity.TimeoutSeconds = r.TimeoutSeconds;
        entity.MaxSchemaTables = r.MaxSchemaTables;
        if (!string.IsNullOrWhiteSpace(r.ApiKey))
            entity.ApiKeySecretReference = secrets.IsReference(r.ApiKey) ? r.ApiKey.Trim() : secrets.Protect(r.ApiKey.Trim());
    }

    private AiProviderConfigurationDto ToDto(AiProviderConfiguration c) => new(
        c.AiProviderConfigurationId, c.Name, c.ProviderType, c.BaseUrl, c.Model,
        c.ApiKeySecretReference.StartsWith("env:", StringComparison.Ordinal) ? c.ApiKeySecretReference : "(encrypted)",
        !string.IsNullOrEmpty(secrets.Resolve(c.ApiKeySecretReference)),
        c.Temperature, c.MaxOutputTokens, c.TimeoutSeconds, c.MaxSchemaTables, c.IsActive, c.CreatedAt, c.UpdatedAt);
}
