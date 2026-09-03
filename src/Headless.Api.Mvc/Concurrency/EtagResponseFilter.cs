// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace Headless.Api.Concurrency;

internal sealed class EtagResponseFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (
            context.Result is ObjectResult { Value: IHasETag { ETag: { Length: > 0 } etag } } result
            && (result.StatusCode is null or (>= 200 and < 300))
            && !context.HttpContext.Response.Headers.ContainsKey(HeaderNames.ETag)
        )
        {
            context.HttpContext.Response.Headers.ETag = EntityTagCodec.Format(etag);
        }

        _ = await next().ConfigureAwait(false);
    }
}
