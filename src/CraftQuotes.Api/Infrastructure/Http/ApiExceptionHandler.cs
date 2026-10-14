using CraftQuotes.Api.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CraftQuotes.Api.Infrastructure.Http;

/// <summary>Turns expected exceptions into ProblemDetails; anything else falls through to the default 500.</summary>
internal sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            DomainException e => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The request conflicts with the quote's current state.",
                Detail = e.Message,
                Extensions = { ["errorCode"] = e.Code },
            },
            // R11: another writer committed between our load and save; the database predicate rejected this write.
            DbUpdateConcurrencyException => new ProblemDetails
            {
                Status = StatusCodes.Status412PreconditionFailed,
                Title = "The quote has changed.",
                Detail = "Another request modified the quote while this one was processed; reload it and retry.",
            },
            // Minimal APIs throw this for binding failures (malformed JSON, integer enums) when
            // ThrowOnBadRequest is on (Development); elsewhere they return an empty 400 that UseStatusCodePages fills.
            BadHttpRequestException e => new ProblemDetails
            {
                Status = e.StatusCode,
                Title = "The request is malformed.",
                Detail = e.Message,
            },
            // Reading a JSON body with an unknown charset (e.g. "application/json; charset=bogus") throws from the
            // framework's body reader, outside the binding errors above.
            InvalidOperationException e when e.TargetSite?.DeclaringType == typeof(HttpRequestJsonExtensions) =>
                new ProblemDetails
                {
                    Status = StatusCodes.Status415UnsupportedMediaType,
                    Title = "The request body's charset is not supported.",
                    Detail = "Send JSON as UTF-8 (Content-Type: application/json).",
                },
            _ => null,
        };

        if (problem is null)
        {
            return false;
        }

        // Written regardless of the Accept header: IProblemDetailsService.TryWriteAsync writes nothing for e.g.
        // "Accept: text/html", which would leave an empty body and report the exception as unhandled.
        await TypedResults.Problem(problem).ExecuteAsync(httpContext);
        return true;
    }
}
