namespace AIQueryGenerator.Shared.Exceptions;

/// <summary>Stable machine-readable error codes returned to clients.</summary>
public static class ErrorCodes
{
    public const string NotFound = "NOT_FOUND";
    public const string Forbidden = "FORBIDDEN";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string TokenExpired = "TOKEN_EXPIRED";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string Conflict = "CONFLICT";
    public const string BusinessRule = "BUSINESS_RULE";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string InternalError = "INTERNAL_ERROR";
    public const string RateLimited = "RATE_LIMITED";
    public const string RequestTooLarge = "REQUEST_TOO_LARGE";

    public const string AiNotConfigured = "AI_NOT_CONFIGURED";
    public const string AiTimeout = "AI_TIMEOUT";
    public const string AiRateLimited = "AI_RATE_LIMITED";
    public const string AiInvalidResponse = "AI_INVALID_RESPONSE";
    public const string AiUnavailable = "AI_UNAVAILABLE";

    public const string SqlInvalid = "SQL_INVALID";
    public const string SqlNotValidated = "SQL_NOT_VALIDATED";
    public const string SqlExecutionFailed = "SQL_EXECUTION_FAILED";
    public const string SqlTimeout = "SQL_TIMEOUT";
    public const string SqlPermissionDenied = "SQL_PERMISSION_DENIED";
    public const string DatabaseUnavailable = "DATABASE_UNAVAILABLE";
    public const string ConnectionInactive = "CONNECTION_INACTIVE";
    public const string ConcurrencyLimit = "CONCURRENT_QUERY_LIMIT";
    public const string InvalidParameters = "INVALID_PARAMETERS";
    public const string UnsafePrincipal = "UNSAFE_DATABASE_PRINCIPAL";
}
