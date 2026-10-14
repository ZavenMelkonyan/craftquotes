using System.Runtime.CompilerServices;

namespace CraftQuotes.Api.Domain;

/// <summary>
/// Argument guards for states the request validator already rules out. A failure here is a bug, not a user error.
/// </summary>
internal static class Guard
{
    public static string Required(
        string? value, int maxLength, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return MaxLength(value, maxLength, paramName);
    }

    public static string? Optional(
        string? value, int maxLength, [CallerArgumentExpression(nameof(value))] string? paramName = null) =>
        value is null ? null : MaxLength(value, maxLength, paramName);

    public static void MaxDecimals(
        decimal value, int decimals, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (!Money.HasAtMostDecimals(value, decimals))
        {
            throw new ArgumentException($"At most {decimals} decimal places are allowed.", paramName);
        }
    }

    private static string MaxLength(string value, int maxLength, string? paramName) =>
        value.Length <= maxLength
            ? value
            : throw new ArgumentException($"At most {maxLength} characters are allowed.", paramName);
}
