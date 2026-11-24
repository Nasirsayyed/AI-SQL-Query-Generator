using System.Net;
using System.Text.Json;
using AIQueryGenerator.IntegrationTests.Infrastructure;

namespace AIQueryGenerator.IntegrationTests;

public class QueryWorkflowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Generate_validates_but_does_not_execute_sql()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var dev = await scenario.LoginAsAsync("Developer");
        var executedBefore = factory.Target.Executed.Count;
        factory.Ai.Responses.Enqueue(Scenario.GenerationResponse(Scenario.TopCustomersSql));

        var body = await (await dev.PostJsonAsync("/api/ai/generate-sql", new { connectionId = scenario.ConnectionId, question = "Show the top 10 customers by sales" })).ReadApiAsync();

        Assert.True(body.Success, body.Message);
        Assert.True(body.Data.GetProperty("success").GetBoolean());
        Assert.True(body.Data.GetProperty("validation").GetProperty("isValid").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.Data.GetProperty("validation").GetProperty("validationToken").GetString()));
        Assert.Equal("bar", body.Data.GetProperty("chartSuggestion").GetProperty("chartType").GetString());
        Assert.Equal(executedBefore, factory.Target.Executed.Count);

        // The prompt contained only permitted schema: no restricted column or denied table.
        var prompt = factory.Ai.Requests.Last().UserPrompt;
        Assert.Contains("[dbo].[Customers]", prompt);
        Assert.DoesNotContain("[Email]", prompt);
        Assert.DoesNotContain("Secrets", prompt);
        Assert.Contains("<question>", prompt);
    }

    [Fact]
    public async Task Clarification_is_returned_instead_of_sql()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var dev = await scenario.LoginAsAsync("Developer");
        factory.Ai.Responses.Enqueue(Scenario.GenerationResponse("", "Which date range do you mean by recent?"));

        var body = await (await dev.PostJsonAsync("/api/ai/generate-sql", new { connectionId = scenario.ConnectionId, question = "Show recent stuff" })).ReadApiAsync();
        Assert.True(body.Data.GetProperty("needsClarification").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.Data.GetProperty("sqlQuery").ValueKind);
    }

    [Fact]
    public async Task Invalid_ai_sql_is_repaired_once_then_reported()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var dev = await scenario.LoginAsAsync("Developer");
        factory.Ai.Responses.Enqueue(Scenario.GenerationResponse("SELECT c.Email FROM dbo.Customers c"));
        factory.Ai.Responses.Enqueue(Scenario.GenerationResponse("DELETE FROM dbo.Customers"));

        var body = await (await dev.PostJsonAsync("/api/ai/generate-sql", new { connectionId = scenario.ConnectionId, question = "emails" })).ReadApiAsync();
        Assert.False(body.Data.GetProperty("success").GetBoolean());
        Assert.False(body.Data.GetProperty("validation").GetProperty("isValid").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.Data.GetProperty("validation").GetProperty("validationToken").ValueKind);
        Assert.Contains("Previous attempt rejected", factory.Ai.Requests.Last().UserPrompt);
    }

    [Fact]
    public async Task Malformed_ai_response_returns_friendly_error()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var dev = await scenario.LoginAsAsync("Developer");
        factory.Ai.Responses.Enqueue("Sure! Here is your SQL: SELECT 1");

        var response = await dev.PostJsonAsync("/api/ai/generate-sql", new { connectionId = scenario.ConnectionId, question = "anything at all" });
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("AI_INVALID_RESPONSE", (await response.ReadApiAsync()).ErrorCode);
    }

    [Fact]
    public async Task Unconfigured_ai_returns_503()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var dev = await scenario.LoginAsAsync("Developer");
        factory.Ai.Configured = false;
        try
        {
            var response = await dev.PostJsonAsync("/api/ai/generate-sql", new { connectionId = scenario.ConnectionId, question = "top customers" });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("AI_NOT_CONFIGURED", (await response.ReadApiAsync()).ErrorCode);
        }
        finally
        {
            factory.Ai.Configured = true;
        }
    }

    [Fact]
    public async Task Execute_requires_validation_token_for_exact_sql()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var dev = await scenario.LoginAsAsync("Developer");

        var noToken = await dev.PostJsonAsync("/api/sql/execute", new { connectionId = scenario.ConnectionId, sql = Scenario.TopCustomersSql, parameters = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noToken.StatusCode);
        Assert.Equal("SQL_NOT_VALIDATED", (await noToken.ReadApiAsync()).ErrorCode);

        var validation = await (await dev.PostJsonAsync("/api/sql/validate", new { connectionId = scenario.ConnectionId, sql = Scenario.TopCustomersSql })).ReadApiAsync();
        var token = validation.Data.GetProperty("validationToken").GetString();

        // Token for one SQL text cannot authorise a different (edited) SQL text.
        var edited = await dev.PostJsonAsync("/api/sql/execute", new { connectionId = scenario.ConnectionId, sql = Scenario.TopCustomersSql + " ", parameters = Array.Empty<object>(), validationToken = token });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, edited.StatusCode);

        var ok = await dev.PostJsonAsync("/api/sql/execute", new { connectionId = scenario.ConnectionId, sql = Scenario.TopCustomersSql, parameters = Array.Empty<object>(), validationToken = token });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var result = await ok.ReadApiAsync();
        Assert.Equal(3, result.Data.GetProperty("rowCount").GetInt32());
        Assert.Equal(1000, result.Data.GetProperty("maxRows").GetInt32());
        Assert.False(result.Data.GetProperty("isTruncated").GetBoolean());
    }

    [Theory]
    [InlineData("DROP TABLE dbo.Customers", "STATEMENT_NOT_ALLOWED")]
    [InlineData("SELECT c.Email FROM dbo.Customers c", "UNAUTHORIZED_COLUMN")]
    [InlineData("SELECT s.Value FROM dbo.Secrets s", "UNAUTHORIZED_OBJECT")]
    [InlineData("SELECT 1; DELETE FROM dbo.Orders", "MULTIPLE_STATEMENTS")]
    public async Task Validate_rejects_unsafe_or_unauthorised_sql(string sql, string code)
    {
        var scenario = await Scenario.CreateAsync(factory);
        var dev = await scenario.LoginAsAsync("Developer");
        var body = await (await dev.PostJsonAsync("/api/sql/validate", new { connectionId = scenario.ConnectionId, sql })).ReadApiAsync();
        Assert.False(body.Data.GetProperty("isValid").GetBoolean());
        Assert.Contains(body.Data.GetProperty("errors").EnumerateArray(), e => e.GetProperty("code").GetString() == code);
        Assert.Equal(JsonValueKind.Null, body.Data.GetProperty("validationToken").ValueKind);
    }

    [Fact]
    public async Task Execute_revalidates_against_current_permissions()
    {
        // Even a correctly-signed token cannot make the pipeline execute SQL that fails validation now
        // (e.g. permissions were tightened after validation).
        var scenario = await Scenario.CreateAsync(factory);
        var dev = await scenario.LoginAsAsync("Developer");
        const string sql = "SELECT p.ProductName FROM dbo.Products p";
        var token = (await (await dev.PostJsonAsync("/api/sql/validate", new { connectionId = scenario.ConnectionId, sql })).ReadApiAsync())
            .Data.GetProperty("validationToken").GetString();

        await scenario.Admin.PutJsonAsync($"/api/connections/{scenario.ConnectionId}/allowed-objects", new
        {
            rules = new object[] { new { objectType = "Table", schemaName = "dbo", objectName = "Customers", accessLevel = "Allow" } },
        });

        var before = factory.Target.Executed.Count;
        var response = await dev.PostJsonAsync("/api/sql/execute", new { connectionId = scenario.ConnectionId, sql, parameters = Array.Empty<object>(), validationToken = token });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("SQL_INVALID", (await response.ReadApiAsync()).ErrorCode);
        Assert.Equal(before, factory.Target.Executed.Count);
    }

    [Fact]
    public async Task Parameters_are_bound_and_typed()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var dev = await scenario.LoginAsAsync("Developer");
        const string sql = "SELECT o.OrderId FROM dbo.Orders o WHERE o.OrderStatus = @Status AND o.TotalAmount > @MinAmount";
        var parameters = new object[] { new { name = "Status", type = "String", value = "Pending" }, new { name = "MinAmount", type = "Decimal", value = "100.50" } };

        var token = (await (await dev.PostJsonAsync("/api/sql/validate", new { connectionId = scenario.ConnectionId, sql, parameters })).ReadApiAsync())
            .Data.GetProperty("validationToken").GetString();
        var ok = await dev.PostJsonAsync("/api/sql/execute", new { connectionId = scenario.ConnectionId, sql, parameters, validationToken = token });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var executed = factory.Target.Executed.Last();
        Assert.Equal(100.50m, executed.Parameters.Single(p => p.Name == "MinAmount").Value);

        var bad = await dev.PostJsonAsync("/api/sql/validate", new
        {
            connectionId = scenario.ConnectionId, sql,
            parameters = new object[] { new { name = "Status", type = "String", value = "x" }, new { name = "MinAmount", type = "Decimal", value = "abc" } },
        });
        Assert.Equal("INVALID_PARAMETERS", (await bad.ReadApiAsync()).ErrorCode);
    }

    [Fact]
    public async Task Analyst_can_run_generated_sql_but_not_edited_sql()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var analyst = await scenario.LoginAsAsync("Analyst");
        factory.Ai.Responses.Enqueue(Scenario.GenerationResponse(Scenario.TopCustomersSql));
        var gen = await (await analyst.PostJsonAsync("/api/ai/generate-sql", new { connectionId = scenario.ConnectionId, question = "top customers by sales" })).ReadApiAsync();
        var historyId = gen.Data.GetProperty("queryHistoryId").GetInt64();
        var token = gen.Data.GetProperty("validation").GetProperty("validationToken").GetString();

        var ok = await analyst.PostJsonAsync("/api/sql/execute", new { connectionId = scenario.ConnectionId, sql = Scenario.TopCustomersSql, parameters = Array.Empty<object>(), validationToken = token, queryHistoryId = historyId });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        const string edited = "SELECT p.ProductName FROM dbo.Products p";
        var editedToken = (await (await analyst.PostJsonAsync("/api/sql/validate", new { connectionId = scenario.ConnectionId, sql = edited })).ReadApiAsync())
            .Data.GetProperty("validationToken").GetString();
        var denied = await analyst.PostJsonAsync("/api/sql/execute", new { connectionId = scenario.ConnectionId, sql = edited, parameters = Array.Empty<object>(), validationToken = editedToken, queryHistoryId = historyId });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task History_is_persisted_and_rerun_revalidates()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var dev = await scenario.LoginAsAsync("Developer");
        factory.Ai.Responses.Enqueue(Scenario.GenerationResponse(Scenario.TopCustomersSql));
        var gen = await (await dev.PostJsonAsync("/api/ai/generate-sql", new { connectionId = scenario.ConnectionId, question = "Top customers" })).ReadApiAsync();
        var historyId = gen.Data.GetProperty("queryHistoryId").GetInt64();
        var token = gen.Data.GetProperty("validation").GetProperty("validationToken").GetString();
        await dev.PostJsonAsync("/api/sql/execute", new { connectionId = scenario.ConnectionId, sql = Scenario.TopCustomersSql, parameters = Array.Empty<object>(), validationToken = token, queryHistoryId = historyId });

        var detail = await (await dev.GetAsync($"/api/query-history/{historyId}")).ReadApiAsync();
        var item = detail.Data.GetProperty("item");
        Assert.Equal("Succeeded", item.GetProperty("executionStatus").GetString());
        Assert.Equal(Scenario.TopCustomersSql, item.GetProperty("executedSql").GetString());
        Assert.Equal("Top customers", item.GetProperty("userQuestion").GetString());
        Assert.Equal(3, item.GetProperty("returnedRows").GetInt32());

        var list = await (await dev.GetAsync("/api/query-history?search=Top&pageSize=5")).ReadApiAsync();
        Assert.True(list.Data.GetProperty("totalCount").GetInt32() >= 1);

        var rerun = await dev.PostJsonAsync($"/api/query-history/{historyId}/rerun", new { });
        Assert.Equal(HttpStatusCode.OK, rerun.StatusCode);

        // Another user cannot see the record.
        var analyst = await scenario.LoginAsAsync("Analyst");
        Assert.Equal(HttpStatusCode.NotFound, (await analyst.GetAsync($"/api/query-history/{historyId}")).StatusCode);

        var logs = await factory.ScopeAsync(db => Task.FromResult(db.QueryExecutionLogs.Count(l => l.QueryId == historyId || l.ConnectionId == scenario.ConnectionId)));
        Assert.True(logs >= 2);

        Assert.Equal(HttpStatusCode.OK, (await dev.DeleteAsync($"/api/query-history/{historyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await dev.GetAsync($"/api/query-history/{historyId}")).StatusCode);
    }

    [Fact]
    public async Task Stored_parameters_are_encrypted_at_rest()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var dev = await scenario.LoginAsAsync("Developer");
        const string sql = "SELECT c.CustomerName FROM dbo.Customers c WHERE c.City = @City";
        var parameters = new object[] { new { name = "City", type = "String", value = "SensitiveCityValue" } };
        var token = (await (await dev.PostJsonAsync("/api/sql/validate", new { connectionId = scenario.ConnectionId, sql, parameters })).ReadApiAsync()).Data.GetProperty("validationToken").GetString();
        var result = await (await dev.PostJsonAsync("/api/sql/execute", new { connectionId = scenario.ConnectionId, sql, parameters, validationToken = token })).ReadApiAsync();
        var id = result.Data.GetProperty("queryHistoryId").GetInt64();

        var stored = await factory.ScopeAsync(db => Task.FromResult(db.QueryHistory.First(h => h.QueryId == id).ParametersProtected));
        Assert.NotNull(stored);
        Assert.DoesNotContain("SensitiveCityValue", stored);

        var detail = await (await dev.GetAsync($"/api/query-history/{id}")).ReadApiAsync();
        Assert.Equal("SensitiveCityValue", detail.Data.GetProperty("parameters")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task Saved_query_share_favorite_and_viewer_run()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var dev = await scenario.LoginAsAsync("Developer");
        var created = await dev.PostJsonAsync("/api/saved-queries", new
        {
            connectionId = scenario.ConnectionId, queryTitle = "Top customers", description = "Monthly report", sqlQuery = Scenario.TopCustomersSql,
            category = "Sales", isShared = true, parameters = Array.Empty<object>(),
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.ReadApiAsync()).Data.GetProperty("savedQueryId").GetInt32();

        var fav = await (await dev.PostAsync($"/api/saved-queries/{id}/favorite", null)).ReadApiAsync();
        Assert.True(fav.Data.GetProperty("isFavorite").GetBoolean());
        var favs = await (await dev.GetAsync("/api/saved-queries?favoritesOnly=true")).ReadApiAsync();
        Assert.Contains(favs.Data.GetProperty("items").EnumerateArray(), i => i.GetProperty("savedQueryId").GetInt32() == id);

        var viewer = await scenario.LoginAsAsync("Viewer");
        var shared = await (await viewer.GetAsync("/api/saved-queries?scope=shared")).ReadApiAsync();
        Assert.Contains(shared.Data.GetProperty("items").EnumerateArray(), i => i.GetProperty("savedQueryId").GetInt32() == id);
        Assert.Equal(HttpStatusCode.OK, (await viewer.PostAsync($"/api/saved-queries/{id}/run", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"/api/saved-queries/{id}")).StatusCode);
    }

    [Fact]
    public async Task Saving_invalid_sql_is_rejected()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var dev = await scenario.LoginAsAsync("Developer");
        var response = await dev.PostJsonAsync("/api/saved-queries", new
        {
            connectionId = scenario.ConnectionId, queryTitle = "Bad", sqlQuery = "UPDATE dbo.Orders SET TotalAmount = 0", parameters = Array.Empty<object>(),
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Analyst_cannot_share_queries()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var analyst = await scenario.LoginAsAsync("Analyst");
        var response = await analyst.PostJsonAsync("/api/saved-queries", new
        {
            connectionId = scenario.ConnectionId, queryTitle = "x", sqlQuery = Scenario.TopCustomersSql, isShared = true, parameters = Array.Empty<object>(),
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_reflects_recorded_activity()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var dev = await scenario.LoginAsAsync("Developer");
        const string sql = "SELECT p.ProductName FROM dbo.Products p";
        var token = (await (await dev.PostJsonAsync("/api/sql/validate", new { connectionId = scenario.ConnectionId, sql })).ReadApiAsync()).Data.GetProperty("validationToken").GetString();
        await dev.PostJsonAsync("/api/sql/execute", new { connectionId = scenario.ConnectionId, sql, parameters = Array.Empty<object>(), validationToken = token });

        var summary = await (await scenario.Admin.GetAsync("/api/dashboard/summary")).ReadApiAsync();
        Assert.True(summary.Data.GetProperty("totalQueries").GetInt32() >= 1);
        Assert.True(summary.Data.GetProperty("isGlobalScope").GetBoolean());

        var trends = await (await scenario.Admin.GetAsync("/api/dashboard/query-trends?days=7")).ReadApiAsync();
        Assert.Equal(7, trends.Data.GetArrayLength());

        var tables = await (await scenario.Admin.GetAsync("/api/dashboard/top-tables")).ReadApiAsync();
        Assert.Contains(tables.Data.EnumerateArray(), t => t.GetProperty("name").GetString() == "dbo.Products");

        Assert.Equal(HttpStatusCode.OK, (await scenario.Admin.GetAsync("/api/dashboard/execution-performance")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scenario.Admin.GetAsync("/api/dashboard/recent-activity")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scenario.Admin.GetAsync("/api/dashboard/top-users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dev.GetAsync("/api/dashboard/top-users")).StatusCode);
    }

    [Fact]
    public async Task Format_and_explain_plan_work()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var dev = await scenario.LoginAsAsync("Developer");
        var formatted = await (await dev.PostJsonAsync("/api/sql/format", new { sql = "select p.ProductName from dbo.Products p" })).ReadApiAsync();
        Assert.StartsWith("SELECT", formatted.Data.GetProperty("sql").GetString());

        var plan = await (await dev.PostJsonAsync("/api/sql/explain-plan", new { connectionId = scenario.ConnectionId, sql = "SELECT p.ProductName FROM dbo.Products p" })).ReadApiAsync();
        Assert.True(plan.Success, plan.Message);
        Assert.Equal(0.5, plan.Data.GetProperty("summary").GetProperty("estimatedTotalCost").GetDouble());
    }

    [Fact]
    public async Task Chart_suggestion_falls_back_to_heuristic_when_ai_unavailable()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var dev = await scenario.LoginAsAsync("Developer");
        factory.Ai.Configured = false;
        try
        {
            var body = await (await dev.PostJsonAsync("/api/ai/suggest-chart", new
            {
                question = "monthly revenue", rowCount = 12,
                columns = new[] { new { name = "OrderMonth", dataType = "date", clrType = "DateTime" }, new { name = "Revenue", dataType = "decimal", clrType = "Decimal" } },
            })).ReadApiAsync();
            Assert.Equal("heuristic", body.Data.GetProperty("source").GetString());
            Assert.Equal("line", body.Data.GetProperty("chartType").GetString());
        }
        finally
        {
            factory.Ai.Configured = true;
        }
    }

    [Fact]
    public async Task Audit_log_records_security_events()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var logs = await (await scenario.Admin.GetAsync("/api/audit-logs?action=connection&pageSize=50")).ReadApiAsync();
        var actions = logs.Data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("action").GetString()).ToList();
        Assert.Contains("connection.created", actions);
        Assert.Contains("connection.tested", actions);
        Assert.DoesNotContain("S3cret-password!", logs.Data.GetRawText());
    }

    [Fact]
    public async Task History_user_filter_list_requires_view_all()
    {
        var scenario = await Scenario.CreateAsync(factory);
        var dev = await scenario.LoginAsAsync("Developer");
        const string sql = "SELECT p.ProductName FROM dbo.Products p";
        var token = (await (await dev.PostJsonAsync("/api/sql/validate", new { connectionId = scenario.ConnectionId, sql })).ReadApiAsync()).Data.GetProperty("validationToken").GetString();
        await dev.PostJsonAsync("/api/sql/execute", new { connectionId = scenario.ConnectionId, sql, parameters = Array.Empty<object>(), validationToken = token });

        var users = await (await scenario.Admin.GetAsync("/api/query-history/users")).ReadApiAsync();
        Assert.Contains(users.Data.EnumerateArray(), u => u.GetProperty("fullName").GetString() == "Developer User");
        Assert.Equal(HttpStatusCode.Forbidden, (await dev.GetAsync("/api/query-history/users")).StatusCode);
    }
}
