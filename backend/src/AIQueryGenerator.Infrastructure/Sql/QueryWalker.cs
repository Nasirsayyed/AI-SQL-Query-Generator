using AIQueryGenerator.Application.Sql;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AIQueryGenerator.Infrastructure.Sql;

/// <summary>
/// Visits every node of a SELECT statement. Any node type outside <see cref="AllowedNodeTypes"/> is rejected
/// (explicit allowlist), and references to tables, columns, variables and functions are collected for authorisation.
/// </summary>
internal sealed class QueryWalker : TSqlFragmentVisitor
{
    private static readonly HashSet<Type> AllowedNodeTypes =
    [
        typeof(SelectStatement), typeof(QuerySpecification), typeof(BinaryQueryExpression), typeof(QueryParenthesisExpression),
        typeof(SelectScalarExpression), typeof(SelectStarExpression), typeof(FromClause), typeof(NamedTableReference),
        typeof(QualifiedJoin), typeof(UnqualifiedJoin), typeof(JoinParenthesisTableReference), typeof(QueryDerivedTable),
        typeof(InlineDerivedTable), typeof(RowValue), typeof(SchemaObjectName), typeof(Identifier), typeof(MultiPartIdentifier),
        typeof(IdentifierOrValueExpression), typeof(ColumnReferenceExpression), typeof(WhereClause),
        typeof(BooleanComparisonExpression), typeof(BooleanBinaryExpression), typeof(BooleanNotExpression),
        typeof(BooleanParenthesisExpression), typeof(BooleanIsNullExpression), typeof(BooleanTernaryExpression),
        typeof(InPredicate), typeof(LikePredicate), typeof(ExistsPredicate), typeof(SubqueryComparisonPredicate),
        typeof(DistinctPredicate), typeof(ScalarSubquery), typeof(GroupByClause), typeof(ExpressionGroupingSpecification),
        typeof(RollupGroupingSpecification), typeof(CubeGroupingSpecification), typeof(GroupingSetsGroupingSpecification),
        typeof(CompositeGroupingSpecification), typeof(GrandTotalGroupingSpecification), typeof(HavingClause),
        typeof(OrderByClause), typeof(ExpressionWithSortOrder), typeof(TopRowFilter), typeof(OffsetClause), typeof(FunctionCall),
        typeof(IntegerLiteral), typeof(StringLiteral), typeof(NumericLiteral), typeof(RealLiteral), typeof(MoneyLiteral),
        typeof(NullLiteral), typeof(BinaryLiteral), typeof(MaxLiteral), typeof(VariableReference), typeof(BinaryExpression),
        typeof(UnaryExpression), typeof(ParenthesisExpression), typeof(SearchedCaseExpression), typeof(SimpleCaseExpression),
        typeof(SearchedWhenClause), typeof(SimpleWhenClause), typeof(CastCall), typeof(ConvertCall), typeof(TryCastCall),
        typeof(TryConvertCall), typeof(ParseCall), typeof(TryParseCall), typeof(CoalesceExpression), typeof(NullIfExpression),
        typeof(IIfCall), typeof(LeftFunctionCall), typeof(RightFunctionCall), typeof(SqlDataTypeReference), typeof(OverClause),
        typeof(WindowFrameClause), typeof(WindowDelimiter), typeof(WindowClause), typeof(WindowDefinition),
        typeof(WithCtesAndXmlNamespaces), typeof(CommonTableExpression), typeof(TableHint), typeof(IndexTableHint),
        typeof(AtTimeZoneCall), typeof(WithinGroupClause), typeof(ParameterlessCall),
    ];

