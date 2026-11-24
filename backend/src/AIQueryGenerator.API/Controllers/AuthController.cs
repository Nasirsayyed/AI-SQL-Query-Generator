using AIQueryGenerator.Application.Auth;
using AIQueryGenerator.Infrastructure.Security;
using AIQueryGenerator.Shared.Contracts;
using AIQueryGenerator.Shared.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AIQueryGenerator.API.Controllers;

/// <summary>Authentication. Refresh tokens are rotated on every use and only ever travel in an HttpOnly cookie.</summary>
[Route("api/auth")]
public sealed class AuthController(IAuthService auth, IConfiguration configuration, IWebHostEnvironment env) : ApiControllerBase
{
    public const string RefreshCookie = "aiqg_refresh";
    private const string CsrfHeader = "X-Requested-With";

    /// <summary>Signs in with email and password.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, ct);
        SetRefreshCookie(result.RefreshToken, result.RefreshTokenExpiresAt);
        return Success(new AuthResponse(result.AccessToken, result.AccessTokenExpiresAt, result.User));
    }

    /// <summary>Exchanges the refresh cookie for a new access token (rotating the refresh token).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Refresh(CancellationToken ct)
    {
        RequireCsrfHeader();
        try
        {
            var result = await auth.RefreshAsync(Request.Cookies[RefreshCookie], ct);
            SetRefreshCookie(result.RefreshToken, result.RefreshTokenExpiresAt);
            return Success(new AuthResponse(result.AccessToken, result.AccessTokenExpiresAt, result.User));
        }
        catch (UnauthorizedAppException)
        {
            ClearRefreshCookie();
            throw;
        }
    }

    /// <summary>Revokes the refresh token family and clears the cookie.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object>>> Logout(CancellationToken ct)
    {
        RequireCsrfHeader();
        await auth.LogoutAsync(Request.Cookies[RefreshCookie], ct);
        ClearRefreshCookie();
        return Done("Signed out.");
    }

    /// <summary>Returns the signed-in user's profile, roles and permissions.</summary>
    [HttpGet("profile")]
    public async Task<ActionResult<ApiResponse<UserProfileDto>>> Profile(CancellationToken ct) => Success(await auth.GetProfileAsync(ct));

    /// <summary>Changes the signed-in user's password and revokes their sessions.</summary>
    [HttpPost("change-password")]
    public async Task<ActionResult<ApiResponse<object>>> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await auth.ChangePasswordAsync(request, ct);
        ClearRefreshCookie();
        return Done("Password changed. Please sign in again.");
    }

    private void RequireCsrfHeader()
    {
        if (!Request.Headers.ContainsKey(CsrfHeader))
            throw new ForbiddenException("Missing anti-forgery header.");
    }

    private CookieOptions CookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = configuration.GetValue("Auth:RefreshCookieSecure", !env.IsDevelopment() || Request.IsHttps),
        SameSite = Enum.TryParse<SameSiteMode>(configuration["Auth:RefreshCookieSameSite"], out var mode) ? mode : SameSiteMode.Strict,
        Path = (Request.PathBase.HasValue ? Request.PathBase.Value : string.Empty) + "/api/auth",
        Expires = expires,
        IsEssential = true,
    };

    private void SetRefreshCookie(string token, DateTime expires) => Response.Cookies.Append(RefreshCookie, token, CookieOptions(expires));

    private void ClearRefreshCookie() => Response.Cookies.Delete(RefreshCookie, CookieOptions(null));
}
