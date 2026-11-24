using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Application.Connections;
using AIQueryGenerator.Shared.Exceptions;
using AIQueryGenerator.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIQueryGenerator.Application.Sql;

public interface ISqlQueryService
{
    Task<SqlValidationResponse> ValidateAsync(ValidateSqlRequest request, CancellationToken ct);
    Task<QueryResultDto> ExecuteAsync(ExecuteSqlRequest request, CancellationToken ct);
    FormatSqlResponse Format(FormatSqlRequest request);
    Task<ExplainPlanResponse> ExplainPlanAsync(ExplainPlanRequest request, CancellationToken ct);
}

public sealed class SqlQueryService(
    IApplicationDbContext db,
    IQueryExecutionPipeline pipeline,
    IConnectionAccessService access,
    IValidationTokenService validationTokens,
    ITargetDatabaseClient target,
    ISqlFormatter formatter,
    IExecutionPlanParser planParser,
    ICurrentUser currentUser,
    IOptions<ExecutionOptions> executionOptions,
    IOptions<SqlValidationOptions> validationOptions,
    TimeProvider clock) : ISqlQueryService
{
    public async Task<SqlValidationResponse> ValidateAsync(ValidateSqlRequest request, CancellationToken ct)
    {
        await access.GetViewableConnectionAsync(request.ConnectionId, ct);
        var names = request.Parameters?.Select(p => p.Name.TrimStart('@')).ToList() ?? [];
        var result = await pipeline.ValidateAsync(request.ConnectionId, request.Sql, names, ct);

        string? token = null;
        if (result.IsValid)
        {
            // Parameter values must also be well-formed for the token to be issued.
            ParameterBinder.Bind(request.Parameters, validationOptions.Value.MaxParameters, executionOptions.Value.MaxParameterValueLength);
            token = validationTokens.Issue(currentUser.UserId, request.ConnectionId, request.Sql, ParameterBinder.Signature(request.Parameters));
        }

        return ToResponse(result, token, clock.GetUtcNow().UtcDateTime);
    }

    public async Task<QueryResultDto> ExecuteAsync(ExecuteSqlRequest request, CancellationToken ct)
    {
        if (executionOptions.Value.RequireValidationToken &&
            !validationTokens.Verify(request.ValidationToken, currentUser.UserId, request.ConnectionId, request.Sql, ParameterBinder.Signature(request.Parameters)))
        {
            throw new BusinessRuleException("The SQL has changed or its validation has expired. Validate the latest SQL before executing it.", ErrorCodes.SqlNotValidated);
        }

        string? generatedSql = null;
        string? question = null;
        if (request.QueryHistoryId is { } historyId)
        {
            var history = await db.QueryHistory.AsNoTracking()
                .FirstOrDefaultAsync(h => h.QueryId == historyId && h.UserId == currentUser.UserId && !h.IsDeleted, ct)
                ?? throw new NotFoundException("Query history record not found.");
            generatedSql = history.GeneratedSql;
            question = history.UserQuestion;
        }

        // Users without SQL editing rights may only run SQL exactly as the AI generated it for them.
        if (!currentUser.HasPermission(Permissions.QueryEditSql) && (generatedSql is null || Normalize(generatedSql) != Normalize(request.Sql)))
            throw new ForbiddenException("Your role can execute AI-generated SQL but cannot run manually edited SQL.");

        return await pipeline.ExecuteAsync(new PipelineRequest
        {
            ConnectionId = request.ConnectionId,
            Sql = request.Sql,
            Parameters = request.Parameters,
            QueryHistoryId = request.QueryHistoryId,
            QueryTitle = request.QueryTitle,
            UserQuestion = question,
            GeneratedSql = generatedSql,
        }, ct);
    }

    public FormatSqlResponse Format(FormatSqlRequest request) => new(formatter.Format(request.Sql));

    public async Task<ExplainPlanResponse> ExplainPlanAsync(ExplainPlanRequest request, CancellationToken ct)
    {
        var connection = await access.GetQueryableConnectionAsync(request.ConnectionId, ct);
        var bound = ParameterBinder.Bind(request.Parameters, validationOptions.Value.MaxParameters, executionOptions.Value.MaxParameterValueLength);
        var validation = await pipeline.ValidateAsync(connection.ConnectionId, request.Sql, bound.Select(b => b.Name).ToList(), ct);
        if (!validation.IsValid) throw new SqlValidationFailedException(validation);

        var options = executionOptions.Value;
        var xml = await target.GetEstimatedPlanXmlAsync(new QueryExecutionRequest(connection, request.Sql, bound, 0,
            Math.Clamp(connection.CommandTimeoutSeconds, 1, options.MaxCommandTimeoutSeconds), options.MaxCellLength), ct);
        return new ExplainPlanResponse(planParser.Parse(xml), xml);
    }

    internal static string Normalize(string sql) => string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimEnd(';');

    internal static SqlValidationResponse ToResponse(SqlValidationResult r, string? token, DateTime at) =>
        new(r.IsValid, r.Message, r.Errors, r.Warnings, r.ReferencedObjects, r.ReferencedParameters, token, at);
}
