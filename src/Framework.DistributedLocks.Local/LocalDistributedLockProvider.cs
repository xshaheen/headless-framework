using System.Collections.Concurrent;
using AsyncKeyedLock;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Checks;
using Microsoft.Extensions.Logging;

namespace Framework.DistributedLocks.Local;

[PublicAPI]
public sealed class LocalDistributedLockProvider(
    IDistributedLockResourceNormalizer resourceNormalizer,
    IUniqueLongGenerator longGenerator,
    IClock clock,
    ILogger<LocalDistributedLockProvider> logger
) : IDistributedLockProvider, IDisposable
{
    private readonly AsyncKeyedLocker<string> _locks = _CreateAsyncKeyedLocker();
    private readonly ConcurrentDictionary<string, ResourceLock> _resources = new(StringComparer.Ordinal);

    public async Task<IDistributedLock?> TryAcquireAsync(
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

        var key = resourceNormalizer.Normalize(resource);

        var timestamp = clock.GetTimestamp();
        IDisposable lockReleaser;

        if (acquireTimeout == Timeout.InfiniteTimeSpan)
        {
            lockReleaser = await _locks.LockAsync(key);
        }
        else
        {
            var timeoutRelease = await _locks.LockAsync(key, acquireTimeout.Value);

            if (timeoutRelease.EnteredSemaphore)
            {
                timeoutRelease.Dispose();

                return null;
            }

            lockReleaser = timeoutRelease;
        }

        var elapsed = TimeSpan.FromTicks(clock.GetTimestamp() - timestamp);

        var lockId = longGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var resourceLock = new ResourceLock(lockId, lockReleaser, timeUntilExpires.Value);
        // Safe because the resource lock is acquired only by one thread
        var added = _resources.TryAdd(key, resourceLock);

        if (!added)
        {
            logger.LogWarning("(Shouldn't happen) The resource lock for the key '{Key}' was not added", key);
            resourceLock.Dispose();

            return null;
        }

        // Expire the lock when the expiration token source is triggered
        resourceLock.ExpireSource.Token.Register(() =>
        {
            // TODO: this expiration can be triggered parallel with the renew of the same lock id
            // which can make the lock to be released even if it is renewed

            if (_resources.TryRemove(key, out var value))
            {
                value.Dispose();
            }
        });

        return new DisposableDistributedLock(resource, lockId, elapsed, this, clock, logger);
    }

    public Task<bool> IsLockedAsync(string resource)
    {
        return Task.FromResult(_locks.IsInUse(resource));
    }

    public Task<bool> RenewAsync(string resource, string lockId, TimeSpan? timeUntilExpires = null)
    {
        timeUntilExpires ??= TimeSpan.FromMinutes(20);

        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);
        Argument.IsPositiveOrZero(timeUntilExpires.Value);

        // If the lock is not found, then it is already released
        if (!_resources.TryGetValue(resource, out var value))
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

        // If the lock is not found, then it is already released
        if (!_resources.TryGetValue(resource, out var value))
        {
            return Task.CompletedTask;
        }

        // If the lock id does not match, then it is not the lock that we are looking for
        if (!string.Equals(value.LockId, lockId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        // Release the lock
        if (_resources.TryRemove(resource, out value))
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
        public CancellationTokenSource ExpireSource { get; } = new(TimeUntilExpires);

        public void Dispose()
        {
            LockReleaser.Dispose();
            ExpireSource.Dispose();
        }
    }

    #endregion
}
