namespace AIQueryGenerator.Shared.Contracts;

/// <summary>Consistent envelope returned by every API endpoint.</summary>
public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Message { get; init; }
    public string? ErrorCode { get; init; }
    public IReadOnlyList<ApiError> Errors { get; init; } = Array.Empty<ApiError>();
    public string? TraceId { get; init; }

    public static ApiResponse<T> Ok(T data, string? message = null) => new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail(string message, string errorCode, IReadOnlyList<ApiError>? errors = null, string? traceId = null) =>
        new() { Success = false, Message = message, ErrorCode = errorCode, Errors = errors ?? Array.Empty<ApiError>(), TraceId = traceId };
}

public sealed record ApiError(string Field, string Message);
