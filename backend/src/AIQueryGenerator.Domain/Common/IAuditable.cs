namespace AIQueryGenerator.Domain.Common;

/// <summary>Entities with creation/modification timestamps maintained by the persistence layer.</summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
}

/// <summary>Entities that are hidden rather than physically deleted.</summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
}

public abstract class AuditableEntity : IAuditable
{
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
