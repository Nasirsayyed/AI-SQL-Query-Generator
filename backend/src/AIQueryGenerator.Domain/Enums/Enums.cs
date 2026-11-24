namespace AIQueryGenerator.Domain.Enums;

public enum SqlAuthenticationType
{
    /// <summary>SQL Server login (username + password held in the secret store).</summary>
    SqlPassword = 1,
    /// <summary>Windows integrated security using the API process identity.</summary>
    WindowsIntegrated = 2,
    /// <summary>Microsoft Entra ID managed identity of the hosting service.</summary>
    EntraManagedIdentity = 3,
    /// <summary>Microsoft Entra ID default credential chain.</summary>
    EntraDefault = 4,
}

public enum ConnectionStatus
{
    Untested = 0,
    Healthy = 1,
    Failed = 2,
    Unsafe = 3,
}

public enum DbObjectType
{
    Schema = 1,
    Table = 2,
    View = 3,
    Column = 4,
}

public enum ObjectAccessLevel
{
    Allow = 1,
    Deny = 2,
}

public enum QueryExecutionStatus
{
    Generated = 1,
    ClarificationRequested = 2,
    ValidationFailed = 3,
    Succeeded = 4,
    Failed = 5,
    TimedOut = 6,
    Cancelled = 7,
}

public enum AiProviderType
{
    OpenAI = 1,
    AzureOpenAI = 2,
    OpenAICompatible = 3,
}
