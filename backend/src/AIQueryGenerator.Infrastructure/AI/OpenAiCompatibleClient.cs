using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Domain.Enums;
using AIQueryGenerator.Shared.Exceptions;
using Microsoft.Extensions.Logging;

namespace AIQueryGenerator.Infrastructure.AI;

public sealed record AiProviderSettings(
    string Name,
    AiProviderType ProviderType,
    string BaseUrl,
    string Model,
    string ApiKey,
    double Temperature,
    int MaxOutputTokens,
    int TimeoutSeconds,
    int MaxSchemaTables);

/// <summary>
/// Chat Completions client for OpenAI, Azure OpenAI and OpenAI-compatible endpoints, using structured outputs
/// (response_format = json_schema, strict) so the model must return a single JSON object matching the schema.
/// The API key stays on the server and is never logged.
/// </summary>
public sealed class OpenAiCompatibleClient(HttpClient http, AiProviderSettings settings, ILogger logger) : IAiCompletionClient
{
    private const string AzureApiVersion = "2024-10-21";

    public string ProviderName => settings.Name;
    public string Model => settings.Model;
    public int MaxSchemaTables => settings.MaxSchemaTables;

    public async Task<AiCompletionResult> CompleteJsonAsync(AiJsonCompletionRequest request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        using var message = new HttpRequestMessage(HttpMethod.Post, BuildUri())
        {
            Content = new StringContent(BuildBody(request).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (settings.ProviderType == AiProviderType.AzureOpenAI) message.Headers.Add("api-key", settings.ApiKey);
        else message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        var sw = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, HttpCompletionOption.ResponseContentRead, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ExternalServiceException($"The AI provider did not respond within {settings.TimeoutSeconds} seconds. Please try again.", ErrorCodes.AiTimeout, 504);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "AI provider request failed");
            throw new ExternalServiceException("The AI provider could not be reached. Please try again later.", ErrorCodes.AiUnavailable, 503, ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
            logger.LogInformation("AI completion {Schema} with {Model} returned {Status} in {Elapsed} ms", request.SchemaName, settings.Model, (int)response.StatusCode, sw.ElapsedMilliseconds);

            if (!response.IsSuccessStatusCode)
                throw MapError(response.StatusCode, body);

            return ParseCompletion(body);
        }
    }

    private Uri BuildUri()
    {
        var baseUrl = settings.BaseUrl.TrimEnd('/');
        return settings.ProviderType == AiProviderType.AzureOpenAI
            ? new Uri($"{baseUrl}/openai/deployments/{Uri.EscapeDataString(settings.Model)}/chat/completions?api-version={AzureApiVersion}")
            : new Uri($"{baseUrl}/chat/completions");
    }

    private JsonObject BuildBody(AiJsonCompletionRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = settings.Model,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = request.UserPrompt },
            },
            ["temperature"] = request.Temperature ?? settings.Temperature,
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = request.SchemaName,
                    ["strict"] = true,
                    ["schema"] = JsonNode.Parse(request.JsonSchema),
                },
            },
        };
        var tokenField = settings.ProviderType == AiProviderType.OpenAICompatible ? "max_tokens" : "max_completion_tokens";
        body[tokenField] = request.MaxOutputTokens ?? settings.MaxOutputTokens;
        return body;
    }

    internal AiCompletionResult ParseCompletion(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var choice = root.GetProperty("choices")[0];
            var msg = choice.GetProperty("message");

            if (msg.TryGetProperty("refusal", out var refusal) && refusal.ValueKind == JsonValueKind.String)
                throw new ExternalServiceException("The AI model declined to answer this request. Try rephrasing the question.", ErrorCodes.AiInvalidResponse, 502);

            if (choice.TryGetProperty("finish_reason", out var finish) && finish.GetString() == "length")
                throw new ExternalServiceException("The AI response was cut off before completion. Try a simpler question.", ErrorCodes.AiInvalidResponse, 502);

            var content = msg.GetProperty("content").GetString() ?? string.Empty;
            var model = root.TryGetProperty("model", out var m) ? m.GetString() ?? settings.Model : settings.Model;
            int? promptTokens = null, completionTokens = null;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var pt)) promptTokens = pt.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var ctk)) completionTokens = ctk.GetInt32();
            }
            return new AiCompletionResult(content, model, promptTokens, completionTokens);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new ExternalServiceException("The AI provider returned an unexpected response.", ErrorCodes.AiInvalidResponse, 502, ex);
        }
    }

    private ExternalServiceException MapError(HttpStatusCode status, string body)
    {
        // Provider error bodies can echo request content; log a bounded excerpt only.
        logger.LogWarning("AI provider error {Status}: {Body}", (int)status, body.Length > 500 ? body[..500] : body);
        return status switch
        {
            HttpStatusCode.TooManyRequests => new ExternalServiceException("The AI provider rate limit was reached. Please wait a moment and try again.", ErrorCodes.AiRateLimited, 503),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ExternalServiceException("The AI provider rejected the configured API key. An administrator must update the AI settings.", ErrorCodes.AiNotConfigured, 503),
            HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity =>
                new ExternalServiceException("The AI provider rejected the request. Check that the configured model supports structured JSON output.", ErrorCodes.AiUnavailable, 502),
            _ => new ExternalServiceException("The AI provider is temporarily unavailable. Please try again later.", ErrorCodes.AiUnavailable, 503),
        };
    }
}
