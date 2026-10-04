// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Caching.Distributed;

namespace Headless.Caching;

internal static class DistributedCacheEntryOptionsMapper
{
    public static CacheEntryOptions Map(
        DistributedCacheEntryOptions options,
        TimeSpan defaultAbsoluteExpiration,
        TimeProvider timeProvider
    )
    {
        Argument.IsNotNull(options);
        Argument.IsPositive(defaultAbsoluteExpiration);
        Argument.IsNotNull(timeProvider);

        var duration = _ResolveDuration(options, defaultAbsoluteExpiration, timeProvider);
        var sliding = options.SlidingExpiration;

        if (sliding.HasValue)
        {
            Argument.IsPositive(sliding.Value, nameof(options.SlidingExpiration));
        }

        return new CacheEntryOptions { Duration = duration, SlidingExpiration = sliding };
    }

    private static TimeSpan _ResolveDuration(
        DistributedCacheEntryOptions options,
        TimeSpan defaultAbsoluteExpiration,
        TimeProvider timeProvider
    )
    {
        if (options.AbsoluteExpirationRelativeToNow is { } relative)
        {
            return Argument.IsPositive(relative, nameof(options.AbsoluteExpirationRelativeToNow));
        }

        if (options.AbsoluteExpiration is { } absolute)
        {
            // A past absolute timestamp produces a non-positive duration. Pass it through so the engine expires the
            // entry immediately rather than throwing an exception.
            return absolute - timeProvider.GetUtcNow();
        }

        return defaultAbsoluteExpiration;
    }
}
