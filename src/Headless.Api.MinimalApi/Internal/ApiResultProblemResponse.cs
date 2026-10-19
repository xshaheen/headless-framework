// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

/// <summary>Executes and documents the problem-details responses of the <see cref="ApiResult"/> result types.</summary>
internal static class ApiResultProblemResponse
{
    /// <summary>
    /// The statuses an <see cref="ApiResult"/> result can answer with (the shared result contract plus the 400 a
    /// dispatched request answers when its body is missing), and 429, which the rate limiter answers before any
    /// handler runs.
    /// </summary>
    public static IReadOnlyList<int> StandardProblemStatusCodes { get; } =
    [
        StatusCodes.Status400BadRequest,
        StatusCodes.Status401Unauthorized,
        StatusCodes.Status403Forbidden,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status422UnprocessableEntity,
        StatusCodes.Status429TooManyRequests,
    ];

    public static int GetStatusCode(ApiResultError error)
    {
        return error is MalformedRequestError
            ? StatusCodes.Status400BadRequest
            : ApiResultErrorProblemDetails.GetStatusCode(error);
    }

    public static Task ExecuteAsync(HttpContext httpContext, ApiResultError error)
    {
        var creator = httpContext.RequestServices.GetRequiredService<IProblemDetailsCreator>();

        var problemDetails =
            error is MalformedRequestError ? creator.BadRequest() : ApiResultErrorProblemDetails.Create(error, creator);

        return TypedResults.Problem(problemDetails).ExecuteAsync(httpContext);
    }

    public static void PopulateMetadata(EndpointBuilder builder, int successStatusCode, Type? successType)
    {
        ApiResultResponseMetadata.Populate(builder, successStatusCode, successType);

        // Only Minimal API dispatch turns a missing body into a result, so the 400 sits outside the shared contract.
        AddProblemIfMissing(builder, StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Documents a problem-details response for <paramref name="statusCode"/> unless the endpoint already documents
    /// that status, so a convention never lists one status twice or overrides what the handler's result declared.
    /// </summary>
    public static void AddProblemIfMissing(EndpointBuilder builder, int statusCode)
    {
        foreach (var item in builder.Metadata)
        {
            if (item is IProducesResponseTypeMetadata produces && produces.StatusCode == statusCode)
            {
                return;
            }
        }

        builder.Metadata.Add(_Problem(statusCode));
    }

    private static ProducesResponseTypeMetadata _Problem(int statusCode)
    {
        return new ProducesResponseTypeMetadata(statusCode, typeof(ProblemDetails), ["application/problem+json"]);
    }
}
