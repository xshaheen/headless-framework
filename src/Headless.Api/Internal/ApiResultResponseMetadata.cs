// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

namespace Headless.Api;

/// <summary>
/// The success response an <see cref="Headless.Primitives.ApiResult"/> endpoint publishes, shared by the Minimal API and
/// MVC result types.
/// </summary>
/// <remarks>
/// Error responses are not published here: Headless.OpenApi documents the problem responses on every operation, so an
/// endpoint that returns <c>ApiResult</c> and one that returns a plain value and throws document the same errors.
/// </remarks>
internal static class ApiResultResponseMetadata
{
    // A null successType means a response without a body.
    public static void Populate(EndpointBuilder builder, int successStatusCode, Type? successType)
    {
        // A bodyless response is typeof(void), as the native NoContent result declares it: MVC's API Explorer drops a
        // response type whose Type is null.
        builder.Metadata.Add(
            new ProducesResponseTypeMetadata(
                successStatusCode,
                successType ?? typeof(void),
                successType is null ? [] : ["application/json"]
            )
        );
    }
}
