// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace Headless.Api.Concurrency;

internal sealed class EntityTagResponseFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (
            context.Result is ObjectResult { Value: IHasEntityTag { EntityTag: { } entityTag } } result
            && (result.StatusCode is null or (>= 200 and < 300))
            && !context.HttpContext.Response.Headers.ContainsKey(HeaderNames.ETag)
        )
        {
            context.HttpContext.Response.Headers.ETag = entityTag.HeaderValue;
        }

        _ = await next().ConfigureAwait(false);
    }
}
