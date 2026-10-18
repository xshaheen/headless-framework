// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Headless.Api;

/// <summary>
/// The one response contract an <see cref="Headless.Primitives.ApiResult"/> endpoint publishes, shared by the Minimal
/// API and MVC result types, so the two documents never disagree on which responses such an endpoint can return.
/// </summary>
/// <remarks>
/// The success response, then a problem-details response for each status <see cref="ApiResultErrorProblemDetails"/>
/// can map an error to: 401, 403, 404, 409, and 422.
/// </remarks>
internal static class ApiResultResponseMetadata
{
    private static readonly int[] _ProblemStatusCodes =
    [
        StatusCodes.Status401Unauthorized,
        StatusCodes.Status403Forbidden,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status422UnprocessableEntity,
    ];

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

        foreach (var statusCode in _ProblemStatusCodes)
        {
            builder.Metadata.Add(
                new ProducesResponseTypeMetadata(statusCode, typeof(ProblemDetails), ["application/problem+json"])
            );
        }
    }
}
