using AIQueryGenerator.Shared.Exceptions;
using Microsoft.Data.SqlClient;

namespace AIQueryGenerator.Infrastructure.TargetDatabase;

/// <summary>
/// Converts SqlExceptions into user-safe errors. Query-level errors (severity 11–16: invalid object, conversion,
/// permission denied) are shown because they describe the user's own SQL; connection and server errors are replaced
/// by generic messages and logged in full on the server.
/// </summary>
internal static class SqlErrorMapper
{
    private static readonly HashSet<int> ConnectionErrors = [-1, 2, 53, 64, 233, 4060, 10053, 10054, 10060, 10061, 11001, 18456, 18452, 40613, 40197, 40501];
    private static readonly HashSet<int> PermissionErrors = [229, 230, 262, 297, 300, 916, 15247];

    public static AppException Map(SqlException ex, int timeoutSeconds)
    {
        if (ex.Number == -2)
            return new ExternalServiceException($"The query exceeded the time limit of {timeoutSeconds} seconds and was cancelled.", ErrorCodes.SqlTimeout, 504, ex);

        if (ex.Number == 18456)
            return new ExternalServiceException("The application could not sign in to the target database. Ask an administrator to check the connection credentials.", ErrorCodes.DatabaseUnavailable, 503, ex);

        if (ConnectionErrors.Contains(ex.Number) || ex.Class >= 17)
            return new ExternalServiceException("The target database is currently unavailable. Please try again later.", ErrorCodes.DatabaseUnavailable, 503, ex);

        if (PermissionErrors.Contains(ex.Number))
            return new ExternalServiceException($"The database denied access: {Clean(ex.Message)}", ErrorCodes.SqlPermissionDenied, 403, ex);

        return new ExternalServiceException($"SQL Server error {ex.Number}: {Clean(ex.Message)}", ErrorCodes.SqlExecutionFailed, 422, ex);
    }

    private static string Clean(string message)
    {
        var first = message.Split('\n', 2)[0].Trim();
        return first.Length > 400 ? first[..400] : first;
    }
}
