using System.Net;
using AIQueryGenerator.IntegrationTests.Infrastructure;

namespace AIQueryGenerator.IntegrationTests;

public class AuthorizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("Viewer", "/api/users")]
    [InlineData("Analyst", "/api/users")]
    [InlineData("Developer", "/api/connections/manage")]
    [InlineData("Analyst", "/api/settings/ai")]
    [InlineData("Viewer", "/api/query-history")]
    public async Task Role_without_permission_gets_403(string role, string url)
    {
        var scenario = await Scenario.CreateAsync(factory);
        var client = await scenario.LoginAsAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Viewer_cannot_generate_or_execute_sql()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var viewer = await scenario.LoginAsAsync("Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostJsonAsync("/api/ai/generate-sql", new { connectionId = scenario.ConnectionId, question = "top customers" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostJsonAsync("/api/sql/execute", new { connectionId = scenario.ConnectionId, sql = "SELECT 1", parameters = Array.Empty<object>() })).StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_create_super_admin_but_super_admin_can()
    {
        var scenario = await Scenario.CreateAsync(factory);
        await scenario.Admin.PostJsonAsync("/api/users", new { fullName = "Admin Two", email = "admin2@test.local", password = ApiFactory.Password, roles = new[] { "Admin" } });
        var admin = await factory.LoginAsync("admin2@test.local");

        var response = await admin.PostJsonAsync("/api/users", new { fullName = "Evil", email = "evil@test.local", password = ApiFactory.Password, roles = new[] { "SuperAdmin" } });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var ok = await admin.PostJsonAsync("/api/users", new { fullName = "Dev Two", email = "dev2@test.local", password = ApiFactory.Password, roles = new[] { "Developer" } });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task Weak_passwords_are_rejected()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var response = await scenario.Admin.PostJsonAsync("/api/users", new { fullName = "Weak", email = "weak@test.local", password = "password", roles = new[] { "Viewer" } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Connection_list_never_exposes_credentials()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var json = await (await scenario.Admin.GetAsync("/api/connections/manage")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("S3cret-password!", json);
        Assert.DoesNotContain("encryptedCredentialsReference", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"hasStoredCredentials\":true", json);

        var stored = await factory.ScopeAsync(db => Task.FromResult(db.DatabaseConnections.First(c => c.ConnectionId == scenario.ConnectionId).EncryptedCredentialsReference));
        Assert.StartsWith("dp:", stored);
        Assert.DoesNotContain("S3cret-password!", stored);
    }
}
