// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Caching;
using Framework.Constants;
using Microsoft.AspNetCore.Http;

namespace Framework.Api.Middlewares;

public sealed class IdempotencyMiddleware(ICache cache) : IMiddleware
{
    public Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Headers.TryGetValue(HttpHeaderNames.IdempotencyKey, out var value) || value.Count == 0)
        {
            return next(context);
        }
    }
}
