using CraftQuotes.Api.Domain;
using CraftQuotes.Api.Features.Quotes;
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
    private static readonly CustomerInfo Kari = new("Kari Nordmann", "kari@example.com", null, "Fjellveien 12, 5019 Bergen");

    private static BusinessInstant Now => new(ApiFactory.StartTime, Today);

    [Fact]
    public async Task R11_Version_predicate_rejects_a_stale_line_edit_after_a_root_only_change()
    {
        // The first writer touches only the quote row (Send), so the second writer's line DELETE/INSERTs succeed on
        // their own: only WHERE version = 1 can reject it. Without it, a Sent quote would get lines nobody reviewed.
        var original = await CreateAsync();
        await using var first = Api.CreateDbContext();
        await using var second = Api.CreateDbContext();
        var firstCopy = await first.Quotes.SingleAsync(q => q.Id == original.Id, Ct);
        var secondCopy = await second.Quotes.SingleAsync(q => q.Id == original.Id, Ct);

        firstCopy.Send(Now);
        secondCopy.ReplaceDraftContent(original.Title, null, Kari, original.ValidUntil, Reworded(original), Now);

        await first.SaveChangesAsync(Ct);
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Ct));

        var stored = await LoadAsync(original.Id);
        (stored.Status, stored.Version).ShouldBe((QuoteStatus.Sent, 2));
        stored.Lines.Select(l => l.Description).ShouldBe(original.Lines.Select(l => l.Description), ignoreOrder: true);
    }

    [Fact]
    public async Task R11_Losing_writer_leaves_no_partial_lines_when_both_replace_lines()
    {
        var original = await CreateAsync();
        await using var first = Api.CreateDbContext();
        await using var second = Api.CreateDbContext();
        var firstCopy = await first.Quotes.SingleAsync(q => q.Id == original.Id, Ct);
        var secondCopy = await second.Quotes.SingleAsync(q => q.Id == original.Id, Ct);

        // First writer: changes the title only. Second writer: line-only edit with unchanged totals.
        firstCopy.ReplaceDraftContent("Renamed by writer 1", null, Kari, original.ValidUntil, Inputs(original), Now);
        secondCopy.ReplaceDraftContent(original.Title, null, Kari, original.ValidUntil, Reworded(original), Now);
        secondCopy.Total.ShouldBe(original.Total);

        await first.SaveChangesAsync(Ct);
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Ct));

        var stored = await LoadAsync(original.Id);
        stored.Title.ShouldBe("Renamed by writer 1");
        stored.Version.ShouldBe(2);
        stored.Lines.Count.ShouldBe(original.Lines.Count);
        stored.Lines.ShouldNotContain(l => l.Description == "Reworded by writer 2");
        stored.Total.ShouldBe(original.Total);
        await using var db = Api.CreateDbContext();
        (await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM quote_lines").SingleAsync(Ct))
            .ShouldBe(original.Lines.Count);
    }

    private static List<QuoteLineInput> Inputs(QuoteResponse quote) =>
        quote.Lines.Select(l => new QuoteLineInput(l.Type, l.Description, l.Quantity, l.Unit, l.UnitPrice, l.VatRate)).ToList();

    private static List<QuoteLineInput> Reworded(QuoteResponse quote) =>
        Inputs(quote).Select((l, i) => i == 0 ? l with { Description = "Reworded by writer 2" } : l).ToList();

    private async Task<Quote> LoadAsync(Guid id)
    {
        await using var db = Api.CreateDbContext();
        return await db.Quotes.AsNoTracking().SingleAsync(q => q.Id == id, Ct);
    }
}
