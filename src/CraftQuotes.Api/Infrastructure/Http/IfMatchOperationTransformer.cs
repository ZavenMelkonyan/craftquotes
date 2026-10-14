using System.Reflection;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CraftQuotes.Api.Infrastructure.Http;

/// <summary>
/// <see cref="IfMatch"/> binds through <c>BindAsync</c>, so API Explorer cannot tell it reads a header and
/// leaves it out of the document. This adds the header, as required, to every endpoint whose handler takes it.
/// </summary>
internal sealed class IfMatchOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        // BindAsync parameters are absent from ParameterDescriptions, so inspect the handler's signature instead.
        var takesIfMatch = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<MethodInfo>()
            .Any(handler => handler.GetParameters().Any(p => p.ParameterType == typeof(IfMatch)));

        if (takesIfMatch)
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "If-Match",
                In = ParameterLocation.Header,
                Required = true,
                Description = "The quote's current ETag, e.g. \"3\" (from GET or the response's 'version' field).",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            });
        }

        return Task.CompletedTask;
    }
}
