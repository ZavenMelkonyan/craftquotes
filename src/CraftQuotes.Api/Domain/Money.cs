namespace CraftQuotes.Api.Domain;

public static class Money
{
    public const string Currency = "NOK";

    /// <summary>The single rounding policy for amounts: 2 decimals, commercial rounding.</summary>
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Compares significant decimals, not the decimal's scale: System.Text.Json keeps trailing zeros,
    /// so <c>950.000</c> must still count as a value with 0 decimals.
    /// </summary>
    public static bool HasAtMostDecimals(decimal value, int decimals) => value == Math.Round(value, decimals);
}
