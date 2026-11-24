using System.Net;
using System.Text.Json;
using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Domain.Enums;
using AIQueryGenerator.Infrastructure.AI;
using AIQueryGenerator.Shared.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIQueryGenerator.UnitTests.Ai;

public class OpenAiClientTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private static (OpenAiCompatibleClient, StubHandler) Create(HttpStatusCode status, string body, AiProviderType type = AiProviderType.OpenAI)
    {
        var handler = new StubHandler(status, body);
        var settings = new AiProviderSettings("test", type, "https://api.example.com/v1", "gpt-test", "sk-secret", 0, 500, 30, 40);
        return (new OpenAiCompatibleClient(new HttpClient(handler), settings, NullLogger.Instance), handler);
    }

    private static readonly AiJsonCompletionRequest Request = new("system", "user", "test_schema", """{"type":"object","additionalProperties":false,"required":["ok"],"properties":{"ok":{"type":"boolean"}}}""");

    [Fact]
    public async Task Sends_strict_json_schema_and_bearer_key()
    {
        var (client, handler) = Create(HttpStatusCode.OK, """{"model":"gpt-test","choices":[{"message":{"content":"{\"ok\":true}"},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":2}}""");
        var result = await client.CompleteJsonAsync(Request, CancellationToken.None);

        Assert.Equal("{\"ok\":true}", result.Content);
        Assert.Equal(5, result.PromptTokens);
        Assert.Equal("https://api.example.com/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        var format = body.RootElement.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("json_schema").GetProperty("strict").GetBoolean());
        Assert.Equal("system", body.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task Azure_uses_deployment_url_and_api_key_header()
    {
        var (client, handler) = Create(HttpStatusCode.OK, """{"choices":[{"message":{"content":"{}"}}]}""", AiProviderType.AzureOpenAI);
        await client.CompleteJsonAsync(Request, CancellationToken.None);
        Assert.Contains("/openai/deployments/gpt-test/chat/completions?api-version=", handler.Request!.RequestUri!.ToString());
        Assert.True(handler.Request.Headers.Contains("api-key"));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, ErrorCodes.AiRateLimited)]
    [InlineData(HttpStatusCode.Unauthorized, ErrorCodes.AiNotConfigured)]
    [InlineData(HttpStatusCode.InternalServerError, ErrorCodes.AiUnavailable)]
    [InlineData(HttpStatusCode.BadRequest, ErrorCodes.AiUnavailable)]
    public async Task Maps_http_errors(HttpStatusCode status, string code)
    {
        var (client, _) = Create(status, """{"error":{"message":"nope"}}""");
        var ex = await Assert.ThrowsAsync<ExternalServiceException>(() => client.CompleteJsonAsync(Request, CancellationToken.None));
        Assert.Equal(code, ex.ErrorCode);
        Assert.DoesNotContain("sk-secret", ex.Message);
    }

    [Theory]
    [InlineData("""{"choices":[{"message":{"content":null,"refusal":"I cannot"}}]}""")]
    [InlineData("""{"choices":[{"message":{"content":"{\"ok\":"},"finish_reason":"length"}]}""")]
    [InlineData("""not json""")]
    [InlineData("""{"choices":[]}""")]
    public async Task Rejects_refusals_truncation_and_malformed_bodies(string body)
    {
        var (client, _) = Create(HttpStatusCode.OK, body);
        var ex = await Assert.ThrowsAsync<ExternalServiceException>(() => client.CompleteJsonAsync(Request, CancellationToken.None));
        Assert.Equal(ErrorCodes.AiInvalidResponse, ex.ErrorCode);
    }

    [Fact]
    public void Prompt_provider_loads_all_versioned_prompts()
    {
        var provider = new EmbeddedPromptProvider(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        foreach (var name in new[] { PromptNames.SqlGeneration, PromptNames.SqlExplanation, PromptNames.ChartRecommendation, PromptNames.QueryOptimization })
        {
            var prompt = provider.Get(name);
            Assert.Equal("v1", prompt.Version);
            Assert.False(string.IsNullOrWhiteSpace(prompt.Text));
        }
        Assert.Contains("Never invent tables or columns", provider.Get(PromptNames.SqlGeneration).Text);
    }
}
