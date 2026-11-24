using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Infrastructure.Persistence;
using AIQueryGenerator.Shared.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIQueryGenerator.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API pipeline (auth, authorisation, validation, execution pipeline, persistence) against an
/// in-memory SQLite application database, with the AI provider and target database replaced by fakes.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@test.local";
    public const string Password = "Str0ng!Passw0rd";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public FakeAiClient Ai { get; } = new();
    public FakeTargetDatabase Target { get; } = new();
    public FakeSchemaDiscovery Discovery { get; } = new();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public ApiFactory() => _connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = "integration-tests-signing-key-0123456789abcdef",
            ["Database:Initialization"] = "EnsureCreated",
            ["Seed:SuperAdmin:Email"] = AdminEmail,
            ["Seed:SuperAdmin:Password"] = Password,
            ["RateLimiting:AuthPerMinute"] = "1000",
            ["RateLimiting:GlobalPerMinute"] = "10000",
            ["RateLimiting:AiPerMinute"] = "1000",
            ["RateLimiting:ExecutePerMinute"] = "1000",
            ["Auth:RefreshCookieSecure"] = "false",
            ["Serilog:MinimumLevel:Default"] = "Warning",
            ["Swagger:Enabled"] = "true",
        }));

        builder.ConfigureServices(services =>
        {
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));
            services.RemoveAll<IAiClientFactory>();
            services.AddSingleton<IAiClientFactory>(Ai);
            services.RemoveAll<ITargetDatabaseClient>();
            services.AddSingleton<ITargetDatabaseClient>(Target);
            services.RemoveAll<ISchemaDiscoveryService>();
            services.AddSingleton<ISchemaDiscoveryService>(Discovery);
        });
    }

    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, BaseAddress = new Uri("http://localhost") });

    public async Task<HttpClient> LoginAsync(string email = AdminEmail, string password = Password)
    {
        var client = CreateApiClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, Json);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>(Json);
        if (body is not { Success: true }) throw new InvalidOperationException($"Login failed for {email}: {body?.Message}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.Data.GetProperty("accessToken").GetString());
        return client;
    }

    public async Task<T> ScopeAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}

public static class HttpExtensions
{
    public static async Task<ApiResponse<JsonElement>> ReadApiAsync(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>(ApiFactory.Json))!;

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body) =>
        client.PostAsJsonAsync(url, body, ApiFactory.Json);

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string url, object body) =>
        client.PutAsJsonAsync(url, body, ApiFactory.Json);
}
