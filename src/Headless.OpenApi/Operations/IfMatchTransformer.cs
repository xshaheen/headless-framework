// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Documents the required <c>If-Match</c> header, and its 400, 409, and 428 responses, on endpoints marked
/// <see cref="RequireIfMatchAttribute"/>.
/// </summary>
internal sealed class IfMatchTransformer : IOpenApiOperationTransformer
{
    private const string _HeaderName = "If-Match";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (!context.Description.GetEndpointMetadata().OfType<RequireIfMatchAttribute>().Any())
        {
            return Task.CompletedTask;
        }

        var parameters = operation.Parameters ??= [];

        // An action that binds the header itself ([FromHeader(Name = "If-Match")]) already declares it, and a
        // repeated name and location makes the document invalid; the filter still rejects a request without it.
        var declared = parameters.FirstOrDefault(parameter =>
            parameter is { In: ParameterLocation.Header }
            && string.Equals(parameter.Name, _HeaderName, StringComparison.OrdinalIgnoreCase)
        );

        if (declared is null)
        {
            parameters.Add(_CreateHeaderParameter());
        }
        else if (declared is OpenApiParameter concrete)
        {
            concrete.Required = true;
        }

        var responses = operation.Responses ??= [];
        responses.TryAdd(
            OpenApiStatusCodes.BadRequest,
            new OpenApiResponse { Description = "The supplied If-Match value was invalid." }
        );
        // A tag that no longer matches the stored version fails the write as a concurrency conflict.
        responses.TryAdd(
            OpenApiStatusCodes.Conflict,
            new OpenApiResponse
            {
                Description = "The If-Match entity tag no longer matches the resource (g:concurrency_failure).",
            }
        );
        responses.TryAdd(
            OpenApiStatusCodes.PreconditionRequired,
            new OpenApiResponse { Description = "The required If-Match precondition was not supplied." }
        );

        return Task.CompletedTask;
    }

    private static OpenApiParameter _CreateHeaderParameter()
    {
        return new OpenApiParameter
        {
            Name = _HeaderName,
            In = ParameterLocation.Header,
            Required = true,
            Description = "One strong entity tag returned by the resource.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        };
    }
}
