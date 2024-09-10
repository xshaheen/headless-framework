using Framework.Kernel.BuildingBlocks.Abstractions;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

namespace Framework.DistributedLocks;

public sealed class DisposableDistributedLock(
    string resource,
    string lockId,
    TimeSpan timeWaitedForLock,
    IDistributedLockProvider lockProvider,
    IClock clock,
    ILogger logger
) : IDistributedLock
{
    private readonly AsyncLock _lock = new();
    private readonly long _timestamp = clock.GetTimestamp();
    private bool _isReleased;

    public string LockId { get; } = lockId;

    public string Resource { get; } = resource;

    public DateTimeOffset DateAcquired { get; } = clock.Now;

    public TimeSpan TimeWaitedForLock { get; } = timeWaitedForLock;

    public int RenewalCount { get; private set; }

    public async Task<bool> RenewAsync(TimeSpan? timout = null)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("Renewing lock {Resource} ({LockId})", Resource, LockId);
        }

        var result = await lockProvider.RenewAsync(Resource, LockId, timout).AnyContext();

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

    public async Task ReleaseAsync()
    {
        if (_isReleased)
        {
            return;
        }

        using (await _lock.LockAsync().AnyContext())
        {
            if (_isReleased)
            {
                return;
            }

            _isReleased = true;
            var elapsed = clock.GetTimestamp() - _timestamp;

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Releasing lock {Resource} ({LockId}) after {Duration:g}", Resource, LockId, elapsed);
            }

            try
            {
                await lockProvider.ReleaseAsync(Resource, LockId).AnyContext();
            }
            catch
            {
                _isReleased = false;

                throw;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        var isTraceLogLevelEnabled = logger.IsEnabled(LogLevel.Trace);

        if (isTraceLogLevelEnabled)
        {
            logger.LogTrace("Disposing lock {Resource} ({LockId})", Resource, LockId);
        }

        try
        {
            await ReleaseAsync().AnyContext();
        }
        catch (Exception e)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(e, "Unable to release lock {Resource} ({LockId})", Resource, LockId);
            }
        }

        if (isTraceLogLevelEnabled)
        {
            logger.LogTrace("Disposed lock {Resource} ({LockId})", Resource, LockId);
        }
    }
}
