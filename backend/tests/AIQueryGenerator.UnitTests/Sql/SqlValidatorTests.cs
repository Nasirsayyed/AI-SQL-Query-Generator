using AIQueryGenerator.Application.Sql;
using AIQueryGenerator.Infrastructure.Sql;

namespace AIQueryGenerator.UnitTests.Sql;

public class SqlValidatorTests
{
    private readonly ScriptDomSqlValidator _validator = new();

    private SqlValidationResult Validate(string sql, IReadOnlyCollection<string>? parameters = null, SqlValidationOptions? options = null) =>
        _validator.Validate(sql, TestCatalog.Context(parameters, options));

    private void AssertRejected(string sql, string expectedCode, IReadOnlyCollection<string>? parameters = null)
    {
        var result = Validate(sql, parameters);
        Assert.False(result.IsValid, $"Expected rejection for: {sql}");
        Assert.Contains(result.Errors, e => e.Code == expectedCode);
    }

    public static TheoryData<string> ValidQueries => new()
    {
        "SELECT TOP 10 c.CustomerName, SUM(o.TotalAmount) AS TotalSales FROM dbo.Customers c JOIN dbo.Orders o ON o.CustomerId = c.CustomerId WHERE o.OrderDate >= DATEFROMPARTS(YEAR(GETDATE()), 1, 1) GROUP BY c.CustomerName ORDER BY TotalSales DESC",
        "SELECT o.OrderId, o.OrderDate, o.TotalAmount FROM Orders o WHERE CAST(o.OrderDate AS date) = CAST(GETDATE() AS date)",
        "SELECT DATENAME(month, o.OrderDate) AS MonthName, MONTH(o.OrderDate) AS MonthNumber, SUM(o.TotalAmount) AS Revenue FROM dbo.Orders AS o WHERE YEAR(o.OrderDate) = YEAR(GETDATE()) GROUP BY DATENAME(month, o.OrderDate), MONTH(o.OrderDate) ORDER BY MonthNumber",
        "SELECT p.ProductName, i.AvailableQuantity, p.ReorderLevel FROM dbo.Products p INNER JOIN dbo.Inventory i ON i.ProductId = p.ProductId WHERE i.AvailableQuantity < p.ReorderLevel",
        "SELECT w.WarehouseName, COUNT(*) AS PendingOrders FROM dbo.Orders o JOIN dbo.Warehouses w ON w.WarehouseId = o.WarehouseId WHERE o.OrderStatus = 'Pending' GROUP BY w.WarehouseName",
        "SELECT c.CustomerId, c.CustomerName FROM dbo.Customers c WHERE NOT EXISTS (SELECT 1 FROM dbo.Orders o WHERE o.CustomerId = c.CustomerId AND o.OrderDate >= DATEADD(day, -90, GETDATE()))",
        "WITH Monthly AS (SELECT YEAR(OrderDate) AS Y, MONTH(OrderDate) AS M, SUM(TotalAmount) AS Revenue FROM dbo.Orders GROUP BY YEAR(OrderDate), MONTH(OrderDate)) SELECT m.Y, m.M, m.Revenue, LAG(m.Revenue) OVER (ORDER BY m.Y, m.M) AS PreviousRevenue FROM Monthly m",
        "SELECT TOP (5) p.ProductName, SUM(od.Quantity) AS UnitsSold FROM dbo.OrderDetails od JOIN dbo.Products p ON p.ProductId = od.ProductId GROUP BY p.ProductName ORDER BY UnitsSold DESC",
        "SELECT AVG(CAST(DATEDIFF(hour, o.OrderDate, o.ShippedDate) AS decimal(10,2))) AS AvgProcessingHours FROM dbo.Orders o WHERE o.ShippedDate IS NOT NULL",
        "SELECT c.Region, SUM(o.TotalAmount) AS TotalRevenue FROM dbo.Customers c JOIN dbo.Orders o ON o.CustomerId = c.CustomerId GROUP BY c.Region ORDER BY TotalRevenue DESC",
        "SELECT CustomerName FROM Customers ORDER BY CustomerName OFFSET 0 ROWS FETCH NEXT 50 ROWS ONLY",
        "SELECT o.OrderId, CASE WHEN o.TotalAmount > 1000 THEN 'Large' ELSE 'Small' END AS SizeBand FROM dbo.Orders o -- trailing comment",
        "SELECT [o].[OrderId] FROM [dbo].[Orders] AS [o] WHERE [o].[OrderStatus] IN ('Pending', 'Shipped')",
        "SELECT d.CustomerId, d.Total FROM (SELECT CustomerId, SUM(TotalAmount) AS Total FROM dbo.Orders GROUP BY CustomerId) d WHERE d.Total > 100",
        "SELECT o.OrderId FROM AIQueryDemoDB.dbo.Orders o",
        "SELECT o.OrderId FROM dbo.Orders o UNION ALL SELECT p.OrderId FROM dbo.Payments p",
    };

