using CraftQuotes.Api.Domain;
using CraftQuotes.Api.Infrastructure.Http;
using CraftQuotes.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CraftQuotes.Api.Features.Quotes;

/// <summary>The one sequence every mutation of an existing quote follows (PLAN §4, "Mechanics").</summary>
internal static class QuoteMutation
{
    public static async Task<Results<Ok<QuoteResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        Guid id,
        IfMatch ifMatch,
        QuotesDbContext db,
        Action<Quote> command,
        CancellationToken cancellationToken)
    {
        // 1. Load → 404.
        var quote = await db.Quotes.SingleOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (quote is null)
        {
            return TypedResults.NotFound();
        }

        var loadedVersion = quote.Version;

        // 2. Domain command → DomainException → 409. A failed command leaves the aggregate unchanged.
        command(quote);

        // 3. Precondition against the version we loaded → 428 / 412. Nothing is saved.
        if (ifMatch.Evaluate(loadedVersion) is { } preconditionFailed)
        {
            return preconditionFailed;
        }

        // 4. EF issues UPDATE … WHERE version = <loaded version>, which step 3 proved equals the client's.
        //    A writer that committed in between → DbUpdateConcurrencyException → 412 (ApiExceptionHandler).
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(QuoteResponse.From(quote));
    }
}
