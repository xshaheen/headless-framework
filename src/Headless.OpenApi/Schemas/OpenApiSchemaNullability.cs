// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>Reads and removes the ways a schema under construction admits <see langword="null"/>.</summary>
/// <remarks>
/// Schema transformers see a schema before the native generator moves named types into components. At that point a
/// nullable property of a named type carries no <see langword="null"/> in its type: the generator marks it with
/// <see cref="NullablePropertyKey"/> metadata and only later wraps the reference in <c>oneOf: [null, $ref]</c>.
/// </remarks>
internal static class OpenApiSchemaNullability
{
    // Both keys are written by Microsoft.AspNetCore.OpenApi's schema service (JsonNodeSchemaExtensions
    // .ApplyNullabilityContextInfo and .ApplySchemaReferenceId) and read back by its OpenApiSchemaService
    // .ResolveReferenceForSchema; they are not public API, so a package update that renames them breaks the tests here.

    /// <summary>The metadata key the native generator sets on a nullable property of a component type.</summary>
    public const string NullablePropertyKey = "x-is-nullable-property";

    /// <summary>The metadata key carrying the component id the native generator gives a named type.</summary>
    public const string SchemaIdKey = "x-schema-id";

    /// <summary>
    /// Whether the schema admits <see langword="null"/>: through its type, a <see langword="null"/> alternative, or the nullable-property
    /// marker.
    /// </summary>
    public static bool IsNullable(IOpenApiSchema schema)
    {
        if (schema is not OpenApiSchema concrete)
        {
            return false;
        }

        return (concrete.Type is { } type && type.HasFlag(JsonSchemaType.Null))
            || concrete.OneOf?.Any(_IsNullType) == true
            || concrete.AnyOf?.Any(_IsNullType) == true
            || IsMarkedNullableProperty(concrete);
    }

    /// <summary>Whether the native generator marked the schema as a nullable property of a component type.</summary>
    public static bool IsMarkedNullableProperty(OpenApiSchema schema)
    {
        return schema.Metadata?.TryGetValue(NullablePropertyKey, out var value) == true && value is true;
    }

    /// <summary>
    /// Removes <see langword="null"/> from the schema's type, alternatives, and nullable-property marker. A schema left with a
    /// single alternative collapses to it, so the result describes the value directly.
    /// </summary>
    public static IOpenApiSchema RemoveNullability(IOpenApiSchema schema)
    {
        if (schema is not OpenApiSchema concrete)
        {
            return schema;
        }

        if (concrete.Type is { } type)
        {
            concrete.Type = type & ~JsonSchemaType.Null;
        }

        concrete.Metadata?.Remove(NullablePropertyKey);
        concrete.OneOf = _WithoutNull(concrete.OneOf);
        concrete.AnyOf = _WithoutNull(concrete.AnyOf);

        if (
            concrete.Type is null
            && concrete.Properties is null
            && concrete.OneOf is [var single]
            && concrete.AnyOf is null
        )
        {
            return single;
        }

        return concrete;
    }

    private static List<IOpenApiSchema>? _WithoutNull(IList<IOpenApiSchema>? alternatives)
    {
        if (alternatives is null)
        {
            return null;
        }

        var kept = alternatives.Where(alternative => !_IsNullType(alternative)).ToList();

        return kept.Count == 0 ? null : kept;
    }

    private static bool _IsNullType(IOpenApiSchema schema) => schema is OpenApiSchema { Type: JsonSchemaType.Null };
}
