// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Api;

/// <summary>
/// Default predicate that decides whether a completed response should be cached for replay.
/// Caches all 2xx and a conservative subset of 4xx; excludes transient 4xx (408/425/429),
/// all 5xx, 1xx, and 3xx.
/// </summary>
internal static class DefaultCachePredicate
{
    private static readonly HashSet<int> _Cacheable4Xx =
    [
        400,
        401,
        403,
        404,
        405,
        409,
        410,
        411,
        412,
        413,
        414,
        415,
        416,
        422,
        451,
    ];

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
