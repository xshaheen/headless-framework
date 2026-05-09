// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Api.Middlewares;

/// <summary>Adds a no-cache response header when the application did not explicitly set one.</summary>
internal sealed class NoCacheHeadersMiddleware(RequestDelegate next)
{
    /// <summary>Processes the current request.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            if (context.Response.Headers.CacheControl.Count is 0)
            {
                context.Response.Headers.CacheControl = "no-cache,no-store,must-revalidate";
            }

            return Task.CompletedTask;
        });

        await next(context).ConfigureAwait(false);
    }
}
