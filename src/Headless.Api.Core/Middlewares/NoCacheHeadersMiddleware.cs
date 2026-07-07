// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Api.Middlewares;

/// <summary>Adds a no-cache response header when the application did not explicitly set one.</summary>
internal sealed class NoCacheHeadersMiddleware(RequestDelegate next)
{
    /// <summary>Processes the current request.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // State-passing overload: a plain lambda would capture `context` and allocate a closure per request.
        context.Response.OnStarting(
            static state =>
            {
                var httpContext = (HttpContext)state;

                if (httpContext.Response.Headers.CacheControl.Count is 0)
                {
                    httpContext.Response.Headers.CacheControl = "no-cache,no-store,must-revalidate";
                }

                return Task.CompletedTask;
            },
            context
        );

        await next(context).ConfigureAwait(false);
    }
}
