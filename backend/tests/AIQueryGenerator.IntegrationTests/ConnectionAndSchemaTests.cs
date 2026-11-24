using System.Net;
using System.Text.Json;
using AIQueryGenerator.IntegrationTests.Infrastructure;

namespace AIQueryGenerator.IntegrationTests;

public class ConnectionAndSchemaTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Connection_cannot_be_activated_when_principal_is_not_least_privilege()
    {
        var admin = await factory.LoginAsync();
        var created = await (await admin.PostJsonAsync("/api/connections", new
        {
            connectionName = "Unsafe " + Guid.NewGuid().ToString("N")[..6], serverName = "sql.test.local", databaseName = "Db",
            authenticationType = "SqlPassword", username = "sa", password = "x", allowedRoles = Array.Empty<string>(),
        })).ReadApiAsync();
        var id = created.Data.GetProperty("connectionId").GetInt32();

        factory.Target.LeastPrivilege = false;
        try
        {
            var test = await (await admin.PostAsync($"/api/connections/{id}/test", null)).ReadApiAsync();
            Assert.False(test.Data.GetProperty("isLeastPrivilege").GetBoolean());
            var activate = await admin.PostJsonAsync($"/api/connections/{id}/active", new { isActive = true });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, activate.StatusCode);
        }
        finally
        {
            factory.Target.LeastPrivilege = true;
        }
    }

    [Theory]
    [InlineData("Server=evil;Database=x")]
    [InlineData("host;Integrated Security=true")]
    public async Task Connection_strings_are_not_accepted_as_server_name(string server)
    {
        var admin = await factory.LoginAsync();
        var response = await admin.PostJsonAsync("/api/connections", new
        {
            connectionName = "Bad", serverName = server, databaseName = "Db", authenticationType = "SqlPassword", username = "u", password = "p",
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Schema_sync_stores_metadata_and_relationships()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var overview = await (await scenario.Admin.GetAsync($"/api/schema/{scenario.ConnectionId}")).ReadApiAsync();
        var tables = overview.Data.GetProperty("schemas")[0].GetProperty("tables").EnumerateArray().Select(t => t.GetProperty("tableName").GetString()).ToList();
        Assert.Contains("Customers", tables);
        Assert.Contains("Secrets", tables); // admins see every object with its permission flag
        Assert.Single(overview.Data.GetProperty("relationships").EnumerateArray());
    }

    [Fact]
    public async Task Non_admin_sees_only_permitted_tables_and_columns()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var dev = await scenario.LoginAsAsync("Developer");

        var overview = await (await dev.GetAsync($"/api/schema/{scenario.ConnectionId}")).ReadApiAsync();
        var tables = overview.Data.GetProperty("schemas")[0].GetProperty("tables").EnumerateArray().Select(t => t.GetProperty("tableName").GetString()).ToList();
        Assert.DoesNotContain("Secrets", tables);

        var customers = await (await dev.GetAsync($"/api/schema/{scenario.ConnectionId}/tables/dbo.Customers")).ReadApiAsync();
        var columns = customers.Data.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("columnName").GetString()).ToList();
        Assert.DoesNotContain("Email", columns);
        Assert.Contains("CustomerName", columns);

        Assert.Equal(HttpStatusCode.NotFound, (await dev.GetAsync($"/api/schema/{scenario.ConnectionId}/tables/dbo.Secrets")).StatusCode);

        var search = await (await dev.GetAsync($"/api/schema/{scenario.ConnectionId}/search?q=mail")).ReadApiAsync();
        Assert.Empty(search.Data.EnumerateArray());
    }

    [Fact]
    public async Task Incremental_refresh_skips_unchanged_objects()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var result = await (await scenario.Admin.PostAsync($"/api/connections/{scenario.ConnectionId}/refresh-schema", null)).ReadApiAsync();
        Assert.Equal(4, result.Data.GetProperty("tablesUnchanged").GetInt32());
        Assert.Equal(0, result.Data.GetProperty("tablesUpdated").GetInt32());

        var full = await (await scenario.Admin.PostAsync($"/api/connections/{scenario.ConnectionId}/refresh-schema?full=true", null)).ReadApiAsync();
        Assert.Equal(4, full.Data.GetProperty("tablesUpdated").GetInt32());
    }

    [Fact]
    public async Task Dropped_tables_are_removed_on_refresh()
    {
        var scenario = await Scenario.CreateAsync(factory);
        factory.Discovery.IncludeSecretsTable = false;
        try
        {
            var result = await (await scenario.Admin.PostAsync($"/api/connections/{scenario.ConnectionId}/refresh-schema", null)).ReadApiAsync();
            Assert.Equal(1, result.Data.GetProperty("tablesRemoved").GetInt32());
        }
        finally
        {
            factory.Discovery.IncludeSecretsTable = true;
        }
    }

    [Fact]
    public async Task Role_without_connection_grant_cannot_use_connection()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var conn = await (await scenario.Admin.GetAsync($"/api/connections/{scenario.ConnectionId}")).ReadApiAsync();
        var update = JsonSerializer.Deserialize<Dictionary<string, object?>>(conn.Data.GetRawText())!;
        update["allowedRoles"] = new[] { "Analyst" };
        update.Remove("password");
        var res = await scenario.Admin.PutJsonAsync($"/api/connections/{scenario.ConnectionId}", update);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var dev = await scenario.LoginAsAsync("Developer");
        Assert.Equal(HttpStatusCode.Forbidden, (await dev.GetAsync($"/api/schema/{scenario.ConnectionId}")).StatusCode);
    }
}
