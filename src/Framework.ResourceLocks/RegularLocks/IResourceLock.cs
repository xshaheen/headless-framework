// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.ResourceLocks;

/// <summary>
/// A mutex synchronization primitive which can be used to coordinate access to a resource or critical region of code
/// across processes or systems. The scope and capabilities of the lock are dependent on the particular implementation
/// </summary>
[PublicAPI]
public interface IResourceLock : IAsyncDisposable
{
    /// <summary>A unique identifier for the lock instance.</summary>
    string LockId { get; }

    /// <summary>A name that uniquely identifies the lock.</summary>
    string Resource { get; }

    /// <summary>The number of times the lock has been renewed.</summary>
    int RenewalCount { get; }

    /// <summary>The time the lock was acquired.</summary>
    DateTimeOffset DateAcquired { get; }

    /// <summary>The amount of time waited to acquire the lock.</summary>
    TimeSpan TimeWaitedForLock { get; }

    /// <summary>Releases the lock.</summary>
    Task ReleaseAsync();

    /// <summary>Attempts to renew the lock.</summary>
    Task<bool> RenewAsync(TimeSpan? timout = null);
}

public sealed class DisposableResourceLock(
    string resource,
    string lockId,
    TimeSpan timeWaitedForLock,
    IResourceLockProvider lockProvider,
    ILogger logger,
    TimeProvider timeProvider
) : IResourceLock
{
    private readonly AsyncLock _lock = new();
    private readonly long _timestamp = timeProvider.GetTimestamp();
    private bool _isReleased;

    public string LockId { get; } = lockId;

    public string Resource { get; } = resource;

    public DateTimeOffset DateAcquired { get; } = timeProvider.GetUtcNow();

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
            var elapsed = timeProvider.GetElapsedTime(_timestamp);

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
