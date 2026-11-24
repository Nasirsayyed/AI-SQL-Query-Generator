using System.Collections.Concurrent;
using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Application.Schema;
using AIQueryGenerator.Application.Sql;
using AIQueryGenerator.Domain.Entities;
using AIQueryGenerator.Domain.Enums;
using AIQueryGenerator.Shared.Exceptions;

namespace AIQueryGenerator.IntegrationTests.Infrastructure;

/// <summary>Scripted AI provider: returns queued JSON responses and records the prompts it received.</summary>
public sealed class FakeAiClient : IAiCompletionClient, IAiClientFactory
{
    public ConcurrentQueue<string> Responses { get; } = new();
    public ConcurrentQueue<AiJsonCompletionRequest> Requests { get; } = new();
    public bool Configured { get; set; } = true;

    public string ProviderName => "Fake";
    public string Model => "fake-model";
    public int MaxSchemaTables => 40;

    public Task<IAiCompletionClient> CreateAsync(CancellationToken cancellationToken) =>
        Configured ? Task.FromResult<IAiCompletionClient>(this)
            : throw new ExternalServiceException("The AI provider is not configured.", ErrorCodes.AiNotConfigured, 503);

    public Task<AiCompletionResult> CompleteJsonAsync(AiJsonCompletionRequest request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        if (!Responses.TryDequeue(out var content))
            throw new InvalidOperationException("No scripted AI response queued.");
        return Task.FromResult(new AiCompletionResult(content, Model, 100, 50));
    }
}

/// <summary>Target database stand-in that records executed SQL and returns a fixed result set.</summary>
public sealed class FakeTargetDatabase : ITargetDatabaseClient
{
    public ConcurrentQueue<QueryExecutionRequest> Executed { get; } = new();
    public bool LeastPrivilege { get; set; } = true;
    public int RowsToReturn { get; set; } = 3;

    public Task<ConnectionTestResult> TestConnectionAsync(DatabaseConnection connection, CancellationToken cancellationToken) =>
        Task.FromResult(new ConnectionTestResult(true, LeastPrivilege, LeastPrivilege ? "ok" : "elevated", "16.0", "aiqg_reader",
            LeastPrivilege ? [] : ["The user is a member of db_owner."]));

    public Task<QueryResultData> ExecuteReadOnlyAsync(QueryExecutionRequest request, CancellationToken cancellationToken)
    {
        Executed.Enqueue(request);
        var rows = Enumerable.Range(1, Math.Min(RowsToReturn, request.MaxRows))
            .Select(i => new object?[] { $"Customer {i}", 1000m * i }).ToList();
        return Task.FromResult(new QueryResultData
        {
            Columns = [new ResultColumn("CustomerName", "nvarchar", "String"), new ResultColumn("TotalSales", "decimal", "Decimal")],
            Rows = rows,
            IsTruncated = RowsToReturn > request.MaxRows,
            DurationMs = 12,
        });
    }

    public Task<string> GetEstimatedPlanXmlAsync(QueryExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult("""<ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan"><BatchSequence><Batch><Statements><StmtSimple StatementSubTreeCost="0.5" StatementEstRows="10"><QueryPlan><RelOp PhysicalOp="Clustered Index Scan" LogicalOp="Clustered Index Scan" EstimateRows="10" EstimatedTotalSubtreeCost="0.5"/></QueryPlan></StmtSimple></Statements></Batch></BatchSequence></ShowPlanXML>""");
}

/// <summary>Schema discovery returning the AIQueryDemoDB structure.</summary>
public sealed class FakeSchemaDiscovery : ISchemaDiscoveryService
{
    public DateTime ModifiedAt { get; set; } = new(2026, 1, 1);
    public bool IncludeSecretsTable { get; set; } = true;

    public Task<DiscoveredDatabase> DiscoverAsync(DatabaseConnection connection, CancellationToken cancellationToken)
    {
        DiscoveredTable T(string name, params (string Name, string Type, bool Pk)[] cols) => new()
        {
            Schema = "dbo",
            Name = name,
            ObjectType = DbObjectType.Table,
            ModifiedAt = ModifiedAt,
            RowCountEstimate = 100,
            Columns = cols.Select((c, i) => new DiscoveredColumn { Name = c.Name, DataType = c.Type, IsPrimaryKey = c.Pk, OrdinalPosition = i + 1, MaxLength = c.Type == "nvarchar" ? 200 : null }).ToList(),
            Indexes = [new DiscoveredIndex { Name = $"PK_{name}", IsPrimaryKey = true, IsUnique = true, Columns = cols[0].Name }],
        };

        var tables = new List<DiscoveredTable>
        {
            T("Customers", ("CustomerId", "int", true), ("CustomerName", "nvarchar", false), ("Email", "nvarchar", false), ("City", "nvarchar", false), ("Region", "nvarchar", false), ("CreatedDate", "datetime2", false)),
            T("Orders", ("OrderId", "int", true), ("CustomerId", "int", false), ("OrderDate", "datetime2", false), ("OrderStatus", "nvarchar", false), ("TotalAmount", "decimal", false), ("WarehouseId", "int", false)),
            T("Products", ("ProductId", "int", true), ("ProductName", "nvarchar", false), ("Category", "nvarchar", false), ("UnitPrice", "decimal", false), ("ReorderLevel", "int", false)),
        };
        if (IncludeSecretsTable) tables.Add(T("Secrets", ("SecretId", "int", true), ("Value", "nvarchar", false)));

        return Task.FromResult(new DiscoveredDatabase
        {
            DatabaseName = connection.DatabaseName,
            Tables = tables,
            ForeignKeys = [new DiscoveredForeignKey { Name = "FK_Orders_Customers", FromSchema = "dbo", FromTable = "Orders", FromColumn = "CustomerId", ToSchema = "dbo", ToTable = "Customers", ToColumn = "CustomerId", Ordinal = 1 }],
        });
    }
}
