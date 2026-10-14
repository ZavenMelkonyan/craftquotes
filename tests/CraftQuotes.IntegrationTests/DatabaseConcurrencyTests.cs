using CraftQuotes.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace CraftQuotes.IntegrationTests;

/// <summary>
/// R11: the database predicate (UPDATE … WHERE version = @loaded), not only the If-Match pre-check, rejects a stale
/// write; root and lines are saved atomically. Two independent contexts stand in for two concurrent requests that
/// both passed their If-Match check before either saved.
/// </summary>
public sealed class DatabaseConcurrencyTests(ApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task R11_Second_writer_with_a_line_only_edit_is_rejected_and_leaves_no_partial_lines()
    {
        var original = await CreateAsync();
        var now = new BusinessInstant(ApiFactory.StartTime, Today);
        var customer = new CustomerInfo("Kari Nordmann", "kari@example.com", null, "Fjellveien 12, 5019 Bergen");
        var originalLines = original.Lines
            .Select(l => new QuoteLineInput(l.Type, l.Description, l.Quantity, l.Unit, l.UnitPrice, l.VatRate))
            .ToList();

        await using var first = Api.CreateDbContext();
        await using var second = Api.CreateDbContext();
        var firstCopy = await first.Quotes.SingleAsync(q => q.Id == original.Id, Ct);
        var secondCopy = await second.Quotes.SingleAsync(q => q.Id == original.Id, Ct);

        // First writer: changes the title only.
        firstCopy.ReplaceDraftContent("Renamed by writer 1", null, customer, original.ValidUntil, originalLines, now);
        // Second writer: line-only edit (reworded description) with unchanged totals.
        var rewordedLines = originalLines.Select((l, i) => i == 0 ? l with { Description = "Reworded by writer 2" } : l).ToList();
        secondCopy.ReplaceDraftContent(original.Title, null, customer, original.ValidUntil, rewordedLines, now);
        secondCopy.Total.ShouldBe(original.Total);

        await first.SaveChangesAsync(Ct);
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Ct));

        await using var fresh = Api.CreateDbContext();
        var stored = await fresh.Quotes.AsNoTracking().SingleAsync(q => q.Id == original.Id, Ct);
        stored.Title.ShouldBe("Renamed by writer 1");
        stored.Version.ShouldBe(2);
        stored.Lines.Count.ShouldBe(original.Lines.Count);
        stored.Lines.ShouldNotContain(l => l.Description == "Reworded by writer 2");
        stored.Total.ShouldBe(original.Total);
        (await fresh.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM quote_lines").SingleAsync(Ct))
            .ShouldBe(original.Lines.Count);
    }
}
