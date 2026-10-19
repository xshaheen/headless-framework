// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Moves the named types of a schema a transformer created into <c>components.schemas</c>, the way the native
/// generator does for the schemas it creates itself.
/// </summary>
/// <remarks>
/// A transformer context's <c>GetOrCreateSchemaAsync</c> returns the schema inline: every named type stays nested, and
/// a nullable property of a named type keeps only its marker. Without this, each operation would repeat the whole
/// type and the nullable properties would read as non-nullable.
/// </remarks>
internal static class OpenApiSchemaComponents
{
    /// <summary>
    /// Registers <paramref name="schema"/> and every named type inside it as components, and returns the schema to
    /// place in the operation: a reference when the schema itself is a named type.
    /// </summary>
    public static IOpenApiSchema Resolve(OpenApiDocument document, IOpenApiSchema schema)
    {
        if (schema is not OpenApiSchema concrete)
        {
            return schema;
        }

        if (
            concrete.Metadata?.TryGetValue(OpenApiSchemaNullability.SchemaIdKey, out var value) == true
            && value is string { Length: > 0 } id
        )
        {
            if (document.Components?.Schemas?.ContainsKey(id) != true)
            {
                // Register before descending, so a type that refers to itself resolves to its own component.
                document.AddComponent(id, concrete);
                _ResolveChildren(document, concrete);
            }

            return new OpenApiSchemaReference(id, document);
        }

        _ResolveChildren(document, concrete);

        return concrete;
    }

    private static void _ResolveChildren(OpenApiDocument document, OpenApiSchema schema)
    {
        if (schema.Properties is { } properties)
        {
            foreach (var key in properties.Keys.ToArray())
            {
                var property = properties[key];
                var resolved = Resolve(document, property);

                properties[key] =
                    property is OpenApiSchema concrete && OpenApiSchemaNullability.IsMarkedNullableProperty(concrete)
                        ? new OpenApiSchema { OneOf = [new OpenApiSchema { Type = JsonSchemaType.Null }, resolved] }
                        : resolved;
            }
        }

        if (schema.Items is { } items)
        {
            schema.Items = Resolve(document, items);
        }

        if (schema.AdditionalProperties is { } additionalProperties)
        {
            schema.AdditionalProperties = Resolve(document, additionalProperties);
        }

        _ResolveAll(document, schema.AllOf);
        _ResolveAll(document, schema.AnyOf);
        _ResolveAll(document, schema.OneOf);
    }

    private static void _ResolveAll(OpenApiDocument document, IList<IOpenApiSchema>? schemas)
    {
        if (schemas is null)
        {
            return;
        }

        for (var i = 0; i < schemas.Count; i++)
        {
            schemas[i] = Resolve(document, schemas[i]);
        }
    }
}
