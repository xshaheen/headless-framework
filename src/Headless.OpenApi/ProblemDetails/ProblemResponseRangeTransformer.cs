// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Adds the <c>4XX</c> and <c>5XX</c> range responses to every operation, typed with
/// <see cref="HeadlessProblemDetails"/>.
/// </summary>
/// <remarks>
/// Every error the framework writes is a problem-details body, so a range response is always true. It covers the
/// statuses no rule can predict for an operation, such as a timeout, an unavailable dependency, or an application's own
/// mapped exception: a client generator that falls back from an exact status to its range still deserializes the body
/// instead of raising an untyped error. Registered last, so an operation's exact statuses are already in place and keep
/// their specific schemas; an operation that declares a range itself keeps its own.
/// </remarks>
internal sealed class ProblemResponseRangeTransformer : IOpenApiOperationTransformer
{
    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        var responses = operation.Responses ??= [];

        await _AddAsync(
            responses,
            OpenApiStatusCodes.ClientErrorRange,
            "Client Error - The request failed with a status this operation does not list.",
            context,
            cancellationToken
        );
        await _AddAsync(
            responses,
            OpenApiStatusCodes.ServerErrorRange,
            "Server Error - The server failed with a status this operation does not list.",
            context,
            cancellationToken
        );
    }

    private static Task _AddAsync(
        OpenApiResponses responses,
        string range,
        string description,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        var response = new OpenApiResponse { Description = description };

        return responses.TryAdd(range, response)
            ? ProblemDetailsTransformer.DescribeAsync(range, response, context, cancellationToken)
            : Task.CompletedTask;
    }
}
