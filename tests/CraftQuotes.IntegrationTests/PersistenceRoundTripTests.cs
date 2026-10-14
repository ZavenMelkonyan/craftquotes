using CraftQuotes.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace CraftQuotes.IntegrationTests;

/// <summary>Everything the domain calculated must survive a save and a reload in a fresh context, exactly.</summary>
public sealed class PersistenceRoundTripTests(ApiFactory api) : ApiTest(api)
{
    private static readonly BusinessInstant Now = new(ApiFactory.StartTime, Today);

    private static QuoteLineInput DomainLine(string description, decimal quantity, decimal unitPrice, decimal vatRate = 0.25m) =>
        new(LineType.Material, description, quantity, "pcs", unitPrice, vatRate);

    [Fact]
    public async Task Create_then_replace_lines_and_customer_round_trips_exactly()
    {
        var created = Quote.CreateDraft(
            "EV charger installation",
            "Garage wall mount",
            new CustomerInfo("Kari Nordmann", "kari@example.com", null, "Fjellveien 12, 5019 Bergen"),
            Today.AddDays(30),
            [
                DomainLine("A", 2.5m, 949.99m),
                DomainLine("B", 1m, 0.10m),
                DomainLine("C", 15m, 89m),
                DomainLine("D", QuoteLine.MaxQuantity, QuoteLine.MaxUnitPrice, QuoteLine.MaxVatRate),
            ],
            Now);

        await using (var db = Api.CreateDbContext())
        {
            db.Quotes.Add(created);
            await db.SaveChangesAsync(Ct);
        }

        var reloaded = await Load(created.Id);
        reloaded.Number.ShouldBeGreaterThan(0);
        reloaded.Customer.ShouldBe(created.Customer);
        reloaded.Description.ShouldBe("Garage wall mount");
        reloaded.CreatedAt.ShouldBe(created.CreatedAt);
        ShouldMatch(reloaded, created);

        // Replace: drop "B", reorder, change the customer; edited through a separate tracked context.
        var newCustomer = new CustomerInfo("Kari Nordmann", "kari@example.no", "+47 900 00 000", null);
        await using (var db = Api.CreateDbContext())
        {
            var tracked = await db.Quotes.SingleAsync(q => q.Id == created.Id, Ct);
            tracked.ReplaceDraftContent(
                "EV charger installation (revised)",
                null,
                newCustomer,
                Today.AddDays(14),
                [
                    DomainLine("D", QuoteLine.MaxQuantity, QuoteLine.MaxUnitPrice, QuoteLine.MaxVatRate),
                    DomainLine("C", 15m, 89m),
                    DomainLine("A", 2.5m, 949.99m),
                ],
                Now);
            await db.SaveChangesAsync(Ct);
        }

        var replaced = await Load(created.Id);
        replaced.Title.ShouldBe("EV charger installation (revised)");
        replaced.Description.ShouldBeNull();
        replaced.Customer.ShouldBe(newCustomer);
        replaced.ValidUntil.ShouldBe(Today.AddDays(14));
        replaced.Version.ShouldBe(2);
        var lines = replaced.Lines.OrderBy(l => l.Position).ToList();
        lines.Select(l => (l.Position, l.Description)).ShouldBe([(1, "D"), (2, "C"), (3, "A")]);
        lines[0].NetAmount.ShouldBe(1_000_000_000_000.00m);
        lines[2].NetAmount.ShouldBe(2_374.98m);
        replaced.Subtotal.ShouldBe(1_000_000_000_000.00m + 1_335.00m + 2_374.98m);
        replaced.Total.ShouldBe(replaced.Lines.Sum(l => l.TotalAmount));
    }

    [Fact]
    public async Task Upper_bound_quote_round_trips_unchanged()
    {
        var maxLine = DomainLine("Max", QuoteLine.MaxQuantity, QuoteLine.MaxUnitPrice, QuoteLine.MaxVatRate);
        var created = Quote.CreateDraft(
            "Upper bounds",
            null,
            new CustomerInfo("Test", "test@example.com", null, null),
            Today,
            Enumerable.Repeat(maxLine, Quote.MaxLines).ToArray(),
            Now);

        await using (var db = Api.CreateDbContext())
        {
            db.Quotes.Add(created);
            await db.SaveChangesAsync(Ct);
        }

        var reloaded = await Load(created.Id);
        reloaded.Subtotal.ShouldBe(100_000_000_000_000.00m);
        reloaded.VatTotal.ShouldBe(100_000_000_000_000.00m);
        reloaded.Total.ShouldBe(200_000_000_000_000.00m);
        ShouldMatch(reloaded, created);
    }

    private async Task<Quote> Load(Guid id)
    {
        await using var db = Api.CreateDbContext();
        return await db.Quotes.AsNoTracking().SingleAsync(q => q.Id == id, Ct);
    }

    private static void ShouldMatch(Quote actual, Quote expected)
    {
        (actual.Subtotal, actual.VatTotal, actual.Total).ShouldBe((expected.Subtotal, expected.VatTotal, expected.Total));
        actual.Lines.Count.ShouldBe(expected.Lines.Count);
        foreach (var (a, e) in actual.Lines.OrderBy(l => l.Position).Zip(expected.Lines))
        {
            (a.Position, a.Type, a.Description, a.Unit).ShouldBe((e.Position, e.Type, e.Description, e.Unit));
            (a.Quantity, a.UnitPrice, a.VatRate).ShouldBe((e.Quantity, e.UnitPrice, e.VatRate));
            (a.NetAmount, a.VatAmount, a.TotalAmount).ShouldBe((e.NetAmount, e.VatAmount, e.TotalAmount));
        }
    }
}
