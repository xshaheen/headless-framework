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
    private readonly AsyncKeyedLocker<string> _locks = _CreateAsyncKeyedLocker();
    private readonly CacheDictionary<string, ResourceLock> _resources = new(100);
    private readonly ResourceLockOptions _options = optionsAccessor.Value;

    public TimeSpan DefaultTimeUntilExpires => 20.Minutes();

    public TimeSpan DefaultAcquireTimeout => 30.Seconds();

    public async Task<IResourceLock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null,
        CancellationToken acquireAbortToken = default
    )
    {
        // Normalize & Validate Arguments
        Argument.IsNotNullOrWhiteSpace(resource);

        timeUntilExpires = _NormalizeTimeUntilExpires(timeUntilExpires);
        acquireTimeout = _NormalizeAcquireTimeout(acquireTimeout);
        var normalizeResource = _options.KeyPrefix + resource;
        var timestamp = timeProvider.GetTimestamp();

        var lockReleaser = await _LockAsync(normalizeResource, acquireTimeout.Value, acquireAbortToken);

        if (lockReleaser == null)
        {
            return null;
        }

        var elapsed = timeProvider.GetElapsedTime(timestamp);
        var lockId = longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);

#pragma warning disable CA2000 // Dispose objects before losing scope
        var resourceLock = new ResourceLock(lockId, lockReleaser, timeUntilExpires.Value);
        // Safe because the resource lock is acquired only by one thread
        _resources.TryAdd(normalizeResource, resourceLock, timeUntilExpires.Value);
#pragma warning restore CA2000

        return new DisposableResourceLock(resource, lockId, elapsed, this, logger, timeProvider);
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
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);

        if (timeUntilExpires != Timeout.InfiniteTimeSpan)
        {
            Argument.IsPositive(timeUntilExpires);
        }

        timeUntilExpires ??= DefaultTimeUntilExpires;

        // Check if the lock is still valid
        var normalizeResource = _options.KeyPrefix + resource;
        var isLocked = _IsLocked(normalizeResource, lockId);

        return Task.FromResult(isLocked && _Renew(normalizeResource, timeUntilExpires.Value));
    }

    public Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        cancellationToken.ThrowIfCancellationRequested();
        resource = _options.KeyPrefix + resource;
        var isLocked = _IsLocked(resource);

        return Task.FromResult(isLocked);
    }

    public Task ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);

        resource = _options.KeyPrefix + resource;
        var isLocked = _IsLocked(resource);

        if (!isLocked)
        {
            return Task.CompletedTask;
        }

        // Remove & Dispose the resource lock
        if (_resources.TryRemove(resource, out var resourceLock))
        {
            resourceLock.Dispose();
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var value in _resources)
        {
            value.Value.Dispose();
        }

        _resources.Clear();
        _resources.Dispose();
        _locks.Dispose();
    }

    #region Helpers

    private async Task<IDisposable?> _LockAsync(
        string resource,
        TimeSpan acquireTimeout,
        CancellationToken acquireAbortToken
    )
    {
        return acquireTimeout == Timeout.InfiniteTimeSpan
            ? await _locks.LockAsync(resource, acquireAbortToken)
            : await _locks.LockOrNullAsync(resource, acquireTimeout, acquireAbortToken);
    }

    private bool _IsLocked(string resource, string lockId)
    {
        return _resources.TryGet(resource, out var resourceLock)
            && string.Equals(resourceLock.LockId, lockId, StringComparison.Ordinal);
    }

    private bool _IsLocked(string resource)
    {
        return _resources.TryGet(resource, out var resourceLock) && resourceLock is not null;
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

    private bool _Renew(string resource, TimeSpan timeUntilExpires)
    {
        if (!_resources.TryGet(resource, out var resourceLock))
        {
            return false;
        }

        resourceLock.Renew(timeUntilExpires);

        return true;
    }

    private sealed class ResourceLock : IDisposable
    {
        private readonly Timer _timer;
        private readonly IDisposable _lockReleaser;

        public ResourceLock(string lockId, IDisposable lockReleaser, TimeSpan timeUntilExpires)
        {
            LockId = lockId;
            _lockReleaser = lockReleaser;

            _timer = new Timer(
                _ => Dispose(),
                state: null,
                dueTime: timeUntilExpires,
                period: Timeout.InfiniteTimeSpan // Pass infinite to disable periodic signaling
            );
        }

        public string LockId { get; }

        public void Renew(TimeSpan timeUntilExpires)
        {
            _timer.Change(timeUntilExpires, Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            _lockReleaser.Dispose();
            _timer.Dispose();
        }
    }

    #endregion
}
