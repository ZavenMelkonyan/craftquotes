using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace CraftQuotes.Api.Infrastructure.Http;

/// <summary>
/// The <c>If-Match</c> precondition, required on every mutation of an existing quote.
/// Parsed during binding, so a malformed header is a 400 before the quote is even loaded;
/// evaluated by the handler after the domain command, so 409 wins over 428/412 (RFC 9110 §13.2.1).
/// </summary>
public sealed class IfMatch
{
    // null: header absent. Otherwise a non-empty list that is either exactly [*] or contains no *.
    private readonly IList<EntityTagHeaderValue>? _tags;

    private IfMatch(IList<EntityTagHeaderValue>? tags) => _tags = tags;

    public static ValueTask<IfMatch?> BindAsync(HttpContext context)
    {
        var header = context.Request.Headers.IfMatch;
        if (header.Count == 0)
        {
            return ValueTask.FromResult<IfMatch?>(new IfMatch(null));
        }

        // The strict parser rejects the whole list on any invalid member; the lenient TryParseList skips it.
        if (!EntityTagHeaderValue.TryParseStrictList(header, out var tags)
            || tags.Count == 0
            || (tags.Count > 1 && tags.Any(t => t.Equals(EntityTagHeaderValue.Any))))
        {
            throw new BadHttpRequestException(
                "The If-Match header is malformed. Send one or more entity tags, e.g. If-Match: \"3\".");
        }

        return ValueTask.FromResult<IfMatch?>(new IfMatch(tags));
    }

    /// <summary>Returns the failure to send, or <c>null</c> when the precondition holds for <paramref name="version"/>.</summary>
    public ProblemHttpResult? Evaluate(int version)
    {
        // "*" only asserts that the quote exists; we require the client to name the version it saw.
        if (_tags is null || (_tags.Count == 1 && _tags[0].Equals(EntityTagHeaderValue.Any)))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status428PreconditionRequired,
                title: "If-Match is required.",
                detail: "Send If-Match with the quote's current ETag (e.g. \"3\"), taken from GET or the 'version' field.");
        }

        // Strong comparison: weak tags never match, but they don't invalidate the other members of the list.
        var current = new EntityTagHeaderValue(QuoteETag.From(version));
        if (_tags.Any(tag => tag.Compare(current, useStrongComparison: true)))
        {
            return null;
        }

        return TypedResults.Problem(
            statusCode: StatusCodes.Status412PreconditionFailed,
            title: "The quote has changed.",
            detail: $"The quote is at version {version}; reload it and retry.");
    }
}
