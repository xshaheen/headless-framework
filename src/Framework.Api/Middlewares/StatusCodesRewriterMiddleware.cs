// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Framework.Api.Middlewares;

public sealed class StatusCodesRewriterMiddleware(IProblemDetailsCreator problemDetailsCreator) : IMiddleware
{
    /// <summary>Executes the middleware.</summary>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        await next(context).AnyContext();

        var isNonError = context.Response.StatusCode is < 400 or >= 600;

        if (
            isNonError // Ignore non-error status codes.
            || context.Response.HasStarted // Do nothing if a response body has already been provided.
            || context.Response.ContentLength.HasValue
            || !string.IsNullOrEmpty(context.Response.ContentType)
        )
        {
            return;
        }

        switch (context.Response.StatusCode)
        {
            case StatusCodes.Status401Unauthorized:
            {
                var problemDetails = problemDetailsCreator.Unauthorized();
                await Results.Problem(problemDetails).ExecuteAsync(context).AnyContext();

                break;
            }
            case StatusCodes.Status403Forbidden:
            {
                var problemDetails = problemDetailsCreator.Forbidden();
                await Results.Problem(problemDetails).ExecuteAsync(context).AnyContext();

                break;
            }
            case StatusCodes.Status404NotFound:
            {
                var problemDetails = problemDetailsCreator.EndpointNotFound();
                await Results.Problem(problemDetails).ExecuteAsync(context).AnyContext();

                break;
            }
        }
    }
}
