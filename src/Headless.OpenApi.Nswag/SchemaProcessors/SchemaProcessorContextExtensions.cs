// Copyright (c) Mahmoud Shaheen. All rights reserved.

using NJsonSchema;

namespace Headless.OpenApi.Nswag.SchemaProcessors;

/// <summary>
/// Extension methods on NJsonSchema's <c>JsonSchema</c> for OpenAPI nullable-to-required normalization.
/// </summary>
[PublicAPI]
public static class SchemaProcessorContextExtensions
{
    /// <summary>
    /// Adds every non-nullable property of <paramref name="schema"/> to its <c>required</c> set, aligning
    /// the OpenAPI 3.x <c>required</c> array with C# non-nullable reference type semantics.
    /// </summary>
    /// <param name="schema">The object schema to normalise. Non-object schemas are returned unchanged.</param>
    /// <returns>The same <paramref name="schema"/> instance for chaining.</returns>
    public static JsonSchema NormalizeNullableAsRequired(this JsonSchema schema)
    {
        if (!schema.IsObject || schema.Properties.Count == 0)
        {
            return schema;
        }

        foreach (var (name, property) in schema.Properties)
        {
            if (
                !property.IsNullable(SchemaType.OpenApi3)
                && !schema.RequiredProperties.Contains(name, StringComparer.Ordinal)
            )
            {
                schema.RequiredProperties.Add(name);
            }
        }

        return schema;
    }
}
