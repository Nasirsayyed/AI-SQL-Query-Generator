using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Shared.Exceptions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AIQueryGenerator.Infrastructure.Sql;

/// <summary>Formats T-SQL by regenerating it from the parsed syntax tree (comments are not preserved).</summary>
public sealed class ScriptDomSqlFormatter : ISqlFormatter
{
    public string Format(string sql)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out var errors);
        if (errors.Count > 0)
            throw new BusinessRuleException($"The SQL cannot be formatted because it contains a syntax error: {errors[0].Message} (line {errors[0].Line}).", ErrorCodes.SqlInvalid);

        var generator = new Sql160ScriptGenerator(new SqlScriptGeneratorOptions
        {
            KeywordCasing = KeywordCasing.Uppercase,
            IncludeSemicolons = false,
            AlignClauseBodies = false,
            IndentationSize = 4,
            NewLineBeforeFromClause = true,
            NewLineBeforeWhereClause = true,
            NewLineBeforeGroupByClause = true,
            NewLineBeforeOrderByClause = true,
            NewLineBeforeHavingClause = true,
            NewLineBeforeJoinClause = true,
            MultilineSelectElementsList = true,
            MultilineWherePredicatesList = true,
        });
        generator.GenerateScript(fragment, out var formatted);
        return formatted.Trim();
    }
}
