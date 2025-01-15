using System.Diagnostics;
using Framework.Abstractions;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

namespace Tests.Lock;

internal class DisposableLock(
    string resource,
    string lockId,
    TimeSpan timeWaitedForLock,
    ILockProvider lockProvider,
    ILogger logger,
    bool shouldReleaseOnDispose
) : ILock
{
    private bool _isReleased;
    private readonly AsyncLock _lock = new();
    private readonly Stopwatch _duration = Stopwatch.StartNew();

    public string LockId { get; } = lockId;

    public string Resource { get; } = resource;

    public DateTimeOffset DateAcquired { get; } = lockProvider.GetTimeProvider().GetUtcNow();

    public TimeSpan TimeWaitedForLock { get; } = timeWaitedForLock;

    public int RenewalCount { get; private set; }

    public async ValueTask DisposeAsync()
    {
        if (!shouldReleaseOnDispose)
        {
            return;
        }

        var isTraceLogLevelEnabled = logger.IsEnabled(LogLevel.Trace);

        if (isTraceLogLevelEnabled)
        {
            logger.LogTrace("Disposing lock {Resource} ({LockId})", Resource, LockId);
        }

        try
        {
            await ReleaseAsync().AnyContext();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Unable to release lock {Resource} ({LockId})", Resource, LockId);
            }
        }

        if (isTraceLogLevelEnabled)
        {
            logger.LogTrace("Disposed lock {Resource} ({LockId})", Resource, LockId);
        }
    }

    public async Task RenewAsync(TimeSpan? timeUntilExpires = null)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("Renewing lock {Resource} ({LockId})", Resource, LockId);
        }

        await lockProvider.RenewAsync(Resource, LockId, timeUntilExpires).AnyContext();
        RenewalCount++;

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Renewed lock {Resource} ({LockId})", Resource, LockId);
        }
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
            _duration.Stop();

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "Releasing lock {Resource} ({LockId}) after {Duration:g}",
                    Resource,
                    LockId,
                    _duration.Elapsed
                );
            }

            await lockProvider.ReleaseAsync(Resource, LockId).AnyContext();
        }
    }
}
