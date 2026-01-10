// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.ResourceLocks;

public sealed class DisposableResourceLock(
    string resource,
    string lockId,
    TimeSpan timeWaitedForLock,
    IResourceLockProvider lockProvider,
    TimeProvider timeProvider,
    ILogger logger
) : IResourceLock
{
    private bool _isReleased;
    private readonly AsyncLock _lock = new();
    private readonly long _timestamp = timeProvider.GetTimestamp();

    public string LockId { get; } = lockId;

    public string Resource { get; } = resource;

    public DateTimeOffset DateAcquired { get; } = timeProvider.GetUtcNow();

    public TimeSpan TimeWaitedForLock { get; } = timeWaitedForLock;

    public int RenewalCount { get; private set; }

    public async Task<bool> RenewAsync(TimeSpan? timeUntilExpires = null, CancellationToken cancellationToken = default)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("Renewing lock {Resource} ({LockId})", Resource, LockId);
        }

        var result = await lockProvider.RenewAsync(Resource, LockId, timeUntilExpires, cancellationToken).AnyContext();

        if (!result)
        {
            logger.LogDebug("Unable to renew lock {Resource} ({LockId})", Resource, LockId);

            return false;
        }

        RenewalCount++;

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Renewed lock {Resource} ({LockId})", Resource, LockId);
        }

        return true;
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        if (_isReleased)
        {
            return;
        }

        using (await _lock.LockAsync(cancellationToken).AnyContext())
        {
            if (_isReleased)
            {
                return;
            }

            _isReleased = true;

            if (logger.IsEnabled(LogLevel.Debug))
            {
                var elapsed = timeProvider.GetElapsedTime(_timestamp);

                logger.LogDebug(
                    "Releasing lock: R={Resource} Id={LockId} after {Duration:g}",
                    Resource,
                    LockId,
                    elapsed
                );
            }

            await lockProvider.ReleaseAsync(Resource, LockId, cancellationToken).AnyContext();
        }
    }

    public async ValueTask DisposeAsync()
    {
        var isTraceLogLevelEnabled = logger.IsEnabled(LogLevel.Trace);

        if (isTraceLogLevelEnabled)
        {
            logger.LogTrace("Disposing lock: R={Resource} Id={LockId}", Resource, LockId);
        }

        try
        {
            await ReleaseAsync().AnyContext();
        }
        catch (Exception e)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(e, "Unable to release lock: R={Resource} Id={LockId}", Resource, LockId);
            }
        }

        if (isTraceLogLevelEnabled)
        {
            logger.LogTrace("Disposed lock: R={Resource} Id={LockId}", Resource, LockId);
        }
    }
}
