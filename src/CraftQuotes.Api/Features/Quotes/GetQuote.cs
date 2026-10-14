using CraftQuotes.Api.Infrastructure.Http;
using CraftQuotes.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CraftQuotes.Api.Features.Quotes;

internal static class GetQuote
{
    public static async Task<Results<Ok<QuoteResponse>, ProblemHttpResult>> Handle(
        Guid id,
        QuotesDbContext db,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var quote = await db.Quotes.AsNoTracking().SingleOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (quote is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound);
        }

        response.Headers.ETag = QuoteETag.From(quote.Version);
        return TypedResults.Ok(QuoteResponse.From(quote));
    }
}
