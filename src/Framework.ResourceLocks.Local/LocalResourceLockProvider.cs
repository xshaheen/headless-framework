// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Collections.Concurrent;
using AsyncKeyedLock;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.ResourceLocks.Local;

[PublicAPI]
public sealed class LocalResourceLockProvider(
    IUniqueLongGenerator longGenerator,
    TimeProvider timeProvider,
    ILogger<LocalResourceLockProvider> logger,
    IOptions<ResourceLockOptions> optionsAccessor
) : IResourceLockProvider, IDisposable
{
    private readonly AsyncKeyedLocker<string> _locks = _CreateAsyncKeyedLocker();
    private readonly ConcurrentDictionary<string, ResourceLock> _resources = new(StringComparer.Ordinal);
    private readonly ResourceLockOptions _options = optionsAccessor.Value;

    public async Task<IResourceLock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null
    )
    {
        timeUntilExpires ??= TimeSpan.FromMinutes(20);
        acquireTimeout ??= TimeSpan.FromSeconds(30);

        Argument.IsNotNullOrWhiteSpace(resource);
        if (timeUntilExpires != Timeout.InfiniteTimeSpan)
        {
            Argument.IsPositive(timeUntilExpires.Value);
        }
        if (acquireTimeout != Timeout.InfiniteTimeSpan)
        {
            Argument.IsPositive(acquireTimeout.Value);
        }

        var normalizeResource = _options.KeyPrefix + resource;

        var timestamp = timeProvider.GetTimestamp();
        IDisposable lockReleaser;

        if (acquireTimeout == Timeout.InfiniteTimeSpan)
        {
            lockReleaser = await _locks.LockAsync(normalizeResource);
        }
        else
        {
            var timeoutRelease = await _locks.LockAsync(normalizeResource, acquireTimeout.Value);

            if (timeoutRelease.EnteredSemaphore)
            {
                timeoutRelease.Dispose();

                return null;
            }

            lockReleaser = timeoutRelease;
        }

        var elapsed = timeProvider.GetElapsedTime(timestamp);

        var lockId = longGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var resourceLock = new ResourceLock(lockId, lockReleaser, timeUntilExpires.Value);
        // Safe because the resource lock is acquired only by one thread
        var added = _resources.TryAdd(normalizeResource, resourceLock);

        if (!added)
        {
            logger.LogWarning(
                "(Shouldn't happen) The resource lock for the key '{Key}' was not added",
                normalizeResource
            );
            resourceLock.Dispose();

            return null;
        }

        // Expire the lock when the expiration token source is triggered
        resourceLock.ExpireSource.Token.Register(() =>
        {
            // TODO: this expiration can be triggered parallel with the renew of the same lock id
            // which can make the lock to be released even if it is renewed

            if (_resources.TryRemove(normalizeResource, out var value))
            {
                value.Dispose();
            }
        });

        return new DisposableResourceLock(resource, lockId, elapsed, this, logger, timeProvider);
    }

    public Task<bool> IsLockedAsync(string resource)
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        var normalizeResource = _options.KeyPrefix + resource;

        return Task.FromResult(_locks.IsInUse(normalizeResource));
    }

    public Task<bool> RenewAsync(string resource, string lockId, TimeSpan? timeUntilExpires = null)
    {
        timeUntilExpires ??= TimeSpan.FromMinutes(20);

        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);
        if (timeUntilExpires != Timeout.InfiniteTimeSpan)
        {
            Argument.IsPositive(timeUntilExpires.Value);
        }

        var normalizeResource = _options.KeyPrefix + resource;
        // If the lock is not found, then it is already released
        if (!_resources.TryGetValue(normalizeResource, out var value))
        {
            return Task.FromResult(false);
        }

        // If the lock id does not match, then it is not the lock that we are looking for
        if (!string.Equals(value.LockId, lockId, StringComparison.Ordinal))
        {
            return Task.FromResult(false);
        }

        if (value.ExpireSource.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        // Renew the lock
        value.ExpireSource.CancelAfter(value.TimeUntilExpires);

        return Task.FromResult(true);
    }

    public Task ReleaseAsync(string resource, string lockId)
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);

        var normalizeResource = _options.KeyPrefix + resource;
        // If the lock is not found, then it is already released
        if (!_resources.TryGetValue(normalizeResource, out var value))
        {
            return Task.CompletedTask;
        }

        // If the lock id does not match, then it is not the lock that we are looking for
        if (!string.Equals(value.LockId, lockId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        // Release the lock
        if (_resources.TryRemove(normalizeResource, out value))
        {
            value.Dispose();
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var value in _resources.Values)
        {
            value.Dispose();
        }

        _resources.Clear();
        _locks.Dispose();
    }

    #region Helpers

    private static AsyncKeyedLocker<string> _CreateAsyncKeyedLocker()
    {
        return new(
            options: o =>
            {
                o.MaxCount = 1;
                o.PoolSize = 20;
                o.PoolInitialFill = 1;
            },
            StringComparer.Ordinal
        );
    }

    private sealed record ResourceLock(string LockId, IDisposable LockReleaser, TimeSpan TimeUntilExpires) : IDisposable
    {
        public CancellationTokenSource ExpireSource { get; } =
            TimeUntilExpires == Timeout.InfiniteTimeSpan ? new() : new(TimeUntilExpires);

        public void Dispose()
        {
            LockReleaser.Dispose();
            ExpireSource.Dispose();
        }
    }

    #endregion
}
