// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Lists the <see langword="null"/> alternative of a <c>oneOf</c> or <c>anyOf</c> last, so a nullable reference reads
/// <c>oneOf: [{ $ref }, { type: null }]</c>.
/// </summary>
/// <remarks>
/// The native generator writes the null alternative first, and client generators keep the order they read, so every
/// nullable object member came out as <c>null | Address</c>. The order means nothing to a validator; it only changes
/// what a person reads in the document and in generated code.
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
