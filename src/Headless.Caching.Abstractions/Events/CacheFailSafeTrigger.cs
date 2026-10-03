// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>What triggered a fail-safe stale-serving activation. Mirrors the <c>headless.cache.trigger</c> metric dimension.</summary>
[PublicAPI]
public enum CacheFailSafeTrigger
{
    /// <summary>The factory threw a non-timeout exception.</summary>
    FactoryError,

    /// <summary>The factory hit a soft or hard timeout.</summary>
    FactoryTimeout,

    /// <summary>Acquiring the distributed factory lock failed.</summary>
    LockAcquireFailed,
}
