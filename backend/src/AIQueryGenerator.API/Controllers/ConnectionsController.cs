using AIQueryGenerator.API.Infrastructure;
using AIQueryGenerator.Application.Connections;
using AIQueryGenerator.Shared.Contracts;
using AIQueryGenerator.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace AIQueryGenerator.API.Controllers;

/// <summary>Target database connections. Credentials are write-only and never returned.</summary>
[Route("api/connections")]
public sealed class ConnectionsController(IConnectionService connections) : ApiControllerBase
{
    /// <summary>Connections available to the caller (admins: all; others: active connections their roles may query).</summary>
    [HttpGet]
    [HasPermission(Permissions.ConnectionsView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ConnectionSummaryDto>>>> List(CancellationToken ct) =>
        Success(await connections.ListAccessibleAsync(ct));

    /// <summary>Full connection details for administrators.</summary>
    [HttpGet("manage")]
    [HasPermission(Permissions.ConnectionsManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ConnectionDto>>>> ListManaged(CancellationToken ct) => Success(await connections.ListAsync(ct));

    [HttpGet("{id:int}")]
    [HasPermission(Permissions.ConnectionsManage)]
    public async Task<ActionResult<ApiResponse<ConnectionDto>>> Get(int id, CancellationToken ct) => Success(await connections.GetAsync(id, ct));

    [HttpPost]
    [HasPermission(Permissions.ConnectionsManage)]
    public async Task<ActionResult<ApiResponse<ConnectionDto>>> Create(SaveConnectionRequest request, CancellationToken ct)
    {
        var created = await connections.CreateAsync(request, ct);
        return Created($"/api/connections/{created.ConnectionId}", created);
    }

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.ConnectionsManage)]
    public async Task<ActionResult<ApiResponse<ConnectionDto>>> Update(int id, SaveConnectionRequest request, CancellationToken ct) =>
        Success(await connections.UpdateAsync(id, request, ct));

    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.ConnectionsManage)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(int id, CancellationToken ct)
    {
        await connections.DeleteAsync(id, ct);
        return Done("Connection deleted.");
    }

    /// <summary>Tests connectivity and verifies the database principal is least-privilege (read-only).</summary>
    [HttpPost("{id:int}/test")]
    [HasPermission(Permissions.ConnectionsManage)]
    public async Task<ActionResult<ApiResponse<ConnectionTestResultDto>>> Test(int id, CancellationToken ct) => Success(await connections.TestAsync(id, ct));

    /// <summary>Activates or deactivates a connection. Activation requires a passing, least-privilege test.</summary>
    [HttpPost("{id:int}/active")]
    [HasPermission(Permissions.ConnectionsManage)]
    public async Task<ActionResult<ApiResponse<ConnectionDto>>> SetActive(int id, SetConnectionActiveRequest request, CancellationToken ct) =>
        Success(await connections.SetActiveAsync(id, request.IsActive, ct));

    /// <summary>Synchronises schema metadata. Incremental by default; pass full=true to rewrite everything.</summary>
    [HttpPost("{id:int}/refresh-schema")]
    [HasPermission(Permissions.ConnectionsManage)]
    public async Task<ActionResult<ApiResponse<SchemaRefreshResultDto>>> RefreshSchema(int id, [FromQuery] bool full, CancellationToken ct) =>
        Success(await connections.RefreshSchemaAsync(id, full, ct));

    [HttpGet("{id:int}/allowed-objects")]
    [HasPermission(Permissions.SchemaConfigure)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AllowedObjectRuleDto>>>> GetAllowedObjects(int id, CancellationToken ct) =>
        Success(await connections.GetAllowedObjectsAsync(id, ct));

    /// <summary>Replaces the allow/deny rules that control which schemas, tables, views and columns AI and users may access.</summary>
    [HttpPut("{id:int}/allowed-objects")]
    [HasPermission(Permissions.SchemaConfigure)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AllowedObjectRuleDto>>>> UpdateAllowedObjects(int id, UpdateAllowedObjectsRequest request, CancellationToken ct) =>
        Success(await connections.UpdateAllowedObjectsAsync(id, request, ct));
}
