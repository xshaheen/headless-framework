// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Adds the 404, 409, and 422 problem responses to every operation, the statuses the exception handler and the
/// <c>ApiResult</c> conversion write for not-found, conflict, and validation failures.
/// </summary>
/// <remarks>
/// Any handler or service can raise these, whether the endpoint returns <c>ApiResult</c> or a plain value and throws,
/// so endpoint metadata cannot tell which operations return them. Listing them everywhere over-states a few
/// operations rather than hiding a failure a client must handle. <see cref="HeadlessOpenApiOptions.StandardProblemResponses"/>
/// turns them off. The responses start without content; <see cref="ProblemDetailsTransformer"/> then describes their
/// typed problem bodies. An operation that already declares one of these statuses keeps its own response.
/// </remarks>
internal sealed class StandardProblemResponseTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        var options = context.ApplicationServices.GetRequiredService<IOptions<HeadlessOpenApiOptions>>().Value;

        if (!options.StandardProblemResponses)
        {
            return Task.CompletedTask;
        }

        var responses = operation.Responses ??= [];

        responses.TryAdd(
            OpenApiStatusCodes.NotFound,
            new OpenApiResponse { Description = "Not Found - The requested resource does not exist." }
        );
        responses.TryAdd(
            OpenApiStatusCodes.Conflict,
            new OpenApiResponse
            {
                Description = "Conflict - The request conflicts with the current state of the resource.",
            }
        );
        responses.TryAdd(
            OpenApiStatusCodes.UnprocessableEntity,
            new OpenApiResponse { Description = "Unprocessable Entity - The request failed validation." }
        );

        return Task.CompletedTask;
    }
}
