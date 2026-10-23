// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

/// <summary>Executes the problem-details responses of the <see cref="ApiResult"/> result types.</summary>
internal static class ApiResultProblemResponse
{
    // Before execution the request's services are out of reach, so an error the application may map reports no
    // status rather than a guessed 409.
    public static int? GetStatusCode(ApiResultError error)
    {
        return error is MalformedRequestError
            ? StatusCodes.Status400BadRequest
            : ApiResultErrorProblemDetails.GetStatusCode(error, mappings: null);
    }

    public static Task ExecuteAsync(HttpContext httpContext, ApiResultError error)
    {
        var creator = httpContext.RequestServices.GetRequiredService<IProblemDetailsCreator>();

        var problemDetails =
            error is MalformedRequestError
                ? creator.BadRequest()
                : ApiResultErrorProblemDetails.Create(
                    error,
                    creator,
                    ApiResultErrorProblemDetails.GetMappings(httpContext.RequestServices)
                );

        return TypedResults.Problem(problemDetails).ExecuteAsync(httpContext);
    }
}
