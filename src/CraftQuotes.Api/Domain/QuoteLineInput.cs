namespace CraftQuotes.Api.Domain;

/// <summary>What a caller supplies for a line; position and amounts are assigned by the aggregate.</summary>
public sealed record QuoteLineInput(
    LineType Type,
    string Description,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal VatRate);
