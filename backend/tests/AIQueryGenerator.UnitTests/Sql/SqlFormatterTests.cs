using AIQueryGenerator.Infrastructure.Sql;
using AIQueryGenerator.Shared.Exceptions;

namespace AIQueryGenerator.UnitTests.Sql;

public class SqlFormatterTests
{
    [Fact]
    public void Formats_with_uppercase_keywords_and_clause_newlines()
    {
        var result = new ScriptDomSqlFormatter().Format("select o.OrderId from dbo.Orders o where o.TotalAmount > 10 order by o.OrderId");
        var formatted = result.Sql;
        Assert.Equal(0, result.CommentsRemoved);
        Assert.Contains("SELECT", formatted);
        Assert.Contains("\nFROM", formatted.Replace("\r", ""));
        Assert.Contains("\nWHERE", formatted.Replace("\r", ""));
    }

    [Fact]
    public void Reports_comments_that_formatting_drops()
    {
        var result = new ScriptDomSqlFormatter().Format("-- top customers\nselect o.OrderId /* id */ from dbo.Orders o");
        Assert.Equal(2, result.CommentsRemoved);
        Assert.DoesNotContain("--", result.Sql);
    }

    [Fact]
    public void Throws_for_invalid_sql() =>
        Assert.Throws<BusinessRuleException>(() => new ScriptDomSqlFormatter().Format("SELEC FROM"));
}
