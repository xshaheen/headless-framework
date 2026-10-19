// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>Publishes query parameter names in camelCase, matching the JSON property names of the same models.</summary>
/// <remarks>
/// The native generator keeps the CLR property name of an <c>[AsParameters]</c> or <c>[FromQuery]</c> complex type
/// (<c>PageIndex</c>). Query binding is case-insensitive, so the camelCase name binds the same way. Names starting with
/// <c>$</c>, such as OData's <c>$filter</c>, keep their spelling.
/// </remarks>
internal sealed class CamelCaseQueryParameterTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
        {
            if (parameter is { In: ParameterLocation.Query, Name: { Length: > 0 } name } && name[0] != '$')
            {
                parameter.Name = JsonNamingPolicy.CamelCase.ConvertName(name);
            }
        }

        return Task.CompletedTask;
    }
}
