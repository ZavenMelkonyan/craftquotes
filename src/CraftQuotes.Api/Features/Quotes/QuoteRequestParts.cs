using CraftQuotes.Api.Domain;
using FluentValidation;

namespace CraftQuotes.Api.Features.Quotes;

// Request parts shared by create (POST) and replace (PUT). Every member is nullable so that a missing value
// is reported by the validator as 400 instead of silently becoming a default (e.g. vatRate = 0).

public sealed record CustomerRequest(string? Name, string? Email, string? Phone, string? SiteAddress)
{
    internal CustomerInfo ToDomain() => new(Name!, Email!, Phone, SiteAddress);
}

public sealed record QuoteLineRequest(
    LineType? Type,
    string? Description,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? VatRate)
{
    internal QuoteLineInput ToDomain() =>
        new(Type!.Value, Description!, Quantity!.Value, Unit!, UnitPrice!.Value, VatRate!.Value);
}

internal sealed class CustomerRequestValidator : AbstractValidator<CustomerRequest>
{
    public CustomerRequestValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(CustomerInfo.NameMaxLength);
        RuleFor(c => c.Email).NotEmpty().MaximumLength(CustomerInfo.EmailMaxLength).EmailAddress();
        RuleFor(c => c.Phone).MaximumLength(CustomerInfo.PhoneMaxLength);
        RuleFor(c => c.SiteAddress).MaximumLength(CustomerInfo.SiteAddressMaxLength);
    }
}

/// <summary>R9 at the boundary (400). The domain enforces the same limits as guards.</summary>
internal sealed class QuoteLineRequestValidator : AbstractValidator<QuoteLineRequest>
{
    public QuoteLineRequestValidator()
    {
        RuleFor(l => l.Type).NotNull().IsInEnum();
        RuleFor(l => l.Description).NotEmpty().MaximumLength(QuoteLine.DescriptionMaxLength);
        RuleFor(l => l.Unit).NotEmpty().MaximumLength(QuoteLine.UnitMaxLength);

        RuleFor(l => l.Quantity).NotNull()
            .GreaterThan(0m).LessThanOrEqualTo(QuoteLine.MaxQuantity)
            .Must(q => HasAtMostDecimals(q, QuoteLine.QuantityDecimals))
            .WithMessage($"'{{PropertyName}}' must have at most {QuoteLine.QuantityDecimals} decimal places.");

        RuleFor(l => l.UnitPrice).NotNull()
            .GreaterThanOrEqualTo(0m).LessThanOrEqualTo(QuoteLine.MaxUnitPrice)
            .Must(p => HasAtMostDecimals(p, QuoteLine.UnitPriceDecimals))
            .WithMessage($"'{{PropertyName}}' must have at most {QuoteLine.UnitPriceDecimals} decimal places.");

        RuleFor(l => l.VatRate).NotNull()
            .InclusiveBetween(0m, QuoteLine.MaxVatRate)
            .Must(r => HasAtMostDecimals(r, QuoteLine.VatRateDecimals))
            .WithMessage($"'{{PropertyName}}' must have at most {QuoteLine.VatRateDecimals} decimal places.");
    }

    private static bool HasAtMostDecimals(decimal? value, int decimals) =>
        value is not { } v || Money.HasAtMostDecimals(v, decimals);
}
