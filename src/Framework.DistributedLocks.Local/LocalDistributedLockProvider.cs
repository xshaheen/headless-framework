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
        CancellationToken cancellationToken = default
    )
    {
        timeUntilExpires ??= TimeSpan.FromMinutes(15);

        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsPositiveOrZero(timeUntilExpires.Value);

        var key = resourceNormalizer.Normalize(resource);
        var lockId = longGenerator.Create().ToString(CultureInfo.InvariantCulture);

        var timestamp = clock.GetTimestamp();
        var lockReleaser = await _locks.LockAsync(key, millisecondsTimeout: 0, cancellationToken);
        var elapsed = TimeSpan.FromTicks(clock.GetTimestamp() - timestamp);

        // Not able to acquire the lock
        if (lockReleaser.EnteredSemaphore)
        {
            lockReleaser.Dispose();

            return null;
        }

        var resourceLock = new ResourceLock(lockId, lockReleaser, timeUntilExpires.Value);
        _resources.TryAdd(key, resourceLock);

        // Expire the lock when the expiration token source is triggered
        resourceLock.ExpireSource.Token.Register(() =>
        {
            if (!_resources.TryRemove(key, out var value))
            {
                return;
            }

            value.Dispose();
        });

        return new DisposableDistributedLock(resource, lockId, elapsed, this, clock, logger);
    }

    public Task<bool> IsLockedAsync(string resource)
    {
        return Task.FromResult(_locks.IsInUse(resource));
    }

    public Task RenewAsync(string resource, string lockId, TimeSpan? timeUntilExpires = null)
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);

        if (timeUntilExpires is not null)
        {
            Argument.IsPositive(timeUntilExpires.Value);
        }

        if (!_resources.TryGetValue(resource, out var value))
        {
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
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
