// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>Lists every non-nullable property of an object schema in its <c>"required"</c> set.</summary>
/// <remarks>
/// The native generator marks only <see langword="required"/> members and constructor parameters as required. Headless treats a
/// non-nullable property as always present, so a client generated from the document gets a non-optional member for
/// it. Runs after the FluentValidation transformer, so a <c>NotNull</c> rule that removed nullability counts.
/// </remarks>
internal sealed class RequiredSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (context.JsonTypeInfo.Kind != JsonTypeInfoKind.Object || schema.Properties is not { Count: > 0 } properties)
        {
            return Task.CompletedTask;
        }

        foreach (var (name, property) in properties)
        {
            if (!OpenApiSchemaNullability.IsNullable(property))
            {
                (schema.Required ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
            }
        }

        return Task.CompletedTask;
    }
}