    /// <summary>Constructs that reach outside the database or have side effects; reported with a specific message.</summary>
    private static readonly Dictionary<Type, string> DangerousNodeTypes = new()
    {
        [typeof(OpenRowsetTableReference)] = "OPENROWSET",
        [typeof(BulkOpenRowset)] = "OPENROWSET(BULK)",
        [typeof(InternalOpenRowset)] = "OPENROWSET",
        [typeof(OpenQueryTableReference)] = "OPENQUERY",
        [typeof(AdHocTableReference)] = "OPENDATASOURCE",
        [typeof(OpenXmlTableReference)] = "OPENXML",
        [typeof(OpenJsonTableReference)] = "OPENJSON",
        [typeof(GlobalVariableExpression)] = "system variables (@@...)",
        [typeof(NextValueForExpression)] = "NEXT VALUE FOR (modifies a sequence)",
        [typeof(SelectSetVariable)] = "variable assignment",
        [typeof(SchemaObjectFunctionTableReference)] = "table-valued functions",
        [typeof(ExpressionCallTarget)] = "method calls",
        [typeof(MultiPartIdentifierCallTarget)] = "user-defined or schema-qualified functions",
        [typeof(UserDefinedTypeCallTarget)] = "user-defined type methods",
        [typeof(FullTextPredicate)] = "full-text predicates",
        [typeof(FullTextTableReference)] = "full-text table functions",
        [typeof(ChangeTableChangesTableReference)] = "CHANGETABLE",
        [typeof(ChangeTableVersionTableReference)] = "CHANGETABLE",
        [typeof(XmlForClause)] = "FOR XML",
        [typeof(JsonForClause)] = "FOR JSON",
        [typeof(BrowseForClause)] = "FOR BROWSE",
        [typeof(ReadOnlyForClause)] = "FOR READ ONLY",
        [typeof(UpdateForClause)] = "FOR UPDATE",
        [typeof(TableSampleClause)] = "TABLESAMPLE",
        [typeof(PartitionFunctionCall)] = "partition functions",
        [typeof(VariableTableReference)] = "table variables",
        [typeof(VariableMethodCallTableReference)] = "table variable methods",
        [typeof(BuiltInFunctionTableReference)] = "built-in table functions",
        [typeof(GlobalFunctionTableReference)] = "system table functions",
        [typeof(PivotedTableReference)] = "PIVOT",
        [typeof(UnpivotedTableReference)] = "UNPIVOT",
    };

