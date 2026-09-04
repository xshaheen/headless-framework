// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Headless.Api.Concurrency;

internal sealed class EntityTagResponseFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult result)
        {
            EntityTagResponseWriter.Register(context.HttpContext, result.Value as IHasEntityTag);
        }

        _ = await next().ConfigureAwait(false);
    }
}
