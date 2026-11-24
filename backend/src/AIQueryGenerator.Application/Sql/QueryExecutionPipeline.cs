using System.Diagnostics;
using System.Text.Json;
using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Application.Common;
using AIQueryGenerator.Application.Connections;
using AIQueryGenerator.Application.Schema;
using AIQueryGenerator.Domain.Entities;
using AIQueryGenerator.Domain.Enums;
using AIQueryGenerator.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIQueryGenerator.Application.Sql;

public sealed record PipelineRequest
{
    public required int ConnectionId { get; init; }
    public required string Sql { get; init; }
    public IReadOnlyList<QueryParameterDto> Parameters { get; init; } = [];
    public long? QueryHistoryId { get; init; }
    public int? SavedQueryId { get; init; }
    public string? QueryTitle { get; init; }
    public string? UserQuestion { get; init; }
    public string? GeneratedSql { get; init; }
}

public interface IQueryExecutionPipeline
{
    /// <summary>
    /// The single path through which SQL reaches a target database: authorise connection → validate (AST + object
    /// authorisation) → bind parameters → apply limits → execute read-only → persist history and execution log.
    /// </summary>
    Task<QueryResultDto> ExecuteAsync(PipelineRequest request, CancellationToken ct);

    /// <summary>Validates SQL against the connection's authorised catalog without executing it.</summary>
    Task<SqlValidationResult> ValidateAsync(int connectionId, string sql, IReadOnlyCollection<string>? parameterNames, CancellationToken ct);
}

