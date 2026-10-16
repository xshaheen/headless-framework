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
    // Every status an ApiResult result can answer with: a binding failure plus each ApiResultError kind.
    private static readonly int[] _ResultProblemStatusCodes =
    [
        StatusCodes.Status400BadRequest,
        StatusCodes.Status401Unauthorized,
        StatusCodes.Status403Forbidden,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status422UnprocessableEntity,
    ];

    /// <summary>The result statuses plus 429, which the rate limiter answers before any handler runs.</summary>
    public static IReadOnlyList<int> StandardProblemStatusCodes { get; } =
    [.. _ResultProblemStatusCodes, StatusCodes.Status429TooManyRequests];

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
        builder.Metadata.Add(
            new ProducesResponseTypeMetadata(
                successStatusCode,
                successType,
                successType is null ? [] : ["application/json"]
            )
        );

        foreach (var statusCode in _ResultProblemStatusCodes)
        {
            builder.Metadata.Add(_Problem(statusCode));
        }
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
