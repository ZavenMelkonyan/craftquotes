using System.Globalization;
using CraftQuotes.Api.Domain;

namespace CraftQuotes.Api.Features.Quotes;

/// <summary>
/// Detail representation, tagged with a strong ETag on GET. It deliberately has no time-derived fields
/// (no isExpired): the same version must always produce the same representation.
/// </summary>
public sealed record QuoteResponse(
    Guid Id,
    string Number,
    string Title,
    string? Description,
    CustomerResponse Customer,
    DateOnly ValidUntil,
    QuoteStatus Status,
    IReadOnlyList<QuoteLineResponse> Lines,
    decimal Subtotal,
    decimal VatTotal,
    decimal Total,
    string Currency,
    string? RejectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? DecidedAt,
    int Version)
{
    internal static QuoteResponse From(Quote quote) => new(
        quote.Id,
        FormatNumber(quote.Number),
        quote.Title,
        quote.Description,
        new CustomerResponse(quote.Customer.Name, quote.Customer.Email, quote.Customer.Phone, quote.Customer.SiteAddress),
        quote.ValidUntil,
        quote.Status,
        quote.Lines
            .OrderBy(l => l.Position)
            .Select(l => new QuoteLineResponse(
                l.Position, l.Type, l.Description, l.Quantity, l.Unit, l.UnitPrice, l.VatRate,
                l.NetAmount, l.VatAmount, l.TotalAmount))
            .ToList(),
        quote.Subtotal,
        quote.VatTotal,
        quote.Total,
        Money.Currency,
        quote.RejectionReason,
        quote.CreatedAt,
        quote.UpdatedAt,
        quote.SentAt,
        quote.DecidedAt,
        quote.Version);

    internal static string FormatNumber(long number) => string.Create(CultureInfo.InvariantCulture, $"Q-{number:D6}");
}

public sealed record CustomerResponse(string Name, string Email, string? Phone, string? SiteAddress);

public sealed record QuoteLineResponse(
    int Position,
    LineType Type,
    string Description,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal VatRate,
    decimal NetAmount,
    decimal VatAmount,
    decimal TotalAmount);
