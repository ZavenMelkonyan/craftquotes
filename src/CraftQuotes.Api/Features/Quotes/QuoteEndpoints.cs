using CraftQuotes.Api.Infrastructure.Http;

namespace CraftQuotes.Api.Features.Quotes;

internal static class QuoteEndpoints
{
    public static IEndpointRouteBuilder MapQuoteEndpoints(this IEndpointRouteBuilder app)
    {
        var quotes = app.MapGroup("/api/quotes").WithTags("Quotes");

        quotes.MapPost("/", CreateQuote.Handle)
            .WithName("CreateQuote")
            .WithValidation<CreateQuoteRequest>();

        quotes.MapGet("/{id:guid}", GetQuote.Handle)
            .WithName("GetQuote")
            .ProducesProblem(StatusCodes.Status404NotFound);

        quotes.MapGet("/", ListQuotes.Handle)
            .WithName("ListQuotes")
            .WithValidation<ListQuotesQuery>();

        return app;
    }
}