    [Theory]
    [MemberData(nameof(ValidQueries))]
    public void Accepts_read_only_queries(string sql)
    {
        var result = Validate(sql);
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Theory]
    [InlineData("INSERT INTO dbo.Orders (CustomerId) VALUES (1)")]
    [InlineData("UPDATE dbo.Orders SET TotalAmount = 0")]
    [InlineData("DELETE FROM dbo.Orders")]
    [InlineData("MERGE dbo.Orders AS t USING dbo.Payments AS s ON t.OrderId = s.OrderId WHEN MATCHED THEN DELETE;")]
    [InlineData("DROP TABLE dbo.Orders")]
    [InlineData("TRUNCATE TABLE dbo.Orders")]
    [InlineData("CREATE TABLE dbo.X (Id int)")]
    [InlineData("ALTER TABLE dbo.Orders ADD X int")]
    [InlineData("EXEC sp_who")]
    [InlineData("EXECUTE ('SELECT 1')")]
    [InlineData("EXEC sp_executesql N'SELECT 1'")]
    [InlineData("USE master")]
    [InlineData("BEGIN TRANSACTION")]
    [InlineData("COMMIT")]
    [InlineData("WAITFOR DELAY '00:00:10'")]
    [InlineData("DECLARE @x int")]
    [InlineData("GRANT SELECT ON dbo.Orders TO public")]
    [InlineData("DBCC CHECKDB")]
    [InlineData("SHUTDOWN")]
    [InlineData("BACKUP DATABASE AIQueryDemoDB TO DISK = 'c:\\x.bak'")]
    [InlineData("WITH x AS (SELECT OrderId FROM dbo.Orders) DELETE FROM x")]
    public void Rejects_non_select_statements(string sql) => AssertRejected(sql, SqlValidationCodes.StatementNotAllowed);

    [Theory]
    [InlineData("SELECT 1; DROP TABLE dbo.Orders")]
    [InlineData("SELECT OrderId FROM dbo.Orders\nGO\nDELETE FROM dbo.Orders")]
    [InlineData("SELECT OrderId FROM dbo.Orders /* harmless */; /**/ DELETE FROM dbo.Orders")]
    [InlineData("SELECT OrderId FROM dbo.Orders;SELECT 2")]
    public void Rejects_multiple_statements(string sql) => AssertRejected(sql, SqlValidationCodes.MultipleStatements);

    [Fact]
    public void Rejects_select_into() => AssertRejected("SELECT OrderId INTO dbo.Copy FROM dbo.Orders", SqlValidationCodes.SelectInto);

