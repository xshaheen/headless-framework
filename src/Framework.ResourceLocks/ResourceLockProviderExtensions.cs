// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.ResourceLocks;

[PublicAPI]
public static class ResourceLockProviderExtensions
{
    public static Task ReleaseAsync(this IResourceLockProvider provider, IResourceLock @lock)
    {
        return provider.ReleaseAsync(@lock.Resource, @lock.LockId);
    }

    public static Task RenewAsync(
        this IResourceLockProvider provider,
        IResourceLock @lock,
        TimeSpan? timeUntilExpires = null
    )
    {
        return provider.RenewAsync(@lock.Resource, @lock.LockId, timeUntilExpires);
    }
}
