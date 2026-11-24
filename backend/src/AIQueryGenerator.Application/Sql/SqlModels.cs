using AIQueryGenerator.Application.Schema;

namespace AIQueryGenerator.Application.Sql;

public enum SqlParameterType
{
    String,
    Int,
    BigInt,
    Decimal,
    Bool,
    Date,
    DateTime,
    Guid,
}

/// <summary>Administrator-configurable limits enforced by the validator.</summary>
public sealed class SqlValidationOptions
{
    public const string SectionName = "SqlValidation";

    public int MaxQueryLength { get; set; } = 20_000;
    public int MaxJoins { get; set; } = 12;
    public int MaxOutputColumns { get; set; } = 100;
    public int MaxSyntaxNodes { get; set; } = 5_000;
    public int MaxSubqueryDepth { get; set; } = 5;
    public int MaxParameters { get; set; } = 25;

    /// <summary>Schemas that represent SQL Server system catalogs and are rejected unless explicitly allowed.</summary>
    public string[] SystemSchemas { get; set; } = ["sys", "INFORMATION_SCHEMA"];
    public bool AllowSystemCatalogs { get; set; }

    /// <summary>Additional built-in function names to permit beyond the default allowlist.</summary>
    public string[] AdditionalAllowedFunctions { get; set; } = [];
}

public sealed class SqlValidationContext
{
    public required AuthorizedCatalog Catalog { get; init; }
    public required SqlValidationOptions Options { get; init; }

    /// <summary>Names (without @) of parameters the caller will bind. Null skips the parameter check (e.g. AI output).</summary>
    public IReadOnlyCollection<string>? ProvidedParameterNames { get; init; }
}

public sealed record SqlValidationIssue(string Code, string Message, int? Line = null, int? Column = null);

public sealed class SqlValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public string Message { get; set; } = string.Empty;
    public List<SqlValidationIssue> Errors { get; } = new();
    public List<SqlValidationIssue> Warnings { get; } = new();

    /// <summary>Referenced base objects as "schema.object".</summary>
    public List<string> ReferencedObjects { get; } = new();
    public List<string> ReferencedColumns { get; } = new();

    /// <summary>Variable names referenced in the query, without "@".</summary>
    public List<string> ReferencedParameters { get; } = new();
    public bool HasTopOrPaging { get; set; }
    public bool HasAggregation { get; set; }
    public int JoinCount { get; set; }
    public int OutputColumnCount { get; set; }

    public SqlValidationResult Error(string code, string message, int? line = null, int? column = null)
    {
        Errors.Add(new SqlValidationIssue(code, message, line, column));
        return this;
    }

    public SqlValidationResult Warning(string code, string message)
    {
        Warnings.Add(new SqlValidationIssue(code, message));
        return this;
    }
}

public static class SqlValidationCodes
{
    public const string Empty = "EMPTY";
    public const string TooLong = "TOO_LONG";
    public const string Syntax = "SYNTAX_ERROR";
    public const string MultipleStatements = "MULTIPLE_STATEMENTS";
    public const string StatementNotAllowed = "STATEMENT_NOT_ALLOWED";
    public const string SelectInto = "SELECT_INTO";
    public const string ConstructNotAllowed = "CONSTRUCT_NOT_ALLOWED";
    public const string DangerousFunction = "DANGEROUS_FUNCTION";
    public const string FunctionNotAllowed = "FUNCTION_NOT_ALLOWED";
    public const string CrossDatabase = "CROSS_DATABASE";
    public const string SystemCatalog = "SYSTEM_CATALOG";
    public const string UnknownObject = "UNKNOWN_OBJECT";
    public const string UnauthorizedObject = "UNAUTHORIZED_OBJECT";
    public const string UnknownColumn = "UNKNOWN_COLUMN";
    public const string UnauthorizedColumn = "UNAUTHORIZED_COLUMN";
    public const string StarExposesRestricted = "STAR_EXPOSES_RESTRICTED";
    public const string TooManyJoins = "TOO_MANY_JOINS";
    public const string TooComplex = "TOO_COMPLEX";
    public const string TooManyColumns = "TOO_MANY_COLUMNS";
    public const string Parameter = "PARAMETER";
    public const string SystemVariable = "SYSTEM_VARIABLE";
}

public sealed record ResultColumn(string Name, string DataType, string ClrType);

public sealed class QueryResultData
{
    public required IReadOnlyList<ResultColumn> Columns { get; init; }
    public required IReadOnlyList<object?[]> Rows { get; init; }
    public bool IsTruncated { get; init; }
    public int DurationMs { get; init; }
}

public sealed record PlanOperator(string PhysicalOp, string LogicalOp, double EstimatedRows, double EstimatedSubtreeCost, string? ObjectName);

public sealed record ExecutionPlanSummary(
    double EstimatedTotalCost,
    double EstimatedRows,
    IReadOnlyList<PlanOperator> TopOperators,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> MissingIndexes);
