// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.ResourceLocks;

[PublicAPI]
public static class ResourceLockProviderExtensions
{
    public static Task ReleaseAsync(this IResourceLockProvider provider, IResourceLock resourceLock)
    {
        return provider.ReleaseAsync(resourceLock.Resource, resourceLock.LockId);
    }

    public static Task RenewAsync(
        this IResourceLockProvider provider,
        IResourceLock resourceLock,
        TimeSpan? timeUntilExpires = null
    )
    {
        return provider.RenewAsync(resourceLock.Resource, resourceLock.LockId, timeUntilExpires);
    }

    public static async Task<bool> TryUsingAsync(
        this IResourceLockProvider provider,
        string resource,
        Func<Task> work,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        var resourceLock = await provider
            .TryAcquireAsync(resource, timeUntilExpires, acquireTimeout, cancellationToken)
            .AnyContext();

        if (resourceLock is null)
        {
            return false;
        }

        try
        {
            await work().AnyContext();

            return true;
        }
        finally
        {
            await resourceLock.ReleaseAsync();
        }
    }

    public static async Task<bool> TryUsingAsync<TState>(
        this IResourceLockProvider provider,
        string resource,
        TState state,
        Func<TState, Task> work,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        var resourceLock = await provider
            .TryAcquireAsync(resource, timeUntilExpires, acquireTimeout, cancellationToken)
            .AnyContext();

        if (resourceLock is null)
        {
            return false;
        }

        try
        {
            await work(state).AnyContext();

            return true;
        }
        finally
        {
            await resourceLock.ReleaseAsync();
        }
    }

    public static async Task<bool> TryUsingAsync(
        this IResourceLockProvider provider,
        string resource,
        Func<CancellationToken, Task> work,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        var resourceLock = await provider
            .TryAcquireAsync(resource, timeUntilExpires, acquireTimeout, cancellationToken)
            .AnyContext();

        if (resourceLock is null)
        {
            return false;
        }

        try
        {
            await work(cancellationToken).AnyContext();

            return true;
        }
        finally
        {
            await resourceLock.ReleaseAsync();
        }
    }

    public static async Task<bool> TryUsingAsync<TState>(
        this IResourceLockProvider provider,
        string resource,
        TState state,
        Func<TState, CancellationToken, Task> work,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        var resourceLock = await provider
            .TryAcquireAsync(resource, timeUntilExpires, acquireTimeout, cancellationToken)
            .AnyContext();

        if (resourceLock is null)
        {
            return false;
        }

        try
        {
            await work(state, cancellationToken).AnyContext();

            return true;
        }
        finally
        {
            await resourceLock.ReleaseAsync();
        }
    }

    public static async Task<bool> TryUsingAsync(
        this IResourceLockProvider provider,
        string resource,
        Action work,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        var resourceLock = await provider
            .TryAcquireAsync(resource, timeUntilExpires, acquireTimeout, cancellationToken)
            .AnyContext();

        if (resourceLock is null)
        {
            return false;
        }

        try
        {
            work();

            return true;
        }
        finally
        {
            await resourceLock.ReleaseAsync();
        }
    }

    public static async Task<bool> TryUsingAsync<TState>(
        this IResourceLockProvider provider,
        string resource,
        TState state,
        Action<TState> work,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        var resourceLock = await provider
            .TryAcquireAsync(resource, timeUntilExpires, acquireTimeout, cancellationToken)
            .AnyContext();

        if (resourceLock is null)
        {
            return false;
        }

        try
        {
            work(state);

            return true;
        }
        finally
        {
            await resourceLock.ReleaseAsync();
        }
    }
}
