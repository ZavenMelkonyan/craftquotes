using CraftQuotes.Api.Domain;
using CraftQuotes.Api.Infrastructure.Http;
using CraftQuotes.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CraftQuotes.Api.Features.Quotes;

public sealed record UpdateQuoteRequest(
    string? Title,
    string? Description,
    CustomerRequest? Customer,
    DateOnly? ValidUntil,
    IReadOnlyList<QuoteLineRequest>? Lines) : IQuoteContentRequest;

/// <summary>PUT is a full replacement: no defaults, so validUntil and lines (possibly []) are required.</summary>
internal sealed class UpdateQuoteRequestValidator : AbstractValidator<UpdateQuoteRequest>
{
    public UpdateQuoteRequestValidator()
    {
        Include(new QuoteContentValidator());
        RuleFor(r => r.ValidUntil).NotNull();
        RuleFor(r => r.Lines).NotNull();
    }
}

internal static class UpdateQuote
{
    /// <summary>R2: replaces the draft's whole content; omitted optional fields are cleared.</summary>
    public static Task<Results<Ok<QuoteResponse>, NotFound, ProblemHttpResult>> Handle(
        Guid id,
        UpdateQuoteRequest request,
        IfMatch ifMatch,
        QuotesDbContext db,
        BusinessClock clock,
        CancellationToken cancellationToken) =>
        QuoteMutation.ExecuteAsync(id, ifMatch, db, quote => quote.ReplaceDraftContent(
            request.Title!,
            request.Description,
            request.Customer!.ToDomain(),
            request.ValidUntil!.Value,
            request.Lines!.Select(l => l.ToDomain()).ToList(),
            clock.Now()), cancellationToken);
}
