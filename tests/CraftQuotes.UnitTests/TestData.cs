using CraftQuotes.Api.Domain;

namespace CraftQuotes.UnitTests;

internal static class TestData
{
    public static readonly DateOnly Today = new(2026, 10, 14);

    public static BusinessInstant At(DateOnly today) =>
        new(new DateTimeOffset(today.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero), today);

    public static BusinessInstant Now => At(Today);

    public static CustomerInfo Kari => new("Kari Nordmann", "kari@example.com", null, "Fjellveien 12, 5019 Bergen");

    /// <summary>PLAN §1: subtotal 18,215.00, VAT 4,553.75, total 22,768.75.</summary>
    public static QuoteLineInput[] KariLines =>
    [
        new(LineType.Labour, "Installation, electrician", 5m, "h", 950m, 0.25m),
        new(LineType.Material, "EV charger 11 kW", 1m, "pcs", 8990m, 0.25m),
        new(LineType.Material, "Cable 5x6 mm²", 15m, "m", 89m, 0.25m),
        new(LineType.Material, "RCD Type B 40A", 1m, "pcs", 2450m, 0.25m),
        new(LineType.Other, "Call-out / travel", 1m, "pcs", 690m, 0.25m),
    ];

    public static QuoteLineInput Line(decimal quantity = 1m, decimal unitPrice = 100m, decimal vatRate = 0.25m) =>
        new(LineType.Labour, "Work", quantity, "h", unitPrice, vatRate);

    public static Quote Draft(QuoteLineInput[]? lines = null, DateOnly? validUntil = null) =>
        Quote.CreateDraft(
            "EV charger installation",
            null,
            Kari,
            validUntil ?? Today.AddDays(30),
            lines ?? KariLines,
            Now);

    public static Quote Sent(DateOnly? validUntil = null)
    {
        var quote = Draft(validUntil: validUntil);
        quote.Send(Now);
        return quote;
    }

    public static Quote Accepted()
    {
        var quote = Sent();
        quote.Accept(Now);
        return quote;
    }

    public static Quote Rejected()
    {
        var quote = Sent();
        quote.Reject("Too expensive", Now);
        return quote;
    }
}
