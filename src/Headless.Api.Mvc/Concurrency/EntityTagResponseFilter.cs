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
            context.Result is ObjectResult { Value: IHasEntityTag entityTagged } result
            && (result.StatusCode ?? context.HttpContext.Response.StatusCode) is >= 200 and < 300
            && !context.HttpContext.Response.Headers.ContainsKey(HeaderNames.ETag)
        )
        {
            context.HttpContext.Response.Headers.ETag = entityTagged.GetEntityTag().HeaderValue;
        }

        _ = await next().ConfigureAwait(false);
    }
}
