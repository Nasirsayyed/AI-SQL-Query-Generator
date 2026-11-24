using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Application.Schema;
using AIQueryGenerator.Application.Sql;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AIQueryGenerator.Infrastructure.Sql;

/// <summary>
/// AST-based T-SQL validator. The SQL is parsed with the official SQL Server parser (ScriptDom); comments and
/// whitespace are tokens, not syntax nodes, so they cannot hide statements. Validation is allowlist based:
/// exactly one SELECT statement, composed only of explicitly permitted syntax node types and built-in functions,
/// referencing only authorised objects and columns.
/// This is defence in depth: the target connection must ALSO use a least-privilege, SELECT-only database principal.
/// </summary>
public sealed class ScriptDomSqlValidator : ISqlValidator
{
    public SqlValidationResult Validate(string sql, SqlValidationContext context)
    {
        var result = new SqlValidationResult();
        var options = context.Options;

        if (string.IsNullOrWhiteSpace(sql))
            return Finish(result.Error(SqlValidationCodes.Empty, "The SQL query is empty."));

        if (sql.Length > options.MaxQueryLength)
            return Finish(result.Error(SqlValidationCodes.TooLong, $"The SQL query exceeds the maximum length of {options.MaxQueryLength} characters."));

        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        TSqlFragment fragment;
        IList<ParseError> parseErrors;
        using (var reader = new StringReader(sql))
            fragment = parser.Parse(reader, out parseErrors);

        if (parseErrors.Count > 0)
        {
            foreach (var e in parseErrors.Take(5))
                result.Error(SqlValidationCodes.Syntax, $"Syntax error: {e.Message}", e.Line, e.Column);
            return Finish(result);
        }

        if (fragment is not TSqlScript script)
            return Finish(result.Error(SqlValidationCodes.Syntax, "The SQL could not be parsed."));

        var statements = script.Batches.SelectMany(b => b.Statements).ToList();
        if (statements.Count == 0)
            return Finish(result.Error(SqlValidationCodes.Empty, "The SQL query does not contain a statement."));

        if (script.Batches.Count > 1 || statements.Count > 1)
            return Finish(result.Error(SqlValidationCodes.MultipleStatements, "Only a single SQL statement is allowed. Multiple statements or batches were detected."));

        var statement = statements[0];
        if (statement is not SelectStatement select || statement is SelectStatementSnippet)
            return Finish(result.Error(SqlValidationCodes.StatementNotAllowed,
                $"{DescribeStatement(statement)} statements are not allowed. Only read-only SELECT queries are permitted."));

        if (select.Into is not null)
            return Finish(result.Error(SqlValidationCodes.SelectInto, "SELECT INTO is not allowed because it creates a table."));
        if (select.On is not null)
            result.Error(SqlValidationCodes.ConstructNotAllowed, "Filegroup clauses are not allowed.");
        if (select.ComputeClauses is { Count: > 0 })
            result.Error(SqlValidationCodes.ConstructNotAllowed, "COMPUTE clauses are not allowed.");
        if (select.OptimizerHints is { Count: > 0 })
            result.Error(SqlValidationCodes.ConstructNotAllowed, "Query hints (OPTION clause) are not allowed.");

        var walker = new QueryWalker(options);
        select.Accept(walker);

        foreach (var issue in walker.Errors)
            result.Errors.Add(issue);

        if (walker.NodeCount > options.MaxSyntaxNodes)
            result.Error(SqlValidationCodes.TooComplex, $"The query is too complex ({walker.NodeCount} syntax nodes; maximum {options.MaxSyntaxNodes}).");
        if (walker.MaxDepth > options.MaxSubqueryDepth)
            result.Error(SqlValidationCodes.TooComplex, $"Subqueries are nested too deeply (depth {walker.MaxDepth}; maximum {options.MaxSubqueryDepth}).");

        result.JoinCount = walker.JoinCount;
        if (walker.JoinCount > options.MaxJoins)
            result.Error(SqlValidationCodes.TooManyJoins, $"The query uses {walker.JoinCount} joins; the maximum is {options.MaxJoins}.");

        var top = FirstQuerySpecification(select.QueryExpression);
        result.OutputColumnCount = top?.SelectElements.Count ?? 0;
        if (result.OutputColumnCount > options.MaxOutputColumns)
            result.Error(SqlValidationCodes.TooManyColumns, $"The query returns {result.OutputColumnCount} columns; the maximum is {options.MaxOutputColumns}.");

        result.HasTopOrPaging = top?.TopRowFilter is not null || select.QueryExpression.OffsetClause is not null;
        result.HasAggregation = walker.HasAggregation;

        if (result.Errors.Count == 0)
            new ObjectAuthorizer(context.Catalog, options, walker, result).Authorize();

        ValidateParameters(walker, context, result);

        if (walker.HasSelectStar)
            result.Warning("SELECT_STAR", "Avoid SELECT *; list explicit columns.");
        if (result.IsValid && !result.HasTopOrPaging && !result.HasAggregation)
            result.Warning("ROW_LIMIT", "The query has no TOP/OFFSET clause; returned rows will be capped at the configured maximum.");

        return Finish(result);
    }

