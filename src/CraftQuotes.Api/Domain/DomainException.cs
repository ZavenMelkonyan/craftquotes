namespace CraftQuotes.Api.Domain;

/// <summary>A business rule rejected a command against the aggregate's current state.</summary>
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
