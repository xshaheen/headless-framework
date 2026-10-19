// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Removes the native problem-details components that nothing references once <see cref="ProblemDetailsTransformer"/>
/// has replaced them with the typed Headless schemas.
/// </summary>
/// <remarks>
/// <c>ProducesProblem(...)</c> and <c>ProducesValidationProblem()</c> make the native generator publish
/// <c>ProblemDetails</c> and <c>HttpValidationProblemDetails</c> before any operation transformer runs. An unused
/// component would mislead a client generator into emitting a type no response uses. A component still referenced, for
/// example by a media type other than <c>application/problem+json</c>, stays.
/// </remarks>
internal sealed class ProblemDetailsComponentsTransformer : IOpenApiDocumentTransformer
{
    private static readonly string[] _NativeComponents =
    [
        nameof(Microsoft.AspNetCore.Mvc.ProblemDetails),
        nameof(Microsoft.AspNetCore.Http.HttpValidationProblemDetails),
        nameof(Microsoft.AspNetCore.Mvc.ValidationProblemDetails),
    ];

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (document.Components?.Schemas is not { Count: > 0 } schemas || !_NativeComponents.Any(schemas.ContainsKey))
        {
            return Task.CompletedTask;
        }

        var collector = new SchemaReferenceCollector();
        new OpenApiWalker(collector).Walk(document);

        foreach (var name in _NativeComponents)
        {
            if (!collector.Ids.Contains(name))
            {
                schemas.Remove(name);
            }
        }

        return Task.CompletedTask;
    }

    private sealed class SchemaReferenceCollector : OpenApiVisitorBase
    {
        public HashSet<string> Ids { get; } = new(StringComparer.Ordinal);

        public override void Visit(IOpenApiReferenceHolder referenceHolder)
        {
            if (referenceHolder is OpenApiSchemaReference { Reference.Id: { } id })
            {
                Ids.Add(id);
            }
        }
    }
}
