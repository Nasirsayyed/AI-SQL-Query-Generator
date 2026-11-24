using AIQueryGenerator.Domain.Enums;

namespace AIQueryGenerator.Domain.Entities;

public class DatabaseSchema
{
    public int SchemaId { get; set; }
    public int ConnectionId { get; set; }
    public string SchemaName { get; set; } = string.Empty;
    public DateTime LastSyncedAt { get; set; }

    public DatabaseConnection Connection { get; set; } = null!;
    public ICollection<DatabaseTable> Tables { get; set; } = new List<DatabaseTable>();
}

public class DatabaseTable
{
    public int TableId { get; set; }
    public int SchemaId { get; set; }
    public int ConnectionId { get; set; }
    public string TableName { get; set; } = string.Empty;
    public DbObjectType ObjectType { get; set; } = DbObjectType.Table;

    /// <summary>MS_Description extended property. Untrusted text: it is sanitised before being sent to the AI.</summary>
    public string? Description { get; set; }

    /// <summary>sys.objects.modify_date used for incremental refresh.</summary>
    public DateTime ObjectModifiedAt { get; set; }
    public long? RowCountEstimate { get; set; }
    public DateTime LastSyncedAt { get; set; }

    public DatabaseSchema Schema { get; set; } = null!;
    public ICollection<DatabaseColumn> Columns { get; set; } = new List<DatabaseColumn>();
    public ICollection<DatabaseIndex> Indexes { get; set; } = new List<DatabaseIndex>();
}

public class DatabaseColumn
{
    public int ColumnId { get; set; }
    public int TableId { get; set; }
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public int? MaxLength { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsIdentity { get; set; }
    public bool IsComputed { get; set; }
    public int OrdinalPosition { get; set; }
    public string? Description { get; set; }

    public DatabaseTable Table { get; set; } = null!;
}

public class DatabaseRelationship
{
    public int RelationshipId { get; set; }
    public int ConnectionId { get; set; }
    public string ForeignKeyName { get; set; } = string.Empty;
    public int FromTableId { get; set; }
    public string FromColumnName { get; set; } = string.Empty;
    public int ToTableId { get; set; }
    public string ToColumnName { get; set; } = string.Empty;
    public int OrdinalPosition { get; set; }

    public DatabaseTable FromTable { get; set; } = null!;
    public DatabaseTable ToTable { get; set; } = null!;
}

public class DatabaseIndex
{
    public int DatabaseIndexId { get; set; }
    public int TableId { get; set; }
    public string IndexName { get; set; } = string.Empty;
    public bool IsUnique { get; set; }
    public bool IsPrimaryKey { get; set; }
    public string Columns { get; set; } = string.Empty;

    public DatabaseTable Table { get; set; } = null!;
}
