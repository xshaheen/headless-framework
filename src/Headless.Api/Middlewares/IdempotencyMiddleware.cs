// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Api.Resources;
using Headless.Caching;
using Headless.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Api.Middlewares;

public sealed class IdempotencyMiddleware(
    ICache cache,
    IOptionsSnapshot<IdempotencyMiddlewareOptions> optionsAccessor,
    ICancellationTokenProvider cancellationTokenProvider,
    IProblemDetailsCreator problemDetailsCreator,
    IClock clock,
    ILogger<IdempotencyMiddleware> logger
) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (
            !context.Request.Headers.TryGetValue(HttpHeaderNames.IdempotencyKey, out var value)
            || value.Count == 0
            || string.IsNullOrEmpty(value[^1])
        )
        {
            await next(context).AnyContext();
            return;
        }

        var idempotencyKey = value[^1]!;
        var cacheKey = "idempotency_key:" + idempotencyKey;

        var inserted = await cache
            .TryInsertAsync(
                key: cacheKey,
                value: clock.UtcNow,
                expiration: optionsAccessor.Value.IdempotencyKeyExpiration,
                cancellationToken: cancellationTokenProvider.Token
            )
            .AnyContext();

        if (inserted)
        {
            await next(context).AnyContext();
            return;
        }

        logger.LogWarning("Idempotency key {IdempotencyKey} already exists, returning 409 Conflict.", idempotencyKey);
        var problemDetails = problemDetailsCreator.Conflict(GeneralMessageDescriber.DuplicatedRequest());
        await Results.Problem(problemDetails).ExecuteAsync(context).AnyContext();
    }
}
