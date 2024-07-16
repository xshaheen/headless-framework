using Framework.Api.Core.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Framework.Api.Core.Middlewares;

public sealed class StatusCodesRewriterMiddleware(IProblemDetailsCreator problemDetailsCreator) : IMiddleware
{
    /// <summary>Executes the middleware.</summary>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        await next(context);

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

        if (context.Response.StatusCode is StatusCodes.Status404NotFound)
        {
            var endpointNotFound = problemDetailsCreator.EndpointNotFound(context);
            await Results.Problem(endpointNotFound).ExecuteAsync(context);
        }
    }
}
