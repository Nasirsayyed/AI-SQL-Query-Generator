using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AIQueryGenerator.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "AIQueryGenerator";
    public string Audience { get; set; } = "AIQueryGenerator.Web";

    /// <summary>At least 32 bytes. Supply via environment variable Jwt__SigningKey; never commit it.</summary>
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}

public static class AppClaimTypes
{
    public const string Permission = "perm";
    public const string SecurityStamp = "sstamp";
}

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenDays);

    public AccessToken CreateAccessToken(User user, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(AppClaimTypes.SecurityStamp, user.SecurityStamp),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(permissions.Select(p => new Claim(AppClaimTypes.Permission, p)));

        var token = new JwtSecurityToken(
            _options.Issuer, _options.Audience, claims, now, expires,
            new SigningCredentials(CreateKey(_options.SigningKey), SecurityAlgorithms.HmacSha256));
        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public string GenerateRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));

    public string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static SymmetricSecurityKey CreateKey(string signingKey)
    {
        var bytes = Encoding.UTF8.GetBytes(signingKey);
        if (bytes.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes. Configure it through the Jwt__SigningKey environment variable or a secret store.");
        return new SymmetricSecurityKey(bytes);
    }
}
