using AIQueryGenerator.Infrastructure.Sql;
using AIQueryGenerator.Shared.Exceptions;

namespace AIQueryGenerator.UnitTests.Sql;

public class SqlFormatterTests
{
    [Fact]
    public void Formats_with_uppercase_keywords_and_clause_newlines()
    {
        var formatted = new ScriptDomSqlFormatter().Format("select o.OrderId from dbo.Orders o where o.TotalAmount > 10 order by o.OrderId");
        Assert.Contains("SELECT", formatted);
        Assert.Contains("\nFROM", formatted.Replace("\r", ""));
        Assert.Contains("\nWHERE", formatted.Replace("\r", ""));
    }

    [Fact]
    public void Throws_for_invalid_sql() =>
        Assert.Throws<BusinessRuleException>(() => new ScriptDomSqlFormatter().Format("SELEC FROM"));
}
