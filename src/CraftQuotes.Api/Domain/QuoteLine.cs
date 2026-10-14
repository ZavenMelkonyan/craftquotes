namespace CraftQuotes.Api.Domain;

public sealed class QuoteLine
{
    public const int DescriptionMaxLength = 200;
    public const int UnitMaxLength = 10;
    public const decimal MaxQuantity = 100_000m;
    public const int QuantityDecimals = 3;
    public const decimal MaxUnitPrice = 10_000_000m;
    public const int UnitPriceDecimals = 2;
    public const decimal MaxVatRate = 1m;
    public const int VatRateDecimals = 4;

    private QuoteLine() { } // EF Core

    public int Position { get; private set; }
    public LineType Type { get; private set; }
    public string Description { get; private set; } = null!;
    public decimal Quantity { get; private set; }
    public string Unit { get; private set; } = null!;
    public decimal UnitPrice { get; private set; }
    public decimal VatRate { get; private set; }
    public decimal NetAmount { get; private set; }
    public decimal VatAmount { get; private set; }
    public decimal TotalAmount { get; private set; }

    internal static QuoteLine Create(int position, QuoteLineInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(position);
        if (!Enum.IsDefined(input.Type))
        {
            throw new ArgumentOutOfRangeException(nameof(input), input.Type, "Unknown line type.");
        }

        var description = Guard.Required(input.Description, DescriptionMaxLength);
        var unit = Guard.Required(input.Unit, UnitMaxLength);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input.Quantity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(input.Quantity, MaxQuantity);
        Guard.MaxDecimals(input.Quantity, QuantityDecimals);

        // Compare, don't test the sign bit: JSON "-0" is a valid zero (ThrowIfNegative would reject it).
        ArgumentOutOfRangeException.ThrowIfLessThan(input.UnitPrice, 0m);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(input.UnitPrice, MaxUnitPrice);
        Guard.MaxDecimals(input.UnitPrice, UnitPriceDecimals);

        ArgumentOutOfRangeException.ThrowIfLessThan(input.VatRate, 0m);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(input.VatRate, MaxVatRate);
        Guard.MaxDecimals(input.VatRate, VatRateDecimals);

        // Policy: round each line, then sum rounded lines for the quote totals.
        var netAmount = Money.Round(input.Quantity * input.UnitPrice);
        var vatAmount = Money.Round(netAmount * input.VatRate);

        return new QuoteLine
        {
            Position = position,
            Type = input.Type,
            Description = description,
            Quantity = input.Quantity,
            Unit = unit,
            UnitPrice = input.UnitPrice,
            VatRate = input.VatRate,
            NetAmount = netAmount,
            VatAmount = vatAmount,
            TotalAmount = netAmount + vatAmount,
        };
    }
}
