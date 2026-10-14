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

        quotes.MapPut("/{id:guid}", UpdateQuote.Handle)
            .WithName("UpdateQuote")
            .WithValidation<UpdateQuoteRequest>()
            .ProducesMutationProblems();

        quotes.MapPost("/{id:guid}/send", QuoteTransitions.Send)
            .WithName("SendQuote")
            .ProducesMutationProblems();

        quotes.MapPost("/{id:guid}/accept", QuoteTransitions.Accept)
            .WithName("AcceptQuote")
            .ProducesMutationProblems();

        quotes.MapPost("/{id:guid}/reject", QuoteTransitions.Reject)
            .WithName("RejectQuote")
            .WithValidation<RejectQuoteRequest>()
            .ProducesMutationProblems();

        return app;
    }

    private static RouteHandlerBuilder ProducesMutationProblems(this RouteHandlerBuilder builder) =>
        builder
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
}
