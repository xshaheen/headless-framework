// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>Publishes numeric values as plain JSON numbers.</summary>
/// <remarks>
/// The Headless web JSON options read numbers from strings (<c>JsonNumberHandling.AllowReadingFromString</c>), so the
/// native generator describes every number as <c>["integer", "string"]</c> with a digits pattern. That leniency is an
/// input convenience, not the contract: responses always carry numbers, and generated clients typed from the union
/// lose their numeric types. This transformer drops the string alternative and its digits pattern. A number written
/// as a string (<c>JsonNumberHandling.WriteAsString</c> on the property, its declaring type, or the options) keeps the
/// native union, because the response really carries a string.
/// </remarks>
internal sealed class NumberSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (
            schema.Type is not { } type
            || (!type.HasFlag(JsonSchemaType.Integer) && !type.HasFlag(JsonSchemaType.Number))
        )
        {
            return Task.CompletedTask;
        }

        if (_GetNumberHandling(context).HasFlag(JsonNumberHandling.WriteAsString))
        {
            return Task.CompletedTask;
        }

        // JSON Schema applies "pattern" to strings only, so on a schema that no longer admits strings it says nothing.
        schema.Type = type & ~JsonSchemaType.String;
        schema.Pattern = null;

        return Task.CompletedTask;
    }

    // System.Text.Json resolves number handling from the property, then its declaring type, then the options.
    private static JsonNumberHandling _GetNumberHandling(OpenApiSchemaTransformerContext context)
    {
        var options = context.JsonTypeInfo.Options;

        if (context.JsonPropertyInfo is { } property)
        {
            return property.NumberHandling
                ?? options.GetTypeInfo(property.DeclaringType).NumberHandling
                ?? options.NumberHandling;
        }

        return context.JsonTypeInfo.NumberHandling ?? options.NumberHandling;
    }
}
