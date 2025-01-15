using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Foundatio.Messaging;
using Framework.Abstractions;
using Framework.Checks;
using Framework.Constants;
using Framework.Core;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

namespace Tests.Lock;

public sealed class CacheLockProvider(
    ILongIdGenerator longIdGenerator,
    ICacheClient cacheClient,
    IMessageBus messageBus,
    TimeProvider? timeProvider,
    ILoggerFactory? loggerFactory = null
) : ILockProvider, IHaveLogger, IHaveTimeProvider
{
    private readonly ICacheClient _cacheClient = new ScopedCacheClient(cacheClient, "lock");
    private readonly TimeProvider _timeProvider = timeProvider ?? cacheClient.GetTimeProvider();

    private readonly ConcurrentDictionary<string, ResetEventWithRefCount> _autoResetEvents = new(
        StringComparer.Ordinal
    );

    private readonly AsyncLock _lock = new();
    private bool _isSubscribed;

    private readonly ILogger _logger = loggerFactory?.CreateLogger<CacheLockProvider>() ?? cacheClient.GetLogger();

    private readonly Histogram<double> _lockWaitTimeHistogram = FrameworkDiagnostics.Meter.CreateHistogram<double>(
        "framework.lock.wait.time",
        unit: "ms",
        description: "Time waiting for locks"
    );

    private readonly Counter<int> _lockTimeoutCounter = FrameworkDiagnostics.Meter.CreateCounter<int>(
        "framework.lock.failed",
        description: "Number of failed attempts to acquire a lock"
    );

    ILogger IHaveLogger.Logger => _logger;

    TimeProvider IHaveTimeProvider.TimeProvider => _timeProvider;

    private async Task _EnsureTopicSubscriptionAsync()
    {
        if (_isSubscribed)
        {
            return;
        }

        using (await _lock.LockAsync().AnyContext())
        {
            if (_isSubscribed)
            {
                return;
            }

            var isTraceLogLevelEnabled = _logger.IsEnabled(LogLevel.Trace);

            if (isTraceLogLevelEnabled)
            {
                _logger.LogTrace("Subscribing to cache lock released");
            }

            await messageBus.SubscribeAsync<CacheLockReleased>(OnLockReleasedAsync).AnyContext();
            _isSubscribed = true;

            if (isTraceLogLevelEnabled)
            {
                _logger.LogTrace("Subscribed to cache lock released");
            }
        }
    }

    private Task OnLockReleasedAsync(CacheLockReleased msg, CancellationToken cancellationToken = default)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Got lock released message: {Resource} ({LockId})", msg.Resource, msg.LockId);
        }

        if (_autoResetEvents.TryGetValue(msg.Resource, out var autoResetEvent))
        {
            autoResetEvent.Target.Set();
        }

        return Task.CompletedTask;
    }

    private static Activity? _StartLockActivity(string resource)
    {
        var activity = FrameworkDiagnostics.ActivitySource.StartActivity(nameof(TryAcquireAsync));

        if (activity is null)
        {
            return null;
        }

        activity.AddTag("resource", resource);
        activity.DisplayName = $"Lock: {resource}";

        return activity;
    }

    public async Task<ILock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        bool releaseOnDispose = true,
        CancellationToken acquireAbortToken = default
    )
    {
        var isTraceLogLevelEnabled = _logger.IsEnabled(LogLevel.Trace);
        var isDebugLogLevelEnabled = _logger.IsEnabled(LogLevel.Debug);
        var shouldWait = !acquireAbortToken.IsCancellationRequested;
        var lockId = longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        timeUntilExpires ??= TimeSpan.FromMinutes(20);

        if (isDebugLogLevelEnabled)
        {
            _logger.LogDebug("Attempting to acquire lock {Resource} ({LockId})", resource, lockId);
        }

        using var activity = _StartLockActivity(resource);

        var gotLock = false;
        var sw = Stopwatch.StartNew();

        try
        {
            do
            {
                try
                {
                    if (timeUntilExpires.Value == TimeSpan.Zero) // no lock timeout
                    {
                        gotLock = await _cacheClient.AddAsync(resource, lockId).AnyContext();
                    }
                    else
                    {
                        gotLock = await _cacheClient.AddAsync(resource, lockId, timeUntilExpires).AnyContext();
                    }
                }
                catch (Exception ex)
                {
                    if (isTraceLogLevelEnabled)
                    {
                        _logger.LogTrace(ex, "Error acquiring lock {Resource} ({LockId})", resource, lockId);
                    }
                }

                if (gotLock)
                {
                    break;
                }

                if (isDebugLogLevelEnabled)
                {
                    _logger.LogDebug("Failed to acquire lock {Resource} ({LockId})", resource, lockId);
                }

                if (acquireAbortToken.IsCancellationRequested)
                {
                    if (isTraceLogLevelEnabled && shouldWait)
                    {
                        _logger.LogTrace(
                            "Cancellation requested while acquiring lock {Resource} ({LockId})",
                            resource,
                            lockId
                        );
                    }

                    break;
                }

                var autoResetEvent = _autoResetEvents.AddOrUpdate(
                    resource,
                    new ResetEventWithRefCount { RefCount = 1, Target = new AsyncAutoResetEvent() },
                    (n, e) =>
                    {
                        e.RefCount++;

                        return e;
                    }
                );

                if (!_isSubscribed)
                {
                    await _EnsureTopicSubscriptionAsync().AnyContext();
                }

                var keyExpiration = _timeProvider
                    .GetUtcNow()
                    .UtcDateTime.SafeAdd(await _cacheClient.GetExpirationAsync(resource).AnyContext() ?? TimeSpan.Zero);

                var delayAmount = keyExpiration.Subtract(_timeProvider.GetUtcNow().UtcDateTime);

                // delay a minimum of 50ms and a maximum of 3 seconds
                if (delayAmount < TimeSpan.FromMilliseconds(50))
                {
                    delayAmount = TimeSpan.FromMilliseconds(50);
                }
                else if (delayAmount > TimeSpan.FromSeconds(3))
                {
                    delayAmount = TimeSpan.FromSeconds(3);
                }

                if (isTraceLogLevelEnabled)
                {
                    _logger.LogTrace(
                        "Will wait {Delay:g} before retrying to acquire lock {Resource} ({LockId})",
                        delayAmount,
                        resource,
                        lockId
                    );
                }

                // wait until we get a message saying the lock was released or 3 seconds has elapsed or cancellation has been requested
                using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    acquireAbortToken
                );

                linkedCancellationTokenSource.CancelAfter(delayAmount);

                try
                {
                    await autoResetEvent.Target.WaitAsync(linkedCancellationTokenSource.Token).AnyContext();
                }
                catch (OperationCanceledException) { }

                Thread.Yield();
            } while (!acquireAbortToken.IsCancellationRequested);
        }
        finally
        {
            var shouldRemove = false;

            _autoResetEvents.TryUpdate(
                resource,
                (n, e) =>
                {
                    e.RefCount--;

                    if (e.RefCount == 0)
                    {
                        shouldRemove = true;
                    }

                    return e;
                }
            );

            if (shouldRemove)
            {
                _autoResetEvents.TryRemove(resource, out _);
            }
        }

        sw.Stop();

        _lockWaitTimeHistogram.Record(sw.Elapsed.TotalMilliseconds);

        if (!gotLock)
        {
            _lockTimeoutCounter.Add(1);

            if (acquireAbortToken.IsCancellationRequested && isTraceLogLevelEnabled)
            {
                _logger.LogTrace(
                    "Cancellation requested for lock {Resource} ({LockId}) after {Duration:g}",
                    resource,
                    lockId,
                    sw.Elapsed
                );
            }
            else if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    "Failed to acquire lock {Resource} ({LockId}) after {Duration:g}",
                    resource,
                    lockId,
                    sw.Elapsed
                );
            }

            return null;
        }

        if (sw.Elapsed > TimeSpan.FromSeconds(5) && _logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning("Acquired lock {Resource} ({LockId}) after {Duration:g}", resource, lockId, sw.Elapsed);
        }
        else if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Acquired lock {Resource} ({LockId}) after {Duration:g}", resource, lockId, sw.Elapsed);
        }

        return new DisposableLock(resource, lockId, sw.Elapsed, this, _logger, releaseOnDispose);
    }

    public async Task<bool> IsLockedAsync(string resource)
    {
        return await Run.WithRetriesAsync(() => _cacheClient.ExistsAsync(resource)).AnyContext();
    }

    public async Task ReleaseAsync(string resource, string lockId)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("ReleaseAsync Start: {Resource} ({LockId})", resource, lockId);
        }

        await Run.WithRetriesAsync(() => _cacheClient.RemoveIfEqualAsync(resource, lockId), 15).AnyContext();

        await messageBus.PublishAsync(new CacheLockReleased { Resource = resource, LockId = lockId }).AnyContext();

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Released lock: {Resource} ({LockId})", resource, lockId);
        }
    }

    public Task RenewAsync(string resource, string lockId, TimeSpan? timeUntilExpires = null)
    {
        timeUntilExpires ??= TimeSpan.FromMinutes(20);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Renewing lock {Resource} ({LockId}) for {Duration:g}",
                resource,
                lockId,
                timeUntilExpires
            );
        }

        return Run.WithRetriesAsync(
            () => _cacheClient.ReplaceIfEqualAsync(resource, lockId, lockId, timeUntilExpires.Value)
        );
    }

    private sealed class ResetEventWithRefCount
    {
        public required int RefCount { get; set; }

        public required AsyncAutoResetEvent Target { get; set; }
    }
}

public class CacheLockReleased
{
    public required string Resource { get; set; }

    public required string LockId { get; set; }
}

internal static class ConcurrentDictionaryExtensions
{
    public static bool TryUpdate<TKey, TValue>(
        this ConcurrentDictionary<TKey, TValue> concurrentDictionary,
        TKey key,
        Func<TKey, TValue, TValue> updateValueFactory
    )
        where TKey : notnull
    {
        Argument.IsNotNull(key);
        Argument.IsNotNull(updateValueFactory);

        TValue? comparisonValue;
        TValue newValue;

        do
        {
            if (!concurrentDictionary.TryGetValue(key, out comparisonValue))
            {
                return false;
            }

            newValue = updateValueFactory(key, comparisonValue); // TODO: can has side effects
        } while (!concurrentDictionary.TryUpdate(key, newValue, comparisonValue));

        return true;
    }
}
