using AIQueryGenerator.API.Infrastructure;
using AIQueryGenerator.Application.Auth;
using AIQueryGenerator.Shared.Contracts;
using AIQueryGenerator.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace AIQueryGenerator.API.Controllers;

/// <summary>Admin-controlled user registration and role management.</summary>
[Route("api/users")]
[HasPermission(Permissions.UsersManage)]
public sealed class UsersController(IUserService users) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<UserListItemDto>>>> List([FromQuery] UserQuery query, CancellationToken ct) =>
        Success(await users.ListAsync(query, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApiResponse<UserListItemDto>>> Get(int id, CancellationToken ct) => Success(await users.GetAsync(id, ct));

    /// <summary>Registers a new user. Only Super Admins may create administrators.</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<UserListItemDto>>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var user = await users.CreateAsync(request, ct);
        return Created($"/api/users/{user.UserId}", user);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiResponse<UserListItemDto>>> Update(int id, UpdateUserRequest request, CancellationToken ct) =>
        Success(await users.UpdateAsync(id, request, ct));

    [HttpPost("{id:int}/reset-password")]
    public async Task<ActionResult<ApiResponse<object>>> ResetPassword(int id, ResetPasswordRequest request, CancellationToken ct)
    {
        await users.ResetPasswordAsync(id, request, ct);
        return Done("Password reset.");
    }
}

[Route("api/roles")]
[HasPermission(Permissions.UsersManage)]
public sealed class RolesController(IUserService users) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RoleDto>>>> List(CancellationToken ct) => Success(await users.ListRolesAsync(ct));

    [HttpGet("permissions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PermissionDto>>>> ListPermissions(CancellationToken ct) => Success(await users.ListPermissionsAsync(ct));

    [HttpPut("{id:int}/permissions")]
    public async Task<ActionResult<ApiResponse<RoleDto>>> UpdatePermissions(int id, UpdateRolePermissionsRequest request, CancellationToken ct) =>
        Success(await users.UpdateRolePermissionsAsync(id, request, ct));
}
