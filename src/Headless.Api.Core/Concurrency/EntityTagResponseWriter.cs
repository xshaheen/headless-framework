// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Headless.Api.Concurrency;

internal static class EntityTagResponseWriter
{
    public static void Register(HttpContext context, IHasEntityTag? entityTagged)
    {
        if (entityTagged is null)
        {
            return;
        }

        context.Response.OnStarting(
            static state =>
            {
                var (httpContext, tagged) = ((HttpContext Context, IHasEntityTag Tagged))state;
                if (
                    httpContext.Response.StatusCode
                        is >= StatusCodes.Status200OK
                            and < StatusCodes.Status300MultipleChoices
                    && !httpContext.Response.Headers.ContainsKey(HeaderNames.ETag)
                )
                {
                    httpContext.Response.Headers.ETag = tagged.GetEntityTag().HeaderValue;
                }

                return Task.CompletedTask;
            },
            (context, entityTagged)
        );
    }
}
