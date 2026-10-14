using CraftQuotes.Api.Domain;
using Shouldly;
using static CraftQuotes.UnitTests.TestData;

namespace CraftQuotes.UnitTests;

public sealed class QuoteLineRulesTests
{
    [Fact]
    public void R8_Totals_are_computed_from_lines_Kari_fixture()
    {
        var quote = Draft(KariLines);

        quote.Subtotal.ShouldBe(18_215.00m);
        quote.VatTotal.ShouldBe(4_553.75m);
        quote.Total.ShouldBe(22_768.75m);
        quote.Lines.Select(l => l.NetAmount).ShouldBe([4_750.00m, 8_990.00m, 1_335.00m, 2_450.00m, 690.00m]);
    }

    [Fact]
    public void R8_Fractional_quantity_rounds_line_net_away_from_zero()
    {
        // 2.5 h x 949.99 = 2,374.975
        var line = Draft([Line(quantity: 2.5m, unitPrice: 949.99m)]).Lines[0];

        line.NetAmount.ShouldBe(2_374.98m);
    }

    [Fact]
    public void R8_Vat_midpoint_rounds_away_from_zero()
    {
        // 0.10 x 25% = 0.025
        var line = Draft([Line(quantity: 1m, unitPrice: 0.10m, vatRate: 0.25m)]).Lines[0];

        line.VatAmount.ShouldBe(0.03m);
        line.TotalAmount.ShouldBe(0.13m);
    }

    [Fact]
    public void R8_Quote_totals_are_the_sum_of_rounded_line_amounts()
    {
        // Each line VAT is 0.025 -> 0.03; summing first and rounding once would give 0.05.
        var quote = Draft([Line(1m, 0.10m, 0.25m), Line(1m, 0.10m, 0.25m)]);

        quote.VatTotal.ShouldBe(0.06m);
    }

    [Fact]
    public void R8_Zero_price_line_is_allowed()
    {
        var line = Draft([Line(unitPrice: 0m)]).Lines[0];

        (line.NetAmount, line.VatAmount, line.TotalAmount).ShouldBe((0m, 0m, 0m));
    }

    [Fact]
    public void R9_Upper_bounds_are_accepted_and_totals_are_exact()
    {
        var maxLine = Line(QuoteLine.MaxQuantity, QuoteLine.MaxUnitPrice, QuoteLine.MaxVatRate);

        var quote = Draft(Enumerable.Repeat(maxLine, Quote.MaxLines).ToArray());

        quote.Lines[0].NetAmount.ShouldBe(1_000_000_000_000.00m);
        quote.Subtotal.ShouldBe(100_000_000_000_000.00m);
        quote.Total.ShouldBe(200_000_000_000_000.00m);
    }

    [Fact]
    public void R9_Trailing_zeros_are_not_extra_decimals()
    {
        var line = Draft([Line(quantity: 5.000m, unitPrice: 950.000m, vatRate: 0.250000m)]).Lines[0];

        line.NetAmount.ShouldBe(4_750.00m);
    }

    public static TheoryData<decimal> InvalidQuantities => [0m, -1m, 100_000.001m, 1.0001m];
    public static TheoryData<decimal> InvalidUnitPrices => [-0.01m, 10_000_000.01m, 949.999m];
    public static TheoryData<decimal> InvalidVatRates => [-0.01m, 1.0001m, 0.12345m];

    [Theory]
    [MemberData(nameof(InvalidQuantities))]
    public void R9_Quantity_out_of_range_or_too_precise_is_rejected(decimal quantity) =>
        Should.Throw<ArgumentException>(() => Draft([Line(quantity: quantity)]));

    [Theory]
    [MemberData(nameof(InvalidUnitPrices))]
    public void R9_Unit_price_out_of_range_or_too_precise_is_rejected(decimal unitPrice) =>
        Should.Throw<ArgumentException>(() => Draft([Line(unitPrice: unitPrice)]));

    [Theory]
    [MemberData(nameof(InvalidVatRates))]
    public void R9_Vat_rate_out_of_range_or_too_precise_is_rejected(decimal vatRate) =>
        Should.Throw<ArgumentException>(() => Draft([Line(vatRate: vatRate)]));

    [Theory]
    [InlineData("", "h")]
    [InlineData("Work", " ")]
    [InlineData("Work", "12345678901")]
    public void R9_Description_and_unit_are_required_and_bounded(string description, string unit) =>
        Should.Throw<ArgumentException>(
            () => Draft([new QuoteLineInput(LineType.Labour, description, 1m, unit, 100m, 0.25m)]));

    [Fact]
    public void R9_Description_longer_than_200_characters_is_rejected()
    {
        var description = new string('x', QuoteLine.DescriptionMaxLength + 1);

        Should.Throw<ArgumentException>(
            () => Draft([new QuoteLineInput(LineType.Labour, description, 1m, "h", 100m, 0.25m)]));
    }

    [Fact]
    public void R9_At_most_100_lines()
    {
        Should.Throw<ArgumentException>(() => Draft(Enumerable.Repeat(Line(), Quote.MaxLines + 1).ToArray()));
    }
}
