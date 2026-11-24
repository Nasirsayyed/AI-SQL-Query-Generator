using AIQueryGenerator.Domain.Common;
using AIQueryGenerator.Domain.Enums;

namespace AIQueryGenerator.Domain.Entities;

public class QueryHistory : AuditableEntity, ISoftDelete
{
    public long QueryId { get; set; }
    public int UserId { get; set; }
    public int ConnectionId { get; set; }
    public string QueryTitle { get; set; } = string.Empty;
    public string? UserQuestion { get; set; }
    public string? GeneratedSql { get; set; }
    public string? ExecutedSql { get; set; }

    /// <summary>Parameter values encrypted with the data-protection key ring. Never stored in plain text.</summary>
    public string? ParametersProtected { get; set; }
    public string? Explanation { get; set; }
    public string? TablesUsed { get; set; }
    public string? ChartSuggestionJson { get; set; }
    public string? PromptVersion { get; set; }
    public string? AiModel { get; set; }
    public int? ExecutionDurationMs { get; set; }
    public int? ReturnedRows { get; set; }
    public QueryExecutionStatus ExecutionStatus { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public User User { get; set; } = null!;
    public DatabaseConnection Connection { get; set; } = null!;
}

public class SavedQuery : AuditableEntity
{
    public int SavedQueryId { get; set; }
    public int UserId { get; set; }
    public int ConnectionId { get; set; }
    public string QueryTitle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? UserQuestion { get; set; }
    public string SqlQuery { get; set; } = string.Empty;
    public string? Category { get; set; }
    public bool IsShared { get; set; }

    /// <summary>Parameter definitions and default values, encrypted at rest.</summary>
    public string? ParametersProtected { get; set; }
    public string? ChartConfigJson { get; set; }

    public User User { get; set; } = null!;
    public DatabaseConnection Connection { get; set; } = null!;
    public ICollection<FavoriteQuery> Favorites { get; set; } = new List<FavoriteQuery>();
}

public class FavoriteQuery
{
    public int FavoriteQueryId { get; set; }
    public int UserId { get; set; }
    public int SavedQueryId { get; set; }
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
    public SavedQuery SavedQuery { get; set; } = null!;
}

public class QueryExecutionLog
{
    public long ExecutionId { get; set; }
    public long? QueryId { get; set; }
    public int? SavedQueryId { get; set; }
    public int UserId { get; set; }
    public int ConnectionId { get; set; }
    public QueryExecutionStatus ExecutionStatus { get; set; }
    public int ExecutionDurationMs { get; set; }
    public int RowsReturned { get; set; }
    public bool WasTruncated { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>SHA-256 of the executed SQL for correlation without duplicating text.</summary>
    public string? SqlHash { get; set; }

    /// <summary>Comma separated list of [schema].[table] names referenced by the query.</summary>
    public string? TablesUsed { get; set; }
    public DateTime ExecutedAt { get; set; }

    public QueryHistory? Query { get; set; }
    public User User { get; set; } = null!;
    public DatabaseConnection Connection { get; set; } = null!;
}

public class AuditLog
{
    public long AuditLogId { get; set; }
    public int? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }

    /// <summary>JSON details. Must never contain secrets, passwords or result data.</summary>
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AiProviderConfiguration : AuditableEntity
{
    public int AiProviderConfigurationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public AiProviderType ProviderType { get; set; } = AiProviderType.OpenAI;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>Secret reference such as "env:OPENAI_API_KEY". The key itself is never stored here.</summary>
    public string ApiKeySecretReference { get; set; } = "env:OPENAI_API_KEY";
    public double Temperature { get; set; }
    public int MaxOutputTokens { get; set; } = 2000;
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxSchemaTables { get; set; } = 40;
    public bool IsActive { get; set; }
}
