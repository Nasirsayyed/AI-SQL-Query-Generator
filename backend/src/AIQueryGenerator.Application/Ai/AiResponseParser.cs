using System.Text.Json;
using AIQueryGenerator.Shared.Exceptions;

namespace AIQueryGenerator.Application.Ai;

/// <summary>Deserialises structured AI output. Any deviation from the expected object is an invalid response.</summary>
public static class AiResponseParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        MaxDepth = 16,
    };

    public static T Parse<T>(string content) where T : class
    {
        if (string.IsNullOrWhiteSpace(content))
            throw Invalid("The AI provider returned an empty response.");

        var trimmed = content.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
            throw Invalid("The AI provider returned a response that is not a JSON object.");

        try
        {
            return JsonSerializer.Deserialize<T>(trimmed, Options) ?? throw Invalid("The AI provider returned an empty JSON object.");
        }
        catch (JsonException ex)
        {
            throw Invalid("The AI provider returned malformed JSON.", ex);
        }
    }

    private static ExternalServiceException Invalid(string message, Exception? inner = null) =>
        new($"{message} Please try again or rephrase the question.", ErrorCodes.AiInvalidResponse, 502, inner);
}
