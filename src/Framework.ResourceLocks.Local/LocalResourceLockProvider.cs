// Copyright (c) Mahmoud Shaheen. All rights reserved.

using AsyncKeyedLock;
using Framework.Abstractions;
using Framework.Checks;
using Framework.Core;
using Humanizer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.ResourceLocks.Local;

[PublicAPI]
public sealed class LocalResourceLockProvider(
    ILongIdGenerator longIdGenerator,
    TimeProvider timeProvider,
    ILogger<LocalResourceLockProvider> logger,
    IOptions<ResourceLockOptions> optionsAccessor
) : IResourceLockProvider, IDisposable
{
    private readonly ResourceLockOptions _options = optionsAccessor.Value;
    private readonly AsyncKeyedLocker<string> _locks = _CreateAsyncKeyedLocker();

    private readonly CacheDictionary<string, ResourceLock> _resources = new(
        10_000,
        (_, value) => value.LockReleaser.Dispose()
    );

    public TimeSpan DefaultTimeUntilExpires => 20.Minutes();

    public TimeSpan DefaultAcquireTimeout => 30.Seconds();

    public async Task<IResourceLock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        // Normalize & Validate Arguments
        Argument.IsNotNullOrWhiteSpace(resource);

        timeUntilExpires = _NormalizeTimeUntilExpires(timeUntilExpires);
        acquireTimeout = _NormalizeAcquireTimeout(acquireTimeout);
        var normalizeResource = _options.KeyPrefix + resource;
        var timestamp = timeProvider.GetTimestamp();

        _resources.EvictExpired();

        var lockReleaser =
            acquireTimeout.Value == Timeout.InfiniteTimeSpan
                ? await _locks.LockAsync(normalizeResource, cancellationToken)
                : await _locks.LockOrNullAsync(normalizeResource, acquireTimeout.Value, cancellationToken);

        if (lockReleaser == null)
        {
            return null;
        }

        var elapsed = timeProvider.GetElapsedTime(timestamp);
        var lockId = longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);

        var resourceLock = new ResourceLock(lockId, lockReleaser);
        // Safe because the resource lock is acquired only by one thread
        _resources.TryAdd(normalizeResource, resourceLock, timeUntilExpires.Value);

        return new DisposableResourceLock(resource, lockId, elapsed, this, timeProvider, logger);
    }

    public Task ReleaseAsync(string resource, string lockId)
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);

        resource = _options.KeyPrefix + resource;
        var isLocked = _resources.TryGet(resource, out _);

        if (!isLocked)
        {
            return Task.CompletedTask;
        }

        // Remove & Dispose the resource lock
        if (_resources.TryRemove(resource, out var resourceLock))
        {
            resourceLock.LockReleaser.Dispose();
        }

        return Task.CompletedTask;
    }

    public Task<bool> RenewAsync(string resource, string lockId, TimeSpan? timeUntilExpires = null)
    {
        // Normalize & Validate Arguments
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);
        timeUntilExpires = _NormalizeTimeUntilExpires(timeUntilExpires);
        resource = _options.KeyPrefix + resource;

        // Check if the lock is still valid

        if (
            !_resources.TryGet(resource, out var resourceLock)
            || !string.Equals(resourceLock.LockId, lockId, StringComparison.Ordinal)
        )
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_resources.Extend(resource, timeUntilExpires.Value));
    }

    public Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        cancellationToken.ThrowIfCancellationRequested();
        resource = _options.KeyPrefix + resource;
        var isLocked = _resources.TryGet(resource, out _);

        return Task.FromResult(isLocked);
    }

    public void Dispose()
    {
        _locks.Dispose();
        _resources.Dispose();
        _resources.Clear();
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

    [return: System.Diagnostics.CodeAnalysis.NotNull]
    private TimeSpan? _NormalizeAcquireTimeout(TimeSpan? acquireTimeout)
    {
        if (acquireTimeout != Timeout.InfiniteTimeSpan)
        {
            Argument.IsPositive(acquireTimeout);
        }

        acquireTimeout ??= DefaultAcquireTimeout;

        return acquireTimeout;
    }

    [return: System.Diagnostics.CodeAnalysis.NotNull]
    private TimeSpan? _NormalizeTimeUntilExpires(TimeSpan? timeUntilExpires)
    {
        if (timeUntilExpires != Timeout.InfiniteTimeSpan)
        {
            Argument.IsPositive(timeUntilExpires);
        }

        timeUntilExpires ??= DefaultTimeUntilExpires;

        return timeUntilExpires;
    }

    private sealed record ResourceLock(string LockId, IDisposable LockReleaser);

    #endregion
}