    [Theory]
    [InlineData("SELECT * FROM OPENROWSET('SQLNCLI', 'Server=x;Trusted_Connection=yes;', 'SELECT 1')")]
    [InlineData("SELECT * FROM OPENQUERY(LinkedSrv, 'SELECT 1')")]
    [InlineData("SELECT * FROM OPENDATASOURCE('SQLNCLI', 'Data Source=x').db.dbo.t")]
    [InlineData("SELECT * FROM OPENROWSET(BULK 'c:\\secrets.txt', SINGLE_CLOB) AS x")]
    [InlineData("SELECT @@VERSION")]
    [InlineData("SELECT NEXT VALUE FOR dbo.Seq")]
    [InlineData("SELECT [key] FROM OPENJSON('{}')")]
    public void Rejects_external_data_access_and_dangerous_constructs(string sql) => AssertRejected(sql, SqlValidationCodes.DangerousFunction);

    [Theory]
    [InlineData("SELECT dbo.fn_Secret(1)")]
    [InlineData("SELECT xp_cmdshell('dir')")]
    [InlineData("SELECT HOST_NAME()")]
    [InlineData("SELECT SUSER_SNAME()")]
    public void Rejects_unapproved_functions(string sql)
    {
        var result = Validate(sql);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code is SqlValidationCodes.FunctionNotAllowed or SqlValidationCodes.DangerousFunction);
    }

    [Theory]
    [InlineData("SELECT o.OrderId FROM OtherDb.dbo.Orders o")]
    [InlineData("SELECT o.OrderId FROM LinkedServer.AIQueryDemoDB.dbo.Orders o")]
    public void Rejects_cross_database_and_linked_server_access(string sql) => AssertRejected(sql, SqlValidationCodes.CrossDatabase);

    [Theory]
    [InlineData("SELECT name FROM sys.tables")]
    [InlineData("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES")]
    [InlineData("SELECT name FROM sys.sql_logins")]
    public void Rejects_system_catalogs(string sql) => AssertRejected(sql, SqlValidationCodes.SystemCatalog);

    [Fact]
    public void Rejects_unauthorized_table() => AssertRejected("SELECT s.Value FROM dbo.Secrets s", SqlValidationCodes.UnauthorizedObject);

    [Fact]
    public void Rejects_unknown_table() => AssertRejected("SELECT x.Id FROM dbo.DoesNotExist x", SqlValidationCodes.UnknownObject);

    [Theory]
    [InlineData("SELECT c.Email FROM dbo.Customers c")]
    [InlineData("SELECT Email FROM dbo.Customers")]
    [InlineData("SELECT c.CustomerName FROM dbo.Customers c WHERE c.Email LIKE '%@x.com'")]
    [InlineData("SELECT dbo.Customers.Email FROM dbo.Customers")]
    [InlineData("SELECT Customers.Email FROM dbo.Customers")]
    [InlineData("SELECT x.Email FROM dbo.Customers x JOIN dbo.Orders o ON o.CustomerId = x.CustomerId")]
    [InlineData("SELECT d.E FROM (SELECT Email AS E FROM dbo.Customers) d")]
    [InlineData("WITH c AS (SELECT CustomerId, Email FROM dbo.Customers) SELECT c.CustomerId FROM c")]
    [InlineData("SELECT o.OrderId FROM dbo.Orders o WHERE EXISTS (SELECT 1 FROM dbo.Customers c WHERE c.Email = 'a')")]
    public void Rejects_unauthorized_columns(string sql) => AssertRejected(sql, SqlValidationCodes.UnauthorizedColumn);

    [Theory]
    [InlineData("SELECT * FROM dbo.Customers")]
    [InlineData("SELECT c.* FROM dbo.Customers c")]
    public void Rejects_star_over_restricted_table(string sql) => AssertRejected(sql, SqlValidationCodes.StarExposesRestricted);

    [Fact]
    public void Allows_star_over_unrestricted_table_with_warning()
    {
        var result = Validate("SELECT * FROM dbo.Products");
        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Code == "SELECT_STAR");
    }

    [Theory]
    [InlineData("SELECT c.Nonexistent FROM dbo.Customers c")]
    [InlineData("SELECT Nonexistent FROM dbo.Customers")]
    public void Rejects_unknown_columns(string sql) => AssertRejected(sql, SqlValidationCodes.UnknownColumn);

    [Theory]
    [InlineData("SELECT o.OrderId FROM dbo.Orders o; -- comment hides nothing\nDROP TABLE dbo.Orders")]
    [InlineData("SELECT o.OrderId FROM dbo.Orders o /*; DROP TABLE dbo.Orders */")]
    [InlineData("/* DELETE */ SELECT o.OrderId FROM dbo.Orders o -- ; UPDATE")]
    public void Comments_cannot_smuggle_statements(string sql)
    {
        var result = Validate(sql);
        var hasDrop = sql.Contains("\nDROP", StringComparison.Ordinal);
        Assert.Equal(!hasDrop, result.IsValid);
    }

    [Theory]
    [InlineData("SELECT o.OrderId FROM dbo.Orders o WHERE o.OrderStatus = 'x'';DROP TABLE dbo.Orders;--'")]
    [InlineData("SELECT 'DELETE FROM dbo.Orders' AS Text")]
    public void Sql_inside_string_literals_is_data(string sql) => Assert.True(Validate(sql).IsValid);

    [Theory]
    [InlineData("SELECT o.OrderId FROM dbo.Orders o WHERE o.CustomerId = 1; EXEC('DROP TABLE dbo.Orders')")]
    [InlineData("EXEC('SEL' + 'ECT 1')")]
    [InlineData("DECLARE @s nvarchar(100) = 'DROP TABLE x'; EXEC(@s)")]
    public void Rejects_dynamic_sql(string sql) => Assert.False(Validate(sql).IsValid);

    [Fact]
    public void Rejects_invalid_syntax() => AssertRejected("SELEC OrderId FROM dbo.Orders", SqlValidationCodes.Syntax);

    [Fact]
    public void Rejects_query_exceeding_max_length() =>
        Assert.Contains(Validate("SELECT o.OrderId FROM dbo.Orders o", options: new SqlValidationOptions { MaxQueryLength = 10 }).Errors,
            e => e.Code == SqlValidationCodes.TooLong);

    [Fact]
    public void Rejects_too_many_joins()
    {
        var result = Validate(
            "SELECT o.OrderId FROM dbo.Orders o JOIN dbo.Customers c ON c.CustomerId = o.CustomerId JOIN dbo.Warehouses w ON w.WarehouseId = o.WarehouseId",
            options: new SqlValidationOptions { MaxJoins = 1 });
        Assert.Contains(result.Errors, e => e.Code == SqlValidationCodes.TooManyJoins);
    }

    [Fact]
    public void Rejects_too_many_output_columns() =>
        Assert.Contains(Validate("SELECT o.OrderId, o.OrderDate FROM dbo.Orders o", options: new SqlValidationOptions { MaxOutputColumns = 1 }).Errors,
            e => e.Code == SqlValidationCodes.TooManyColumns);

    [Fact]
    public void Requires_values_for_referenced_parameters()
    {
        const string sql = "SELECT o.OrderId FROM dbo.Orders o WHERE o.OrderStatus = @Status AND o.CustomerId = @CustomerId";
        Assert.Contains(Validate(sql, ["Status"]).Errors, e => e.Code == SqlValidationCodes.Parameter);
        var ok = Validate(sql, ["@Status", "customerid"]);
        Assert.True(ok.IsValid);
        Assert.Equal(2, ok.ReferencedParameters.Count);
    }

    [Fact]
    public void Collects_referenced_objects()
    {
        var result = Validate("SELECT c.CustomerName, o.TotalAmount FROM dbo.Customers c JOIN dbo.Orders o ON o.CustomerId = c.CustomerId");
        Assert.True(result.IsValid);
        Assert.Equal(["dbo.Customers", "dbo.Orders"], result.ReferencedObjects.Order());
        Assert.Equal(1, result.JoinCount);
    }

    [Fact]
    public void Warns_when_unbounded()
    {
        var result = Validate("SELECT o.OrderId FROM dbo.Orders o");
        Assert.Contains(result.Warnings, w => w.Code == "ROW_LIMIT");
    }
}
