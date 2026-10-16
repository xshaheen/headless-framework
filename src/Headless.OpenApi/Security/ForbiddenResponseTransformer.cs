// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Adds a 403 response to every operation whose authorization metadata can refuse an authenticated user: a named
/// policy or roles, or a claims, name, operation, roles, or assertion requirement.
/// </summary>
internal sealed class ForbiddenResponseTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (EndpointAuthorization.CanForbid(context.Description))
        {
            (operation.Responses ??= []).TryAdd(
                OpenApiStatusCodes.Forbidden,
                new OpenApiResponse
                {
                    Description =
                        "Forbidden - The user does not have the necessary permissions to access the resource.",
                }
            );
        }

        return Task.CompletedTask;
    }
}
