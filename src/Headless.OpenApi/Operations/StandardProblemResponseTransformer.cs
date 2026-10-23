// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Adds the 404, 409, and 422 problem responses to every operation, the statuses the exception handler and the
/// <c>ApiResult</c> conversion write for not-found, conflict, and validation failures.
/// </summary>
/// <remarks>
/// <para>
/// Any handler or service can raise these, whether the endpoint returns <c>ApiResult</c> or a plain value and throws,
/// so endpoint metadata cannot tell which operations return them. Listing them everywhere over-states a few
/// operations rather than hiding a failure a client must handle. The responses start without content;
/// <see cref="ProblemDetailsTransformer"/> then describes their typed problem bodies. An operation that already
/// declares one of these statuses keeps its own response.
/// </para>
/// <para>
/// The innermost setting decides: a <see cref="StandardProblemResponsesAttribute"/> on the action, then on its
/// controller, then on the endpoint, its route group, or a <c>MapControllers()</c> convention, then the surface's
/// <see cref="ApiSurfaceOpenApiDescriptor.StandardProblemResponses"/>, then
/// <see cref="HeadlessOpenApiOptions.StandardProblemResponses"/>.
/// </para>
/// </remarks>
internal sealed class StandardProblemResponseTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (!_IsEnabled(context))
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

        // The statuses the application mapped its own exceptions and errors to can come from any handler or
        // service too, so they join the baseline and follow the same control.
        var mappings = context.ApplicationServices.GetService<IOptions<HeadlessProblemDetailsOptions>>()?.Value;

        foreach (var statusCode in mappings?.MappedStatusCodes.Order() ?? Enumerable.Empty<int>())
        {
            responses.TryAdd(
                statusCode.ToString(CultureInfo.InvariantCulture),
                new OpenApiResponse
                {
                    Description =
                        $"{ReasonPhrases.GetReasonPhrase(statusCode)} - A failure the application maps to this status.",
                }
            );
        }

        return Task.CompletedTask;
    }

    private static bool _IsEnabled(OpenApiOperationTransformerContext context)
    {
        var metadata = context.Description.GetEndpointMetadata();

        // MVC adds MapControllers() conventions after the action's own attributes, so a plain last-wins read of the
        // endpoint metadata would let a convention override an action. The action descriptor holds only the controller's
        // and the action's attributes, in that order, so it is read first; for a Minimal API endpoint it already holds the
        // whole endpoint metadata, groups before the endpoint.
        var marker =
            context
                .Description.ActionDescriptor.EndpointMetadata.OfType<StandardProblemResponsesAttribute>()
                .LastOrDefault()
            ?? metadata.OfType<StandardProblemResponsesAttribute>().LastOrDefault();

        if (marker is not null)
        {
            return marker.Enabled;
        }

        return metadata.OfType<ApiSurfaceDescriptor>().LastOrDefault()?.OpenApi.StandardProblemResponses
            ?? context
                .ApplicationServices.GetRequiredService<IOptions<HeadlessOpenApiOptions>>()
                .Value.StandardProblemResponses;
    }
}