    private static void ValidateParameters(QueryWalker walker, SqlValidationContext context, SqlValidationResult result)
    {
        var referenced = walker.Variables.Select(v => v.TrimStart('@')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        result.ReferencedParameters.AddRange(referenced);

        if (referenced.Count > context.Options.MaxParameters)
            result.Error(SqlValidationCodes.Parameter, $"The query references too many parameters (maximum {context.Options.MaxParameters}).");

        if (context.ProvidedParameterNames is null) return;

        var provided = new HashSet<string>(context.ProvidedParameterNames.Select(p => p.TrimStart('@')), StringComparer.OrdinalIgnoreCase);
        foreach (var missing in referenced.Where(r => !provided.Contains(r)))
            result.Error(SqlValidationCodes.Parameter, $"A value for parameter @{missing} must be supplied.");
        foreach (var unused in provided.Where(p => !referenced.Contains(p, StringComparer.OrdinalIgnoreCase)))
            result.Warning("UNUSED_PARAMETER", $"Parameter @{unused} is not used by the query.");
    }

    private static QuerySpecification? FirstQuerySpecification(QueryExpression? expression) => expression switch
    {
        QuerySpecification spec => spec,
        BinaryQueryExpression binary => FirstQuerySpecification(binary.FirstQueryExpression),
        QueryParenthesisExpression paren => FirstQuerySpecification(paren.QueryExpression),
        _ => null,
    };

    private static SqlValidationResult Finish(SqlValidationResult result)
    {
        result.Message = result.IsValid
            ? "The query is valid and read-only."
            : result.Errors.Any(e => e.Code is SqlValidationCodes.StatementNotAllowed or SqlValidationCodes.SelectInto or SqlValidationCodes.MultipleStatements)
                ? "Only read-only SELECT queries are allowed."
                : result.Errors[0].Message;
        return result;
    }

    internal static string DescribeStatement(TSqlStatement statement)
    {
        var name = statement.GetType().Name;
        if (name.EndsWith("Statement", StringComparison.Ordinal)) name = name[..^"Statement".Length];
        return name switch
        {
            "Execute" or "ExecuteAs" => "EXEC/EXECUTE",
            "TruncateTable" => "TRUNCATE",
            "Use" => "USE",
            "BeginTransaction" or "CommitTransaction" or "RollbackTransaction" or "SaveTransaction" or "BeginEndBlock" => "Transaction control",
            "DeclareVariable" or "SetVariable" or "DeclareTableVariable" => "Variable declaration/assignment",
            "WaitFor" => "WAITFOR",
            _ => SplitWords(name).ToUpperInvariant(),
        };
    }

    private static string SplitWords(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));
}
