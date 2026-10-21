// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Keeps operation ids unique within a document by numbering repeats (<c>Orders_Get</c>, <c>Orders_Get2</c>, ...).
/// </summary>
/// <remarks>
/// Two MVC overloads, or two endpoints sharing a name, produce the same id. A duplicate id makes generated clients
/// collide or drop an operation, so the second and later uses get the first free numeric suffix, in path order.
/// </remarks>
internal sealed class OperationIdDeduplicationTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (document.Paths is null)
        {
            return Task.CompletedTask;
        }

        var operations = document
            .Paths.Values.Where(path => path.Operations is not null)
            .SelectMany(path => path.Operations!.Values)
            .Where(operation => !string.IsNullOrEmpty(operation.OperationId))
            .ToList();
        var used = new HashSet<string>(operations.Select(operation => operation.OperationId!), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var operation in operations)
        {
            var id = operation.OperationId!;

            if (seen.Add(id))
            {
                continue;
            }

            var number = 2;

            while (used.Contains(id + number.ToString(CultureInfo.InvariantCulture)))
            {
                number++;
            }

            operation.OperationId = id + number.ToString(CultureInfo.InvariantCulture);
            used.Add(operation.OperationId);
            seen.Add(operation.OperationId);
        }

        return Task.CompletedTask;
    }
}
