using AIQueryGenerator.API.Infrastructure;
using AIQueryGenerator.Application.Audit;
using AIQueryGenerator.Application.Settings;
using AIQueryGenerator.Shared.Contracts;
using AIQueryGenerator.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace AIQueryGenerator.API.Controllers;

/// <summary>AI provider configuration. API keys are write-only; only secret references are stored.</summary>
[Route("api/settings/ai")]
[HasPermission(Permissions.AiSettingsManage)]
public sealed class AiSettingsController(IAiSettingsService settings) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AiProviderConfigurationDto>>>> List(CancellationToken ct) => Success(await settings.ListAsync(ct));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AiProviderConfigurationDto>>> Create(SaveAiProviderConfigurationRequest request, CancellationToken ct) =>
        Success(await settings.CreateAsync(request, ct));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiResponse<AiProviderConfigurationDto>>> Update(int id, SaveAiProviderConfigurationRequest request, CancellationToken ct) =>
        Success(await settings.UpdateAsync(id, request, ct));

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(int id, CancellationToken ct)
    {
        await settings.DeleteAsync(id, ct);
        return Done("Configuration deleted.");
    }

    [HttpPost("{id:int}/activate")]
    public async Task<ActionResult<ApiResponse<AiProviderConfigurationDto>>> Activate(int id, CancellationToken ct) => Success(await settings.ActivateAsync(id, ct));

    /// <summary>Sends a minimal structured-output request to the active provider.</summary>
    [HttpPost("test")]
    public async Task<ActionResult<ApiResponse<AiProviderTestResultDto>>> Test(CancellationToken ct) => Success(await settings.TestAsync(ct));
}

[Route("api/audit-logs")]
[HasPermission(Permissions.AuditView)]
public sealed class AuditLogsController(IAuditLogService audit) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AuditLogDto>>>> List([FromQuery] AuditLogQuery query, CancellationToken ct) => Success(await audit.ListAsync(query, ct));
}
