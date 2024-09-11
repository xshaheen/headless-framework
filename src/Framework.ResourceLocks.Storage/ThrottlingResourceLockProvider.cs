using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Framework.ResourceLocks.Caching;

public class ThrottlingResourceLockProvider : IResourceLockProvider
{
    private readonly IResourceLockStorage _resourceLockStorage;
    private readonly TimeSpan _throttlingPeriod = TimeSpan.FromMinutes(15);
    private readonly int _maxHitsPerPeriod;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;

    public ThrottlingResourceLockProvider(
        IResourceLockStorage resourceLockStorage,
        int maxHitsPerPeriod = 100,
        TimeSpan? throttlingPeriod = null,
        TimeProvider? timeProvider = null,
        ILoggerFactory? loggerFactory = null
    )
    {
        _timeProvider = timeProvider ?? resourceLockStorage.GetTimeProvider();
        _logger =
            loggerFactory?.CreateLogger<ThrottlingResourceLockProvider>()
            ?? NullLogger<ThrottlingResourceLockProvider>.Instance;
        _resourceLockStorage = new ScopedCacheClient(resourceLockStorage, "lock:throttled");
        _maxHitsPerPeriod = maxHitsPerPeriod;

        if (maxHitsPerPeriod <= 0)
            throw new ArgumentException("Must be a positive number.", nameof(maxHitsPerPeriod));

        if (throttlingPeriod.HasValue)
            _throttlingPeriod = throttlingPeriod.Value;
    }

    public async Task<IResourceLock> TryAcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        bool releaseOnDispose = true,
        CancellationToken cancellationToken = default
    )
    {
        bool isTraceLogLevelEnabled = _logger.IsEnabled(LogLevel.Trace);
        if (isTraceLogLevelEnabled)
            _logger.LogTrace("AcquireLockAsync: {Resource}", resource);

        bool allowLock = false;
        byte errors = 0;

        string lockId = Guid.NewGuid().ToString("N");
        var sw = Stopwatch.StartNew();
        do
        {
            string cacheKey = _GetCacheKey(resource, _timeProvider.GetUtcNow().UtcDateTime);

            try
            {
                if (isTraceLogLevelEnabled)
                {
                    _logger.LogTrace(
                        "Current time: {CurrentTime} throttle: {ThrottlingPeriod} key: {Key}",
                        _timeProvider.GetUtcNow().ToString("mm:ss.fff"),
                        _timeProvider.GetUtcNow().UtcDateTime.Floor(_throttlingPeriod).ToString("mm:ss.fff"),
                        cacheKey
                    );
                }

                var hitCount = await _resourceLockStorage.GetAsync<long?>(cacheKey, 0).AnyContext();

                if (isTraceLogLevelEnabled)
                {
                    _logger.LogTrace(
                        "Current hit count: {HitCount} max: {MaxHitsPerPeriod}",
                        hitCount,
                        _maxHitsPerPeriod
                    );
                }

                if (hitCount <= _maxHitsPerPeriod - 1)
                {
                    hitCount = await _resourceLockStorage
                        .IncrementAsync(cacheKey, 1, _timeProvider.GetUtcNow().UtcDateTime.Ceiling(_throttlingPeriod))
                        .AnyContext();

                    // make sure someone didn't beat us to it.
                    if (hitCount <= _maxHitsPerPeriod)
                    {
                        allowLock = true;
                        break;
                    }

                    if (isTraceLogLevelEnabled)
                        _logger.LogTrace("Max hits exceeded after increment for {Resource}.", resource);
                }
                else if (isTraceLogLevelEnabled)
                {
                    _logger.LogTrace("Max hits exceeded for {Resource}.", resource);
                }

                if (cancellationToken.IsCancellationRequested)
                    break;

                var sleepUntil = _timeProvider.GetUtcNow().UtcDateTime.Ceiling(_throttlingPeriod).AddMilliseconds(1);
                if (sleepUntil > _timeProvider.GetUtcNow())
                {
                    if (isTraceLogLevelEnabled)
                    {
                        _logger.LogTrace(
                            "Sleeping until key expires: {SleepUntil}",
                            sleepUntil - _timeProvider.GetUtcNow()
                        );
                    }

                    await _timeProvider
                        .SafeDelay(sleepUntil - _timeProvider.GetUtcNow(), cancellationToken)
                        .AnyContext();
                }
                else
                {
                    if (isTraceLogLevelEnabled)
                        _logger.LogTrace("Default sleep");
                    await _timeProvider.SafeDelay(TimeSpan.FromMilliseconds(50), cancellationToken).AnyContext();
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error acquiring throttled lock: name={Resource} message={Message}",
                    resource,
                    ex.Message
                );
                errors++;
                if (errors >= 3)
                    break;

                await _timeProvider.SafeDelay(TimeSpan.FromMilliseconds(50), cancellationToken).AnyContext();
            }
        } while (!cancellationToken.IsCancellationRequested);

        if (cancellationToken.IsCancellationRequested && isTraceLogLevelEnabled)
            _logger.LogTrace("Cancellation requested");

        if (!allowLock)
            return null;

        if (isTraceLogLevelEnabled)
            _logger.LogTrace("Allowing lock: {Resource}", resource);

        sw.Stop();
        return new DisposableLock(resource, lockId, sw.Elapsed, this, _logger, releaseOnDispose);
    }

    public async Task<bool> IsLockedAsync(string resource)
    {
        var cacheKey = _GetCacheKey(resource, _timeProvider.GetUtcNow().UtcDateTime);
        var hitCount = await _resourceLockStorage.GetAsync<long>(cacheKey, 0).AnyContext();

        return hitCount >= _maxHitsPerPeriod;
    }

    public Task<bool> RenewAsync(string resource, string lockId, TimeSpan? timeUntilExpires = null)
    {
        return Task.FromResult(true);
    }

    public Task ReleaseAsync(string resource, string lockId)
    {
        return Task.CompletedTask;
    }

    private string _GetCacheKey(string resource, DateTime now)
    {
        return string.Concat(resource, ":", now.Floor(_throttlingPeriod).Ticks);
    }
}
