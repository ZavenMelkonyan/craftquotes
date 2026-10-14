using FluentValidation;

namespace CraftQuotes.Api.Features.Quotes;

internal static class ValidationRules
{
    /// <summary>Postgres text cannot store U+0000; reject it as input (400) rather than fail on save (500).</summary>
    public static IRuleBuilderOptions<T, string?> NoNulCharacters<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(value => value is null || !value.Contains('\0'))
            .WithMessage("'{PropertyName}' must not contain NUL characters.");

    /// <summary>
    /// Exactly one member name (case-insensitive). Enum.Parse and JsonStringEnumConverter would also accept
    /// numbers ("1") and flag combinations ("Labour, Material"), silently turning them into another member.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> IsEnumName<T, TEnum>(this IRuleBuilder<T, string?> rule)
        where TEnum : struct, Enum =>
        rule.Must(value => value is null || Enum.GetNames<TEnum>().Contains(value, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"'{{PropertyName}}' must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}.");
}
