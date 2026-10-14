using CraftQuotes.Api.Domain;
using Shouldly;

namespace CraftQuotes.UnitTests;

public sealed class MoneyTests
{
    public static TheoryData<decimal, decimal> RoundingCases => new()
    {
        { 2_374.975m, 2_374.98m },
        { 0.025m, 0.03m },
        { -0.025m, -0.03m },
        { 1.004m, 1.00m },
        { 18_215.00m, 18_215.00m },
    };

    [Theory]
    [MemberData(nameof(RoundingCases))]
    public void Round_uses_two_decimals_and_midpoint_away_from_zero(decimal amount, decimal expected) =>
        Money.Round(amount).ShouldBe(expected);

    public static TheoryData<decimal, int, bool> DecimalsCases => new()
    {
        { 950m, 2, true },
        { 950.000m, 2, true },
        { 949.99m, 2, true },
        { 949.999m, 2, false },
        { 2.125m, 3, true },
        { 0.2500m, 4, true },
        { 0.12345m, 4, false },
    };

    [Theory]
    [MemberData(nameof(DecimalsCases))]
    public void HasAtMostDecimals_counts_significant_decimals_not_scale(decimal value, int decimals, bool expected) =>
        Money.HasAtMostDecimals(value, decimals).ShouldBe(expected);
}
