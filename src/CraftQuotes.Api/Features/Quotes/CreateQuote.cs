using CraftQuotes.Api.Domain;
using CraftQuotes.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace CraftQuotes.Api.Features.Quotes;

public sealed record CreateQuoteRequest(
    string? Title,
    string? Description,
    CustomerRequest? Customer,
    DateOnly? ValidUntil,
    IReadOnlyList<QuoteLineRequest>? Lines);

internal sealed class CreateQuoteRequestValidator : AbstractValidator<CreateQuoteRequest>
{
    public CreateQuoteRequestValidator()
    {
        RuleFor(r => r.Title).NotEmpty().MaximumLength(Quote.TitleMaxLength);
        RuleFor(r => r.Description).MaximumLength(Quote.DescriptionMaxLength);
        RuleFor(r => r.Customer!).NotNull().SetValidator(new CustomerRequestValidator());
        RuleFor(r => r.Lines)
            .Must(lines => lines is null || lines.Count <= Quote.MaxLines)
            .WithMessage($"A quote can have at most {Quote.MaxLines} lines.");
        RuleForEach(r => r.Lines).NotNull().SetValidator(new QuoteLineRequestValidator());
    }
}

internal static class CreateQuote
{
    /// <summary>POST: defaults apply here and only here (validUntil = today + N days, lines = []).</summary>
    public static async Task<Created<QuoteResponse>> Handle(
        CreateQuoteRequest request,
        QuotesDbContext db,
        BusinessClock clock,
        IOptions<QuotesOptions> options,
        CancellationToken cancellationToken)
    {
        var now = clock.Now();
        var quote = Quote.CreateDraft(
            request.Title!,
            request.Description,
            request.Customer!.ToDomain(),
            request.ValidUntil ?? now.Today.AddDays(options.Value.DefaultValidityDays),
            request.Lines?.Select(l => l.ToDomain()).ToList() ?? [],
            now);

        db.Quotes.Add(quote);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/quotes/{quote.Id}", QuoteResponse.From(quote));
    }
}
