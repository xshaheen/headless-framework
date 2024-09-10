// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.DistributedLocks;

[PublicAPI]
public static class DistributedLockProviderExtensions
{
    public static Task ReleaseAsync(this IDistributedLockProvider provider, IDistributedLock @lock)
    {
        return provider.ReleaseAsync(@lock.Resource, @lock.LockId);
    }

    public static Task RenewAsync(
        this IDistributedLockProvider provider,
        IDistributedLock @lock,
        TimeSpan? timeUntilExpires = null
    )
    {
        return provider.RenewAsync(@lock.Resource, @lock.LockId, timeUntilExpires);
    }
}
