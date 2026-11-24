using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AIQueryGenerator.UnitTests.Sql;

/// <summary>Parses the shipped database scripts with the SQL Server parser to catch syntax errors without a server.</summary>
public class DatabaseScriptTests
{
    private static string ScriptPath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "database"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("database folder"), "database", name);
    }

    [Theory]
    [InlineData("ApplicationDatabase.sql")]
    [InlineData("SampleTargetDatabase.sql")]
    public void Script_parses_without_errors(string name)
    {
        var sql = File.ReadAllText(ScriptPath(name)).Replace("$(ReaderPassword)", "placeholder");
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        parser.Parse(new StringReader(sql), out var errors);
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => $"line {e.Line}: {e.Message}")));
    }

    [Fact]
    public void Sample_script_creates_read_only_login_without_embedded_password()
    {
        var sql = File.ReadAllText(ScriptPath("SampleTargetDatabase.sql"));
        Assert.Contains("PASSWORD = N'$(ReaderPassword)'", sql);
        Assert.Contains("GRANT SELECT ON SCHEMA::dbo TO aiqg_reader", sql);
        Assert.Contains("DENY INSERT, UPDATE, DELETE", sql);
        Assert.DoesNotContain("db_owner", sql.Replace("Never use sysadmin, db_owner", ""));
    }
}
