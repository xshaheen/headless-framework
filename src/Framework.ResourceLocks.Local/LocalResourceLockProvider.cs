// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using AsyncKeyedLock;
using Framework.BuildingBlocks.Abstractions;
using Framework.Checks;
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
        TimeSpan? acquireTimeout = null,
        CancellationToken acquireAbortToken = default
    )
    {
        // Normalize & Validate Arguments

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
        var existResourceLock = _GetResourceLock(normalizeResource);

        if (existResourceLock is not null)
        {
            return null;
        }

        var timestamp = timeProvider.GetTimestamp();
        IDisposable lockReleaser;

        if (acquireTimeout == Timeout.InfiniteTimeSpan)
        {
            lockReleaser = await _locks.LockAsync(normalizeResource, acquireAbortToken);
        }
        else
        {
            var timeoutRelease = await _locks.LockOrNullAsync(
                normalizeResource,
                acquireTimeout.Value,
                acquireAbortToken
            );

            if (timeoutRelease is null)
            {
                return null;
            }

            lockReleaser = timeoutRelease;
        }

        var elapsed = timeProvider.GetElapsedTime(timestamp);

        var resourceLock = new ResourceLock(
            LockId: longGenerator.Create().ToString(CultureInfo.InvariantCulture),
            lockReleaser,
            timeUntilExpires.Value
        );

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

        return new DisposableResourceLock(resource, resourceLock.LockId, elapsed, this, logger, timeProvider);
    }

    public Task<bool> RenewAsync(
        string resource,
        string lockId,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    )
    {
        // Normalize & Validate Arguments

        cancellationToken.ThrowIfCancellationRequested();
        timeUntilExpires ??= TimeSpan.FromMinutes(20);

        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);
        if (timeUntilExpires != Timeout.InfiniteTimeSpan)
        {
            Argument.IsPositive(timeUntilExpires.Value);
        }

        // Check if the lock is still valid

        var normalizeResource = _options.KeyPrefix + resource;
        var resourceLock = _GetResourceLock(normalizeResource);

        if (resourceLock is null)
        {
            return Task.FromResult(false);
        }

        // If the lock id does not match, then it is not the lock that we are looking for
        if (!string.Equals(resourceLock.LockId, lockId, StringComparison.Ordinal))
        {
            return Task.FromResult(false);
        }

        // Renew the lock

        resourceLock.Renew(timeUntilExpires.Value);

        return Task.FromResult(true);
    }

    public Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsNotNullOrWhiteSpace(resource);

        var normalizeResource = _options.KeyPrefix + resource;
        var resourceLock = _GetResourceLock(normalizeResource);

        return Task.FromResult(resourceLock is not null && _locks.IsInUse(normalizeResource));
    }

    public Task ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);

        var normalizeResource = _options.KeyPrefix + resource;
        var resourceLock = _GetResourceLock(normalizeResource);

        // If the lock is not found, then it is already released
        if (resourceLock is null)
        {
            return Task.CompletedTask;
        }

        // If the lock id does not match, then it is not the lock that we are looking for
        if (!string.Equals(resourceLock.LockId, lockId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        // Release the lock
        _RemoveResourceLock(normalizeResource);

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

    private ResourceLock? _GetResourceLock(string resource)
    {
        if (!_resources.TryGetValue(resource, out var existResourceLock))
        {
            return null;
        }

        if (existResourceLock.IsExpired)
        {
            _RemoveResourceLock(resource);

            return null;
        }

        return existResourceLock;
    }

    private void _RemoveResourceLock(string resource)
    {
        if (_resources.TryRemove(resource, out var resourceLock))
        {
            resourceLock.Dispose();
        }
    }

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

    private sealed record ResourceLock(string LockId, IDisposable LockReleaser, TimeSpan DateUntilExpires) : IDisposable
    {
        public long TimestampAcquired { get; private set; } = Stopwatch.GetTimestamp();

        public TimeSpan DateUntilExpires { get; private set; } = DateUntilExpires;

        public bool IsExpired => DateUntilExpires != Timeout.InfiniteTimeSpan && Elapsed >= DateUntilExpires;

        private TimeSpan Elapsed => Stopwatch.GetElapsedTime(TimestampAcquired);

        public void Renew(TimeSpan timeUntilExpires)
        {
            TimestampAcquired = Stopwatch.GetTimestamp();
            DateUntilExpires = timeUntilExpires;
        }

        public void Dispose()
        {
            LockReleaser.Dispose();
        }
    }

    #endregion
}
