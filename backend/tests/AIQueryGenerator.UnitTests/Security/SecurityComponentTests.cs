using AIQueryGenerator.Application.Sql;
using AIQueryGenerator.Domain.Entities;
using AIQueryGenerator.Infrastructure.Security;
using AIQueryGenerator.Infrastructure.TargetDatabase;
using AIQueryGenerator.Shared.Exceptions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AIQueryGenerator.UnitTests.Security;

public class SecurityComponentTests
{
    private static readonly IDataProtectionProvider Provider = new EphemeralDataProtectionProvider();

    [Fact]
    public void Password_hashing_round_trips_and_rejects_wrong_passwords()
    {
        var hasher = new PasswordHasherService();
        var hash = hasher.Hash("Correct-Horse-1");
        Assert.DoesNotContain("Correct-Horse-1", hash);
        Assert.True(hasher.Verify(hash, "Correct-Horse-1", out _));
        Assert.False(hasher.Verify(hash, "wrong", out _));
        Assert.False(hasher.Verify("garbage", "x", out _));
    }

    [Fact]
    public void Validation_token_is_bound_to_user_connection_sql_and_parameters()
    {
        var service = new ValidationTokenService(Provider, Options.Create(new ExecutionOptions()));
        var token = service.Issue(1, 2, "SELECT 1", "p");
        Assert.True(service.Verify(token, 1, 2, "SELECT 1", "p"));
        Assert.False(service.Verify(token, 9, 2, "SELECT 1", "p"));
        Assert.False(service.Verify(token, 1, 9, "SELECT 1", "p"));
        Assert.False(service.Verify(token, 1, 2, "SELECT 2", "p"));
        Assert.False(service.Verify(token, 1, 2, "SELECT 1", "q"));
        Assert.False(service.Verify(token + "x", 1, 2, "SELECT 1", "p"));
        Assert.False(service.Verify(null, 1, 2, "SELECT 1", "p"));
    }

    [Fact]
    public void Secret_store_encrypts_and_restricts_environment_references()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AIQG_SECRET_DB"] = "db-password",
            ["Jwt:SigningKey"] = "super-secret",
        }).Build();
        var store = new DataProtectionSecretStore(Provider, config, Options.Create(new SecretStoreOptions()));

        var reference = store.Protect("p@ss");
        Assert.StartsWith("dp:", reference);
        Assert.DoesNotContain("p@ss", reference);
        Assert.Equal("p@ss", store.Resolve(reference));
        Assert.Equal("db-password", store.Resolve("env:AIQG_SECRET_DB"));
        Assert.Null(store.Resolve("env:Jwt:SigningKey"));
        Assert.Null(store.Resolve("dp:tampered"));
        Assert.Null(store.Resolve("plaintext"));
    }

    [Fact]
    public async Task Concurrency_limiter_enforces_per_user_limit()
    {
        var limiter = new QueryConcurrencyLimiter(Options.Create(new ExecutionOptions { MaxConcurrentQueriesPerUser = 1 }));
        var slot = await limiter.AcquireAsync(1, CancellationToken.None);
        var ex = await Assert.ThrowsAsync<ExternalServiceException>(() => limiter.AcquireAsync(1, CancellationToken.None));
        Assert.Equal(ErrorCodes.ConcurrencyLimit, ex.ErrorCode);
        using (await limiter.AcquireAsync(2, CancellationToken.None)) { }
        slot.Dispose();
        using (await limiter.AcquireAsync(1, CancellationToken.None)) { }
    }

    [Fact]
    public void Connection_factory_builds_read_only_intent_without_injection()
    {
        var store = new DataProtectionSecretStore(Provider, new ConfigurationBuilder().Build(), Options.Create(new SecretStoreOptions()));
        var factory = new SqlConnectionFactory(store);
        var connection = factory.Create(new DatabaseConnection
        {
            ServerName = "sql01",
            DatabaseName = "Db;Integrated Security=true",
            AuthenticationType = Domain.Enums.SqlAuthenticationType.SqlPassword,
            Username = "reader",
            EncryptedCredentialsReference = store.Protect("pw;Server=evil"),
            EncryptConnection = true,
        });
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection.ConnectionString);
        Assert.Equal("sql01", builder.DataSource);
        Assert.Equal("Db;Integrated Security=true", builder.InitialCatalog);
        Assert.False(builder.IntegratedSecurity);
        Assert.Equal("pw;Server=evil", builder.Password);
        Assert.Equal(Microsoft.Data.SqlClient.ApplicationIntent.ReadOnly, builder.ApplicationIntent);
        Assert.False(builder.PersistSecurityInfo);
    }

    [Fact]
    public void Lockout_after_repeated_failures()
    {
        var user = new User();
        var now = DateTime.UtcNow;
        for (var i = 0; i < User.MaxFailedLogins; i++) user.RegisterFailedLogin(now);
        Assert.True(user.IsLockedOut(now));
        Assert.False(user.IsLockedOut(now.AddMinutes(16)));
    }

    [Theory]
    [InlineData(9_007_199_254_740_993L, true)]
    [InlineData(42L, false)]
    public void Large_integers_are_returned_as_strings(long value, bool asString) =>
        Assert.Equal(asString, ValueConverter.Convert(value, "bigint", 100) is string);

    [Fact]
    public void Value_converter_handles_common_sql_types()
    {
        Assert.Equal("2026-01-31", ValueConverter.Convert(new DateTime(2026, 1, 31), "date", 100));
        Assert.Equal("12345678901234567.89", ValueConverter.Convert(12345678901234567.89m, "decimal", 100));
        Assert.Equal(12.5m, ValueConverter.Convert(12.5m, "decimal", 100));
        Assert.Equal("abc…", ValueConverter.Convert("abcdef", "nvarchar", 3));
        Assert.Equal("0x0102", ValueConverter.Convert(new byte[] { 1, 2 }, "varbinary", 100));
    }

    [Fact]
    public void Showplan_parser_extracts_cost_operators_and_missing_indexes()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan"><BatchSequence><Batch><Statements>
            <StmtSimple StatementSubTreeCost="1.25" StatementEstRows="42"><QueryPlan>
            <MissingIndexes><MissingIndexGroup><MissingIndex Schema="[dbo]" Table="[Orders]"><ColumnGroup Usage="EQUALITY"><Column Name="[OrderStatus]"/></ColumnGroup></MissingIndex></MissingIndexGroup></MissingIndexes>
            <RelOp PhysicalOp="Hash Match" LogicalOp="Aggregate" EstimateRows="42" EstimatedTotalSubtreeCost="1.25">
              <RelOp PhysicalOp="Clustered Index Scan" LogicalOp="Clustered Index Scan" EstimateRows="1000" EstimatedTotalSubtreeCost="0.9"/>
            </RelOp></QueryPlan></StmtSimple></Statements></Batch></BatchSequence></ShowPlanXML>
            """;
        var summary = new ShowplanParser().Parse(xml);
        Assert.Equal(1.25, summary.EstimatedTotalCost);
        Assert.Equal("Hash Match", summary.TopOperators[0].PhysicalOp);
        Assert.Contains("[dbo].[Orders]", summary.MissingIndexes[0]);
    }
}
