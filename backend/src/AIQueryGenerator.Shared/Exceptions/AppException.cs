namespace AIQueryGenerator.Shared.Exceptions;

/// <summary>
/// Base type for expected, user-presentable failures. The message is safe to show to clients;
/// technical details must go into <see cref="Exception.InnerException"/> and logs only.
/// </summary>
public abstract class AppException : Exception
{
    protected AppException(string message, string errorCode, int statusCode, Exception? inner = null) : base(message, inner)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }
    public int StatusCode { get; }
}

public sealed class NotFoundException(string message) : AppException(message, ErrorCodes.NotFound, 404);

public sealed class ForbiddenException(string message = "You do not have permission to perform this action.")
    : AppException(message, ErrorCodes.Forbidden, 403);

public sealed class UnauthorizedAppException(string message = "Authentication is required.", string code = ErrorCodes.Unauthorized)
    : AppException(message, code, 401);

public sealed class ConflictException(string message) : AppException(message, ErrorCodes.Conflict, 409);

public sealed class BusinessRuleException(string message, string errorCode = ErrorCodes.BusinessRule)
    : AppException(message, errorCode, 422);

public sealed class RequestValidationException : AppException
{
    public RequestValidationException(IReadOnlyList<Contracts.ApiError> errors, string message = "One or more validation errors occurred.")
        : base(message, ErrorCodes.ValidationFailed, 400) => Errors = errors;

    public IReadOnlyList<Contracts.ApiError> Errors { get; }
}

/// <summary>Raised when an external dependency (AI provider, target database) fails.</summary>
public sealed class ExternalServiceException(string message, string errorCode, int statusCode = 502, Exception? inner = null)
    : AppException(message, errorCode, statusCode, inner);
