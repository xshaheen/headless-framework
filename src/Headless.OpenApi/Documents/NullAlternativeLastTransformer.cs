// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Lists the <see langword="null"/> alternative of a <c>oneOf</c> or <c>anyOf</c> last, so a nullable reference reads
/// <c>oneOf: [{ $ref }, { type: null }]</c>, and lifts the property comment from that reference onto the
/// <c>oneOf</c> itself.
/// </summary>
/// <remarks>
/// The native generator writes the null alternative first, and client generators keep the order they read, so every
/// nullable object member came out as <c>null | Address</c>. The order means nothing to a validator; it only changes
/// what a person reads in the document and in generated code. The generator also leaves a nullable property's comment
/// on the reference inside the <c>oneOf</c>, where client generators do not look for a property's description.
/// </remarks>
internal sealed class NullAlternativeLastTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        var visited = new HashSet<IOpenApiSchema>(ReferenceEqualityComparer.Instance);

        foreach (var schema in document.Components?.Schemas?.Values ?? [])
        {
            _Visit(schema, visited);
        }

        var operations = document
            .Paths?.Values.Where(x => x.Operations is not null)
            .SelectMany(x => x.Operations!.Values);

        foreach (var operation in operations ?? [])
        {
            foreach (var parameter in operation.Parameters ?? [])
            {
                _Visit(parameter.Schema, visited);
            }

            foreach (var media in operation.RequestBody?.Content?.Values ?? [])
            {
                _Visit(media.Schema, visited);
            }

            foreach (var response in operation.Responses?.Values.AsEnumerable() ?? [])
            {
                foreach (var media in response.Content?.Values ?? [])
                {
                    _Visit(media.Schema, visited);
                }
            }
        }

        return Task.CompletedTask;
    }

    private static void _Visit(IOpenApiSchema? node, HashSet<IOpenApiSchema> visited)
    {
        if (node is not OpenApiSchema schema || !visited.Add(schema))
        {
            return;
        }

        _MoveNullLast(schema.OneOf);
        _MoveNullLast(schema.AnyOf);
        _LiftReferenceDescription(schema, schema.OneOf);
        _LiftReferenceDescription(schema, schema.AnyOf);

        foreach (var child in (schema.OneOf ?? []).Concat(schema.AnyOf ?? []).Concat(schema.AllOf ?? []))
        {
            _Visit(child, visited);
        }

        foreach (var property in schema.Properties?.Values ?? [])
        {
            _Visit(property, visited);
        }

        _Visit(schema.Items, visited);
        _Visit(schema.AdditionalProperties, visited);
    }

    private static void _LiftReferenceDescription(OpenApiSchema schema, IList<IOpenApiSchema>? alternatives)
    {
        if (
            !string.IsNullOrEmpty(schema.Description)
            || alternatives is not { Count: 2 }
            || alternatives[1] is not OpenApiSchema { Type: JsonSchemaType.Null }
            || alternatives[0] is not OpenApiSchemaReference { Description: { Length: > 0 } description } reference
        )
        {
            return;
        }

        schema.Description = description;
        reference.Description = null;
    }

    private static void _MoveNullLast(IList<IOpenApiSchema>? alternatives)
    {
        if (alternatives is not { Count: > 1 })
        {
            return;
        }

        for (var i = 0; i < alternatives.Count - 1; i++)
        {
            if (alternatives[i] is OpenApiSchema { Type: JsonSchemaType.Null } nullAlternative)
            {
                alternatives.RemoveAt(i);
                alternatives.Add(nullAlternative);

                return;
            }
        }
    }
}
