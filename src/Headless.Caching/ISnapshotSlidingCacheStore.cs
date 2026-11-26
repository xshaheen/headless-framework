// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>Re-arms the sliding deadline of the snapshot read by the caller while it still matches the store.</summary>
internal interface ISnapshotSlidingCacheStore
{
    ValueTask TryRearmSlidingIfCurrentAsync(
        string key,
        string concurrencyStamp,
        TimeSpan slidingExpiration,
        DateTime physicalExpiresAt,
        DateTime now,
        CancellationToken cancellationToken
    );
}
