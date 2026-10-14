namespace CraftQuotes.Api.Domain;

public static class QuoteErrors
{
    public const string NotEditable = "quote.not_editable";
    public const string InvalidTransition = "quote.invalid_transition";
    public const string Expired = "quote.expired";
    public const string NoLines = "quote.no_lines";
    public const string ValidUntilInPast = "quote.valid_until_in_past";
}
