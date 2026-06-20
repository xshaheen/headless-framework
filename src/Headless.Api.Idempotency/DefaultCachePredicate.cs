// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Api;

/// <summary>
/// Default predicate that decides whether a completed response should be cached for replay.
/// Caches all 2xx and a conservative subset of 4xx; excludes transient 4xx (408/425/429),
/// authentication/authorization/not-found outcomes (401/403/404 — these are transient w.r.t.
/// idempotency: a client whose token expires gets a 401, refreshes, and retries the same
/// Idempotency-Key; caching the 401 would lock that client out for the full TTL), all 5xx,
/// 1xx, and 3xx.
/// </summary>
internal static class DefaultCachePredicate
{
    private static readonly HashSet<int> _Cacheable4Xx = [400, 405, 409, 410, 411, 412, 413, 414, 415, 416, 422, 451];

    public static readonly Func<HttpContext, bool> Instance = ctx =>
    {
        var status = ctx.Response.StatusCode;

        if (status is >= 200 and <= 299)
        {
            return true;
        }

        if (status is >= 400 and <= 499)
        {
            return _Cacheable4Xx.Contains(status);
        }

        return false;
    };
}
