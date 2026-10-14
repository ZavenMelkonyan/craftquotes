using CraftQuotes.Api.Domain;
using CraftQuotes.Api.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CraftQuotes.Api.Features.Quotes;

// Status is bound as a string: Minimal APIs' enum binding (Enum.TryParse) would also accept "1".
public sealed record ListQuotesQuery(string? Status, string? Search, int? Page, int? PageSize);

public sealed record QuoteSummaryResponse(
    Guid Id,
    string Number,
    string Title,
    string CustomerName,
    QuoteStatus Status,
    bool IsExpired,
    decimal Total,
    string Currency,
    DateOnly ValidUntil,
    DateTimeOffset CreatedAt);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

internal sealed class ListQuotesQueryValidator : AbstractValidator<ListQuotesQuery>
{
    public ListQuotesQueryValidator()
    {
        RuleFor(q => q.Status)
            .Must(s => s is null || Enum.GetNames<QuoteStatus>().Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"'Status' must be one of: {string.Join(", ", Enum.GetNames<QuoteStatus>())}.");
        RuleFor(q => q.Search).MaximumLength(200);
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, ListQuotes.MaxPageSize);
    }
}

internal static class ListQuotes
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    private const string LikeEscape = @"\";

    public static async Task<Ok<PagedResponse<QuoteSummaryResponse>>> Handle(
        [AsParameters] ListQuotesQuery query,
        QuotesDbContext db,
        BusinessClock clock,
        CancellationToken cancellationToken)
    {
        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? DefaultPageSize;
        var today = clock.Now().Today;

        var quotes = db.Quotes.AsNoTracking();
        if (query.Status is not null)
        {
            var status = Enum.Parse<QuoteStatus>(query.Status, ignoreCase: true);
            quotes = quotes.Where(q => q.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{EscapeLikePattern(query.Search)}%";
            // The escape character is passed explicitly: the two-argument ILike disables backslash escaping.
            quotes = quotes.Where(q =>
                EF.Functions.ILike(q.Title, pattern, LikeEscape) || EF.Functions.ILike(q.Customer.Name, pattern, LikeEscape));
        }

        var totalCount = await quotes.CountAsync(cancellationToken);
        var items = await quotes
            .OrderByDescending(q => q.CreatedAt)
            .ThenByDescending(q => q.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(q => new QuoteSummaryResponse(
                q.Id,
                QuoteResponse.FormatNumber(q.Number), // runs client-side on the fetched column
                q.Title,
                q.Customer.Name,
                q.Status,
                q.Status == QuoteStatus.Sent && today > q.ValidUntil,
                q.Total,
                Money.Currency,
                q.ValidUntil,
                q.CreatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new PagedResponse<QuoteSummaryResponse>(items, page, pageSize, totalCount));
    }

    // User input is matched literally: LIKE wildcards and the escape character itself are escaped.
    private static string EscapeLikePattern(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}
