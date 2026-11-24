using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Infrastructure.Security;

namespace AIQueryGenerator.API.Infrastructure;

/// <summary>Resolves the caller from the validated JWT principal of the current HTTP request.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public int UserId => int.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public string? Email => Principal?.FindFirstValue(JwtRegisteredClaimNames.Email) ?? Principal?.FindFirstValue(ClaimTypes.Email);

    public IReadOnlyCollection<string> Roles => Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? [];

    public IReadOnlyCollection<string> Permissions => Principal?.FindAll(AppClaimTypes.Permission).Select(c => c.Value).ToList() ?? [];

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public bool HasPermission(string permission) => Principal?.HasClaim(AppClaimTypes.Permission, permission) == true;

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;
}
