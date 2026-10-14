namespace CraftQuotes.Api.Features.Quotes;

public sealed class QuotesOptions
{
    public const string SectionName = "Quotes";

    /// <summary>IANA zone that defines the business date ("today") for validity and expiry.</summary>
    public string BusinessTimeZone { get; init; } = "Europe/Oslo";

    /// <summary>Applied only on create when <c>validUntil</c> is omitted.</summary>
    public int DefaultValidityDays { get; init; } = 30;
}
