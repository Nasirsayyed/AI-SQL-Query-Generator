using AIQueryGenerator.API.Infrastructure;
using AIQueryGenerator.Application.Sql;
using AIQueryGenerator.Shared.Contracts;
using AIQueryGenerator.Shared.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AIQueryGenerator.API.Controllers;

[Route("api/sql")]
public sealed class SqlController(ISqlQueryService sql) : ApiControllerBase
{
    /// <summary>
    /// Validates SQL with the AST validator and object-level authorisation. A successful result includes a
    /// validationToken bound to this exact SQL and parameter set, required by /execute.
    /// </summary>
    [HttpPost("validate")]
    [HasPermission(Permissions.QueryExecute)]
    public async Task<ActionResult<ApiResponse<SqlValidationResponse>>> Validate(ValidateSqlRequest request, CancellationToken ct) =>
        Success(await sql.ValidateAsync(request, ct));

    /// <summary>Executes previously validated SQL read-only, with row, time and concurrency limits. The SQL is revalidated.</summary>
    [HttpPost("execute")]
    [HasPermission(Permissions.QueryExecute)]
    [EnableRateLimiting("execute")]
    [ProducesResponseType(typeof(ApiResponse<QueryResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<QueryResultDto>>> Execute(ExecuteSqlRequest request, CancellationToken ct) =>
        Success(await sql.ExecuteAsync(request, ct));

    /// <summary>Formats SQL (keywords upper-cased, one clause per line). Comments are not preserved.</summary>
    [HttpPost("format")]
    [HasPermission(Permissions.QueryGenerate)]
    public ActionResult<ApiResponse<FormatSqlResponse>> Format(FormatSqlRequest request) => Success(sql.Format(request));

    /// <summary>Returns the estimated execution plan (SHOWPLAN_XML) without running the query.</summary>
    [HttpPost("explain-plan")]
    [HasPermission(Permissions.QueryEditSql)]
    [EnableRateLimiting("execute")]
    public async Task<ActionResult<ApiResponse<ExplainPlanResponse>>> ExplainPlan(ExplainPlanRequest request, CancellationToken ct) =>
        Success(await sql.ExplainPlanAsync(request, ct));
}
