using AIQueryGenerator.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIQueryGenerator.API.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult<ApiResponse<T>> Success<T>(T data, string? message = null) => Ok(ApiResponse<T>.Ok(data, message));

    protected ActionResult<ApiResponse<T>> Created<T>(string location, T data) => base.Created(location, ApiResponse<T>.Ok(data));

    protected ActionResult<ApiResponse<object>> Done(string message) => Ok(ApiResponse<object>.Ok(new { }, message));
}
