namespace Tests.Lock;

public interface ILockProvider
{
    Task<ILock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        bool releaseOnDispose = true,
        CancellationToken acquireAbortToken = default
    );

    Task<bool> IsLockedAsync(string resource);

    Task ReleaseAsync(string resource, string lockId);

    Task RenewAsync(string resource, string lockId, TimeSpan? timeUntilExpires = null);
}

public static class LockProviderExtensions
{
    public static Task ReleaseAsync(this ILockProvider provider, ILock @lock)
    {
        return provider.ReleaseAsync(@lock.Resource, @lock.LockId);
    }

    public static Task RenewAsync(this ILockProvider provider, ILock @lock, TimeSpan? timeUntilExpires = null)
    {
        return provider.RenewAsync(@lock.Resource, @lock.LockId, timeUntilExpires);
    }

    public static Task<ILock?> TryAcquireAsync(
        this ILockProvider provider,
        string resource,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    )
    {
        return provider.TryAcquireAsync(resource, timeUntilExpires, releaseOnDispose: true, cancellationToken);
    }

    public static async Task<ILock?> TryAcquireAsync(
        this ILockProvider provider,
        string resource,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null
    )
    {
        using var cancellationTokenSource = acquireTimeout.ToCancellationTokenSource(TimeSpan.FromSeconds(30));

        return await provider
            .TryAcquireAsync(resource, timeUntilExpires, true, cancellationTokenSource.Token)
            .AnyContext();
    }

    public static async Task<bool> TryUsingAsync(
        this ILockProvider locker,
        string resource,
        Func<CancellationToken, Task> work,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    )
    {
        var l = await locker.TryAcquireAsync(resource, timeUntilExpires, true, cancellationToken).AnyContext();

        if (l == null)
        {
            return false;
        }

        try
        {
            await work(cancellationToken).AnyContext();
        }
        finally
        {
            await l.ReleaseAsync().AnyContext();
        }

        return true;
    }

    public static async Task<bool> TryUsingAsync(
        this ILockProvider locker,
        string resource,
        Func<Task> work,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    )
    {
        var l = await locker.TryAcquireAsync(resource, timeUntilExpires, true, cancellationToken).AnyContext();

        if (l == null)
        {
            return false;
        }

        try
        {
            await work().AnyContext();
        }
        finally
        {
            await l.ReleaseAsync().AnyContext();
        }

        return true;
    }

    public static async Task<bool> TryUsingAsync(
        this ILockProvider locker,
        string resource,
        Func<CancellationToken, Task> work,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null
    )
    {
        using var cancellationTokenSource = acquireTimeout.ToCancellationTokenSource();
        var l = await locker
            .TryAcquireAsync(resource, timeUntilExpires, true, cancellationTokenSource.Token)
            .AnyContext();

        if (l == null)
        {
            return false;
        }

        try
        {
            await work(cancellationTokenSource.Token).AnyContext();
        }
        finally
        {
            await l.ReleaseAsync().AnyContext();
        }

        return true;
    }

    public static async Task<bool> TryUsingAsync(
        this ILockProvider locker,
        string resource,
        Func<Task> work,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null
    )
    {
        using var cancellationTokenSource = acquireTimeout.ToCancellationTokenSource();
        var l = await locker
            .TryAcquireAsync(resource, timeUntilExpires, true, cancellationTokenSource.Token)
            .AnyContext();

        if (l == null)
        {
            return false;
        }

        try
        {
            await work().AnyContext();
        }
        finally
        {
            await l.ReleaseAsync().AnyContext();
        }

        return true;
    }

    public static Task<bool> TryUsingAsync(
        this ILockProvider locker,
        string resource,
        Action work,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null
    )
    {
        return locker.TryUsingAsync(
            resource,
            () =>
            {
                work();

                return Task.CompletedTask;
            },
            timeUntilExpires,
            acquireTimeout
        );
    }
}
