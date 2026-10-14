using CraftQuotes.Api.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CraftQuotes.Api.Infrastructure.Http;

/// <summary>Turns expected exceptions into ProblemDetails; anything else falls through to the default 500.</summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
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
            // Minimal APIs throw this for binding failures (malformed JSON, integer enums) when
            // ThrowOnBadRequest is on (Development); elsewhere they return an empty 400 that UseStatusCodePages fills.
            BadHttpRequestException e => new ProblemDetails
            {
                Status = e.StatusCode,
                Title = "The request is malformed.",
                Detail = e.Message,
            },
            _ => null,
        };

        if (problem is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
