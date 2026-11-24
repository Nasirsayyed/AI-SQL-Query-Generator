using AIQueryGenerator.Shared.Contracts;
using AIQueryGenerator.Shared.Exceptions;

namespace AIQueryGenerator.Application.Sql;

/// <summary>Raised when SQL fails backend validation at execution time.</summary>
public sealed class SqlValidationFailedException(SqlValidationResult result)
    : AppException(result.Message, ErrorCodes.SqlInvalid, 422)
{
    public SqlValidationResult Result { get; } = result;

    public IReadOnlyList<ApiError> ToApiErrors() =>
        Result.Errors.Select(e => new ApiError(e.Code, e.Line is null ? e.Message : $"{e.Message} (line {e.Line}, column {e.Column})")).ToList();
}
