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
            var duration = absolute - timeProvider.GetUtcNow();

            return Argument.IsPositive(duration, nameof(options.AbsoluteExpiration));
        }

        return defaultAbsoluteExpiration;
    }
}