    private static readonly HashSet<string> DangerousFunctionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "OPENROWSET", "OPENQUERY", "OPENDATASOURCE", "OPENXML", "OPENJSON", "XP_CMDSHELL", "SP_EXECUTESQL",
        "SP_EXECUTE", "SP_OACREATE", "SP_OAMETHOD", "FN_GET_AUDIT_FILE", "FN_XE_FILE_TARGET_READ_FILE", "FN_DBLOG",
        "FN_DUMP_DBLOG", "FILEPROPERTY", "PWDCOMPARE", "PWDENCRYPT", "HAS_DBACCESS", "SUSER_SNAME", "SUSER_SID",
        "SUSER_NAME", "USER_NAME", "CONTEXT_INFO", "SESSION_CONTEXT", "DB_NAME", "DB_ID", "HOST_NAME", "APP_NAME",
        "SERVERPROPERTY", "DATABASEPROPERTYEX", "OBJECT_DEFINITION", "PERMISSIONS", "IS_SRVROLEMEMBER", "IS_MEMBER",
    };

    /// <summary>Approved SQL Server built-in scalar, aggregate and window functions.</summary>
    internal static readonly HashSet<string> DefaultAllowedFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        // aggregates
        "COUNT", "COUNT_BIG", "SUM", "AVG", "MIN", "MAX", "STDEV", "STDEVP", "VAR", "VARP", "STRING_AGG", "GROUPING",
        "GROUPING_ID", "APPROX_COUNT_DISTINCT", "APPROX_PERCENTILE_CONT", "APPROX_PERCENTILE_DISC",
        // window / ranking
        "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE", "LAG", "LEAD", "FIRST_VALUE", "LAST_VALUE", "PERCENT_RANK",
        "CUME_DIST", "PERCENTILE_CONT", "PERCENTILE_DISC",
        // date & time
        "GETDATE", "GETUTCDATE", "SYSDATETIME", "SYSUTCDATETIME", "SYSDATETIMEOFFSET", "DATEADD", "DATEDIFF",
        "DATEDIFF_BIG", "DATEPART", "DATENAME", "DATEFROMPARTS", "DATETIMEFROMPARTS", "DATETIME2FROMPARTS",
        "SMALLDATETIMEFROMPARTS", "TIMEFROMPARTS", "EOMONTH", "YEAR", "MONTH", "DAY", "ISDATE", "DATETRUNC",
        "DATE_BUCKET", "SWITCHOFFSET", "TODATETIMEOFFSET", "FORMAT",
        // string
        "LEN", "DATALENGTH", "UPPER", "LOWER", "LTRIM", "RTRIM", "TRIM", "SUBSTRING", "REPLACE", "CHARINDEX", "PATINDEX",
        "CONCAT", "CONCAT_WS", "REPLICATE", "REVERSE", "STUFF", "SPACE", "STR", "QUOTENAME", "SOUNDEX", "DIFFERENCE",
        "TRANSLATE", "ASCII", "CHAR", "NCHAR", "UNICODE", "STRING_ESCAPE",
        // math
        "ABS", "CEILING", "FLOOR", "ROUND", "POWER", "SQRT", "SQUARE", "SIGN", "EXP", "LOG", "LOG10", "PI",
        "SIN", "COS", "TAN", "ASIN", "ACOS", "ATAN", "ATN2", "DEGREES", "RADIANS",
        // logical / conversion / json
        "ISNULL", "ISNUMERIC", "CHOOSE", "GREATEST", "LEAST", "JSON_VALUE", "JSON_QUERY", "ISJSON", "CHECKSUM",
    };

    private static readonly HashSet<string> AggregateFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "COUNT_BIG", "SUM", "AVG", "MIN", "MAX", "STDEV", "STDEVP", "VAR", "VARP", "STRING_AGG", "APPROX_COUNT_DISTINCT",
    };

    private static readonly HashSet<string> DatePartFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "DATEADD", "DATEDIFF", "DATEDIFF_BIG", "DATEPART", "DATENAME", "DATETRUNC", "DATE_BUCKET",
    };

    private readonly HashSet<string> _allowedFunctions;
    private readonly HashSet<TSqlFragment> _datePartArguments = new(ReferenceEqualityComparer.Instance);
    private int _depth;

    public QueryWalker(SqlValidationOptions options)
    {
        _allowedFunctions = new HashSet<string>(DefaultAllowedFunctions, StringComparer.OrdinalIgnoreCase);
        foreach (var f in options.AdditionalAllowedFunctions) _allowedFunctions.Add(f);
    }

    public List<SqlValidationIssue> Errors { get; } = new();
    public List<NamedTableReference> TableReferences { get; } = new();
    public List<ColumnReferenceExpression> ColumnReferences { get; } = new();
    public List<SelectStarExpression> StarExpressions { get; } = new();
    public HashSet<string> CteNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Aliases of derived tables and CTEs (their columns are validated where they are defined).</summary>
    public HashSet<string> VirtualTableAliases { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Output names defined inside the query (select aliases, CTE/derived column lists).</summary>
    public HashSet<string> DefinedColumnNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Variables { get; } = new();
    public int NodeCount { get; private set; }
    public int JoinCount { get; private set; }
    public int MaxDepth { get; private set; }
    public bool HasAggregation { get; private set; }
    public bool HasSelectStar => StarExpressions.Count > 0;

    public override void Visit(TSqlFragment node)
    {
        NodeCount++;
        var type = node.GetType();
        if (DangerousNodeTypes.TryGetValue(type, out var dangerous))
        {
            AddError(SqlValidationCodes.DangerousFunction, $"{dangerous} is not allowed.", node);
            return;
        }

        if (!AllowedNodeTypes.Contains(type))
            AddError(SqlValidationCodes.ConstructNotAllowed, $"The SQL construct '{type.Name}' is not permitted in read-only queries.", node);
    }

    public override void ExplicitVisit(ScalarSubquery node) => Nested(() => base.ExplicitVisit(node));

    public override void ExplicitVisit(QueryDerivedTable node)
    {
        if (node.Alias is not null) VirtualTableAliases.Add(node.Alias.Value);
        foreach (var c in node.Columns) DefinedColumnNames.Add(c.Value);
        Nested(() => base.ExplicitVisit(node));
    }

    public override void ExplicitVisit(InlineDerivedTable node)
    {
        if (node.Alias is not null) VirtualTableAliases.Add(node.Alias.Value);
        foreach (var c in node.Columns) DefinedColumnNames.Add(c.Value);
        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(CommonTableExpression node)
    {
        CteNames.Add(node.ExpressionName.Value);
        VirtualTableAliases.Add(node.ExpressionName.Value);
        foreach (var c in node.Columns) DefinedColumnNames.Add(c.Value);
        Nested(() => base.ExplicitVisit(node));
    }

    public override void ExplicitVisit(NamedTableReference node)
    {
        TableReferences.Add(node);
        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(QualifiedJoin node) { JoinCount++; base.ExplicitVisit(node); }

    public override void ExplicitVisit(UnqualifiedJoin node) { JoinCount++; base.ExplicitVisit(node); }

    public override void ExplicitVisit(SelectScalarExpression node)
    {
        if (node.ColumnName?.Value is { } alias) DefinedColumnNames.Add(alias);
        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(SelectStarExpression node)
    {
        StarExpressions.Add(node);
        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(ColumnReferenceExpression node)
    {
        if (_datePartArguments.Contains(node))
        {
            base.ExplicitVisit(node);
            return;
        }

        switch (node.ColumnType)
        {
            case ColumnType.Regular:
                ColumnReferences.Add(node);
                break;
            case ColumnType.Wildcard:
                break; // COUNT(*)
            default:
                AddError(SqlValidationCodes.ConstructNotAllowed, $"Column reference type '{node.ColumnType}' is not permitted.", node);
                break;
        }

        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(VariableReference node)
    {
        Variables.Add(node.Name);
        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(ParameterlessCall node)
    {
        if (node.ParameterlessCallType != ParameterlessCallType.CurrentTimestamp)
            AddError(SqlValidationCodes.FunctionNotAllowed, $"{node.ParameterlessCallType} is not permitted.", node);
        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(FunctionCall node)
    {
        var name = node.FunctionName?.Value ?? string.Empty;

        if (node.CallTarget is not null)
            AddError(SqlValidationCodes.FunctionNotAllowed, $"Schema-qualified or user-defined function '{name}' is not permitted.", node);
        else if (DangerousFunctionNames.Contains(name) || name.StartsWith("xp_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("sp_", StringComparison.OrdinalIgnoreCase))
            AddError(SqlValidationCodes.DangerousFunction, $"The function '{name}' is not allowed.", node);
        else if (!_allowedFunctions.Contains(name))
            AddError(SqlValidationCodes.FunctionNotAllowed, $"The function '{name}' is not in the list of approved SQL functions.", node);

        if (AggregateFunctions.Contains(name) && node.OverClause is null) HasAggregation = true;

        // The first argument of DATEADD/DATEDIFF/... is a date-part keyword that ScriptDom models as a column reference.
        if (DatePartFunctions.Contains(name) && node.Parameters.Count > 0 &&
            node.Parameters[0] is ColumnReferenceExpression { MultiPartIdentifier.Identifiers.Count: 1 } datePart)
            _datePartArguments.Add(datePart);

        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(QuerySpecification node)
    {
        if (node.GroupByClause is not null) HasAggregation = true;
        base.ExplicitVisit(node);
    }

    private void Nested(Action visit)
    {
        _depth++;
        MaxDepth = Math.Max(MaxDepth, _depth);
        visit();
        _depth--;
    }

    private void AddError(string code, string message, TSqlFragment node)
    {
        if (Errors.Count < 20 && !Errors.Any(e => e.Message == message))
            Errors.Add(new SqlValidationIssue(code, message, node.StartLine, node.StartColumn));
    }
}
