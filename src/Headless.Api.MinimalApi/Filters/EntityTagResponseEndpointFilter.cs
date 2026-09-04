// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Headless.Api.Concurrency;

internal sealed class EntityTagResponseEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context).ConfigureAwait(false);
        var statusCode = (result as IStatusCodeHttpResult)?.StatusCode ?? context.HttpContext.Response.StatusCode;
        if (statusCode is < 200 or >= 300 || context.HttpContext.Response.Headers.ContainsKey(HeaderNames.ETag))
        {
            return result;
        }

        var entityTagged = result switch
        {
            IHasEntityTag direct => direct,
            IValueHttpResult { Value: IHasEntityTag value } => value,
            _ => null,
        };

        if (entityTagged is not null)
        {
            context.HttpContext.Response.Headers.ETag = entityTagged.GetEntityTag().HeaderValue;
        }

        return result;
    }
}
