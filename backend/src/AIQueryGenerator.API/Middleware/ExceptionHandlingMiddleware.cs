using System.Text.Json;
using AIQueryGenerator.Application.Sql;
using AIQueryGenerator.Shared.Contracts;
using AIQueryGenerator.Shared.Exceptions;
using Microsoft.AspNetCore.Http.Features;

namespace AIQueryGenerator.API.Middleware;

/// <summary>
/// Converts exceptions into the standard <see cref="ApiResponse{T}"/> envelope. Unexpected exceptions are logged with
/// full detail but clients only receive a generic message and a trace id — never stack traces, SQL connection
/// strings or infrastructure details.
/// </summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IWebHostEnvironment env)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request {Path} was cancelled by the client", context.Request.Path);
            if (!context.Response.HasStarted) context.Response.StatusCode = 499;
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        var traceId = context.Features.Get<IHttpActivityFeature>()?.Activity.TraceId.ToString() ?? context.TraceIdentifier;
        int status;
        ApiResponse<object> body;

        switch (ex)
        {
            case SqlValidationFailedException sql:
                status = sql.StatusCode;
                body = new ApiResponse<object>
                {
                    Success = false,
                    Message = sql.Message,
                    ErrorCode = sql.ErrorCode,
                    Errors = sql.ToApiErrors(),
                    Data = new { isValid = false, errors = sql.Result.Errors, warnings = sql.Result.Warnings },
                    TraceId = traceId,
                };
                break;
            case RequestValidationException validation:
                status = validation.StatusCode;
                body = ApiResponse<object>.Fail(validation.Message, validation.ErrorCode, validation.Errors, traceId);
                break;
            case AppException app:
                status = app.StatusCode;
                if (status >= 500) logger.LogWarning(ex, "Dependency failure {Code} on {Path}", app.ErrorCode, context.Request.Path);
                body = ApiResponse<object>.Fail(app.Message, app.ErrorCode, traceId: traceId);
                break;
            case BadHttpRequestException bad when bad.StatusCode == StatusCodes.Status413PayloadTooLarge:
                status = 413;
                body = ApiResponse<object>.Fail("The request is too large.", ErrorCodes.RequestTooLarge, traceId: traceId);
                break;
            case JsonException or BadHttpRequestException:
                status = 400;
                body = ApiResponse<object>.Fail("The request body is malformed.", ErrorCodes.ValidationFailed, traceId: traceId);
                break;
            default:
                status = 500;
                logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
                body = ApiResponse<object>.Fail(
                    env.IsDevelopment() ? $"An unexpected error occurred ({ex.GetType().Name})." : "An unexpected error occurred. Please try again later.",
                    ErrorCodes.InternalError, traceId: traceId);
                break;
        }

        if (context.Response.HasStarted) return;
        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(body, Json));
    }
}
