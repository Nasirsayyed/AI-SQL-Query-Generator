using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Application.Sql;
using AIQueryGenerator.Domain.Entities;
using AIQueryGenerator.Domain.Enums;
using AIQueryGenerator.Infrastructure.TargetDatabase;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIQueryGenerator.IntegrationTests;

/// <summary>Runs only when AIQG_TEST_SQLSERVER is set to a connection string for a server hosting AIQueryDemoDB.</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AIQG_TEST_SQLSERVER")))
            Skip = "Set AIQG_TEST_SQLSERVER (read-only login to AIQueryDemoDB) to run SQL Server integration tests.";
    }
}

/// <summary>
/// Exercises the real SqlClient execution path, least-privilege probe and catalog discovery against
/// backend/database/SampleTargetDatabase.sql using the aiqg_reader login.
/// </summary>
public class SqlServerTargetTests
{
    private static (TargetDatabaseClient Client, SqlServerSchemaDiscoveryService Discovery, DatabaseConnection Connection) Create()
    {
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AIQG_TEST_SQLSERVER"));
        var connection = new DatabaseConnection
        {
            ConnectionId = 1,
            ConnectionName = "integration",
            ServerName = builder.DataSource,
            DatabaseName = string.IsNullOrEmpty(builder.InitialCatalog) ? "AIQueryDemoDB" : builder.InitialCatalog,
            AuthenticationType = SqlAuthenticationType.SqlPassword,
            Username = builder.UserID,
            EncryptedCredentialsReference = "plain",
            TrustServerCertificate = true,
        };
        var factory = new SqlConnectionFactory(new PlainSecretStore(builder.Password));
        return (new TargetDatabaseClient(factory, NullLogger<TargetDatabaseClient>.Instance),
                new SqlServerSchemaDiscoveryService(factory, NullLogger<SqlServerSchemaDiscoveryService>.Instance),
                connection);
    }

    [SqlServerFact]
    public async Task Reader_login_is_detected_as_least_privilege()
    {
        var (client, _, connection) = Create();
        var result = await client.TestConnectionAsync(connection, CancellationToken.None);
        Assert.True(result.Success, result.Message);
        Assert.True(result.IsLeastPrivilege, string.Join("; ", result.PrivilegeWarnings));
    }

    [SqlServerFact]
    public async Task Discovery_reads_tables_columns_and_foreign_keys()
    {
        var (_, discovery, connection) = Create();
        var db = await discovery.DiscoverAsync(connection, CancellationToken.None);
        Assert.Contains(db.Tables, t => t.Name == "Orders" && t.Columns.Any(c => c.Name == "TotalAmount" && c.DataType == "decimal"));
        Assert.Contains(db.ForeignKeys, f => f.FromTable == "Orders" && f.ToTable == "Customers");
        Assert.Contains(db.Tables.Single(t => t.Name == "Customers").Indexes, i => i.IsPrimaryKey);
    }

    [SqlServerFact]
    public async Task Executes_parameterised_query_and_applies_row_limit()
    {
        var (client, _, connection) = Create();
        var result = await client.ExecuteReadOnlyAsync(new QueryExecutionRequest(connection,
            "SELECT o.OrderId, o.TotalAmount FROM dbo.Orders o WHERE o.TotalAmount > @Min ORDER BY o.OrderId",
            [new BoundParameter("Min", SqlParameterType.Decimal, 0m)], 5, 30, 4000), CancellationToken.None);
        Assert.Equal(5, result.Rows.Count);
        Assert.True(result.IsTruncated);
        Assert.Equal("decimal", result.Columns[1].DataType);
    }

    [SqlServerFact]
    public async Task Database_permissions_block_writes_even_if_validation_were_bypassed()
    {
        var (client, _, connection) = Create();
        var ex = await Assert.ThrowsAsync<AIQueryGenerator.Shared.Exceptions.ExternalServiceException>(() =>
            client.ExecuteReadOnlyAsync(new QueryExecutionRequest(connection, "DELETE FROM dbo.Payments WHERE 1 = 0", [], 10, 30, 4000), CancellationToken.None));
        Assert.Equal("SQL_PERMISSION_DENIED", ex.ErrorCode);
    }

    private sealed class PlainSecretStore(string password) : ISecretStore
    {
        public string Protect(string secret) => secret;
        public string? Resolve(string? reference) => password;
        public bool IsReference(string? value) => false;
    }
}
