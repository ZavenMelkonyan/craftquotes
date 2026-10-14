using System.Text.Json;
using FluentValidation;

namespace CraftQuotes.Api.Infrastructure.Http;

/// <summary>Runs the FluentValidation validator for the endpoint's <typeparamref name="T"/> argument; 400 on failure.</summary>
internal sealed class ValidationFilter<T>(IValidator<T> validator) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // Absent optional bodies (e.g. reject without a reason) have nothing to validate.
        if (context.Arguments.OfType<T>().FirstOrDefault() is not { } argument)
        {
            return await next(context);
        }

        var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
        if (result.IsValid)
        {
            return await next(context);
        }

        var errors = result.Errors
            .GroupBy(e => ToJsonPath(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        return TypedResults.ValidationProblem(errors);
    }

    // Report errors under the same camelCase names the client sent, e.g. "lines[0].vatRate".
    private static string ToJsonPath(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}

internal static class ValidationFilterExtensions
{
    public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<ValidationFilter<T>>().ProducesValidationProblem();
}