public sealed class QueryExecutionPipeline(
    IApplicationDbContext db,
    IConnectionAccessService access,
    ISchemaCatalogService catalogs,
    ISqlValidator validator,
    ITargetDatabaseClient target,
    IQueryConcurrencyLimiter limiter,
    IDataProtector protector,
    ICurrentUser currentUser,
    IOptions<SqlValidationOptions> validationOptions,
    IOptions<ExecutionOptions> executionOptions,
    TimeProvider clock,
    ILogger<QueryExecutionPipeline> logger) : IQueryExecutionPipeline
{
    public async Task<SqlValidationResult> ValidateAsync(int connectionId, string sql, IReadOnlyCollection<string>? parameterNames, CancellationToken ct)
    {
        var catalog = await catalogs.GetCatalogAsync(connectionId, ct);
        return validator.Validate(sql, new SqlValidationContext
        {
            Catalog = catalog,
            Options = validationOptions.Value,
            ProvidedParameterNames = parameterNames,
        });
    }

    public async Task<QueryResultDto> ExecuteAsync(PipelineRequest request, CancellationToken ct)
    {
        var options = executionOptions.Value;
        var connection = await access.GetQueryableConnectionAsync(request.ConnectionId, ct);
        var bound = ParameterBinder.Bind(request.Parameters, validationOptions.Value.MaxParameters, options.MaxParameterValueLength);

        var validation = await ValidateAsync(connection.ConnectionId, request.Sql, bound.Select(b => b.Name).ToList(), ct);
        if (!validation.IsValid)
        {
            await RecordAsync(request, connection, QueryExecutionStatus.ValidationFailed, 0, 0, false, ErrorCodes.SqlInvalid, validation.Message, validation, ct);
            throw new SqlValidationFailedException(validation);
        }

        var maxRows = Math.Clamp(connection.MaxRows, 1, options.MaxRowsCeiling);
        var timeout = Math.Clamp(connection.CommandTimeoutSeconds, 1, options.MaxCommandTimeoutSeconds);
        var executedAt = clock.GetUtcNow().UtcDateTime;
        var sw = Stopwatch.StartNew();

        QueryResultData data;
        try
        {
            using (await limiter.AcquireAsync(currentUser.UserId, ct))
            {
                data = await target.ExecuteReadOnlyAsync(
                    new QueryExecutionRequest(connection, request.Sql, bound, maxRows, timeout, options.MaxCellLength), ct);
            }
        }
        catch (AppException ex)
        {
            var status = ex.ErrorCode == ErrorCodes.SqlTimeout ? QueryExecutionStatus.TimedOut : QueryExecutionStatus.Failed;
            await RecordAsync(request, connection, status, (int)sw.ElapsedMilliseconds, 0, false, ex.ErrorCode, ex.Message, validation, CancellationToken.None);
            throw;
        }
        catch (OperationCanceledException)
        {
            await RecordAsync(request, connection, QueryExecutionStatus.Cancelled, (int)sw.ElapsedMilliseconds, 0, false, "CANCELLED", "The query was cancelled.", validation, CancellationToken.None);
            throw;
        }

        var historyId = await RecordAsync(request, connection, QueryExecutionStatus.Succeeded, data.DurationMs, data.Rows.Count, data.IsTruncated, null, null, validation, ct);

        var warnings = validation.Warnings.Select(w => w.Message).ToList();
        if (data.IsTruncated) warnings.Insert(0, $"Results were limited to the first {maxRows:N0} rows.");

        return new QueryResultDto(historyId, data.Columns, data.Rows, data.Rows.Count, data.IsTruncated, maxRows, data.DurationMs,
            executedAt, connection.ConnectionId, connection.ConnectionName, warnings);
    }

    private async Task<long?> RecordAsync(
        PipelineRequest request,
        DatabaseConnection connection,
        QueryExecutionStatus status,
        int durationMs,
        int rows,
        bool truncated,
        string? errorCode,
        string? errorMessage,
        SqlValidationResult validation,
        CancellationToken ct)
    {
        try
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var tables = string.Join(",", validation.ReferencedObjects).Truncate(1000);

            QueryHistory? history = null;
            if (request.QueryHistoryId is { } hid)
                history = await db.QueryHistory.FirstOrDefaultAsync(h => h.QueryId == hid && h.UserId == currentUser.UserId && !h.IsDeleted, ct);

            if (history is null || history.ExecutedSql is not null)
            {
                history = new QueryHistory
                {
                    UserId = currentUser.UserId,
                    ConnectionId = connection.ConnectionId,
                    QueryTitle = (request.QueryTitle ?? history?.QueryTitle ?? "Manual query").Truncate(200)!,
                    UserQuestion = request.UserQuestion ?? history?.UserQuestion,
                    GeneratedSql = request.GeneratedSql ?? history?.GeneratedSql,
                    Explanation = history?.Explanation,
                    ChartSuggestionJson = history?.ChartSuggestionJson,
                    PromptVersion = history?.PromptVersion,
                    AiModel = history?.AiModel,
                };
                db.QueryHistory.Add(history);
            }
            else if (!string.IsNullOrWhiteSpace(request.QueryTitle))
            {
                history.QueryTitle = request.QueryTitle.Truncate(200)!;
            }

            history.ExecutedSql = request.Sql;
            history.ParametersProtected = request.Parameters.Count > 0 ? protector.Protect(JsonSerializer.Serialize(request.Parameters)) : null;
            history.TablesUsed = tables;
            history.ExecutionDurationMs = durationMs;
            history.ReturnedRows = rows;
            history.ExecutionStatus = status;
            history.ErrorMessage = errorMessage.Truncate(1000);

            db.QueryExecutionLogs.Add(new QueryExecutionLog
            {
                Query = history,
                SavedQueryId = request.SavedQueryId,
                UserId = currentUser.UserId,
                ConnectionId = connection.ConnectionId,
                ExecutionStatus = status,
                ExecutionDurationMs = durationMs,
                RowsReturned = rows,
                WasTruncated = truncated,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage.Truncate(1000),
                SqlHash = Hashing.Sha256(request.Sql),
                TablesUsed = tables,
                ExecutedAt = now,
            });

            await db.SaveChangesAsync(ct);
            return history.QueryId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Execution results must still reach the user if persistence fails; the failure is logged for operators.
            logger.LogError(ex, "Failed to persist query history for connection {ConnectionId}", connection.ConnectionId);
            return null;
        }
    }
}
