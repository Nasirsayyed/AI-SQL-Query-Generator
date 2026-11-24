using System.Net;
using System.Text.Json;

namespace AIQueryGenerator.IntegrationTests.Infrastructure;

/// <summary>Shared setup: users per role, an active demo connection with synced schema and permission rules.</summary>
public sealed class Scenario
{
    public required ApiFactory Factory { get; init; }
    public required HttpClient Admin { get; init; }
    public required int ConnectionId { get; init; }

    public static async Task<Scenario> CreateAsync(ApiFactory factory)
    {
        var admin = await factory.LoginAsync();
        foreach (var role in new[] { "Developer", "Analyst", "Viewer" })
        {
            var email = $"{role.ToLowerInvariant()}@test.local";
            var res = await admin.PostJsonAsync("/api/users", new { fullName = $"{role} User", email, password = ApiFactory.Password, roles = new[] { role } });
            if (res.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.Conflict))
                throw new InvalidOperationException($"Create user failed: {res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        }

        var name = "Demo " + Guid.NewGuid().ToString("N")[..6];
        var created = await (await admin.PostJsonAsync("/api/connections", new
        {
            connectionName = name,
            serverName = "sql.test.local",
            databaseName = "AIQueryDemoDB",
            authenticationType = "SqlPassword",
            username = "aiqg_reader",
            password = "S3cret-password!",
            allowedRoles = new[] { "Developer", "Analyst", "Viewer" },
        })).ReadApiAsync();
        if (!created.Success) throw new InvalidOperationException(created.Message);
        var id = created.Data.GetProperty("connectionId").GetInt32();

        Ensure(await (await admin.PostAsync($"/api/connections/{id}/test", null)).ReadApiAsync());
        Ensure(await (await admin.PostJsonAsync($"/api/connections/{id}/active", new { isActive = true })).ReadApiAsync());
        Ensure(await (await admin.PostAsync($"/api/connections/{id}/refresh-schema", null)).ReadApiAsync());
        Ensure(await (await admin.PutJsonAsync($"/api/connections/{id}/allowed-objects", new
        {
            rules = new object[]
            {
                new { objectType = "Schema", schemaName = "dbo", accessLevel = "Allow" },
                new { objectType = "Table", schemaName = "dbo", objectName = "Secrets", accessLevel = "Deny" },
                new { objectType = "Column", schemaName = "dbo", objectName = "Customers", columnName = "Email", accessLevel = "Deny" },
            },
        })).ReadApiAsync());

        return new Scenario { Factory = factory, Admin = admin, ConnectionId = id };
    }

    public Task<HttpClient> LoginAsAsync(string role) => Factory.LoginAsync($"{role.ToLowerInvariant()}@test.local");

    private static void Ensure(Shared.Contracts.ApiResponse<JsonElement> response)
    {
        if (!response.Success) throw new InvalidOperationException($"{response.ErrorCode}: {response.Message}");
    }

    public static string GenerationResponse(string sql, string? clarification = null, params object[] parameters) => JsonSerializer.Serialize(new
    {
        success = clarification is null,
        needsClarification = clarification is not null,
        clarificationQuestion = clarification,
        sqlQuery = clarification is null ? sql : null,
        queryTitle = "Top Customers by Sales",
        explanation = "Sums order totals per customer.",
        tablesUsed = new[] { "dbo.Customers", "dbo.Orders" },
        columnsUsed = new[] { "CustomerName", "TotalAmount" },
        parameters,
        warnings = Array.Empty<string>(),
        chartSuggestion = new { recommended = true, chartType = "bar", xAxis = "CustomerName", yAxis = "TotalSales", aggregation = "none", title = (string?)null, reason = "Category comparison" },
    });

    public const string TopCustomersSql =
        "SELECT TOP (10) c.CustomerName, SUM(o.TotalAmount) AS TotalSales FROM dbo.Customers c JOIN dbo.Orders o ON o.CustomerId = c.CustomerId GROUP BY c.CustomerName ORDER BY TotalSales DESC";
}
