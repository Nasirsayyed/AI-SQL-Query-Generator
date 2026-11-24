using System.Net;
using AIQueryGenerator.IntegrationTests.Infrastructure;

namespace AIQueryGenerator.IntegrationTests;

public class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Login_returns_access_token_and_sets_httponly_refresh_cookie()
    {
        var client = factory.CreateApiClient();
        var response = await client.PostJsonAsync("/api/auth/login", new { email = ApiFactory.AdminEmail, password = ApiFactory.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadApiAsync();
        Assert.True(body.Success);
        Assert.False(string.IsNullOrEmpty(body.Data.GetProperty("accessToken").GetString()));
        Assert.False(body.Data.TryGetProperty("refreshToken", out _));
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("aiqg_refresh="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_with_wrong_password_is_rejected_with_generic_message()
    {
        var response = await factory.CreateApiClient().PostJsonAsync("/api/auth/login", new { email = ApiFactory.AdminEmail, password = "wrong-Password1!" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.ReadApiAsync();
        Assert.Equal("INVALID_CREDENTIALS", body.ErrorCode);
        Assert.Equal("Invalid email or password.", body.Message);
    }

    [Fact]
    public async Task Profile_requires_authentication()
    {
        var response = await factory.CreateApiClient().GetAsync("/api/auth/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("UNAUTHORIZED", (await response.ReadApiAsync()).ErrorCode);
    }

    [Fact]
    public async Task Profile_returns_roles_and_permissions()
    {
        var client = await factory.LoginAsync();
        var body = await (await client.GetAsync("/api/auth/profile")).ReadApiAsync();
        Assert.Contains("SuperAdmin", body.Data.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.Contains("admins.manage", body.Data.GetProperty("permissions").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Refresh_rotates_token_and_reuse_revokes_family()
    {
        var client = factory.CreateApiClient();
        var login = await client.PostJsonAsync("/api/auth/login", new { email = ApiFactory.AdminEmail, password = ApiFactory.Password });
        var original = login.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("aiqg_refresh=")).Split(';')[0];

        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        var refreshed = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var rotated = refreshed.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("aiqg_refresh=")).Split(';')[0];
        Assert.NotEqual(original, rotated);

        // Replay the original (already rotated) token from a different client.
        var attacker = factory.CreateApiClient();
        attacker.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        attacker.DefaultRequestHeaders.Add("Cookie", original);
        Assert.Equal(HttpStatusCode.Unauthorized, (await attacker.PostAsync("/api/auth/refresh", null)).StatusCode);

        // The legitimate client's descendant token was revoked with the family.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Refresh_requires_anti_forgery_header()
    {
        var client = factory.CreateApiClient();
        await client.PostJsonAsync("/api/auth/login", new { email = ApiFactory.AdminEmail, password = ApiFactory.Password });
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_refresh_token()
    {
        var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        await client.PostJsonAsync("/api/auth/login", new { email = ApiFactory.AdminEmail, password = ApiFactory.Password });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Invalid_login_payload_returns_validation_errors()
    {
        var response = await factory.CreateApiClient().PostJsonAsync("/api/auth/login", new { email = "not-an-email", password = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.ReadApiAsync();
        Assert.Equal("VALIDATION_FAILED", body.ErrorCode);
        Assert.NotEmpty(body.Errors);
    }

    [Fact]
    public async Task Health_endpoint_is_available()
    {
        var response = await factory.CreateApiClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public class OpenApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Swagger_document_lists_all_required_endpoints()
    {
        var json = await factory.CreateApiClient().GetStringAsync("/swagger/v1/swagger.json");
        foreach (var path in new[]
        {
            "/api/auth/login", "/api/auth/logout", "/api/auth/refresh", "/api/auth/profile",
            "/api/connections", "/api/connections/{id}", "/api/connections/{id}/test", "/api/connections/{id}/refresh-schema",
            "/api/schema/{connectionId}", "/api/schema/{connectionId}/tables", "/api/schema/{connectionId}/tables/{tableName}", "/api/schema/{connectionId}/search",
            "/api/ai/generate-sql", "/api/ai/explain-sql", "/api/ai/suggest-chart",
            "/api/sql/validate", "/api/sql/execute", "/api/sql/format", "/api/sql/explain-plan",
            "/api/query-history", "/api/query-history/{id}", "/api/query-history/{id}/rerun",
            "/api/saved-queries", "/api/saved-queries/{id}", "/api/saved-queries/{id}/favorite",
            "/api/dashboard/summary", "/api/dashboard/query-trends", "/api/dashboard/top-tables", "/api/dashboard/execution-performance",
        })
            Assert.Contains($"\"{path}\"", json);
        Assert.Contains("\"Bearer\"", json);
    }
}
