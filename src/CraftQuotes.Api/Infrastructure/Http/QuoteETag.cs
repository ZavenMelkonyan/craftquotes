using System.Globalization;

namespace CraftQuotes.Api.Infrastructure.Http;

/// <summary>The quote's version as a strong entity tag: <c>"{version}"</c>.</summary>
internal static class QuoteETag
{
    public static string From(int version) => string.Create(CultureInfo.InvariantCulture, $"\"{version}\"");
}
