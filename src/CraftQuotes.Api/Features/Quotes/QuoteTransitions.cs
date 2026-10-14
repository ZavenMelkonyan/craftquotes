using CraftQuotes.Api.Domain;
using CraftQuotes.Api.Infrastructure.Http;
using CraftQuotes.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CraftQuotes.Api.Features.Quotes;

public sealed record RejectQuoteRequest(string? Reason = null);

internal sealed class RejectQuoteRequestValidator : AbstractValidator<RejectQuoteRequest>
{
    public RejectQuoteRequestValidator() =>
        RuleFor(r => r.Reason)
            .Must(reason => reason is null || !string.IsNullOrWhiteSpace(reason))
            .WithMessage("'Reason' must not be blank; omit it instead.")
            .MaximumLength(Quote.RejectionReasonMaxLength)
            .NoNulCharacters();
}

internal static class QuoteTransitions
{
    public static Task<Results<Ok<QuoteResponse>, ProblemHttpResult>> Send(
        Guid id, IfMatch ifMatch, QuotesDbContext db, BusinessClock clock, CancellationToken cancellationToken) =>
        QuoteMutation.ExecuteAsync(id, ifMatch, db, quote => quote.Send(clock.Now()), cancellationToken);

    public static Task<Results<Ok<QuoteResponse>, ProblemHttpResult>> Accept(
        Guid id, IfMatch ifMatch, QuotesDbContext db, BusinessClock clock, CancellationToken cancellationToken) =>
        QuoteMutation.ExecuteAsync(id, ifMatch, db, quote => quote.Accept(clock.Now()), cancellationToken);

    /// <summary>The body is optional: an empty body rejects without a reason.</summary>
    public static Task<Results<Ok<QuoteResponse>, ProblemHttpResult>> Reject(
        Guid id,
        RejectQuoteRequest? request,
        IfMatch ifMatch,
        QuotesDbContext db,
        BusinessClock clock,
        CancellationToken cancellationToken) =>
        QuoteMutation.ExecuteAsync(
            id, ifMatch, db, quote => quote.Reject(request?.Reason, clock.Now()), cancellationToken);
}
