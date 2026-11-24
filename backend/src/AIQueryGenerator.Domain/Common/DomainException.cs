namespace AIQueryGenerator.Domain.Common;

/// <summary>Thrown when a domain invariant would be violated.</summary>
public sealed class DomainException(string message) : Exception(message);
