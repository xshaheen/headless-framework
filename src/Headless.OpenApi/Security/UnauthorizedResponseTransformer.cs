// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>Adds a 401 response to every operation that requires an authenticated user.</summary>
/// <remarks>
/// The response starts without content; <see cref="ProblemDetailsTransformer"/> then describes its typed problem
/// body. An operation that already declares 401 keeps its own.
/// </remarks>
internal sealed class UnauthorizedResponseTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (EndpointAuthorization.RequiresAuthentication(context.Description))
        {
            (operation.Responses ??= []).TryAdd(
                OpenApiStatusCodes.Unauthorized,
                new OpenApiResponse
                {
                    Description =
                        "Unauthorized - The user has not supplied the necessary credentials to access the resource.",
                }
            );
        }

        return Task.CompletedTask;
    }
}
