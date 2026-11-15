// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.DistributedLocks.Redis;

internal static class RedisLeaseMilliseconds
{
    /// <summary>
    /// The lease length in whole milliseconds, rounded up. Truncating a positive lease under 1 ms to 0 makes Redis
    /// reject the <c>SET … PX 0</c> or expire the key at once, while the in-memory provider grants the lease.
    /// </summary>
    public static long From(TimeSpan lease)
    {
        var milliseconds = (long)Math.Ceiling(lease.TotalMilliseconds);

        return milliseconds < 1 ? 1 : milliseconds;
    }
}
